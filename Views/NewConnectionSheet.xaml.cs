using Josha.Business;
using Josha.Business.Ftp;
using Josha.Models;
using Josha.Services;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Josha.Views
{
    public partial class NewConnectionSheet : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        internal FtpSite? Result { get; private set; }
        internal bool SaveToSites { get; private set; }

        private bool _testVisible;
        public bool TestVisible
        {
            get => _testVisible;
            set
            {
                if (_testVisible == value) return;
                _testVisible = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TestVisible)));
            }
        }

        public NewConnectionSheet()
        {
            InitializeComponent();
            ProtocolBox.SelectedIndex = 0;
            UpdateDefaultPort();
            DataContext = this;
            Loaded += (_, _) => HostBox.Focus();
        }

        internal void Prefill(FtpSite site)
        {
            HostBox.Text = site.Host;
            PortBox.Text = site.Port.ToString();
            UserBox.Text = site.Username;
            PasswordBox.Password = site.Password;
            StartDirBox.Text = site.StartDirectory;
            EncodingBox.Text = site.Encoding;
            SelectComboTag(ProtocolBox, site.Protocol.ToString());
            SelectComboTag(ModeBox, site.Mode.ToString());
            SelectComboTag(TlsBox, site.TlsValidation.ToString());
            SaveSiteCheck.IsChecked = true;
            SiteNameBox.IsEnabled = true;
            SiteNameBox.Text = site.Name;
        }

        private static void SelectComboTag(ComboBox box, string tag)
        {
            foreach (var i in box.Items)
            {
                if (i is ComboBoxItem item && (item.Tag as string) == tag)
                {
                    box.SelectedItem = item;
                    return;
                }
            }
        }

        private void OnProtocolChanged(object sender, SelectionChangedEventArgs e) => UpdateDefaultPort();

        private void UpdateDefaultPort()
        {
            if (PortBox == null || ProtocolBox?.SelectedItem is not ComboBoxItem item) return;
            // Don't overwrite a port the user already typed.
            if (!string.IsNullOrWhiteSpace(PortBox.Text) && PortBox.Text != "21" && PortBox.Text != "22" && PortBox.Text != "990") return;
            PortBox.Text = (item.Tag as string) switch
            {
                "FtpsImplicit" => "990",
                "Sftp"         => "22",
                _              => "21",
            };
        }

        private void OnTogglePasswordReveal(object sender, RoutedEventArgs e)
        {
            if (ShowPasswordCheck.IsChecked == true)
            {
                PasswordPlain.Text = PasswordBox.Password;
                PasswordPlain.Visibility = Visibility.Visible;
                PasswordBox.Visibility = Visibility.Collapsed;
            }
            else
            {
                PasswordBox.Password = PasswordPlain.Text;
                PasswordBox.Visibility = Visibility.Visible;
                PasswordPlain.Visibility = Visibility.Collapsed;
            }
        }

        private void OnSaveSiteToggle(object sender, RoutedEventArgs e)
        {
            SiteNameBox.IsEnabled = SaveSiteCheck.IsChecked == true;
            if (SiteNameBox.IsEnabled && string.IsNullOrWhiteSpace(SiteNameBox.Text))
                SiteNameBox.Text = HostBox.Text;
        }

        private FtpSite? BuildSite(out string? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(HostBox.Text)) { error = "Host is required"; return null; }
            if (!int.TryParse(PortBox.Text, out var port) || port <= 0 || port > 65535) { error = "Invalid port"; return null; }

            var protoTag = (ProtocolBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Ftp";
            var modeTag = (ModeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Passive";
            var tlsTag  = (TlsBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Strict";

            return new FtpSite
            {
                Name = string.IsNullOrWhiteSpace(SiteNameBox.Text) ? HostBox.Text : SiteNameBox.Text,
                Host = HostBox.Text.Trim(),
                Port = port,
                Username = UserBox.Text,
                Password = ShowPasswordCheck.IsChecked == true ? PasswordPlain.Text : PasswordBox.Password,
                StartDirectory = string.IsNullOrWhiteSpace(StartDirBox.Text) ? "/" : StartDirBox.Text,
                Encoding = string.IsNullOrWhiteSpace(EncodingBox.Text) ? "UTF-8" : EncodingBox.Text,
                Protocol = protoTag switch { "FtpsExplicit" => FtpProtocol.FtpsExplicit, "FtpsImplicit" => FtpProtocol.FtpsImplicit, "Sftp" => FtpProtocol.Sftp, _ => FtpProtocol.Ftp },
                Mode = modeTag == "Active" ? FtpMode.Active : FtpMode.Passive,
                TlsValidation = tlsTag switch { "AcceptOnFirstUse" => TlsValidation.AcceptOnFirstUse, "AcceptAny" => TlsValidation.AcceptAny, _ => TlsValidation.Strict },
            };
        }

        private async void OnTestConnection(object sender, RoutedEventArgs e)
        {
            var site = BuildSite(out var err);
            if (site == null) { ShowTest(err ?? "Invalid input", isError: true); return; }

            ShowTest("Connecting…", isError: false, neutral: true);
            Cursor = Cursors.Wait;
            try
            {
                IRemoteClient client = site.Protocol == FtpProtocol.Sftp
                    ? new SftpClientComponent(site)
                    : new FtpClientComponent(site);
                await using (client)
                {
                    await client.ConnectAsync(default);
                    await client.ListAsync(string.IsNullOrEmpty(site.StartDirectory) ? "/" : site.StartDirectory, default);
                    await client.DisconnectAsync();
                }
                ShowTest($"OK — connected to {site.Host}:{site.Port}", isError: false);
            }
            catch (Exception ex)
            {
                ShowTest($"Failed: {ex.Message}", isError: true);
                Log.Warn("Sites", $"Test connection failed for {site.Username}@{site.Host}", ex);
            }
            finally { Cursor = null; }
        }

        private void ShowTest(string text, bool isError, bool neutral = false)
        {
            TestVisible = true;
            TestResultText.Text = text;
            var key = neutral ? "Brush.OnSurface" : (isError ? "Brush.Status.Error" : "Brush.Status.Ok");
            if (Application.Current?.Resources[key] is Brush b) TestResultText.Foreground = b;
        }

        private void OnConnect(object sender, RoutedEventArgs e)
        {
            var site = BuildSite(out var err);
            if (site == null) { ShowTest(err ?? "Invalid input", isError: true); return; }

            Result = site;
            SaveToSites = SaveSiteCheck.IsChecked == true;
            if (SaveToSites) SiteManagerComponent.Upsert(site);

            DialogResult = true;
            Close();
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
