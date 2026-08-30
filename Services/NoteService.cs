using Josha.Business;
using Josha.Models;
using System.Collections.ObjectModel;
using System.Linq;

namespace Josha.Services
{
    // Global, cross-pane store of notes attached to local files/directories,
    // backing the sidebar's NOTES section and the per-row note indicator.
    // Local-only, like the undo buffer — remote paths have no stable identity
    // across sessions/sites. Persisted immediately on every change; notes are
    // edited rarely enough that a debounce (like SnapshotService) isn't worth
    // the extra state.
    internal sealed class NoteService
    {
        private readonly List<Note> _notes;
        private readonly Dictionary<string, Note> _byPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();

        public ObservableCollection<Note> All { get; } = new();

        public NoteService()
        {
            _notes = NoteComponent.Load();
            RebuildIndex();
        }

        public Note? GetForPath(string path) => _byPath.GetValueOrDefault(Normalize(path));

        public bool HasNote(string path) => _byPath.ContainsKey(Normalize(path));

        public void Upsert(string targetPath, bool isDirectory, string text)
        {
            lock (_lock)
            {
                var key = Normalize(targetPath);
                if (_byPath.TryGetValue(key, out var existing))
                {
                    existing.Text = text;
                    existing.ModifiedUtc = DateTime.UtcNow;
                }
                else
                {
                    _notes.Add(new Note
                    {
                        TargetPath = targetPath,
                        IsDirectory = isDirectory,
                        Text = text,
                        CreatedUtc = DateTime.UtcNow,
                        ModifiedUtc = DateTime.UtcNow,
                    });
                }
                SaveAndRefresh();
            }
        }

        public void Delete(string targetPath)
        {
            lock (_lock)
            {
                var key = Normalize(targetPath);
                if (!_byPath.TryGetValue(key, out var note)) return;
                _notes.Remove(note);
                SaveAndRefresh();
            }
        }

        private void SaveAndRefresh()
        {
            NoteComponent.Save(_notes);
            RebuildIndex();
        }

        private void RebuildIndex()
        {
            _byPath.Clear();
            foreach (var note in _notes)
                _byPath[Normalize(note.TargetPath)] = note;

            All.Clear();
            foreach (var note in _notes.OrderByDescending(n => n.ModifiedUtc))
                All.Add(note);
        }

        private static string Normalize(string path) => path.TrimEnd('\\', '/');
    }
}
