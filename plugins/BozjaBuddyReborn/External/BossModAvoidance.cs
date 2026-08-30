using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using ECommons.Automation;
using ECommons.DalamudServices;

namespace BozjaBuddyReborn.External;

/// <summary>Which BossMod is installed and being driven.</summary>
public enum BossModFork : byte
{
    None = 0,

    /// <summary>FFXIV-CombatReborn/BossmodReborn - InternalName "BossModReborn", /bmr + /bmrai.</summary>
    Reborn = 1,

    /// <summary>awgil/ffxiv_bossmod - InternalName "BossMod", /vbm (+ the deprecated /vbmai alias).</summary>
    Original = 2,
}

/// <summary>
/// BossMod driven in AoE-AVOIDANCE ONLY mode. Supports BOTH forks - the Combat Reborn fork and
/// the original awgil plugin - detecting whichever is loaded and driving it through the surface
/// that fork actually has. Reborn is preferred when both are present (which is itself a broken
/// setup: they register the same "BossMod.*" IPC gates and the last one loaded wins).
///
/// THE ROLE SPLIT. RSR owns the rotation, so BossMod must press nothing. What "press nothing"
/// takes differs per fork, and this is the whole reason the two are handled separately:
///
///   REBORN. The AI is its own subsystem (AIManager / AIBehaviour) that runs INDEPENDENTLY of the
///   global autorotation preset. Three guards, each closing a different hole:
///     1. /bmrai forbidactions on   -> the AI itself queues no actions
///     2. AI.SetPreset("")          -> the AI has no rotation preset to run
///     3. Presets.SetForceDisabled  -> the GLOBAL autorotation cannot fire either
///   Guard 3 matters if the user had an autorotation preset active, since that runs regardless
///   of the AI. Movement is left ENABLED - dodging is movement.
///
///   ORIGINAL. There is no AI subsystem any more. "AI mode" (AIConfig.Enabled) makes the
///   RotationModuleManager add its built-in "VBM Multibox" preset to the active list, and that
///   preset is nothing but AutoTarget + FollowSlot + NormalMovement (read from
///   DefaultRotationPresets.json) - it contains no class rotation module, so it cannot press
///   buttons in the first place. The guards are therefore:
///     1. AIConfig.ForbidActions = true  -> AutoTarget is set to "Passive" (RSR owns targeting)
///     2. AIConfig.ForbidMovement = false -> NormalMovement stays on "Pathfind" - the dodge engine
///     3. Presets.ClearActive            -> any USER rotation preset ("VBM Default", ...) that
///                                          would press buttons alongside RSR is removed
///   And, crucially, NOT SetForceDisabled: in the original, RotationModuleManager.Update only adds
///   the AI preset `if (_aiConfig.Enabled &amp;&amp; !Presets.Contains(ForceDisable))`, so force-disabling
///   would remove the dodge engine along with everything else. The Reborn guard 3 is actively
///   harmful here. The original also force-disables ITSELF on death in combat and on a ninja pull,
///   so the heartbeat re-clears that whenever it reappears mid-fight.
///
/// WHAT EACH FORK CAN TELL US. Reborn exposes AI.IsNavigating and the Hints.* telemetry; the
/// original exposes none of that - only Presets.* and the Configuration console gate. So with the
/// original there is no "BossMod is dodging right now" signal. That turns out not to matter for
/// contention, because BOTH forks' MovementOverride refuse to steer while vnavmesh's
/// "vnav.PathIsRunning" shared flag is set: whichever fork is loaded, BossMod only moves when we
/// are not pathing. The original goes one further and CLOSES ON THE TARGET by itself -
/// FollowSlot has a fallback that adds a goal zone around the player's hostile target whenever no
/// other module has - which Reborn does not do solo; <see cref="ClosesToTargetItself"/> lets the
/// controller stand CombatApproach down for it.
///
/// Everything this changes is saved and restored on release, so stopping the plugin hands the
/// user's BossMod back exactly as it was found.
///
/// Gates used (Framework/IPCProvider.cs in each fork; both register under "BossMod."):
///   shared     Presets.GetActive/SetActive/ClearActive/GetForceDisabled/SetForceDisabled,
///              Configuration  Func&lt;List&lt;string&gt;,bool,List&lt;string&gt;&gt;  ("&lt;node&gt; &lt;field&gt; [&lt;value&gt;]",
///              NO "cfg" prefix - the console parser takes the node name first)
///   Reborn     AI.SetPreset, AI.GetPreset, AI.PauseMovement, AI.IsNavigating,
///              Hints.IsPositionSafe, Hints.ForbiddenZonesCount, Hints.NextDamageIn
///   original   Presets.GetActiveList Func&lt;List&lt;string&gt;&gt;, Presets.SetActiveList Func&lt;List&lt;string&gt;,bool&gt;
///              (the original runs SEVERAL presets at once, so GetActive - "the name iff exactly
///              one is active" - is useless for a snapshot there)
/// </summary>
public sealed class BossModAvoidance
{
    /// <summary>The Combat Reborn fork.</summary>
    public const string RebornInternalName = "BossModReborn";

    /// <summary>The original awgil plugin.</summary>
    public const string OriginalInternalName = "BossMod";

    private const string RebornAiCommand = "/bmrai";
    private const string OriginalRootCommand = "/vbm";

    /// <summary>The original's built-in AI preset; it is in the active list whenever AI mode is on.</summary>
    private const string OriginalAiPresetName = "VBM Multibox";

    /// <summary>Config node name for the console gate. Same in both forks (BossMod.AI.AIConfig).</summary>
    private const string AiConfigNode = "AIConfig";

    /// <summary>How often the installed-plugin list is re-walked to see which fork is loaded.</summary>
    private const long ForkRefreshMs = 1000;

    private readonly IDalamudPluginInterface _pi;

    // --- shared gates -----------------------------------------------------------
    private readonly ICallGateSubscriber<string?>? _getActivePreset;
    private readonly ICallGateSubscriber<string, bool>? _setActivePreset;
    private readonly ICallGateSubscriber<bool>? _clearActivePreset;
    private readonly ICallGateSubscriber<bool>? _getForceDisabled;
    private readonly ICallGateSubscriber<bool>? _setForceDisabled;
    private readonly ICallGateSubscriber<List<string>, bool, List<string>>? _configuration;

    // --- Reborn-only gates ------------------------------------------------------
    private readonly ICallGateSubscriber<string, object>? _aiSetPreset;
    private readonly ICallGateSubscriber<string>? _aiGetPreset;
    private readonly ICallGateSubscriber<bool, object>? _pauseMovement;
    private readonly ICallGateSubscriber<bool>? _isNavigating;
    private readonly ICallGateSubscriber<Vector3, bool>? _isPositionSafe;
    private readonly ICallGateSubscriber<int>? _forbiddenZonesCount;
    private readonly ICallGateSubscriber<float>? _nextDamageIn;

    // --- original-only gates ----------------------------------------------------
    private readonly ICallGateSubscriber<List<string>>? _getActiveList;
    private readonly ICallGateSubscriber<List<string>, bool>? _setActiveList;

    private readonly EdgeTrigger<bool> _aiEnabled;

    // --- fork detection cache ---------------------------------------------------
    private BossModFork _fork;
    private bool _bothLoaded;
    private long _forkCheckedMs;

    // --- saved user state, restored on release -----------------------------------
    private bool _configured;
    private bool _savedStateValid;
    private BossModFork _savedFork;
    private bool _savedWasForceDisabled;
    private bool? _savedForbidActions;
    private bool? _savedForbidMovement;
    // Reborn
    private string? _savedAutorotationPreset;
    private string? _savedAiPreset;
    // original
    private bool? _savedOriginalAiEnabled;
    private List<string>? _savedOriginalPresetList;

    /// <summary>
    /// The last AI on/off value actually written to the original's config, so a heartbeat whose
    /// read-back fails does not re-write (and re-save) the same value every interval.
    /// </summary>
    private bool? _lastOriginalEnabledWritten;

    public BossModAvoidance(IDalamudPluginInterface pi)
    {
        _pi = pi;

        _getActivePreset = Bind(() => pi.GetIpcSubscriber<string?>("BossMod.Presets.GetActive"));
        _setActivePreset = Bind(() => pi.GetIpcSubscriber<string, bool>("BossMod.Presets.SetActive"));
        _clearActivePreset = Bind(() => pi.GetIpcSubscriber<bool>("BossMod.Presets.ClearActive"));
        _getForceDisabled = Bind(() => pi.GetIpcSubscriber<bool>("BossMod.Presets.GetForceDisabled"));
        _setForceDisabled = Bind(() => pi.GetIpcSubscriber<bool>("BossMod.Presets.SetForceDisabled"));
        _configuration = Bind(() => pi.GetIpcSubscriber<List<string>, bool, List<string>>("BossMod.Configuration"));

        _aiSetPreset = Bind(() => pi.GetIpcSubscriber<string, object>("BossMod.AI.SetPreset"));
        _aiGetPreset = Bind(() => pi.GetIpcSubscriber<string>("BossMod.AI.GetPreset"));
        _pauseMovement = Bind(() => pi.GetIpcSubscriber<bool, object>("BossMod.AI.PauseMovement"));
        _isNavigating = Bind(() => pi.GetIpcSubscriber<bool>("BossMod.AI.IsNavigating"));
        _isPositionSafe = Bind(() => pi.GetIpcSubscriber<Vector3, bool>("BossMod.Hints.IsPositionSafe"));
        _forbiddenZonesCount = Bind(() => pi.GetIpcSubscriber<int>("BossMod.Hints.ForbiddenZonesCount"));
        _nextDamageIn = Bind(() => pi.GetIpcSubscriber<float>("BossMod.Hints.NextDamageIn"));

        _getActiveList = Bind(() => pi.GetIpcSubscriber<List<string>>("BossMod.Presets.GetActiveList"));
        _setActiveList = Bind(() => pi.GetIpcSubscriber<List<string>, bool>("BossMod.Presets.SetActiveList"));

        _aiEnabled = new EdgeTrigger<bool>(SetAi);
    }

    private static T? Bind<T>(Func<T> resolve) where T : class
    {
        try { return resolve(); }
        catch { return null; }
    }

    // ------------------------------------------------------------------ detection

    /// <summary>Which fork is loaded and being driven. Reborn wins if both are.</summary>
    public BossModFork Fork
    {
        get
        {
            RefreshFork();
            return _fork;
        }
    }

    /// <summary>
    /// True when BOTH forks are loaded at once. That is a broken setup - they register identical
    /// "BossMod.*" gate names, so whichever loaded last owns the IPC and unloading either strips
    /// it - and the UI says so.
    /// </summary>
    public bool BothForksLoaded
    {
        get
        {
            RefreshFork();
            return _bothLoaded;
        }
    }

    /// <summary>True when BossMod Reborn is installed and loaded.</summary>
    public bool RebornLoaded => Fork == BossModFork.Reborn;

    /// <summary>True when the original awgil BossMod is the fork being driven.</summary>
    public bool OriginalLoaded => Fork == BossModFork.Original;

    /// <summary>Human name of the fork being driven, for the UI.</summary>
    public string ForkName => Fork switch
    {
        BossModFork.Reborn => "BossMod Reborn",
        BossModFork.Original => "BossMod (original)",
        _ => "BossMod",
    };

    /// <summary>
    /// Do the gates only BossMod Reborn registers actually answer?
    ///
    /// AI.SetPreset / Hints.ForbiddenZonesCount exist in Reborn's IPCProvider and in no version
    /// of the original, so this distinguishes the two by what is really on the wire rather than
    /// by what the plugin list claims. HasFunction/HasAction is a registration check, not a call,
    /// so this is cheap enough for the once-a-second refresh.
    /// </summary>
    private bool RebornGatesAnswer()
    {
        try
        {
            if (_forbiddenZonesCount?.HasFunction == true)
                return true;
            if (_aiGetPreset?.HasFunction == true)
                return true;
            return _aiSetPreset?.HasAction == true;
        }
        catch
        {
            return false;
        }
    }

    private void RefreshFork()
    {
        var now = Environment.TickCount64;
        if (_forkCheckedMs != 0 && now - _forkCheckedMs < ForkRefreshMs)
            return;
        _forkCheckedMs = now;

        bool reborn = false, original = false;
        try
        {
            foreach (var p in _pi.InstalledPlugins)
            {
                if (!p.IsLoaded)
                    continue;
                if (p.InternalName == RebornInternalName)
                    reborn = true;
                else if (p.InternalName == OriginalInternalName)
                    original = true;
            }
        }
        catch { /* plugin list unavailable */ }

        // CAPABILITY BEATS NAME. The installed-plugin list says what is installed and enabled,
        // which is not the same as what actually constructed and registered its IPC - a plugin
        // can sit in that list marked loaded while its instance never came up (observed: a
        // BossModReborn entry with loadPlugin:true that never reached "Finished loading"). If we
        // believe the name we then drive the wrong surface: every Reborn-only read returns its
        // failure default, and this class silently reports Reborn behaviour for a fork that is
        // not there. So Reborn is only accepted when a Reborn-ONLY gate actually answers.
        if (reborn && !RebornGatesAnswer())
        {
            reborn = false;
            original = original || (_configuration?.HasFunction ?? false);
        }

        var fork = reborn ? BossModFork.Reborn : original ? BossModFork.Original : BossModFork.None;
        if (fork != _fork)
        {
            // Whatever we configured was configured on the OTHER plugin.
            _configured = false;
            _fork = fork;
        }
        _bothLoaded = reborn && original;
    }

    // --------------------------------------------------------------- availability

    /// <summary>True when a BossMod is loaded and the gates this fork is driven through answer.</summary>
    public bool Available
    {
        get
        {
            try
            {
                return Fork switch
                {
                    BossModFork.Reborn => _isPositionSafe?.HasFunction ?? false,
                    BossModFork.Original => _configuration?.HasFunction ?? false,
                    _ => false,
                };
            }
            catch { return false; }
        }
    }

    /// <summary>Why avoidance is unavailable, or null when it is fine.</summary>
    public string? UnavailableReason
    {
        get
        {
            if (Available)
                return null;
            return Fork switch
            {
                BossModFork.Reborn => "BossMod Reborn is loaded but its IPC is not answering.",
                BossModFork.Original => "BossMod is loaded but its IPC is not answering.",
                _ => "No BossMod is installed. Install BossMod Reborn (preferred) or the original BossMod.",
            };
        }
    }

    // ------------------------------------------------------------------ telemetry

    /// <summary>
    /// True when this fork can report that it is steering the character (Reborn's
    /// AI.IsNavigating). The original has no such gate, so <see cref="IsNavigating"/> is always
    /// false for it - the UI should say "unknown" rather than "no".
    /// </summary>
    public bool SteeringKnown => Fork == BossModFork.Reborn;

    /// <summary>
    /// True when this fork walks the character into range of its target BY ITSELF, so the
    /// controller's own approach must stand down or the two will fight over movement.
    ///
    /// The original: FollowSlot (part of its AI preset) adds a goal zone around the player's
    /// hostile target whenever no other module has - 3y for melee and tanks, 25y otherwise - and
    /// NormalMovement pathfinds to it, around forbidden zones. Reborn does not do this solo (see
    /// CombatApproach for the proof), so for Reborn this is false and the approach is ours.
    /// </summary>
    public bool ClosesToTargetItself => Fork == BossModFork.Original;

    /// <summary>True while Reborn's AI is actively navigating the character somewhere. Always false for the original.</summary>
    public bool IsNavigating
    {
        get
        {
            if (Fork != BossModFork.Reborn)
                return false;
            try { return _isNavigating?.InvokeFunc() ?? false; } catch { return false; }
        }
    }

    /// <summary>Active telegraphed danger zones. &gt;0 means mechanics are out right now. 0 for the original (no gate).</summary>
    public int ForbiddenZones
    {
        get
        {
            if (Fork != BossModFork.Reborn)
                return 0;
            try { return _forbiddenZonesCount?.InvokeFunc() ?? 0; } catch { return 0; }
        }
    }

    /// <summary>Seconds until the next predicted damage event, or float.MaxValue when none / unknown.</summary>
    public float NextDamageIn
    {
        get
        {
            if (Fork != BossModFork.Reborn)
                return float.MaxValue;
            try { return _nextDamageIn?.InvokeFunc() ?? float.MaxValue; } catch { return float.MaxValue; }
        }
    }

    /// <summary>
    /// Is this world position currently outside every telegraph? Returns true when unknown -
    /// "unknown" must never block movement.
    /// </summary>
    public bool IsPositionSafe(Vector3 world)
    {
        if (Fork != BossModFork.Reborn)
            return true;
        try { return _isPositionSafe?.InvokeFunc(world) ?? true; }
        catch { return true; }
    }

    // ------------------------------------------------------------------ on / off

    /// <summary>
    /// How often to re-send the AI on/off state even when it has not changed. Reborn's AIManager
    /// idles its own AI whenever the party slot it follows stops being valid - which a Bozja
    /// alliance does routinely - and there is no gate to read the AI's enabled state back, so a
    /// periodic re-assert is the only way to notice. The original force-disables its own
    /// autorotation (AI preset included) on death and on a ninja pull, so its heartbeat re-clears
    /// that. 0 disables.
    /// </summary>
    public long ReassertIntervalMs
    {
        get => _aiEnabled.ReassertIntervalMs;
        set => _aiEnabled.ReassertIntervalMs = value;
    }

    /// <summary>Seconds since the AI state was last actually sent, for the UI.</summary>
    public float SecondsSinceSent => _aiEnabled.SecondsSinceSent;

    /// <summary>
    /// Turn avoidance on or off. Edge-triggered, so per-tick calls are free.
    ///
    /// The re-assert is deliberately withheld while Reborn's AI is steering: "/bmrai on" runs
    /// SwitchToFollow, which disposes the current AIBehaviour and clears the navigation
    /// controller. Doing that in the middle of a dodge would drop the character back into the
    /// mechanic it was walking out of, and a heartbeat is never worth that. (The original's
    /// heartbeat is read-before-write and touches nothing when nothing drifted, so it is safe at
    /// any moment - and there is no steering signal to withhold on anyway.)
    /// </summary>
    public void SetEnabled(bool enabled) => _aiEnabled.Request(enabled, allowReassert: !IsNavigating);

    public void Resync()
    {
        _aiEnabled.Resync();
        _configured = false;
    }

    private void SetAi(bool enabled)
    {
        switch (Fork)
        {
            case BossModFork.Reborn:
                if (enabled)
                {
                    ApplyAvoidanceOnlyConfig();
                    Chat.ExecuteCommand($"{RebornAiCommand} on");
                }
                else
                {
                    Chat.ExecuteCommand($"{RebornAiCommand} off");
                }
                break;

            case BossModFork.Original:
                if (enabled)
                {
                    // Every step here reads before it writes, so the heartbeat re-running the whole
                    // avoidance-only setup costs a handful of IPC reads and writes nothing (and, since
                    // a config write fires Modified and saves to disk, that is the point).
                    ApplyAvoidanceOnlyConfig(force: true);
                    SetOriginalAiEnabled(true);
                }
                else
                {
                    SetOriginalAiEnabled(false);
                }
                break;
        }
    }

    // ------------------------------------------------------- avoidance-only setup

    /// <summary>
    /// Put BossMod into avoidance-only mode, saving whatever it is being changed from.
    /// </summary>
    public void ApplyAvoidanceOnlyConfig(bool force = false)
    {
        switch (Fork)
        {
            case BossModFork.Reborn:
                if (_configured && !force)
                    return;
                SaveUserState();
                ApplyRebornAvoidanceOnly();
                _configured = true;
                break;

            case BossModFork.Original:
                // No latch: it is idempotent by construction (read-before-write) and cheap.
                SaveUserState();
                ApplyOriginalAvoidanceOnly();
                _configured = true;
                break;
        }
    }

    private void ApplyRebornAvoidanceOnly()
    {
        try
        {
            // 1. The AI queues no actions. Explicit "on" rather than a bare toggle, so calling
            //    this twice cannot flip it back off. (Reborn's handler only fires Modified on a
            //    real change, so this is already save-idempotent.)
            Chat.ExecuteCommand($"{RebornAiCommand} forbidactions on");

            // Movement must stay allowed - dodging IS movement.
            Chat.ExecuteCommand($"{RebornAiCommand} forbidmovement off");

            // 2. Clear the AI's own rotation preset. An unmatched name resolves to null inside
            //    Reborn's gate, which is exactly "no preset".
            _aiSetPreset?.InvokeAction(string.Empty);

            // 3. Stop the GLOBAL autorotation too. This is the guard that actually matters when
            //    the user had a preset active, since that runs independently of the AI.
            //    SetForceDisabled takes no arguments and returns false if already set.
            if (_getForceDisabled?.InvokeFunc() == false)
                _setForceDisabled?.InvokeFunc();
        }
        catch (Exception ex)
        {
            Svc.Log.Warning($"[BozjaBuddyReborn] Could not configure BossMod Reborn for avoidance-only: {ex.Message}");
        }
    }

    private void ApplyOriginalAvoidanceOnly()
    {
        try
        {
            // 1. AutoTarget -> Passive. RSR picks the targets. (AIWindow pushes this into the AI
            //    preset's transient settings when AIConfig.Modified fires, which the console gate
            //    does with save=true.)
            SetConfigBoolIfDifferent("ForbidActions", true, viaChatFallback: true);

            // 2. NormalMovement stays on Pathfind - that IS the dodging.
            SetConfigBoolIfDifferent("ForbidMovement", false, viaChatFallback: true);

            // 3. Anything else in the active list is either a user rotation preset - which would
            //    press buttons alongside RSR - or the force-disable sentinel (its name is ""),
            //    which would keep the AI preset from being added at all. Clear() drops both; the
            //    RotationModuleManager re-adds the AI preset on its next rebuild because
            //    AIConfig.Enabled is on. Note that this is deliberately NOT SetForceDisabled.
            var active = ReadOriginalActiveList();
            if (active != null)
            {
                var foreign = false;
                foreach (var name in active)
                {
                    if (!string.Equals(name, OriginalAiPresetName, StringComparison.OrdinalIgnoreCase))
                    {
                        foreign = true;
                        break;
                    }
                }
                if (foreign)
                    _clearActivePreset?.InvokeFunc();
            }
            else if (_getForceDisabled?.InvokeFunc() == true)
            {
                // No list gate (older build) - at least make sure force-disable is not eating the AI.
                _clearActivePreset?.InvokeFunc();
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Warning($"[BozjaBuddyReborn] Could not configure BossMod for avoidance-only: {ex.Message}");
        }
    }

    // ---------------------------------------------------------- snapshot / restore

    /// <summary>
    /// Snapshot the settings this class is about to change, once per engagement of control.
    /// </summary>
    private void SaveUserState()
    {
        if (_savedStateValid)
            return;

        try
        {
            _savedFork = Fork;
            _savedWasForceDisabled = _getForceDisabled?.InvokeFunc() ?? false;
            _savedForbidActions = ReadConfigBool("ForbidActions");
            _savedForbidMovement = ReadConfigBool("ForbidMovement");

            switch (_savedFork)
            {
                case BossModFork.Reborn:
                    // Only meaningful to remember a preset name if force-disable was NOT already
                    // on - otherwise GetActive reports the force-disable pseudo-preset, which
                    // cannot be restored by name.
                    _savedAutorotationPreset = _savedWasForceDisabled ? null : _getActivePreset?.InvokeFunc();
                    _savedAiPreset = _aiGetPreset?.InvokeFunc();
                    break;

                case BossModFork.Original:
                    _savedOriginalAiEnabled = ReadConfigBool("Enabled");
                    _savedOriginalPresetList = ReadOriginalActiveList();
                    if (_savedOriginalPresetList != null)
                    {
                        // The AI preset re-adds itself from AIConfig.Enabled, and "" is the
                        // force-disable sentinel (restored via SetForceDisabled, since
                        // FindPresetByName("") cannot resolve it and SetActiveList would give up).
                        _savedOriginalPresetList.RemoveAll(n =>
                            string.IsNullOrEmpty(n) ||
                            string.Equals(n, OriginalAiPresetName, StringComparison.OrdinalIgnoreCase));
                    }
                    break;
            }

            _savedStateValid = true;
        }
        catch (Exception ex)
        {
            Svc.Log.Debug($"[BozjaBuddyReborn] Could not snapshot BossMod state: {ex.Message}");
            // Leave _savedStateValid false: without a trustworthy snapshot we must not pretend
            // to restore one later.
        }
    }

    /// <summary>
    /// Stop the AI and put back everything we changed, so the user's BossMod is handed back as
    /// it was found. Called on stop, on zone-out, and on dispose.
    /// </summary>
    public void ReleaseControl()
    {
        var fork = Fork;

        if (fork == BossModFork.None)
        {
            _aiEnabled.Resync();
            ForgetSnapshot();
            return;
        }

        try
        {
            switch (fork)
            {
                case BossModFork.Reborn:
                    ReleaseReborn();
                    break;
                case BossModFork.Original:
                    ReleaseOriginal();
                    break;
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Warning($"[BozjaBuddyReborn] Could not fully restore BossMod state: {ex.Message}");
        }
        finally
        {
            ForgetSnapshot();
        }
    }

    private void ReleaseReborn()
    {
        // Edge-triggered: only actually sends "/bmrai off" if we had turned it on.
        SetEnabled(false);

        _pauseMovement?.InvokeAction(false);

        if (!_savedStateValid || _savedFork != BossModFork.Reborn)
            return;

        // ForbidActions / ForbidMovement: only touch them if we actually know what they were.
        if (_savedForbidActions is { } forbid)
            Chat.ExecuteCommand($"{RebornAiCommand} forbidactions {(forbid ? "on" : "off")}");
        if (_savedForbidMovement is { } forbidMove)
            Chat.ExecuteCommand($"{RebornAiCommand} forbidmovement {(forbidMove ? "on" : "off")}");

        // The AI's own preset.
        if (!string.IsNullOrEmpty(_savedAiPreset))
            _aiSetPreset?.InvokeAction(_savedAiPreset);

        // The global autorotation preset. Leave force-disable alone if that is how we found it;
        // otherwise put the user's preset back, or clear if they had none.
        if (!_savedWasForceDisabled)
        {
            if (!string.IsNullOrEmpty(_savedAutorotationPreset))
                _setActivePreset?.InvokeFunc(_savedAutorotationPreset);
            else
                _clearActivePreset?.InvokeFunc();
        }
    }

    private void ReleaseOriginal()
    {
        var haveSnapshot = _savedStateValid && _savedFork == BossModFork.Original;

        // AI on/off is a CONFIG value in this fork, so "off" and "back to what it was" are the same
        // write. Do it once, directly, rather than sending off and then on again through the edge
        // trigger. Without a snapshot, only turn it off if we are the ones who turned it on -
        // ReleaseControl runs twice on dispose (controller stop, then the director), and the second
        // pass must not switch off an AI the user had running before we started.
        if (haveSnapshot)
            SetOriginalAiEnabled(_savedOriginalAiEnabled ?? false);
        else if (_aiEnabled.Current == true)
            SetOriginalAiEnabled(false);
        _aiEnabled.Resync();

        if (!haveSnapshot)
            return;

        if (_savedForbidActions is { } forbid)
            SetConfigBoolIfDifferent("ForbidActions", forbid, viaChatFallback: true);
        if (_savedForbidMovement is { } forbidMove)
            SetConfigBoolIfDifferent("ForbidMovement", forbidMove, viaChatFallback: true);

        if (_savedWasForceDisabled)
        {
            if (_getForceDisabled?.InvokeFunc() == false)
                _setForceDisabled?.InvokeFunc();
        }
        else if (_savedOriginalPresetList is { Count: > 0 } list)
        {
            // Clear() + Activate() each; the AI preset re-adds itself afterwards if AI stays on.
            var ok = _setActiveList?.InvokeFunc(list) ?? false;
            if (!ok)
            {
                // A name that no longer resolves makes SetActiveList give up wholesale. Fall back
                // to restoring one at a time through the single-preset gate.
                foreach (var name in list)
                    _setActivePreset?.InvokeFunc(name);
            }
        }
        // else: nothing of the user's was active. Whatever is there now is the AI preset (if AI
        // was and still is on) or nothing.
    }

    private void ForgetSnapshot()
    {
        _configured = false;
        _savedStateValid = false;
        _savedFork = BossModFork.None;
        _savedWasForceDisabled = false;
        _savedForbidActions = null;
        _savedForbidMovement = null;
        _savedAutorotationPreset = null;
        _savedAiPreset = null;
        _savedOriginalAiEnabled = null;
        _savedOriginalPresetList = null;
        _lastOriginalEnabledWritten = null;
    }

    // ------------------------------------------------------ original: AI on/off

    /// <summary>
    /// The original's AI on/off IS AIConfig.Enabled (what "/vbm ai on|off" writes), so it is both
    /// readable and settable through the console gate - which is what makes its heartbeat
    /// read-before-write instead of a blind re-send. Falls back to the chat command if the gate
    /// cannot be used, but never re-sends an unchanged value that way, because a config write
    /// fires Modified and saves the config file.
    /// </summary>
    private void SetOriginalAiEnabled(bool enabled)
    {
        var current = ReadConfigBool("Enabled");
        if (current == enabled)
        {
            _lastOriginalEnabledWritten = enabled;
            return;
        }

        if (current == null && _lastOriginalEnabledWritten == enabled)
            return; // cannot read it back and we already sent this value - do not spam saves

        if (!WriteConfigBool("Enabled", enabled))
            Chat.ExecuteCommand($"{OriginalRootCommand} ai {(enabled ? "on" : "off")}");

        _lastOriginalEnabledWritten = enabled;
    }

    // ------------------------------------------------- Configuration console gate

    /// <summary>
    /// Read a bool field of AIConfig through the console gate ("&lt;node&gt; &lt;field&gt;" with save=false
    /// returns the value's ToString()). Null when the gate is absent, the field is unknown to
    /// this fork, or the value cannot be parsed.
    /// </summary>
    private bool? ReadConfigBool(string field)
    {
        try
        {
            if (_configuration is not { } gate || !gate.HasFunction)
                return null;

            var result = gate.InvokeFunc([AiConfigNode, field], false);
            if (result == null || result.Count == 0)
                return null;

            return bool.TryParse(result[0], out var value) ? value : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Write a bool field of AIConfig through the console gate with save=true, which also fires
    /// the node's Modified event - required, because that is what the original's AIWindow
    /// listens on to push ForbidActions/ForbidMovement into the AI preset. Returns false if the
    /// gate is absent or the console reported anything (it is silent on success).
    /// </summary>
    private bool WriteConfigBool(string field, bool value)
    {
        try
        {
            if (_configuration is not { } gate || !gate.HasFunction)
                return false;

            var result = gate.InvokeFunc([AiConfigNode, field, value ? "true" : "false"], true);
            return result == null || result.Count == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Read-then-write a bool field so an unchanged value costs no write (and therefore no
    /// config save). Used for the original; Reborn's own /bmrai handler is already change-gated.
    /// </summary>
    private void SetConfigBoolIfDifferent(string field, bool value, bool viaChatFallback)
    {
        if (ReadConfigBool(field) == value)
            return;

        if (WriteConfigBool(field, value))
            return;

        if (viaChatFallback && Fork == BossModFork.Original)
            Chat.ExecuteCommand($"{OriginalRootCommand} cfg {AiConfigNode} {field} {(value ? "true" : "false")}");
    }

    private List<string>? ReadOriginalActiveList()
    {
        try
        {
            if (_getActiveList is not { } gate || !gate.HasFunction)
                return null;
            var list = gate.InvokeFunc();
            return list == null ? null : new List<string>(list);
        }
        catch
        {
            return null;
        }
    }
}
