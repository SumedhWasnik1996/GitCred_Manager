using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using GitCredMan.App.ViewModels;

namespace GitCredMan.App.Views;

public partial class AssignAccountDialog : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public AssignAccountDialog(AssignAccountDialogViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        SourceInitialized += (_, _) => ApplyTitleBar();
    }

    private void ApplyTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        bool isDark = true;
        if (System.Windows.Application.Current.TryFindResource("IsDark") is bool b)
            isDark = b;
        int dark = isDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }

    private void ApplyBtn_Click(object sender, RoutedEventArgs e)  => DialogResult = true;
    private void CancelBtn_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
