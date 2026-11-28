using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using PercivalMM.Core.Services;
using PercivalMM.UI.ViewModels;
using PercivalMM.UI.Views;

namespace PercivalMM.UI
{
    public partial class App : Application
    {
        private readonly CancellationTokenSource _ipcCts = new();

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override async void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                if (!string.IsNullOrWhiteSpace(Program.StartupNxmUri))
                {
                    bool confirmed = await ConfirmClosedAppNxmLaunchAsync(Program.StartupNxmUri);
                    if (!confirmed)
                    {
                        desktop.Shutdown();
                        return;
                    }
                }

                var vm = new MainWindowViewModel();
                var mainWindow = new MainWindow { DataContext = vm };
                desktop.MainWindow = mainWindow;
                mainWindow.Show();

                StartNxmIpcListener(vm, mainWindow, _ipcCts.Token);

                if (!string.IsNullOrWhiteSpace(Program.StartupNxmUri))
                {
                    _ = vm.HandleIncomingNxmUriAsync(Program.StartupNxmUri);
                }
            }

            base.OnFrameworkInitializationCompleted();
        }

        private static async Task<bool> ConfirmClosedAppNxmLaunchAsync(string nxmUri)
        {
            var req = NxmHandlerService.ParseNxmUri(nxmUri);
            var game = req != null ? GameRegistryService.GetByNexusDomain(req.GameDomain) : null;
            string gameLabel = game?.GameName ?? req?.GameDomain ?? "a Nexus Mods archive";

            var tcs = new TaskCompletionSource<bool>();
            var dialog = new Window
            {
                Title = "PercivalMM — Nexus Mod Download",
                Width = 440,
                Height = 185,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = Brush.Parse("#1e1e24")
            };

            var yesBtn = new Button
            {
                Content = "Yes, Open & Install",
                Background = Brush.Parse("#E54E00"),
                Foreground = Brushes.White,
                FontWeight = FontWeight.Bold,
                Padding = new Thickness(14, 7)
            };
            var cancelBtn = new Button
            {
                Content = "Cancel",
                Background = Brush.Parse("#2a2a35"),
                Foreground = Brush.Parse("#e4e4e7"),
                Padding = new Thickness(14, 7)
            };

            yesBtn.Click += (_, _) => { tcs.TrySetResult(true); dialog.Close(); };
            cancelBtn.Click += (_, _) => { tcs.TrySetResult(false); dialog.Close(); };
            dialog.Closed += (_, _) => tcs.TrySetResult(false);

            dialog.Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 14,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"PercivalMM is currently closed, but received a Nexus Mod download for:\n{gameLabel}",
                        Foreground = Brush.Parse("#f5f5f6"),
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 13
                    },
                    new TextBlock
                    {
                        Text = "Do you want to launch PercivalMM and install this mod? (Click Cancel if you intended to use another mod manager.)",
                        Foreground = Brush.Parse("#a1a1aa"),
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 11.5
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 10,
                        Children = { cancelBtn, yesBtn }
                    }
                }
            };

            dialog.Show();
            return await tcs.Task;
        }

        private static void StartNxmIpcListener(MainWindowViewModel vm, MainWindow mainWindow, CancellationToken ct)
        {
            _ = Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        await using var server = new NamedPipeServerStream(
                            NxmHandlerService.PipeName,
                            PipeDirection.In,
                            1,
                            PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous);

                        await server.WaitForConnectionAsync(ct);
                        using var reader = new StreamReader(server);
                        string? line = await reader.ReadLineAsync(ct);

                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                mainWindow.Activate();
                                _ = vm.HandleIncomingNxmUriAsync(line.Trim());
                            });
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        await Task.Delay(250, ct);
                    }
                }
            }, ct);
        }
    }
}
