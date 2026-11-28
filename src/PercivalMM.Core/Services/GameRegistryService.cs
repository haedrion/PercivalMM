using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PercivalMM.Core.Models;

namespace PercivalMM.Core.Services
{
    public static class GameRegistryService
    {
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
        private static List<GameDefinition>? _cachedGames;

        public static string GetRegistryPath()
            => Path.Combine(AppContext.BaseDirectory, "games.json");

                public static IReadOnlyList<GameDefinition> LoadGames()
        {
            if (_cachedGames != null) return _cachedGames;

            var defaults = GetDefaultGames();
            string path = GetRegistryPath();
            if (!File.Exists(path))
            {
                _cachedGames = defaults;
                File.WriteAllText(path, JsonSerializer.Serialize(_cachedGames, JsonOpts));
                return _cachedGames;
            }

            try
            {
                var loaded = JsonSerializer.Deserialize<List<GameDefinition>>(File.ReadAllText(path));
                if (loaded == null || loaded.Count == 0)
                {
                    _cachedGames = defaults;
                }
                else
                {
                    bool updated = false;
                    foreach (var def in defaults)
                    {
                        var existing = loaded.FirstOrDefault(g => g.SteamAppId == def.SteamAppId);
                        if (existing == null)
                        {
                            loaded.Add(def);
                            updated = true;
                        }
                        else if (string.IsNullOrWhiteSpace(existing.NexusDomain) && !string.IsNullOrWhiteSpace(def.NexusDomain))
                        {
                            existing.NexusDomain = def.NexusDomain;
                            updated = true;
                        }
                    }

                    _cachedGames = loaded;
                    if (updated)
                        File.WriteAllText(path, JsonSerializer.Serialize(_cachedGames, JsonOpts));
                }
            }
            catch
            {
                _cachedGames = defaults;
            }

            return _cachedGames;
        }

        public static GameDefinition? GetByAppId(string appId)
            => LoadGames().FirstOrDefault(g => g.SteamAppId == appId);
        public static GameDefinition? GetByNexusDomain(string domain)
            => LoadGames().FirstOrDefault(g => g.NexusDomain.Equals(domain, StringComparison.OrdinalIgnoreCase));
        public static List<GameDefinition> GetDefaultGames() => new()
        {
            new GameDefinition
            {
                SteamAppId = "292030",
                NexusDomain = "witcher3",
                GameName = "The Witcher 3: Wild Hunt",
                DefaultInstallFolderName = "The Witcher 3",
                LaunchButtonLabel = "⚔️ Launch Witcher 3",
                BannerFileName = "witcher3.png",
                ModTypeBadge = "MOD",
                TargetRoot = DeploymentTargetRoot.GameInstall,
                ActiveModsRelPath = "mods",
                DisabledModsRelPath = "mods_disabled",
                SweepRelPaths = new() { "mods", "dlc" },
                DirectorySymlinkPrefixes = new() { "mods", "dlc" },
                SyncEngine = "Witcher3"
            },
            new GameDefinition
            {
                SteamAppId = "892970",
                NexusDomain = "valheim",
                GameName = "Valheim",
                DefaultInstallFolderName = "Valheim",
                LaunchButtonLabel = "⚔️ Launch Valheim",
                BannerFileName = "valheim.png",
                ModTypeBadge = "BEPINEX",
                TargetRoot = DeploymentTargetRoot.GameInstall,
                ActiveModsRelPath = "BepInEx/plugins",
                DisabledModsRelPath = "BepInEx/plugins_disabled",
                SweepRelPaths = new() { "BepInEx/plugins" },
                DirectorySymlinkPrefixes = new() { "BepInEx/plugins" },
                SyncEngine = "Valheim"
            },
            new GameDefinition
            {
                SteamAppId = "275850",
                NexusDomain = "nomanssky",
                GameName = "No Man's Sky",
                DefaultInstallFolderName = "No Man's Sky",
                LaunchButtonLabel = "⚔️ Launch No Man's Sky",
                BannerFileName = "nms.png",
                ModTypeBadge = "NMS",
                TargetRoot = DeploymentTargetRoot.GameInstall,
                ActiveModsRelPath = "GAMEDATA/MODS",
                DisabledModsRelPath = "GAMEDATA/MODS_DISABLED",
                SweepRelPaths = new() { "GAMEDATA/MODS" },
                DirectorySymlinkPrefixes = new() { "GAMEDATA/MODS" },
                SyncEngine = "NMS"
            },
            new GameDefinition
            {
                SteamAppId = "1903340",
                NexusDomain = "expedition33",
                GameName = "Clair Obscur: Expedition 33",
                DefaultInstallFolderName = "Expedition 33",
                LaunchButtonLabel = "⚔️ Launch Expedition 33",
                BannerFileName = "expedition33.png",
                ModTypeBadge = "PAK",
                TargetRoot = DeploymentTargetRoot.GameInstall,
                ActiveModsRelPath = "Sandfall/Content/Paks/~mods",
                DisabledModsRelPath = "Sandfall/Content/Paks/~mods_disabled",
                SweepRelPaths = new()
                {
                    "Sandfall/Content/Paks/~mods",
                    "Sandfall/Content/Paks/LogicMods",
                    "Sandfall/Binaries/Win64/ue4ss/Mods"
                },
                DirectorySymlinkPrefixes = new() { "Sandfall/Content/Paks/~mods", "Sandfall/Content/Paks/LogicMods" },
                SyncEngine = "Default"
            },
            new GameDefinition
            {
                SteamAppId = "435150",
                NexusDomain = "divinityoriginalsin2definitiveedition",
                GameName = "Divinity: Original Sin 2 - Definitive Edition",
                DefaultInstallFolderName = "Divinity Original Sin 2",
                LaunchButtonLabel = "⚔️ Launch Divinity: OS 2",
                BannerFileName = "dos2.png",
                ModTypeBadge = "PAK",
                TargetRoot = DeploymentTargetRoot.ProtonUserDocuments,
                ActiveModsRelPath = "Larian Studios/Divinity Original Sin 2 Definitive Edition/Mods",
                DisabledModsRelPath = "Larian Studios/Divinity Original Sin 2 Definitive Edition/Mods_Disabled",
                SweepRelPaths = new() { "Larian Studios/Divinity Original Sin 2 Definitive Edition/Mods" },
                DirectorySymlinkPrefixes = new(),
                SyncEngine = "DOS2"
            }
        };
    }
}
