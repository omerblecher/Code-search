using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Configuration;
using System.Web.Mvc;
using CodeSearchDemo.Models;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using SharpSvn;
using Directory = Lucene.Net.Store.Directory;

namespace CodeSearchDemo.Controllers
{
    public class DiffController : AsyncController
    {




        [ChildActionOnly]
        public void GetDiffBranchesAsync(string relativeFilePath, string branch, string uri)
        {
            AsyncManager.OutstandingOperations.Increment(2);
            Task.Run(() =>
            {

                var diffBranchesTask = Session["diffBranchesTask"] as Task;
                if (diffBranchesTask != null)
                {
                    diffBranchesTask.Wait();
                    AsyncManager.Parameters["comparedBranchesList"] = Session["comparedBranchesList"];

                }
                AsyncManager.Parameters["branch"] = branch;
                AsyncManager.Parameters["relativeFilePath"] = relativeFilePath;
                AsyncManager.OutstandingOperations.Decrement();
            });

            Task.Run(() =>
            {
                if (string.IsNullOrEmpty(uri))
                {
                    uri = GetUriOfBranch(relativeFilePath, branch, branch);
                }
                AsyncManager.Parameters["uri"] = uri;
                AsyncManager.OutstandingOperations.Decrement();
            });

        }

        public PartialViewResult GetDiffBranchesCompleted(string uri, string branch, string relativeFilePath, List<SelectListItem> comparedBranchesList)
        {

            DiffBranchesModel model = new DiffBranchesModel
            {
                Uri = uri,
                Branch = branch,
                RelativeFilePath = relativeFilePath,
                ComparedBranchesList = comparedBranchesList
            };
            return PartialView("DiffListView", model);

        }

        [HttpGet]
        public void CompareFilesAsync(string relativeFilePath, string sourceFileUri, string sourceBranch, string branchInfo)
        {
            AsyncManager.OutstandingOperations.Increment();
            Task.Run(() =>
            {


                string branchUrl = GetUriOfBranch(relativeFilePath, sourceBranch, branchInfo);
                if (!string.IsNullOrEmpty(branchUrl))
                {
                    //Generate compare report
                    GenerateCompareReport(sourceFileUri, branchUrl);
                }
                else
                {
                    ModelState.AddModelError("Error",
                        string.Format("File path {0} doesn't exist in branch {1}", relativeFilePath,
                            branchInfo));
                    AsyncManager.Parameters["diffResults"] = string.Empty;
                }


                AsyncManager.OutstandingOperations.Decrement();
            });

        }

        /// <summary>
        /// Generating html compare report
        /// </summary>
        /// <param name="sourceFileUri">Source file uri in SVN</param>
        /// <param name="branchUrl">Destination file uri in SVN</param>
        private void GenerateCompareReport(string sourceFileUri, string branchUrl)
        {
            try
            {
                string fileName = Path.GetTempFileName();
                using (SvnClient client = new SvnClient())
                using (MemoryStream result = new MemoryStream())
                using (Stream fs = System.IO.File.Create(fileName))
                {
                    SvnTarget from = sourceFileUri;
                    SvnTarget to = branchUrl;
                    var diffargs = new SvnDiffArgs();
                    diffargs.IgnoreAncestry = true;

                    client.Authentication.ForceCredentials("umdbuild", "Rel7.xPass!");
                    client.LoadConfiguration(Path.Combine(Path.GetTempPath(), "Svn"), true);

                    //Get the diff results
                    if (client.Diff(from, to, diffargs, result))
                    {
                        result.Position = 0;

                        if (result.Length > 0)
                        {
                            using (StreamReader sr = new StreamReader(result, Encoding.ASCII))
                            using (StreamWriter sw = new StreamWriter(fs))
                            {
                                string res;
                                while ((res = sr.ReadLine()) != null)
                                {
                                    sw.WriteLine(res);
                                }
                            }
                        }
                        else
                        {
                            ModelState.AddModelError("Same",
                                "Files are identical!");
                            AsyncManager.Parameters["diffResults"] = string.Empty;
                        }
                    }
                    else
                    {
                        ModelState.AddModelError("Error",
                            string.Format("Failed to get comparison results to URL {0} and URL {1}", sourceFileUri,
                                branchUrl));
                        AsyncManager.Parameters["diffResults"] = string.Empty;
                    }
                }

                if (System.IO.File.Exists(fileName))
                {
                    RunHtmlGenProcess(fileName);
                }
            }
            catch (Exception e)
            {
                ModelState.AddModelError("Error",
                    string.Format("Failed to get comparison results to URL {0} and URL {1}. Reason: {2}", sourceFileUri,
                        branchUrl, e));
                AsyncManager.Parameters["diffResults"] = string.Empty;
            }
        }

        /// <summary>
        /// Run Python script to generate HTML diff report
        /// </summary>
        /// <param name="fileName">Temporary file name</param>
        private void RunHtmlGenProcess(string fileName)
        {
            Process converToHtmlProcess = new Process();

            string script = WebConfigurationManager.AppSettings["DiffToHtmlTool"];
            string args = script + " -i " + fileName + " -e ASCII";

            converToHtmlProcess.StartInfo.FileName = "python.exe";
            converToHtmlProcess.StartInfo.Arguments = args;
            converToHtmlProcess.StartInfo.UseShellExecute = false;
            converToHtmlProcess.StartInfo.RedirectStandardOutput = true;
            converToHtmlProcess.StartInfo.StandardOutputEncoding = Encoding.ASCII;
            converToHtmlProcess.StartInfo.RedirectStandardError = true;

            converToHtmlProcess.Start();


            string diffResults = converToHtmlProcess.StandardOutput.ReadToEnd();
            string error = converToHtmlProcess.StandardError.ReadToEnd();
            if (!string.IsNullOrEmpty(error))
            {
                ModelState.AddModelError("Error",
                    error);
            }

            Thread tr = new Thread(() => System.IO.File.Delete(fileName));
            tr.Start();

            converToHtmlProcess.WaitForExit();
            converToHtmlProcess.Close();
            AsyncManager.Parameters["diffResults"] = diffResults;
        }

        public ActionResult CompareFilesCompleted(string diffResults)
        {

            CompareModel model = new CompareModel
            {
                CompareResultsHtml = diffResults
            };
            return View("CompareView", model);

        }

        /// <summary>
        /// Get the uri of a file in the SVN
        /// </summary>
        /// <param name="relativeFilePath">Relative file path</param>
        /// <param name="sourcebranch">The source branch</param>
        /// <param name="fileUri"></param>
        /// <returns></returns>
        public string GetUriOfBranch(string relativeFilePath, string sourcebranch, string fileUri)
        {
            string uri = fileUri;


            if (sourcebranch != "Configuration")
            {
                string indexPath = FileSearchController.GetIndexPath();

                //Get the directory of the file information
                // in that case file uri is the destination file name
                using (Directory luceneIndexDirectory = FSDirectory.Open(Path.Combine(indexPath, fileUri, "FileInformation")))
                using (IndexSearcher searcher = new IndexSearcher(luceneIndexDirectory))
                {
                    BooleanQuery bq = new BooleanQuery();

                    bq.Add(new TermQuery(new Term("RelativeFilePath", relativeFilePath)), Occur.MUST);

                    //Search for file in the branch
                    Lucene.Net.Search.TopDocs results = searcher.Search(bq, null, 10);

                    //A file is found
                    if (results.ScoreDocs.Any())
                    {
                        ScoreDoc scoreDoc = results.ScoreDocs[0];
                        Lucene.Net.Documents.Document doc = searcher.Doc(scoreDoc.Doc);
                        uri = doc.Get("Uri");
                    }
                    else
                    {
                        uri = string.Empty;
                    }
                }

            }


            return uri;
        }




    }
}
