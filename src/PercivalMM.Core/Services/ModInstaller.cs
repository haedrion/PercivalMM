using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace PercivalMM.Core.Services
{
    public class ModInstaller
    {
        public static void InstallZipMod(string archivePath, string targetGameRootDirectory, string gameName = "The Witcher 3: Wild Hunt")
        {
            Console.WriteLine($"\n📦 [PercivalMM] Staging installation archive: {Path.GetFileName(archivePath)}");

            if (!File.Exists(archivePath))
            {
                Console.WriteLine("❌ Error: Downloaded archive file not found.");
                return;
            }

            string workingBaseDir = Path.GetDirectoryName(archivePath) ?? Path.GetTempPath();
            string tempExtractionPath = Path.Combine(workingBaseDir, "PercivalMM_Temp_" + Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempExtractionPath);

            try
            {
                using (var archive = ArchiveFactory.OpenArchive(archivePath))
                {
                    foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                    {
                        entry.WriteToDirectory(tempExtractionPath, new ExtractionOptions
                        {
                            ExtractFullPath = true,
                            Overwrite = true
                        });
                    }
                }

                // --- VALHEIM (BepInEx) PIPELINE ---
                if (gameName.Contains("Valheim", StringComparison.OrdinalIgnoreCase))
                {
                    InstallValheimArchive(tempExtractionPath, archivePath, targetGameRootDirectory);
                    return;
                }

                // --- THE WITCHER 3 PIPELINE ---
                string targetModsDir = Path.Combine(targetGameRootDirectory, "mods");
                string targetDlcDir = Path.Combine(targetGameRootDirectory, "dlc");
                string targetBinDir = Path.Combine(targetGameRootDirectory, "bin");

                Directory.CreateDirectory(targetModsDir);
                Directory.CreateDirectory(targetDlcDir);

                string[] discoveredDirectories = Directory.GetDirectories(tempExtractionPath, "*", SearchOption.AllDirectories);
                var validModFolders = new List<string>();
                var validDlcFolders = new List<string>();
                string? discoveredBinFolder = null;

                foreach (var dir in discoveredDirectories)
                {
                    string folderName = Path.GetFileName(dir);
                    string lowerName = folderName.ToLower();

                    if (lowerName != "mods" && lowerName.StartsWith("mod"))
                    {
                        validModFolders.Add(dir);
                    }
                    else if (lowerName != "dlc" && lowerName.StartsWith("dlc"))
                    {
                        validDlcFolders.Add(dir);
                    }
                    else if (lowerName == "bin" && discoveredBinFolder == null)
                    {
                        discoveredBinFolder = dir;
                    }
                }

                if (validModFolders.Count == 0 && validDlcFolders.Count == 0 && discoveredBinFolder == null)
                {
                    string cleanArchiveName = "mod_" + Path.GetFileNameWithoutExtension(archivePath).Replace(" ", "");
                    string fallbackDestination = Path.Combine(targetModsDir, cleanArchiveName);

                    if (Directory.Exists(fallbackDestination)) Directory.Delete(fallbackDestination, true);
                    CopyDirectoryCrossDevice(tempExtractionPath, fallbackDestination);
                    return;
                }

                foreach (string sourceFolder in validModFolders)
                {
                    string folderName = Path.GetFileName(sourceFolder);
                    string destinationPath = Path.Combine(targetModsDir, folderName);

                    if (Directory.Exists(destinationPath)) Directory.Delete(destinationPath, true);
                    CopyDirectoryCrossDevice(sourceFolder, destinationPath);
                }

                foreach (string sourceFolder in validDlcFolders)
                {
                    string folderName = Path.GetFileName(sourceFolder);
                    string destinationPath = Path.Combine(targetDlcDir, folderName);

                    if (Directory.Exists(destinationPath)) Directory.Delete(destinationPath, true);
                    CopyDirectoryCrossDevice(sourceFolder, destinationPath);
                }

                if (discoveredBinFolder != null)
                {
                    CopyDirectoryCrossDevice(discoveredBinFolder, targetBinDir);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Extraction pipeline fault: {ex.Message}");
            }
            finally
            {
                if (Directory.Exists(tempExtractionPath))
                {
                    Directory.Delete(tempExtractionPath, true);
                }
            }
        }

        private static void InstallValheimArchive(string tempExtractionPath, string archivePath, string targetGameRootDirectory)
        {
            string pluginsDir = Path.Combine(targetGameRootDirectory, "BepInEx", "plugins");
            Directory.CreateDirectory(pluginsDir);

            // Check if this archive is BepInExPack itself (contains BepInEx/core)
            string? bepInExRoot = Directory.GetDirectories(tempExtractionPath, "BepInEx", SearchOption.AllDirectories).FirstOrDefault();
            if (bepInExRoot != null && Directory.Exists(Path.Combine(bepInExRoot, "core")))
            {
                string parentOfBepInEx = Directory.GetParent(bepInExRoot)!.FullName;
                CopyDirectoryCrossDevice(parentOfBepInEx, targetGameRootDirectory);
                Console.WriteLine("✅ Installed BepInEx framework directly into Valheim root!");
                return;
            }

            // Otherwise, package the mod into its own clean subfolder inside BepInEx/plugins/<ModName>
            string modFolderName = Path.GetFileNameWithoutExtension(archivePath).Replace(" ", "_");
            string destinationModFolder = Path.Combine(pluginsDir, modFolderName);

            // If the zip has a nested "plugins" folder, copy from inside that plugins folder
            string? nestedPlugins = Directory.GetDirectories(tempExtractionPath, "plugins", SearchOption.AllDirectories).FirstOrDefault();
            string sourcePayload = nestedPlugins ?? tempExtractionPath;

            if (Directory.Exists(destinationModFolder)) Directory.Delete(destinationModFolder, true);
            CopyDirectoryCrossDevice(sourcePayload, destinationModFolder);
            Console.WriteLine($"✅ Deployed Valheim plugin bundle: {modFolderName}");
        }

        private static void CopyDirectoryCrossDevice(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
                CopyDirectoryCrossDevice(subDir, destSubDir);
            }
        }
    }
}
