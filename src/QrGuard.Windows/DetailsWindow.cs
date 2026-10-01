using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using QrGuard.Core;

namespace QrGuard.Windows;

internal sealed class DetailsWindow : Window
{
    private readonly LabController _controller;
    private readonly long _trackId;
    private DetailsView? _view;
    private readonly TextBlock _host = new() { FontSize = 24, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, FlowDirection = FlowDirection.LeftToRight };
    private readonly TextBlock _state = new() { FontSize = 18, Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBox _destination = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FlowDirection = FlowDirection.LeftToRight };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly Button _open = new() { Content = "Open in browser", Padding = new Thickness(8) };
    private readonly Button _copy = new() { Content = "Copy original link", Padding = new Thickness(8), Margin = new Thickness(8, 0, 0, 0) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    public DetailsWindow(LabController controller, long trackId)
    {
        _controller = controller; _trackId = trackId;
        Title = "QR Guard · Destination details"; Width = 600; Height = 430; MinWidth = 380; MinHeight = 350;
        var layout = new StackPanel { Margin = new Thickness(20) };
        layout.Children.Add(new TextBlock { Text = "ASCII destination host" }); layout.Children.Add(_host); layout.Children.Add(_state);
        AutomationProperties.SetName(_destination, "Destination with query and fragment values redacted");
        layout.Children.Add(_destination);
        layout.Children.Add(new TextBlock { Text = "Company approval is a policy decision, not a guarantee of safety. Unverified means not checked. Query and fragment values are hidden here. Open and Copy use the original validated link.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
        var buttons = new WrapPanel(); buttons.Children.Add(_open); buttons.Children.Add(_copy);
        var refresh = new Button { Content = "Refresh details", Padding = new Thickness(8), Margin = new Thickness(8, 0, 0, 0) };
        buttons.Children.Add(refresh); layout.Children.Add(buttons); layout.Children.Add(_message);
        Content = new ScrollViewer { Content = layout, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        refresh.Click += (_, _) => Refresh();
        _open.Click += (_, _) =>
        {
            DetailsView? view = _view; if (view is null) return;
            ActionOutcome outcome = _controller.Open(view.Ticket);
            if (outcome == ActionOutcome.WarningRequired)
            {
                MessageBoxResult answer = MessageBox.Show(this, $"This destination is Unverified and has not been checked.\n\nASCII host: {view.AsciiHost}\n\nOpen the original link in your browser?", "Unverified destination", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (answer != MessageBoxResult.Yes) { _controller.CancelWarning(view.Ticket); return; }
                outcome = _controller.Open(view.Ticket, warningConfirmed: true);
            }
            Report(outcome);
        };
        _copy.Click += (_, _) => { if (_view is not null) Report(_controller.Copy(_view.Ticket)); };
        _timer.Tick += (_, _) =>
        {
            if (_view is not null && !_controller.IsCurrent(_view.Ticket)) Invalidate();
            else if (_view is not null)
            {
                // Rule expiry can change enabled actions without changing the policy's snapshot identity.
                DetailsView? current = _controller.Details(_trackId);
                if (current is not null) { _state.Text = current.Label; _open.IsEnabled = current.CanOpen; _copy.IsEnabled = current.CanCopy; }
            }
        };
        Closed += (_, _) => { _timer.Stop(); _view = null; _host.Text = ""; _destination.Text = ""; };
        Refresh(); _timer.Start();
    }
    private void Refresh()
    {
        _view = _controller.Details(_trackId);
        if (_view is null) { Invalidate(); return; }
        _host.Text = _view.AsciiHost; _state.Text = _view.Label; _destination.Text = _view.Destination;
        _open.IsEnabled = _view.CanOpen; _copy.IsEnabled = _view.CanCopy; _message.Text = "";
    }
    private void Invalidate()
    {
        _view = null; _host.Text = "Destination unavailable"; _state.Text = "Details out of date"; _destination.Text = "";
        _open.IsEnabled = _copy.IsEnabled = false;
        _message.Text = "The QR, display, observation or policy changed, or capture stopped. Refresh details before acting.";
    }
    private void Report(ActionOutcome outcome)
    {
        if (outcome == ActionOutcome.ChangedOrStale) { Invalidate(); return; }
        _message.Text = outcome switch { ActionOutcome.Completed => "Action completed.", ActionOutcome.AdapterFailed => "The browser or clipboard action failed.", _ => "Action refused by the current policy or payload state." };
    }
}
