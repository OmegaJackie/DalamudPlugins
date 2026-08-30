using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using ECommons.DalamudServices;

namespace BozjaBuddyReborn.Multibox;

/// <summary>What kind of objective the host has told everyone to go do.</summary>
public enum ObjectiveKind : byte
{
    None = 0,
    CriticalEngagement = 1,
    Fate = 2,
}

/// <summary>
/// The shared objective every box in the group is working on.
///
/// TERRITORY IS PART OF THE IDENTITY. Without it a host in the Bozjan Southern Front could hand
/// a client in Zadnor a bare coordinate, and since the two zones use overlapping coordinate
/// ranges the client would path to a real-looking point in the wrong zone rather than rejecting
/// the instruction. It carries no cost - the wire message already exists.
/// </summary>
public readonly record struct SharedObjective(ObjectiveKind Kind, uint Id, Vector3 Position, uint Territory = 0)
{
    public bool IsSet => Kind != ObjectiveKind.None;
    public static readonly SharedObjective None = new(ObjectiveKind.None, 0, Vector3.Zero, 0);

    /// <summary>
    /// Is this the same objective, regardless of where it has drifted to?
    ///
    /// THE VALUE COMPARISON IS NOT USABLE FOR THIS, and using it broke the multibox barrier. A
    /// record struct compares every field including Position, and a skirmish FATE's ring moves
    /// as the FATE progresses - so re-selecting the same FATE produced a struct that was "not
    /// equal" five times a second. That reset the arrival barrier and the committed state on
    /// every tick, so the group could never all be counted as arrived at once. Identity is the
    /// kind and the id; the position is a property of the objective, not part of naming it.
    /// </summary>
    public bool SameTarget(SharedObjective other) => Kind == other.Kind && Id == other.Id;
}

/// <summary>What the client half of the link is doing, so the UI can say more than up/down.</summary>
public enum LinkState : byte
{
    /// <summary>Multibox is off, or this box is the host.</summary>
    Idle = 0,

    /// <summary>Looking for a host.</summary>
    Connecting = 1,

    /// <summary>Talking to a host.</summary>
    Connected = 2,
}

/// <summary>
/// Cross-client coordination for running several game clients in tandem, modelled on
/// AutoDuty's multibox utility: one named pipe, one HOST, N clients.
///
/// WHY A PIPE AND NOT SHARED MEMORY OR A FILE: each game client is a separate process with
/// its own Dalamud instance, so ECommons' in-process shared-data helpers cannot see across
/// them. A named pipe is the same transport AutoDuty settled on, it needs no ports or
/// firewall rules, and it gives ordered, framed delivery for free.
///
/// WHAT IT ACTUALLY PREVENTS: without coordination, each box independently picks "the best"
/// Critical Engagement and they scatter - one flies to a CE in the north while another takes
/// a FATE in the south, and neither group has enough bodies. The host picks ONE objective and
/// broadcasts it, so every box converges. The optional arrival barrier then holds the group
/// until everyone is on site before committing, so nobody registers into an engagement alone.
///
/// SCOPE: named pipes are machine-local, which covers the normal multibox setup (several
/// clients on one PC). Running boxes across separate machines would need a network transport;
/// that is not implemented here.
///
/// THREADING: every pipe read/write happens on background threads. The controller only ever
/// touches the concurrent collections and the volatile snapshot fields from the framework
/// thread, so no game state is read off-tick. Writes are serialised per connection because
/// they genuinely come from three different threads (framework tick, accept loop, ImGui
/// render) and StreamWriter is not thread-safe - interleaved WriteLines corrupt the framing.
///
/// IDENTITY IS THE TRANSPORT'S, NOT THE CHARACTER'S. Everything that has to distinguish one
/// box from another keys on the connection id, which is unique by construction. Character
/// names are display only: they are self-reported, arrive after the connection is already in
/// the table, are not unique, and - the defect that broke the arrival barrier for the whole of
/// 1.0.x - are frequently the literal string "unknown", because the plugin starts at the title
/// screen where there is no character to name yet.
/// </summary>
public sealed class MultiboxLink : IDisposable
{
    private const string PipeName = "BozjaBuddyRebornPipe";
    private const int MaxClients = 8;

    /// <summary>
    /// How long a single connect attempt looks for a host.
    ///
    /// This is a TIMEOUT, not a wait: .NET's ConnectAsync polls WaitNamedPipe and returns the
    /// instant a server instance appears, so this window is active discovery and costs nothing
    /// when a host is up.
    /// </summary>
    private const int ConnectTimeoutMs = 1000;

    /// <summary>
    /// Dead time between connect attempts, escalating, reset on every success.
    ///
    /// The single 3000ms constant this replaces was the reason the link felt random. It was used
    /// both as the connect timeout AND as an unconditional sleep after every attempt, so a host
    /// that restarted was invisible for up to 3s - and toggling multibox on the HOST dropped
    /// every client into that sleep at once, meaning impatient re-toggling actively prevented
    /// discovery. Starting at zero makes a host restart land within a frame or two; the tail
    /// keeps a hostless box from spinning.
    /// </summary>
    private static readonly int[] BackoffMs = [0, 250, 500, 1000, 2000];

    // --- wire protocol (newline-delimited, '|' separated) ---
    private const string MsgHello = "HELLO";
    private const string MsgObjective = "OBJECTIVE";
    private const string MsgArrived = "ARRIVED";
    private const string MsgGo = "GO";
    private const string MsgStart = "START";
    private const string MsgStop = "STOP";

    /// <summary>Client -> host: this box's duty-action loadout.</summary>
    private const string MsgDuty = "DUTY";

    /// <summary>Host -> clients: everyone's loadout, so every box can draw the same hotbar.</summary>
    private const string MsgRoster = "ROSTER";

    /// <summary>Host -> clients: a one-shot instruction for one box or all of them.</summary>
    private const string MsgCommand = "CMD";

    /// <summary>
    /// Cancellation for the CURRENT run only. Recreated by every Start and cancelled by every
    /// Stop, so flipping the host/client role actually tears the old loop down instead of
    /// leaving it running alongside the new one.
    /// </summary>
    private CancellationTokenSource _cts = new();

    private readonly ConcurrentDictionary<int, ClientConnection> _clients = new();

    /// <summary>
    /// Which peers have reported arrival, keyed by CONNECTION ID.
    ///
    /// Keyed by id and not by character name on purpose. The name is self-reported over the
    /// wire, and every box that starts at the title screen announces the same literal
    /// "unknown" - so a name-keyed set collapsed N clients into one entry, the barrier could
    /// never reach arrived >= peers, and every objective burned the full barrier timeout.
    /// The same collision also made one client disconnecting erase everyone else's arrival.
    /// </summary>
    private readonly ConcurrentDictionary<int, bool> _arrived = new();

    /// <summary>Host-side: each connected box's latest encoded duty loadout, keyed by connection id.</summary>
    private readonly ConcurrentDictionary<int, string> _peerDuty = new();

    /// <summary>The roster last received (client) or last built (host), for the hotbar window.</summary>
    private volatile List<PeerDuty> _roster = [];

    private int _nextClientId;

    /// <summary>
    /// Identifies the current client session. Only the loop that owns the live generation may
    /// clear the shared connection state, so a previous loop unwinding on a threadpool thread
    /// cannot mark a newly established link as down.
    /// </summary>
    private int _clientGeneration;

    private volatile bool _running;
    private volatile bool _isHost;
    private volatile bool _connected;
    private volatile string _selfName = "unknown";

    private SharedObjective _objective = SharedObjective.None;
    private readonly object _objectiveLock = new();

    /// <summary>Set by the host's GO broadcast; cleared when a DIFFERENT objective arrives.</summary>
    private volatile bool _released;

    // --- diagnostics, so "it is not working" is answerable from the UI ------------
    private volatile int _consecutiveFailures;
    private volatile string? _lastLinkError;
    private long _connectingSinceMs;

    /// <summary>Host-side: remote peers currently connected. Client-side: 1 when linked to a host.</summary>
    public int PeerCount => _isHost ? _clients.Count : (_connected ? 1 : 0);

    public bool IsHost => _isHost;
    public bool Connected => _connected;
    public bool Running => _running;

    /// <summary>Client-side link state, so the UI can distinguish "looking" from "no host".</summary>
    public LinkState State => !_running || _isHost
        ? LinkState.Idle
        : _connected ? LinkState.Connected : LinkState.Connecting;

    /// <summary>Failed connect attempts since the last success. 0 while connected.</summary>
    public int ConsecutiveFailures => _consecutiveFailures;

    /// <summary>Why the last connect attempt failed, for the UI. Null when connected.</summary>
    public string? LastLinkError => _lastLinkError;

    /// <summary>Seconds this box has been looking for a host, or 0 when it is not.</summary>
    public float SecondsSearching => _connectingSinceMs == 0 || _connected
        ? 0f
        : (Environment.TickCount64 - _connectingSinceMs) / 1000f;

    /// <summary>Commands pushed from the host that the controller should act on (START/STOP).</summary>
    public ConcurrentQueue<string> InboundCommands { get; } = new();

    /// <summary>The objective the whole group is working on.</summary>
    public SharedObjective Objective
    {
        get { lock (_objectiveLock) return _objective; }
    }

    /// <summary>True once the host has told the group to commit to the current objective.</summary>
    public bool Released => _released;

    /// <summary>
    /// Forget the shared objective and the release latch without touching the connection.
    ///
    /// Needed because both of them outlived everything that should have ended them - a
    /// controller Stop/Start, and a zone change. A client restarted after a Stop picked the
    /// stale objective straight back up, and because <c>_released</c> was still set it committed
    /// to it with no barrier at all; after a zone change the stale objective was one from the
    /// zone it had just left.
    /// </summary>
    public void ResetObjective()
    {
        lock (_objectiveLock)
            _objective = SharedObjective.None;

        _arrived.Clear();
        _released = false;
    }

    /// <summary>Host-side: how many peers have reported arrival at the current objective.</summary>
    public int ArrivedCount => _arrived.Count;

    public void Start(bool asHost, string selfName)
    {
        Stop();

        _selfName = string.IsNullOrWhiteSpace(selfName) ? "unknown" : selfName;
        _isHost = asHost;
        _running = true;
        _consecutiveFailures = 0;
        _lastLinkError = null;
        _connectingSinceMs = asHost ? 0 : Environment.TickCount64;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        if (asHost)
            _ = Task.Run(() => HostAcceptLoopAsync(token), token);
        else
            _ = Task.Run(() => ClientLoopAsync(Interlocked.Increment(ref _clientGeneration), token), token);
    }

    public void Stop()
    {
        _running = false;
        _connected = false;

        // Invalidate any client loop still unwinding, so its finally cannot touch shared state.
        Interlocked.Increment(ref _clientGeneration);

        // Cancel the previous run's loops before tearing their state down, so a loop cannot
        // resurrect a connection into the collections we are about to clear.
        try { _cts.Cancel(); } catch { /* already cancelled */ }
        try { _cts.Dispose(); } catch { /* already disposed */ }
        _cts = new CancellationTokenSource();

        foreach (var c in _clients.Values)
            c.Dispose();
        _clients.Clear();
        _arrived.Clear();
        _peerDuty.Clear();
        _roster = [];
        _clientWriter = null;
        _connectingSinceMs = 0;

        lock (_objectiveLock)
            _objective = SharedObjective.None;
        _released = false;
    }

    /// <summary>
    /// Refresh the name this box announces itself with, and tell the host about it.
    ///
    /// The character is not loaded when the plugin starts, so the first HELLO goes out as
    /// "unknown". Nothing used to correct that: this method only assigned a field, and the
    /// comment claiming "the client's reconnect loop re-sends HELLO" was wrong - the pipe never
    /// drops on its own, because nothing in the plugin ever sends a keepalive. So the host
    /// displayed, and keyed on, "unknown" forever. Now a real change is pushed down the live
    /// link; the host's HELLO handler simply reassigns the display name, so repeats are free.
    /// </summary>
    public void UpdateSelfName(string selfName)
    {
        if (string.IsNullOrWhiteSpace(selfName) || selfName == _selfName)
            return;

        _selfName = selfName;

        // No-ops when the link is down; the next connect sends the current name anyway.
        if (!_isHost)
            SendToHost($"{MsgHello}|{selfName}");
    }

    // ------------------------------------------------------------------ host

    private async Task HostAcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _running)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    PipeName, PipeDirection.InOut, MaxClients,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                var id = Interlocked.Increment(ref _nextClientId);
                var conn = new ClientConnection(id, server);
                _clients[id] = conn;
                _connected = true;

                _ = Task.Run(() => HostReadLoopAsync(conn, token), token);

                // Bring a late joiner straight up to date rather than making it wait for the
                // next objective change - otherwise a client that connects mid-travel idles.
                // The release flag goes with it: without that, a box that reconnects after the
                // group was released sits at the objective waiting for a GO that, since the
                // host only ever sends GO once per objective, is never coming.
                var current = Objective;
                if (current.IsSet)
                {
                    conn.Send(FormatObjective(current));
                    if (_released)
                        conn.Send(MsgGo);
                }
            }
            catch (OperationCanceledException)
            {
                server?.Dispose();
                return;
            }
            catch (Exception ex)
            {
                server?.Dispose();
                _lastLinkError = ex.Message;
                Svc.Log.Warning($"[BozjaBuddyReborn] Multibox host accept failed: {ex.Message}");
                try { await Task.Delay(1000, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private async Task HostReadLoopAsync(ClientConnection conn, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && _running && conn.IsConnected)
            {
                var line = await conn.Reader.ReadLineAsync(token).ConfigureAwait(false);
                if (line == null)
                    break;

                var parts = line.Split('|');
                switch (parts[0])
                {
                    case MsgHello:
                        // Display only. A repeat HELLO is how a box corrects the "unknown" it
                        // announced from the title screen, so this must be idempotent.
                        var name = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1])
                            ? parts[1]
                            : $"client{conn.Id}";
                        if (name != conn.Name)
                        {
                            conn.Name = name;
                            Svc.Log.Information($"[BozjaBuddyReborn] Multibox client joined: {name}");
                        }
                        break;

                    case MsgArrived:
                        // Keyed by id, so re-sends are free and two boxes cannot collide.
                        _arrived[conn.Id] = true;
                        break;

                    case MsgDuty when parts.Length > 1:
                        // Latest wins; the host re-broadcasts the collected roster on its own
                        // cadence rather than relaying each update, so N boxes cost N sends a
                        // second in each direction instead of N squared.
                        _peerDuty[conn.Id] = parts[1];
                        break;
                }
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (Exception ex)
        {
            Svc.Log.Debug($"[BozjaBuddyReborn] Multibox client {conn.Id} dropped: {ex.Message}");
        }
        finally
        {
            _clients.TryRemove(conn.Id, out _);
            _arrived.TryRemove(conn.Id, out _);
            _peerDuty.TryRemove(conn.Id, out _);
            Svc.Log.Information($"[BozjaBuddyReborn] Multibox client left: {conn.Name}");
            conn.Dispose();
            if (_clients.IsEmpty)
                _connected = false;
        }
    }

    /// <summary>Host: tell every box which objective to work. Resets the arrival barrier.</summary>
    public void BroadcastObjective(SharedObjective objective)
    {
        if (!_isHost)
            return;

        bool sameTarget;
        lock (_objectiveLock)
        {
            if (_objective == objective)
                return;

            // Compared by IDENTITY, not by value - see SharedObjective.SameTarget. A skirmish
            // ring drifts as the FATE progresses, so the same objective arrives here as a
            // different struct several times a second; clearing the barrier on that made
            // "everyone has arrived" unreachable and deadlocked the group until the timeout.
            sameTarget = _objective.SameTarget(objective);
            _objective = objective;
        }

        if (!sameTarget)
        {
            _arrived.Clear();
            _released = false;
        }

        Broadcast(FormatObjective(objective));
    }

    /// <summary>
    /// Host: release the group to commit to the current objective.
    ///
    /// The flag is remembered so a late joiner can be caught up, but a repeat GO is still sent:
    /// it is idempotent on the client, and refusing to re-send was how a reconnected box could
    /// be stranded waiting for a release that had already happened.
    /// </summary>
    public void BroadcastGo()
    {
        if (!_isHost)
            return;
        _released = true;
        Broadcast(MsgGo);
    }

    /// <summary>Host: start or stop every client alongside this one.</summary>
    public void BroadcastRunState(bool start) => Broadcast(start ? MsgStart : MsgStop);

    // ------------------------------------------------------- duty action roster

    /// <summary>
    /// Everyone's duty-action loadout, self included, for the shared hotbar. Empty when the link
    /// is down - a box alone still shows its own row, which the window supplies itself.
    /// </summary>
    public IReadOnlyList<PeerDuty> Roster => _roster;

    /// <summary>
    /// Client: report this box's two duty slots to the host.
    /// </summary>
    public bool ReportDutyActions(string encoded) => SendToHost($"{MsgDuty}|{encoded}");

    /// <summary>
    /// Host: fold this box's own loadout in with everything the clients have reported and push
    /// the combined roster to all of them, so every box draws the same hotbar.
    /// </summary>
    public void BroadcastRoster(string ownEncoded)
    {
        if (!_isHost)
            return;

        var entries = new List<string> { ownEncoded };
        foreach (var (_, encoded) in _peerDuty)
        {
            if (!string.IsNullOrEmpty(encoded))
                entries.Add(encoded);
        }

        // The host builds its own view from the same list it sends, so host and clients cannot
        // disagree about what the roster is.
        _roster = DutyRoster.DecodeAll(entries, 0, _selfName);

        if (!_clients.IsEmpty)
            Broadcast($"{MsgRoster}|{string.Join('|', entries)}");
    }

    /// <summary>
    /// Host: issue a one-shot instruction to one box or all of them.
    ///
    /// Not retained and not replayed to a reconnecting box - see BoxCommand for why an
    /// imperative must not behave like the shared objective does.
    /// </summary>
    public void SendCommand(BoxCommand command)
    {
        if (!_isHost)
            return;

        Broadcast($"{MsgCommand}|{command.Encode()}");

        // The operator's own box is a box too: "all" has to include it, and a targeted command
        // aimed at the host has nowhere else to go.
        if (command.IsForEveryone || command.AppliesTo(_selfName))
            InboundCommandQueue.Enqueue(command);
    }

    /// <summary>Instructions this box has been told to carry out.</summary>
    public ConcurrentQueue<BoxCommand> InboundCommandQueue { get; } = new();

    /// <summary>
    /// Queue an instruction for THIS box only, without touching the wire.
    ///
    /// The panel's "apply here" / "send this box" buttons go through the same queue as a remote
    /// instruction rather than calling the drivers directly, so local and remote take exactly one
    /// code path and cannot drift in behaviour. It also means a client - which may not send
    /// commands to anyone - can still drive itself.
    /// </summary>
    public void SendCommandLocal(BoxCommand command) => InboundCommandQueue.Enqueue(command);

    /// <summary>Drop a stale roster, e.g. when the link goes away.</summary>
    public void ClearRoster()
    {
        _roster = [];
        _peerDuty.Clear();
    }

    private void Broadcast(string message)
    {
        foreach (var c in _clients.Values)
            c.Send(message);
    }

    // ---------------------------------------------------------------- client

    private async Task ClientLoopAsync(int generation, CancellationToken token)
    {
        var attempt = 0;

        while (!token.IsCancellationRequested && _running && generation == Volatile.Read(ref _clientGeneration))
        {
            NamedPipeClientStream? pipe = null;
            try
            {
                pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(ConnectTimeoutMs, token).ConfigureAwait(false);

                // A newer Start() may have superseded us while we were connecting.
                if (generation != Volatile.Read(ref _clientGeneration))
                {
                    pipe.Dispose();
                    return;
                }

                attempt = 0;
                _consecutiveFailures = 0;
                _lastLinkError = null;
                _connectingSinceMs = 0; // this search episode is over; the next one starts its own clock
                _connected = true;

                using var reader = new StreamReader(pipe);
                await using var writer = new StreamWriter(pipe) { AutoFlush = true };

                _clientWriter = writer;
                SendToHost($"{MsgHello}|{_selfName}");

                while (!token.IsCancellationRequested && _running && pipe.IsConnected &&
                       generation == Volatile.Read(ref _clientGeneration))
                {
                    var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                    if (line == null)
                        break;
                    HandleHostMessage(line);
                }
            }
            catch (OperationCanceledException)
            {
                pipe?.Dispose();
                return;
            }
            catch (Exception ex)
            {
                _lastLinkError = ex is TimeoutException ? "no host is listening" : ex.Message;

                // The first failure of a streak is worth seeing; the rest are noise. Previously
                // every one of these was Debug, so a box that could not find a host for an
                // entire session said nothing at all in the log OR the UI.
                if (_consecutiveFailures == 0)
                    Svc.Log.Information($"[BozjaBuddyReborn] Multibox: {_lastLinkError}. Retrying.");
                else
                    Svc.Log.Debug($"[BozjaBuddyReborn] Multibox connect attempt failed: {ex.Message}");

                _consecutiveFailures++;
            }
            finally
            {
                // Only the loop that still owns the link may declare it down. Without this, a
                // superseded loop unwinding on a threadpool thread could clear _connected and
                // _clientWriter that a newer, working connection had just set - the link would
                // read "down" while the pipe was live, and ARRIVED would silently never send.
                if (generation == Volatile.Read(ref _clientGeneration))
                {
                    _connected = false;
                    _clientWriter = null;

                    // Restart the clock for THIS search episode. Leaving it pinned to the moment
                    // multibox was enabled made a four-second reconnect after two hours of happy
                    // farming render as "Searching for 7200s" plus the "no host is listening, tick
                    // one box as host" advice - which is wrong, and would send the user straight
                    // back to toggling the checkbox, the very behaviour this display exists to stop.
                    if (_connectingSinceMs == 0)
                        _connectingSinceMs = Environment.TickCount64;
                }
                pipe?.Dispose();
            }

            var delay = BackoffMs[Math.Min(attempt, BackoffMs.Length - 1)];
            attempt++;
            if (delay > 0)
            {
                try { await Task.Delay(delay, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private volatile StreamWriter? _clientWriter;
    private readonly object _clientWriteLock = new();

    private void HandleHostMessage(string line)
    {
        var parts = line.Split('|');
        switch (parts[0])
        {
            case MsgObjective:
                if (TryParseObjective(parts, out var objective))
                {
                    bool changed;
                    lock (_objectiveLock)
                    {
                        // Identity, not value: a drifting FATE position is the same objective,
                        // and treating it as a new one cleared the release latch on every
                        // message so the client waited for a GO it had already been sent.
                        changed = !_objective.SameTarget(objective);
                        _objective = objective;
                    }

                    // Only a genuinely NEW objective resets the release. The host re-sends the
                    // current objective to catch up a reconnecting box, and clearing the flag on
                    // that byte-identical message stranded the client: it went back to waiting
                    // for a GO for an objective the group had already been released on.
                    if (changed)
                        _released = false;
                }
                break;

            case MsgGo:
                _released = true;
                break;

            case MsgRoster:
                _roster = DutyRoster.DecodeAll(parts, 1, _selfName);
                break;

            case MsgCommand:
                // Filtered here rather than at the sender so the host can broadcast once instead
                // of addressing each connection, and so a box that renames itself mid-session
                // still answers to the name the operator can currently see.
                if (BoxCommand.TryDecode(parts, 1, out var boxCommand) && boxCommand.AppliesTo(_selfName))
                    InboundCommandQueue.Enqueue(boxCommand);
                break;

            case MsgStart:
            case MsgStop:
                InboundCommands.Enqueue(parts[0]);
                break;
        }
    }

    /// <summary>
    /// Client: report that this box has reached the shared objective.
    /// </summary>
    /// <returns>False when the link was down, so the caller knows it did not land.</returns>
    public bool ReportArrived() => SendToHost(MsgArrived);

    private bool SendToHost(string message)
    {
        var writer = _clientWriter;
        if (writer == null)
            return false;

        // Serialised: this runs on the framework thread while the read loop runs on a
        // threadpool thread, and UpdateSelfName can now write from the framework thread too.
        lock (_clientWriteLock)
        {
            try
            {
                writer.WriteLine(message);
                return true;
            }
            catch
            {
                return false; // link dropped; the client loop will reconnect
            }
        }
    }

    // ----------------------------------------------------------- serialisation

    private static string FormatObjective(SharedObjective o) =>
        string.Join('|',
            MsgObjective,
            ((byte)o.Kind).ToString(CultureInfo.InvariantCulture),
            o.Id.ToString(CultureInfo.InvariantCulture),
            o.Position.X.ToString("R", CultureInfo.InvariantCulture),
            o.Position.Y.ToString("R", CultureInfo.InvariantCulture),
            o.Position.Z.ToString("R", CultureInfo.InvariantCulture),
            o.Territory.ToString(CultureInfo.InvariantCulture));

    private static bool TryParseObjective(IReadOnlyList<string> parts, out SharedObjective objective)
    {
        objective = SharedObjective.None;
        if (parts.Count < 6)
            return false;

        if (!byte.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var kind) ||
            !uint.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ||
            !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
            !float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
            !float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
            return false;

        // Optional seventh field, so a box running an older build still links rather than
        // dropping every objective message. Territory 0 means "not stated", which the consumer
        // treats as "do not reject on this grounds".
        uint territory = 0;
        if (parts.Count >= 7)
            uint.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out territory);

        objective = new SharedObjective((ObjectiveKind)kind, id, new Vector3(x, y, z), territory);
        return true;
    }

    public void Dispose()
    {
        // Stop() cancels and replaces the run CTS; dispose the replacement so nothing leaks.
        Stop();
        try { _cts.Dispose(); } catch { /* already disposed */ }
    }

    private sealed class ClientConnection(int id, NamedPipeServerStream stream) : IDisposable
    {
        public int Id { get; } = id;

        /// <summary>Display only - never an identity. See the class remarks on keying.</summary>
        public string Name { get; set; } = $"client{id}";

        private readonly NamedPipeServerStream _stream = stream;
        private readonly object _writeLock = new();
        public StreamReader Reader { get; } = new(stream);
        private readonly StreamWriter _writer = new(stream) { AutoFlush = true };

        public bool IsConnected
        {
            get { try { return _stream.IsConnected; } catch { return false; } }
        }

        /// <summary>
        /// Send a line to this peer. Locked because the host writes from three different
        /// threads - the controller tick (objective/GO), the accept loop (late-joiner catch-up)
        /// and the ImGui render thread (Start all / Stop all) - and StreamWriter is not thread
        /// safe, so interleaved writes corrupt the line framing for everyone.
        /// </summary>
        public void Send(string message)
        {
            lock (_writeLock)
            {
                try { _writer.WriteLine(message); }
                catch { /* peer went away; the read loop will clean up */ }
            }
        }

        public void Dispose()
        {
            try { _stream.Dispose(); } catch { /* already disposed */ }
        }
    }
}
