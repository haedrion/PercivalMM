using System.Collections.Generic;

namespace PercivalMM.Core.Models
{
    // The Holy Grail blueprint. Any game added to CamelotHUB 
    // will fill out this questionnaire.
    public interface IGameProfile
    {
        string GameName { get; }
        string SteamAppId { get; }
        string DefaultInstallFolderName { get; }

        // Handles custom priority-list deployments like mod.settings
        void SaveConfiguration(List<ModInfo> mods, string gameInstallPath, string homePath);
    }

    // A structured token to safely represent individual active modifications
    public class ModInfo
    {
        public string Name { get; set; }
        public string AbsolutePath { get; set; }
        public int Priority { get; set; }
        public bool IsEnabled { get; set; }
    }
}
