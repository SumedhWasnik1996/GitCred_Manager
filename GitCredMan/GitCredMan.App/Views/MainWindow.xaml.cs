using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using GitCredMan.App.ViewModels;
using GitCredMan.Core.Models;

namespace GitCredMan.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private int _lastPage = -1;

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
#pragma warning disable SYSLIB1054
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
#pragma warning restore SYSLIB1054

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = _vm;

        SourceInitialized += OnSourceInitialized;

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentTheme))
                ApplyDwmTitleBar();
            if (e.PropertyName == nameof(MainViewModel.ActivePage))
                AnimatePageIn(_vm.ActivePage);
        };
        Loaded += (_, _) => AnimatePageIn(0);
    }

    // ── Page animation ────────────────────────────────────────

    private void AnimatePageIn(int page)
    {
        if (page == _lastPage) return;
        _lastPage = page;

        UIElement? target = page switch { 0 => AccountsPage, 1 => ReposPage, _ => null };
        if (target is null) return;

        var fadeIn = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(180)))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var slideIn = new ThicknessAnimation(
            new Thickness(0, 14, 0, 0), new Thickness(0),
            new Duration(TimeSpan.FromMilliseconds(200)))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

        target.BeginAnimation(OpacityProperty, fadeIn);
        if (target is FrameworkElement fe)
            fe.BeginAnimation(MarginProperty, slideIn);
    }

    // ── Nav handlers ──────────────────────────────────────────

    private void NavAccounts_Click(object sender, RoutedEventArgs e) => _vm.ActivePage = 0;
    private void NavRepos_Click(object sender, RoutedEventArgs e) => _vm.ActivePage = 1;
    private void ApplyAll_Click(object sender, RoutedEventArgs e) => _ = _vm.ApplyAllCommand.ExecuteAsync(null);
    private void Theme_Click(object sender, RoutedEventArgs e) => _vm.ToggleThemeCommand.Execute(null);

    // ── DWM title bar ─────────────────────────────────────────

    private void OnSourceInitialized(object? sender, EventArgs e) => ApplyDwmTitleBar();

    private void ApplyDwmTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        int dark = _vm.CurrentTheme == AppTheme.Dark ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }

    // ── Close ────────────────────────────────────────────────

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Save state then allow the window to close normally
        _vm.SaveNow();
        base.OnClosing(e);
    }

}