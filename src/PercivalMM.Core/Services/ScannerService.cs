using System;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PercivalMM.Core.Services
{
    public class ScannerService
    {
        public static List<string> FindAllSteamLibraries(string homePath)
        {
            var libraryPaths = new List<string>();
            string defaultSteam = Path.Combine(homePath, ".local/share/Steam");
            
            // 1. Always add the default internal storage drive path
            libraryPaths.Add(defaultSteam);

            // 2. Add your explicit custom GamingSSD path directly to the core detection radar
            string customGamingSSD = "/media/Haedrion/GamingSSD/SteamLibrary";
            if (Directory.Exists(customGamingSSD))
            {
                libraryPaths.Add(customGamingSSD);
            }

            // 3. Scan Valve's configuration map file as a dynamic fallback engine
            string vdfPath = Path.Combine(defaultSteam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdfPath))
            {
                try
                {
                    string vdfContent = File.ReadAllText(vdfPath);
                    
                    // Upgraded regex to catch paths wrapped in tabs, spaces, or various quotation formats
                    var matches = Regex.Matches(vdfContent, @"""path""\s+""([^""]+)""");
                    
                    foreach (Match match in matches)
                    {
                        string discoveredPath = match.Groups[1].Value.Replace(@"\\", "/");
                        if (Directory.Exists(discoveredPath) && !libraryPaths.Contains(discoveredPath))
                        {
                            libraryPaths.Add(discoveredPath);
                        }
                    }
                }
                catch (Exception)
                {
                    // Fail silently and rely on our hardcoded paths if the file is locked or corrupted
                }
            }
            return libraryPaths;
        }

        public static string LocateGamePath(string homePath, string folderName)
        {
            var activeLibraries = FindAllSteamLibraries(homePath);

            foreach (var library in activeLibraries)
            {
                // Steam games can live in 'steamapps/common/' relative to the library path base directory
                string checkPath1 = Path.Combine(library, "steamapps", "common", folderName);
                if (Directory.Exists(checkPath1))
                {
                    return checkPath1;
                }

                // Fallback check if the path returned from libraryfolders.vdf already included steamapps/common/
                string checkPath2 = Path.Combine(library, "common", folderName);
                if (Directory.Exists(checkPath2))
                {
                    return checkPath2;
                }
                
                if (library.EndsWith(folderName) && Directory.Exists(library))
                {
                    return library;
                }
            }
            return null;
        }
    }
}
