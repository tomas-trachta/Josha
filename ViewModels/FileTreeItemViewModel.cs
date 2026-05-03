using Josha.Models;

namespace Josha.ViewModels
{
    internal class FileTreeItemViewModel : TreeItemViewModel
    {
        private readonly FileOD _file;
        private readonly string _directoryPath;

        internal FileOD Model => _file;
        internal string DirectoryPath => _directoryPath;
        internal string FullPath => System.IO.Path.Combine(_directoryPath, _file.Name);

        public override string DisplayName => _file.Name;
        public override string SizeDisplay => FormatSize(_file.SizeKiloBytes);
        public override bool IsFile => true;

        public FileTreeItemViewModel(FileOD file, string directoryPath)
        {
            _file = file;
            _directoryPath = directoryPath;
        }
    }
}
