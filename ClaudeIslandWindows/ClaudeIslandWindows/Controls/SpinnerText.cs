using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ClaudeIslandWindows.Controls;

/// <summary>
/// Self-animating spinner that cycles through star/asterisk symbols.
/// Matches the macOS ProcessingSpinner.
/// </summary>
public class SpinnerText : TextBlock
{
    private static readonly string[] Symbols = ["·", "✳", "∗", "✶", "✳", "∗"];
    private readonly DispatcherTimer _timer;
    private int _phase;

    public SpinnerText()
    {
        FontFamily = new FontFamily("Segoe UI Symbol");
        FontSize = 12;
        FontWeight = FontWeights.Bold;
        TextAlignment = TextAlignment.Center;
        Width = 14;
        Text = Symbols[0];

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _timer.Tick += (_, _) =>
        {
            _phase = (_phase + 1) % Symbols.Length;
            Text = Symbols[_phase];
        };

        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue) _timer.Start();
            else _timer.Stop();
        };
    }
}
