using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QrGuard.Decoder;

namespace QrGuard.Windows;

internal sealed class FixtureWindow : Window
{
    private readonly Canvas _canvas = new() { Background = Brushes.White, Width = 800, Height = 420 };
    private readonly Image _qr = new() { Width = 270, Height = 270, Stretch = Stretch.Fill };
    private readonly DispatcherTimer _motion = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private int _position = 40, _direction = 1;

    public FixtureWindow()
    {
        Title = "QR Guard synthetic fixture · seed P0-2026-10-01";
        Width = 870; Height = 630; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var layout = new StackPanel { Margin = new Thickness(16) };
        layout.Children.Add(new TextBlock { Text = "Synthetic reserved-domain QR codes only. Use another ordinary window to test occlusion.", TextWrapping = TextWrapping.Wrap });
        var controls = new WrapPanel();
        AddButton(controls, "Payload A", () => ShowPayload(SyntheticFixtures.PayloadA));
        AddButton(controls, "Payload B (same location)", () => ShowPayload(SyntheticFixtures.PayloadB));
        AddButton(controls, "Sensitive demo", () => ShowPayload(SyntheticFixtures.Sensitive));
        AddButton(controls, "Unverified demo", () => ShowPayload("https://benefits.corp.example/enroll?token=demo"));
        AddButton(controls, "Unsupported demo", () => ShowPayload("javascript:alert(1)"));
        AddButton(controls, "IDNA demo", () => ShowPayload("https://bücher.example/שלום?token=SYNTHETICONLY#demo"));
        AddButton(controls, "Move / stop", () => { if (_motion.IsEnabled) _motion.Stop(); else _motion.Start(); });
        AddButton(controls, "Remove QR", () => { _motion.Stop(); _qr.Visibility = Visibility.Hidden; });
        layout.Children.Add(controls);
        _canvas.Children.Add(_qr); Canvas.SetLeft(_qr, _position); Canvas.SetTop(_qr, 60);
        RenderOptions.SetBitmapScalingMode(_qr, BitmapScalingMode.NearestNeighbor);
        layout.Children.Add(_canvas);
        layout.Children.Add(new TextBlock { Text = "Focus/input check: keep typing here while masks appear, move, change, and disappear." });
        layout.Children.Add(new TextBox { Height = 30, Text = "Test typing here; do not enter private data." });
        Content = layout;
        _motion.Tick += (_, _) =>
        {
            _position += _direction * 18;
            if (_position >= 470 || _position <= 20) _direction *= -1;
            Canvas.SetLeft(_qr, _position);
        };
        Closed += (_, _) => _motion.Stop();
        ShowPayload(SyntheticFixtures.PayloadA);
    }

    private void ShowPayload(string payload)
    {
        var fixture = SyntheticFixtures.Create(payload);
        _qr.Source = BitmapSource.Create(fixture.Width, fixture.Height, 96, 96, PixelFormats.Gray8,
            null, fixture.Pixels, fixture.Width);
        _qr.Visibility = Visibility.Visible;
    }

    private static void AddButton(Panel panel, string text, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(2), Padding = new Thickness(8) };
        button.Click += (_, _) => action(); panel.Children.Add(button);
    }
}
