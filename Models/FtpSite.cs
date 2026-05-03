using System;
using System.Collections.Generic;

namespace Josha.Models
{
    internal sealed class FtpSite
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "";
        public string Host { get; set; } = "";
        public int Port { get; set; } = 21;
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string StartDirectory { get; set; } = "/";
        public FtpProtocol Protocol { get; set; } = FtpProtocol.Ftp;
        public FtpMode Mode { get; set; } = FtpMode.Passive;
        public string Encoding { get; set; } = "UTF-8";
        public TlsValidation TlsValidation { get; set; } = TlsValidation.Strict;
        public string? PinnedFingerprint { get; set; }
        public List<string> AsciiExtensions { get; set; } = new();
        public DateTime LastUsedUtc { get; set; }
    }
}
