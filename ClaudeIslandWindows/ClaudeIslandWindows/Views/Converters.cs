using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ClaudeIslandWindows.Models;
using ClaudeIslandWindows.ViewModels;

namespace ClaudeIslandWindows.Views;

public class PhaseColorConverter : IValueConverter
{
    // Claude orange matching macOS: Color(red: 0.85, green: 0.47, blue: 0.34)
    private static readonly SolidColorBrush ClaudeOrange = new(Color.FromRgb(217, 120, 87));
    private static readonly SolidColorBrush Amber = new(Color.FromRgb(255, 180, 0));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not SessionPhase phase) return Brushes.Gray;
        return phase.Kind switch
        {
            SessionPhaseKind.WaitingForApproval => Amber,
            SessionPhaseKind.Processing => ClaudeOrange,
            SessionPhaseKind.WaitingForInput => new SolidColorBrush(Color.FromRgb(100, 200, 100)),
            SessionPhaseKind.Compacting => new SolidColorBrush(Color.FromRgb(100, 150, 255)),
            SessionPhaseKind.Idle => Brushes.Gray,
            SessionPhaseKind.Ended => new SolidColorBrush(Color.FromRgb(80, 80, 80)),
            _ => Brushes.Gray
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class PhaseTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not SessionPhase phase) return "Unknown";
        return phase.Kind switch
        {
            SessionPhaseKind.WaitingForApproval => phase.Permission?.ToolName ?? "Needs approval",
            SessionPhaseKind.Processing => "Processing...",
            SessionPhaseKind.WaitingForInput => "Done — waiting for input",
            SessionPhaseKind.Compacting => "Compacting context...",
            SessionPhaseKind.Idle => "Idle",
            SessionPhaseKind.Ended => "Ended",
            _ => "Unknown"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class ApprovalVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SessionPhase phase && phase.Kind == SessionPhaseKind.WaitingForApproval)
            return Visibility.Visible;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class ContentVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ContentType content && parameter is string target)
        {
            return content.ToString() == target ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class GreaterThanZeroConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int n && n > 0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class EmptyVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int n && n == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// Collapses a TextBlock when the bound string is null/empty.
public class NullOrEmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Shows spinner text for processing/approval phases.</summary>
public class PhaseSpinnerConverter : IValueConverter
{
    private static readonly string[] Symbols = ["·", "✳", "∗", "✶", "✳", "∗"];
    private static int _phase;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SessionPhase phase &&
            phase.Kind is SessionPhaseKind.Processing or SessionPhaseKind.WaitingForApproval or SessionPhaseKind.Compacting)
        {
            return Symbols[_phase++ % Symbols.Length];
        }
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Shows spinner for active phases, hides for idle.</summary>
public class SpinnerVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SessionPhase phase &&
            phase.Kind is SessionPhaseKind.Processing or SessionPhaseKind.WaitingForApproval or SessionPhaseKind.Compacting)
            return Visibility.Visible;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Shows dot for idle/waiting phases, hides for active.</summary>
public class DotVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SessionPhase phase &&
            phase.Kind is SessionPhaseKind.WaitingForInput or SessionPhaseKind.Idle or SessionPhaseKind.Ended)
            return Visibility.Visible;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
