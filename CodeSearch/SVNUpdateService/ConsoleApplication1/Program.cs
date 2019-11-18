using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers;
using Lucene.Net.Search;
using Lucene.Net.Store;
using SharpSvn;
using ZetaLongPaths;
using Directory = Lucene.Net.Store.Directory;


namespace IndexBranchesTask
{
    class Program
    {
        private static readonly string[] _extensions = { ".cs", ".cpp", ".h", ".xml", ".txt" };

        private static readonly string[] _confExtensions = { ".xml", ".reg" };

        private static readonly string[] _blackLis = { @"obj\Release", @"obj\Debug", ".csproj" };

        private static readonly string[] _layers = {"Common", "Core", "Access", "Delivery", "CCFlow", "Targeting"};

        public const string EVENT_SOURCE = "Indexing SVN Branches";

        

        private const string BASE_URL = "http://tlvsvn1/svn/repos-UMD/One FE/";

        private const string LAST_UPDATE_FILE = "UpdateTime.txt";
        private const int CONFIGURATION_LAYER = 5;
        private const int CONFIGURATION_DOMAIN = 4;
        private const int CONFIGURATION_TECHNOLOGY = 6;
        private const int CONFIGURATION_PROJECT_NAME = 3;
        private const int CODE_FILE_LAYER = 3;
        private const int DEFAULT_DAYS_BEFORE = 2;


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
        /// <returns>The oldest updated working directory</returns>
        private static string GetIndexPath()
        {
            var appSettings = ConfigurationManager.AppSettings;
            string firstIndexPath = appSettings["FirstRootPath"];
            string secondIndexPath = appSettings["SecondRootPath"];

            return GetLastUpdateTime(secondIndexPath) < GetLastUpdateTime(firstIndexPath)
                ? secondIndexPath
                : firstIndexPath;
        }


        private static void Main(string[] args)
        {

            if (!EventLog.SourceExists(EVENT_SOURCE))
            {
                EventLog.CreateEventSource(EVENT_SOURCE, "Application");
            }



            var appSettings = ConfigurationManager.AppSettings;
            string rootPath = appSettings["RootPath"];

            Console.WriteLine("Start indexing: " + DateTime.Now);
            string[] BranchNames = new string[0];

            // string[] BranchNames = System.IO.File.ReadAllLines(@"D:\IndexBranches\Branches.txt");

            int daysBefore = DEFAULT_DAYS_BEFORE;
            bool readFromFile = false;

            if (args.Length > 0)
            {
                if (args[0].ToUpper().Equals("-P"))
                {
                    BranchNames = System.IO.File.ReadAllLines(args[1]);
                    readFromFile = true;
                }
                else if (args[0].ToUpper().Equals("-D"))
                {
                    daysBefore = int.Parse(args[1]);
                }
                
            }

            if (!readFromFile)
            {
                BranchNames = GetBranchesForUpdate(daysBefore).ToArray();
            }




            //Checkout configuration
            Analyzer confAnalyzer = new WhitespaceAnalyzer();
            Directory confLuceneIndexDirectory = null;
            IndexWriter confWriter = null;
            try
            {
                try
                {
                    using (SvnClient client = new SvnClient())
                    {
                        client.Authentication.ForceCredentials("umdbuild", "Rel7.xPass!");
                        client.LoadConfiguration(Path.Combine(Path.GetTempPath(), "Svn"), true);

                        // Checkout the code to the specified directory
                        Console.WriteLine("Checking out configuration!");
                        client.CheckOut(new Uri(BASE_URL + "Configuration/"),
                            Path.Combine(rootPath, "Configuration"));


                    }
                }
                catch (Exception e)
                {
                    EventLog.WriteEntry(EVENT_SOURCE,
                        "Fail to checking out configuration. Details: " + e, EventLogEntryType.Error);
                    Console.WriteLine("Fail to checking out configuration. Details: " + e);
                }


                string indexPath = Path.Combine(GetIndexPath(), "Configuration");
                if (System.IO.Directory.Exists(indexPath))
                {
                    System.IO.Directory.Delete(indexPath, true);
                }

                confLuceneIndexDirectory = FSDirectory.Open(indexPath);
                confWriter = new IndexWriter(confLuceneIndexDirectory, confAnalyzer,
                    IndexWriter.MaxFieldLength.UNLIMITED);
                Console.WriteLine("Indexing configuration Time: " + DateTime.Now);

                IndexConfiguration(confWriter);



                try
                {
                    ZlpDirectoryInfo confDir = new ZlpDirectoryInfo(Path.Combine(rootPath, "Configuration"));
                    SetAttributesNormal(confDir);
                    confDir.Delete(true);
                }
                catch (Exception e)
                {
                    EventLog.WriteEntry(EVENT_SOURCE,
                        "Failed to delete configuration directory. Details: " + e,
                        EventLogEntryType.Error);
                    Console.WriteLine("Failed to delete configuration directory. Details: " + e);
                }




                Console.WriteLine("Optimizing " + DateTime.Now);
                confWriter.Optimize();

            }
            catch (Exception e)
            {
                EventLog.WriteEntry(EVENT_SOURCE,
                    "Fail to indexing configuration. Details: " + e, EventLogEntryType.Error);
                Console.WriteLine("Fail to indexing configuration. Details: " + e);
            }
            finally
            {
                if (confWriter != null) confWriter.Dispose();
                confAnalyzer.Dispose();
                if (confLuceneIndexDirectory != null) confLuceneIndexDirectory.Dispose();
            }


            foreach (var branch in BranchNames)
            {
                Analyzer analyzer = new WhitespaceAnalyzer();
                Directory luceneIndexDirectory = null;
                IndexWriter indexWriter = null;
                Directory luceneFileInfoDirectory = null;
                IndexWriter fileInfoWriter = null;
                try
                {

                    if (!string.IsNullOrEmpty(branch) && !branch.Equals("Configuration"))
                    {
                        Console.WriteLine("Checking out code for branch: " + branch);
                        Parallel.ForEach(_layers, layer =>
                        {
                            string layerUrl = BASE_URL + layer + "/";
                            string branchUrl = branch.Equals("Trunk")
                                ? layerUrl + "Trunk/"
                                : layerUrl + "Branch/" + branch + "/";
                            //Checkout configuration
                            Console.WriteLine("Checking out " + branchUrl);
                            try
                            {
                                using (SvnClient client = new SvnClient())
                                {
                                    client.Authentication.ForceCredentials("umdbuild", "Rel7.xPass!");
                                    client.LoadConfiguration(Path.Combine(Path.GetTempPath(), "Svn"), true);

                                    string localFolder = branch.Equals("MAINTENANCE-Trunk-GA21")
                                        ? "MAINT-GA21"
                                        : branch;
                                    // Checkout the code to the specified directory
                                    client.CheckOut(new Uri(branchUrl),
                                        Path.Combine(rootPath, localFolder, layer));
                                    if (layer.Equals("Access"))
                                    {
                                        string nrgUrl = layerUrl + "NRG/";
                                        branchUrl = branch.Equals("Trunk")
                                            ? nrgUrl + "Trunk/"
                                            : nrgUrl + "Branch/" + branch + "/";
                                        Console.WriteLine("Checking out " + branchUrl);
                                        // Checkout the code to the specified directory
                                        client.CheckOut(new Uri(branchUrl),
                                            Path.Combine(rootPath, localFolder, "NRG"));
                                    }

                                }
                            }
                            catch (Exception e)
                            {
                                EventLog.WriteEntry(EVENT_SOURCE,
                                    "Failed to checking out from url " + branchUrl + ". Details: " + e,
                                    EventLogEntryType.Error);
                                Console.WriteLine("Failed to checking out from url " + branchUrl + ". Details: " + e);
                            }
                        });




                        string indexPath = Path.Combine(GetIndexPath(), branch);
                        if (System.IO.Directory.Exists(indexPath))
                        {
                            System.IO.Directory.Delete(indexPath, true);
                        }

                        luceneIndexDirectory = FSDirectory.Open(indexPath);
                        indexWriter = new IndexWriter(luceneIndexDirectory, analyzer,
                            IndexWriter.MaxFieldLength.UNLIMITED);

                        //File information directory
                        luceneFileInfoDirectory = FSDirectory.Open(Path.Combine(indexPath, "FileInformation"));
                        fileInfoWriter = new IndexWriter(luceneFileInfoDirectory, analyzer,
                            IndexWriter.MaxFieldLength.UNLIMITED); 


                        Console.WriteLine("Indexing branch: " + branch + " Time: " + DateTime.Now);



                        IndexBranch(branch, indexWriter, fileInfoWriter);

                        string deleteFolder = branch;
                        Thread tr = new Thread(() =>
                        {
                            ZlpDirectoryInfo branchDir = deleteFolder.Equals("MAINTENANCE-Trunk-GA21")
                            ? new ZlpDirectoryInfo(Path.Combine(rootPath, "MAINT-GA21"))
                            : new ZlpDirectoryInfo(Path.Combine(rootPath, deleteFolder));

                            try
                            {
                                SetAttributesNormal(branchDir);
                                branchDir.Delete(true);
                            }
                            catch (Exception e)
                            {
                                EventLog.WriteEntry(EVENT_SOURCE,
                                    "Failed to delete directory " + branchDir + ". Details: " + e,
                                    EventLogEntryType.Error);
                                Console.WriteLine("Failed to delete directory " + branchDir + ". Details: " + e);
                            }
                        });

                        tr.Start();


                        Thread indexingThread = new Thread(() =>
                        {
                            try
                            {
                                Console.WriteLine("Optimizing indexing" + DateTime.Now);
                                indexWriter.Optimize();
                            }
                            catch (Exception e)
                            {
                                EventLog.WriteEntry(EVENT_SOURCE,
                                    "Error in optimizing indexing for " + branch + ". Details: " + e,
                                    EventLogEntryType.Error);
                                Console.WriteLine(e);
                            }
                        }
                        );



                        Thread fileInfoThread = new Thread(() =>
                        {
                            try
                            {
                                Console.WriteLine("Optimizing file information" + DateTime.Now);
                                fileInfoWriter.Optimize();
                            }
                            catch (Exception e)
                            {
                                EventLog.WriteEntry(EVENT_SOURCE,
                                    "Error in optimizing file information for " + branch + ". Details: " + e,
                                    EventLogEntryType.Error);
                                Console.WriteLine(e);
                            }
                        }
                            );

                        indexingThread.Start();
                        fileInfoThread.Start();
                        indexingThread.Join();
                        fileInfoThread.Join();




                    }
                }
                catch (Exception e)
                {
                    EventLog.WriteEntry(EVENT_SOURCE,
                        "Error in updating and indexing " + branch + ". Details: " + e, EventLogEntryType.Error);
                    Console.WriteLine(e);
                }
                finally
                {
                    if (indexWriter != null) indexWriter.Dispose();
                    if (fileInfoWriter != null) fileInfoWriter.Dispose();
                    analyzer.Dispose();
                    if (luceneIndexDirectory != null) luceneIndexDirectory.Dispose();
                    if (luceneFileInfoDirectory != null) luceneFileInfoDirectory.Dispose();
                }
            }






            System.IO.File.WriteAllText(Path.Combine(GetIndexPath(), LAST_UPDATE_FILE), DateTime.Now.ToString("O"));


            Console.WriteLine("Finish indexing " + DateTime.Now);

        }


        private static void SetAttributesNormal(ZlpDirectoryInfo dir)
        {
            foreach (var subDirPath in dir.GetDirectories())
            {
                SetAttributesNormal(subDirPath);
            }

            foreach (var filePath in dir.GetFiles())
            {
                var file = new ZlpFileInfo(filePath.FullName) {Attributes = ZetaLongPaths.Native.FileAttributes.Normal};
            }
        }



        private static void IndexBranch(string branch, IndexWriter contentWriter, IndexWriter fileInfoWriter)
        {
            var appSettings = ConfigurationManager.AppSettings;
            string rootPath = appSettings["RootPath"];
            string codePath = branch.Equals("MAINTENANCE-Trunk-GA21") ? Path.Combine(rootPath, "MAINT-GA21"):Path.Combine(rootPath, branch);
            
            Task.WaitAll(
                System.IO.Directory.EnumerateFiles(codePath, "*.*", SearchOption.AllDirectories)
                    .Where(
                        fileName =>
                            (_extensions.Any(ext => ext == Path.GetExtension(fileName)) &&
                             !_blackLis.Any(fileName.Contains)))
                    .Select(codeFile => Task.Factory.StartNew(
                        () =>
                        {
                            try
                            {

                                Console.WriteLine("Writing file info: " + codeFile);
                                FileInfoToIndex fileInfoToIndex = new FileInfoToIndex
                                {
                                    Branch = branch,
                                    RelativeFilePath = codeFile.Remove(0, codePath.Length),
                                    Layer = codeFile.Split(Path.DirectorySeparatorChar)[CODE_FILE_LAYER],
                                    FileName = Path.GetFileName(codeFile),
                                    Uri = GetFileUri(codeFile)
                                };

                                //Index first the file info
                                IndexFileInfo(fileInfoToIndex, fileInfoWriter);

                                Console.WriteLine("Indexing file: " + codeFile);

                                var lines = File.ReadAllLines(codeFile);

                            

                                for (int index = 0; index < lines.Length; index++)
                                {
                                    ItemToIndex item = new ItemToIndex
                                    {
                                        RelativeFilePath = codeFile.Remove(0, codePath.Length),
                                        LineNumber = index,
                                        LineText = lines[index],
                                        Branch = branch,
                                        Layer = codeFile.Split(Path.DirectorySeparatorChar)[CODE_FILE_LAYER]
                                    };
                                    IndexItem(item, contentWriter);
                                }
                            }
                            catch (Exception e)
                            {
                                EventLog.WriteEntry(EVENT_SOURCE, "Failed index " + codeFile + ". Details: " + e, EventLogEntryType.Error);
                                Console.WriteLine(e);
                            }



                        })).ToArray());


            

        }

        /// <summary>
        /// Get the SVN Uri of a file
        /// </summary>
        /// <param name="fullFilePath">Fill full path</param>
        /// <returns>The Uri of a file</returns>
        private static string GetFileUri(string fullFilePath)
        {
            string uri = string.Empty;

            using (SvnClient client = new SvnClient())
            {
                try
                {
                    client.Authentication.ForceCredentials("umdbuild", "Rel7.xPass!");
                    client.LoadConfiguration(Path.Combine(Path.GetTempPath(), "Svn"), true);

                    Console.WriteLine("Indexing file: " + fullFilePath);
                    SvnStatusArgs sa = new SvnStatusArgs();
                    sa.RetrieveAllEntries = true;
                    Collection<SvnStatusEventArgs> statuses;
                    client.GetStatus(
                        fullFilePath, sa,
                        out statuses);

                    if (statuses.Count > 0)
                    {
                        uri = statuses[0].Uri.ToString();
                        Console.WriteLine("Uri of file " + fullFilePath + " is " + uri);
                    }

                }
                catch (Exception e)
                {

                    EventLog.WriteEntry(EVENT_SOURCE,
                                "Failed to get Uri of file " + fullFilePath + ". Details: " + e,
                                EventLogEntryType.Error);
                    Console.WriteLine("Failed to get Uri of file " + fullFilePath + ". Details: " + e);
                }
            }

            return uri;
        }

        private static List<string> GetBranchesForUpdate(int daysBefore)
        {
            List<string> branches = new List<string>();

            using (SvnClient client = new SvnClient())
            {

                client.Authentication.ForceCredentials("umdbuild", "Rel7.xPass!");
                client.LoadConfiguration(Path.Combine(Path.GetTempPath(), "Svn"), true);

                foreach (var layer in _layers)
                {
                    if (layer.Equals("NRG"))
                    {
                        continue;
                    }
                    string layerUrl = BASE_URL + layer;
                    Console.WriteLine("Checking updates of repository" + layerUrl);
                    try
                    {
                        Collection<SvnLogEventArgs> logitems;
                        DateTime startDateTime = DateTime.Now.AddDays(-daysBefore);
                        DateTime endDateTime = DateTime.Now;
                        var uri = new Uri(layerUrl);
                        SvnRevisionRange range = new SvnRevisionRange(new SvnRevision(startDateTime),
                            new SvnRevision(endDateTime));
                        client.GetLog(uri, new SvnLogArgs(range), out logitems);
                        foreach (var item in logitems)
                        {
                            foreach (var path in item.ChangedPaths)
                            {
                                string[] pathParts = path.RepositoryPath.ToString().Split('/');
                                if (pathParts[2].Equals("Trunk") && !branches.Contains("Trunk"))
                                {
                                    branches.Add("Trunk");
                                    Console.WriteLine("Trunk will be updated");
                                }
                                else if (pathParts[2].Equals("Branch") && !branches.Contains(pathParts[3]))
                                {
                                    branches.Add(pathParts[3]);
                                    Console.WriteLine(pathParts[3] + " will be updated");
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        EventLog.WriteEntry(EVENT_SOURCE,
                            "Failed to get updates from repository " + layerUrl + ". Details: " + e,
                            EventLogEntryType.Error);
                        Console.WriteLine("Failed to get updates from repository  " + layerUrl + ". Details: " + e);
                    }

                }
         

            }

            return branches;
        }

        private static void IndexConfiguration(IndexWriter writer)
        {
            var appSettings = ConfigurationManager.AppSettings;
            string rootPath = appSettings["RootPath"];
            string configPath = Path.Combine(rootPath, "Configuration");


            Task.WaitAll(
                System.IO.Directory.EnumerateFiles(configPath, "*.*", SearchOption.AllDirectories).Where(
                        fileName =>
                            (_confExtensions.Any(ext => ext == Path.GetExtension(fileName))))
                    .Select(codeFile => Task.Factory.StartNew(() =>
                    {
                        //There should be minimal path length
                        if (codeFile.Split(Path.DirectorySeparatorChar).Length < CONFIGURATION_TECHNOLOGY)
                        {
                            return;
                        }

                        try
                        {
                            string uri = GetFileUri(codeFile);
                            Console.WriteLine("Indexing file: " + codeFile);
                            var lines = File.ReadAllLines(codeFile);



                            for (int index = 0; index < lines.Length; index++)
                            {
                                ConfItemToIndex item = new ConfItemToIndex
                                {
                                    FileName = Path.GetFileName(codeFile),
                                    RelativeFilePath = codeFile.Remove(0, rootPath.Length),
                                    LineNumber = index,
                                    Uri = uri,
                                    LineText = lines[index],
                                    Branch = "Configuration",
                                    Domain = codeFile.Split(Path.DirectorySeparatorChar)[CONFIGURATION_DOMAIN],
                                    ProjectName = codeFile.Split(Path.DirectorySeparatorChar)[CONFIGURATION_PROJECT_NAME],
                                    Layer = GetConfigurationLayer(codeFile)
                                };
                                IndexItem(item, writer);
                            }
                        }
                        catch (Exception e)
                        {
                            EventLog.WriteEntry(EVENT_SOURCE,
                                "Failed to index configuration from " + configPath + ". Details: " + e,
                                EventLogEntryType.Error);
                            Console.WriteLine("Failed to index configuration from " + configPath + ". Details: " + e);
                        }

                    })).ToArray());
        }


        /// <summary>
        /// Retrieve the layer of configuration file. Basically it checks if the layer is NRG, and if so, it set it correctly
        /// </summary>
        /// <param name="codeFile">Full path of code file</param>
        /// <returns>Configuration file layer</returns>
        private static string GetConfigurationLayer(string codeFile)
        {
            string[] parts = codeFile.Split(Path.DirectorySeparatorChar);
            string layer = parts[CONFIGURATION_LAYER];
            string domain = parts[CONFIGURATION_DOMAIN];

            if (parts.Length > CONFIGURATION_TECHNOLOGY)
            {
                string technology = parts[CONFIGURATION_TECHNOLOGY];


                if (domain.ToUpper().Equals("CS") && layer.ToUpper().Equals("ACCESS") && technology.ToUpper().Equals("NRG"))
                {
                    layer = technology.ToUpper();
                } 
            }
            

            return layer;

        }

        private static void IndexItem(ItemToIndex item, IndexWriter writer)
        {
            try
            {
                Document doc = new Document();
                doc.Add(new Field("LineNumber",
                    item.LineNumber.ToString(),
                    Field.Store.YES,
                    Field.Index.NO));

                doc.Add(new Field("RelativeFilePath",
                    item.RelativeFilePath,
                    Field.Store.YES,
                    Field.Index.NOT_ANALYZED));



                doc.Add(new Field("LineText",
                    item.LineText,
                    Field.Store.YES,
                    Field.Index.ANALYZED));

                doc.Add(new Field("Branch",
                    item.Branch,
                    Field.Store.YES,
                    Field.Index.ANALYZED));

                doc.Add(new Field("Layer",
                    item.Layer,
                    Field.Store.YES,
                    Field.Index.ANALYZED));


                ConfItemToIndex confItem = item as ConfItemToIndex;
                if (confItem != null)
                {
                    doc.Add(new Field("Project",
                        confItem.ProjectName,
                        Field.Store.YES,
                        Field.Index.ANALYZED));

                    doc.Add(new Field("Domain",
                        confItem.Domain,
                        Field.Store.YES,
                        Field.Index.ANALYZED));

                    doc.Add(new Field("FileName",
                        confItem.FileName,
                        Field.Store.YES,
                        Field.Index.ANALYZED));

                    doc.Add(new Field("Uri",
                        confItem.Uri,
                        Field.Store.YES,
                        Field.Index.NOT_ANALYZED));
                }

                writer.AddDocument(doc);
            }
            catch (Exception e)
            {
                EventLog.WriteEntry(EVENT_SOURCE,
                    "Failed to write index for relative path" + item.RelativeFilePath + ". Details: " + e,
                    EventLogEntryType.Error);
                Console.WriteLine(e);
            }

        }


        private static void IndexFileInfo(FileInfoToIndex item, IndexWriter writer)
        {
            try
            {
                Document doc = new Document();

                doc.Add(new Field("RelativeFilePath",
                    item.RelativeFilePath,
                    Field.Store.YES,
                    Field.Index.NOT_ANALYZED));


                doc.Add(new Field("Branch",
                    item.Branch,
                    Field.Store.YES,
                    Field.Index.NOT_ANALYZED));

                doc.Add(new Field("Layer",
                    item.Layer,
                    Field.Store.YES,
                    Field.Index.NOT_ANALYZED));


                doc.Add(new Field("FileName",
                    item.FileName,
                    Field.Store.YES,
                    Field.Index.NOT_ANALYZED));

                doc.Add(new Field("Uri",
                    item.Uri,
                    Field.Store.YES,
                    Field.Index.NOT_ANALYZED));


                writer.AddDocument(doc);
            }
            catch (Exception e)
            {
                EventLog.WriteEntry(EVENT_SOURCE,
                    "Failed to write file into" + item.RelativeFilePath + ". Details: " + e,
                    EventLogEntryType.Error);
                Console.WriteLine(e);
            }

        }



    }
}
