using System.Collections.ObjectModel;

namespace Josha.ViewModels
{
    internal abstract class TreeItemViewModel : BaseViewModel
    {
        public abstract string DisplayName { get; }
        public abstract string SizeDisplay { get; }
        public virtual bool IsFile => false;
        public virtual bool HasContent => false;
        public virtual bool IsExpanded { get; set; }
        public virtual ObservableCollection<TreeItemViewModel> Children { get; } = [];

        public virtual void PreloadChildren() { }
        public virtual void FlushPreloadedChildren() { }

        protected static string FormatSize(decimal sizeKB)
        {
            if (sizeKB < 1000)
                return $"{sizeKB:N0} KB";
            if (sizeKB < 1_000_000)
                return $"{sizeKB / 1000:N1} MB";
            return $"{sizeKB / 1_000_000:N2} GB";
        }
    }
}
