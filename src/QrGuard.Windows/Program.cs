using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using QrGuard.Core;

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
    private DetailsWindow? _details;
    private readonly ComboBox _detected = new() { MinWidth = 300, Margin = new Thickness(0, 8, 8, 0) };
    private QrChoice[] _choices = [];
    private sealed record QrChoice(long TrackId, string Label) { public override string ToString() => $"QR {TrackId} · {Label}"; }

    public LabWindow()
    {
        Title = "QR Guard · P1 offline MVP"; Width = 650; Height = 500;
        var layout = new StackPanel { Margin = new Thickness(20) };
        layout.Children.Add(new TextBlock { Text = "Offline QR masks and local destination policy", FontSize = 20 });
        layout.Children.Add(new TextBlock { Text = "Windows 11 x64, one landscape monitor up to 1920×1080. Test at 100% DPI first. Screenshots and remote viewers may see the original QR. Physical masking is unverified until lab acceptance.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        _monitors.ItemsSource = MonitorTarget.Enumerate(); _monitors.SelectedIndex = 0; layout.Children.Add(_monitors);
        var buttons = new WrapPanel(); buttons.Children.Add(_start); buttons.Children.Add(_stop); layout.Children.Add(buttons);
        var tools = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var fixtureButton = new Button { Content = "Open synthetic fixture", Padding = new Thickness(8) };
        var exportButton = new Button { Content = "Export measurements", Padding = new Thickness(8), Margin = new Thickness(8, 0, 0, 0) };
        var policyButton = new Button { Content = "Load local policy", Padding = new Thickness(8), Margin = new Thickness(8, 0, 0, 0) };
        tools.Children.Add(fixtureButton); tools.Children.Add(exportButton); tools.Children.Add(policyButton); layout.Children.Add(tools);
        layout.Children.Add(new TextBlock { Text = "Click a mask to view details. Open and Copy require explicit actions. Local policy edits are checked every second during capture. Stop before display/session changes.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        var detailsRow = new WrapPanel(); detailsRow.Children.Add(_detected);
        System.Windows.Automation.AutomationProperties.SetName(_detected, "Detected QR codes");
        var detailsButton = new Button { Content = "View selected QR details", Padding = new Thickness(8), Margin = new Thickness(0, 8, 0, 0) };
        detailsButton.Click += (_, _) => { if (_detected.SelectedItem is QrChoice choice) OpenDetails(choice.TrackId); };
        detailsRow.Children.Add(detailsButton); layout.Children.Add(detailsRow);
        layout.Children.Add(_status);
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
        policyButton.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "Local JSON policy (*.json)|*.json", CheckFileExists = true };
            if (dialog.ShowDialog(this) == true) _controller.LoadPolicy(dialog.FileName);
        };
        _controller.DetailsRequested += OpenDetails;
        exportButton.Click += (_, _) =>
        {
            var dialog = new SaveFileDialog { Filter = "Redacted measurements (*.json)|*.json", FileName = "p1-lab-measurements.json" };
            if (dialog.ShowDialog(this) == true)
                System.IO.File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(_controller.MeasurementReport(), new JsonSerializerOptions { WriteIndented = true }));
        };
        _timer.Tick += (_, _) =>
        {
            _controller.Tick(); _status.Text = _controller.Status + "\n" + _controller.PolicyStatus;
            QrChoice[] choices = _controller.Masks.Select(t => new QrChoice(t.TrackId, new PolicyDecision(t.State).Label)).ToArray();
            if (!_choices.SequenceEqual(choices))
            {
                long? selected = (_detected.SelectedItem as QrChoice)?.TrackId;
                _choices = choices; _detected.ItemsSource = choices;
                _detected.SelectedItem = choices.FirstOrDefault(c => c.TrackId == selected) ?? choices.FirstOrDefault();
            }
        };
        Closed += (_, _) => { _timer.Stop(); _details?.Close(); _controller.Dispose(); _fixture?.Close(); };
        _timer.Start(); _status.Text = _controller.Status;
    }
    private void OpenDetails(long id)
    {
        if (_details is not null) { _details.Close(); _details = null; }
        if (_controller.Details(id) is null) return;
        _details = new DetailsWindow(_controller, id);
        _details.Closed += (_, _) => _details = null;
        _details.Show(); // Only an explicit mask click or keyboard-accessible button can open this window.
    }
}
