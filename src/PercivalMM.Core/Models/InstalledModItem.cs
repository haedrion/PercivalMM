using System;

namespace PercivalMM.Core.Models
{
    public enum ModTier
    {
        Core,
        Assets
    }

    public class InstalledModItem
    {
        public string Name { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public string ModType { get; set; } = "MOD"; // "MOD" or "DLC"
        public int Priority { get; set; }
        public bool IsEnabled { get; set; }
        public ModTier Tier { get; set; } = ModTier.Core;
        public ModItemKind ItemKind { get; set; } = ModItemKind.StandardMod;
        public string CategoryGroup { get; set; } = "General";
        public string? StagingPath { get; set; }
        public bool IsSeparator => ItemKind == ModItemKind.Separator;
    }

    public enum ModItemKind
    {
        StandardMod = 0,
        TextureAssetPack = 1,
        Separator = 2 // Future Skyrim SE/AE categories (UI, Characters, Armor, etc.)
    }

}
