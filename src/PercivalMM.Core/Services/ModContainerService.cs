using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PercivalMM.Core.Models;

namespace PercivalMM.Core.Services
{
    public class ModContainerManifest
    {
        public string DisplayName { get; set; } = string.Empty;
        public string GameName { get; set; } = string.Empty;
        public ModTier Tier { get; set; } = ModTier.Core;
        public bool IsEnabled { get; set; } = true;
        public int Priority { get; set; } = 999;
        public ModItemKind ItemKind { get; set; } = ModItemKind.StandardMod;
        public string CategoryGroup { get; set; } = "General";
        public List<string> DeployedSymlinks { get; set; } = new();
    }

    public static class ModContainerService
    {
        public const string StagingRootName = ".percival_staging";
        public const string ModsSubdir = "Mods";
        private const string ManifestFileName = "manifest.json";
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        public static string GetContainersRootDirectory(string gameInstallPath)
            => Path.Combine(gameInstallPath, StagingRootName, ModsSubdir);

        public static string GetContainerDirectory(string gameInstallPath, string containerName)
            => Path.Combine(GetContainersRootDirectory(gameInstallPath), SanitizeName(containerName));

        public static string CleanArchiveDisplayName(string archivePath)
        {
            string name = Path.GetFileNameWithoutExtension(archivePath).Trim();
            foreach (var ext in new[] { ".zip", ".7z", ".rar" })
            {
                if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    name = name[..^ext.Length];
            }

            int dateIdx = name.IndexOf("_202", StringComparison.Ordinal);
            if (dateIdx > 0) name = name[..dateIdx];

            return name.Replace('_', ' ').Trim();
        }

        public static string SanitizeName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            string cleaned = string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? "ModContainer" : cleaned;
        }

        public static ModContainerManifest? LoadManifest(string containerDir)
        {
            string path = Path.Combine(containerDir, ManifestFileName);
            if (!File.Exists(path)) return null;

            try
            {
                return JsonSerializer.Deserialize<ModContainerManifest>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }

        public static void SaveManifest(string containerDir, ModContainerManifest manifest)
        {
            Directory.CreateDirectory(containerDir);
            string path = Path.Combine(containerDir, ManifestFileName);
            File.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOpts));
        }

        public static string SyncDeployedContainers(
            string gameInstallPath,
            string gameSteamAppId,
            IEnumerable<InstalledModItem> allItems)
        {
            string containersRoot = GetContainersRootDirectory(gameInstallPath);
            if (!Directory.Exists(containersRoot))
                return "No staged mod containers yet.";

            var gameDef = GameRegistryService.GetByAppId(gameSteamAppId);
            string deployRoot = gameDef?.ResolveDeploymentRoot(gameInstallPath) ?? gameInstallPath;

            // 1. Remove previously tracked symlinks from every container manifest
            foreach (var dir in Directory.GetDirectories(containersRoot))
            {
                var manifest = LoadManifest(dir);
                if (manifest == null) continue;

                foreach (var relLink in manifest.DeployedSymlinks)
                {
                    string fullLink = Path.Combine(deployRoot, relLink);
                    RemoveSymlinkIfOwned(fullLink);
                }
                manifest.DeployedSymlinks.Clear();
                SaveManifest(dir, manifest);
            }

            // 2. Sweep any orphaned symlinks in known target directories (LinkTarget != null safety)
            SweepOrphanSymlinksInGameDirs(deployRoot, gameDef);

            // 3. Deploy active containers in ascending Priority order
            var activeContainers = allItems
                .Where(m => m.IsEnabled && !m.IsSeparator && !string.IsNullOrEmpty(m.StagingPath) && Directory.Exists(m.StagingPath))
                .Where(m => !m.StagingPath!.Replace('\\', '/').Contains($"/{StagingRootName}/{ValheimAssetPackService.PacksSubdir}/"))
                .OrderBy(m => m.Priority)
                .ToList();

            int deployedCount = 0;

            foreach (var item in activeContainers)
            {
                string containerDir = item.StagingPath!;
                var manifest = LoadManifest(containerDir) ?? new ModContainerManifest
                {
                    DisplayName = item.Name,
                    Tier = item.Tier,
                    IsEnabled = item.IsEnabled,
                    Priority = item.Priority
                };

                manifest.IsEnabled = item.IsEnabled;
                manifest.Priority = item.Priority;
                manifest.Tier = item.Tier;

                DeployContainerTree(deployRoot, containerDir, manifest.DeployedSymlinks, gameDef);
                SaveManifest(containerDir, manifest);
                deployedCount++;
            }

            return $"Deployed {deployedCount} active mod container(s) via symlinks.";
        }

        private static void DeployContainerTree(
            string deployRoot,
            string containerDir,
            List<string> trackedLinks,
            GameDefinition? gameDef)
        {
            foreach (var file in Directory.GetFiles(containerDir, "*", SearchOption.AllDirectories))
            {
                string fileName = Path.GetFileName(file);
                if (fileName.Equals(ManifestFileName, StringComparison.OrdinalIgnoreCase) ||
                    fileName.StartsWith(".percival", StringComparison.OrdinalIgnoreCase))
                    continue;

                string relFilePath = Path.GetRelativePath(containerDir, file);
                string[] parts = relFilePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string? dirLinkRel = GetDirectorySymlinkBoundary(parts, gameDef);

                if (dirLinkRel != null)
                {
                    string sourceDir = Path.Combine(containerDir, dirLinkRel);
                    string targetLink = Path.Combine(deployRoot, dirLinkRel);

                    if (!trackedLinks.Any(x => x.Equals(dirLinkRel, StringComparison.OrdinalIgnoreCase)))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(targetLink)!);
                        RemoveSymlinkIfOwned(targetLink);
                        if (!Directory.Exists(targetLink))
                        {
                            Directory.CreateSymbolicLink(targetLink, sourceDir);
                            trackedLinks.Add(dirLinkRel);
                        }
                    }
                }
                else
                {
                    string targetLink = Path.Combine(deployRoot, relFilePath);
                    if (!trackedLinks.Any(x => x.Equals(relFilePath, StringComparison.OrdinalIgnoreCase)))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(targetLink)!);
                        RemoveSymlinkIfOwned(targetLink);
                        if (!File.Exists(targetLink))
                        {
                            File.CreateSymbolicLink(targetLink, file);
                            trackedLinks.Add(relFilePath);
                        }
                    }
                }
            }
        }

        private static string? GetDirectorySymlinkBoundary(string[] parts, GameDefinition? gameDef)
        {
            if (gameDef == null || gameDef.DirectorySymlinkPrefixes.Count == 0) return null;

            foreach (var prefix in gameDef.DirectorySymlinkPrefixes)
            {
                var prefixParts = prefix.Split('/', '\\');
                if (parts.Length > prefixParts.Length + 1 &&
                    prefixParts.Select((p, idx) => parts[idx].Equals(p, StringComparison.OrdinalIgnoreCase)).All(x => x))
                {
                    return Path.Combine(parts.Take(prefixParts.Length + 1).ToArray());
                }
            }

            return null;
        }

        private static void SweepOrphanSymlinksInGameDirs(string deployRoot, GameDefinition? gameDef)
        {
            if (gameDef == null) return;

            var dirsToSweep = gameDef.SweepRelPaths
                .Select(rel => Path.Combine(deployRoot, rel.Replace('/', Path.DirectorySeparatorChar)));

            foreach (var dir in dirsToSweep.Where(Directory.Exists))
            {
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    if (new DirectoryInfo(sub).LinkTarget != null)
                        Directory.Delete(sub, false);
                }
                foreach (var file in Directory.GetFiles(dir))
                {
                    if (new FileInfo(file).LinkTarget != null)
                        File.Delete(file);
                }
            }
        }

        private static void RemoveSymlinkIfOwned(string fullPath)
        {
            var dirInfo = new DirectoryInfo(fullPath);
            if (dirInfo.Exists && dirInfo.LinkTarget != null)
            {
                dirInfo.Delete(false);
                return;
            }

            var fileInfo = new FileInfo(fullPath);
            if (fileInfo.Exists && fileInfo.LinkTarget != null)
            {
                fileInfo.Delete();
            }
        }

        public static void DeleteContainer(string gameInstallPath, InstalledModItem mod, string? gameSteamAppId = null)
        {
            if (string.IsNullOrEmpty(mod.StagingPath)) return;

            var gameDef = !string.IsNullOrEmpty(gameSteamAppId)
                ? GameRegistryService.GetByAppId(gameSteamAppId)
                : null;
            string deployRoot = gameDef?.ResolveDeploymentRoot(gameInstallPath) ?? gameInstallPath;

            var manifest = LoadManifest(mod.StagingPath);
            if (manifest != null)
            {
                foreach (var relLink in manifest.DeployedSymlinks)
                    RemoveSymlinkIfOwned(Path.Combine(deployRoot, relLink));
            }

            if (Directory.Exists(mod.StagingPath))
                Directory.Delete(mod.StagingPath, recursive: true);
        }
    }
}
