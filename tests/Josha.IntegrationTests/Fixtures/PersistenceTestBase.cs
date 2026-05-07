using Josha.Business;

namespace Josha.IntegrationTests.Fixtures;

// Persistence components compute paths as `WinRoot + "josha_data"` (literal
// string concat — no Path.Combine), so we set WinRoot to a per-test temp
// dir with a trailing separator and the components transparently write
// under <tmp>/josha_data/ instead of the user's real C:\josha_data\.
//
// Tests inheriting this base should be marked [Collection("Persistence")]
// so xUnit serializes them — WinRoot is global static state.
public abstract class PersistenceTestBase : TempDirTestBase
{
    protected string DataDir => Path.Combine(TempDir, "josha_data");

    private string _savedWinRoot = null!;

    public override Task InitializeAsync()
    {
        // base sets up TempDir; we then point WinRoot at it. base is
        // synchronous-completed so reading TempDir on the next line is safe.
        var t = base.InitializeAsync();
        _savedWinRoot = DirectoryAnalyserComponent.WinRoot;
        DirectoryAnalyserComponent.WinRoot = TempDir.TrimEnd('\\') + "\\";
        return t;
    }

    public override Task DisposeAsync()
    {
        DirectoryAnalyserComponent.WinRoot = _savedWinRoot;
        return base.DisposeAsync();
    }
}
