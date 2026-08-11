using Josha.Business;

namespace Josha.Services
{
    // Ctrl+Z target: a bounded stack of recently reversible local-filesystem
    // actions (move, rename, recycle-bin delete). All push/pop calls happen on
    // the UI thread (same convention as FileOperationQueue.Jobs), so no lock.
    internal sealed class UndoBufferService
    {
        private const int MaxActions = 20;

        private readonly LinkedList<IUndoableAction> _actions = new();

        public bool CanUndo => _actions.Count > 0;

        public void Push(IUndoableAction action)
        {
            _actions.AddFirst(action);
            while (_actions.Count > MaxActions)
            {
                var evicted = _actions.Last!.Value;
                _actions.RemoveLast();
                evicted.Discard();
            }
        }

        public async Task<(bool success, string description, string? error)> UndoLastAsync(CancellationToken ct = default)
        {
            if (_actions.First == null) return (false, "", null);

            var action = _actions.First.Value;
            _actions.RemoveFirst();

            var result = await action.UndoAsync(ct).ConfigureAwait(true);
            return (result.Success, action.Description, result.Error);
        }
    }
}
