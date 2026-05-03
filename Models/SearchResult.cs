using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Josha.Models
{
    internal class SearchResult(string name, DirOD dir, SearchResultType type)
    {
        public string Name { get; set; } = name;
        public DirOD Directory { get; set; } = dir;
        public SearchResultType ResultType { get; set; } = type;

        public string TypeLabel => ResultType == SearchResultType.File ? "File" : "Folder";
        public string GroupLabel => ResultType == SearchResultType.File ? "Files" : "Folders";
        public string LocationPath => ResultType == SearchResultType.Directory
            ? System.IO.Path.GetDirectoryName(Directory.Path) ?? Directory.Path
            : Directory.Path;
    }

    internal enum SearchResultType
    {
        File,
        Directory
    }
}
