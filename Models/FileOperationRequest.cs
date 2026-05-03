using Josha.Business;

namespace Josha.Models
{
    internal enum FileOperationKind { Copy, Move }

    // Describes a single queueable file op. The queue runner reads this and
    // dispatches to the appropriate provider (intra-provider Copy/Move vs.
    // cross-provider ImportFromAsync + optional source delete).
    internal sealed class FileOperationRequest
    {
        public required FileOperationKind Kind { get; init; }
        public required IFileSystemProvider SrcProvider { get; init; }
        public required IFileSystemProvider DstProvider { get; init; }
        public required string SrcPath { get; init; }
        public required string DstPath { get; init; }
        public required string DisplayName { get; init; }
        public bool Overwrite { get; init; }
        // Hint only — used for ETA / status; not authoritative. Set to -1 when
        // the source is a directory (recursive size is too costly to pre-scan).
        public long SizeHint { get; init; } = -1;

        // Optional refresh hooks; the queue calls each after the request
        // completes (success or fail) so the affected pane VMs can re-list.
        public Action? OnComplete { get; init; }
    }
}
