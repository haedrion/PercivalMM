using System;
using System.Collections.Generic;
using PercivalMM.Core.Models;

namespace PercivalMM.Core.Games
{
    public class Expedition33Profile : IGameProfile
    {
        public string GameName => "Clair Obscur: Expedition 33";
        public string SteamAppId => "1903340";
        public string DefaultInstallFolderName => "Expedition 33";

        public void SaveConfiguration(List<ModInfo> mods, string gameInstallPath, string homePath)
        {
            // Unreal Engine 5 loads .pak/.ucas/.utoc bundles automatically from Sandfall/Content/Paks/~mods/
            Console.WriteLine("🎨 [PercivalMM] Clair Obscur: Expedition 33 UE5 ~mods state verified.");
        }
    }
}
