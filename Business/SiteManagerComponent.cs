using Josha.Models;
using Josha.Services;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Josha.Business
{
    // Encrypted FTP site list at C:\josha_data\sites.dans.
    //
    // Unlike namespaces / bookmarks (hardware-id-keyed AES via CryptoComponent), this
    // file holds live credentials. Per spec D2 it uses Windows DPAPI
    // (DataProtectionScope.CurrentUser): the OS pins the protection key to the user
    // profile + machine, so the file is unreadable if copied to another user/machine
    // even if the disk leaves the device.
    //
    // The same versioned envelope (PersistenceMigrator) is wrapped around the DPAPI
    // ciphertext so the format-version story stays uniform with the other .dans files.
    internal static class SiteManagerComponent
    {
        private const string FileName = "sites.dans";
        private const string LogCat = "Sites";
        private const byte FlagDpapi = 0x01;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false,
        };

        // Serializes Load/Save/Upsert/Delete. Without this, the cert callback's
        // fingerprint pin (off-thread) and the UI thread's LastUsedUtc upsert
        // race on read-modify-write and lose updates.
        private static readonly object _ioLock = new();

        // Per-app DPAPI entropy. Mixed into Protect/Unprotect so a different
        // process running as the same user can't decrypt sites.dans by calling
        // ProtectedData.Unprotect with a null entropy.
        private static readonly byte[] DpapiEntropy =
            Encoding.UTF8.GetBytes("Josha/sites/v1");

        private static string GetDirPath() =>
            DirectoryAnalyserComponent.WinRoot + "josha_data";

        private static string GetFilePath() => Path.Combine(GetDirPath(), FileName);

        public static List<FtpSite> Load()
        {
            lock (_ioLock) return LoadCore();
        }

        public static void Save(IEnumerable<FtpSite> sites)
        {
            lock (_ioLock) SaveCore(sites);
        }

        public static void Upsert(FtpSite site)
        {
            lock (_ioLock)
            {
                var sites = LoadCore();
                var idx = sites.FindIndex(s => s.Id == site.Id);
                if (idx >= 0) sites[idx] = site;
                else sites.Add(site);
                SaveCore(sites);
            }
        }

        public static void Delete(Guid id)
        {
            lock (_ioLock)
            {
                var sites = LoadCore();
                if (sites.RemoveAll(s => s.Id == id) > 0)
                    SaveCore(sites);
            }
        }

        private static List<FtpSite> LoadCore()
        {
            var path = GetFilePath();
            if (!FileAnalyserComponent.FileExists(path))
                return new List<FtpSite>();

            byte[] rawBytes;
            try { rawBytes = FileAnalyserComponent.ReadFile(path); }
            catch (Exception ex)
            {
                Log.Error(LogCat, $"Failed to read {FileName}", ex);
                return new List<FtpSite>();
            }
            if (rawBytes is null || rawBytes.Length == 0)
                return new List<FtpSite>();

            var unwrap = PersistenceMigrator.Unwrap(rawBytes);
            if (unwrap.Status == PersistenceMigrator.UnwrapStatus.NewerVersion)
            {
                Log.Warn(LogCat,
                    $"{FileName}: written by newer format (v{unwrap.Version}); cannot read with v{PersistenceMigrator.CurrentVersion}");
                BackupAndIsolate(path, "newer-version");
                return new List<FtpSite>();
            }
            if (unwrap.Status == PersistenceMigrator.UnwrapStatus.Truncated)
            {
                Log.Warn(LogCat, $"{FileName}: truncated / corrupted; will not load");
                BackupAndIsolate(path, "truncated");
                return new List<FtpSite>();
            }

            byte[] plaintext;
            try
            {
                plaintext = ProtectedData.Unprotect(unwrap.Payload, DpapiEntropy, DataProtectionScope.CurrentUser);
            }
            catch (Exception ex)
            {
                // Critical: if we just `return new List` here, the next Upsert
                // overwrites the user's site list with a fresh single-entry one
                // and the original credentials are gone forever. Move the file
                // aside so a manual restore is possible, and toast the user.
                Log.Error(LogCat, $"DPAPI unprotect failed for {FileName} (different user/machine?)", ex);
                BackupAndIsolate(path, "dpapi-failed");
                try
                {
                    Services.AppServices.Toast?.Error(
                        $"Saved sites couldn't be decrypted on this machine — preserved as {FileName}.bak");
                }
                catch { }
                return new List<FtpSite>();
            }

            try
            {
                var json = Encoding.UTF8.GetString(plaintext);
                var sites = JsonSerializer.Deserialize<List<FtpSite>>(json, JsonOptions);
                return sites ?? new List<FtpSite>();
            }
            catch (Exception ex)
            {
                Log.Error(LogCat, $"Deserialize failed for {FileName}", ex);
                BackupAndIsolate(path, "json-corrupt");
                return new List<FtpSite>();
            }
        }

        private static void SaveCore(IEnumerable<FtpSite> sites)
        {
            var dir = GetDirPath();
            if (!DirectoryAnalyserComponent.DirectoryExists(dir))
                DirectoryAnalyserComponent.CreateDirectory(dir);

            try
            {
                var json = JsonSerializer.Serialize(sites, JsonOptions);
                var plaintext = Encoding.UTF8.GetBytes(json);
                var encrypted = ProtectedData.Protect(plaintext, DpapiEntropy, DataProtectionScope.CurrentUser);
                var wrapped = PersistenceMigrator.WrapV1(encrypted);
                FileAnalyserComponent.WriteFile(GetFilePath(), wrapped);
            }
            catch (Exception ex)
            {
                Log.Error(LogCat, $"Failed to save {FileName}", ex);
            }
        }

        // Move the unreadable file aside so the next Save can't blow it away.
        // Suffixed with reason + timestamp so multiple failures don't overwrite
        // each other's evidence.
        private static void BackupAndIsolate(string path, string reason)
        {
            try
            {
                var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                var bak = $"{path}.{reason}-{stamp}.bak";
                File.Move(path, bak);
                Log.Warn(LogCat, $"Moved unreadable {FileName} to {Path.GetFileName(bak)}");
            }
            catch (Exception ex)
            {
                Log.Error(LogCat, $"Could not preserve {FileName} as backup", ex);
            }
        }
    }
}
