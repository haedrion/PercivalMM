using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PercivalMM.Core.Models;

namespace PercivalMM.Core.Services
{
    public static class Witcher3ConfigSync
    {
        private const string Witcher3SteamAppId = "292030";

        public static string? LocateModsSettingsPath(string gameRootPath)
        {
            var commonDir = Directory.GetParent(gameRootPath);
            var steamAppsDir = commonDir?.Parent;

            var candidateSteamApps = new List<string>();
            if (steamAppsDir != null) candidateSteamApps.Add(steamAppsDir.FullName);

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string defaultSteamApps = Path.Combine(home, ".local/share/Steam/steamapps");
            if (!candidateSteamApps.Contains(defaultSteamApps))
                candidateSteamApps.Add(defaultSteamApps);

            foreach (var steamApps in candidateSteamApps)
            {
                string docDir = Path.Combine(
                    steamApps,
                    "compatdata",
                    Witcher3SteamAppId,
                    "pfx/drive_c/users/steamuser/Documents/The Witcher 3"
                );

                if (Directory.Exists(docDir))
                {
                    return Path.Combine(docDir, "mods.settings");
                }
            }

            return null;
        }

        public static string SyncLoadOrder(string gameRootPath, IEnumerable<InstalledModItem> mods)
        {
            string? settingsPath = LocateModsSettingsPath(gameRootPath);
            if (string.IsNullOrEmpty(settingsPath))
            {
                return "⚠️ Proton prefix (AppID 292030) not found yet—launch Witcher 3 once via Steam to initialize Documents folder.";
            }

            var sb = new StringBuilder();
            foreach (var mod in mods)
            {
                if (mod.IsSeparator) continue;

                // Expand container mods/mod* subfolders so multi-part Nexus archives register every mod* entry
                if (!string.IsNullOrEmpty(mod.StagingPath) && Directory.Exists(Path.Combine(mod.StagingPath, "mods")))
                {
                    var subModDirs = Directory.GetDirectories(Path.Combine(mod.StagingPath, "mods"))
                        .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase);

                    foreach (var subDir in subModDirs)
                    {
                        string subModName = Path.GetFileName(subDir);
                        sb.AppendLine($"[{subModName}]");
                        sb.AppendLine($"Enabled={(mod.IsEnabled ? 1 : 0)}");
                        sb.AppendLine($"Priority={mod.Priority}");
                        sb.AppendLine();
                    }
                }
                else
                {
                    // Legacy non-container mod folder fallback
                    sb.AppendLine($"[{mod.Name}]");
                    sb.AppendLine($"Enabled={(mod.IsEnabled ? 1 : 0)}");
                    sb.AppendLine($"Priority={mod.Priority}");
                    sb.AppendLine();
                }
            }

            File.WriteAllText(settingsPath, sb.ToString());
            return $"✅ Synced {settingsPath}";
        }
    }
}
