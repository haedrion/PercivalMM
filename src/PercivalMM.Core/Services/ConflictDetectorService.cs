using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PercivalMM.Core.Models;

namespace PercivalMM.Core.Services
{
    public static class ConflictDetectorService
    {
        public static List<string> ScanForConflicts(IEnumerable<InstalledModItem> mods)
        {
            // Maps relative file path (e.g. "content/scripts/game/player/playerWitcher.ws") -> list of active mod names touching it
            var fileOwners = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var mod in mods.Where(m => m.IsEnabled && Directory.Exists(m.FolderPath)))
            {
                foreach (var filePath in Directory.GetFiles(mod.FolderPath, "*", SearchOption.AllDirectories))
                {
                    string relativePath = Path.GetRelativePath(mod.FolderPath, filePath);

                    if (!fileOwners.TryGetValue(relativePath, out var owners))
                    {
                        owners = new List<string>();
                        fileOwners[relativePath] = owners;
                    }

                    if (!owners.Contains(mod.Name))
                    {
                        owners.Add(mod.Name);
                    }
                }
            }

            var conflicts = new List<string>();
            foreach (var kvp in fileOwners.Where(k => k.Value.Count > 1))
            {
                string winningMod = kvp.Value.First(); // Rank #1 priority wins in Witcher 3
                string involvedMods = string.Join(" vs. ", kvp.Value);
                conflicts.Add($"⚠️ Overlap on '{kvp.Key}' ({involvedMods}) -> '{winningMod}' takes priority.");
            }

            return conflicts;
        }
    }
}
