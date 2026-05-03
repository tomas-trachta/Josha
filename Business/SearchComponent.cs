using Josha.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Josha.Business
{
    internal class SearchComponent
    {
        internal List<SearchResult> SearchResults { get; set; } = [];

        internal void Search(string name, DirOD dir)
        {
            if (SearchResults.Count >= 50) return;

            if (dir.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                SearchResults.Add(new SearchResult(dir.Name, dir, SearchResultType.Directory));

            foreach(var file in dir.Files)
            {
                if (SearchResults.Count >= 50) return;
                if(file.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                    SearchResults.Add(new SearchResult(file.Name, dir, SearchResultType.File));
            }

            foreach(var subdir in dir.Subdirectories)
            {
                if (SearchResults.Count >= 50) return;
                Search(name, subdir);
            }
        }
    }
}
