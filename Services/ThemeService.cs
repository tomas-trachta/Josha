using Microsoft.Win32;
using System.Windows;

namespace Josha.Services
{
    internal static class ThemeService
    {
        private const string ResolvedDark = "Dark";
        private const string ResolvedLight = "Light";

        public static void Apply(string preference)
        {
            var resolved = Resolve(preference);
            if (resolved == ResolvedDark) return;

            var dict = new ResourceDictionary
            {
                Source = new Uri("Styles/Theme.Light.xaml", UriKind.Relative),
            };
            Application.Current.Resources.MergedDictionaries.Add(dict);
        }

        public static string Resolve(string preference)
        {
            return preference switch
            {
                "Light"  => ResolvedLight,
                "System" => ReadSystemPreference(),
                _        => ResolvedDark,
            };
        }

        private static string ReadSystemPreference()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key?.GetValue("AppsUseLightTheme") is int v && v == 1)
                    return ResolvedLight;
            }
            catch { }
            return ResolvedDark;
        }
    }
}
