using System;
using System.Collections.ObjectModel;
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
        private string _telemetryLogStreamText = "[System Init] Core app modules loaded.\n[System Init] Awaiting active trigger execution signal.";

        public ObservableCollection<InstalledModItem> InstalledMods { get; } = new();

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


        public MainWindowViewModel()
        {
            TriggerSSDScanCommand = new RelayCommand(ExecuteLiveStorageDriveScan);
            ToggleModCommand = new RelayCommand<InstalledModItem>(ExecuteToggleMod);
            MoveModUpCommand = new RelayCommand<InstalledModItem>(mod => MoveModPriority(mod, -1));
            MoveModDownCommand = new RelayCommand<InstalledModItem>(mod => MoveModPriority(mod, 1));
            DeleteModCommand = new RelayCommand<InstalledModItem>(ExecuteDeleteMod);

            ExecuteLiveStorageDriveScan();
        }

        private void ExecuteLiveStorageDriveScan()
        {
            TelemetryLogStreamText += "\n[Radar Action] Booting cross-drive asset search sequence...";
            
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            IGameProfile witcherModule = new Witcher3Profile();

            string pathResult = ScannerService.LocateGamePath(home, witcherModule.DefaultInstallFolderName);

            if (!string.IsNullOrEmpty(pathResult))
            {
                DiscoveredGamePathText = pathResult;
                TelemetryLogStreamText += $"\n[Radar Success] Locked target common path configuration layer.";
                TelemetryLogStreamText += $"\n[File Stream] Target verified: {pathResult}";
                RefreshInstalledModsGrid();
            }
            else
            {
                DiscoveredGamePathText = "❌ Game folder not found across system storage arrays.";
                TelemetryLogStreamText += "\n[Radar Warning] Zero matching directories discovered on hard drive layers.";
            }
        }

        public void RefreshInstalledModsGrid()
        {
            if (!Directory.Exists(DiscoveredGamePathText)) return;

            InstalledMods.Clear();
            string activeModsDir = Path.Combine(DiscoveredGamePathText, "mods");
            string disabledModsDir = Path.Combine(DiscoveredGamePathText, "mods_disabled");

            int priorityCounter = 1;

            // 1. Scan active mods
            if (Directory.Exists(activeModsDir))
            {
                foreach (var dir in Directory.GetDirectories(activeModsDir).OrderBy(d => d))
                {
                    InstalledMods.Add(new InstalledModItem
                    {
                        Name = Path.GetFileName(dir),
                        FolderPath = dir,
                        ModType = "MOD",
                        Priority = priorityCounter++,
                        IsEnabled = true
                    });
                }
            }

            // 2. Scan disabled mods
            if (Directory.Exists(disabledModsDir))
            {
                foreach (var dir in Directory.GetDirectories(disabledModsDir).OrderBy(d => d))
                {
                    InstalledMods.Add(new InstalledModItem
                    {
                        Name = Path.GetFileName(dir),
                        FolderPath = dir,
                        ModType = "MOD",
                        Priority = priorityCounter++,
                        IsEnabled = false
                    });
                }
            }

            TelemetryLogStreamText += $"\n[Grid Sync] Discovered {InstalledMods.Count} mod bundle(s) on target drive.";
        }

        private void ExecuteToggleMod(InstalledModItem? mod)
        {
            if (mod == null || !Directory.Exists(DiscoveredGamePathText)) return;

            try
            {
                string targetParent = mod.IsEnabled
                    ? Path.Combine(DiscoveredGamePathText, "mods")
                    : Path.Combine(DiscoveredGamePathText, "mods_disabled");

                Directory.CreateDirectory(targetParent);
                string newPath = Path.Combine(targetParent, mod.Name);

                if (Directory.Exists(mod.FolderPath) && mod.FolderPath != newPath)
                {
                    // Same physical SSD -> instant atomic directory rename/move!
                    Directory.Move(mod.FolderPath, newPath);
                    mod.FolderPath = newPath;
                    TelemetryLogStreamText += $"\n[Mod State] {(mod.IsEnabled ? "Enabled" : "Disabled")}: {mod.Name}";
                
                string syncResult = Witcher3ConfigSync.SyncLoadOrder(DiscoveredGamePathText, InstalledMods);
TelemetryLogStreamText += $"\n[Proton Sync] {syncResult}";

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

            // Re-index priority numbers cleanly
            for (int i = 0; i < InstalledMods.Count; i++)
            {
                InstalledMods[i].Priority = i + 1;
            }

            // Refresh UI items
            var snapshot = InstalledMods.ToList();
            InstalledMods.Clear();
            foreach (var item in snapshot) InstalledMods.Add(item);

            TelemetryLogStreamText += $"\n[Priority] Moved '{mod.Name}' to rank #{mod.Priority}.";
        
        string syncResult = Witcher3ConfigSync.SyncLoadOrder(DiscoveredGamePathText, InstalledMods);
TelemetryLogStreamText += $"\n[Proton Sync] {syncResult}";

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

                // Re-index remaining ranks cleanly
                for (int i = 0; i < InstalledMods.Count; i++)
                {
                    InstalledMods[i].Priority = i + 1;
                }

                var snapshot = InstalledMods.ToList();
                InstalledMods.Clear();
                foreach (var item in snapshot) InstalledMods.Add(item);

                TelemetryLogStreamText += $"\n🗑️ [Uninstall] Removed mod bundle: {mod.Name}";

                string syncResult = Witcher3ConfigSync.SyncLoadOrder(DiscoveredGamePathText, InstalledMods);
                TelemetryLogStreamText += $"\n[Proton Sync] {syncResult}";
            }
            catch (Exception ex)
            {
                TelemetryLogStreamText += $"\n❌ [Error] Failed to delete {mod.Name}: {ex.Message}";
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
