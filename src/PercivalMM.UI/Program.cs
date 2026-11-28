using System;
using System.Linq;
using Avalonia;
using Avalonia.ReactiveUI;
using PercivalMM.Core.Services;

namespace PercivalMM.UI
{
    class Program
    {
        public static string? StartupNxmUri { get; private set; }

        [STAThread]
        public static void Main(string[] args)
        {
            SharpCompress.Common.DictionarySizeLimits.Lzma2 = 512 * 1024 * 1024;
            SharpCompress.Common.DictionarySizeLimits.Lzma = 512 * 1024 * 1024;

            string? nxmArg = args.FirstOrDefault(a => a.StartsWith("nxm://", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(nxmArg))
            {
                if (NxmHandlerService.TryForwardToRunningInstance(nxmArg))
                {
                    return;
                }

                StartupNxmUri = nxmArg;
            }

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace()
                .UseReactiveUI();
    }
}
