namespace QrGuard.Core;

public interface ILinkLauncher { void Open(ValidatedHttpTarget target); }
public interface IClipboardAdapter { void Copy(ValidatedHttpTarget target); }
public enum ActionOutcome { Completed, WarningRequired, Refused, ChangedOrStale, AdapterFailed }
public sealed record DetailsView(ActionTicket Ticket, string Label, string AsciiHost, string Destination, bool CanOpen, bool CanCopy);

public sealed class ActionController(TrackStore tracks, IPolicyProvider policies, IPolicyEvaluator evaluator,
    IClock clock, ILinkLauncher launcher, IClipboardAdapter clipboard, IEventSink events)
{
    // One pending warning, bound to the exact UI ticket. A boolean alone cannot authorize an unseen warning.
    private ActionTicket? _pendingWarning;
    public DetailsView? Details(long trackId) => policies.UseCurrent(policy =>
        tracks.ReadCurrent<DetailsView?>(trackId, null, clock, (identity, payload, bytes) =>
        {
            PolicyDecision decision = evaluator.Evaluate(payload, bytes, policy, clock.UtcNow);
            return new(new(identity, policy.Token), decision.Label, payload.Target?.AsciiHost ?? "No launchable destination",
                payload.Target?.RedactedDisplay() ?? "Payload hidden", decision.OpenEnabled(policy), decision.CopyEnabled);
        }, () => null));

    public bool IsCurrent(ActionTicket ticket) => policies.UseCurrent(policy => policy.Token == ticket.PolicyToken
        && tracks.ReadCurrent(ticket.Track.TrackId, ticket.Track, clock, (_, _, _) => true, () => false));

    // Invoked only by deliberate UI actions. The warning is a separate user interaction, followed by another recheck.
    public ActionOutcome Open(ActionTicket ticket, bool warningConfirmed = false) => Execute(ticket, true, warningConfirmed);
    public ActionOutcome Copy(ActionTicket ticket) => Execute(ticket, false, false);
    public void CancelWarning(ActionTicket ticket) => policies.UseCurrent(policy =>
    { if (_pendingWarning == ticket) _pendingWarning = null; return 0; });

    private ActionOutcome Execute(ActionTicket ticket, bool open, bool confirmed)
    {
        ActionOutcome result = policies.UseCurrent(policy =>
        {
            if (policy.Token != ticket.PolicyToken) return ActionOutcome.ChangedOrStale;
            return tracks.ReadCurrent(ticket.Track.TrackId, ticket.Track, clock, (_, payload, bytes) =>
            {
                PolicyDecision decision = evaluator.Evaluate(payload, bytes, policy, clock.UtcNow);
                if (payload.Target is null || (open ? !decision.OpenEnabled(policy) : !decision.CopyEnabled)) return ActionOutcome.Refused;
                if (open && confirmed && _pendingWarning != ticket) return ActionOutcome.Refused;
                if (open && decision.State == DecisionState.Unverified && !confirmed)
                { _pendingWarning = ticket; return ActionOutcome.WarningRequired; }
                if (open) _pendingWarning = null;
                try
                {
                    if (open) launcher.Open(payload.Target); else clipboard.Copy(payload.Target);
                    return ActionOutcome.Completed;
                }
                catch (Exception error) when (error is not OutOfMemoryException) { return ActionOutcome.AdapterFailed; }
            }, () => ActionOutcome.ChangedOrStale);
        });
        events.Record(new(result switch { ActionOutcome.Completed => open ? EventCode.Opened : EventCode.Copied,
            ActionOutcome.WarningRequired => EventCode.WarningRequired, _ => EventCode.ActionRefused }, clock.MonotonicTicks));
        return result;
    }
}
