using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace CodeSearchDemo.Models
{
    public class DiffBranchesModel
    {

        public string Branch { get; set; }
        public string RelativeFilePath { get; set; }
        public string Uri { get; set; }
        public List<SelectListItem> ComparedBranchesList { get; set; }
    }
}