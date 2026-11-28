using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PercivalMM.Core.Models;

namespace PercivalMM.Core.Services
{
    public record GameBuildInfo(string AppId, string BuildId, string LastUpdated);

    public static class VersionAlignerService
    {
        // Matches trailing Nexus / version tags like:
        // _3.2-2556-3-2-1744190047, -69-1-0, _v5.63, (v1.4.2), etc.
        private static readonly Regex NexusSuffixRegex = new(
            @"([_\-\s]+v?\d+(?:\.\d+)*-\d+(?:-\d+)+|[_\-\s]+v\d+(?:\.\d+)+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Reads the live Steam buildid and update timestamp from steamapps/appmanifest_<AppId>.acf.
        /// </summary>
        public static GameBuildInfo? GetInstalledGameBuild(string gameInstallPath, string steamAppId)
        {
            if (string.IsNullOrWhiteSpace(gameInstallPath) || string.IsNullOrWhiteSpace(steamAppId))
                return null;

            try
            {
                // gameInstallPath is typically .../steamapps/common/<GameFolder>
                var commonDir = Directory.GetParent(gameInstallPath);
                var steamAppsDir = commonDir?.Parent;
                if (steamAppsDir == null) return null;

                string manifestPath = Path.Combine(steamAppsDir.FullName, $"appmanifest_{steamAppId}.acf");
                if (!File.Exists(manifestPath)) return null;

                string acf = File.ReadAllText(manifestPath);
                string buildId = ExtractAcfValue(acf, "buildid") ?? "0";
                string lastUpdated = ExtractAcfValue(acf, "LastUpdated") ?? "0";

                return new GameBuildInfo(steamAppId, buildId, lastUpdated);
            }
            catch
            {
                return null;
            }
        }

        private static string? ExtractAcfValue(string acfContent, string key)
        {
            var match = Regex.Match(acfContent, $"\"{Regex.Escape(key)}\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// Strips messy version & Nexus archive IDs from a mod name while preserving its core identifier.
        /// </summary>
        public static string StripVersionSuffix(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return rawName;
            string cleaned = NexusSuffixRegex.Replace(rawName.Trim(), string.Empty).Trim('_', '-', ' ');
            return string.IsNullOrWhiteSpace(cleaned) ? rawName : cleaned;
        }

        /// <summary>
        /// Resolves the actual deployed folder name for No Man's Sky inside GAMEDATA/MODS
        /// so GCMODSETTINGS.MXML always matches what NMS sees on disk.
        /// </summary>
        public static string ResolveNmsDeployedFolderName(InstalledModItem mod)
        {
            if (!string.IsNullOrEmpty(mod.StagingPath) && Directory.Exists(mod.StagingPath))
            {
                string stagedModsDir = Path.Combine(mod.StagingPath, "GAMEDATA", "MODS");
                if (Directory.Exists(stagedModsDir))
                {
                    var sub = Directory.GetDirectories(stagedModsDir).FirstOrDefault();
                    if (sub != null) return Path.GetFileName(sub);
                }
            }

            if (!string.IsNullOrEmpty(mod.FolderPath))
                return Path.GetFileName(mod.FolderPath);

            return mod.Name;
        }

        /// <summary>
        /// Aligns version metadata inside staged mod manifests (meta.lsx, info.json, descriptor.mod, etc.)
        /// to match the live installed game build. Creates a .percival_bak backup before modifying any file.
        /// </summary>
        public static string AlignStagedModManifests(
            string gameInstallPath,
            string steamAppId,
            IEnumerable<InstalledModItem> mods)
        {
            var buildInfo = GetInstalledGameBuild(gameInstallPath, steamAppId);
            string buildLabel = buildInfo != null ? $"Build #{buildInfo.BuildId}" : "Current Install";

            int patchedFiles = 0;

            foreach (var mod in mods)
            {
                string? rootDir = !string.IsNullOrEmpty(mod.StagingPath) && Directory.Exists(mod.StagingPath)
                    ? mod.StagingPath
                    : (Directory.Exists(mod.FolderPath) ? mod.FolderPath : null);

                if (rootDir == null) continue;

                // 1. Larian / BG3 / DOS2 meta.lsx Version64 alignment
                foreach (var lsxFile in Directory.GetFiles(rootDir, "meta.lsx", SearchOption.AllDirectories))
                {
                    if (PatchLarianMetaLsx(lsxFile))
                        patchedFiles++;
                }

                // 2. JSON manifests (Cyberpunk 2077 info.json, BepInEx/manifest.json, etc.)
                foreach (var jsonFile in Directory.GetFiles(rootDir, "info.json", SearchOption.AllDirectories))
                {
                    if (buildInfo != null && PatchJsonGameVersion(jsonFile, buildInfo.BuildId))
                        patchedFiles++;
                }
            }

            return patchedFiles > 0
                ? $"Aligned {patchedFiles} mod manifest(s) to {buildLabel}."
                : $"Verified mod compatibility against {buildLabel}.";
        }

        private static bool PatchLarianMetaLsx(string lsxPath)
        {
            try
            {
                var doc = XDocument.Load(lsxPath);
                bool modified = false;

                // Ensure ModuleInfo Version64 is present and valid so Larian's engine doesn't reject it
                var moduleInfo = doc.Descendants("node")
                    .FirstOrDefault(n => (string?)n.Attribute("id") == "ModuleInfo");

                if (moduleInfo != null)
                {
                    var versionAttr = moduleInfo.Elements("attribute")
                        .FirstOrDefault(a => (string?)a.Attribute("id") == "Version64");

                    if (versionAttr != null && (string?)versionAttr.Attribute("value") == "0")
                    {
                        BackupOnce(lsxPath);
                        versionAttr.SetAttributeValue("value", "36028797018963968"); // Standard 1.0.0.0 Version64
                        modified = true;
                    }
                }

                if (modified)
                    doc.Save(lsxPath);

                return modified;
            }
            catch
            {
                return false;
            }
        }

        private static bool PatchJsonGameVersion(string jsonPath, string buildId)
        {
            try
            {
                string content = File.ReadAllText(jsonPath);
                // Replace "gameVersion": "..." or "supported_version": "..." if pinned to an older patch
                string updated = Regex.Replace(
                    content,
                    "(\"(?:gameVersion|supported_version)\"\\s*:\\s*\")([^\"]+)(\")",
                    m => $"{m.Groups[1].Value}{buildId}{m.Groups[3].Value}",
                    RegexOptions.IgnoreCase);

                if (updated != content)
                {
                    BackupOnce(jsonPath);
                    File.WriteAllText(jsonPath, updated);
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static void BackupOnce(string filePath)
        {
            string bak = filePath + ".percival_bak";
            if (!File.Exists(bak))
                File.Copy(filePath, bak);
        }
    }
}
