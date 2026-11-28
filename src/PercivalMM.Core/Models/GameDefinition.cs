using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;

namespace PercivalMM.Core.Models
{
    public enum DeploymentTargetRoot
    {
        GameInstall,
        ProtonUserDocuments
    }

    public class GameDefinition : IGameProfile
    {
        public string SteamAppId { get; set; } = string.Empty;
        public string NexusDomain { get; set; } = string.Empty;
        public string GameName { get; set; } = string.Empty;
        public string DefaultInstallFolderName { get; set; } = string.Empty;
        public string LaunchButtonLabel { get; set; } = "⚔️ Launch Game";
        public string BannerFileName { get; set; } = "witcher3.png";
        public string ModTypeBadge { get; set; } = "MOD";

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public DeploymentTargetRoot TargetRoot { get; set; } = DeploymentTargetRoot.GameInstall;

        public string ActiveModsRelPath { get; set; } = "mods";
        public string DisabledModsRelPath { get; set; } = "mods_disabled";
        public List<string> SweepRelPaths { get; set; } = new();
        public List<string> DirectorySymlinkPrefixes { get; set; } = new();
        public string SyncEngine { get; set; } = "Default";
        public string ResolveDeploymentRoot(string gameInstallPath)
        {
            if (TargetRoot == DeploymentTargetRoot.GameInstall || string.IsNullOrWhiteSpace(gameInstallPath))
                return gameInstallPath;

            var commonDir = Directory.GetParent(gameInstallPath);
            var steamAppsDir = commonDir?.Parent;
            if (steamAppsDir != null && steamAppsDir.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase))
            {
                string libraryPrefixDocs = Path.Combine(
                    steamAppsDir.FullName, "compatdata", SteamAppId,
                    "pfx", "drive_c", "users", "steamuser", "Documents");
                if (Directory.Exists(libraryPrefixDocs))
                    return libraryPrefixDocs;
            }

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(
                home, ".local", "share", "Steam", "steamapps", "compatdata", SteamAppId,
                "pfx", "drive_c", "users", "steamuser", "Documents");
        }

        public void SaveConfiguration(List<ModInfo> mods, string gameInstallPath, string homePath)
        {
            // Dispatched via SyncEngine in MainWindowViewModel
        }
    }
}
