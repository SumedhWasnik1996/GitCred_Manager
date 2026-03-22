using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using GitCredMan.App.ViewModels;
using GitCredMan.Core.Models;

using WinFormsNotifyIcon       = System.Windows.Forms.NotifyIcon;
using WinFormsContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using WinFormsToolStripSep     = System.Windows.Forms.ToolStripSeparator;
using WinFormsToolTipIcon      = System.Windows.Forms.ToolTipIcon;

namespace GitCredMan.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private WinFormsNotifyIcon? _trayIcon;
    private int _lastPage = -1;

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm         = vm;
        DataContext = _vm;

        SourceInitialized += OnSourceInitialized;

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentTheme))
                ApplyDwmTitleBar();

            if (e.PropertyName == nameof(MainViewModel.ActivePage))
                AnimatePageIn(_vm.ActivePage);
        };

        InitTrayIcon();

        // Trigger initial page animation after load
        Loaded += (_, _) => AnimatePageIn(0);
    }

    // ── Page fade-in animation ────────────────────────────────

    private void AnimatePageIn(int page)
    {
        if (page == _lastPage) return;
        _lastPage = page;

        UIElement? target = page switch
        {
            0 => AccountsPage,
            1 => ReposPage,
            _ => null
        };

        if (target is null) return;

        var fadeIn = new DoubleAnimation(0, 1,
            new Duration(TimeSpan.FromMilliseconds(180)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        var slideIn = new ThicknessAnimation(
            new Thickness(0, 14, 0, 0),
            new Thickness(0),
            new Duration(TimeSpan.FromMilliseconds(200)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        target.BeginAnimation(OpacityProperty, fadeIn);
        if (target is FrameworkElement fe)
            fe.BeginAnimation(MarginProperty, slideIn);
    }

    // ── Nav click handlers ────────────────────────────────────

    private void NavAccounts_Click(object sender, RoutedEventArgs e) =>
        _vm.ActivePage = 0;

    private void NavRepos_Click(object sender, RoutedEventArgs e) =>
        _vm.ActivePage = 1;

    private void ApplyAll_Click(object sender, RoutedEventArgs e) =>
        _ = _vm.ApplyAllCommand.ExecuteAsync(null);

    private void Theme_Click(object sender, RoutedEventArgs e) =>
        _vm.ToggleThemeCommand.Execute(null);

    // ── DWM title bar ─────────────────────────────────────────

    private void OnSourceInitialized(object? sender, EventArgs e) =>
        ApplyDwmTitleBar();

    private void ApplyDwmTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        int dark = _vm.CurrentTheme == AppTheme.Dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }

    // ── System tray ───────────────────────────────────────────

    private void InitTrayIcon()
    {
        // Load custom icon for tray
        var iconUri  = new Uri("pack://application:,,,/Resources/Icons/app.ico");
        var iconStream = System.Windows.Application.GetResourceStream(iconUri)?.Stream;
        var trayIcon  = iconStream is not null
            ? new System.Drawing.Icon(iconStream)
            : System.Drawing.SystemIcons.Application;

        _trayIcon = new WinFormsNotifyIcon
        {
            Icon    = trayIcon,
            Text    = "Git Credential Manager",
            Visible = true,
        };

        var ctxMenu = new WinFormsContextMenuStrip();
        ctxMenu.Items.Add("Show", null, (_, _) => BringToFront());
        ctxMenu.Items.Add(new WinFormsToolStripSep());
        ctxMenu.Items.Add("Exit", null, (_, _) => HardExit());

        _trayIcon.ContextMenuStrip = ctxMenu;
        _trayIcon.DoubleClick     += (_, _) => BringToFront();
    }

    private void BringToFront()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void HardExit()
    {
        _vm.SaveNow();
        _trayIcon?.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    // ── Close → minimise to tray ──────────────────────────────

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
        _trayIcon?.ShowBalloonTip(2000,
            "Git Credential Manager",
            "Running in the background. Right-click the tray icon to exit.",
            WinFormsToolTipIcon.Info);
    }

    protected override void OnClosed(EventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnClosed(e);
    }
}
