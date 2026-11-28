using System;
using System.Collections.Generic;

namespace PercivalMM.Core.Models;

public class PlaythroughProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Default Profile";
    public string GameName { get; set; } = "Valheim";
    public DateTime LastModifiedUtc { get; set; } = DateTime.UtcNow;

    public List<ProfileModEntry> Mods { get; set; } = new();
    public List<ProfileModEntry> AssetPacks { get; set; } = new();

    public override string ToString() => Name;
}

public class ProfileModEntry
{
    public string Name { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public string? StagingPath { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int Priority { get; set; }
    public bool IsSeparator { get; set; }
}
