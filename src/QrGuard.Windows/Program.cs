using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;

namespace QrGuard.Windows;

internal static class Program
{
    [STAThread]
    public static void Main()
    {
        var application = new Application();
        application.Run(new LabWindow());
    }
}

internal sealed class LabWindow : Window
{
    private readonly LabController _controller = new();
    private readonly ComboBox _monitors = new() { MinWidth = 360, Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly Button _start = new() { Content = "Start selected monitor", Padding = new Thickness(8) };
    private readonly Button _stop = new() { Content = "Stop / remove masks", Padding = new Thickness(8), Margin = new Thickness(8, 0, 0, 0) };
    private FixtureWindow? _fixture;

    public LabWindow()
    {
        Title = "QR Guard · P0 feasibility lab"; Width = 600; Height = 350;
        var layout = new StackPanel { Margin = new Thickness(20) };
        layout.Children.Add(new TextBlock { Text = "P0 lab: persistent physical-display masking candidate", FontSize = 20 });
        layout.Children.Add(new TextBlock { Text = "Windows 11 x64, one landscape monitor up to 1920×1080. Test at 100% DPI first. Screenshots and remote viewers may see the original QR. Physical masking is unverified until lab acceptance.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        _monitors.ItemsSource = MonitorTarget.Enumerate(); _monitors.SelectedIndex = 0; layout.Children.Add(_monitors);
        var buttons = new WrapPanel(); buttons.Children.Add(_start); buttons.Children.Add(_stop); layout.Children.Add(buttons);
        var tools = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var fixtureButton = new Button { Content = "Open synthetic fixture", Padding = new Thickness(8) };
        var exportButton = new Button { Content = "Export measurements", Padding = new Thickness(8), Margin = new Thickness(8, 0, 0, 0) };
        tools.Children.Add(fixtureButton); tools.Children.Add(exportButton); layout.Children.Add(tools); layout.Children.Add(_status);
        Content = layout;
        _start.Click += (_, _) =>
        {
            if (_monitors.SelectedItem is not MonitorTarget monitor) return;
            _controller.Start(monitor); _start.IsEnabled = false; _monitors.IsEnabled = false;
        };
        _stop.Click += async (_, _) =>
        {
            _stop.IsEnabled = false; await _controller.StopAsync(); _stop.IsEnabled = true;
            _start.IsEnabled = _controller.CanStart; _monitors.IsEnabled = _controller.CanStart;
        };
        fixtureButton.Click += (_, _) =>
        {
            if (_fixture is null) { _fixture = new FixtureWindow(); _fixture.Closed += (_, _) => _fixture = null; _fixture.Show(); }
            else _fixture.Activate(); // Explicit user action only; mask creation never activates.
        };
        exportButton.Click += (_, _) =>
        {
            var dialog = new SaveFileDialog { Filter = "Redacted measurements (*.json)|*.json", FileName = "p0-lab-measurements.json" };
            if (dialog.ShowDialog(this) == true)
                System.IO.File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(_controller.MeasurementReport(), new JsonSerializerOptions { WriteIndented = true }));
        };
        _timer.Tick += (_, _) => { _controller.Tick(); _status.Text = _controller.Status; };
        Closed += (_, _) => { _timer.Stop(); _controller.Dispose(); _fixture?.Close(); };
        _timer.Start(); _status.Text = _controller.Status;
    }
}
