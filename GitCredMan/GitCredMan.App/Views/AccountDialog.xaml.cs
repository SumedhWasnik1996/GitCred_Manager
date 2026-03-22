using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using GitCredMan.App.ViewModels;
using GitCredMan.Core.Models;

namespace GitCredMan.App.Views;

public partial class AccountDialog : Window
{
    private readonly AccountDialogViewModel _vm;
    private bool   _showingToken = false;
    private string _tokenBuffer  = string.Empty;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public string CollectedToken => _tokenBuffer;

    public AccountDialog(AccountDialogViewModel vm)
    {
        InitializeComponent();
        _vm         = vm;
        DataContext = _vm;

        // Apply DWM dark/light title bar to match current theme
        SourceInitialized += (_, _) => ApplyTitleBar();

        if (_vm.IsEditMode)
        {
            TitleBlock.Text = "Edit Account";
            Title           = "Edit Account";
        }
    }

    private void ApplyTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        // Read IsDark from app resources (set by theme file)
        bool isDark = true;
        if (System.Windows.Application.Current.TryFindResource("IsDark") is bool b)
            isDark = b;
        int dark = isDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }

    private void ShowHideBtn_Click(object sender, RoutedEventArgs e)
    {
        _showingToken = !_showingToken;
        ShowHideBtn.Content = _showingToken ? "Hide" : "Show";

        if (_showingToken)
        {
            _tokenBuffer             = TokenPasswordBox.Password;
            TokenPlainBox.Text       = _tokenBuffer;
            TokenPasswordBox.Visibility = Visibility.Collapsed;
            TokenPlainBox.Visibility    = Visibility.Visible;
            TokenPlainBox.Focus();
        }
        else
        {
            _tokenBuffer              = TokenPlainBox.Text;
            TokenPasswordBox.Password = _tokenBuffer;
            TokenPlainBox.Text        = string.Empty;
            TokenPlainBox.Visibility     = Visibility.Collapsed;
            TokenPasswordBox.Visibility  = Visibility.Visible;
            TokenPasswordBox.Focus();
        }
    }

    private void TokenPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _tokenBuffer = TokenPasswordBox.Password;
        _vm.NotifyTokenChanged(!string.IsNullOrEmpty(_tokenBuffer));
    }

    private void TokenPlainBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _tokenBuffer = TokenPlainBox.Text;
        _vm.NotifyTokenChanged(!string.IsNullOrEmpty(_tokenBuffer));
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        _tokenBuffer = _showingToken ? TokenPlainBox.Text : TokenPasswordBox.Password;

        var err = _vm.ValidationError;
        if (err is not null)
        {
            ErrorText.Text          = err;
            ErrorBanner.Visibility  = Visibility.Visible;
            return;
        }

        ErrorBanner.Visibility = Visibility.Collapsed;
        DialogResult = true;
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        ClearToken();
        DialogResult = false;
    }

    public void ClearToken()
    {
        _tokenBuffer = string.Empty;
        TokenPasswordBox.Clear();
        TokenPlainBox.Text = string.Empty;
    }

    protected override void OnClosed(EventArgs e)
    {
        ClearToken();
        base.OnClosed(e);
    }
}
