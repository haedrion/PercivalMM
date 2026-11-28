using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PercivalMM.Core.Models;

namespace PercivalMM.Core.Services;

public class ProfileService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string GetProfilesDirectory(string gameInstallPath)
        => Path.Combine(gameInstallPath, ValheimAssetPackService.StagingRootName, "Profiles");

    public List<PlaythroughProfile> LoadProfiles(string gameInstallPath, string gameName)
    {
        string dir = GetProfilesDirectory(gameInstallPath);
        Directory.CreateDirectory(dir);

        var profiles = new List<PlaythroughProfile>();
        foreach (string file in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                string json = File.ReadAllText(file);
                var profile = JsonSerializer.Deserialize<PlaythroughProfile>(json, JsonOptions);
                if (profile != null && profile.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase))
                    profiles.Add(profile);
            }
            catch
            {
                // Skip malformed profile files
            }
        }

        return profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void SaveProfile(string gameInstallPath, PlaythroughProfile profile)
    {
        string dir = GetProfilesDirectory(gameInstallPath);
        Directory.CreateDirectory(dir);

        profile.LastModifiedUtc = DateTime.UtcNow;
        string filePath = Path.Combine(dir, $"{profile.Id}.json");
        string json = JsonSerializer.Serialize(profile, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    public void DeleteProfile(string gameInstallPath, PlaythroughProfile profile)
    {
        string filePath = Path.Combine(GetProfilesDirectory(gameInstallPath), $"{profile.Id}.json");
        if (File.Exists(filePath))
            File.Delete(filePath);
    }

    public PlaythroughProfile CaptureCurrentState(
        string profileName,
        string gameName,
        IEnumerable<InstalledModItem> mods,
        IEnumerable<InstalledModItem> assetPacks,
        string? existingId = null)
    {
        return new PlaythroughProfile
        {
            Id = existingId ?? Guid.NewGuid().ToString("N"),
            Name = profileName.Trim(),
            GameName = gameName,
            LastModifiedUtc = DateTime.UtcNow,
            Mods = mods.Select(m => new ProfileModEntry
            {
                Name = m.Name,
                FolderPath = m.FolderPath,
                StagingPath = m.StagingPath,
                IsEnabled = m.IsEnabled,
                Priority = m.Priority,
                IsSeparator = m.IsSeparator
            }).ToList(),
            AssetPacks = assetPacks.Select(a => new ProfileModEntry
            {
                Name = a.Name,
                FolderPath = a.FolderPath,
                StagingPath = a.StagingPath,
                IsEnabled = a.IsEnabled,
                Priority = a.Priority,
                IsSeparator = a.IsSeparator
            }).ToList()
        };
    }

    public void ApplyProfileToLists(
        PlaythroughProfile profile,
        IList<InstalledModItem> currentMods,
        IList<InstalledModItem> currentAssetPacks)
    {
        ApplyEntries(profile.Mods, currentMods);
        ApplyEntries(profile.AssetPacks, currentAssetPacks);
    }

        private static void ApplyEntries(List<ProfileModEntry> savedEntries, IList<InstalledModItem> liveItems)
    {
        var byName = savedEntries
            .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        int nextPriority = savedEntries.Count + 1;

        foreach (var item in liveItems)
        {
            if (byName.TryGetValue(item.Name, out var saved))
            {
                item.IsEnabled = saved.IsEnabled;
                item.Priority = saved.Priority;
            }
            else
            {
                item.IsEnabled = false;
                item.Priority = nextPriority++;
            }
        }

        var sorted = liveItems.OrderBy(i => i.Priority).ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            sorted[i].Priority = i + 1;
            liveItems[i] = sorted[i];
        }
    }
}