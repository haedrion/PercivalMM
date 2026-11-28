using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PercivalMM.Core.Models;
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
                if (archivePath.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) && File.Exists("/usr/bin/7z"))
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "/usr/bin/7z",
                        Arguments = $"x \"{archivePath}\" -o\"{tempExtractionPath}\" -y",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var proc = System.Diagnostics.Process.Start(psi);
                    if (proc != null)
                    {
                        proc.WaitForExit();
                        if (proc.ExitCode != 0)
                        {
                            string err = proc.StandardError.ReadToEnd();
                            throw new Exception($"Native 7z extraction failed (exit {proc.ExitCode}): {err}");
                        }
                    }
                }
                else
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

                // --- DIVINITY: ORIGINAL SIN 2 DEFINITIVE EDITION PIPELINE ---
                if (gameName.Contains("Divinity", StringComparison.OrdinalIgnoreCase))
                {
                    InstallDivinityOS2Archive(tempExtractionPath, archivePath, targetGameRootDirectory, gameName);
                    return;
                }

                // --- THE WITCHER 3 CONTAINER PIPELINE ---
                InstallWitcher3Archive(tempExtractionPath, archivePath, targetGameRootDirectory, gameName);
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
        private static void InstallWitcher3Archive(string tempExtractionPath, string archivePath, string targetGameRootDirectory, string gameName)
        {
            string displayName = ModContainerService.CleanArchiveDisplayName(archivePath);
            string containerDir = ModContainerService.GetContainerDirectory(targetGameRootDirectory, displayName);
            if (Directory.Exists(containerDir)) Directory.Delete(containerDir, true);

            string containerModsDir = Path.Combine(containerDir, "mods");
            string containerDlcDir = Path.Combine(containerDir, "dlc");
            string containerBinDir = Path.Combine(containerDir, "bin");

            string[] discoveredDirectories = Directory.GetDirectories(tempExtractionPath, "*", SearchOption.AllDirectories);
            var validModFolders = new List<string>();
            var validDlcFolders = new List<string>();
            string? discoveredBinFolder = null;

            foreach (var dir in discoveredDirectories)
            {
                string folderName = Path.GetFileName(dir);
                string lowerName = folderName.ToLowerInvariant();

                if (lowerName != "mods" && lowerName.StartsWith("mod"))
                    validModFolders.Add(dir);
                else if (lowerName != "dlc" && lowerName.StartsWith("dlc"))
                    validDlcFolders.Add(dir);
                else if (lowerName == "bin" && discoveredBinFolder == null)
                    discoveredBinFolder = dir;
            }

            if (validModFolders.Count == 0 && validDlcFolders.Count == 0 && discoveredBinFolder == null)
            {
                string cleanModFolder = "mod_" + ModContainerService.SanitizeName(displayName).Replace("_", "");
                CopyDirectoryCrossDevice(tempExtractionPath, Path.Combine(containerModsDir, cleanModFolder));
            }
            else
            {
                foreach (string sourceFolder in validModFolders)
                {
                    string folderName = Path.GetFileName(sourceFolder);
                    CopyDirectoryCrossDevice(sourceFolder, Path.Combine(containerModsDir, folderName));
                }

                foreach (string sourceFolder in validDlcFolders)
                {
                    string folderName = Path.GetFileName(sourceFolder);
                    CopyDirectoryCrossDevice(sourceFolder, Path.Combine(containerDlcDir, folderName));
                }

                if (discoveredBinFolder != null)
                {
                    CopyDirectoryCrossDevice(discoveredBinFolder, containerBinDir);
                }
            }

            bool isAssetOnly = validModFolders.Count == 0 && validDlcFolders.Count > 0;
            var manifest = new ModContainerManifest
            {
                DisplayName = displayName,
                GameName = gameName,
                Tier = isAssetOnly ? ModTier.Assets : ModTier.Core,
                IsEnabled = true
            };
            ModContainerService.SaveManifest(containerDir, manifest);
            Console.WriteLine($"✅ Staged Witcher 3 container '{displayName}' ({validModFolders.Count} mod, {validDlcFolders.Count} dlc).");
        }
        private static void InstallValheimArchive(string tempExtractionPath, string archivePath, string targetGameRootDirectory)
        {
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

            string? bepInExRoot = Directory.GetDirectories(tempExtractionPath, "BepInEx", SearchOption.AllDirectories).FirstOrDefault();
            if (bepInExRoot != null && Directory.Exists(Path.Combine(bepInExRoot, "core")))
            {
                string parentOfBepInEx = Directory.GetParent(bepInExRoot)!.FullName;
                CopyDirectoryCrossDevice(parentOfBepInEx, targetGameRootDirectory);

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

            if (ValheimAssetPackService.IsValheimTextureAssetPack(tempExtractionPath))
            {
                string packName = Path.GetFileNameWithoutExtension(archivePath).Replace(" ", "_");
                int zipIdx = packName.IndexOf(".zip", StringComparison.OrdinalIgnoreCase);
                if (zipIdx > 0) packName = packName.Substring(0, zipIdx);
                int dateIdx = packName.IndexOf("_202");
                if (dateIdx > 0) packName = packName.Substring(0, dateIdx);

                string stagingPackDir = ValheimAssetPackService.GetStagingPackDirectory(targetGameRootDirectory, packName);
                if (Directory.Exists(stagingPackDir)) Directory.Delete(stagingPackDir, true);

                CopyDirectoryCrossDevice(tempExtractionPath, stagingPackDir);
                ValheimAssetPackService.NormalizeStagedPackLayout(stagingPackDir);
                File.WriteAllText(Path.Combine(stagingPackDir, ".percival_tier"), "Assets");

                Console.WriteLine($"🎨 Staged Valheim HD Texture / Asset Pack: {packName}");
                return;
            }

            string displayName = ModContainerService.CleanArchiveDisplayName(archivePath);
            string containerDir = ModContainerService.GetContainerDirectory(targetGameRootDirectory, displayName);
            if (Directory.Exists(containerDir)) Directory.Delete(containerDir, true);

            string modFolderName = ModContainerService.SanitizeName(displayName);
            if (modFolderName.Length > 30) modFolderName = modFolderName.Substring(0, 30);

            string? nestedPlugins = Directory.GetDirectories(tempExtractionPath, "plugins", SearchOption.AllDirectories).FirstOrDefault();
            string sourcePayload = nestedPlugins ?? tempExtractionPath;

            if (nestedPlugins != null)
            {
                var subDirs = Directory.GetDirectories(nestedPlugins);
                if (subDirs.Length == 1)
                {
                    modFolderName = Path.GetFileName(subDirs[0]);
                    sourcePayload = subDirs[0];
                }
            }

            string containerPluginDest = Path.Combine(containerDir, "BepInEx", "plugins", modFolderName);
            CopyDirectoryCrossDevice(sourcePayload, containerPluginDest);

            ModContainerService.SaveManifest(containerDir, new ModContainerManifest
            {
                DisplayName = displayName,
                GameName = "Valheim",
                Tier = ModTier.Core,
                IsEnabled = true
            });
            Console.WriteLine($"✅ Staged Valheim plugin container: {displayName}");
        }
                private static void InstallNoMansSkyArchive(string tempExtractionPath, string archivePath, string targetGameRootDirectory)
        {
            string displayName = ModContainerService.CleanArchiveDisplayName(archivePath);
            string containerDir = ModContainerService.GetContainerDirectory(targetGameRootDirectory, displayName);
            if (Directory.Exists(containerDir)) Directory.Delete(containerDir, true);

            string modFolderName = ModContainerService.SanitizeName(displayName);
            string? nestedMods = Directory.GetDirectories(tempExtractionPath, "MODS", SearchOption.AllDirectories).FirstOrDefault();
            string sourcePayload = nestedMods ?? tempExtractionPath;

            // Unwrap single inner mod folder even if author included README.txt / preview images alongside it
            var subDirs = Directory.GetDirectories(sourcePayload);
            var nonDocFiles = Directory.GetFiles(sourcePayload)
                .Where(f => !f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                         && !f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                         && !f.EndsWith(".url", StringComparison.OrdinalIgnoreCase)
                         && !f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                         && !f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (subDirs.Length == 1 && nonDocFiles.Length == 0)
            {
                modFolderName = Path.GetFileName(subDirs[0]);
                sourcePayload = subDirs[0];
            }

            // Detect legacy pre-Worlds single .pak mods so user knows why NMS might skip them
            var pakFiles = Directory.GetFiles(sourcePayload, "*.pak", SearchOption.AllDirectories);
            var mbinFiles = Directory.GetFiles(sourcePayload, "*.mbin", SearchOption.AllDirectories);
            if (pakFiles.Length > 0 && mbinFiles.Length == 0)
            {
                Console.WriteLine($"⚠️ [NMS Warning] '{displayName}' contains legacy .pak archive(s) (pre-Worlds Part II format) — modern NMS may ignore unextracted .pak mods.");
            }

            string containerDest = Path.Combine(containerDir, "GAMEDATA", "MODS", modFolderName);
            CopyDirectoryCrossDevice(sourcePayload, containerDest);

            ModContainerService.SaveManifest(containerDir, new ModContainerManifest
            {
                DisplayName = displayName,
                GameName = "No Man's Sky",
                Tier = ModTier.Core,
                IsEnabled = true
            });
            Console.WriteLine($"✅ Staged No Man's Sky mod container: {displayName} (folder: {modFolderName})");
        }


        private static void InstallExpedition33Archive(string tempExtractionPath, string archivePath, string targetGameRootDirectory)
        {
            string win64Dir = Path.Combine(targetGameRootDirectory, "Sandfall", "Binaries", "Win64");

            if (Directory.GetFiles(tempExtractionPath, "dwmapi.dll", SearchOption.AllDirectories).Length > 0)
            {
                Directory.CreateDirectory(win64Dir);
                string? sandfallRoot = Directory.GetDirectories(tempExtractionPath, "Sandfall", SearchOption.AllDirectories).FirstOrDefault();
                string payload = sandfallRoot != null ? Directory.GetParent(sandfallRoot)!.FullName : tempExtractionPath;
                CopyDirectoryCrossDevice(payload, sandfallRoot != null ? targetGameRootDirectory : win64Dir);
                Console.WriteLine("✅ Deployed UE4SS framework into Sandfall/Binaries/Win64!");
                return;
            }

            string displayName = ModContainerService.CleanArchiveDisplayName(archivePath);
            string containerDir = ModContainerService.GetContainerDirectory(targetGameRootDirectory, displayName);
            if (Directory.Exists(containerDir)) Directory.Delete(containerDir, true);

            string modFolderName = ModContainerService.SanitizeName(displayName);

            string? scriptsDir = Directory.GetDirectories(tempExtractionPath, "Scripts", SearchOption.AllDirectories).FirstOrDefault();
            if (scriptsDir != null && Directory.GetFiles(scriptsDir, "*.lua", SearchOption.AllDirectories).Length > 0)
            {
                string modWrapper = Directory.GetParent(scriptsDir)!.FullName;
                string luaModName = modWrapper != tempExtractionPath ? Path.GetFileName(modWrapper) : modFolderName;
                string destLuaDir = Path.Combine(containerDir, "Sandfall", "Binaries", "Win64", "ue4ss", "Mods", luaModName);

                CopyDirectoryCrossDevice(modWrapper, destLuaDir);
                ModContainerService.SaveManifest(containerDir, new ModContainerManifest
                {
                    DisplayName = displayName,
                    GameName = "Clair Obscur: Expedition 33",
                    Tier = ModTier.Core,
                    IsEnabled = true
                });
                Console.WriteLine($"✅ Staged Expedition 33 Lua script container: {displayName}");
                return;
            }

            bool isLogicMod = Directory.GetDirectories(tempExtractionPath, "LogicMods", SearchOption.AllDirectories).Length > 0;
            string relSubRoot = isLogicMod
                ? Path.Combine("Sandfall", "Content", "Paks", "LogicMods")
                : Path.Combine("Sandfall", "Content", "Paks", "~mods");

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

            string containerDest = Path.Combine(containerDir, relSubRoot, modFolderName);
            CopyDirectoryCrossDevice(sourcePayload, containerDest);

            ModContainerService.SaveManifest(containerDir, new ModContainerManifest
            {
                DisplayName = displayName,
                GameName = "Clair Obscur: Expedition 33",
                Tier = ModTier.Core,
                IsEnabled = true
            });
            Console.WriteLine($"✅ Staged Expedition 33 UE5 mod container: {displayName}");
        }

        private static void InstallDivinityOS2Archive(string tempExtractionPath, string archivePath, string targetGameRootDirectory, string gameName)
        {
            // 1. Detect Norbyte's Script Extender (DXGI.dll) -> drops directly into DefEd/bin/
            var dxgiFiles = Directory.GetFiles(tempExtractionPath, "DXGI.dll", SearchOption.AllDirectories);
            if (dxgiFiles.Length > 0)
            {
                string defEdBinDir = Path.Combine(targetGameRootDirectory, "DefEd", "bin");
                Directory.CreateDirectory(defEdBinDir);
                foreach (var dll in dxgiFiles)
                {
                    File.Copy(dll, Path.Combine(defEdBinDir, Path.GetFileName(dll)), true);
                }
                Console.WriteLine("✅ Deployed Norbyte Script Extender (DXGI.dll) into DefEd/bin! Remember WINEDLLOVERRIDES=\"dxgi=n,b\" in Steam.");
                return;
            }

            // 2. Stage .pak files into container under Larian Studios/Divinity Original Sin 2 Definitive Edition/Mods/
            string displayName = ModContainerService.CleanArchiveDisplayName(archivePath);
            string containerDir = ModContainerService.GetContainerDirectory(targetGameRootDirectory, displayName);
            if (Directory.Exists(containerDir)) Directory.Delete(containerDir, true);

            string containerModsDir = Path.Combine(
                containerDir,
                "Larian Studios",
                "Divinity Original Sin 2 Definitive Edition",
                "Mods");
            Directory.CreateDirectory(containerModsDir);

            var pakFiles = Directory.GetFiles(tempExtractionPath, "*.pak", SearchOption.AllDirectories);
            foreach (var pak in pakFiles)
            {
                File.Copy(pak, Path.Combine(containerModsDir, Path.GetFileName(pak)), true);
            }

            ModContainerService.SaveManifest(containerDir, new ModContainerManifest
            {
                DisplayName = displayName,
                GameName = gameName,
                Tier = ModTier.Core,
                IsEnabled = true
            });
            Console.WriteLine($"✅ Staged Divinity: OS 2 container '{displayName}' ({pakFiles.Length} .pak file(s)).");
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
