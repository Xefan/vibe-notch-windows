using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ClaudeIslandWindows.Models;
using ClaudeIslandWindows.ViewModels;

namespace ClaudeIslandWindows.Views;

public class PhaseColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not SessionPhase phase) return Brushes.Gray;
        return phase.Kind switch
        {
            SessionPhaseKind.WaitingForApproval => new SolidColorBrush(Color.FromRgb(255, 180, 0)),
            SessionPhaseKind.Processing => new SolidColorBrush(Color.FromRgb(255, 140, 0)),
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
            SessionPhaseKind.WaitingForApproval => $"Needs approval: {phase.Permission?.ToolName ?? "tool"}",
            SessionPhaseKind.Processing => "Processing...",
            SessionPhaseKind.WaitingForInput => "Waiting for input",
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
