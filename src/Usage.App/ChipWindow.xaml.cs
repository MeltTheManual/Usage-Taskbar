using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Usage.Core;

// The project references both WPF and Windows Forms, so these names exist twice. WPF wins here.
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Panel = System.Windows.Controls.Panel;
using ToolTip = System.Windows.Controls.ToolTip;

namespace Usage.App;

/// <summary>
/// The taskbar chip. It is a transparent always-on-top window sitting just left of the notification area,
/// which is a squatter rather than a tenant: Windows reserves no space for it.
///
/// The first version of this collided with the taskbar overflow arrow. The cause was not a wrong anchor,
/// it was staleness. TrayNotifyWnd's left edge moves whenever the tray gains or loses an icon, and the old
/// code only re-checked every three minutes, so it sat on top of whatever had moved into its spot. Placement
/// now runs four times a second, which corrects any shift within a quarter of a second.
/// </summary>
internal partial class ChipWindow : Window
{
    private static readonly Brush OkBrush = Frozen(Color.FromRgb(242, 242, 242));
    private static readonly Brush LowBrush = Frozen(Color.FromRgb(255, 176, 32));
    private static readonly Brush QuietBrush = Frozen(Color.FromRgb(154, 160, 166));

    // The chip's three bands, settled on 2026-08-25 after a four-band version was built and dropped.
    // Three colours are far enough apart that no two of them can be confused, which four never quite managed.
    // Plenty is deliberately a soft green rather than a vivid one: most of the time nothing is wrong, and the
    // chip should not keep announcing that. The hover card keeps OkBrush and LowBrush and is untouched.
    private static readonly Brush ChipPlentyBrush = Frozen(Color.FromRgb(143, 214, 162));
    private static readonly Brush ChipFairBrush = Frozen(Color.FromRgb(255, 213, 79));
    private static readonly Brush ChipLowBrush = Frozen(Color.FromRgb(255, 69, 58));

    private static readonly Brush CardBrush = Frozen(Color.FromRgb(42, 42, 42));
    private static readonly Brush CardBorderBrush = Frozen(Color.FromRgb(61, 61, 61));
    private static readonly Brush DividerBrush = Frozen(Color.FromRgb(56, 56, 56));
    private static readonly Brush TrackBrush = Frozen(Color.FromRgb(60, 60, 60));
    private static readonly Brush LabelBrush = Frozen(Color.FromRgb(174, 180, 186));
    private static readonly Brush FaintBrush = Frozen(Color.FromRgb(136, 142, 148));
    private static readonly Brush ClearBrush = Frozen(Color.FromArgb(0, 0, 0, 0));

    // Identity colours for the meters: Claude clay, Codex green. A low window overrides these with amber,
    // because the warning matters more than knowing which provider you are looking at.
    private static readonly Brush ClaudeAccent = Frozen(Color.FromRgb(217, 119, 87));
    private static readonly Brush CodexAccent = Frozen(Color.FromRgb(16, 163, 127));

    private static readonly FontFamily Ui = new("Segoe UI");

    private readonly DispatcherTimer _placeTimer;
    private readonly ChipMenu _menu;

    /// <summary>Off every monitor. Where the chip waits while a full-screen app is in front.</summary>
    private const double ParkedX = -32000;
    private const double ParkedY = -32000;

    private double _lastX = double.NaN;
    private double _lastY = double.NaN;
    private bool _parked;
    private bool _placedOnce;

    public ChipWindow(ChipMenu menu)
    {
        InitializeComponent();
        _menu = menu;

        SourceInitialized += (_, _) => ApplyNoActivate();
        MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            _menu.ShowAtCursor();
        };

        // Four times a second. Cheap enough to be invisible on a modern machine, fast enough that a tray
        // width change is corrected before the eye registers it.
        _placeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _placeTimer.Tick += (_, _) => Reposition();
        _placeTimer.Start();
    }

    public void Apply(RemainingSnapshot snapshot)
    {
        SetSegment(ClaudeRun, "Claude", snapshot.Claude);
        SetSegment(CodexRun, "Codex", snapshot.Codex);

        // The dot only separates two things. With one provider hidden it would dangle, and with both hidden
        // the chip would be a lone dot floating next to the clock.
        var both = !snapshot.Claude.IsHidden && !snapshot.Codex.IsHidden;
        SeparatorRun.Text = both ? "  ·  " : "";

        if (snapshot.Claude.IsHidden && snapshot.Codex.IsHidden)
        {
            // Keep a word on the taskbar so the right-click menu stays reachable if both were turned off.
            var bothMissing = snapshot.Claude.Status == ReadingStatus.NotInstalled
                && snapshot.Codex.Status == ReadingStatus.NotInstalled;
            ClaudeRun.Text = bothMissing ? "Usage: no login found" : "Usage";
            ClaudeRun.Foreground = QuietBrush;
        }

        Root.ToolTip = BuildHoverCard(snapshot);

        // The window sizes itself to the text, so the new width must settle before placement reads it.
        UpdateLayout();
        Reposition();
    }

    public void StopPlacing() => _placeTimer.Stop();

    private static void SetSegment(Run run, string name, ProviderReading reading)
    {
        if (reading.IsHidden)
        {
            run.Text = "";
            return;
        }

        run.Text = ChipText.FormatProvider(name, reading);
        run.Foreground = reading.Status switch
        {
            ReadingStatus.Ok when reading.WeeklyRemaining is { } remaining => LevelBrush(ChipText.LevelFor(remaining)),
            // Ok with no weekly number prints "--", which is not a reading worth colouring.
            ReadingStatus.Ok => OkBrush,
            _ => QuietBrush
        };
    }

    private static Brush LevelBrush(ChipLevel level) => level switch
    {
        ChipLevel.Plenty => ChipPlentyBrush,
        ChipLevel.Fair => ChipFairBrush,
        _ => ChipLowBrush
    };

    private static ToolTip BuildHoverCard(RemainingSnapshot snapshot)
    {
        // The tooltip chrome is emptied out so the rounded card is the only thing that shows.
        return new ToolTip
        {
            Content = BuildCard(snapshot),
            Background = ClearBrush,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Placement = PlacementMode.Top,
            VerticalOffset = -8,
            HasDropShadow = false
        };
    }

    /// <summary>
    /// The hover card on its own, separate from the tooltip that normally carries it, so
    /// <c>--card-preview</c> can render exactly what a hover would show without anyone touching the mouse.
    /// </summary>
    internal static Border BuildCard(RemainingSnapshot snapshot)
    {
        var body = new StackPanel { MinWidth = 228 };

        // Every number on this card is what is left, not what has been spent. T3 shows the opposite shape,
        // so saying it once at the top removes the only ambiguity the card has.
        body.Children.Add(new TextBlock
        {
            Text = "Remaining",
            FontFamily = Ui,
            FontWeight = FontWeights.SemiBold,
            FontSize = 10.5,
            Foreground = FaintBrush,
            Margin = new Thickness(0, 0, 0, 7)
        });
        body.Children.Add(new Border
        {
            Height = 1,
            Background = DividerBrush,
            Margin = new Thickness(0, 0, 0, 11)
        });

        // A tool that is not on this machine, or one the user turned off, gets no row at all.
        // "first" drives the divider, so it has to mean "first row actually drawn", not "Claude".
        var drewOne = false;
        if (!snapshot.Claude.IsHidden)
        {
            AddProvider(body, "Claude", ClaudeAccent, snapshot.Claude, first: true);
            drewOne = true;
        }

        if (!snapshot.Codex.IsHidden)
        {
            AddProvider(body, "Codex", CodexAccent, snapshot.Codex, first: !drewOne);
            drewOne = true;
        }

        if (!drewOne)
        {
            var bothMissing = snapshot.Claude.Status == ReadingStatus.NotInstalled
                && snapshot.Codex.Status == ReadingStatus.NotInstalled;
            body.Children.Add(new TextBlock
            {
                Text = bothMissing
                    ? "No Claude Code or Codex login found on this PC."
                    : "Right-click to choose what appears.",
                FontFamily = Ui,
                FontSize = 11.5,
                Foreground = LabelBrush,
                TextWrapping = TextWrapping.Wrap
            });
        }

        return new Border
        {
            Background = CardBrush,
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 11, 14, 12),
            Child = body
        };
    }

    private static void AddProvider(Panel body, string name, Brush accent, ProviderReading reading, bool first)
    {
        if (!first)
        {
            body.Children.Add(new Border
            {
                Height = 1,
                Background = DividerBrush,
                Margin = new Thickness(0, 12, 0, 12)
            });
        }

        body.Children.Add(new TextBlock
        {
            Text = name,
            FontFamily = Ui,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Foreground = reading.IsLow ? LowBrush : OkBrush,
            Margin = new Thickness(0, 0, 0, 8)
        });

        // A window the provider does not report never reaches here at all. Codex reports no session window,
        // so Codex draws one meter where Claude draws two. Never a zero, never a guess.
        var windows = ChipText.Windows(reading);
        if (windows.Count == 0)
        {
            body.Children.Add(new TextBlock
            {
                Text = ChipText.StatusLine(reading),
                FontFamily = Ui,
                FontSize = 11.5,
                Foreground = LabelBrush,
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        for (var i = 0; i < windows.Count; i++)
        {
            body.Children.Add(BuildWindow(windows[i], accent, last: i == windows.Count - 1));
        }
    }

    private static UIElement BuildWindow(RemainingWindow window, Brush accent, bool last)
    {
        var block = new StackPanel { Margin = new Thickness(0, 0, 0, last ? 0 : 11) };

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        header.Children.Add(new TextBlock
        {
            Text = window.Label,
            FontFamily = Ui,
            FontSize = 11.5,
            Foreground = LabelBrush,
            VerticalAlignment = VerticalAlignment.Bottom
        });

        var percent = new TextBlock
        {
            Text = ChipText.Percent(window.Remaining),
            FontFamily = Ui,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12.5,
            Foreground = window.IsLow ? LowBrush : OkBrush,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        Grid.SetColumn(percent, 1);
        header.Children.Add(percent);

        block.Children.Add(header);
        block.Children.Add(BuildMeter(window, accent));

        var reset = ChipText.ResetText(window.ResetsAt);
        if (reset.Length > 0)
        {
            block.Children.Add(new TextBlock
            {
                Text = reset,
                FontFamily = Ui,
                FontSize = 11,
                Foreground = FaintBrush,
                Margin = new Thickness(0, 5, 0, 0)
            });
        }

        return block;
    }

    /// <summary>
    /// The bar. Proportions come from two star-sized columns rather than any measuring, so it stays correct
    /// at any card width and needs no layout pass to get right.
    /// </summary>
    private static UIElement BuildMeter(RemainingWindow window, Brush accent)
    {
        var remaining = Math.Clamp(window.Remaining, 0, 100);

        var fill = new Border
        {
            Background = window.IsLow ? LowBrush : accent,
            CornerRadius = new CornerRadius(3)
        };
        Grid.SetColumn(fill, 0);

        var proportions = new Grid();
        proportions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(remaining, GridUnitType.Star) });
        proportions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - remaining, GridUnitType.Star) });
        proportions.Children.Add(fill);

        return new Border
        {
            Height = 6,
            Margin = new Thickness(0, 6, 0, 0),
            CornerRadius = new CornerRadius(3),
            Background = TrackBrush,
            Child = proportions
        };
    }

    private void ApplyNoActivate()
    {
        // WS_EX_TOPMOST is deliberately NOT set here. Microsoft requires that bit to be changed through
        // SetWindowPos, not SetWindowLong. Setting it by hand does nothing to real z-order while making the
        // window self-report as topmost, which is precisely what made this window's z-order bug invisible to
        // every diagnostic run against it. Topmost is owned by the XAML property and by RaiseAboveTaskbar.
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle);
        NativeMethods.SetWindowLongPtr(
            hwnd,
            NativeMethods.GwlExStyle,
            style | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow);
    }

    /// <summary>
    /// Re-asserts the chip's rank inside the topmost band, every tick.
    ///
    /// This is the fix for the bug that looked like a rendering failure four separate times. The taskbar is
    /// topmost too. Explorer drops Shell_TrayWnd out of the topmost band whenever a full-screen app appears
    /// and puts it back when that app leaves, and re-entering the band lands it above every topmost window
    /// already there, this chip included. The chip then keeps painting perfectly, at the right size, in the
    /// right place, underneath an opaque taskbar, forever, because nothing ever raised it again.
    ///
    /// WS_EX_TOPMOST reports band membership, not rank within the band, so the chip kept reporting itself as
    /// topmost throughout. Assigning Left, Top or a SizeToContent resize all move the window with
    /// SWP_NOZORDER, so none of them can ever repair it.
    ///
    /// SWP_NOMOVE and SWP_NOSIZE make this a z-order-only call. It cannot touch position, size, or the
    /// layered surface, and it is exactly what WPF's own Topmost setter does. Running it every tick means
    /// any future cause of demotion heals itself within 250ms.
    /// </summary>
    private void RaiseAboveTaskbar(nint hwnd)
    {
        // The one thing that legitimately sits on top of the chip is the chip's own right-click menu, which
        // opens under the pointer and therefore directly over it. Raising four times a second would flicker
        // the chip through it. Notification toasts and the volume overlay sit above the taskbar strip rather
        // than inside it, so they do not overlap these 18 pixels and need no special handling.
        if (_menu.IsOpen)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HwndTopmost,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
    }

    /// <summary>
    /// Puts the chip beside the notification area.
    ///
    /// Everything here works in WPF units and moves the window through Left and Top rather than through
    /// SetWindowPos. That is not a preference, it is the fix for a bug that bit twice. This window is
    /// AllowsTransparency, so it is layered and WPF owns its layered surface. Shoving the HWND around
    /// underneath WPF desynchronises that surface and leaves a window that Windows reports as present,
    /// visible and correctly placed while it paints absolutely nothing. Driving the window through its own
    /// properties keeps WPF's bookkeeping honest, and it removes the unit mixing that was a latent clipping
    /// bug on scaled displays as a bonus.
    /// </summary>
    private void Reposition()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == 0)
            {
                return;
            }

            var tray = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (tray == 0 || !NativeMethods.GetWindowRect(tray, out var trayRect))
            {
                Park("no taskbar");
                return;
            }

            if (NativeMethods.ForegroundIsFullScreen())
            {
                Park("full-screen app in front");
                return;
            }

            var width = ActualWidth;
            var height = ActualHeight;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            // Win32 hands back device pixels; everything below is WPF units, so convert once here.
            var dpi = VisualTreeHelper.GetDpi(this);
            var trayRight = trayRect.Right / dpi.DpiScaleX;
            var trayTop = trayRect.Top / dpi.DpiScaleY;
            var trayBottom = trayRect.Bottom / dpi.DpiScaleY;

            // TrayNotifyWnd is the whole right-hand cluster, overflow arrow and clock included, so its left
            // edge is the correct thing to sit beside. The rough offset is only a fallback for the unlikely
            // case where that window cannot be found at all.
            var x = trayRight - width - 290;
            var notify = NativeMethods.FindWindowEx(tray, 0, "TrayNotifyWnd", null);
            if (notify != 0 && NativeMethods.GetWindowRect(notify, out var notifyRect))
            {
                x = (notifyRect.Left / dpi.DpiScaleX) - width - 10;
            }

            var y = trayTop + Math.Max(0, ((trayBottom - trayTop) - height) / 2);

            if (_parked)
            {
                _parked = false;
                AppLog.Write("chip back on the taskbar");
            }

            // Before the early-return, so rank is restored on every tick and not only when the chip moves.
            // Explorer re-promotes the taskbar above us at moments that have nothing to do with our position.
            RaiseAboveTaskbar(hwnd);

            if (Same(x, _lastX) && Same(y, _lastY))
            {
                return;
            }

            _lastX = x;
            _lastY = y;
            Left = x;
            Top = y;

            if (!_placedOnce)
            {
                // Revealed only once it is standing in the right place, so it never flashes mid-screen.
                _placedOnce = true;
                Opacity = 1;
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("place " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>
    /// Gets out of the way for a full-screen app by moving off every monitor. Moving is used rather than
    /// fading so that no topmost window of ours sits over an exclusive full-screen game at all.
    /// </summary>
    private void Park(string reason)
    {
        if (_parked)
        {
            return;
        }

        _parked = true;
        _lastX = ParkedX;
        _lastY = ParkedY;
        Left = ParkedX;
        Top = ParkedY;
        AppLog.Write("chip stepping aside: " + reason);
    }

    private static bool Same(double a, double b) => Math.Abs(a - b) < 0.5;

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
