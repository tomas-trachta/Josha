using Josha.Business;
using Josha.Services;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace Josha.Views
{
    public partial class InternalViewer : Window
    {
        // Hex view dumps the whole buffer; cap the auto-load at 8 MB so a stray
        // F3 on a 4 GB log doesn't lock the window. Anything bigger is hex-only
        // with a "first 8 MB" disclaimer; user can still open with F4.
        private const int MaxAutoLoadBytes = 8 * 1024 * 1024;

        private byte[]? _bytes;
        private string _displayName = "";
        private bool _isHex;

        public ICommand ToggleHexCommand { get; }

        public InternalViewer()
        {
            InitializeComponent();
            ToggleHexCommand = new ViewModels.RelayCommand(_ => ToggleHex());
        }

        // path = local file path (caller is responsible for downloading remote
        // files to a temp first — keeps this view free of FTP plumbing).
        internal void Load(string path, string displayName)
        {
            _displayName = displayName;
            HeaderTitle.Text = displayName;
            try
            {
                var info = new FileInfo(path);
                long len = info.Length;

                if (len > MaxAutoLoadBytes)
                {
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    _bytes = new byte[MaxAutoLoadBytes];
                    fs.ReadExactly(_bytes);
                    _isHex = true;
                    StatusText.Text = $"Hex (first {Format(MaxAutoLoadBytes)} of {Format(len)} — file truncated for view)";
                }
                else
                {
                    _bytes = File.ReadAllBytes(path);
                    _isHex = !LooksLikeText(_bytes);
                    StatusText.Text = $"{Format(len)} · {(_isHex ? "binary detected" : "text detected")}";
                }

                Render();
            }
            catch (Exception ex)
            {
                Log.Warn("Viewer", $"Load failed for {path}", ex);
                ContentText.Text = $"Couldn't read file: {ex.Message}";
                StatusText.Text = "load error";
            }
        }

        private void ToggleHex()
        {
            _isHex = !_isHex;
            Render();
        }

        private void OnToggleHexClick(object sender, RoutedEventArgs e) => ToggleHex();

        private void Render()
        {
            if (_bytes == null) return;
            HeaderModeLabel.Text = _isHex ? "HEX" : "TEXT";
            ContentText.Text = _isHex ? RenderHex(_bytes) : RenderText(_bytes);
            ContentScroller.ScrollToHome();
        }

        // BOM-sniff first; otherwise prefer UTF-8 with replacement so invalid
        // sequences in mostly-ASCII files (logs with stray bytes) still render.
        private static string RenderText(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

            var enc = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
            return enc.GetString(bytes);
        }

        // Classic hex dump: 16 bytes per line, offset / hex / printable.
        private static string RenderHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 4);
            for (int i = 0; i < bytes.Length; i += 16)
            {
                sb.Append(i.ToString("x8")).Append("  ");

                for (int j = 0; j < 16; j++)
                {
                    if (i + j < bytes.Length) sb.Append(bytes[i + j].ToString("x2")).Append(' ');
                    else sb.Append("   ");
                    if (j == 7) sb.Append(' ');
                }

                sb.Append(" |");
                for (int j = 0; j < 16 && i + j < bytes.Length; j++)
                {
                    var b = bytes[i + j];
                    sb.Append(b >= 32 && b < 127 ? (char)b : '.');
                }
                sb.Append('|').Append('\n');
            }
            return sb.ToString();
        }

        // Heuristic: >5% non-printable bytes (excluding common whitespace) → binary.
        private static bool LooksLikeText(byte[] bytes)
        {
            if (bytes.Length == 0) return true;
            var sample = Math.Min(bytes.Length, 8192);
            int nonPrintable = 0;
            for (int i = 0; i < sample; i++)
            {
                var b = bytes[i];
                if (b == 0) return false;
                if (b < 32 && b != 9 && b != 10 && b != 13) nonPrintable++;
            }
            return (double)nonPrintable / sample < 0.05;
        }

        private void OnTextWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            var delta = e.Delta > 0 ? 1 : -1;
            var size = Math.Clamp(ContentText.FontSize + delta, 9, 28);
            ContentText.FontSize = size;
            e.Handled = true;
        }

        private void OnCloseCommand(object sender, ExecutedRoutedEventArgs e) => Close();

        private static string Format(long bytes)
        {
            if (bytes < 1024) return $"{bytes:N0} B";
            double v = bytes / 1024.0;
            if (v < 1024) return $"{v:N1} KB";
            v /= 1024;
            if (v < 1024) return $"{v:N1} MB";
            v /= 1024;
            return $"{v:N2} GB";
        }
    }
}
