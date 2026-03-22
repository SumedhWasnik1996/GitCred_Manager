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
using WpfMessageBox = System.Windows.MessageBox;

namespace GitCredMan.App;

public partial class App : WpfApplication
{
    private const string MutexName = "GitCredMan_SingleInstance_v1";
    private static Mutex? _mutex;

    public static IServiceProvider Services { get; private set; } = null!;

    private static readonly string CrashLog = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        "GitCredMan_crash.txt");

    // ── Startup ───────────────────────────────────────────────

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            var msg = ex.ExceptionObject?.ToString() ?? "Unknown error";
            WriteCrashLog("AppDomain.UnhandledException", msg);
            ShowFatalError(msg);
        };

        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            var msg = ex.Exception?.ToString() ?? "Unknown task error";
            WriteCrashLog("TaskScheduler.UnobservedTaskException", msg);
            ex.SetObserved();
            Dispatcher.Invoke(() => ShowFatalError(msg));
        };

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
            // Prevent duplicate windows if the user double-clicks the exe
            _mutex = new Mutex(true, MutexName, out bool isFirst);
            if (!isFirst)
            {
                WpfMessageBox.Show(
                    "Git Credential Manager is already open.",
                    "Already Running",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown(0);
                return;
            }

            Services = BuildServices();

            var settings = Services.GetRequiredService<ISettingsRepository>().Load();
            ApplyTheme(settings.Theme);

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

    // ── DI ────────────────────────────────────────────────────

    private static ServiceProvider BuildServices()
    {
        var sc = new ServiceCollection();
        sc.AddLogging(b => b.AddDebug().SetMinimumLevel(LogLevel.Debug));
        sc.AddSingleton<ICredentialStore, WindowsCredentialStore>();
        sc.AddSingleton<ISettingsRepository, JsonSettingsRepository>();
        sc.AddSingleton<IRepositoryScanner, RepositoryScannerService>();
        sc.AddSingleton<IGitConfigService, GitConfigService>();
        sc.AddSingleton<AccountService>();
        sc.AddSingleton<MainViewModel>();
        sc.AddTransient<AccountDialogViewModel>();
        sc.AddSingleton<MainWindow>();
        return sc.BuildServiceProvider();
    }

    // ── Theme ─────────────────────────────────────────────────

    public static void ApplyTheme(AppTheme theme)
    {
        var app = (App)Current;
        var merged = app.Resources.MergedDictionaries;
        if (merged.Count > 0) merged.RemoveAt(0);
        var uri = theme == AppTheme.Dark
            ? new Uri("Themes/DarkTheme.xaml", UriKind.Relative)
            : new Uri("Themes/LightTheme.xaml", UriKind.Relative);
        merged.Insert(0, new ResourceDictionary { Source = uri });
    }

    // ── Shutdown ──────────────────────────────────────────────

    protected override void OnExit(ExitEventArgs e)
    {
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        base.OnExit(e);
    }

    // ── Crash helpers ─────────────────────────────────────────

    private static void WriteCrashLog(string source, string message)
    {
        try { File.AppendAllText(CrashLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\n{message}\n\n"); }
        catch { }
    }

    private static void ShowFatalError(string message)
    {
        try
        {
            var short_msg = message.Length > 800
                ? message[..800] + $"\n\n... (see {CrashLog} for full detail)"
                : message + $"\n\n(also written to {CrashLog})";
            WpfMessageBox.Show(short_msg, "Git Credential Manager — Fatal Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { }
    }
}