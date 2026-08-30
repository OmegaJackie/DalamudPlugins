using Newtonsoft.Json;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace AutoFATE.Core;

public enum MultiboxRole {
    Off = 0,
    Host = 1,
    Client = 2,
}

public sealed class MultiboxMessage {
    public string Type { get; set; } = ""; // host->client: "state" | "settings"; client->host: "status"
    public string? Sender { get; set; }

    // state / status
    public bool Running { get; set; }
    public bool Stopping { get; set; }
    public bool Following { get; set; } // client->host: mirroring a fresh host feed
    public uint TerritoryId { get; set; }
    public uint? TargetFateId { get; set; }

    /// <summary>host->client: aetheryte the host is teleporting to right now, 0 when it isn't.
    /// Lets a client tell the host's party teleport offer apart from another client's — see
    /// <see cref="TeleportOfferGuard"/>. Absent from an older host's messages, which defaults it to 0.</summary>
    public uint TeleportAetheryteId { get; set; }

    // settings (nested payload kept pre-serialized so state heartbeats stay tiny)
    public string? SettingsJson { get; set; }
}

/// <summary>Snapshot of one connected client, for the host's UI.</summary>
public readonly record struct MultiboxClientInfo(string Name, bool Running, bool Following, uint TerritoryId, uint? TargetFateId, bool Responding);

/// <summary>Host settings mirrored onto clients. Deliberately excludes per-character multibox roles.</summary>
public sealed class MultiboxSettingsPayload {
    public int MaxDuration;
    public int MinTimeRemaining;
    public int MaxProgress;
    public bool SwapZones;
    public string DisplayNameFormat = "[{Level}] {Name}";
    public Vector4 BarColour;
    public Dictionary<FateType, HashSet<uint>>? Blacklist;
    public List<FateSortOrder>? SortOrder;
    public string? SelectedModeId;
    public HashSet<uint>? SelectedSwapZones;
    public YokaiPhase YokaiPhase;
    // Travels with the phase: whether clients wear the watch is only meaningful against the phase the
    // host picked. An older host omits it and Newtonsoft leaves the field false — the new default.
    public bool YokaiWearWatchInWeaponsPhase;

    public static MultiboxSettingsPayload From(Configuration c) => new() {
        MaxDuration = c.MaxDuration,
        MinTimeRemaining = c.MinTimeRemaining,
        MaxProgress = c.MaxProgress,
        SwapZones = c.SwapZones,
        DisplayNameFormat = c.DisplayNameFormat,
        BarColour = c.BarColour,
        Blacklist = c.Blacklist.ToDictionary(kv => kv.Key, kv => new HashSet<uint>(kv.Value)),
        SortOrder = [.. c.SortOrder.Select(s => new FateSortOrder { Criteria = s.Criteria, Descending = s.Descending })],
        SelectedModeId = c.SelectedModeId,
        SelectedSwapZones = [.. c.SelectedSwapZones],
        YokaiPhase = c.YokaiPhase,
        YokaiWearWatchInWeaponsPhase = c.YokaiWearWatchInWeaponsPhase,
    };

    public void Apply(FateModule module) {
        var c = module.Config;
        c.MaxDuration = MaxDuration;
        c.MinTimeRemaining = MinTimeRemaining;
        c.MaxProgress = MaxProgress;
        c.SwapZones = SwapZones;
        c.DisplayNameFormat = DisplayNameFormat;
        c.BarColour = BarColour;
        c.Blacklist = Blacklist ?? [];
        c.SortOrder = SortOrder is { Count: > 0 } ? SortOrder : Configuration.DefaultSortOrder();
        c.SelectedSwapZones = SelectedSwapZones ?? [];
        c.YokaiPhase = YokaiPhase;
        c.YokaiWearWatchInWeaponsPhase = YokaiWearWatchInWeaponsPhase;
        module.SelectedModeId = SelectedModeId ?? "None"; // via the module so zone item targets refresh
    }
}

/// <summary>
/// Cross-process sync between game clients on the same machine via a named pipe.
/// The Host broadcasts its running state, zone, current target fate, and (on change) its
/// settings; Clients mirror start/stop, follow the host's fate, and optionally adopt settings.
/// </summary>
public sealed class MultiboxSync : IDisposable {
    private const string PipeName = "AutoFATE.MultiboxSync.v1";
    private const int HeartbeatMs = 500;
    private const int StaleMs = 5000;
    private const int GhostMs = 30000; // silent this long = connection is dead, drop it from the list
    private const int SettingsCheckTicks = 60; // ~1s at 60fps

    private readonly FateModule _module;
    private CancellationTokenSource? _cts;

    public MultiboxRole Role { get; private set; } = MultiboxRole.Off;
    public string StatusText { get; private set; } = "Off";
    public bool SyncSettingsEnabled { get; private set; } = true;

    // ---- host side ----
    private sealed class ClientConn {
        public string Name = "";
        public long SentSettingsVersion = -1;
        public bool Running;
        public bool Following;
        public uint TerritoryId;
        public uint? TargetFateId;
        public long LastStatusAtMs;         // 0 = nothing received yet
        public readonly long ConnectedAtMs = Environment.TickCount64;

        /// <summary>Cuts the heartbeat short when something can't wait for it. Never disposed:
        /// AvailableWaitHandle is never touched, so there is nothing to release.</summary>
        public readonly SemaphoreSlim Wake = new(0, 1);

        public void WakeNow() {
            try {
                Wake.Release();
            }
            catch (SemaphoreFullException) { /* already pending */ }
        }
    }

    private readonly List<ClientConn> _clients = [];
    private volatile MultiboxMessage? _stateSnapshot;    // composed on framework thread
    private volatile string? _settingsMessageJson;       // full "settings" message, pre-serialized
    private string? _lastSettingsPayloadJson;            // framework thread only
    private long _settingsVersion;
    private int _tick;

    public List<MultiboxClientInfo> ConnectedClients {
        get {
            var now = Environment.TickCount64;
            lock (_clients) {
                // a client that stopped talking but whose pipe write hasn't failed yet is a ghost
                _clients.RemoveAll(c => c.LastStatusAtMs != 0 && now - c.LastStatusAtMs > GhostMs);
                return [.. _clients.Select(c => new MultiboxClientInfo(
                    string.IsNullOrEmpty(c.Name) ? "connecting…" : c.Name,
                    c.Running,
                    c.Following,
                    c.TerritoryId,
                    c.TargetFateId,
                    // give a fresh connection a grace period before calling it unresponsive
                    c.LastStatusAtMs != 0 ? now - c.LastStatusAtMs < StaleMs : now - c.ConnectedAtMs < StaleMs))];
            }
        }
    }

    // ---- client side ----
    private volatile MultiboxMessage? _hostState;
    private volatile string? _clientStatusJson;          // composed on framework thread
    private long _hostStateAtMs;
    private volatile string? _hostSettingsJson;
    private long _receivedSettingsVersion;
    private long _appliedSettingsVersion;                // framework thread only
    private bool _hasAppliedRunning;                     // framework thread only
    private bool _lastAppliedRunning;                    // framework thread only
    public bool Connected { get; private set; }

    private ulong _activeCid;
    private string _characterName = "";

    public MultiboxSync(FateModule module) {
        _module = module;
        Svc.Framework.Update += OnFrameworkUpdate;
        TaskBase.Teleporting += OnLocalTeleport;
    }

    public void Dispose() {
        TaskBase.Teleporting -= OnLocalTeleport;
        Svc.Framework.Update -= OnFrameworkUpdate;
        StopTransport();
        Role = MultiboxRole.Off;
    }

    // ---- teleport offer arbitration ----
    /// <summary>How long a teleport stays announced. Covers the cast plus the round trip to the
    /// other clients at <see cref="HeartbeatMs"/>, and expires well before the next fate.</summary>
    private const long TeleportAnnounceMs = 20_000;

    private long _teleportAnnouncedAtMs;
    private uint _teleportAetheryteId;

    /// <summary>Fired on whichever character is teleporting; only a host publishes it.</summary>
    private void OnLocalTeleport(uint aetheryteId) {
        _teleportAetheryteId = aetheryteId;
        Interlocked.Exchange(ref _teleportAnnouncedAtMs, Environment.TickCount64);
        if (Role != MultiboxRole.Host)
            return;

        // This one message races the game: the offer can be on the clients' screens before the next
        // heartbeat would have carried it, and an unannounced offer gets refused. Patch the snapshot
        // the writers are already holding and wake them instead of waiting out the interval.
        if (_stateSnapshot is { } snapshot)
            snapshot.TeleportAetheryteId = aetheryteId;
        lock (_clients) {
            foreach (var client in _clients)
                client.WakeNow();
        }
    }

    private uint AnnouncedTeleportAetheryteId
        => Environment.TickCount64 - Interlocked.Read(ref _teleportAnnouncedAtMs) < TeleportAnnounceMs ? _teleportAetheryteId : 0;

    /// <summary>Aetheryte the host is teleporting to, when this client has a fresh feed saying so.
    /// Null means any teleport offer on screen belongs to someone else.</summary>
    public uint? HostTeleportAetheryteId
        => IsFollowingHost && _hostState?.TeleportAetheryteId is > 0 and var id ? id : null;

    /// <summary>True when this instance is a client with a live, fresh host feed.</summary>
    public bool IsFollowingHost => Role == MultiboxRole.Client && Connected && IsFresh;
    private bool IsFresh => _hostState != null && Environment.TickCount64 - Interlocked.Read(ref _hostStateAtMs) < StaleMs;
    public uint? HostTerritoryId => IsFollowingHost ? _hostState?.TerritoryId : null;
    public uint? RemoteTargetFateId => IsFollowingHost ? _hostState?.TargetFateId : null;
    public string? HostName => IsFresh ? _hostState?.Sender : null;

    public void SetRole(MultiboxRole role, bool persist = true) {
        // Read the live content id, not _activeCid: a command issued the same frame as login
        // would otherwise silently skip persistence.
        var cid = Svc.PlayerState.ContentId;
        if (persist && cid != 0 && _module.Config.GetMultibox(cid).Role != role) {
            _module.Config.GetMultibox(cid).Role = role;
            _module.Config.Save();
        }
        if (role == Role)
            return;

        StopTransport();
        Role = role;
        if (role != MultiboxRole.Off)
            StartTransport();
    }

    /// <summary>Prints the whole sync picture for this character. Role, transport, and feed freshness
    /// are three separate things that all present as "the client just sits there".</summary>
    public void Report() {
        var cid = Svc.PlayerState.ContentId;
        var saved = cid != 0 ? _module.Config.GetMultibox(cid).Role.ToString() : "not logged in";
        Svc.Chat.Print($"[AutoFATE] Multibox — role: {Role} (saved for this character: {saved}), transport: {StatusText}, "
            + $"running: {_module.Running}, mode: {_module.SelectedModeId}");

        if (Role == MultiboxRole.Host) {
            var clients = ConnectedClients;
            Svc.Chat.Print($"[AutoFATE] Host: {clients.Count} connection(s).");
            foreach (var c in clients)
                Svc.Chat.Print($"[AutoFATE]   {c.Name} — responding: {c.Responding}, running: {c.Running}, following: {c.Following}, zone: {c.TerritoryId}, fate: {c.TargetFateId?.ToString() ?? "none"}");
            if (clients.Count == 0)
                Svc.Chat.Print("[AutoFATE]   No clients connected. Each alt needs '/af role client'.");
        }
        else if (Role == MultiboxRole.Client) {
            var ageMs = _hostState is null ? -1 : Environment.TickCount64 - Interlocked.Read(ref _hostStateAtMs);
            Svc.Chat.Print($"[AutoFATE] Client: connected: {Connected}, host: {_hostState?.Sender ?? "none"}, "
                + $"last update: {(ageMs < 0 ? "never" : $"{ageMs}ms ago")}, fresh: {IsFresh}, following: {IsFollowingHost}, "
                + $"hostRunning: {_hostState?.Running.ToString() ?? "?"}, hostZone: {_hostState?.TerritoryId.ToString() ?? "?"}, "
                + $"hostFate: {_hostState?.TargetFateId?.ToString() ?? "none"}");
            if (!Connected)
                Svc.Chat.Print("[AutoFATE]   No pipe to a host — the host character needs '/af role host'.");
        }
        else {
            Svc.Chat.Print("[AutoFATE]   Role is Off on this character. Set it with '/af role client'.");
        }
    }

    public void SetSyncSettings(bool enabled) {
        SyncSettingsEnabled = enabled;
        var cid = Svc.PlayerState.ContentId;
        if (cid != 0) {
            _module.Config.GetMultibox(cid).SyncSettingsFromHost = enabled;
            _module.Config.Save();
        }
    }

    private void OnFrameworkUpdate(IFramework framework) {
        var cid = Svc.PlayerState.ContentId;
        if (cid != _activeCid) {
            // login/logout/character switch: adopt that character's saved role
            _activeCid = cid;
            var mb = cid != 0 ? _module.Config.GetMultibox(cid) : null;
            SyncSettingsEnabled = mb?.SyncSettingsFromHost ?? true;
            SetRole(mb?.Role ?? MultiboxRole.Off, persist: false);
        }

        if (Role == MultiboxRole.Off)
            return;

        if (Svc.PlayerState.CharacterName is { Length: > 0 } name)
            _characterName = name;

        if (Role == MultiboxRole.Host) {
            _stateSnapshot = new MultiboxMessage {
                Type = "state",
                Sender = _characterName,
                Running = _module.Running,
                Stopping = _module.PendingStopWhenSafe,
                TerritoryId = Svc.ClientState.TerritoryType,
                // Live read (not the loop-cached value): the grind task blocks for a whole
                // travel leg inside one loop iteration, and clients must not trail a fate behind.
                TargetFateId = _module.LiveTargetFateId,
                TeleportAetheryteId = AnnouncedTeleportAetheryteId,
            };

            if (++_tick % SettingsCheckTicks == 0) {
                var payloadJson = JsonConvert.SerializeObject(MultiboxSettingsPayload.From(_module.Config));
                if (payloadJson != _lastSettingsPayloadJson) {
                    _lastSettingsPayloadJson = payloadJson;
                    _settingsMessageJson = JsonConvert.SerializeObject(new MultiboxMessage { Type = "settings", Sender = _characterName, SettingsJson = payloadJson });
                    Interlocked.Increment(ref _settingsVersion);
                }
            }
        }
        else if (Role == MultiboxRole.Client && cid != 0) {
            // Sent to the host on a heartbeat rather than once at connect: the name isn't
            // resolved yet on the tick the transport starts, and the host needs live state anyway.
            _clientStatusJson = JsonConvert.SerializeObject(new MultiboxMessage {
                Type = "status",
                Sender = _characterName,
                Running = _module.Running,
                Stopping = _module.PendingStopWhenSafe,
                Following = IsFollowingHost,
                TerritoryId = Svc.ClientState.TerritoryType,
                TargetFateId = _module.LiveTargetFateId,
            });

            ApplyHostState();
            ApplyHostSettings();
        }
    }

    private long _nextStartAttemptMs; // framework thread only

    private void ApplyHostState() {
        var state = _hostState;
        if (state == null || !IsFresh) {
            // Host feed lost (crash/close) while we're running: after a grace period, finish
            // the current fate and stop rather than grinding on headless. Resetting the edge
            // state lets a returning host cleanly restart us.
            if (_module.Running && _hasAppliedRunning && _lastAppliedRunning
                && _hostState != null && Environment.TickCount64 - Interlocked.Read(ref _hostStateAtMs) > StaleMs * 3) {
                _module.PendingStopWhenSafe = true;
                _hasAppliedRunning = false;
            }
            return;
        }

        // Host is running normally — cancel any pending soft stop (stale-triggered, host-mirrored,
        // or local Ctrl+click) so we keep mirroring it. "Host wins" for soft stops by design; a
        // client the user wants parked should be HARD-stopped, which latches as a manual override.
        // Without this, a host soft-stop + restart while we're mid-fate parks us permanently.
        if (state is { Running: true, Stopping: false } && _module is { Running: true, PendingStopWhenSafe: true })
            _module.PendingStopWhenSafe = false;

        // Host is soft-stopping: finish our current fate too.
        if (state is { Running: true, Stopping: true } && _module.Running && !_module.PendingStopWhenSafe)
            _module.PendingStopWhenSafe = true;

        // The first host state after (re)connecting is a baseline, not a transition. An idle host must
        // not be applied on that first observation: to this code a host that has never started looks
        // identical to one that just stopped, so it would cancel a run the user had just started here
        // by hand — the task is created, killed a tick later, and pressing Start appears to do nothing.
        // A *running* host still falls through, so a client joining mid-run joins it.
        if (!_hasAppliedRunning && !state.Running) {
            _hasAppliedRunning = true;
            _lastAppliedRunning = false;
            return;
        }

        // Edge-triggered: mirror host start/stop transitions, but let a manual local
        // override stick until the host actually toggles again.
        if (!_hasAppliedRunning || state.Running != _lastAppliedRunning) {
            if (state.Running && !_module.Running) {
                // a failed start (companion plugins not loaded yet) retries with a cooldown
                if (Environment.TickCount64 < _nextStartAttemptMs)
                    return;
                _module.Running = true;
                if (!_module.Running) {
                    _nextStartAttemptMs = Environment.TickCount64 + 10_000;
                    return; // don't latch — retry once the dependencies come up
                }
            }
            else if (!state.Running && _module.Running && !_module.PendingStopWhenSafe) {
                _module.Running = false; // host hard-stopped; a soft-stopping client finishes on its own
            }
            _hasAppliedRunning = true;
            _lastAppliedRunning = state.Running;
        }
    }

    private void ApplyHostSettings() {
        if (!SyncSettingsEnabled)
            return;

        var received = Interlocked.Read(ref _receivedSettingsVersion);
        if (received == _appliedSettingsVersion || _hostSettingsJson is not { } json)
            return;

        try {
            JsonConvert.DeserializeObject<MultiboxSettingsPayload>(json)?.Apply(_module);
            Svc.Log.Debug("[Multibox] applied settings from host");
        }
        catch (Exception ex) {
            Svc.Log.Error(ex, "[Multibox] failed to apply host settings");
        }
        _appliedSettingsVersion = received;
    }

    private void StartTransport() {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        if (Role == MultiboxRole.Host)
            _ = Task.Run(() => RunHostAsync(ct), CancellationToken.None);
        else
            _ = Task.Run(() => RunClientAsync(ct), CancellationToken.None);
    }

    private void StopTransport() {
        try {
            _cts?.Cancel();
        }
        catch { /* already disposed */ }
        _cts = null;

        Connected = false;
        StatusText = "Off";
        lock (_clients)
            _clients.Clear();
        _stateSnapshot = null;
        _settingsMessageJson = null;
        _lastSettingsPayloadJson = null;
        _hostState = null;
        _hostSettingsJson = null;
        _hasAppliedRunning = false;
    }

    // ---- host transport ----

    // Old transport tasks can linger briefly after a role flip; only the live one may write status.
    private void SetStatus(CancellationToken ct, string text) {
        if (!ct.IsCancellationRequested)
            StatusText = text;
    }

    private async Task RunHostAsync(CancellationToken ct) {
        // A named semaphore (no thread affinity, unlike Mutex) enforces one host per session.
        // Without it a second host's pipe creation SUCCEEDS (MaxAllowedServerInstances), and
        // clients silently split between two hosts mirroring conflicting state.
        Semaphore? hostLock = null;
        var acquired = false;
        try {
            try {
                hostLock = new Semaphore(1, 1, @"Local\AutoFATE.MultiboxSync.v1.Host");
            }
            catch (Exception ex) {
                // this task is fire-and-forget; a throw here would die unobserved with status stuck
                Svc.Log.Warning($"[Multibox] host lock unavailable ({ex.Message}); hosting without single-host enforcement");
            }

            if (hostLock is not null) {
                while (!ct.IsCancellationRequested && !(acquired = hostLock.WaitOne(0))) {
                    SetStatus(ct, "Another host is already running — waiting");
                    if (!await DelaySafe(2000, ct))
                        return;
                }
                if (!acquired)
                    return;
            }

            SetStatus(ct, "Hosting");
            while (!ct.IsCancellationRequested) {
                NamedPipeServerStream pipe;
                try {
                    pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                    SetStatus(ct, "Pipe unavailable — retrying");
                    Svc.Log.Warning($"[Multibox] host pipe create failed: {ex.Message}");
                    if (await DelaySafe(2000, ct))
                        continue;
                    break;
                }

                try {
                    await pipe.WaitForConnectionAsync(ct);
                }
                catch {
                    pipe.Dispose();
                    if (ct.IsCancellationRequested)
                        break;
                    continue;
                }

                _ = Task.Run(() => ServeClientAsync(pipe, ct), CancellationToken.None);
            }
        }
        finally {
            if (acquired) {
                try { hostLock!.Release(); } catch { /* released with the handle regardless */ }
            }
            hostLock?.Dispose();
            SetStatus(ct, "Off");
        }
    }

    private async Task ServeClientAsync(NamedPipeServerStream pipe, CancellationToken ct) {
        var conn = new ClientConn();
        lock (_clients)
            _clients.Add(conn);
        try {
            // A suspended-but-alive client can wedge a non-cancellable pipe write forever;
            // force the pipe closed when the transport shuts down.
            using var forceClose = ct.Register(() => { try { pipe.Dispose(); } catch { /* already gone */ } });
            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

            _ = Task.Run(async () => {
                try {
                    while (!ct.IsCancellationRequested && pipe.IsConnected) {
                        var line = await reader.ReadLineAsync(ct);
                        if (line == null)
                            break;

                        if (JsonConvert.DeserializeObject<MultiboxMessage>(line) is not { } message)
                            continue;

                        // Any client message counts as a liveness signal; identity can arrive
                        // late (the name isn't known on the tick the client's transport starts).
                        lock (_clients) {
                            if (!_clients.Contains(conn))
                                _clients.Add(conn); // was pruned as a ghost but is alive again
                            conn.LastStatusAtMs = Environment.TickCount64;
                            if (message.Sender is { Length: > 0 } sender)
                                conn.Name = sender;
                            if (message.Type is "status") {
                                conn.Running = message.Running;
                                conn.Following = message.Following;
                                conn.TerritoryId = message.TerritoryId;
                                conn.TargetFateId = message.TargetFateId;
                            }
                        }
                    }
                }
                catch { /* connection torn down */ }
            }, CancellationToken.None);

            while (!ct.IsCancellationRequested && pipe.IsConnected) {
                if (_stateSnapshot is { } snapshot)
                    await writer.WriteLineAsync(JsonConvert.SerializeObject(snapshot).AsMemory(), ct);

                var version = Interlocked.Read(ref _settingsVersion);
                if (conn.SentSettingsVersion != version && _settingsMessageJson is { } settingsMessage) {
                    await writer.WriteLineAsync(settingsMessage.AsMemory(), ct);
                    conn.SentSettingsVersion = version;
                }

                // Woken early when the host announces a teleport; otherwise this is the heartbeat.
                await conn.Wake.WaitAsync(HeartbeatMs, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) {
            Svc.Log.Debug($"[Multibox] client connection ended: {ex.Message}");
        }
        finally {
            lock (_clients)
                _clients.Remove(conn);
            try {
                pipe.Dispose();
            }
            catch { /* already gone */ }
        }
    }

    // ---- client transport ----

    private async Task RunClientAsync(CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            CancellationTokenSource? connCts = null;
            Task? heartbeat = null;
            try {
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                // a wedged host can block a non-cancellable pipe write; force the pipe closed on shutdown
                using var forceClose = ct.Register(() => { try { pipe.Dispose(); } catch { /* already gone */ } });
                SetStatus(ct, "Searching for host…");
                await pipe.ConnectAsync(2000, ct);
                if (ct.IsCancellationRequested)
                    break;

                Connected = true;
                if (ct.IsCancellationRequested) {
                    Connected = false; // lost the race with StopTransport; don't leave a ghost flag
                    break;
                }
                SetStatus(ct, "Connected to host");
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(pipe, leaveOpen: true);

                // Heartbeat identity + state to the host for the life of this connection. Sending
                // this repeatedly (rather than one hello at connect) is what makes the host's client
                // list resolve: the character name isn't known yet on the tick the transport starts.
                connCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var heartbeatCt = connCts.Token;
                heartbeat = Task.Run(async () => {
                    try {
                        while (!heartbeatCt.IsCancellationRequested && pipe.IsConnected) {
                            if (_clientStatusJson is { } status)
                                await writer.WriteLineAsync(status.AsMemory(), heartbeatCt);
                            await Task.Delay(HeartbeatMs, heartbeatCt);
                        }
                    }
                    catch { /* connection torn down */ }
                }, CancellationToken.None);

                while (!ct.IsCancellationRequested) {
                    var line = await reader.ReadLineAsync(ct);
                    if (line == null)
                        break;

                    var message = JsonConvert.DeserializeObject<MultiboxMessage>(line);
                    switch (message?.Type) {
                        case "state":
                            _hostState = message;
                            Interlocked.Exchange(ref _hostStateAtMs, Environment.TickCount64);
                            break;
                        case "settings" when message.SettingsJson != null:
                            _hostSettingsJson = message.SettingsJson;
                            Interlocked.Increment(ref _receivedSettingsVersion);
                            break;
                    }
                }
            }
            catch (OperationCanceledException) {
                break; // the finally below still runs
            }
            catch (TimeoutException) { /* no host yet */ }
            catch (Exception ex) {
                Svc.Log.Debug($"[Multibox] host connection ended: {ex.Message}");
            }
            finally {
                if (connCts is not null) {
                    try { connCts.Cancel(); } catch { /* already disposed */ }
                }
                if (heartbeat is not null) {
                    try { await heartbeat; } catch { /* already logged/ignored inside */ }
                }
                connCts?.Dispose();
                if (!ct.IsCancellationRequested)
                    Connected = false; // a superseded transport must not clobber the live one's state
            }

            if (!await DelaySafe(1000, ct))
                break;
            SetStatus(ct, "Disconnected — retrying");
        }
        SetStatus(ct, "Off");
    }

    private static async Task<bool> DelaySafe(int ms, CancellationToken ct) {
        try {
            await Task.Delay(ms, ct);
            return true;
        }
        catch (OperationCanceledException) {
            return false;
        }
    }
}
