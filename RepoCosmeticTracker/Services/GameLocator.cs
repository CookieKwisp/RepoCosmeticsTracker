using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace RepoCosmeticTracker.Services
{
    public static class GameLocator
    {
        private const string RepoAppId = "3241660";
        public static string? GetRepoDataRoot()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string path = Path.GetFullPath(Path.Combine(localAppData, "..", "LocalLow", "semiwork", "Repo"));
            return Directory.Exists(path) ? path : null;
        }
        public static string? GetSaveFolder()
        {
            string? root = GetRepoDataRoot();
            if (root == null)
                return null;

            string path = Path.Combine(root, "saves");
            return Directory.Exists(path) ? path : null;
        }

        public static IEnumerable<string> GetSaveSlotFolders()
        {
            string? savesRoot = GetSaveFolder();
            if (savesRoot == null)
                yield break;

            foreach (string dir in Directory.GetDirectories(savesRoot))
                yield return dir;
        }

        public static string? GetSteamInstallPath()
        {
            string[] machineKeyPaths =
            {
                @"SOFTWARE\WOW6432Node\Valve\Steam",
                @"SOFTWARE\Valve\Steam"
            };

            foreach (string keyPath in machineKeyPaths)
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(keyPath);
                if (key?.GetValue("InstallPath") is string installPath && Directory.Exists(installPath))
                    return installPath;
            }

            using RegistryKey? userKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam");
            if (userKey?.GetValue("SteamPath") is string userPath)
            {
                string normalized = userPath.Replace('/', Path.DirectorySeparatorChar);
                if (Directory.Exists(normalized))
                    return normalized;
            }

            return null;
        }
        public static IEnumerable<string> GetSteamLibraryFolders()
        {
            string? steamPath = GetSteamInstallPath();
            if (steamPath == null)
                yield break;

            yield return steamPath;

            string vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdfPath))
                yield break;

            string content = File.ReadAllText(vdfPath);
            foreach (Match m in Regex.Matches(content, "\"path\"\\s*\"([^\"]+)\""))
            {
                string libraryPath = m.Groups[1].Value.Replace("\\\\", "\\");
                if (Directory.Exists(libraryPath))
                    yield return libraryPath;
            }
        }

        public static string? FindRepoInstallFolder()
        {
            foreach (string library in GetSteamLibraryFolders())
            {
                string manifestPath = Path.Combine(library, "steamapps", $"appmanifest_{RepoAppId}.acf");
                if (!File.Exists(manifestPath))
                    continue;

                string manifest = File.ReadAllText(manifestPath);
                Match installDirMatch = Regex.Match(manifest, "\"installdir\"\\s*\"([^\"]+)\"");
                if (!installDirMatch.Success)
                    continue;

                string candidate = Path.Combine(library, "steamapps", "common", installDirMatch.Groups[1].Value);
                if (Directory.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        public static string? FindAssemblyCSharp()
        {
            string? installDir = FindRepoInstallFolder();
            if (installDir == null)
                return null;

            foreach (string file in Directory.EnumerateFiles(installDir, "Assembly-CSharp.dll", SearchOption.AllDirectories))
                return file;

            return null;
        }

        public static string? FindRepoDataFolder()
        {
            string? installDir = FindRepoInstallFolder();
            if (installDir == null)
                return null;

            string candidate = Path.Combine(installDir, "REPO_Data");
            return Directory.Exists(candidate) ? candidate : null;
        }
    }
}
