using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PercivalMM.Core.Models;

namespace PercivalMM.Core.Services;

public class ValheimAssetPackService
{
    public const string StagingRootName = ".percival_staging";
    public const string PacksSubdir = "AssetPacks";
    public const string VanillaBackupSubdir = "VanillaBackup";

    public static string GetStagingRootDirectory(string gameInstallPath)
        => Path.Combine(gameInstallPath, StagingRootName, PacksSubdir);

    public static string GetStagingPackDirectory(string gameInstallPath, string packName)
        => Path.Combine(GetStagingRootDirectory(gameInstallPath), packName);

    public static string GetVanillaBackupDirectory(string gameInstallPath)
        => Path.Combine(gameInstallPath, StagingRootName, VanillaBackupSubdir);

    public static bool IsValheimTextureAssetPack(string extractedTempDir)
    {
        if (!Directory.Exists(extractedTempDir))
            return false;

        var relativePaths = Directory.GetFiles(extractedTempDir, "*.*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(extractedTempDir, f));

        return IsValheimTextureAssetPack(relativePaths);
    }

    public static bool IsValheimTextureAssetPack(IEnumerable<string> normalizedArchivePaths)
    {
        var paths = normalizedArchivePaths
            .Select(p => p.Replace('\\', '/').TrimStart('/'))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();

        bool hasManagedDll = paths.Any(p =>
            p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
            !p.Contains("valheim_Data/Managed/", StringComparison.OrdinalIgnoreCase));

        if (hasManagedDll)
            return false;

        return paths.Any(p =>
            p.Contains("valheim_Data/", StringComparison.OrdinalIgnoreCase) ||
            p.StartsWith("CustomTextures/", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("/CustomTextures/", StringComparison.OrdinalIgnoreCase) ||
            p.EndsWith(".assets", StringComparison.OrdinalIgnoreCase) ||
            p.EndsWith(".ress", StringComparison.OrdinalIgnoreCase) ||
            p.EndsWith(".resource", StringComparison.OrdinalIgnoreCase));
    }
    public static void NormalizeStagedPackLayout(string stagingPackDir)
    {
        if (!Directory.Exists(stagingPackDir))
            return;

        var topDirs = Directory.GetDirectories(stagingPackDir);
        var topFiles = Directory.GetFiles(stagingPackDir);

        if (topFiles.Length == 0 && topDirs.Length == 1)
        {
            string innerDir = topDirs[0];
            string innerName = Path.GetFileName(innerDir);

            if (!innerName.Equals("valheim_Data", StringComparison.OrdinalIgnoreCase) &&
                !innerName.Equals("BepInEx", StringComparison.OrdinalIgnoreCase) &&
                !innerName.Equals("CustomTextures", StringComparison.OrdinalIgnoreCase))
            {
                string tempDir = stagingPackDir + "_unwrap_tmp";
                Directory.Move(innerDir, tempDir);
                Directory.Delete(stagingPackDir, recursive: true);
                Directory.Move(tempDir, stagingPackDir);
            }
        }

        string rootCustomTextures = Path.Combine(stagingPackDir, "CustomTextures");
        if (Directory.Exists(rootCustomTextures))
        {
            string targetCustomTextures = Path.Combine(stagingPackDir, "BepInEx", "plugins", "CustomTextures");
            Directory.CreateDirectory(Path.GetDirectoryName(targetCustomTextures)!);
            if (!Directory.Exists(targetCustomTextures))
                Directory.Move(rootCustomTextures, targetCustomTextures);
        }
    }

    public static string SyncDeployedAssetPacks(string gameInstallPath, IEnumerable<InstalledModItem> orderedAssetPacks)
    {
        string backupRoot = GetVanillaBackupDirectory(gameInstallPath);
        Directory.CreateDirectory(backupRoot);

        foreach (string backupFile in Directory.EnumerateFiles(backupRoot, "*", SearchOption.AllDirectories))
        {
            string relPath = Path.GetRelativePath(backupRoot, backupFile);
            string targetFile = Path.Combine(gameInstallPath, relPath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(backupFile, targetFile, overwrite: true);
        }

        var activePacks = orderedAssetPacks
            .Where(p => p.IsEnabled && !p.IsSeparator &&
                        (p.StagingPath != null || p.FolderPath.Replace('\\', '/').Contains($"/{StagingRootName}/{PacksSubdir}/")))
            .OrderBy(p => p.Priority)
            .ToList();

        int totalAppliedFiles = 0;
        foreach (var pack in activePacks)
        {
            string packStageDir = pack.StagingPath ?? pack.FolderPath;
            if (!Directory.Exists(packStageDir))
                continue;

            foreach (string stagedFile in Directory.EnumerateFiles(packStageDir, "*", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(stagedFile).StartsWith(".percival", StringComparison.OrdinalIgnoreCase))
                    continue;

                string relPath = Path.GetRelativePath(packStageDir, stagedFile);
                string targetFile = Path.Combine(gameInstallPath, relPath);
                string backupFile = Path.Combine(backupRoot, relPath);

                if (File.Exists(targetFile) && !File.Exists(backupFile))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(backupFile)!);
                    File.Copy(targetFile, backupFile, overwrite: false);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
                File.Copy(stagedFile, targetFile, overwrite: true);
                totalAppliedFiles++;
            }
        }

        return $"Synced {activePacks.Count} Valheim HD asset pack(s) ({totalAppliedFiles} files deployed, vanilla backup intact).";
    }
    public async Task SyncDeployedAssetPacksAsync(
        string gameInstallPath,
        IEnumerable<InstalledModItem> orderedAssetPacks,
        Action<string>? telemetryLog = null)
    {
        string summary = await Task.Run(() => SyncDeployedAssetPacks(gameInstallPath, orderedAssetPacks));
        telemetryLog?.Invoke($"[Asset Sync] {summary}");
    }

    public static async Task PreWarmAssetPacksAsync(
        string gameInstallPath,
        IEnumerable<InstalledModItem> activeAssetPacks,
        Action<string>? telemetryLog = null,
        CancellationToken cancellationToken = default)
    {
        var filesToWarm = new List<string>();

        foreach (var pack in activeAssetPacks.Where(p => p.IsEnabled && !p.IsSeparator))
        {
            string packStageDir = pack.StagingPath ?? pack.FolderPath;
            if (!Directory.Exists(packStageDir))
                continue;

            bool isStagedPack = packStageDir.Replace('\\', '/').Contains($"/{StagingRootName}/{PacksSubdir}/");
            foreach (string stagedFile in Directory.EnumerateFiles(packStageDir, "*", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(stagedFile).StartsWith(".percival", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (isStagedPack)
                {
                    string relPath = Path.GetRelativePath(packStageDir, stagedFile);
                    string deployedPath = Path.Combine(gameInstallPath, relPath);
                    if (File.Exists(deployedPath))
                        filesToWarm.Add(deployedPath);
                }
                else
                {
                    filesToWarm.Add(stagedFile);
                }
            }
        }

        if (filesToWarm.Count == 0)
            return;

        long totalBytes = filesToWarm.Sum(f => new FileInfo(f).Length);
        double totalGb = totalBytes / (1024.0 * 1024.0 * 1024.0);
        telemetryLog?.Invoke($"⚡ [Asset Pre-Warm] Caching {filesToWarm.Count} HD texture file(s) ({totalGb:F2} GB) into Linux RAM page cache...");

        await Task.Run(async () =>
        {
            byte[] buffer = new byte[4 * 1024 * 1024];
            long bytesReadTotal = 0;
            int lastPercentReported = 0;

            foreach (string file in filesToWarm)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await using var fs = new FileStream(
                    file,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: buffer.Length,
                    options: FileOptions.SequentialScan);

                int read;
                while ((read = await fs.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    bytesReadTotal += read;
                    if (totalBytes > 0)
                    {
                        int pct = (int)((bytesReadTotal * 100L) / totalBytes);
                        if (pct >= lastPercentReported + 25)
                        {
                            lastPercentReported = pct;
                            telemetryLog?.Invoke($"⚡ [Asset Pre-Warm] RAM cache warm-up: {pct}% ({bytesReadTotal / (1024.0 * 1024.0 * 1024.0):F2} / {totalGb:F2} GB)");
                        }
                    }
                }
            }

            telemetryLog?.Invoke("✅ [Asset Pre-Warm] HD textures cached in RAM. Zero-stall streaming ready.");
        }, cancellationToken);
    }
}
