using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IndexBranchesTask
{
    public class ItemToIndex
    {
        
        public string RelativeFilePath { get; set; }
        public int LineNumber { get; set; }
        public string LineText { get; set; }
        public string Branch { get; set; }
        public string Layer { get; set; }
        
    }
}
