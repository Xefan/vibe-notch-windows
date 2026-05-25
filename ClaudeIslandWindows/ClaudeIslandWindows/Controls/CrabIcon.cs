using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ClaudeIslandWindows.Controls;

/// <summary>
/// Pixel art Claude crab icon matching the macOS version.
/// Drawn procedurally with rectangles. Legs animate when IsAnimating is true.
/// </summary>
public class CrabIcon : Canvas
{
    public static readonly Color DefaultCrabColor = Color.FromRgb(217, 120, 87); // Claude orange
    private static readonly Brush EyeBrush = Brushes.Black;

    private readonly Rectangle[] _legs = new Rectangle[4];
    private readonly DispatcherTimer _legTimer;
    private int _legPhase;

    // Original viewBox: 66 wide, 52 tall
    private const double OrigW = 66;
    private const double OrigH = 52;

    public static readonly DependencyProperty IsAnimatingProperty =
        DependencyProperty.Register(nameof(IsAnimating), typeof(bool),
            typeof(CrabIcon), new PropertyMetadata(false, OnIsAnimatingChanged));

    public bool IsAnimating
    {
        get => (bool)GetValue(IsAnimatingProperty);
        set => SetValue(IsAnimatingProperty, value);
    }

    public static readonly DependencyProperty BodyColorProperty =
        DependencyProperty.Register(nameof(BodyColor), typeof(Color),
            typeof(CrabIcon), new PropertyMetadata(DefaultCrabColor, OnBodyColorChanged));

    public Color BodyColor
    {
        get => (Color)GetValue(BodyColorProperty);
        set => SetValue(BodyColorProperty, value);
    }

    private SolidColorBrush _crabBrush = new(DefaultCrabColor);

    public CrabIcon()
    {
        Width = 18;
        Height = 14;
        ClipToBounds = false;

        _legTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _legTimer.Tick += (_, _) =>
        {
            _legPhase = (_legPhase + 1) % 4;
            UpdateLegs();
        };

        Loaded += (_, _) => DrawCrab();
    }

    private static void OnIsAnimatingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CrabIcon icon)
        {
            if ((bool)e.NewValue)
                icon._legTimer.Start();
            else
                icon._legTimer.Stop();
        }
    }

    private static void OnBodyColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CrabIcon icon)
        {
            icon._crabBrush = new SolidColorBrush((Color)e.NewValue);
            icon.DrawCrab();
        }
    }

    private void DrawCrab()
    {
        Children.Clear();
        var sx = Width / OrigW;
        var sy = Height / OrigH;

        // Body: (6, 0, 54, 39)
        AddRect(6, 0, 54, 39, _crabBrush, sx, sy);

        // Left antenna: (0, 13, 6, 13)
        AddRect(0, 13, 6, 13, _crabBrush, sx, sy);

        // Right antenna: (60, 13, 6, 13)
        AddRect(60, 13, 6, 13, _crabBrush, sx, sy);

        // Left eye: (12, 13, 6, 6.5)
        AddRect(12, 13, 6, 6.5, EyeBrush, sx, sy);

        // Right eye: (48, 13, 6, 6.5)
        AddRect(48, 13, 6, 6.5, EyeBrush, sx, sy);

        // Legs: positions [6, 18, 42, 54], y=39, height=13
        double[] legXPositions = [6, 18, 42, 54];
        for (int i = 0; i < 4; i++)
        {
            var leg = new Rectangle
            {
                Fill = _crabBrush,
                Width = 6 * sx,
                Height = 13 * sy
            };
            SetLeft(leg, legXPositions[i] * sx);
            SetTop(leg, 39 * sy);
            Children.Add(leg);
            _legs[i] = leg;
        }
    }

    private void AddRect(double x, double y, double w, double h, Brush fill, double sx, double sy)
    {
        var rect = new Rectangle
        {
            Fill = fill,
            Width = w * sx,
            Height = h * sy
        };
        SetLeft(rect, x * sx);
        SetTop(rect, y * sy);
        Children.Add(rect);
    }

    private void UpdateLegs()
    {
        var sy = Height / OrigH;
        double[][] offsets =
        [
            [3, -3, 3, -3],  // Phase 0
            [0, 0, 0, 0],    // Phase 1
            [-3, 3, -3, 3],  // Phase 2
            [0, 0, 0, 0],    // Phase 3
        ];

        var current = offsets[_legPhase % 4];
        for (int i = 0; i < 4; i++)
        {
            _legs[i].Height = (13 + current[i]) * sy;
        }
    }
}
