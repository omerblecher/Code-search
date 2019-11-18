using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace CodeSearchDemo.Models
{
    public class DisplayFileModel
    {
        public string FileNameForDisplay { get; set; }
        public List<string> Lines { get; set; }
        public List<int> RowNumbers { get; set; } 
        public string Branch { get; set; }
        public string RelativeFilePath { get; set; }
        public string Uri { get; set; }
    }
}