using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IndexBranchesTask
{
    /// <summary>
    /// This class contains extended information for configuration indexing
    /// </summary>
    public class ConfItemToIndex : ItemToIndex
    {
        public string FileName { get; set; }

        public string ProjectName { get; set; }

        public string Domain { get; set; }

        public string Uri { get; set; }
    }
}
