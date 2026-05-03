using System.IO;
using System.Windows.Media;

namespace Josha.Views;

/// <summary>
/// Maps file extensions to icon colors for the tree graph control.
/// </summary>
internal static class FileIconMap
{
    internal readonly record struct FileIconStyle(Brush BodyBrush, Brush FoldBrush, string Label);

    private static readonly Dictionary<string, FileIconStyle> ExtensionMap;

    // Category brushes (all frozen for thread-safety / perf)
    // Default (generic file)
    private static readonly Brush DefaultBody = Freeze(Color.FromRgb(144, 202, 249));
    private static readonly Brush DefaultFold = Freeze(Color.FromRgb(66, 165, 245));

    // Images — green
    private static readonly Brush ImageBody = Freeze(Color.FromRgb(165, 214, 167));
    private static readonly Brush ImageFold = Freeze(Color.FromRgb(102, 187, 106));

    // Documents — red/pink
    private static readonly Brush DocBody = Freeze(Color.FromRgb(239, 154, 154));
    private static readonly Brush DocFold = Freeze(Color.FromRgb(239, 83, 80));

    // Code — purple
    private static readonly Brush CodeBody = Freeze(Color.FromRgb(206, 147, 216));
    private static readonly Brush CodeFold = Freeze(Color.FromRgb(171, 71, 188));

    // Web/markup — orange
    private static readonly Brush WebBody = Freeze(Color.FromRgb(255, 204, 128));
    private static readonly Brush WebFold = Freeze(Color.FromRgb(255, 167, 38));

    // Text/config — gray
    private static readonly Brush TextBody = Freeze(Color.FromRgb(224, 224, 224));
    private static readonly Brush TextFold = Freeze(Color.FromRgb(158, 158, 158));

    // Archives — amber
    private static readonly Brush ArchiveBody = Freeze(Color.FromRgb(255, 224, 130));
    private static readonly Brush ArchiveFold = Freeze(Color.FromRgb(255, 179, 0));

    // Audio — pink
    private static readonly Brush AudioBody = Freeze(Color.FromRgb(244, 143, 177));
    private static readonly Brush AudioFold = Freeze(Color.FromRgb(236, 64, 122));

    // Video — deep purple
    private static readonly Brush VideoBody = Freeze(Color.FromRgb(179, 157, 219));
    private static readonly Brush VideoFold = Freeze(Color.FromRgb(126, 87, 194));

    // Executables/scripts — teal
    private static readonly Brush ExeBody = Freeze(Color.FromRgb(128, 203, 196));
    private static readonly Brush ExeFold = Freeze(Color.FromRgb(38, 166, 154));

    // Data/database — cyan
    private static readonly Brush DataBody = Freeze(Color.FromRgb(128, 222, 234));
    private static readonly Brush DataFold = Freeze(Color.FromRgb(38, 198, 218));

    // Font — indigo
    private static readonly Brush FontBody = Freeze(Color.FromRgb(159, 168, 218));
    private static readonly Brush FontFold = Freeze(Color.FromRgb(92, 107, 192));

    static FileIconMap()
    {
        ExtensionMap = new Dictionary<string, FileIconStyle>(StringComparer.OrdinalIgnoreCase)
        {
            // Images
            [".png"]  = new(ImageBody, ImageFold, "PNG"),
            [".jpg"]  = new(ImageBody, ImageFold, "JPG"),
            [".jpeg"] = new(ImageBody, ImageFold, "JPG"),
            [".gif"]  = new(ImageBody, ImageFold, "GIF"),
            [".bmp"]  = new(ImageBody, ImageFold, "BMP"),
            [".svg"]  = new(ImageBody, ImageFold, "SVG"),
            [".ico"]  = new(ImageBody, ImageFold, "ICO"),
            [".webp"] = new(ImageBody, ImageFold, "WBP"),
            [".tiff"] = new(ImageBody, ImageFold, "TIF"),
            [".tif"]  = new(ImageBody, ImageFold, "TIF"),
            [".raw"]  = new(ImageBody, ImageFold, "RAW"),
            [".psd"]  = new(ImageBody, ImageFold, "PSD"),

            // Documents
            [".pdf"]  = new(DocBody, DocFold, "PDF"),
            [".doc"]  = new(DocBody, DocFold, "DOC"),
            [".docx"] = new(DocBody, DocFold, "DOC"),
            [".xls"]  = new(DocBody, DocFold, "XLS"),
            [".xlsx"] = new(DocBody, DocFold, "XLS"),
            [".ppt"]  = new(DocBody, DocFold, "PPT"),
            [".pptx"] = new(DocBody, DocFold, "PPT"),
            [".odt"]  = new(DocBody, DocFold, "ODT"),
            [".ods"]  = new(DocBody, DocFold, "ODS"),
            [".rtf"]  = new(DocBody, DocFold, "RTF"),

            // Code
            [".cs"]   = new(CodeBody, CodeFold, "C#"),
            [".js"]   = new(CodeBody, CodeFold, "JS"),
            [".ts"]   = new(CodeBody, CodeFold, "TS"),
            [".tsx"]  = new(CodeBody, CodeFold, "TSX"),
            [".jsx"]  = new(CodeBody, CodeFold, "JSX"),
            [".py"]   = new(CodeBody, CodeFold, "PY"),
            [".java"] = new(CodeBody, CodeFold, "JVA"),
            [".cpp"]  = new(CodeBody, CodeFold, "C++"),
            [".c"]    = new(CodeBody, CodeFold, "C"),
            [".h"]    = new(CodeBody, CodeFold, "H"),
            [".hpp"]  = new(CodeBody, CodeFold, "H++"),
            [".go"]   = new(CodeBody, CodeFold, "GO"),
            [".rs"]   = new(CodeBody, CodeFold, "RS"),
            [".rb"]   = new(CodeBody, CodeFold, "RB"),
            [".php"]  = new(CodeBody, CodeFold, "PHP"),
            [".swift"] = new(CodeBody, CodeFold, "SWF"),
            [".kt"]   = new(CodeBody, CodeFold, "KT"),
            [".lua"]  = new(CodeBody, CodeFold, "LUA"),
            [".r"]    = new(CodeBody, CodeFold, "R"),
            [".scala"] = new(CodeBody, CodeFold, "SCL"),
            [".dart"] = new(CodeBody, CodeFold, "DRT"),
            [".xaml"] = new(CodeBody, CodeFold, "XML"),

            // Web / markup
            [".html"] = new(WebBody, WebFold, "HTM"),
            [".htm"]  = new(WebBody, WebFold, "HTM"),
            [".css"]  = new(WebBody, WebFold, "CSS"),
            [".scss"] = new(WebBody, WebFold, "CSS"),
            [".less"] = new(WebBody, WebFold, "CSS"),
            [".json"] = new(WebBody, WebFold, "JSN"),
            [".xml"]  = new(WebBody, WebFold, "XML"),
            [".yaml"] = new(WebBody, WebFold, "YML"),
            [".yml"]  = new(WebBody, WebFold, "YML"),
            [".toml"] = new(WebBody, WebFold, "TML"),
            [".wasm"] = new(WebBody, WebFold, "WSM"),

            // Text / config
            [".txt"]  = new(TextBody, TextFold, "TXT"),
            [".md"]   = new(TextBody, TextFold, "MD"),
            [".log"]  = new(TextBody, TextFold, "LOG"),
            [".csv"]  = new(TextBody, TextFold, "CSV"),
            [".tsv"]  = new(TextBody, TextFold, "TSV"),
            [".ini"]  = new(TextBody, TextFold, "INI"),
            [".cfg"]  = new(TextBody, TextFold, "CFG"),
            [".conf"] = new(TextBody, TextFold, "CNF"),
            [".env"]  = new(TextBody, TextFold, "ENV"),
            [".gitignore"] = new(TextBody, TextFold, "GIT"),
            [".editorconfig"] = new(TextBody, TextFold, "CFG"),

            // Archives
            [".zip"]  = new(ArchiveBody, ArchiveFold, "ZIP"),
            [".rar"]  = new(ArchiveBody, ArchiveFold, "RAR"),
            [".7z"]   = new(ArchiveBody, ArchiveFold, "7Z"),
            [".tar"]  = new(ArchiveBody, ArchiveFold, "TAR"),
            [".gz"]   = new(ArchiveBody, ArchiveFold, "GZ"),
            [".bz2"]  = new(ArchiveBody, ArchiveFold, "BZ2"),
            [".xz"]   = new(ArchiveBody, ArchiveFold, "XZ"),
            [".iso"]  = new(ArchiveBody, ArchiveFold, "ISO"),
            [".cab"]  = new(ArchiveBody, ArchiveFold, "CAB"),
            [".nupkg"] = new(ArchiveBody, ArchiveFold, "NUP"),

            // Audio
            [".mp3"]  = new(AudioBody, AudioFold, "MP3"),
            [".wav"]  = new(AudioBody, AudioFold, "WAV"),
            [".flac"] = new(AudioBody, AudioFold, "FLC"),
            [".ogg"]  = new(AudioBody, AudioFold, "OGG"),
            [".aac"]  = new(AudioBody, AudioFold, "AAC"),
            [".wma"]  = new(AudioBody, AudioFold, "WMA"),
            [".m4a"]  = new(AudioBody, AudioFold, "M4A"),

            // Video
            [".mp4"]  = new(VideoBody, VideoFold, "MP4"),
            [".avi"]  = new(VideoBody, VideoFold, "AVI"),
            [".mkv"]  = new(VideoBody, VideoFold, "MKV"),
            [".mov"]  = new(VideoBody, VideoFold, "MOV"),
            [".wmv"]  = new(VideoBody, VideoFold, "WMV"),
            [".flv"]  = new(VideoBody, VideoFold, "FLV"),
            [".webm"] = new(VideoBody, VideoFold, "WBM"),
            [".m4v"]  = new(VideoBody, VideoFold, "M4V"),

            // Executables / scripts
            [".exe"]  = new(ExeBody, ExeFold, "EXE"),
            [".msi"]  = new(ExeBody, ExeFold, "MSI"),
            [".dll"]  = new(ExeBody, ExeFold, "DLL"),
            [".bat"]  = new(ExeBody, ExeFold, "BAT"),
            [".cmd"]  = new(ExeBody, ExeFold, "CMD"),
            [".ps1"]  = new(ExeBody, ExeFold, "PS1"),
            [".sh"]   = new(ExeBody, ExeFold, "SH"),
            [".com"]  = new(ExeBody, ExeFold, "COM"),
            [".sys"]  = new(ExeBody, ExeFold, "SYS"),
            [".appx"] = new(ExeBody, ExeFold, "APX"),

            // Data / database
            [".sql"]  = new(DataBody, DataFold, "SQL"),
            [".db"]   = new(DataBody, DataFold, "DB"),
            [".sqlite"] = new(DataBody, DataFold, "SQL"),
            [".mdb"]  = new(DataBody, DataFold, "MDB"),
            [".accdb"] = new(DataBody, DataFold, "ACC"),
            [".bak"]  = new(DataBody, DataFold, "BAK"),

            // Fonts
            [".ttf"]  = new(FontBody, FontFold, "TTF"),
            [".otf"]  = new(FontBody, FontFold, "OTF"),
            [".woff"] = new(FontBody, FontFold, "WOF"),
            [".woff2"] = new(FontBody, FontFold, "WF2"),
            [".eot"]  = new(FontBody, FontFold, "EOT"),

            // .NET / C# specific
            [".sln"]  = new(CodeBody, CodeFold, "SLN"),
            [".csproj"] = new(CodeBody, CodeFold, "CSP"),
            [".resx"] = new(CodeBody, CodeFold, "RSX"),
            [".config"] = new(WebBody, WebFold, "CFG"),
        };
    }

    internal static FileIconStyle GetStyle(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        if (!string.IsNullOrEmpty(ext) && ExtensionMap.TryGetValue(ext, out var style))
            return style;
        return new FileIconStyle(DefaultBody, DefaultFold, "");
    }

    private static Brush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
