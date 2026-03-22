using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using GitCredMan.Core.Models;

namespace GitCredMan.App.Converters;

public class BoolToVisibility : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) =>
        v is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
        v is Visibility.Visible;
}

public class InverseBoolToVisibility : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) =>
        v is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
        v is not Visibility.Visible;
}

public class NullToVisibility : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) =>
        v is null ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
        throw new NotImplementedException();
}

public class InverseBool : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) => v is bool b && !b;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => v is bool b && !b;
}

/// <summary>true → accent brush, false → muted brush</summary>
public class BoolToAccentBrush : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        var app = System.Windows.Application.Current;
        return v is true
            ? app.FindResource("AccentBrush")
            : app.FindResource("TextMutedBrush");
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
        throw new NotImplementedException();
}

/// <summary>true → ★, false → ☆</summary>
public class BoolToStar : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) =>
        v is true ? "★" : "☆";
    public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
        throw new NotImplementedException();
}

/// <summary>Scanning → "⏹  Cancel", else → "🔍  Scan for Repos"</summary>
public class ScanButtonText : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) =>
        v is true ? "⏹  Cancel" : "🔍  Scan for Repos";
    public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
        throw new NotImplementedException();
}

/// <summary>HasRemote bool → success/muted brush</summary>
public class HasRemoteBrush : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        var app = System.Windows.Application.Current;
        return v is true
            ? app.FindResource("SuccessBrush")
            : app.FindResource("TextMutedBrush");
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
        throw new NotImplementedException();
}

/// <summary>AppTheme enum → display string</summary>
public class ThemeToDisplayName : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) =>
        v is AppTheme theme ? (theme == AppTheme.Dark ? "Dark" : "Light (Fluent)") : string.Empty;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
        throw new NotImplementedException();
}

/// <summary>0-based int index → Visibility — visible when matching param</summary>
public class IndexToVisibility : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        if (v is int idx && p is string ps && int.TryParse(ps, out int target))
            return idx == target ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
        throw new NotImplementedException();
}
