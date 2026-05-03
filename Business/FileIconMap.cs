using MahApps.Metro.IconPacks;
using System.Collections.Frozen;

// Brand-name icons (Docker, Git, Nodejs, MicrosoftVisualStudio, …) are
// flagged obsolete by MahApps for trademark reasons but still render today.
// The replacement glyphs aren't shipped yet — revisit when MahApps publishes them.
#pragma warning disable CS0618

namespace Josha.Business
{
    internal static class FileIconMap
    {
        private const string BrushFolder    = "Brush.RowFolder";
        private const string BrushUnknown   = "Brush.OnSurfaceMuted";
        private const string BrushCode      = "Brush.Icon.Code";
        private const string BrushArchive   = "Brush.Icon.Archive";
        private const string BrushSpecial   = "Brush.Icon.Special";
        private const string BrushAccent    = "Brush.Accent";
        private const string BrushImage     = "Brush.Toast.Success";
        private const string BrushDoc       = "Brush.Toast.Error";
        private const string BrushVideo     = "Brush.Age.Months";
        private const string BrushAudio     = "Brush.Age.Weeks";
        private const string BrushExe       = "Brush.Toast.Warning";
        private const string BrushFont      = "Brush.Age.Hours";
        private const string BrushDesign    = "Brush.Age.Seconds";
        private const string BrushThreeD    = "Brush.Age.Minutes";

        private static readonly FrozenDictionary<string, (PackIconMaterialKind Kind, string BrushKey)> _extMap;
        private static readonly FrozenDictionary<string, (PackIconMaterialKind Kind, string BrushKey)> _byNameMap;
        private static readonly FrozenDictionary<string, (PackIconMaterialKind Kind, string BrushKey)> _specialFolderMap;

        static FileIconMap()
        {
            var ext = new Dictionary<string, (PackIconMaterialKind, string)>(StringComparer.OrdinalIgnoreCase);

            void Add(PackIconMaterialKind kind, string brush, params string[] xs)
            {
                foreach (var x in xs) ext[x] = (kind, brush);
            }

            Add(PackIconMaterialKind.FileImageOutline, BrushImage,
                "png", "jpg", "jpeg", "gif", "bmp", "webp", "heic", "heif",
                "tif", "tiff", "ico", "avif", "jfif");
            Add(PackIconMaterialKind.Svg, BrushImage, "svg");

            Add(PackIconMaterialKind.FileVideoOutline, BrushVideo,
                "mp4", "mkv", "avi", "mov", "wmv", "webm", "flv", "m4v",
                "mpg", "mpeg", "3gp");

            Add(PackIconMaterialKind.FileMusicOutline, BrushAudio,
                "mp3", "wav", "flac", "ogg", "m4a", "aac", "wma", "opus", "aiff");

            Add(PackIconMaterialKind.FilePdfBox,        BrushDoc, "pdf");
            Add(PackIconMaterialKind.FileWordBox,       BrushAccent, "doc", "docx", "odt", "rtf");
            Add(PackIconMaterialKind.FileExcelBox,      BrushImage, "xls", "xlsx", "ods", "csv");
            Add(PackIconMaterialKind.FilePowerpointBox, BrushDesign, "ppt", "pptx", "odp");

            Add(PackIconMaterialKind.LanguageCsharp,     BrushCode, "cs");
            Add(PackIconMaterialKind.FileCodeOutline,    BrushCode, "fs", "fsx");
            Add(PackIconMaterialKind.LanguagePython,     BrushCode, "py", "pyw", "ipynb");
            Add(PackIconMaterialKind.LanguageJavascript, BrushCode, "js", "mjs", "cjs");
            Add(PackIconMaterialKind.LanguageTypescript, BrushCode, "ts");
            Add(PackIconMaterialKind.React,              BrushCode, "jsx", "tsx");
            Add(PackIconMaterialKind.LanguageGo,         BrushCode, "go");
            Add(PackIconMaterialKind.LanguageRust,       BrushCode, "rs");
            Add(PackIconMaterialKind.LanguageJava,       BrushCode, "java");
            Add(PackIconMaterialKind.LanguageKotlin,     BrushCode, "kt", "kts");
            Add(PackIconMaterialKind.LanguageSwift,      BrushCode, "swift");
            Add(PackIconMaterialKind.LanguageRuby,       BrushCode, "rb");
            Add(PackIconMaterialKind.LanguagePhp,        BrushCode, "php");
            Add(PackIconMaterialKind.LanguageC,          BrushCode, "c", "h");
            Add(PackIconMaterialKind.LanguageCpp,        BrushCode, "cpp", "cc", "cxx", "hpp", "hxx");
            Add(PackIconMaterialKind.LanguageLua,        BrushCode, "lua");
            Add(PackIconMaterialKind.LanguageR,          BrushCode, "r");
            Add(PackIconMaterialKind.Vuejs,              BrushImage, "vue");
            Add(PackIconMaterialKind.LanguageHtml5,      BrushDoc,   "html", "htm");
            Add(PackIconMaterialKind.LanguageCss3,       BrushAccent, "css", "scss", "sass", "less");
            Add(PackIconMaterialKind.LanguageMarkdownOutline, "Brush.OnSurface", "md", "markdown", "mdx");

            Add(PackIconMaterialKind.FileCodeOutline, BrushCode,
                "scala", "dart", "m", "mm", "sh", "bash", "zsh",
                "ps1", "psm1", "bat", "cmd", "vb", "ex", "exs",
                "pl", "jl", "nim", "zig", "clj", "cljs", "erl", "hs");

            Add(PackIconMaterialKind.FileCodeOutline, BrushAccent, "svelte", "astro");

            Add(PackIconMaterialKind.Xml, BrushAccent, "xaml", "axaml", "csproj", "vbproj", "fsproj", "props", "targets");

            Add(PackIconMaterialKind.CodeJson, BrushUnknown, "json", "json5", "jsonc");
            Add(PackIconMaterialKind.Xml,      BrushUnknown, "xml", "xsd", "xsl", "xslt");
            Add(PackIconMaterialKind.CogOutline, BrushUnknown,
                "yaml", "yml", "toml", "ini", "conf", "cfg", "env", "properties", "plist");

            Add(PackIconMaterialKind.FileDocumentOutline, "Brush.OnSurface",
                "txt", "log", "rst", "adoc");

            Add(PackIconMaterialKind.FolderZipOutline, BrushArchive,
                "zip", "rar", "7z", "tar", "gz", "bz2", "xz", "zst",
                "tgz", "tbz", "cab");

            Add(PackIconMaterialKind.ApplicationBracesOutline, BrushExe, "exe", "com");
            Add(PackIconMaterialKind.PackageVariantClosed,     BrushExe, "msi", "msix", "appx", "deb", "rpm");
            Add(PackIconMaterialKind.Android,                  BrushImage, "apk");
            Add(PackIconMaterialKind.Apple,                    "Brush.OnSurface", "app", "dmg");
            Add(PackIconMaterialKind.PuzzleOutline,            BrushExe, "dll", "sys");

            Add(PackIconMaterialKind.FormatFont, BrushFont,
                "ttf", "otf", "woff", "woff2", "eot", "fon");

            Add(PackIconMaterialKind.Palette, BrushDesign,
                "psd", "ai", "sketch", "fig", "xd", "indd", "afdesign", "afphoto");

            Add(PackIconMaterialKind.CubeOutline, BrushThreeD,
                "obj", "fbx", "stl", "blend", "dae", "gltf", "glb",
                "dwg", "dxf", "step", "stp");

            Add(PackIconMaterialKind.DatabaseOutline, BrushAudio,
                "db", "sqlite", "sqlite3", "mdb", "accdb", "sql", "dump");

            Add(PackIconMaterialKind.KeyVariant, BrushExe,
                "pem", "crt", "cer", "key", "pfx", "p12", "pub", "gpg", "asc");

            Add(PackIconMaterialKind.Disc, BrushUnknown,
                "iso", "img", "vhd", "vhdx", "vmdk");

            _extMap = ext.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

            var byName = new Dictionary<string, (PackIconMaterialKind, string)>(StringComparer.OrdinalIgnoreCase)
            {
                ["Dockerfile"]            = (PackIconMaterialKind.Docker, BrushAccent),
                [".dockerignore"]         = (PackIconMaterialKind.Docker, BrushUnknown),
                ["docker-compose.yml"]    = (PackIconMaterialKind.Docker, BrushAccent),
                ["docker-compose.yaml"]   = (PackIconMaterialKind.Docker, BrushAccent),
                ["Makefile"]              = (PackIconMaterialKind.Cog, BrushUnknown),
                ["Rakefile"]              = (PackIconMaterialKind.LanguageRuby, BrushCode),
                ["Gemfile"]               = (PackIconMaterialKind.LanguageRuby, BrushCode),
                [".gitignore"]            = (PackIconMaterialKind.Git, BrushUnknown),
                [".gitattributes"]        = (PackIconMaterialKind.Git, BrushUnknown),
                [".gitmodules"]           = (PackIconMaterialKind.Git, BrushUnknown),
                [".gitkeep"]              = (PackIconMaterialKind.Git, BrushUnknown),
                ["package.json"]          = (PackIconMaterialKind.Nodejs, BrushImage),
                ["package-lock.json"]     = (PackIconMaterialKind.Nodejs, BrushUnknown),
                ["yarn.lock"]             = (PackIconMaterialKind.Nodejs, BrushUnknown),
                ["pnpm-lock.yaml"]        = (PackIconMaterialKind.Nodejs, BrushUnknown),
                ["tsconfig.json"]         = (PackIconMaterialKind.LanguageTypescript, BrushCode),
                ["LICENSE"]               = (PackIconMaterialKind.License, BrushUnknown),
                ["LICENSE.md"]            = (PackIconMaterialKind.License, BrushUnknown),
                ["LICENSE.txt"]           = (PackIconMaterialKind.License, BrushUnknown),
                ["CMakeLists.txt"]        = (PackIconMaterialKind.Cog, BrushUnknown),
                [".editorconfig"]         = (PackIconMaterialKind.CogOutline, BrushUnknown),
                [".prettierrc"]           = (PackIconMaterialKind.CogOutline, BrushUnknown),
                [".eslintrc"]             = (PackIconMaterialKind.CogOutline, BrushUnknown),
                [".eslintrc.json"]        = (PackIconMaterialKind.CogOutline, BrushUnknown),
                [".eslintrc.js"]          = (PackIconMaterialKind.CogOutline, BrushUnknown),
                [".npmrc"]                = (PackIconMaterialKind.Nodejs, BrushUnknown),
                [".env"]                  = (PackIconMaterialKind.CogOutline, BrushUnknown),
            };
            _byNameMap = byName.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

            var sf = new Dictionary<string, (PackIconMaterialKind, string)>(StringComparer.OrdinalIgnoreCase)
            {
                ["Desktop"]          = (PackIconMaterialKind.MonitorDashboard,        BrushFolder),
                ["Downloads"]        = (PackIconMaterialKind.FolderDownloadOutline,   BrushFolder),
                ["Documents"]        = (PackIconMaterialKind.FolderTextOutline,       BrushFolder),
                ["Pictures"]         = (PackIconMaterialKind.FolderImage,             BrushImage),
                ["Videos"]           = (PackIconMaterialKind.FolderPlayOutline,       BrushVideo),
                ["Music"]            = (PackIconMaterialKind.FolderMusicOutline,      BrushAudio),
                ["AppData"]          = (PackIconMaterialKind.FolderCogOutline,        BrushUnknown),
                ["Application Data"] = (PackIconMaterialKind.FolderCogOutline,        BrushUnknown),
                ["ProgramData"]      = (PackIconMaterialKind.FolderCogOutline,        BrushUnknown),
                ["Program Files"]    = (PackIconMaterialKind.FolderCogOutline,        BrushUnknown),
                ["Program Files (x86)"] = (PackIconMaterialKind.FolderCogOutline,     BrushUnknown),
                ["Windows"]          = (PackIconMaterialKind.MicrosoftWindows,        BrushAccent),
                [".git"]             = (PackIconMaterialKind.Git,                     BrushSpecial),
                [".github"]          = (PackIconMaterialKind.Github,                  BrushSpecial),
                ["node_modules"]     = (PackIconMaterialKind.Nodejs,                  "Brush.OnSurfaceSubtle"),
                ["bin"]              = (PackIconMaterialKind.FolderHidden,     "Brush.OnSurfaceSubtle"),
                ["obj"]              = (PackIconMaterialKind.FolderHidden,     "Brush.OnSurfaceSubtle"),
                ["dist"]             = (PackIconMaterialKind.FolderHidden,     "Brush.OnSurfaceSubtle"),
                ["build"]            = (PackIconMaterialKind.FolderHidden,     "Brush.OnSurfaceSubtle"),
                ["target"]           = (PackIconMaterialKind.FolderHidden,     "Brush.OnSurfaceSubtle"),
                ["out"]              = (PackIconMaterialKind.FolderHidden,     "Brush.OnSurfaceSubtle"),
                [".vscode"]          = (PackIconMaterialKind.MicrosoftVisualStudioCode, BrushSpecial),
                [".idea"]            = (PackIconMaterialKind.FolderEditOutline,       BrushSpecial),
                [".vs"]              = (PackIconMaterialKind.MicrosoftVisualStudio,   BrushSpecial),
            };
            _specialFolderMap = sf.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }

        public static (PackIconMaterialKind Kind, string BrushKey) Resolve(
            string name, string extension, bool isDirectory, bool isParentLink)
        {
            if (isParentLink)
                return (PackIconMaterialKind.ArrowUpThick, BrushUnknown);

            if (isDirectory)
            {
                if (_specialFolderMap.TryGetValue(name, out var sf))
                    return sf;
                return (PackIconMaterialKind.FolderOutline, BrushFolder);
            }

            if (_byNameMap.TryGetValue(name, out var byName))
                return byName;

            // Single-ext lookup only sees ".gz"; probe the full tail for
            // compound archive extensions.
            var lower = name.ToLowerInvariant();
            if (lower.EndsWith(".tar.gz")  || lower.EndsWith(".tar.bz2") ||
                lower.EndsWith(".tar.xz")  || lower.EndsWith(".tar.zst"))
                return (PackIconMaterialKind.FolderZipOutline, BrushArchive);

            if (!string.IsNullOrEmpty(extension) && _extMap.TryGetValue(extension, out var hit))
                return hit;

            return (PackIconMaterialKind.FileOutline, BrushUnknown);
        }
    }
}
