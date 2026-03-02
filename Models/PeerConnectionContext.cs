using System.Collections.Frozen;

namespace VIIDII.Models;

/// <summary>
/// Represents the 6 states of a WebRTC peer connection lifecycle.
/// Transitions are strictly validated — no state may be skipped.
/// </summary>
public enum PeerState
{
    Idle,
    Signaling,
    Connecting,
    Connected,
    Degraded,
    Disconnected
}

/// <summary>
/// Events that trigger state transitions in the WebRTC DFA.
/// </summary>
public enum PeerTrigger
{
    SessionStarted,
    ReceivePeerId,
    StreamReceived,
    NetworkDrop,
    IceRestart,
    Timeout,
    ManualRejoin
}

/// <summary>
/// Tracks the DFA state of a single WebRTC peer connection.
/// Thread-safe for use in SignalR hub shared state.
/// </summary>
public class PeerConnectionContext
{
    private static readonly FrozenDictionary<(PeerState From, PeerTrigger Trigger), PeerState> Transitions =
        new Dictionary<(PeerState, PeerTrigger), PeerState>
        {
            { (PeerState.Idle, PeerTrigger.SessionStarted), PeerState.Signaling },
            { (PeerState.Signaling, PeerTrigger.ReceivePeerId), PeerState.Connecting },
            { (PeerState.Connecting, PeerTrigger.StreamReceived), PeerState.Connected },
            { (PeerState.Connected, PeerTrigger.NetworkDrop), PeerState.Degraded },
            { (PeerState.Degraded, PeerTrigger.IceRestart), PeerState.Connecting },
            { (PeerState.Degraded, PeerTrigger.Timeout), PeerState.Disconnected },
            { (PeerState.Disconnected, PeerTrigger.ManualRejoin), PeerState.Signaling },
        }.ToFrozenDictionary();

    private readonly object _lock = new();

    public string PeerId { get; }
    public PeerState CurrentState { get; private set; } = PeerState.Idle;
    public DateTime LastTransitionUtc { get; private set; } = DateTime.UtcNow;

    public PeerConnectionContext(string peerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(peerId);
        PeerId = peerId;
    }

    /// <summary>
    /// Attempts a state transition. Returns true if the transition was valid and applied.
    /// Invalid transitions are silently rejected (caller should log).
    /// </summary>
    public bool TryTransition(PeerTrigger trigger, out PeerState newState)
    {
        lock (_lock)
        {
            if (Transitions.TryGetValue((CurrentState, trigger), out var target))
            {
                var previous = CurrentState;
                CurrentState = target;
                LastTransitionUtc = DateTime.UtcNow;
                newState = target;
                return true;
            }

            newState = CurrentState;
            return false;
        }
    }

    /// <summary>
    /// Checks whether a trigger is valid from the current state without applying it.
    /// </summary>
    public bool CanTransition(PeerTrigger trigger)
    {
        lock (_lock)
        {
            return Transitions.ContainsKey((CurrentState, trigger));
        }
    }

    /// <summary>
    /// Returns all valid triggers from the current state.
    /// </summary>
    public static IReadOnlyList<PeerTrigger> GetValidTriggers(PeerState state)
    {
        return Transitions.Keys
            .Where(k => k.From == state)
            .Select(k => k.Trigger)
            .ToList();
    }
}
