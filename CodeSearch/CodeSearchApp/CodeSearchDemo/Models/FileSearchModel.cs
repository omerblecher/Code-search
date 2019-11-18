using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace CodeSearchDemo.Models
{
    public class FileSearchModel
    {
        public string FileName { get; set; }
        public string FileNameForDisplay { get; set; }
        public SortedSet<int> RowNumbers { get; set; }
        public SortedSet<string> Lines { get; set; } 
        public string Branch { get; set; }
    }
}