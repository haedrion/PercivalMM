using System;

namespace PercivalMM.Core.Models
{
    public class InstalledModItem
    {
        public string Name { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public string ModType { get; set; } = "MOD"; // "MOD" or "DLC"
        public int Priority { get; set; }
        public bool IsEnabled { get; set; }
    }
}
