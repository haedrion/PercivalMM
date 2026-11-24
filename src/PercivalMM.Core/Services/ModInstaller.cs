using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;

namespace PercivalMM.Core.Services
{
    public class ModInstaller
    {
        public static void InstallZipMod(string zipPath, string targetGameRootDirectory)
        {
            Console.WriteLine($"\n📦 [PercivalMM] Staging installation archive: {Path.GetFileName(zipPath)}");

            if (!File.Exists(zipPath))
            {
                Console.WriteLine("❌ Error: Downloaded archive file not found.");
                return;
            }

            string targetModsDir = Path.Combine(targetGameRootDirectory, "mods");
            string targetDlcDir = Path.Combine(targetGameRootDirectory, "dlc");
            string targetBinDir = Path.Combine(targetGameRootDirectory, "bin");

            Directory.CreateDirectory(targetModsDir);
            Directory.CreateDirectory(targetDlcDir);

            string workingBaseDir = Path.GetDirectoryName(zipPath) ?? Path.GetTempPath();
            string tempExtractionPath = Path.Combine(workingBaseDir, "PercivalMM_Temp_" + Guid.NewGuid().ToString());

            try
            {
                ZipFile.ExtractToDirectory(zipPath, tempExtractionPath);

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

                // Fallback Layout: If no standard mod*/dlc*/bin folders exist
                if (validModFolders.Count == 0 && validDlcFolders.Count == 0 && discoveredBinFolder == null)
                {
                    Console.WriteLine("⚠️ Warning: Archive lacks standard 'mod/dlc' directory formatting.");
                    string cleanArchiveName = "mod_" + Path.GetFileNameWithoutExtension(zipPath).Replace(" ", "");
                    string fallbackDestination = Path.Combine(targetModsDir, cleanArchiveName);

                    if (Directory.Exists(fallbackDestination)) Directory.Delete(fallbackDestination, true);
                    CopyDirectoryCrossDevice(tempExtractionPath, fallbackDestination);
                    return;
                }

                // 1. Deploy mod* bundles -> /mods/
                foreach (string sourceFolder in validModFolders)
                {
                    string folderName = Path.GetFileName(sourceFolder);
                    string destinationPath = Path.Combine(targetModsDir, folderName);

                    if (Directory.Exists(destinationPath)) Directory.Delete(destinationPath, true);
                    CopyDirectoryCrossDevice(sourceFolder, destinationPath);
                    Console.WriteLine($"✅ Deployed MOD bundle: {folderName}");
                }

                // 2. Deploy dlc* bundles -> /dlc/
                foreach (string sourceFolder in validDlcFolders)
                {
                    string folderName = Path.GetFileName(sourceFolder);
                    string destinationPath = Path.Combine(targetDlcDir, folderName);

                    if (Directory.Exists(destinationPath)) Directory.Delete(destinationPath, true);
                    CopyDirectoryCrossDevice(sourceFolder, destinationPath);
                    Console.WriteLine($"✅ Deployed DLC bundle: {folderName}");
                }

                // 3. Merge bin/ menu configs -> /bin/ (non-destructive merge!)
                if (discoveredBinFolder != null)
                {
                    CopyDirectoryCrossDevice(discoveredBinFolder, targetBinDir);
                    Console.WriteLine("✅ Merged custom Mod Menu configuration files into /bin/");
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
