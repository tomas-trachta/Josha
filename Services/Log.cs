using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Channels;

namespace Josha.Services
{
    internal static class Log
    {
        private const long MaxFileBytes = 5L * 1024 * 1024;
        private const int RetentionDays = 7;

        private static readonly Channel<LogEntry> _channel = Channel.CreateUnbounded<LogEntry>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        private static string _logDir = "";
        private static bool _initialized;
        private static Task? _writerTask;

        public static string LogDirectory => _logDir;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                _logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Josha", "logs");
                Directory.CreateDirectory(_logDir);
                DeleteOldLogs();
            }
            catch
            {
                _logDir = "";
            }

            _writerTask = Task.Run(WriterLoopAsync);
            Info("App", "Log started");
        }

        public static void Info(string category, string message, Exception? ex = null)
            => Enqueue("INFO ", category, message, ex);

        public static void Warn(string category, string message, Exception? ex = null)
            => Enqueue("WARN ", category, message, ex);

        public static void Error(string category, string message, Exception? ex = null)
            => Enqueue("ERROR", category, message, ex);

        private static void Enqueue(string level, string category, string message, Exception? ex)
        {
            _channel.Writer.TryWrite(new LogEntry(DateTime.UtcNow, level, category, message, ex));
        }

        private static async Task WriterLoopAsync()
        {
            await foreach (var entry in _channel.Reader.ReadAllAsync())
            {
                if (string.IsNullOrEmpty(_logDir)) continue;

                try
                {
                    var path = GetCurrentLogPath(entry.TimestampUtc);
                    var line = FormatLine(entry);
                    await File.AppendAllTextAsync(path, line);
                }
                catch
                {
                    // A logging failure can't itself be logged — drop the entry.
                }
            }
        }

        private static string GetCurrentLogPath(DateTime timestampUtc)
        {
            var date = timestampUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            var basePath = Path.Combine(_logDir, $"diskanalyser-{date}.log");

            var current = basePath;
            int suffix = 1;
            while (File.Exists(current))
            {
                long len;
                try { len = new FileInfo(current).Length; }
                catch { break; }

                if (len < MaxFileBytes) break;
                suffix++;
                current = Path.Combine(_logDir, $"diskanalyser-{date}-{suffix}.log");
            }
            return current;
        }

        private static string FormatLine(LogEntry e)
        {
            var sb = new StringBuilder(160);
            sb.Append(e.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
            sb.Append("Z  ");
            sb.Append(e.Level);
            sb.Append("  [");
            sb.Append(e.Category);
            sb.Append("] ");
            sb.Append(e.Message);

            if (e.Exception != null)
            {
                sb.Append(Environment.NewLine);
                sb.Append("    ");
                sb.Append(e.Exception.GetType().Name);
                sb.Append(": ");
                sb.Append(e.Exception.Message);
                if (e.Exception.StackTrace != null)
                {
                    foreach (var stackLine in e.Exception.StackTrace.Split('\n'))
                    {
                        sb.Append(Environment.NewLine);
                        sb.Append("      ");
                        sb.Append(stackLine.TrimEnd('\r'));
                    }
                }
            }
            sb.Append(Environment.NewLine);
            return sb.ToString();
        }

        private static void DeleteOldLogs()
        {
            try
            {
                var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
                foreach (var path in Directory.EnumerateFiles(_logDir, "diskanalyser-*.log"))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(path) < cutoff)
                            File.Delete(path);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private sealed record LogEntry(
            DateTime TimestampUtc,
            string Level,
            string Category,
            string Message,
            Exception? Exception);
    }
}
