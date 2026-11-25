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
                    foreach (var entry in archive.Entries.Where(e => !e.IsDirectory && !string.IsNullOrEmpty(e.Key)))
                    {
                        string normalizedRelPath = entry.Key.Replace('\\', '/').TrimStart('/');
                        string fullDestPath = Path.Combine(tempExtractionPath, normalizedRelPath);
                        string? destDir = Path.GetDirectoryName(fullDestPath);
                        if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

                        entry.WriteToFile(fullDestPath, new ExtractionOptions
                        {
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

                // --- NO MAN'S SKY PIPELINE ---
                if (gameName.Contains("No Man's Sky", StringComparison.OrdinalIgnoreCase))
                {
                    InstallNoMansSkyArchive(tempExtractionPath, archivePath, targetGameRootDirectory);
                    return;
                }

                // --- CLAIR OBSCUR: EXPEDITION 33 (UE5) PIPELINE ---
                if (gameName.Contains("Expedition 33", StringComparison.OrdinalIgnoreCase))
                {
                    InstallExpedition33Archive(tempExtractionPath, archivePath, targetGameRootDirectory);
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

            // 1. Unpack nested Vortex .zip/.7z/.rar archives if no top-level .dll exists
            var innerArchives = Directory.GetFiles(tempExtractionPath, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".rar", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (innerArchives.Count > 0 && Directory.GetFiles(tempExtractionPath, "*.dll", SearchOption.AllDirectories).Length == 0)
            {
                string innerExtractDir = Path.Combine(tempExtractionPath, "_inner");
                Directory.CreateDirectory(innerExtractDir);
                                foreach (var inner in innerArchives)
                {
                    using var subArchive = ArchiveFactory.OpenArchive(inner);
                    foreach (var entry in subArchive.Entries.Where(e => !e.IsDirectory && !string.IsNullOrEmpty(e.Key)))
                    {
                        string normalizedRelPath = entry.Key.Replace('\\', '/').TrimStart('/');
                        string fullDestPath = Path.Combine(innerExtractDir, normalizedRelPath);
                        string? destDir = Path.GetDirectoryName(fullDestPath);
                        if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

                        entry.WriteToFile(fullDestPath, new ExtractionOptions
                        {
                            Overwrite = true
                        });
                    }
                }

                tempExtractionPath = innerExtractDir;
            }

            // 2. Detect BepInExPack framework itself (contains BepInEx/core)
            string? bepInExRoot = Directory.GetDirectories(tempExtractionPath, "BepInEx", SearchOption.AllDirectories).FirstOrDefault();
            if (bepInExRoot != null && Directory.Exists(Path.Combine(bepInExRoot, "core")))
            {
                string parentOfBepInEx = Directory.GetParent(bepInExRoot)!.FullName;
                CopyDirectoryCrossDevice(parentOfBepInEx, targetGameRootDirectory);

                // Automatically mark start_game_bepinex.sh (and any .sh scripts) executable on Linux!
                if (!OperatingSystem.IsWindows())
                {
                    foreach (var shFile in Directory.GetFiles(targetGameRootDirectory, "*.sh"))
                    {
                        File.SetUnixFileMode(shFile,
                            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                    }
                }

                Console.WriteLine("✅ Installed BepInEx framework and set executable permissions!");
                return;
            }

                        // 3. Strip Vortex's timestamp and ID suffixes cleanly
            string modFolderName = Path.GetFileNameWithoutExtension(archivePath).Replace(" ", "_");
            int zipIndex = modFolderName.IndexOf(".zip", StringComparison.OrdinalIgnoreCase);
            if (zipIndex > 0) modFolderName = modFolderName.Substring(0, zipIndex);

            int dateIndex = modFolderName.IndexOf("_202");
            if (dateIndex > 0) modFolderName = modFolderName.Substring(0, dateIndex);

            // Keep folder names under 30 chars so Proton never hits MAX_PATH
            if (modFolderName.Length > 30) modFolderName = modFolderName.Substring(0, 30);

            string destinationModFolder = Path.Combine(pluginsDir, modFolderName);
                    string? nestedPlugins = Directory.GetDirectories(tempExtractionPath, "plugins", SearchOption.AllDirectories).FirstOrDefault();
            string sourcePayload = nestedPlugins ?? tempExtractionPath;

            if (nestedPlugins != null)
            {
                var subDirs = Directory.GetDirectories(nestedPlugins);
                var rootFiles = Directory.GetFiles(nestedPlugins);
                if (subDirs.Length == 1)
                {
                    destinationModFolder = Path.Combine(pluginsDir, Path.GetFileName(subDirs[0]));
                    sourcePayload = subDirs[0];
                }
            }

            if (Directory.Exists(destinationModFolder)) Directory.Delete(destinationModFolder, true);
            CopyDirectoryCrossDevice(sourcePayload, destinationModFolder);
            Console.WriteLine($"✅ Deployed Valheim plugin bundle: {Path.GetFileName(destinationModFolder)}");

        }
        private static void InstallNoMansSkyArchive(string tempExtractionPath, string archivePath, string targetGameRootDirectory)
        {
            string nmsModsDir = Path.Combine(targetGameRootDirectory, "GAMEDATA", "MODS");
            Directory.CreateDirectory(nmsModsDir);

            string modFolderName = Path.GetFileNameWithoutExtension(archivePath).Replace(" ", "_");
            int dateIndex = modFolderName.IndexOf("_202");
            if (dateIndex > 0) modFolderName = modFolderName.Substring(0, dateIndex);

            string? nestedMods = Directory.GetDirectories(tempExtractionPath, "MODS", SearchOption.AllDirectories).FirstOrDefault();
            string sourcePayload = nestedMods ?? tempExtractionPath;

            var subDirs = Directory.GetDirectories(sourcePayload);
            var rootFiles = Directory.GetFiles(sourcePayload);
            if (subDirs.Length == 1 && rootFiles.Length == 0)
            {
                modFolderName = Path.GetFileName(subDirs[0]);
                sourcePayload = subDirs[0];
            }

            string destinationPath = Path.Combine(nmsModsDir, modFolderName);
            if (Directory.Exists(destinationPath)) Directory.Delete(destinationPath, true);
            CopyDirectoryCrossDevice(sourcePayload, destinationPath);
            Console.WriteLine($"✅ Deployed No Man's Sky mod bundle: {modFolderName}");
        }
        private static void InstallExpedition33Archive(string tempExtractionPath, string archivePath, string targetGameRootDirectory)
        {
            string win64Dir = Path.Combine(targetGameRootDirectory, "Sandfall", "Binaries", "Win64");
            string paksModsDir = Path.Combine(targetGameRootDirectory, "Sandfall", "Content", "Paks", "~mods");
            string logicModsDir = Path.Combine(targetGameRootDirectory, "Sandfall", "Content", "Paks", "LogicMods");

            Directory.CreateDirectory(paksModsDir);

            // 1. Detect UE4SS framework archive itself (contains dwmapi.dll)
            if (Directory.GetFiles(tempExtractionPath, "dwmapi.dll", SearchOption.AllDirectories).Length > 0)
            {
                Directory.CreateDirectory(win64Dir);
                string? sandfallRoot = Directory.GetDirectories(tempExtractionPath, "Sandfall", SearchOption.AllDirectories).FirstOrDefault();
                string payload = sandfallRoot != null ? Directory.GetParent(sandfallRoot)!.FullName : tempExtractionPath;
                CopyDirectoryCrossDevice(payload, sandfallRoot != null ? targetGameRootDirectory : win64Dir);
                Console.WriteLine("✅ Deployed UE4SS framework into Sandfall/Binaries/Win64!");
                return;
            }

            // Clean up archive name
            string modFolderName = Path.GetFileNameWithoutExtension(archivePath).Replace(" ", "_");
            int dateIndex = modFolderName.IndexOf("_202");
            if (dateIndex > 0) modFolderName = modFolderName.Substring(0, dateIndex);

            // 2. Detect UE4SS Lua Script mod (contains Scripts/ folder + .lua files)
            string? scriptsDir = Directory.GetDirectories(tempExtractionPath, "Scripts", SearchOption.AllDirectories).FirstOrDefault();
            if (scriptsDir != null && Directory.GetFiles(scriptsDir, "*.lua", SearchOption.AllDirectories).Length > 0)
            {
                string ue4ssModsDir = Path.Combine(win64Dir, "ue4ss", "Mods");
                Directory.CreateDirectory(ue4ssModsDir);

                string modWrapper = Directory.GetParent(scriptsDir)!.FullName;
                string luaModName = modWrapper != tempExtractionPath ? Path.GetFileName(modWrapper) : modFolderName;
                string destLuaDir = Path.Combine(ue4ssModsDir, luaModName);

                if (Directory.Exists(destLuaDir)) Directory.Delete(destLuaDir, true);
                CopyDirectoryCrossDevice(modWrapper, destLuaDir);
                Console.WriteLine($"✅ Deployed Expedition 33 Lua script mod: {luaModName}");
                return;
            }

            // 3. Detect LogicMods blueprint pak vs. standard ~mods pak (.pak/.ucas/.utoc)
            bool isLogicMod = Directory.GetDirectories(tempExtractionPath, "LogicMods", SearchOption.AllDirectories).Length > 0;
            string targetRoot = isLogicMod ? logicModsDir : paksModsDir;
            Directory.CreateDirectory(targetRoot);

            string? nestedTildeMods = Directory.GetDirectories(tempExtractionPath, "~mods", SearchOption.AllDirectories).FirstOrDefault()
                                   ?? Directory.GetDirectories(tempExtractionPath, "LogicMods", SearchOption.AllDirectories).FirstOrDefault();
            string sourcePayload = nestedTildeMods ?? tempExtractionPath;

            var subDirs = Directory.GetDirectories(sourcePayload);
            var rootFiles = Directory.GetFiles(sourcePayload);
            if (subDirs.Length == 1 && rootFiles.Length == 0)
            {
                modFolderName = Path.GetFileName(subDirs[0]);
                sourcePayload = subDirs[0];
            }

            string destinationPath = Path.Combine(targetRoot, modFolderName);
            if (Directory.Exists(destinationPath)) Directory.Delete(destinationPath, true);
            CopyDirectoryCrossDevice(sourcePayload, destinationPath);
            Console.WriteLine($"✅ Deployed Expedition 33 UE5 mod bundle: {modFolderName}");
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
