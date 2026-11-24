using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PercivalMM.Core.Services
{
    public static class ValheimHealthCheckService
    {
        public static List<string> RunDiagnostics(string valheimRootPath)
        {
            var report = new List<string>();

            // 1. Check if BepInEx core is installed
            string bepInExCore = Path.Combine(valheimRootPath, "BepInEx", "core");
            if (!Directory.Exists(bepInExCore))
            {
                report.Add("⚠️ [Valheim Setup] BepInEx framework is NOT installed yet! Import 'BepInExPack_Valheim' first.");
                return report;
            }

            // 2. Check each installed plugin folder for an actual .dll file
            string pluginsDir = Path.Combine(valheimRootPath, "BepInEx", "plugins");
            if (Directory.Exists(pluginsDir))
            {
                foreach (var modFolder in Directory.GetDirectories(pluginsDir))
                {
                    string modName = Path.GetFileName(modFolder);
                    var dlls = Directory.GetFiles(modFolder, "*.dll", SearchOption.AllDirectories);
                    if (dlls.Length == 0)
                    {
                        report.Add($"⚠️ [Mod Warning] '{modName}' contains NO .dll plugin file (may be a texture pack or needs re-importing).");
                    }
                }
            }

            // 3. Read BepInEx/LogOutput.log from the last game launch to catch broken/outdated mods
            string logPath = Path.Combine(valheimRootPath, "BepInEx", "LogOutput.log");
            if (File.Exists(logPath))
            {
                var lines = File.ReadAllLines(logPath);
                var loadedMods = lines.Where(l => l.Contains("Loading [")).ToList();
                var errorLines = lines.Where(l => l.Contains("[Error  :") || l.Contains("MissingMethodException") || l.Contains("TypeLoadException")).ToList();

                if (loadedMods.Count > 0)
                {
                    report.Add($"✅ [Last Launch] BepInEx loaded {loadedMods.Count} plugin(s) in-game.");
                }

                if (errorLines.Count > 0)
                {
                    report.Add($"❌ [Mod Crash Detected] {errorLines.Count} error(s) in BepInEx log (likely an outdated mod for this Valheim patch):");
                    foreach (var err in errorLines.Take(4))
                    {
                        report.Add($"   -> {err.Trim()}");
                    }
                }
            }

            return report;
        }
    }
}
