using Josha.Models.Git;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Josha.Business.Git
{
    // Read-only git.exe wrapper: log / branch / diff-tree only, no mutating
    // commands. Every call shells out fresh — this is a review tool, not a
    // long-lived repo handle, so there's no client/session to keep warm.
    internal static class GitComponent
    {
        private const string GitExe = "git";
        private const int LogPageSize = 500;

        internal static bool TryFindRepositoryRoot(string startPath, out string repoRoot)
        {
            var result = RunGit(startPath, "rev-parse", "--show-toplevel");
            if (result.ExitCode == 0 && result.StandardOutput.Trim().Length > 0)
            {
                repoRoot = result.StandardOutput.Trim().Replace('/', Path.DirectorySeparatorChar);
                return true;
            }

            repoRoot = "";
            return false;
        }

        internal static List<GitBranch> GetBranches(string repoRoot)
        {
            var result = RunGit(repoRoot, "branch", "-a", "--format=%(refname:short)%09%(HEAD)");
            var branches = new List<GitBranch>();
            if (result.ExitCode != 0) return branches;

            foreach (var line in SplitLines(result.StandardOutput))
            {
                var parts = line.Split('\t');
                if (parts.Length < 2) continue;

                var name = parts[0];
                // "-> origin/main" symbolic-ref lines aren't real branches.
                if (name.Contains(" -> ", StringComparison.Ordinal)) continue;

                branches.Add(new GitBranch(name, name.StartsWith("remotes/", StringComparison.Ordinal), parts[1] == "*"));
            }
            return branches;
        }

        internal static List<GitCommit> GetLog(string repoRoot, string? branch)
        {
            // %b (body) goes last — it's free-form commit text, so keeping it
            // after every delimited field means nothing downstream depends on
            // it not containing anything unexpected (newlines are fine, only
            // the \x1f/\x1e control chars themselves would break parsing).
            const string format = "%H%x1f%h%x1f%an%x1f%aI%x1f%s%x1f%P%x1f%b%x1e";
            var args = new List<string> { "log", $"--format={format}", $"-n{LogPageSize}" };
            if (!string.IsNullOrWhiteSpace(branch)) args.Add(branch);

            var result = RunGit(repoRoot, args.ToArray());
            var commits = new List<GitCommit>();
            if (result.ExitCode != 0) return commits;

            foreach (var record in result.StandardOutput.Split('\x1e', StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = record.TrimStart('\n', '\r').Split('\x1f');
                if (fields.Length < 7) continue;

                var parents = fields[5].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (!DateTimeOffset.TryParse(fields[3], out var date)) date = DateTimeOffset.MinValue;
                var body = fields[6].Trim('\n', '\r');

                commits.Add(new GitCommit(fields[0], fields[1], fields[2], date, fields[4], body, parents));
            }
            return commits;
        }

        internal static List<GitFileChange> GetCommitFiles(string repoRoot, string hash)
        {
            var result = RunGit(repoRoot, "diff-tree", "--no-commit-id", "--name-status", "-r", "-M", hash);
            var files = new List<GitFileChange>();
            if (result.ExitCode != 0) return files;

            foreach (var line in SplitLines(result.StandardOutput))
            {
                var parts = line.Split('\t');
                if (parts.Length < 2) continue;

                var kind = ParseChangeType(parts[0][0]);
                files.Add(parts[0][0] is 'R' or 'C' && parts.Length >= 3
                    ? new GitFileChange(parts[2], parts[1], kind)
                    : new GitFileChange(parts[1], null, kind));
            }
            return files;
        }

        // fullFile requests the whole file as context (not just a few lines
        // around each change) — git has no direct "show entire file" switch,
        // but -U with a context count larger than the file itself achieves
        // the same result, since git clamps context to what's available.
        internal static string GetRawDiff(string repoRoot, string hash, string filePath, bool fullFile = false)
        {
            var args = new List<string> { "diff-tree", "-p", "--no-color", "-M" };
            if (fullFile) args.Add("-U100000");
            args.Add(hash);
            args.Add("--");
            args.Add(filePath);

            var result = RunGit(repoRoot, args.ToArray());
            return result.ExitCode == 0 ? result.StandardOutput : "";
        }

        // Writes the blob for <path> as it existed at <hash> to a temp file so
        // it can be handed to the OS's default handler for its extension.
        // Copies the raw byte stream rather than going through RunGit's UTF8
        // text pipe — that would corrupt any binary file (images, PDFs, etc).
        internal static string? ExtractBlobToTempFile(string repoRoot, string hash, string relativePath)
        {
            var psi = new ProcessStartInfo
            {
                FileName = GitExe,
                WorkingDirectory = repoRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("show");
            psi.ArgumentList.Add($"{hash}:{relativePath.Replace('\\', '/')}");

            string tempPath;
            try
            {
                using var process = Process.Start(psi);
                if (process == null) return null;

                var tempDir = Path.Combine(Path.GetTempPath(), "Josha", "git-view", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);

                // Short hash goes in the filename (not just the temp folder)
                // so it's visible in the app's title bar / taskbar / recent-
                // files list — a bare original filename looks exactly like
                // the working-copy version and invites editing the wrong one.
                var shortHash = hash[..Math.Min(7, hash.Length)];
                var fileName = Path.GetFileNameWithoutExtension(relativePath);
                var extension = Path.GetExtension(relativePath);
                tempPath = Path.Combine(tempDir, $"{fileName}@{shortHash}{extension}");

                using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                    process.StandardOutput.BaseStream.CopyTo(fileStream);

                process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    File.Delete(tempPath);
                    return null;
                }
            }
            catch
            {
                return null;
            }
            return tempPath;
        }

        private static GitChangeType ParseChangeType(char statusChar) => statusChar switch
        {
            'A' => GitChangeType.Added,
            'M' => GitChangeType.Modified,
            'D' => GitChangeType.Deleted,
            'R' => GitChangeType.Renamed,
            'C' => GitChangeType.Copied,
            'T' => GitChangeType.TypeChanged,
            'U' => GitChangeType.Unmerged,
            _ => GitChangeType.Unknown,
        };

        private static IEnumerable<string> SplitLines(string text) =>
            text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r'));

        private readonly record struct ProcessResult(int ExitCode, string StandardOutput, string StandardError);

        // Reads stdout/stderr concurrently before waiting on exit — sequential
        // ReadToEnd() calls deadlock once either pipe's OS buffer fills for a
        // large diff (git blocks writing while nothing drains the other pipe).
        private static ProcessResult RunGit(string workingDirectory, params string[] args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = GitExe,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var arg in args) psi.ArgumentList.Add(arg);

            try
            {
                using var process = Process.Start(psi);
                if (process == null) return new ProcessResult(-1, "", "failed to start git");

                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                Task.WaitAll(stdoutTask, stderrTask);
                process.WaitForExit();

                return new ProcessResult(process.ExitCode, stdoutTask.Result, stderrTask.Result);
            }
            catch (Exception ex)
            {
                return new ProcessResult(-1, "", ex.Message);
            }
        }
    }
}
