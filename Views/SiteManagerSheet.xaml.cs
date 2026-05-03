using Josha.Business;
using Josha.Models;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace Josha.Views
{
    public partial class SiteManagerSheet : Window
    {
        private readonly ObservableCollection<FtpSite> _sites = new();

        internal FtpSite? Result { get; private set; }

        internal SiteManagerSheet()
        {
            InitializeComponent();
            ReloadSites();
            SiteList.ItemsSource = _sites;
            if (_sites.Count > 0) SiteList.SelectedIndex = 0;
        }

        private void ReloadSites()
        {
            _sites.Clear();
            foreach (var s in SiteManagerComponent.Load().OrderByDescending(s => s.LastUsedUtc))
                _sites.Add(s);
        }

        private void OnNew(object sender, RoutedEventArgs e)
        {
            var sheet = new NewConnectionSheet { Owner = this };
            if (sheet.ShowDialog() != true || sheet.Result == null) return;

            if (!sheet.SaveToSites)
            {
                // Connect-without-save still funnels through the same Connect path.
                Result = sheet.Result;
                DialogResult = true;
                Close();
                return;
            }

            ReloadSites();
            var newest = _sites.FirstOrDefault(s => s.Id == sheet.Result.Id);
            if (newest != null) SiteList.SelectedItem = newest;
        }

        private void OnEdit(object sender, RoutedEventArgs e)
        {
            // Phase 2 ships the simple flow: edit = "open New connection prefilled".
            // Full inline-edit form is a Phase 3 nicety.
            if (SiteList.SelectedItem is not FtpSite s) return;
            var sheet = new NewConnectionSheet { Owner = this };
            sheet.Prefill(s);
            if (sheet.ShowDialog() != true || sheet.Result == null) return;

            sheet.Result.Id = s.Id;
            SiteManagerComponent.Upsert(sheet.Result);
            ReloadSites();
            SiteList.SelectedItem = _sites.FirstOrDefault(x => x.Id == s.Id);
        }

        private void OnDelete(object sender, RoutedEventArgs e)
        {
            if (SiteList.SelectedItem is not FtpSite s) return;
            SiteManagerComponent.Delete(s.Id);
            ReloadSites();
        }

        private void OnConnect(object sender, RoutedEventArgs e)
        {
            if (SiteList.SelectedItem is not FtpSite s) return;
            Result = s;
            DialogResult = true;
            Close();
        }

        private void OnDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SiteList.SelectedItem is FtpSite)
                OnConnect(sender, e);
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OnCancelCommand(object sender, ExecutedRoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
