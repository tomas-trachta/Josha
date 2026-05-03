namespace Josha.Services
{
    // Envelope: 3-byte magic "DAS" + 1-byte version + 1-byte flags + 3 reserved + N-byte
    // encrypted payload. Magic prefix lets Unwrap distinguish a versioned file from a
    // legacy raw CryptoComponent ciphertext (which can begin with any byte).
    internal static class PersistenceMigrator
    {
        public const byte CurrentVersion = 1;

        private const int HeaderSize = 8;
        private const byte Magic0 = 0x44; // 'D'
        private const byte Magic1 = 0x41; // 'A'
        private const byte Magic2 = 0x53; // 'S'

        public enum UnwrapStatus
        {
            Ok,
            NoEnvelope,
            NewerVersion,
            Truncated,
        }

        public sealed record UnwrapResult(
            UnwrapStatus Status,
            byte Version,
            byte Flags,
            byte[] Payload);

        public static byte[] WrapV1(byte[] encryptedPayload)
        {
            ArgumentNullException.ThrowIfNull(encryptedPayload);

            var output = new byte[HeaderSize + encryptedPayload.Length];
            output[0] = Magic0;
            output[1] = Magic1;
            output[2] = Magic2;
            output[3] = CurrentVersion;
            Buffer.BlockCopy(encryptedPayload, 0, output, HeaderSize, encryptedPayload.Length);
            return output;
        }

        public static UnwrapResult Unwrap(byte[] fileBytes)
        {
            ArgumentNullException.ThrowIfNull(fileBytes);

            if (fileBytes.Length == 0)
                return new UnwrapResult(UnwrapStatus.Truncated, 0, 0, fileBytes);

            if (fileBytes.Length < HeaderSize ||
                fileBytes[0] != Magic0 || fileBytes[1] != Magic1 || fileBytes[2] != Magic2)
            {
                return new UnwrapResult(UnwrapStatus.NoEnvelope, 0, 0, fileBytes);
            }

            var version = fileBytes[3];
            var flags = fileBytes[4];

            if (version == 0)
                return new UnwrapResult(UnwrapStatus.Truncated, version, flags, fileBytes);

            if (version > CurrentVersion)
                return new UnwrapResult(UnwrapStatus.NewerVersion, version, flags, fileBytes);

            var payload = new byte[fileBytes.Length - HeaderSize];
            Buffer.BlockCopy(fileBytes, HeaderSize, payload, 0, payload.Length);
            return new UnwrapResult(UnwrapStatus.Ok, version, flags, payload);
        }
    }
}
