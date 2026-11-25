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

        public string LaunchButtonText => SelectedGame?.SteamAppId switch
{
    "292030" => "⚔️ Launch Witcher 3",
    "892970" => "⚔️ Launch Valheim",
    "275850" => "⚔️ Launch No Man's Sky",
    "1903340" => "⚔️ Launch Expedition 33",
    _ => "⚔️ Launch Game"
};


   public Bitmap? ActiveGameBanner
   {
       get
       {
           string fileName = SelectedGame?.SteamAppId switch
           {
               "292030" => "witcher3.png",
               "892970" => "valheim.png",
               "275850" => "nms.png",
               "1903340" => "expedition33.png",
               _ => "witcher3.png"
           };

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

        public MainWindowViewModel()
        {
            AvailableGames.Add(new Witcher3Profile());
            AvailableGames.Add(new ValheimProfile());
            AvailableGames.Add(new NoMansSkyProfile());
            AvailableGames.Add(new Expedition33Profile());

            _selectedGame = AvailableGames[0];

            TriggerSSDScanCommand = new RelayCommand(ExecuteLiveStorageDriveScan);
            ToggleModCommand = new RelayCommand<InstalledModItem>(ExecuteToggleMod);
            MoveModUpCommand = new RelayCommand<InstalledModItem>(mod => MoveModPriority(mod, -1));
            MoveModDownCommand = new RelayCommand<InstalledModItem>(mod => MoveModPriority(mod, 1));
            DeleteModCommand = new RelayCommand<InstalledModItem>(ExecuteDeleteMod);
            LaunchGameCommand = new RelayCommand(ExecuteLaunchGame);
            SwitchModTierCommand = new RelayCommand<InstalledModItem>(ExecuteSwitchModTier);

            ExecuteLiveStorageDriveScan();
        }

        private bool IsValheimActive => SelectedGame.SteamAppId == "892970";
        private bool IsNoMansSkyActive => SelectedGame.SteamAppId == "275850";
        private bool IsExpedition33Active => SelectedGame.SteamAppId == "1903340";

        private string GetActiveModsDirectory()
        {
            if (IsValheimActive) return Path.Combine(DiscoveredGamePathText, "BepInEx", "plugins");
            if (IsNoMansSkyActive) return Path.Combine(DiscoveredGamePathText, "GAMEDATA", "MODS");
            if (IsExpedition33Active) return Path.Combine(DiscoveredGamePathText, "Sandfall", "Content", "Paks", "~mods");
            if (IsExpedition33Active) return Path.Combine(DiscoveredGamePathText, "Sandfall", "Content", "Paks", "~mods_disabled");
            return Path.Combine(DiscoveredGamePathText, "mods");
        }

        private string GetDisabledModsDirectory()
        {
            if (IsValheimActive) return Path.Combine(DiscoveredGamePathText, "BepInEx", "plugins_disabled");
            if (IsNoMansSkyActive) return Path.Combine(DiscoveredGamePathText, "GAMEDATA", "MODS_DISABLED");
            return Path.Combine(DiscoveredGamePathText, "mods_disabled");
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
            }
            else
            {
                CoreMods.Clear();
                AssetMods.Clear();
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

            int corePriority = 1;
            int assetPriority = 1;

            void AddDiscoveredFolder(string dir, bool isEnabled)
            {
                ModTier tier = DetectFolderTier(dir);
                var item = new InstalledModItem
                {
                    Name = Path.GetFileName(dir),
                    FolderPath = dir,
                    ModType = tier == ModTier.Assets ? "ASSET" : (IsValheimActive ? "BEPINEX" : (IsNoMansSkyActive ? "NMS" : "MOD")),
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

               private static ModTier DetectFolderTier(string folderPath)
        {
            if (!Directory.Exists(folderPath)) return ModTier.Core;

            // 1. Check if the user manually pinned this mod to a specific tier
            string overrideMarker = Path.Combine(folderPath, ".percival_tier");
            if (File.Exists(overrideMarker))
            {
                string saved = File.ReadAllText(overrideMarker).Trim();
                if (Enum.TryParse<ModTier>(saved, out var manualTier))
                    return manualTier;
            }

            var files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);

            // 2. Hard code/script indicators -> ALWAYS Core
            bool hasCodeOrPlugin = files.Any(f =>
                f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".ws", StringComparison.OrdinalIgnoreCase)  || // Witcher 3 scripts
                f.EndsWith(".esp", StringComparison.OrdinalIgnoreCase) || // Skyrim plugins
                f.EndsWith(".esm", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".esl", StringComparison.OrdinalIgnoreCase));

            if (hasCodeOrPlugin) return ModTier.Core;

            // 3. Game-specific & loose asset indicators -> Assets
            bool hasLooseAssets = files.Any(f =>
                f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".obj", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".nif", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(f).Equals("texture.cache", StringComparison.OrdinalIgnoreCase)); // Witcher 3 textures

            if (hasLooseAssets) return ModTier.Assets;

            // 4. Smart name heuristic for packed archives (.pak / .bundle without scripts)
            string folderName = Path.GetFileName(folderPath).ToLowerInvariant();
            string[] assetKeywords = { "texture", "retex", "4k", "2k", "8k", "hd", "mesh", "visual", "reshade", "cavill", "face", "hair", "skin", "outfit", "armor" };
            if (assetKeywords.Any(k => folderName.Contains(k)))
                return ModTier.Assets;

            return ModTier.Core;
        }
              
        private void SyncActiveGameConfig()
        {
            if (IsValheimActive)
            {
                string launchResult = SteamLaunchOptionsService.ConfigureValheimBepInExLaunchOption(DiscoveredGamePathText);
                Log($"[BepInEx Sync] {launchResult}");

                foreach (var diag in ValheimHealthCheckService.RunDiagnostics(DiscoveredGamePathText))
                {
                    Log(diag);
                }
                string assetSyncResult = ValheimAssetPackService.SyncDeployedAssetPacks(DiscoveredGamePathText, AssetMods);
                Log($"[Asset Sync] {assetSyncResult}");

            }
            else if (IsNoMansSkyActive)
            {
                var modList = InstalledMods.Select(m => new ModInfo
                {
                    Name = m.Name,
                    Priority = m.Priority,
                    IsEnabled = m.IsEnabled
                }).ToList();

                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                SelectedGame.SaveConfiguration(modList, DiscoveredGamePathText, home);
                Log("[NMS Sync] GCMODSETTINGS.MXML load order and enabled state synced.");
            }
            else
            {
                string syncResult = Witcher3ConfigSync.SyncLoadOrder(DiscoveredGamePathText, InstalledMods);
                Log($"[Proton Sync] {syncResult}");
            }
        }



        private void ExecuteToggleMod(InstalledModItem? mod)
        {
            if (mod == null || !Directory.Exists(DiscoveredGamePathText)) return;
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
                return;
            }

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
                if (Directory.Exists(mod.FolderPath))
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

         ModTier newTier = mod.Tier == ModTier.Core ? ModTier.Assets : ModTier.Core;
         File.WriteAllText(Path.Combine(mod.FolderPath, ".percival_tier"), newTier.ToString());

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
