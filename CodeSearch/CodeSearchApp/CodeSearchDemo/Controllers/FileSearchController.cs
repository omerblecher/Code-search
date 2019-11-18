using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.DirectoryServices.ActiveDirectory;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Configuration;
using System.Web.Mvc;
using CodeSearchDemo.Models;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers;
using Lucene.Net.Search;
using Lucene.Net.Search.Spans;
using Lucene.Net.Store;
using Microsoft.Ajax.Utilities;
using PagedList;
using Directory = Lucene.Net.Store.Directory;
using Version = Lucene.Net.Util.Version;
using Microsoft.AspNet.SignalR;
using Timer = System.Timers.Timer;

namespace CodeSearchDemo.Controllers
{
    public class FileSearchController : AsyncController 
    {
        private const int MAX_RESULTS_PER_PAGE = 20;

        private const string LAST_UPDATE_FILE = "UpdateTime.txt";

        private const int CONFIGURATION_TECHNOLOGY = 5;
        private const int CONFIGURATION_LAYER = 4;
        private const int CONFIGURATION_DOMAIN = 3;
        private const int CONFIGURATION_PROJECT_NAME = 2;

        
        /// <summary>
        /// List of all branches
        /// </summary>
        private  List<string> _branchesList;


        /// <summary>
        /// Get the last update time of index path
        /// </summary>
        /// <param name="indexPath">Index directory</param>
        /// <returns>The last update time</returns>
        private static DateTime GetLastUpdateTime(string indexPath)
        {
            DateTime dt = new DateTime(1900, 1, 1, 0, 0, 0);
            string UpdateFilePath = Path.Combine(indexPath, LAST_UPDATE_FILE);

            try
            {
                if (System.IO.File.Exists(UpdateFilePath))
                {
                    dt = DateTime.Parse(System.IO.File.ReadAllText(UpdateFilePath));
                }
            }
            catch (Exception)
            {

                dt = new DateTime(1900, 1, 1, 0, 0, 0);
            }

            return dt;
        }

        /// <summary>
        /// Get the current working directory of all indexes
        /// </summary>
        /// <returns>The most up-to-date working directory</returns>
        public static string GetIndexPath()
        {
            string firstIndexPath = WebConfigurationManager.AppSettings["FirstRootPath"];
            string secondIndexPath = WebConfigurationManager.AppSettings["SecondRootPath"];

            return GetLastUpdateTime(secondIndexPath) > GetLastUpdateTime(firstIndexPath)
                ? secondIndexPath
                : firstIndexPath;
        }


        /// <summary>
        /// Get all branches
        /// </summary>
        private void GetBranches()
        {

            if (Session["branches"] == null)
            {
                string path = GetIndexPath();
                _branchesList = new List<string>();
                _branchesList.Add("Configuration");
                _branchesList.Add("Trunk");
                foreach (string dirName in System.IO.Directory.GetDirectories(path).Select(s => s.Remove(0, path.Length + 1)).Where(dirName => !dirName.Equals("Configuration") && !dirName.Equals("Trunk")))
                {
                    _branchesList.Add(dirName);
                }


                Session["branches"] = _branchesList;


            }
            else
            {
                _branchesList = Session["branches"] as List<string>;
            }


        }

        //
        // GET: /FileSearch/

        public ActionResult Index()
        {
                     
            ViewBag.BranchesDropDownList = new SelectList(new List<string>());   
            return View("FirstPageView");
            

        }

        [HttpGet]
        public void LoadBranchesAsync()
        {
            AsyncManager.OutstandingOperations.Increment();
            Task.Run(() =>
            {
                GetBranches();
                AsyncManager.OutstandingOperations.Decrement();
            });

        }

        public ActionResult LoadBranchesCompleted()
        {
            List<FileSearchModel> results = new List<FileSearchModel>();
            ViewBag.BranchesDropDownList = new SelectList(_branchesList);


            return View("FileSearchView", results.ToPagedList(1, MAX_RESULTS_PER_PAGE));
        }



        [HttpGet]
        public void SearchInFilesAsync(string token, string branch, string layer, string connectionID, int? page)
        {
            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Reset();
            stopWatch.Start();
            if (!string.IsNullOrEmpty(token))
            {
                ProgressHub.SendMessage(
                            string.Format("Start collecting results for token \"{0}\" of branch {1}", token, branch), 0,
                            connectionID);
            }

            AsyncManager.OutstandingOperations.Increment();

            Task.Run(() =>
            {
                
                
                GetBranches();
                ViewBag.BranchesDropDownList = new SelectList(_branchesList);

                List<FileSearchModel> lastSearchResults = null;

                if (string.IsNullOrEmpty(token))
                {
                    ModelState.AddModelError("Error", "Search text cannot be empty!");
                    AsyncManager.Parameters["lastSearchResults"] = new List<FileSearchModel>();
                    AsyncManager.Parameters["currentIndex"] = 1;
                    AsyncManager.OutstandingOperations.Decrement();
                }
                else
                {
                    ViewBag.Currenttoken = token;
                    ViewBag.Currentbranch = branch;
                    ViewBag.Currentlayer = layer;
                    ViewBag.CurrentconnectionID = connectionID;



                    lastSearchResults = Session["LastSearchResults"] as List<FileSearchModel>;
                    string searchMessage = Session["searchMessage"] as string;


                    int currentIndex = page ?? 1;

                    if (!page.HasValue || lastSearchResults == null)
                    {

                        ConcurrentDictionary<string, FileSearchModel> searchDictionaryConcurrent =
                            new ConcurrentDictionary<string, FileSearchModel>();

                        string indexPath = GetIndexPath();
                        Directory luceneIndexDirectory = FSDirectory.Open(Path.Combine(indexPath, branch));




                        IndexSearcher searcher = new IndexSearcher(luceneIndexDirectory);
                        BooleanQuery bq = new BooleanQuery();


                        foreach (string word in token.Split(' '))
                        {
                            if (word.Contains("*"))
                            {
                                Query textQuery = new PrefixQuery(new Term("LineText", word));
                                bq.Add(textQuery, Occur.MUST);
                            }
                            else
                            {
                                string wildWord = "*" + word + "*";
                                Query textQuery = new WildcardQuery(new Term("LineText", wildWord));
                                bq.Add(textQuery, Occur.MUST);
                            }



                        }



                        if (!string.IsNullOrEmpty(layer))
                        {
                            BooleanQuery layerQuery = new BooleanQuery();
                            layerQuery.Add(new TermQuery(new Term("Layer", "Common")), Occur.SHOULD);
                            layerQuery.Add(new TermQuery(new Term("Layer", layer)), Occur.SHOULD);
                            bq.Add(layerQuery, Occur.MUST);
                        }

                        int searchCounter = 5;
                        var waitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);


                        Task.Run(() =>
                        {
                            do
                            {
                                ProgressHub.SendMessage(
                                string.Format("Collecting results for token \"{0}\" of branch {1}", token, branch), (searchCounter) % 100,
                                connectionID);
                                searchCounter += 5;
                            } while (!waitHandle.WaitOne(TimeSpan.FromSeconds(1)));
              
                        });

                        Lucene.Net.Search.TopDocs results = searcher.Search(bq, null, 200000);
                        waitHandle.Set();

                        

                        object sync = new object();

                        int counter = 1;

                        //Calculate what is the 5% of the total results
                        double percentage = (5.0 *results.ScoreDocs.Count()/100);

                        Parallel.ForEach(results.ScoreDocs, scoreDoc =>
                        {
                            Task.Run(() =>
                            {
                                int curCounter;
                                lock (sync)
                                {
                                    curCounter = counter++;
                                }

                                if ((curCounter % Math.Ceiling(percentage)) == 0)
                                {
                                    ProgressHub.SendMessage(
                                        string.Format("Processing results {0} of {1}", curCounter, results.ScoreDocs.Count()),
                                        curCounter * 100 / results.ScoreDocs.Count(), connectionID);
                                }

                                
                                    
                                
                            });

                            // retrieve the document from the 'ScoreDoc' object
                            Lucene.Net.Documents.Document doc = searcher.Doc(scoreDoc.Doc);
                            string relativeFilePath = doc.Get("RelativeFilePath");
                            int lineNumber = int.Parse(doc.Get("LineNumber"));
                            string line = doc.Get("LineText");

                            FileSearchModel mod = new FileSearchModel();
                            mod.FileName = relativeFilePath;
                            mod.FileNameForDisplay = "File name: " + relativeFilePath;
                            mod.RowNumbers = new SortedSet<int>();
                            mod.RowNumbers.Add(lineNumber);
                            mod.Lines = new SortedSet<string>();
                            mod.Lines.Add(string.Format("Line {0}: {1}", lineNumber, line));
                            mod.Branch = branch;

                            searchDictionaryConcurrent.AddOrUpdate(mod.FileName, mod, (key, existingVal) =>
                            {
                                doc = searcher.Doc(scoreDoc.Doc);
                                lineNumber = int.Parse(doc.Get("LineNumber"));
                                line = doc.Get("LineText");
                                existingVal.RowNumbers.Add(lineNumber);
                                existingVal.Lines.Add(string.Format("Line {0}: {1}", lineNumber, line));
                                return existingVal;
                            });


                        });



                        searcher.Dispose();

                        //searchResults.CompleteAdding();
                        lastSearchResults = searchDictionaryConcurrent.Values.ToList();
                        Session["LastSearchResults"] = lastSearchResults;
                        luceneIndexDirectory.Dispose();
                        ProgressHub.SendMessage(string.Empty, 0, connectionID);
                        stopWatch.Stop();
                        if (lastSearchResults.Count > 0)
                        {
                            searchMessage = string.Format("{0} matches in {1} files ({2} seconds)",
                                lastSearchResults.Sum(mod => mod.RowNumbers.Count).ToString("##,###"),
                                lastSearchResults.Count.ToString("##,###"),
                                ((double) stopWatch.ElapsedMilliseconds/1000).ToString("N"));
                        }
                        else
                        {
                            searchMessage = string.Format("Search took {0} seconds to be completed",
                               
                               ((double)stopWatch.ElapsedMilliseconds / 1000).ToString("N"));
                        }
                        Session["searchMessage"] = searchMessage;



                    }
                    ViewBag.SearchMessage = searchMessage;
                    AsyncManager.Parameters["lastSearchResults"] = lastSearchResults;
                    AsyncManager.Parameters["currentIndex"] = currentIndex;
                    AsyncManager.Parameters["stopWatch"] = stopWatch;
                    AsyncManager.OutstandingOperations.Decrement();

                }

      
            });

 


            
        }



        public ActionResult SearchInFilesCompleted(List<FileSearchModel> lastSearchResults, int currentIndex)
        {

            return View("FileSearchView", lastSearchResults.ToPagedList(currentIndex, MAX_RESULTS_PER_PAGE));
        }

        /// <summary>
        /// Generate diff option list 
        /// </summary>
        /// <param name="relativeFilePath">Relative path of existing file</param>
        /// <param name="branch">Branch of file</param>
        private void GenerateDiffList(string relativeFilePath, string branch)
        {
            string indexPath = FileSearchController.GetIndexPath();
            Directory luceneIndexDirectory = FSDirectory.Open(Path.Combine(indexPath, branch));

            IndexSearcher searcher = new IndexSearcher(luceneIndexDirectory);
            BooleanQuery bq = new BooleanQuery();



            List<string> currentBranchList = Session["branches"] as List<string>;
            List<SelectListItem> comparedBranches = new List<SelectListItem>();

            if (branch != "Configuration")
            {
                comparedBranches.AddRange(from branchName in currentBranchList
                                          where branchName != branch && branchName != "Configuration"
                                          select new SelectListItem
                                          {
                                              Text = branchName,
                                              Value = branchName
                                          });
            }
            else
            {
                string[] relativePathParts = relativeFilePath.Split(Path.DirectorySeparatorChar);
                BooleanQuery diffQuery = new BooleanQuery();

                string[] projNameParts = relativePathParts[CONFIGURATION_PROJECT_NAME].Split(' ');
                PhraseQuery prQuery = new PhraseQuery();
                foreach (var str in projNameParts)
                {
                    prQuery.Add(new Term("Project", str));
                }

                diffQuery.Add(prQuery, Occur.MUST_NOT);

                string layer = relativePathParts[CONFIGURATION_LAYER];
                string domain = relativePathParts[CONFIGURATION_DOMAIN];
                string technology = relativePathParts[CONFIGURATION_TECHNOLOGY];


                if (domain.ToUpper().Equals("CS") && layer.ToUpper().Equals("ACCESS") && technology.ToUpper().Equals("NRG"))
                {
                    layer = technology.ToUpper();
                }

                diffQuery.Add(new TermQuery(new Term("Domain", domain)), Occur.MUST);
                diffQuery.Add(new TermQuery(new Term("Layer", layer)), Occur.MUST);
                diffQuery.Add(new TermQuery(new Term("FileName", Path.GetFileName(relativeFilePath))), Occur.MUST);

                //Search files of other projects
                Lucene.Net.Search.TopDocs diffProjects = searcher.Search(diffQuery, null, 200000);

                foreach (ScoreDoc scoreDoc in diffProjects.ScoreDocs)
                {
                    // retrieve the document from the 'ScoreDoc' object
                    Lucene.Net.Documents.Document diffDoc = searcher.Doc(scoreDoc.Doc);

                    string project = diffDoc.Get("Project");
                    string uri = diffDoc.Get("Uri");

                    if (!comparedBranches.Any(l => l.Text == project && l.Value == uri))
                    {
                        comparedBranches.Add(new SelectListItem
                        {
                            Text = project,
                            Value = uri
                        });
                    }
                }
            }

            Session["comparedBranchesList"] = comparedBranches;

        }


        [HttpGet]
        public ActionResult DisplayFile(string fileName, string rowNumbersStr, string branch)
        {

            var diffBranchesTask = Task.Factory.StartNew(() =>GenerateDiffList(fileName,
                branch));


            Session["diffBranchesTask"] = diffBranchesTask;

            List<int> rowNumbers = rowNumbersStr.Split(',').Select(int.Parse).ToList();
            string indexPath = GetIndexPath();
            Directory luceneIndexDirectory = FSDirectory.Open(Path.Combine(indexPath, branch));

            SortedDictionary<int, string> fileLines = new SortedDictionary<int, string>();
            IndexSearcher searcher = new IndexSearcher(luceneIndexDirectory);
            BooleanQuery bq = new BooleanQuery();


            string uri = string.Empty;

            bq.Add(new TermQuery(new Term("RelativeFilePath", fileName)), Occur.MUST);


            Lucene.Net.Search.TopDocs results = searcher.Search(bq, null, 200000);



            foreach (ScoreDoc scoreDoc in results.ScoreDocs)
            {
                // retrieve the document from the 'ScoreDoc' object
                Lucene.Net.Documents.Document doc = searcher.Doc(scoreDoc.Doc);
                int lineNumber = int.Parse(doc.Get("LineNumber"));
                string line = doc.Get("LineText");

                if (!fileLines.ContainsKey(lineNumber))
                {
                    fileLines.Add(lineNumber, lineNumber.ToString("D3") + ":  " + line.Replace("\t", " ").TrimEnd());
                }

                if (string.IsNullOrEmpty(uri))
                {
                    uri = doc.Get("Uri");
                }


            }

            searcher.Dispose();
            luceneIndexDirectory.Dispose();


            DisplayFileModel model = new DisplayFileModel
            {
                RowNumbers = rowNumbers,
                Lines = fileLines.Values.ToList(),
                FileNameForDisplay = "File name: " + fileName,
                Branch = branch,
                RelativeFilePath = fileName,

                Uri = uri
            };


            return View("DisplayFileView", model);


        }

   

    }
}
