using System;
using System.Collections.Generic;

namespace BozjaBuddyReborn.External;

/// <summary>
/// Sends a value when it changes, and re-sends it periodically so the far side cannot drift out
/// of the state we think it is in.
///
/// THE EDGE. The controller runs on the framework tick and re-asserts its intent every pass
/// ("combat on", "combat off"). Firing an IPC gate or a chat command every frame both spams the
/// target plugin and, for rotation backends, prevents them from ever settling. So the last value
/// sent is latched and only a real change is forwarded.
///
/// THE HEARTBEAT, and why the edge alone was not enough. The latch records what we ASKED for, not
/// what the other plugin is actually doing, and both of ours drop the state on their own:
///
///   - BossMod Reborn's AIManager.Update calls SwitchToIdle() the moment the party slot it is
///     following stops being valid. In a Bozja alliance that happens routinely, and it silently
///     turns the AI off - avoidance included.
///   - RSR has no state getter at all, and its own auto-cancel, a death, or the user touching
///     /rotation will take it out of Auto without telling anyone.
///   - A send can also simply not land: an IPC gate that is not bound yet falls back to a chat
///     command, and a chat command issued while the client is mid-zone goes nowhere.
///
/// In every one of those cases the latch says "already on" forever and the run continues with no
/// rotation and no dodging. Re-asserting on an interval is the only way back, because there is
/// nothing to read.
/// </summary>
public sealed class EdgeTrigger<T>(Action<T> send) where T : struct
{
    private readonly Action<T> _send = send;
    private T? _last;
    private long _lastSentMs;

    /// <summary>
    /// How often an unchanged value is re-sent anyway. 0 disables the heartbeat, leaving pure
    /// edge behaviour.
    /// </summary>
    public long ReassertIntervalMs { get; set; }

    /// <summary>Request a value; forwards on a real change, or when the heartbeat is due.</summary>
    /// <param name="allowReassert">
    /// False to suppress the heartbeat for this call only - used to keep a re-assert from landing
    /// at a moment when it would do damage (re-enabling BossMod Reborn's AI tears down and
    /// rebuilds its behaviour, which must not happen mid-dodge).
    /// </param>
    public void Request(T value, bool allowReassert = true)
    {
        var now = Environment.TickCount64;

        if (_last is { } last && EqualityComparer<T>.Default.Equals(last, value))
        {
            if (!allowReassert || ReassertIntervalMs <= 0 || now - _lastSentMs < ReassertIntervalMs)
                return;
        }

        _last = value;
        _lastSentMs = now;
        _send(value);
    }

    /// <summary>Force the next <see cref="Request"/> to send even if the value is unchanged.</summary>
    public void Resync()
    {
        _last = null;
        _lastSentMs = 0;
    }

    /// <summary>The last value actually sent, or null if nothing has been sent yet.</summary>
    public T? Current => _last;

    /// <summary>Seconds since the last actual send, for the UI.</summary>
    public float SecondsSinceSent => _lastSentMs == 0 ? -1f : (Environment.TickCount64 - _lastSentMs) / 1000f;
}
