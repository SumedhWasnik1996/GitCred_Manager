using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using GitCredMan.App.ViewModels;
using GitCredMan.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GitCredMan.App.Views;

public partial class AccountDialog : Window
{
    private readonly AccountDialogViewModel _vm;
    private readonly OAuthService _oauthSvc;

    private CancellationTokenSource? _oauthCts;
    private string? _currentVerificationUri;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public AccountDialog(AccountDialogViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        _oauthSvc = App.Services.GetRequiredService<OAuthService>();
        DataContext = _vm;

        SourceInitialized += (_, _) => ApplyTitleBar();

        if (_vm.IsEditMode)
        {
            TitleBlock.Text = "Edit Account";
            Title = "Edit Account";
        }

        StartSpinnerAnimation();
    }

    // ── Spinner ───────────────────────────────────────────────

    private void StartSpinnerAnimation()
    {
        var anim = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(1)))
        {
            RepeatBehavior = RepeatBehavior.Forever,
        };
        SpinnerTransform.BeginAnimation(
            System.Windows.Media.RotateTransform.AngleProperty, anim);
    }

    // ── Sign in ───────────────────────────────────────────────

    private async void SignInBtn_Click(object sender, RoutedEventArgs e)
    {
        var host = _vm.Host.Trim().ToLowerInvariant();
        var provider = OAuthProvider.For(host);

        if (provider is null)
        {
            ShowError($"OAuth is not configured for '{host}'.\n\n" +
                      "Supported hosts: github.com, gitlab.com");
            return;
        }

        if (provider.ClientId.StartsWith("YOUR_"))
        {
            ShowError("OAuth client ID not configured.\n\n" +
                      $"Register an OAuth App at {host}, then update the ClientId " +
                      "in OAuthService.cs → OAuthProvider.KnownProviders.");
            return;
        }

        CancelOAuthIfRunning();
        _oauthCts = new CancellationTokenSource();

        _vm.IsOAuthPending = true;
        _vm.OAuthStatus = "Requesting authorisation code…";
        _vm.OAuthCompleted = false;
        DeviceCodeCard.Visibility = Visibility.Collapsed;
        ErrorBanner.Visibility = Visibility.Collapsed;

        // Step 1 — get device + user codes
        var (deviceCode, startError) =
            await _oauthSvc.StartDeviceFlowAsync(provider, _oauthCts.Token);

        if (deviceCode is null)
        {
            _vm.IsOAuthPending = false;
            ShowError($"Could not start sign-in:\n{startError}");
            return;
        }

        // Step 2 — show code, open browser
        _currentVerificationUri = deviceCode.VerificationUri;
        UserCodeText.Text = deviceCode.UserCode;
        VerificationUriText.Text = deviceCode.VerificationUri;
        DeviceCodeCard.Visibility = Visibility.Visible;
        _vm.OAuthUserCode = deviceCode.UserCode;
        _vm.OAuthStatus = "Waiting for browser authorisation…";

        try
        {
            Process.Start(new ProcessStartInfo(deviceCode.VerificationUri)
            { UseShellExecute = true });
        }
        catch { /* user can click the link manually */ }

        // Step 3 — poll until approved / timeout / cancel
        var progress = new Progress<string>(msg =>
            Dispatcher.Invoke(() => _vm.OAuthStatus = msg));

        var result = await _oauthSvc.PollForTokenAsync(
            provider, deviceCode, progress, _oauthCts.Token);

        _vm.IsOAuthPending = false;

        if (!result.Success)
        {
            if (result.Error != "Cancelled.")
                ShowError($"Sign-in failed:\n{result.Error}");
            return;
        }

        // Step 4 — success
        _vm.NotifyOAuthCompleted(
            result.AccessToken!,
            result.RefreshToken,
            result.Username,
            result.Email);
    }

    // ── Copy code / open link ─────────────────────────────────

    private void CopyCodeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_vm.OAuthUserCode))
            Clipboard.SetText(_vm.OAuthUserCode);
    }

    private void VerificationUri_Click(object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!string.IsNullOrEmpty(_currentVerificationUri))
            try
            {
                Process.Start(new ProcessStartInfo(_currentVerificationUri)
                { UseShellExecute = true });
            }
            catch { }
    }

    // ── Save / Cancel ─────────────────────────────────────────

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        var err = _vm.ValidationError;
        if (err is not null) { ShowError(err); return; }
        ErrorBanner.Visibility = Visibility.Collapsed;
        DialogResult = true;
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        CancelOAuthIfRunning();
        ClearTokens();
        DialogResult = false;
    }

    // ── Helpers ───────────────────────────────────────────────

    public void ClearTokens()
    {
        _vm.OAuthAccessToken = null;
        _vm.OAuthRefreshToken = null;
    }

    private void CancelOAuthIfRunning()
    {
        _oauthCts?.Cancel();
        _oauthCts?.Dispose();
        _oauthCts = null;
        _vm.IsOAuthPending = false;
    }

    private void ShowError(string msg)
    {
        ErrorText.Text = msg;
        ErrorBanner.Visibility = Visibility.Visible;
    }

    protected override void OnClosed(EventArgs e)
    {
        CancelOAuthIfRunning();
        ClearTokens();
        base.OnClosed(e);
    }

    private void ApplyTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        bool isDark = Application.Current.TryFindResource("IsDark") is bool b && b;
        int dark = isDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }
}