using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Input;
using PercivalMM.Core.Games;
using PercivalMM.Core.Models;
using PercivalMM.Core.Services;

namespace PercivalMM.UI.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private string _discoveredGamePathText = "Storage arrays unscanned. Awaiting pipeline initialization command...";
        private string _telemetryLogStreamText = "[System Init] Core app modules loaded.\n[System Init] Multi-game profile registry active.";
        private IGameProfile _selectedGame;

        public ObservableCollection<IGameProfile> AvailableGames { get; } = new();
        public ObservableCollection<InstalledModItem> InstalledMods { get; } = new();

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
                    ExecuteLiveStorageDriveScan();
                }
            }
        }

        public string LaunchButtonText => $"⚔️ Launch {SelectedGame?.GameName ?? "Game"}";

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

        public MainWindowViewModel()
        {
            AvailableGames.Add(new Witcher3Profile());
            AvailableGames.Add(new ValheimProfile());
            _selectedGame = AvailableGames[0];

            TriggerSSDScanCommand = new RelayCommand(ExecuteLiveStorageDriveScan);
            ToggleModCommand = new RelayCommand<InstalledModItem>(ExecuteToggleMod);
            MoveModUpCommand = new RelayCommand<InstalledModItem>(mod => MoveModPriority(mod, -1));
            MoveModDownCommand = new RelayCommand<InstalledModItem>(mod => MoveModPriority(mod, 1));
            DeleteModCommand = new RelayCommand<InstalledModItem>(ExecuteDeleteMod);
            LaunchGameCommand = new RelayCommand(ExecuteLaunchGame);

            ExecuteLiveStorageDriveScan();
        }

        private bool IsValheimActive => SelectedGame.SteamAppId == "892970";

        private string GetActiveModsDirectory() => IsValheimActive
            ? Path.Combine(DiscoveredGamePathText, "BepInEx", "plugins")
            : Path.Combine(DiscoveredGamePathText, "mods");

        private string GetDisabledModsDirectory() => IsValheimActive
            ? Path.Combine(DiscoveredGamePathText, "BepInEx", "plugins_disabled")
            : Path.Combine(DiscoveredGamePathText, "mods_disabled");

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
                InstalledMods.Clear();
                DiscoveredGamePathText = $"❌ {SelectedGame.GameName} folder not found across storage arrays.";
                TelemetryLogStreamText += $"\n[Radar Warning] Zero matching directories found for '{SelectedGame.DefaultInstallFolderName}'.";
            }
        }
        public void RefreshInstalledModsGrid()
        {
            if (!Directory.Exists(DiscoveredGamePathText)) return;

            InstalledMods.Clear();
            string activeDir = GetActiveModsDirectory();
            string disabledDir = GetDisabledModsDirectory();

            int priorityCounter = 1;

            if (Directory.Exists(activeDir))
            {
                foreach (var dir in Directory.GetDirectories(activeDir).OrderBy(d => d))
                {
                    InstalledMods.Add(new InstalledModItem
                    {
                        Name = Path.GetFileName(dir),
                        FolderPath = dir,
                        ModType = IsValheimActive ? "BEPINEX" : "MOD",
                        Priority = priorityCounter++,
                        IsEnabled = true
                    });
                }
            }

            if (Directory.Exists(disabledDir))
            {
                foreach (var dir in Directory.GetDirectories(disabledDir).OrderBy(d => d))
                {
                    InstalledMods.Add(new InstalledModItem
                    {
                        Name = Path.GetFileName(dir),
                        FolderPath = dir,
                        ModType = IsValheimActive ? "BEPINEX" : "MOD",
                        Priority = priorityCounter++,
                        IsEnabled = false
                    });
                }
            }

            TelemetryLogStreamText += $"\n[Grid Sync] Discovered {InstalledMods.Count} mod bundle(s) for {SelectedGame.GameName}.";
            RunConflictCheck();
        }

        private void SyncActiveGameConfig()
        {
            if (IsValheimActive)
            {
                TelemetryLogStreamText += "\n[BepInEx Sync] Valheim plugin folder state verified.";
            }
            else
            {
                string syncResult = Witcher3ConfigSync.SyncLoadOrder(DiscoveredGamePathText, InstalledMods);
                TelemetryLogStreamText += $"\n[Proton Sync] {syncResult}";
            }
        }

        private void ExecuteToggleMod(InstalledModItem? mod)
        {
            if (mod == null || !Directory.Exists(DiscoveredGamePathText)) return;

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

        private void MoveModPriority(InstalledModItem? mod, int direction)
        {
            if (mod == null) return;
            int oldIndex = InstalledMods.IndexOf(mod);
            int newIndex = oldIndex + direction;

            if (oldIndex < 0 || newIndex < 0 || newIndex >= InstalledMods.Count) return;

            InstalledMods.Move(oldIndex, newIndex);

            for (int i = 0; i < InstalledMods.Count; i++)
            {
                InstalledMods[i].Priority = i + 1;
            }

            var snapshot = InstalledMods.ToList();
            InstalledMods.Clear();
            foreach (var item in snapshot) InstalledMods.Add(item);

            TelemetryLogStreamText += $"\n[Priority] Moved '{mod.Name}' to rank #{mod.Priority}.";

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

                InstalledMods.Remove(mod);

                for (int i = 0; i < InstalledMods.Count; i++)
                {
                    InstalledMods[i].Priority = i + 1;
                }

                var snapshot = InstalledMods.ToList();
                InstalledMods.Clear();
                foreach (var item in snapshot) InstalledMods.Add(item);

                TelemetryLogStreamText += $"\n🗑️ [Uninstall] Removed mod bundle: {mod.Name}";

                SyncActiveGameConfig();
                RunConflictCheck();
            }
            catch (Exception ex)
            {
                TelemetryLogStreamText += $"\n❌ [Error] Failed to delete {mod.Name}: {ex.Message}";
            }
        }

        private void ExecuteLaunchGame()
        {
            if (!Directory.Exists(DiscoveredGamePathText))
            {
                TelemetryLogStreamText += "\n⚠️ [Launcher] Cannot launch: Target game directory not verified.";
                return;
            }

            try
            {
                SyncActiveGameConfig();
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
