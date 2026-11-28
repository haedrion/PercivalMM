using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Input;
using PercivalMM.Core.Games;
using PercivalMM.Core.Models;
using PercivalMM.Core.Services;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace PercivalMM.UI.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private string _discoveredGamePathText = "Storage arrays unscanned. Awaiting pipeline initialization command...";
        private string _telemetryLogStreamText = "[System Init] Core app modules loaded.\n[System Init] Multi-game profile registry active.";
        private IGameProfile _selectedGame;

        public ObservableCollection<IGameProfile> AvailableGames { get; } = new();
        public ObservableCollection<InstalledModItem> CoreMods { get; } = new();
        public ObservableCollection<InstalledModItem> AssetMods { get; } = new();
        public IEnumerable<InstalledModItem> InstalledMods => CoreMods.Concat(AssetMods);

        private readonly ProfileService _profileService = new();
        public ObservableCollection<PlaythroughProfile> Profiles { get; } = new();

        private PlaythroughProfile? _selectedProfile;
        public PlaythroughProfile? SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (_selectedProfile == value) return;
                _selectedProfile = value;
                RaisePropertyChanged();
                if (value != null)
                    ApplySelectedProfile(value);
            }
        }

        private string _newProfileName = string.Empty;
        public string NewProfileName
        {
            get => _newProfileName;
            set
            {
                if (_newProfileName == value) return;
                _newProfileName = value;
                RaisePropertyChanged();
            }
        }

        public IGameProfile SelectedGame
        {
            get => _selectedGame;
            set
            {
                if (_selectedGame != value && value != null)
                {
                    _selectedGame = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(LaunchButtonText));
                    RaisePropertyChanged(nameof(ActiveGameBanner));

                    ExecuteLiveStorageDriveScan();
                }
            }
        }

        private GameDefinition? ActiveGameDef => SelectedGame as GameDefinition;

        public string LaunchButtonText => ActiveGameDef?.LaunchButtonLabel ?? "⚔️ Launch Game";

        public Bitmap? ActiveGameBanner
        {
            get
            {
                string fileName = ActiveGameDef?.BannerFileName ?? "witcher3.png";

                try
                {
                    var uri = new Uri($"avares://PercivalMM.UI/Assets/Banners/{fileName}");
                    if (AssetLoader.Exists(uri))
                    {
                        return new Bitmap(AssetLoader.Open(uri));
                    }
                }
                catch { }

                return null;
            }
        }
        public string DiscoveredGamePathText
        {
            get => _discoveredGamePathText;
            set { _discoveredGamePathText = value; RaisePropertyChanged(); }
        }

        public string TelemetryLogStreamText
        {
            get => _telemetryLogStreamText;
            set { _telemetryLogStreamText = value; RaisePropertyChanged(); }
        }

        public ICommand TriggerSSDScanCommand { get; }
        public ICommand ToggleModCommand { get; }
        public ICommand MoveModUpCommand { get; }
        public ICommand MoveModDownCommand { get; }
        public ICommand DeleteModCommand { get; }
        public ICommand LaunchGameCommand { get; }
        public ICommand SwitchModTierCommand { get; }
        public ICommand SaveProfileCommand { get; }
        public ICommand DeleteProfileCommand { get; }

        private string _nexusApiKey = NxmHandlerService.LoadApiKey();
        public string NexusApiKey
        {
            get => _nexusApiKey;
            set
            {
                if (_nexusApiKey == value) return;
                _nexusApiKey = value;
                RaisePropertyChanged();
                NxmHandlerService.SaveApiKey(value);
            }
        }

        private bool _isNxmRegistered = NxmHandlerService.IsLinuxNxmHandlerRegistered();
        public string NxmHandlerButtonText => _isNxmRegistered ? "🔌 Unregister nxm://" : "🔗 Register nxm://";

        public ICommand ToggleNxmHandlerCommand { get; }
        private string _nexusAccountStatus = string.IsNullOrWhiteSpace(NxmHandlerService.LoadApiKey())
            ? "Not linked"
            : "API Key saved";
        public string NexusAccountStatus
        {
            get => _nexusAccountStatus;
            set { _nexusAccountStatus = value; RaisePropertyChanged(); }
        }

        public ICommand AuthorizeNexusCommand { get; }
        private bool _autoAlignModVersions = true;
        public bool AutoAlignModVersions
        {
            get => _autoAlignModVersions;
            set
            {
                if (_autoAlignModVersions == value) return;
                _autoAlignModVersions = value;
                RaisePropertyChanged();
                Log($"🔄 [Version Aligner] Auto-align mod versions {(value ? "ENABLED" : "DISABLED")}.");
                SyncActiveGameConfig();
            }
        }

        public MainWindowViewModel()
        {
            foreach (var game in GameRegistryService.LoadGames())
                AvailableGames.Add(game);

            _selectedGame = AvailableGames[0];

            TriggerSSDScanCommand = new RelayCommand(ExecuteLiveStorageDriveScan);
            ToggleModCommand = new RelayCommand<InstalledModItem>(ExecuteToggleMod);
            MoveModUpCommand = new RelayCommand<InstalledModItem>(mod => MoveModPriority(mod, -1));
            MoveModDownCommand = new RelayCommand<InstalledModItem>(mod => MoveModPriority(mod, 1));
            DeleteModCommand = new RelayCommand<InstalledModItem>(ExecuteDeleteMod);
            LaunchGameCommand = new RelayCommand(ExecuteLaunchGame);
            SwitchModTierCommand = new RelayCommand<InstalledModItem>(ExecuteSwitchModTier);
            SaveProfileCommand = new RelayCommand(SaveCurrentProfile);
            DeleteProfileCommand = new RelayCommand(DeleteSelectedProfile);
            ToggleNxmHandlerCommand = new RelayCommand(ExecuteToggleNxmHandler);
            AuthorizeNexusCommand = new RelayCommand(async () => await ExecuteAuthorizeNexusAsync());
            if (!string.IsNullOrWhiteSpace(NexusApiKey))
            {
                _ = VerifySavedNexusKeyOnStartupAsync();
            }

            ExecuteLiveStorageDriveScan();
        }

        private bool IsValheimActive => SelectedGame.SteamAppId == "892970";

        private string GetActiveModsDirectory()
        {
            if (ActiveGameDef == null) return Path.Combine(DiscoveredGamePathText, "mods");
            string root = ActiveGameDef.ResolveDeploymentRoot(DiscoveredGamePathText);
            return Path.Combine(root, ActiveGameDef.ActiveModsRelPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private string GetDisabledModsDirectory()
        {
            if (ActiveGameDef == null) return Path.Combine(DiscoveredGamePathText, "mods_disabled");
            string root = ActiveGameDef.ResolveDeploymentRoot(DiscoveredGamePathText);
            return Path.Combine(root, ActiveGameDef.DisabledModsRelPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private void ExecuteLiveStorageDriveScan()
        {
            TelemetryLogStreamText += $"\n[Radar Action] Scanning storage arrays for {SelectedGame.GameName}...";

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string pathResult = ScannerService.LocateGamePath(home, SelectedGame.DefaultInstallFolderName);

            if (!string.IsNullOrEmpty(pathResult))
            {
                DiscoveredGamePathText = pathResult;
                TelemetryLogStreamText += $"\n[Radar Success] Locked target path: {pathResult}";
                RefreshInstalledModsGrid();
                RefreshProfiles();
            }
            else
            {
                CoreMods.Clear();
                AssetMods.Clear();
                Profiles.Clear();
                DiscoveredGamePathText = $"❌ {SelectedGame.GameName} folder not found across storage arrays.";
                TelemetryLogStreamText += $"\n[Radar Warning] Zero matching directories found for '{SelectedGame.DefaultInstallFolderName}'.";
            }
        }
        public void RefreshInstalledModsGrid()
        {
            if (!Directory.Exists(DiscoveredGamePathText)) return;

            CoreMods.Clear();
            AssetMods.Clear();

            string activeDir = GetActiveModsDirectory();
            string disabledDir = GetDisabledModsDirectory();
            string badge = ActiveGameDef?.ModTypeBadge ?? "MOD";

            int corePriority = 1;
            int assetPriority = 1;

            // 1. Load unified Mod Containers from .percival_staging/Mods/
            string containersRoot = ModContainerService.GetContainersRootDirectory(DiscoveredGamePathText);
            if (Directory.Exists(containersRoot))
            {
                var containerEntries = Directory.GetDirectories(containersRoot)
                    .Select(dir => new { Dir = dir, Manifest = ModContainerService.LoadManifest(dir) })
                    .OrderBy(x => x.Manifest?.Priority ?? 999)
                    .ThenBy(x => x.Manifest?.DisplayName ?? Path.GetFileName(x.Dir), StringComparer.OrdinalIgnoreCase);

                foreach (var entry in containerEntries)
                {
                    var manifest = entry.Manifest ?? new ModContainerManifest
                    {
                        DisplayName = Path.GetFileName(entry.Dir),
                        Tier = DetectFolderTier(entry.Dir),
                        IsEnabled = true
                    };

                    ModTier tier = DetectFolderTier(entry.Dir, manifest.Tier);

                    var item = new InstalledModItem
                    {
                        Name = manifest.DisplayName,
                        FolderPath = entry.Dir,
                        StagingPath = entry.Dir,
                        ModType = tier == ModTier.Assets ? "ASSET" : badge,
                        Priority = tier == ModTier.Assets ? assetPriority++ : corePriority++,
                        IsEnabled = manifest.IsEnabled,
                        Tier = tier,
                        ItemKind = manifest.ItemKind,
                        CategoryGroup = manifest.CategoryGroup
                    };

                    if (tier == ModTier.Assets)
                        AssetMods.Add(item);
                    else
                        CoreMods.Add(item);
                }
            }

            // 2. Load legacy non-container folders (skips symlinks created by ModContainerService!)
            void AddDiscoveredFolder(string dir, bool isEnabled)
            {
                if (new DirectoryInfo(dir).LinkTarget != null) return;

                ModTier tier = DetectFolderTier(dir);

                var item = new InstalledModItem
                {
                    Name = Path.GetFileName(dir),
                    FolderPath = dir,
                    ModType = tier == ModTier.Assets ? "ASSET" : badge,
                    Priority = tier == ModTier.Assets ? assetPriority++ : corePriority++,
                    IsEnabled = isEnabled,
                    Tier = tier
                };

                if (tier == ModTier.Assets)
                    AssetMods.Add(item);
                else
                    CoreMods.Add(item);
            }

            if (Directory.Exists(activeDir))
            {
                foreach (var dir in Directory.GetDirectories(activeDir).OrderBy(d => d))
                    AddDiscoveredFolder(dir, true);
            }

            if (Directory.Exists(disabledDir))
            {
                foreach (var dir in Directory.GetDirectories(disabledDir).OrderBy(d => d))
                    AddDiscoveredFolder(dir, false);
            }
            // 3. Load Valheim HD Texture / Asset Packs (.percival_staging/AssetPacks/)
            if (IsValheimActive)
            {
                string stagingPacksRoot = ValheimAssetPackService.GetStagingRootDirectory(DiscoveredGamePathText);
                if (Directory.Exists(stagingPacksRoot))
                {
                    foreach (var dir in Directory.GetDirectories(stagingPacksRoot).OrderBy(d => d))
                    {
                        bool isEnabled = !File.Exists(Path.Combine(dir, ".percival_disabled"));
                        AssetMods.Add(new InstalledModItem
                        {
                            Name = Path.GetFileName(dir),
                            FolderPath = dir,
                            ModType = "HD ASSET",
                            Priority = assetPriority++,
                            IsEnabled = isEnabled,
                            Tier = ModTier.Assets,
                            ItemKind = ModItemKind.TextureAssetPack,
                            StagingPath = dir
                        });
                    }
                }
            }

            TelemetryLogStreamText += $"\n[Grid Sync] Discovered {CoreMods.Count} core mod(s) & {AssetMods.Count} asset pack(s) for {SelectedGame.GameName}.";
            RunConflictCheck();
            SyncActiveGameConfig();
        }

        private static ModTier DetectFolderTier(string folderPath, ModTier? defaultTier = null)
        {
            if (!Directory.Exists(folderPath)) return defaultTier ?? ModTier.Core;

            string overrideMarker = Path.Combine(folderPath, ".percival_tier");
            if (File.Exists(overrideMarker))
            {
                string saved = File.ReadAllText(overrideMarker).Trim();
                if (Enum.TryParse<ModTier>(saved, out var manualTier))
                    return manualTier;
            }

            if (defaultTier.HasValue) return defaultTier.Value;

            var files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);

            bool hasCodeOrPlugin = files.Any(f =>
                f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".ws", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".esp", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".esm", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".esl", StringComparison.OrdinalIgnoreCase));

            if (hasCodeOrPlugin) return ModTier.Core;

            bool hasLooseAssets = files.Any(f =>
                f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".obj", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".nif", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(f).Equals("texture.cache", StringComparison.OrdinalIgnoreCase));

            if (hasLooseAssets) return ModTier.Assets;

            string folderName = Path.GetFileName(folderPath).ToLowerInvariant();
            string[] assetKeywords = { "texture", "retex", "4k", "2k", "8k", "hd", "mesh", "visual", "reshade", "cavill", "face", "hair", "skin", "outfit", "armor" };
            if (assetKeywords.Any(k => folderName.Contains(k)))
                return ModTier.Assets;

            return ModTier.Core;
        }

                private void SyncActiveGameConfig()
        {
            string containerSyncResult = ModContainerService.SyncDeployedContainers(
                DiscoveredGamePathText,
                SelectedGame.SteamAppId,
                InstalledMods);
            Log($"[Container Sync] {containerSyncResult}");

            if (AutoAlignModVersions)
            {
                string alignResult = VersionAlignerService.AlignStagedModManifests(
                    DiscoveredGamePathText,
                    SelectedGame.SteamAppId,
                    InstalledMods);
                Log($"[Version Aligner] {alignResult}");
            }

            string engine = ActiveGameDef?.SyncEngine ?? "Default";
            switch (engine)
            {
                case "Valheim":
                    string launchResult = SteamLaunchOptionsService.ConfigureValheimBepInExLaunchOption(DiscoveredGamePathText);
                    Log($"[BepInEx Sync] {launchResult}");

                    foreach (var diag in ValheimHealthCheckService.RunDiagnostics(DiscoveredGamePathText))
                    {
                        Log(diag);
                    }
                    string assetSyncResult = ValheimAssetPackService.SyncDeployedAssetPacks(DiscoveredGamePathText, AssetMods);
                    Log($"[Asset Sync] {assetSyncResult}");
                    break;

                case "NMS":
                    var modList = InstalledMods.Select(m => new ModInfo
                    {
                        Name = VersionAlignerService.ResolveNmsDeployedFolderName(m),
                        Priority = m.Priority,
                        IsEnabled = m.IsEnabled
                    }).ToList();

                    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    new NoMansSkyProfile().SaveConfiguration(modList, DiscoveredGamePathText, home, AutoAlignModVersions);
                    Log($"[NMS Sync] GCMODSETTINGS.MXML synced ({modList.Count} entries, Auto-Align: {(AutoAlignModVersions ? "ON" : "OFF")}).");
                    break;

                case "Witcher3":
                    string syncResult = Witcher3ConfigSync.SyncLoadOrder(DiscoveredGamePathText, InstalledMods);
                    Log($"[Proton Sync] {syncResult}");
                    break;

                case "DOS2":
                    string dos2SyncResult = DivinityOS2ConfigSync.SyncLoadOrder(DiscoveredGamePathText, InstalledMods);
                    Log($"[DOS2 Sync] {dos2SyncResult}");
                    break;
            }
        }

        private void ExecuteToggleMod(InstalledModItem? mod)
        {
            if (mod == null || !Directory.Exists(DiscoveredGamePathText)) return;

            // 1. Unified Mod Container (.percival_staging/Mods/<Container>/)
            if (!string.IsNullOrEmpty(mod.StagingPath) &&
                mod.StagingPath.Replace('\\', '/').Contains($"/{ModContainerService.StagingRootName}/{ModContainerService.ModsSubdir}/"))
            {
                var manifest = ModContainerService.LoadManifest(mod.StagingPath) ?? new ModContainerManifest
                {
                    DisplayName = mod.Name,
                    Tier = mod.Tier,
                    Priority = mod.Priority
                };
                manifest.IsEnabled = mod.IsEnabled;
                ModContainerService.SaveManifest(mod.StagingPath, manifest);

                Log($"[Container State] {(mod.IsEnabled ? "Enabled" : "Disabled")}: {mod.Name}");
                SyncActiveGameConfig();
                AutoSaveSelectedProfile();
                RunConflictCheck();
                return;
            }

            // 2. Valheim HD Asset Pack (.percival_staging/AssetPacks/<Pack>/)
            if (IsValheimActive && mod.FolderPath.Replace('\\', '/').Contains($"/{ValheimAssetPackService.StagingRootName}/"))
            {
                string disabledMarker = Path.Combine(mod.FolderPath, ".percival_disabled");
                if (mod.IsEnabled)
                {
                    if (File.Exists(disabledMarker)) File.Delete(disabledMarker);
                }
                else
                {
                    File.WriteAllText(disabledMarker, "disabled");
                }

                Log($"[Mod State] {(mod.IsEnabled ? "Enabled" : "Disabled")} HD Pack: {mod.Name}");
                SyncActiveGameConfig();
                AutoSaveSelectedProfile();
                return;
            }

            // 3. Legacy non-container folder fallback
            try
            {
                string targetParent = mod.IsEnabled ? GetActiveModsDirectory() : GetDisabledModsDirectory();
                Directory.CreateDirectory(targetParent);
                string newPath = Path.Combine(targetParent, mod.Name);

                if (Directory.Exists(mod.FolderPath) && mod.FolderPath != newPath)
                {
                    Directory.Move(mod.FolderPath, newPath);
                    mod.FolderPath = newPath;
                    TelemetryLogStreamText += $"\n[Mod State] {(mod.IsEnabled ? "Enabled" : "Disabled")}: {mod.Name}";

                    SyncActiveGameConfig();
                    AutoSaveSelectedProfile();
                    RunConflictCheck();
                }
            }
            catch (Exception ex)
            {
                TelemetryLogStreamText += $"\n[Error] Failed to toggle {mod.Name}: {ex.Message}";
            }
        }

        private ObservableCollection<InstalledModItem> GetCollectionForMod(InstalledModItem mod) =>
            mod.Tier == ModTier.Assets ? AssetMods : CoreMods;

        private void MoveModPriority(InstalledModItem? mod, int direction)
        {
            if (mod == null) return;
            var targetList = GetCollectionForMod(mod);

            int oldIndex = targetList.IndexOf(mod);
            int newIndex = oldIndex + direction;

            if (oldIndex < 0 || newIndex < 0 || newIndex >= targetList.Count) return;

            targetList.Move(oldIndex, newIndex);

            for (int i = 0; i < targetList.Count; i++)
            {
                targetList[i].Priority = i + 1;
                if (!string.IsNullOrEmpty(targetList[i].StagingPath))
                {
                    var manifest = ModContainerService.LoadManifest(targetList[i].StagingPath!);
                    if (manifest != null)
                    {
                        manifest.Priority = targetList[i].Priority;
                        ModContainerService.SaveManifest(targetList[i].StagingPath!, manifest);
                    }
                }
            }

            var snapshot = targetList.ToList();
            targetList.Clear();
            foreach (var item in snapshot) targetList.Add(item);

            TelemetryLogStreamText += $"\n[Priority] Moved '{mod.Name}' to rank #{mod.Priority} in {mod.Tier}.";

            SyncActiveGameConfig();
            RunConflictCheck();
        }

        private void ExecuteDeleteMod(InstalledModItem? mod)
        {
            if (mod == null || !Directory.Exists(DiscoveredGamePathText)) return;

            try
            {
                if (!string.IsNullOrEmpty(mod.StagingPath) &&
                    mod.StagingPath.Replace('\\', '/').Contains($"/{ModContainerService.StagingRootName}/{ModContainerService.ModsSubdir}/"))
                {
                    ModContainerService.DeleteContainer(DiscoveredGamePathText, mod, SelectedGame.SteamAppId);
                }
                else if (Directory.Exists(mod.FolderPath))
                {
                    Directory.Delete(mod.FolderPath, true);
                }

                var targetList = GetCollectionForMod(mod);
                targetList.Remove(mod);

                for (int i = 0; i < targetList.Count; i++)
                {
                    targetList[i].Priority = i + 1;
                }

                var snapshot = targetList.ToList();
                targetList.Clear();
                foreach (var item in snapshot) targetList.Add(item);

                TelemetryLogStreamText += $"\n🗑️ [Uninstall] Removed mod bundle: {mod.Name}";

                SyncActiveGameConfig();
                RunConflictCheck();
            }
            catch (Exception ex)
            {
                TelemetryLogStreamText += $"\n❌ [Error] Failed to delete {mod.Name}: {ex.Message}";
            }
        }
        private void ExecuteSwitchModTier(InstalledModItem? mod)
        {
            if (mod == null || !Directory.Exists(mod.FolderPath)) return;

            if (IsValheimActive &&
                mod.Tier == ModTier.Core &&
                !mod.FolderPath.Replace('\\', '/').Contains($"/{ValheimAssetPackService.StagingRootName}/"))
            {
                string stagingPackDir = ValheimAssetPackService.GetStagingPackDirectory(DiscoveredGamePathText, mod.Name);
                if (Directory.Exists(stagingPackDir)) Directory.Delete(stagingPackDir, true);

                if (!string.IsNullOrEmpty(mod.StagingPath))
                {
                    ModContainerService.DeleteContainer(DiscoveredGamePathText, mod, SelectedGame.SteamAppId);
                }
                else
                {
                    Directory.Move(mod.FolderPath, stagingPackDir);
                }

                ValheimAssetPackService.NormalizeStagedPackLayout(stagingPackDir);
                File.WriteAllText(Path.Combine(stagingPackDir, ".percival_tier"), "Assets");

                Log($"🎨 [Asset Migration] Migrated '{mod.Name}' from BepInEx/plugins to HD Asset Staging.");
                RefreshInstalledModsGrid();
                return;
            }

            ModTier newTier = mod.Tier == ModTier.Core ? ModTier.Assets : ModTier.Core;
            File.WriteAllText(Path.Combine(mod.FolderPath, ".percival_tier"), newTier.ToString());

            if (!string.IsNullOrEmpty(mod.StagingPath))
            {
                var manifest = ModContainerService.LoadManifest(mod.StagingPath);
                if (manifest != null)
                {
                    manifest.Tier = newTier;
                    ModContainerService.SaveManifest(mod.StagingPath, manifest);
                }
            }

            TelemetryLogStreamText += $"\n⇅ [Tier Override] Moved '{mod.Name}' to {(newTier == ModTier.Assets ? "Texture / Asset Packs" : "Installed Mod Bundle")}.";
            RefreshInstalledModsGrid();
        }

        private async void ExecuteLaunchGame()
        {
            if (!Directory.Exists(DiscoveredGamePathText))
            {
                TelemetryLogStreamText += "\n⚠️ [Launcher] Cannot launch: Target game directory not verified.";
                return;
            }

            try
            {
                SyncActiveGameConfig();

                if (IsValheimActive && AssetMods.Any(m => m.IsEnabled))
                {
                    await ValheimAssetPackService.PreWarmAssetPacksAsync(DiscoveredGamePathText, AssetMods, Log);
                }

                TelemetryLogStreamText += $"\n⚔️ [Launcher] Handing off AppID {SelectedGame.SteamAppId} ({SelectedGame.GameName}) to Steam...";

                Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = $"steam://rungameid/{SelectedGame.SteamAppId}",
                    UseShellExecute = false
                });
            }
            catch (Exception ex)
            {
                TelemetryLogStreamText += $"\n❌ [Launcher Error] Failed to trigger Steam: {ex.Message}";
            }
        }

        public void RefreshProfiles()
        {
            if (!Directory.Exists(DiscoveredGamePathText)) return;

            var loaded = _profileService.LoadProfiles(DiscoveredGamePathText, SelectedGame.GameName);
            Profiles.Clear();
            foreach (var p in loaded)
                Profiles.Add(p);

            if (Profiles.Count == 0)
            {
                var defaultProfile = _profileService.CaptureCurrentState(
                    "Default Playthrough",
                    SelectedGame.GameName,
                    CoreMods,
                    AssetMods);
                _profileService.SaveProfile(DiscoveredGamePathText, defaultProfile);
                Profiles.Add(defaultProfile);
            }

            _selectedProfile = Profiles.FirstOrDefault();
            RaisePropertyChanged(nameof(SelectedProfile));
        }

        public void SaveCurrentProfile()
        {
            if (!Directory.Exists(DiscoveredGamePathText)) return;

            bool isCreatingNew = !string.IsNullOrWhiteSpace(NewProfileName);
            if (!isCreatingNew && SelectedProfile == null)
            {
                Log("⚠️ [Profiles] Select an existing profile or type a new profile name first.");
                return;
            }

            string name = isCreatingNew ? NewProfileName.Trim() : SelectedProfile!.Name;
            string? existingId = isCreatingNew ? null : SelectedProfile!.Id;

            var captured = _profileService.CaptureCurrentState(
                name,
                SelectedGame.GameName,
                CoreMods,
                AssetMods,
                existingId);

            _profileService.SaveProfile(DiscoveredGamePathText, captured);
            NewProfileName = string.Empty;

            _isApplyingProfile = true;
            try
            {
                RefreshProfiles();
                _selectedProfile = Profiles.FirstOrDefault(p => p.Id == captured.Id);
                RaisePropertyChanged(nameof(SelectedProfile));
            }
            finally
            {
                _isApplyingProfile = false;
            }

            Log($"💾 [Profiles] Saved state to '{captured.Name}'.");
        }
        public void DeleteSelectedProfile()
        {
            if (!Directory.Exists(DiscoveredGamePathText) || SelectedProfile == null || Profiles.Count <= 1)
                return;

            string deletedName = SelectedProfile.Name;
            _profileService.DeleteProfile(DiscoveredGamePathText, SelectedProfile);
            RefreshProfiles();
            if (_selectedProfile != null)
                ApplySelectedProfile(_selectedProfile);

            Log($"🗑️ [Profiles] Deleted profile '{deletedName}'.");
        }

        private bool _isApplyingProfile;

        private void AutoSaveSelectedProfile()
        {
            if (_isApplyingProfile || SelectedProfile == null || !Directory.Exists(DiscoveredGamePathText)) return;

            var updated = _profileService.CaptureCurrentState(
                SelectedProfile.Name,
                SelectedGame.GameName,
                CoreMods,
                AssetMods,
                SelectedProfile.Id);

            _profileService.SaveProfile(DiscoveredGamePathText, updated);
            SelectedProfile.Mods = updated.Mods;
            SelectedProfile.AssetPacks = updated.AssetPacks;
            SelectedProfile.LastModifiedUtc = updated.LastModifiedUtc;
        }

        private void ApplySelectedProfile(PlaythroughProfile profile)
        {
            _isApplyingProfile = true;
            try
            {
                _profileService.ApplyProfileToLists(profile, CoreMods, AssetMods);

                foreach (var mod in CoreMods)
                    ExecuteToggleMod(mod);
                foreach (var asset in AssetMods)
                    ExecuteToggleMod(asset);

                var coreSnap = CoreMods.ToList();
                CoreMods.Clear();
                foreach (var m in coreSnap) CoreMods.Add(m);

                var assetSnap = AssetMods.ToList();
                AssetMods.Clear();
                foreach (var a in assetSnap) AssetMods.Add(a);

                SyncActiveGameConfig();
                RunConflictCheck();
                Log($"🔀 [Profiles] Switched to '{profile.Name}'.");
            }
            finally
            {
                _isApplyingProfile = false;
            }
        }

        private void ExecuteToggleNxmHandler()
        {
            string msg = _isNxmRegistered
                ? NxmHandlerService.UnregisterLinuxNxmHandler()
                : NxmHandlerService.RegisterLinuxNxmHandler();

            _isNxmRegistered = NxmHandlerService.IsLinuxNxmHandlerRegistered();
            RaisePropertyChanged(nameof(NxmHandlerButtonText));
            Log($"[Nexus NXM] {msg}");
        }

        private async System.Threading.Tasks.Task VerifySavedNexusKeyOnStartupAsync()
        {
            try
            {
                string user = await NxmHandlerService.ValidateApiKeyAsync(NexusApiKey);
                NexusAccountStatus = $"✓ {user}";
            }
            catch
            {
                NexusAccountStatus = "⚠️ Key invalid";
            }
        }

        private async System.Threading.Tasks.Task ExecuteAuthorizeNexusAsync()
        {
            try
            {
                NexusAccountStatus = "Waiting for browser...";
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(3));
                var (apiKey, username) = await NxmHandlerService.AuthorizeViaSsoAsync(Log, cts.Token);
                _nexusApiKey = apiKey;
                RaisePropertyChanged(nameof(NexusApiKey));
                NexusAccountStatus = $"✓ {username}";
                Log($"✅ [Nexus SSO] Authorized and linked Nexus Mods account: {username}");
            }
            catch (Exception ex)
            {
                NexusAccountStatus = "Authorization failed";
                Log($"❌ [Nexus SSO Error] {ex.Message}");
            }
        }

        public async System.Threading.Tasks.Task HandleIncomingNxmUriAsync(string nxmUri)
        {
            var req = NxmHandlerService.ParseNxmUri(nxmUri);
            if (req == null)
            {
                Log($"⚠️ [Nexus NXM] Invalid nxm:// link: {nxmUri}");
                return;
            }

            var matchedGame = GameRegistryService.GetByNexusDomain(req.GameDomain);
            if (matchedGame == null)
            {
                Log($"⚠️ [Nexus NXM] Unsupported Nexus game domain '{req.GameDomain}'. Add its NexusDomain to games.json first.");
                return;
            }

            if (SelectedGame.SteamAppId != matchedGame.SteamAppId)
            {
                SelectedGame = matchedGame;
            }

            if (!Directory.Exists(DiscoveredGamePathText))
            {
                Log($"❌ [Nexus NXM] Cannot install: {matchedGame.GameName} install path was not found.");
                return;
            }

            try
            {
                string archivePath = await NxmHandlerService.DownloadFromNxmAsync(req, NexusApiKey, Log);
                ModInstaller.InstallZipMod(archivePath, DiscoveredGamePathText, matchedGame.GameName);
                RefreshInstalledModsGrid();
                AutoSaveSelectedProfile();
                Log($"🎉 [Nexus NXM] Installed '{Path.GetFileName(archivePath)}' for {matchedGame.GameName}.");
            }
            catch (Exception ex)
            {
                Log($"❌ [Nexus NXM Error] {ex.Message}");
            }
        }

        private void Log(string message)
        {
            TelemetryLogStreamText = $"{message}\n{TelemetryLogStreamText}";
        }

        private void RunConflictCheck()
        {
            var conflicts = ConflictDetectorService.ScanForConflicts(InstalledMods);
            if (conflicts.Count == 0)
            {
                TelemetryLogStreamText += "\n🛡️ [Conflict Radar] Zero overlapping mod files detected.";
            }
            else
            {
                TelemetryLogStreamText += $"\n⚠️ [Conflict Radar] Detected {conflicts.Count} file overlap(s):";
                foreach (var c in conflicts)
                {
                    TelemetryLogStreamText += $"\n   {c}";
                }
            }
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _action;
        public RelayCommand(Action action) => _action = action;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _action();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }

    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T?> _action;
        public RelayCommand(Action<T?> action) => _action = action;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _action((T?)parameter);
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
