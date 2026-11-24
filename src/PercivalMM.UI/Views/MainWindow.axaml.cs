using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PercivalMM.Core.Services;
using PercivalMM.UI.ViewModels;

namespace PercivalMM.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OnImportModClicked(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        // Default directly to ~/Downloads on Linux Mint
        var downloadsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads"
        );
        var suggestedFolder = await topLevel.StorageProvider.TryGetFolderFromPathAsync(downloadsPath);

                var archiveFileType = new FilePickerFileType("Mod Archives (.zip, .rar, .7z)")
        {
            Patterns = new[] { "*.zip", "*.rar", "*.7z" },
            MimeTypes = new[]
            {
                "application/zip",
                "application/x-zip-compressed",
                "application/vnd.rar",
                "application/x-rar-compressed",
                "application/x-7z-compressed"
            }
        };

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Witcher 3 Mod Archive",
            AllowMultiple = false,
            SuggestedStartLocation = suggestedFolder,
            FileTypeFilter = new[] { archiveFileType }
        });


        var selectedFile = files.FirstOrDefault();
        if (selectedFile == null) return;

        // Extract the local filesystem path (e.g. /home/haedrion/Downloads/mod.zip)
        string zipPath = selectedFile.Path.LocalPath;

                var vm = DataContext as MainWindowViewModel;
        string? scannedPath = vm?.DiscoveredGamePathText;
        string gameRootPath = (!string.IsNullOrWhiteSpace(scannedPath) && Directory.Exists(scannedPath))
            ? scannedPath
            : "/media/Haedrion/GamingSSD/SteamLibrary/steamapps/common/The Witcher 3";

        if (sender is Button btn) btn.IsEnabled = false;

        try
        {
            if (vm != null)
                vm.TelemetryLogStreamText += $"\n📦 [IMPORT] Staging '{Path.GetFileName(zipPath)}' -> {gameRootPath}...";

            // Run your static cross-device installer on a background thread
            await Task.Run(() =>
            {
                ModInstaller.InstallZipMod(zipPath, gameRootPath);
            });

            if (vm != null)
            {
                vm.TelemetryLogStreamText += $"\n✅ [SUCCESS] '{Path.GetFileName(zipPath)}' deployed to GamingSSD!";
                vm.RefreshInstalledModsGrid();
            }
        }
        catch (Exception ex)
        {
            if (vm != null)
                vm.TelemetryLogStreamText += $"\n❌ [ERROR] Import failed: {ex.Message}";
        }
        finally
        {
            if (sender is Button btnRestore) btnRestore.IsEnabled = true;
        }
    }
}
