using Josha.Models;
using System.IO;

namespace Josha.Business.Ftp
{
    // Common surface for both FluentFTP-backed and SSH.NET-backed clients.
    // Lets the FTP/SFTP file system providers share one upload/download path.
    internal interface IRemoteClient : IAsyncDisposable
    {
        bool IsConnected { get; }

        Task ConnectAsync(CancellationToken ct);

        Task DisconnectAsync();

        Task<IReadOnlyList<RemoteEntry>> ListAsync(string path, CancellationToken ct);

        // Resume = pick up at the existing remote file's byte length (FTP REST/APPE,
        // SFTP server-side append). Ignored when the remote file doesn't exist.
        Task UploadAsync(
            Stream src,
            string remotePath,
            bool overwrite,
            bool resume,
            IProgress<long>? bytesTransferred,
            CancellationToken ct);

        Task DownloadAsync(
            string remotePath,
            Stream dst,
            IProgress<long>? bytesTransferred,
            CancellationToken ct);

        Task MkdirAsync(string remotePath, CancellationToken ct);

        Task DeleteFileAsync(string remotePath, CancellationToken ct);

        Task DeleteDirectoryAsync(string remotePath, bool recursive, CancellationToken ct);

        Task RenameAsync(string srcPath, string dstPath, CancellationToken ct);

        Task<bool> FileExistsAsync(string remotePath, CancellationToken ct);

        Task<bool> DirectoryExistsAsync(string remotePath, CancellationToken ct);

        Task<long> GetFileSizeAsync(string remotePath, CancellationToken ct);
    }
}
