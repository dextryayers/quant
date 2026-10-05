using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Quant.Desktop.Services;
using Quant.Desktop.ViewModels;
using Quant.Desktop.Views;

namespace Quant.Desktop;

public partial class App : Application
{
    public static ConfigService Config { get; private set; } = new(System.Array.Empty<string>());

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var args = (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Args ?? System.Array.Empty<string>();
        Config = new ConfigService(args);
        LoggerService.Init();
        LoggerService.Info("app", $"start engine port {Config.EnginePort}");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(Config.BaseUrl, Config.EnginePort),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}