using System.IO;
using System.Windows;
using GitCredMan.App.ViewModels;
using GitCredMan.App.Views;
using GitCredMan.Core.Interfaces;
using GitCredMan.Core.Models;
using GitCredMan.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using WpfApplication = System.Windows.Application;
using WpfMessageBox  = System.Windows.MessageBox;

namespace GitCredMan.App;

public partial class App : WpfApplication
{
    private static Mutex? _mutex;
    public static IServiceProvider Services { get; private set; } = null!;

    // Crash log path — written before any UI is available
    private static readonly string CrashLog = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        "GitCredMan_crash.txt");

    protected override void OnStartup(StartupEventArgs e)
    {
        // ── Wire up ALL exception handlers FIRST, before anything else ──

        // 1. Non-UI thread / background exceptions
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            var msg = ex.ExceptionObject?.ToString() ?? "Unknown error";
            WriteCrashLog("AppDomain.UnhandledException", msg);
            ShowFatalError(msg);
        };

        // 2. Unobserved Task exceptions (async void / fire-and-forget)
        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            var msg = ex.Exception?.ToString() ?? "Unknown task error";
            WriteCrashLog("TaskScheduler.UnobservedTaskException", msg);
            ex.SetObserved();
            Dispatcher.Invoke(() => ShowFatalError(msg));
        };

        // 3. UI thread exceptions
        DispatcherUnhandledException += (_, ex) =>
        {
            var msg = ex.Exception?.ToString() ?? "Unknown dispatcher error";
            WriteCrashLog("DispatcherUnhandledException", msg);
            ShowFatalError(msg);
            ex.Handled = true;
        };

        base.OnStartup(e);

        try
        {
            // Single-instance guard
            _mutex = new Mutex(true, "GitCredMan_SingleInstance_v1", out bool isFirst);
            if (!isFirst)
            {
                WpfMessageBox.Show("Git Credential Manager is already running.",
                    "Already Running", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            // Build DI container
            Services = BuildServices();

            // Apply saved theme before first window opens
            var settings = Services.GetRequiredService<ISettingsRepository>().Load();
            ApplyTheme(settings.Theme);

            // Show main window
            var window = Services.GetRequiredService<MainWindow>();
            window.Show();
        }
        catch (Exception ex)
        {
            WriteCrashLog("OnStartup catch", ex.ToString());
            ShowFatalError(ex.ToString());
            Shutdown(1);
        }
    }

    // ── DI composition root ───────────────────────────────────

    private static ServiceProvider BuildServices()
    {
        var sc = new ServiceCollection();

        sc.AddLogging(b => b.AddDebug().SetMinimumLevel(LogLevel.Debug));

        sc.AddSingleton<ICredentialStore,    WindowsCredentialStore>();
        sc.AddSingleton<ISettingsRepository, JsonSettingsRepository>();
        sc.AddSingleton<IRepositoryScanner,  RepositoryScannerService>();
        sc.AddSingleton<IGitConfigService,   GitConfigService>();

        sc.AddSingleton<AccountService>();
        sc.AddSingleton<MainViewModel>();
        sc.AddTransient<AccountDialogViewModel>();
        sc.AddSingleton<MainWindow>();

        return sc.BuildServiceProvider();
    }

    // ── Theme switching ───────────────────────────────────────

    public static void ApplyTheme(AppTheme theme)
    {
        var app    = (App)Current;
        var merged = app.Resources.MergedDictionaries;

        if (merged.Count > 0) merged.RemoveAt(0);

        var uri = theme == AppTheme.Dark
            ? new Uri("Themes/DarkTheme.xaml",  UriKind.Relative)
            : new Uri("Themes/LightTheme.xaml", UriKind.Relative);

        merged.Insert(0, new ResourceDictionary { Source = uri });
    }

    // ── Shutdown ──────────────────────────────────────────────

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    // ── Crash helpers ─────────────────────────────────────────

    private static void WriteCrashLog(string source, string message)
    {
        try
        {
            var text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\n{message}\n\n";
            File.AppendAllText(CrashLog, text);
        }
        catch { /* never throw from crash handler */ }
    }

    private static void ShowFatalError(string message)
    {
        try
        {
            // Truncate for readability — full detail is in the crash log
            var short_msg = message.Length > 800
                ? message[..800] + $"\n\n... (see {CrashLog} for full detail)"
                : message + $"\n\n(also written to {CrashLog})";

            WpfMessageBox.Show(
                short_msg,
                "Git Credential Manager — Fatal Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch { /* swallow — already in crash path */ }
    }
}
