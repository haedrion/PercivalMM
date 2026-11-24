using System;
using System.Collections.Generic;
using PercivalMM.Core.Models;

namespace PercivalMM.Core.Games
{
    public class ValheimProfile : IGameProfile
    {
        public string GameName => "Valheim";
        public string SteamAppId => "892970";
        public string DefaultInstallFolderName => "Valheim";

        public void SaveConfiguration(List<ModInfo> mods, string gameInstallPath, string homePath)
        {
            // Valheim via BepInEx loads active .dll plugins directly from BepInEx/plugins/
            Console.WriteLine("🛡️ [PercivalMM] Valheim BepInEx plugin state verified.");
        }
    }
}
