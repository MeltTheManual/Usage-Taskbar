using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Usage.Core;

// WPF wins over Windows Forms for these, same as in ChipWindow.
using Color = System.Windows.Media.Color;
using Size = System.Windows.Size;

namespace Usage.App;

/// <summary>
/// Renders the hover card straight to a PNG so its design can be reviewed without hovering it.
/// This exists because the only way to see a tooltip is to put a pointer on it, and taking over the user's
/// mouse to check a layout is not acceptable. Developer tool, never reached during normal running.
/// </summary>
internal static class CardPreview
{
    private const double Scale = 3;

    public static void Save(RemainingSnapshot snapshot, string path)
    {
        var card = ChipWindow.BuildCard(snapshot);

        // A backdrop so the rounded corners are visible rather than blending into transparency.
        var backdrop = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(24, 24, 24)),
            Padding = new Thickness(16),
            Child = card
        };

        backdrop.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        backdrop.Arrange(new Rect(backdrop.DesiredSize));
        backdrop.UpdateLayout();

        var width = (int)Math.Ceiling(backdrop.ActualWidth * Scale);
        var height = (int)Math.Ceiling(backdrop.ActualHeight * Scale);
        var bitmap = new RenderTargetBitmap(width, height, 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        bitmap.Render(backdrop);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
