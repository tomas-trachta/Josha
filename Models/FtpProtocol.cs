namespace Josha.Models
{
    internal enum FtpProtocol
    {
        Ftp,            // plain FTP, port 21 default
        FtpsExplicit,   // FTP + AUTH TLS, port 21 default
        FtpsImplicit,   // FTPS implicit, port 990 default
        Sftp,           // SSH-based, port 22 default
    }

    internal enum FtpMode
    {
        Passive,
        Active,
    }

    internal enum TlsValidation
    {
        Strict,           // reject self-signed / expired
        AcceptOnFirstUse, // pin fingerprint, prompt once
        AcceptAny,        // warn each connect, accept everything
    }
}
