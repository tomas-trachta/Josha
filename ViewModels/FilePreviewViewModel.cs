using Josha.Business;
using Josha.Services;
using System.IO;
using System.Text;
using System.Windows.Media.Imaging;

namespace Josha.ViewModels
{
    internal enum PreviewKind { Text, Image, Unsupported, Error }

    internal sealed class FilePreviewViewModel : BaseViewModel
    {
        // Hard cap so a stray Ctrl+Q on a 4 GB log doesn't hang the pane.
        private const int MaxBytesAutoLoad = 2 * 1024 * 1024;

        private static readonly HashSet<string> ImageExt = new(StringComparer.OrdinalIgnoreCase)
        {
            "jpg", "jpeg", "png", "gif", "bmp", "ico", "webp", "tif", "tiff"
        };

        private static readonly HashSet<string> TextExt = new(StringComparer.OrdinalIgnoreCase)
        {
            "txt", "md", "log", "json", "xml", "yaml", "yml", "ini", "conf", "cfg",
            "cs", "csproj", "sln", "csv", "tsv", "html", "htm", "css", "js", "ts",
            "tsx", "jsx", "py", "rb", "go", "rs", "java", "kt", "kts", "sh", "bat",
            "ps1", "psm1", "sql", "toml", "lua", "vue", "svelte", "tex", "rst",
            "gitignore", "editorconfig", "dockerfile", "env",
        };

        public string Title { get; }
        public PreviewKind Kind { get; private set; }
        public string? TextContent { get; private set; }
        public BitmapSource? ImageContent { get; private set; }
        public string SubtitleText { get; private set; } = "";

        private FilePreviewViewModel(string title) { Title = title; }

        public static async Task<FilePreviewViewModel?> LoadAsync(IFileSystemProvider fs, FileRowViewModel row)
        {
            if (row.IsDirectory) return null;

            var preview = new FilePreviewViewModel(row.Name);
            try
            {
                var ext = (row.Extension ?? "").ToLowerInvariant();
                var isImage = ImageExt.Contains(ext);
                var isText = TextExt.Contains(ext);

                if (!isImage && !isText)
                {
                    preview.Kind = PreviewKind.Unsupported;
                    preview.SubtitleText = $".{ext} — preview not supported (use F3 / F4 to open)";
                    return preview;
                }

                var size = row.SizeBytes ?? -1;
                if (size > MaxBytesAutoLoad)
                {
                    preview.Kind = PreviewKind.Unsupported;
                    preview.SubtitleText = $"Too large for preview ({Format(size)})";
                    return preview;
                }

                byte[] bytes;
                if (fs.IsRemote)
                {
                    await using var stream = await fs.OpenReadAsync(row.FullPath);
                    using var ms = new MemoryStream();
                    await stream.CopyToAsync(ms);
                    bytes = ms.ToArray();
                }
                else
                {
                    bytes = await File.ReadAllBytesAsync(row.FullPath);
                }

                if (isImage)
                {
                    using var ms = new MemoryStream(bytes);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();
                    preview.ImageContent = bmp;
                    preview.Kind = PreviewKind.Image;
                    preview.SubtitleText = $"{bmp.PixelWidth}×{bmp.PixelHeight} · {Format(bytes.Length)}";
                }
                else
                {
                    preview.TextContent = DecodeText(bytes);
                    preview.Kind = PreviewKind.Text;
                    preview.SubtitleText = $"{Format(bytes.Length)} · text";
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Preview", $"Load failed for {row.FullPath}", ex);
                preview.Kind = PreviewKind.Error;
                preview.SubtitleText = $"Couldn't load: {ex.Message}";
            }
            return preview;
        }

        private static string DecodeText(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            var enc = new UTF8Encoding(false, throwOnInvalidBytes: false);
            return enc.GetString(bytes);
        }

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
