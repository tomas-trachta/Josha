using Josha.Models;
using System.IO;

namespace Josha.Business
{
    // Unified record for one entry returned by IFileSystemProvider.EnumerateAsync.
    // Local provider populates from FileInfo / DirectoryInfo; remote providers
    // build from RemoteEntry. The pane VM doesn't need to know which.
    internal sealed class FsEntry
    {
        public string Name { get; init; } = "";
        public string FullPath { get; init; } = "";
        public bool IsDirectory { get; init; }
        public long? Size { get; init; }
        public DateTime? ModifiedUtc { get; init; }
    }

    internal interface IFileSystemProvider
    {
        // Stable identity of this provider — local file system shares the same
        // identity across panes; each remote site has its own. Used by F5/F6
        // routing to detect cross-provider transfers (= upload/download) vs.
        // intra-provider transfers (= copy/move).
        string ProviderId { get; }

        bool IsRemote { get; }

        // Disk Usage (TreeGraphControl) requires a snapshot; remote providers
        // can't supply one. Pane UI greys Ctrl+3 when this is false.
        bool SupportsTreeGraph { get; }

        Task<IReadOnlyList<FsEntry>> EnumerateAsync(string path, CancellationToken ct = default);

        Task<FileOpsComponent.OpResult> CopyAsync(
            string srcPath, string dstPath,
            IProgress<long>? bytesCopied = null,
            bool overwrite = false,
            CancellationToken ct = default);

        Task<FileOpsComponent.OpResult> MoveAsync(
            string srcPath, string dstPath,
            IProgress<long>? bytesCopied = null,
            bool overwrite = false,
            CancellationToken ct = default);

        Task<FileOpsComponent.OpResult> RenameAsync(string srcPath, string newName, CancellationToken ct = default);

        Task<FileOpsComponent.OpResult> CreateDirectoryAsync(string parentPath, string name, CancellationToken ct = default);

        Task<FileOpsComponent.OpResult> DeleteAsync(string path, bool toRecycle, CancellationToken ct = default);

        Task<Stream> OpenReadAsync(string path, CancellationToken ct = default);

        Task<Stream> OpenWriteAsync(string path, bool overwrite, CancellationToken ct = default);

        // Optional cross-provider transfer hooks. Concrete providers override when
        // they know how to ingest from / push to a foreign source efficiently —
        // e.g. FTP provider's UploadFromLocalAsync streams a local file straight
        // into a STOR command instead of round-tripping through OpenWriteAsync.
        Task<FileOpsComponent.OpResult> ImportFromAsync(
            IFileSystemProvider src, string srcPath, string dstPath,
            IProgress<long>? bytesCopied,
            bool overwrite,
            CancellationToken ct);
    }
}
