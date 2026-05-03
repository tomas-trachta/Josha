using Josha.Business;
using MahApps.Metro.IconPacks;
using System.IO;

namespace Josha.ViewModels
{
    internal class FileRowViewModel : BaseViewModel
    {
        private bool _isSelected;
        private bool _isEditing;

        public string Name { get; }
        public string Extension { get; }
        public string FullPath { get; }
        public bool IsDirectory { get; }
        public long? SizeBytes { get; }
        public DateTime? LastModifiedUtc { get; }

        // Synthetic ".." row — sorted to the top regardless of column/direction
        // and excluded from selection.
        public bool IsParentLink { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (_isEditing == value) return;
                _isEditing = value;
                OnPropertyChanged();
            }
        }

        public string SizeDisplay
        {
            get
            {
                if (IsParentLink) return "";
                if (IsDirectory) return SizeBytes is null ? "—" : FormatBytes(SizeBytes.Value);
                if (SizeBytes is null) return "";
                return FormatBytes(SizeBytes.Value);
            }
        }

        public string ModifiedDisplay
        {
            get
            {
                if (IsParentLink) return "";
                if (LastModifiedUtc is null) return "";
                return LastModifiedUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            }
        }

        // "X <unit> ago" using the largest non-zero unit. Sort order is unaffected;
        // the column sorts by raw LastModifiedUtc, not this string.
        public string AgeDisplay
        {
            get
            {
                if (IsParentLink || LastModifiedUtc is null) return "";
                var ago = DateTime.UtcNow - LastModifiedUtc.Value;
                if (ago.Ticks < 0) return "just now";

                if (ago.TotalSeconds < 60) return Plural((int)ago.TotalSeconds, "second");
                if (ago.TotalMinutes < 60) return Plural((int)ago.TotalMinutes, "minute");
                if (ago.TotalHours   < 24) return Plural((int)ago.TotalHours,   "hour");
                if (ago.TotalDays    < 7)  return Plural((int)ago.TotalDays,    "day");
                if (ago.TotalDays    < 30) return Plural((int)(ago.TotalDays / 7),   "week");
                if (ago.TotalDays    < 365) return Plural((int)(ago.TotalDays / 30), "month");
                return Plural((int)(ago.TotalDays / 365), "year");
            }
        }

        // Resource key for the brush colouring AgeDisplay — mapped via
        // ResourceKeyToBrushConverter so themes can override the palette.
        public string AgeBrushKey
        {
            get
            {
                if (IsParentLink || LastModifiedUtc is null) return "Brush.OnSurface";
                var ago = DateTime.UtcNow - LastModifiedUtc.Value;
                if (ago.Ticks < 0)         return "Brush.Age.Seconds";
                if (ago.TotalSeconds < 60) return "Brush.Age.Seconds";
                if (ago.TotalMinutes < 60) return "Brush.Age.Minutes";
                if (ago.TotalHours   < 24) return "Brush.Age.Hours";
                if (ago.TotalDays    < 7)  return "Brush.Age.Days";
                if (ago.TotalDays    < 30) return "Brush.Age.Weeks";
                if (ago.TotalDays    < 365) return "Brush.Age.Months";
                return "Brush.Age.Years";
            }
        }

        private static string Plural(int n, string unit) =>
            $"{n} {unit}{(n == 1 ? "" : "s")}";

        // Lazy: only rows that bind in the viewport pay the lookup.
        private PackIconMaterialKind? _iconKind;
        private string? _iconBrushKey;

        private void EnsureIconResolved()
        {
            if (_iconKind.HasValue) return;
            var (kind, brush) = FileIconMap.Resolve(Name, Extension, IsDirectory, IsParentLink);
            _iconKind = kind;
            _iconBrushKey = brush;
        }

        public PackIconMaterialKind IconKind
        {
            get { EnsureIconResolved(); return _iconKind!.Value; }
        }

        public string IconBrushKey
        {
            get { EnsureIconResolved(); return _iconBrushKey!; }
        }


        private FileRowViewModel(
            string name, string extension, string fullPath, bool isDirectory,
            long? sizeBytes, DateTime? modifiedUtc, bool isParentLink)
        {
            Name = name;
            Extension = extension;
            FullPath = fullPath;
            IsDirectory = isDirectory;
            SizeBytes = sizeBytes;
            LastModifiedUtc = modifiedUtc;
            IsParentLink = isParentLink;
        }

        public static FileRowViewModel FromFile(FileInfo fileInfo)
        {
            var ext = Path.GetExtension(fileInfo.Name);
            if (ext.Length > 0 && ext[0] == '.') ext = ext[1..];

            long? size;
            try { size = fileInfo.Length; } catch { size = null; }

            DateTime? mtime;
            try { mtime = fileInfo.LastWriteTimeUtc; } catch { mtime = null; }

            return new FileRowViewModel(fileInfo.Name, ext, fileInfo.FullName, isDirectory: false, size, mtime, isParentLink: false);
        }

        public static FileRowViewModel FromDirectory(DirectoryInfo dirInfo)
        {
            DateTime? mtime;
            try { mtime = dirInfo.LastWriteTimeUtc; } catch { mtime = null; }

            return new FileRowViewModel(dirInfo.Name, "", dirInfo.FullName, isDirectory: true, sizeBytes: null, mtime, isParentLink: false);
        }

        public static FileRowViewModel ParentLink(string parentFullPath) =>
            new("..", "", parentFullPath, isDirectory: true, sizeBytes: null, modifiedUtc: null, isParentLink: true);

        public static FileRowViewModel? FromDiskUsageNode(TreeItemViewModel node)
        {
            switch (node)
            {
                case DirectoryTreeItemViewModel dir when dir.Model != null:
                    return new FileRowViewModel(
                        name: dir.Model.Name,
                        extension: "",
                        fullPath: dir.Model.Path,
                        isDirectory: true,
                        sizeBytes: (long)(dir.Model.SizeKiloBytes * 1000m),
                        modifiedUtc: null,
                        isParentLink: false);

                case FileTreeItemViewModel file:
                    var ext = Path.GetExtension(file.DisplayName);
                    if (ext.Length > 0 && ext[0] == '.') ext = ext[1..];
                    return new FileRowViewModel(
                        name: file.DisplayName,
                        extension: ext,
                        fullPath: file.FullPath,
                        isDirectory: false,
                        sizeBytes: (long)(file.Model.SizeKiloBytes * 1000m),
                        modifiedUtc: null,
                        isParentLink: false);

                default:
                    return null;
            }
        }

        public static FileRowViewModel FromEntry(FsEntry entry)
        {
            string ext = "";
            if (!entry.IsDirectory)
            {
                ext = Path.GetExtension(entry.Name);
                if (ext.Length > 0 && ext[0] == '.') ext = ext[1..];
            }
            return new FileRowViewModel(entry.Name, ext, entry.FullPath, entry.IsDirectory,
                entry.IsDirectory ? null : entry.Size, entry.ModifiedUtc, isParentLink: false);
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes:N0} B";
            double v = bytes / 1024.0;
            if (v < 1024) return $"{v:N1} KB";
            v /= 1024;
            if (v < 1024) return $"{v:N1} MB";
            v /= 1024;
            if (v < 1024) return $"{v:N2} GB";
            v /= 1024;
            return $"{v:N2} TB";
        }
    }
}
