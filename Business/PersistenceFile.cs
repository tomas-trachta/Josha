using Josha.Services;
using System.IO;

namespace Josha.Business
{
    // Shared load/save for the encrypted-flat-file persistence pattern used by
    // namespaces, bindings, bookmarks. Wraps DPAPI ciphertext in the versioned
    // envelope (PersistenceMigrator) so the format-version story stays uniform
    // with sites.dans.
    //
    // On unprotect failure the file is moved aside (BackupAndIsolate). Without
    // that, the next Save would overwrite the user's only copy with a fresh
    // empty one — losing the original credentials/bookmarks/etc. forever.
    internal static class PersistenceFile
    {
        public static string LoadDecrypted(string filePath, byte[] entropy, string logCategory)
        {
            if (!FileAnalyserComponent.FileExists(filePath))
                return string.Empty;

            byte[] rawBytes;
            try { rawBytes = FileAnalyserComponent.ReadFile(filePath); }
            catch (Exception ex)
            {
                Log.Error(logCategory, $"Failed to read {Path.GetFileName(filePath)}", ex);
                return string.Empty;
            }
            if (rawBytes is null || rawBytes.Length == 0)
                return string.Empty;

            var unwrap = PersistenceMigrator.Unwrap(rawBytes);
            byte[] ciphertext;

            switch (unwrap.Status)
            {
                case PersistenceMigrator.UnwrapStatus.Ok:
                    ciphertext = unwrap.Payload;
                    break;
                case PersistenceMigrator.UnwrapStatus.NoEnvelope:
                    // Pre-DPAPI files were AES-HWID encrypted with no envelope.
                    // We can't decrypt those, so isolate and start fresh.
                    Log.Warn(logCategory,
                        $"{Path.GetFileName(filePath)}: pre-DPAPI format; cannot read");
                    BackupAndIsolate(filePath, "legacy-format", logCategory);
                    return string.Empty;
                case PersistenceMigrator.UnwrapStatus.NewerVersion:
                    Log.Warn(logCategory,
                        $"{Path.GetFileName(filePath)}: written by newer format (v{unwrap.Version}); cannot read with v{PersistenceMigrator.CurrentVersion}");
                    BackupAndIsolate(filePath, "newer-version", logCategory);
                    return string.Empty;
                default:
                    Log.Warn(logCategory, $"{Path.GetFileName(filePath)}: truncated / corrupted; will not load");
                    BackupAndIsolate(filePath, "truncated", logCategory);
                    return string.Empty;
            }

            try
            {
                return CryptoComponent.UnprotectString(ciphertext, entropy);
            }
            catch (Exception ex)
            {
                Log.Error(logCategory, $"DPAPI unprotect failed for {Path.GetFileName(filePath)} (different user/machine?)", ex);
                BackupAndIsolate(filePath, "dpapi-failed", logCategory);
                return string.Empty;
            }
        }

        public static void SaveEncrypted(string filePath, string plaintext, byte[] entropy, string logCategory)
        {
            try
            {
                byte[] encrypted = CryptoComponent.ProtectString(plaintext, entropy);
                byte[] wrapped = PersistenceMigrator.WrapV1(encrypted);
                FileAnalyserComponent.WriteFile(filePath, wrapped);
            }
            catch (Exception ex)
            {
                Log.Error(logCategory, $"Failed to save {Path.GetFileName(filePath)}", ex);
            }
        }

        // Move the unreadable file aside so the next Save can't overwrite it.
        // Suffixed with reason + timestamp so multiple failures don't clobber
        // each other's evidence.
        private static void BackupAndIsolate(string path, string reason, string logCategory)
        {
            try
            {
                var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                var bak = $"{path}.{reason}-{stamp}.bak";
                File.Move(path, bak);
                Log.Warn(logCategory, $"Moved unreadable {Path.GetFileName(path)} to {Path.GetFileName(bak)}");
            }
            catch (Exception ex)
            {
                Log.Error(logCategory, $"Could not preserve {Path.GetFileName(path)} as backup", ex);
            }
        }
    }
}
