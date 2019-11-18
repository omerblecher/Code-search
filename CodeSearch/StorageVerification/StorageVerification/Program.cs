using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StorageVerification
{
    public class FolderInformation
    {
        public string Name { get; set; }
        public UInt64 Size { get; set; }
        public bool IsExist { get; set; }
        public bool HasFileInformation { get; set; }
    }
    class Program
    {
        private static Dictionary<string, FolderInformation> firstRootPathInfo = new Dictionary<string, FolderInformation>(); 
        private static Dictionary<string, FolderInformation> secondRootPathInfo = new Dictionary<string, FolderInformation>(); 
        private static List<string> folderNames = new List<string>(); 
        private static List<string> missingBranches = new List<string>(); 
        static void Main(string[] args)
        {
            string firstIndexPath = ConfigurationManager.AppSettings["FirstRootPath"];
            string secondIndexPath = ConfigurationManager.AppSettings["SecondRootPath"];
            string missingPath = ConfigurationManager.AppSettings["MissingRootPath"];

            foreach (string path in System.IO.Directory.GetDirectories(firstIndexPath))
            {
                string folderName = path.Remove(0, firstIndexPath.Length + 1);
                
                //Add to the folder names list
                folderNames.Add(folderName);
                FolderInformation folder = new FolderInformation()
                {
                    Name = folderName,
                    Size = DirSize(new DirectoryInfo(path)),
                    IsExist = true,
                    HasFileInformation = Directory.Exists(Path.Combine(path, "FileInformation"))
                };
                firstRootPathInfo.Add(folderName,folder);
                secondRootPathInfo.Add(folderName, CreateEmptyFolderInformation(folderName));
            }


            foreach (string path in System.IO.Directory.GetDirectories(secondIndexPath))
            {
                string folderName = path.Remove(0, secondIndexPath.Length + 1);
                FolderInformation folder = null;

                if (folderNames.Contains(folderName))
                {
                    folder = secondRootPathInfo[folderName];
                }
                else
                {
                    firstRootPathInfo.Add(folderName, CreateEmptyFolderInformation(folderName));
                    folder = CreateEmptyFolderInformation(folderName);
                    //Add to the folder names list
                    folderNames.Add(folderName);
                }







                folder.Size = DirSize(new DirectoryInfo(path));
                folder.IsExist = true;
                folder.HasFileInformation = Directory.Exists(Path.Combine(path, "FileInformation"));

                if (!secondRootPathInfo.ContainsKey(folderName))
                {
                    secondRootPathInfo.Add(folderName, folder);
                }


            }

            foreach (string folder in folderNames)
            {
                FolderInformation firstFolder = firstRootPathInfo[folder];
                FolderInformation secondFolder = secondRootPathInfo[folder];
                Console.WriteLine(folder);
                Console.WriteLine("Path {0}        Exist - {1}   size - {2} KB  Has information folder {3}", firstIndexPath, firstFolder.IsExist, firstFolder.Size / 1024, firstFolder.HasFileInformation);
                Console.WriteLine("Path {0}        Exist - {1}   size - {2} KB  Has information folder {3}", secondIndexPath, secondFolder.IsExist, secondFolder.Size / 1024, secondFolder.HasFileInformation);
                if (!folder.Equals("Configuration") &&
                    (firstFolder.Size == 0 || !firstFolder.HasFileInformation || secondFolder.Size == 0 ||
                     !secondFolder.HasFileInformation))
                {
                    missingBranches.Add(folder);
                }

            }
            File.WriteAllLines(missingPath, missingBranches);
            Console.WriteLine("Please press any key to continue...");
            Console.ReadKey();

        }

        public static UInt64 DirSize(DirectoryInfo d)
        {
            
            UInt64 Size = 0;
            // Add file sizes.
            FileInfo[] fis = d.GetFiles();
            foreach (FileInfo fi in fis)
            {
                Size += (UInt64)fi.Length;
            }
            // Add subdirectory sizes.
            DirectoryInfo[] dis = d.GetDirectories();
            foreach (DirectoryInfo di in dis)
            {
                Size += DirSize(di);
            }
            return (Size);
        }

        public static FolderInformation CreateEmptyFolderInformation(string name)
        {
            return new FolderInformation()
            {
                Name = name,
                Size = 0,
                IsExist = false,
                HasFileInformation = false
            };
        }
    }


   
    
}
