using Xunit;

namespace Josha.IntegrationTests.Fixtures;

// Per-test scratch directory under %TEMP%\josha-tests\<guid>\. Subclasses
// inherit IAsyncLifetime so xUnit creates the dir before each test and
// best-effort deletes it afterwards.
public abstract class TempDirTestBase : IAsyncLifetime
{
    protected string TempDir { get; private set; } = null!;

    public virtual Task InitializeAsync()
    {
        TempDir = Path.Combine(Path.GetTempPath(), "josha-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempDir);
        return Task.CompletedTask;
    }

    public virtual Task DisposeAsync()
    {
        // Best-effort — a leaked file handle (FileSystemWatcher, antivirus
        // scan) shouldn't fail the test. The test result is the source of truth.
        try { Directory.Delete(TempDir, recursive: true); } catch { }
        return Task.CompletedTask;
    }

    protected string TempPath(params string[] parts) =>
        Path.Combine(new[] { TempDir }.Concat(parts).ToArray());

    protected async Task<string> WriteFileAsync(string relPath, string content)
    {
        var full = TempPath(relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, content);
        return full;
    }
}
