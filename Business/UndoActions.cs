using Vanara.Windows.Shell;

namespace Josha.Business
{
    // A single reversible local-filesystem action recorded by UndoBufferService.
    // Remote (FTP/SFTP) operations are never recorded — the round-trip cost and
    // the chance of remote state drifting between the action and the undo make
    // it unsafe to reverse blindly.
    internal interface IUndoableAction
    {
        string Description { get; }

        Task<FileOpsComponent.OpResult> UndoAsync(CancellationToken ct = default);

        // Called when the action is dropped from the buffer without ever being
        // undone (evicted past the size cap, or discarded at app shutdown/
        // startup). Most actions have nothing to clean up; PermanentDeleteUndoAction
        // uses this to actually free the staged copy.
        void Discard();
    }

    internal sealed class MoveUndoAction : IUndoableAction
    {
        private readonly IFileSystemProvider _provider;
        private readonly string _movedToPath;
        private readonly string _originalPath;

        public MoveUndoAction(IFileSystemProvider provider, string movedToPath, string originalPath)
        {
            _provider = provider;
            _movedToPath = movedToPath;
            _originalPath = originalPath;
        }

        public string Description => $"Move '{System.IO.Path.GetFileName(_originalPath)}'";

        public Task<FileOpsComponent.OpResult> UndoAsync(CancellationToken ct = default) =>
            _provider.MoveAsync(_movedToPath, _originalPath, bytesCopied: null, overwrite: false, ct);

        public void Discard() { }
    }

    internal sealed class RenameUndoAction : IUndoableAction
    {
        private readonly IFileSystemProvider _provider;
        private readonly string _renamedToPath;
        private readonly string _originalName;

        public RenameUndoAction(IFileSystemProvider provider, string renamedToPath, string originalName)
        {
            _provider = provider;
            _renamedToPath = renamedToPath;
            _originalName = originalName;
        }

        public string Description => $"Rename '{System.IO.Path.GetFileName(_renamedToPath)}'";

        public Task<FileOpsComponent.OpResult> UndoAsync(CancellationToken ct = default) =>
            _provider.RenameAsync(_renamedToPath, _originalName, ct);

        public void Discard() { }
    }

    // Recycle-bin delete undo. The OS recycle bin already keeps the item's
    // original location as shell metadata, so restoring only needs that path —
    // no need to track a bin-specific handle at delete time.
    internal sealed class DeleteUndoAction : IUndoableAction
    {
        private readonly string _originalPath;

        public DeleteUndoAction(string originalPath)
        {
            _originalPath = originalPath;
        }

        public string Description => $"Delete '{System.IO.Path.GetFileName(_originalPath)}'";

        public Task<FileOpsComponent.OpResult> UndoAsync(CancellationToken ct = default)
        {
            try
            {
                var item = RecycleBin.GetItemFromOriginalPath(_originalPath);
                if (item == null)
                    return Task.FromResult(FileOpsComponent.OpResult.Fail("Item not found in Recycle Bin"));

                RecycleBin.Restore(item, hideUI: true);
                SnapshotComponent.NotifySnapshotChanged();
                return Task.FromResult(FileOpsComponent.OpResult.Ok());
            }
            catch (Exception ex)
            {
                return Task.FromResult(FileOpsComponent.OpResult.Fail(ex.Message));
            }
        }

        public void Discard() { }
    }

    // Shift+Delete undo. Permanent delete has no OS-level recovery mechanism
    // (unlike the Recycle Bin, which keeps its own restore metadata), so the
    // item is instead moved into a per-delete slot under a local staging
    // folder (FileOpsComponent.DeleteToStaging) and only actually removed from
    // disk once this action falls out of the undo buffer — UndoBufferService.Push
    // evicts the oldest action past the size cap and calls Discard on it.
    // AppServices.Initialize also wipes any staged leftovers at startup, since
    // a fresh in-memory undo buffer can never reference last session's slots.
    internal sealed class PermanentDeleteUndoAction : IUndoableAction
    {
        private readonly string _stagingPath;
        private readonly string _originalPath;
        private bool _consumed;

        public PermanentDeleteUndoAction(string stagingPath, string originalPath)
        {
            _stagingPath = stagingPath;
            _originalPath = originalPath;
        }

        public string Description => $"Delete '{System.IO.Path.GetFileName(_originalPath)}'";

        public Task<FileOpsComponent.OpResult> UndoAsync(CancellationToken ct = default)
        {
            var result = FileOpsComponent.RestoreFromStaging(_stagingPath, _originalPath);
            if (result.Success) _consumed = true;
            return Task.FromResult(result);
        }

        // Only reachable via buffer eviction, since UndoLastAsync already
        // removes the action from the buffer before calling UndoAsync — a
        // successful restore just means there's nothing left to purge.
        public void Discard()
        {
            if (_consumed) return;
            FileOpsComponent.PurgeStaged(_stagingPath);
        }
    }
}
