using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PercivalMM.Core.Models;

namespace PercivalMM.Core.Games
{
    public class NoMansSkyProfile : IGameProfile
    {
        public string GameName => "No Man's Sky";
        public string SteamAppId => "275850";
        public string DefaultInstallFolderName => "No Man's Sky";

        public void SaveConfiguration(List<ModInfo> mods, string gameInstallPath, string homePath)
        {
            string settingsDir = Path.Combine(gameInstallPath, "Binaries", "SETTINGS");
            Directory.CreateDirectory(settingsDir);
            string mxmlPath = Path.Combine(settingsDir, "GCMODSETTINGS.MXML");

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine("<Data template=\"cGcModSettings\">");
            sb.AppendLine("  <Property name=\"DisableAllMods\" value=\"false\" />");
            sb.AppendLine("  <Property name=\"Data\">");

            foreach (var mod in mods)
            {
                sb.AppendLine("    <Property name=\"Data\" value=\"GcModSettingsInfo\">");
                sb.AppendLine($"      <Property name=\"Name\" value=\"{mod.Name}\" />");
                sb.AppendLine($"      <Property name=\"Enabled\" value=\"{(mod.IsEnabled ? "true" : "false")}\" />");
                sb.AppendLine($"      <Property name=\"LoadOrderPriority\" value=\"{mod.Priority}\" />");
                sb.AppendLine("    </Property>");
            }

            sb.AppendLine("  </Property>");
            sb.AppendLine("</Data>");

            File.WriteAllText(mxmlPath, sb.ToString());
            Console.WriteLine("🚀 [PercivalMM] No Man's Sky GCMODSETTINGS.MXML synced successfully.");
        }
    }
}
