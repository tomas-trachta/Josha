using System.Security.Cryptography;
using System.Text;

namespace Josha.Business
{
    // Thin wrapper over Windows DPAPI (CurrentUser scope). Each caller passes
    // its own entropy so a different component running as the same user can't
    // read another's file by calling ProtectedData.Unprotect with a null
    // entropy. The protection key is managed by the OS and tied to the user
    // profile + machine — files are unreadable if the disk leaves the device
    // or if the profile is copied to another user/machine.
    internal static class CryptoComponent
    {
        public static byte[] Protect(byte[] plaintext, byte[] entropy)
            => ProtectedData.Protect(plaintext, entropy, DataProtectionScope.CurrentUser);

        public static byte[] Unprotect(byte[] ciphertext, byte[] entropy)
            => ProtectedData.Unprotect(ciphertext, entropy, DataProtectionScope.CurrentUser);

        public static byte[] ProtectString(string plaintext, byte[] entropy)
            => Protect(Encoding.UTF8.GetBytes(plaintext), entropy);

        public static string UnprotectString(byte[] ciphertext, byte[] entropy)
            => Encoding.UTF8.GetString(Unprotect(ciphertext, entropy));
    }
}
