using System.Collections.ObjectModel;
using System.Windows.Input;

namespace Josha.ViewModels
{
    internal class PaneColumnViewModel : BaseViewModel
    {
        private FilePaneViewModel? _activeTab;

        public ObservableCollection<FilePaneViewModel> Tabs { get; } = new();

        public FilePaneViewModel? ActiveTab
        {
            get => _activeTab;
            set
            {
                if (_activeTab == value) return;
                if (_activeTab != null) _activeTab.IsCurrentInColumn = false;
                _activeTab = value;
                if (_activeTab != null) _activeTab.IsCurrentInColumn = true;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public ICommand NewTabCommand { get; }
        public ICommand CloseActiveTabCommand { get; }
        public ICommand NextTabCommand { get; }
        public ICommand PrevTabCommand { get; }

        public PaneColumnViewModel(string initialPath = @"C:\")
        {
            var first = new FilePaneViewModel(initialPath);
            Tabs.Add(first);
            _activeTab = first;
            first.IsCurrentInColumn = true;

            NewTabCommand        = new RelayCommand(_ => AddTab(_activeTab?.CurrentPath ?? @"C:\"));
            CloseActiveTabCommand = new RelayCommand(_ => CloseTab(_activeTab), _ => Tabs.Count > 1 && _activeTab != null);
            NextTabCommand       = new RelayCommand(_ => CycleTab(+1), _ => Tabs.Count > 1);
            PrevTabCommand       = new RelayCommand(_ => CycleTab(-1), _ => Tabs.Count > 1);
        }

        public FilePaneViewModel AddTab(string path)
        {
            var tab = new FilePaneViewModel(path);
            Tabs.Add(tab);
            ActiveTab = tab;
            return tab;
        }

        public FilePaneViewModel AddRemoteTab(Models.FtpSite site)
        {
            var tab = new FilePaneViewModel(initialPath: null);
            tab.AttachRemoteSite(site);
            Tabs.Add(tab);
            ActiveTab = tab;
            _ = tab.ConnectAsync();
            return tab;
        }

        public void CloseTab(FilePaneViewModel? tab)
        {
            if (tab == null || Tabs.Count <= 1) return;
            var idx = Tabs.IndexOf(tab);
            if (idx < 0) return;

            var wasActive = tab == _activeTab;
            Tabs.Remove(tab);

            if (wasActive)
                ActiveTab = Tabs[Math.Min(idx, Tabs.Count - 1)];
        }

        private void CycleTab(int delta)
        {
            if (_activeTab == null || Tabs.Count <= 1) return;
            var idx = Tabs.IndexOf(_activeTab);
            if (idx < 0) return;
            var next = (idx + delta + Tabs.Count) % Tabs.Count;
            ActiveTab = Tabs[next];
        }
    }
}
