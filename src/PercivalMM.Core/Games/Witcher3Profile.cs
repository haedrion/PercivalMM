using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using PercivalMM.Core.Models;

namespace PercivalMM.Core.Games
{
    public class Witcher3Profile : IGameProfile
    {
        public string GameName => "The Witcher 3: Wild Hunt";
        public string SteamAppId => "292030";
        public string DefaultInstallFolderName => "The Witcher 3";

        public void SaveConfiguration(List<ModInfo> mods, string gameInstallPath, string homePath)
        {
            // Maps straight to the isolated user-profile layout inside the Proton prefix
            string prefixDocs = Path.Combine(
                homePath, 
                ".local/share/Steam/steamapps/compatdata", 
                SteamAppId, 
                "pfx/drive_c/users/steamuser/Documents/The Witcher 3"
            );
            
            string settingsFile = Path.Combine(prefixDocs, "mod.settings");
            StringBuilder sb = new StringBuilder();
            
            foreach (var mod in mods)
            {
                if (mod.IsEnabled)
                {
                    sb.AppendLine($"[{mod.Name}]");
                    sb.AppendLine($"Enabled=1");
                    sb.AppendLine($"Priority={mod.Priority}"); 
                    sb.AppendLine();
                }
            }

            if (!Directory.Exists(prefixDocs)) Directory.CreateDirectory(prefixDocs);
            File.WriteAllText(settingsFile, sb.ToString());
            Console.WriteLine("🛡️ [PercivalMM] Witcher 3 load order written to Proton prefix successfully.");
        }
    }
}
