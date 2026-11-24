using System;
using System.IO;
using System.Text.RegularExpressions;

namespace PercivalMM.Core.Services
{
    public static class SteamLaunchOptionsService
    {
        public static string ConfigureValheimBepInExLaunchOption(string gameRootPath)
        {
            if (OperatingSystem.IsWindows())
                return "ℹ️ Windows detected: BepInEx winhttp.dll hooks automatically without Steam launch options.";

            string bepInExScript = Path.Combine(gameRootPath, "start_game_bepinex.sh");
            if (!File.Exists(bepInExScript))
                return "⚠️ Import BepInExPack_Valheim first so 'start_game_bepinex.sh' exists.";

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] candidatePaths = new[]
            {
                Path.Combine(home, ".local/share/Steam/userdata"),
                Path.Combine(home, ".steam/debian-installation/userdata"),
                Path.Combine(home, ".steam/steam/userdata"),
                Path.Combine(home, ".var/app/com.valvesoftware.Steam/.local/share/Steam/userdata")
            };

            string? userDataRoot = Array.Find(candidatePaths, Directory.Exists);
            if (userDataRoot == null)
                return "⚠️ Steam userdata directory not found across standard Linux Steam paths.";

            string targetOption = "./start_game_bepinex.sh %command%";
            int updatedProfiles = 0;

            foreach (var userDir in Directory.GetDirectories(userDataRoot))
            {
                string configPath = Path.Combine(userDir, "config", "localconfig.vdf");
                if (!File.Exists(configPath)) continue;

                string content = File.ReadAllText(configPath);
                if (content.Contains(targetOption) || content.Contains("WINEDLLOVERRIDES"))
                {
                    updatedProfiles++;
                    continue;
                }

                // Back up localconfig.vdf before modifying
                File.Copy(configPath, configPath + ".percival.bak", true);

                // Look for Valheim's AppID block ("892970") inside localconfig.vdf
                var appBlockRegex = new Regex(@"(""892970""\s*\{)", RegexOptions.Multiline);
                if (appBlockRegex.IsMatch(content))
                {
                    var launchOptRegex = new Regex(@"(""892970""\s*\{[^{}]*?)""LaunchOptions""\s*""[^""]*""", RegexOptions.Singleline);
                    if (launchOptRegex.IsMatch(content))
                    {
                        content = launchOptRegex.Replace(content, $"$1\"LaunchOptions\"\t\t\"{targetOption}\"", 1);
                    }
                    else
                    {
                        content = appBlockRegex.Replace(content, $"$1\n\t\t\t\t\t\t\"LaunchOptions\"\t\t\"{targetOption}\"", 1);
                    }

                    File.WriteAllText(configPath, content);
                    updatedProfiles++;
                }
            }

            if (updatedProfiles > 0)
                return "✅ Valheim Steam Launch Options verified.";

            return "⚠️ Valheim AppID 892970 not found in localconfig.vdf yet—launch Valheim once via Steam first.";
        }
    }
}
