using Josha.Models;
using System.Collections.ObjectModel;

namespace Josha.ViewModels
{
    internal class CommandPaletteViewModel : BaseViewModel
    {
        private readonly List<CommandPaletteItem> _allItems;
        private string _query = "";
        private CommandPaletteItem? _selected;

        public ObservableCollection<CommandPaletteItem> FilteredItems { get; } = new();

        public string Query
        {
            get => _query;
            set
            {
                if (_query == value) return;
                _query = value ?? "";
                OnPropertyChanged();
                Refilter();
            }
        }

        public CommandPaletteItem? Selected
        {
            get => _selected;
            set { if (_selected == value) return; _selected = value; OnPropertyChanged(); }
        }

        public CommandPaletteViewModel(IEnumerable<CommandPaletteItem> items)
        {
            _allItems = items.ToList();
            Refilter();
        }

        private void Refilter()
        {
            FilteredItems.Clear();

            IEnumerable<CommandPaletteItem> ranked;
            if (string.IsNullOrWhiteSpace(_query))
            {
                ranked = _allItems
                    .OrderBy(i => i.CategoryOrder)
                    .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                var q = _query.Trim();
                ranked = _allItems
                    .Select(i => (Item: i, Score: Score(q, i.Title) + 5 * Score(q, i.Subtitle) / 10))
                    .Where(x => x.Score > 0)
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => x.Item.CategoryOrder)
                    .ThenBy(x => x.Item.Title, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.Item);
            }

            foreach (var item in ranked.Take(80))
                FilteredItems.Add(item);

            Selected = FilteredItems.Count > 0 ? FilteredItems[0] : null;
        }

        // Score bands: prefix > word-start > substring > subsequence > miss.
        private static int Score(string query, string target)
        {
            if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(target)) return 0;

            var q = query.ToLowerInvariant();
            var t = target.ToLowerInvariant();

            if (t.StartsWith(q)) return 1000 - q.Length;
            for (int i = 0; i < t.Length - q.Length + 1; i++)
            {
                if ((i == 0 || !char.IsLetterOrDigit(t[i - 1]))
                    && t.AsSpan(i, q.Length).SequenceEqual(q.AsSpan()))
                    return 600 - i;
            }
            int sub = t.IndexOf(q, StringComparison.Ordinal);
            if (sub >= 0) return 300 - sub;

            int qi = 0;
            int firstMatch = -1, lastMatch = -1;
            for (int i = 0; i < t.Length && qi < q.Length; i++)
            {
                if (t[i] == q[qi])
                {
                    if (firstMatch < 0) firstMatch = i;
                    lastMatch = i;
                    qi++;
                }
            }
            if (qi != q.Length) return 0;

            int span = lastMatch - firstMatch + 1;
            return 100 - span + q.Length;
        }
    }
}
