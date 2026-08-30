using System;
using System.Collections.Generic;
using BozjaBuddyReborn.Game;
using Dalamud.Game.ClientState.Conditions;
using ECommons.Automation.UIInput;
using EventType = ECommons.Automation.UIInput.EventType;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace BozjaBuddyReborn.Automation;

/// <summary>Where a sign-up attempt has got to. See <see cref="SignUpRunner"/>.</summary>
public enum SignUpPhase : byte
{
    Idle = 0,

    /// <summary>Asking the agent to show the Resistance Recruitment window.</summary>
    Opening = 1,

    /// <summary>Window is up; looking for the Register button and pressing it.</summary>
    Registering = 2,

    /// <summary>Registered. Waiting out the lottery for a Commence button to appear.</summary>
    AwaitingSelection = 3,

    /// <summary>Commence pressed; waiting for the game to put us in.</summary>
    Commencing = 4,

    Done = 5,
}

/// <summary>
/// Signs this box up for a Critical Engagement through the Resistance Recruitment window.
///
/// HOW JOINING ACTUALLY WORKS, because the whole class turns on it and the previous version had
/// it half right. From the Patch 5.35 notes: critical engagements "do not require you to be
/// present in the field to participate. Instead, players must request deployment via the
/// Resistance Recruitment window." So this is a menu, not a place - walking into the marked
/// circle does not enrol you.
///
/// AND IT IS TWO CLICKS, IN TWO PHASES, WHICH IS THE PART THAT WAS MISSING:
///
///   1. REGISTER, during the ~60s registration phase. This enters a lottery against the
///      engagement's participant cap (24, or 48 for the large-scale ones).
///   2. COMMENCE, during the ~120s preparation phase, and ONLY if the lottery picked you. This
///      is the click that actually puts you in the arena. "Permission to join critical
///      engagement granted! N minute remains to deploy" is the message that opens this phase.
///
/// The button is one button whose label changes: Register -> Withdraw once you are in the
/// lottery -> Commence once you have been selected. A single press can therefore never complete
/// a join, and the old implementation fired once and then watched the ENGAGEMENT's state - which
/// advances on the game's own timer whether or not you did anything, so it reported success for
/// doing nothing.
///
/// WHY THE BUTTON IS CLICKED RATHER THAN A CALLBACK FIRED. The old code fired
/// Callback.Fire(addon, true, index) - a bare int, at a row index, with a comment admitting the
/// shape was inferred rather than observed. Nothing anywhere drives this addon: no plugin in the
/// local corpus of several hundred references it, and FFXIVClientStructs exposes no join method
/// on the agent. Every windowed list addon in ECommons that DOES have per-row buttons uses a
/// leading command selector rather than a bare index, so the guess was very likely wrong - and a
/// wrong payload is not inert, it is an unknown command.
///
/// Clicking the button node needs no payload at all: ClickAddonButton reads the AtkEvent the
/// game already attached to that button and hands it back through ReceiveEvent, which is how
/// ECommons drives every other confirm button in the game. The label is then also the honest
/// answer to "am I registered", with no struct to guess at.
///
/// LOCALISATION IS THE ONE REMAINING ASSUMPTION: the labels matched below are the English
/// client's. Every button found is logged with its text, so a non-English client produces a log
/// line naming exactly what to add rather than a silent failure.
/// </summary>
public sealed class SignUpRunner
{
    /// <summary>FFXIVClientStructs AgentId 388 - the Resistance Recruitment window's agent.</summary>
    private const AgentId RecruitmentAgent = AgentId.MycBattleAreaInfo;

    /// <summary>How long to wait for the window to come up before giving up.</summary>
    private const long OpenTimeoutMs = 8_000;

    /// <summary>
    /// How long to wait for the lottery after registering.
    ///
    /// The registration phase is about a minute and the preparation phase about two, so this
    /// covers being registered early in a fresh window and still catching Commence. The old
    /// 10 SECOND budget covered the whole attempt, which is shorter than the registration phase
    /// alone - it could not have succeeded even with everything else right.
    /// </summary>
    private const long SelectionTimeoutMs = 200_000;

    /// <summary>How long to wait after pressing Commence before calling it a failure.</summary>
    private const long CommenceTimeoutMs = 20_000;

    /// <summary>UI work is paced rather than run at frame rate; the game needs time to build.</summary>
    private const long StepMs = 250;

    private long _startedMs;
    private long _phaseSinceMs;
    private long _lastStepMs;

    /// <summary>
    /// Hard ceiling on one attempt, independent of the per-phase budgets.
    ///
    /// The per-phase clock is deliberately restarted by a window reopen, so without an overall cap
    /// a window that keeps closing could keep the attempt alive indefinitely. Registration plus
    /// preparation is about three minutes, so five is generous and still terminates.
    /// </summary>
    private const long AttemptTimeoutMs = 300_000;
    private bool _showRequested;
    private int _clicks;
    private int _reopens;
    private long _clickSettleUntilMs;

    /// <summary>
    /// How long to stop clicking after a click, while the game updates the button.
    ///
    /// THIS IS A SAFETY INTERLOCK, NOT A COSMETIC PAUSE. It is one button whose label changes,
    /// and the label lags the press by a frame or two - so a second click fired into that gap
    /// lands on the SAME button now meaning Withdraw, and cancels the registration we just made.
    /// The flow would then look like it was trying repeatedly and getting nowhere, which is
    /// indistinguishable from the bug this class was rewritten to fix.
    /// </summary>
    private const long ClickSettleMs = 1500;

    private long _lapsedSinceMs;
    private long _readySinceMs;

    /// <summary>How long the window must have been loaded and visible before anything is clicked.</summary>
    private const long WindowSettleMs = 600;

    /// <summary>How long "Register is back, Withdraw is gone" must hold before it is believed.</summary>
    private const long LapsedConfirmMs = 3000;

    /// <summary>How many times the window may be reopened during one attempt. See the reopen path.</summary>
    private const int MaxReopens = 8;

    /// <summary>The engagement we are signing up FOR, latched by id rather than by list slot.</summary>
    private ushort _targetEventId;

    public bool Active { get; private set; }
    public SignUpPhase Phase { get; private set; } = SignUpPhase.Idle;
    public string Status { get; private set; } = string.Empty;

    /// <summary>
    /// Buttons seen on the last pass, with their labels. Purely diagnostic, and the thing to read
    /// when this does not work: the window's real button labels appear here verbatim.
    /// </summary>
    public IReadOnlyList<string> LastButtons { get; private set; } = [];

    public void Begin()
    {
        Active = true;
        Phase = SignUpPhase.Opening;
        _startedMs = Environment.TickCount64;
        _phaseSinceMs = _startedMs;
        _lastStepMs = 0;
        _showRequested = false;
        _clicks = 0;
        _reopens = 0;
        _clickSettleUntilMs = 0;
        _lapsedSinceMs = 0;
        _readySinceMs = 0;
        _targetEventId = 0;
        _loggedButtons = string.Empty;
        LastButtons = [];
        Status = "Opening the Resistance Recruitment window.";
        Svc.Log.Information("[BozjaBuddyReborn] Sign-up: begin.");
    }

    public void Cancel(string reason = "Sign-up cancelled.")
    {
        if (Active)
            Svc.Log.Information($"[BozjaBuddyReborn] Sign-up: end - {reason}");

        Active = false;
        Phase = SignUpPhase.Idle;
        Status = reason;
    }

    private void Advance(SignUpPhase phase, string status)
    {
        Phase = phase;
        _phaseSinceMs = Environment.TickCount64;

        // RE-ARM THE SHOW. The latch is per phase, not per attempt, and that distinction is the
        // difference between a working Commence and a silent stall: pressing Register frequently
        // closes the window, and the next phase then has to be able to open it again. Latched for
        // the whole attempt (as it was) the runner sat waiting for a window it had decided it had
        // already asked for, and timed out.
        _showRequested = false;

        Status = status;
        Svc.Log.Information($"[BozjaBuddyReborn] Sign-up: -> {phase}: {status}");
    }

    private long PhaseAgeMs => Environment.TickCount64 - _phaseSinceMs;

    /// <summary>Drive one tick. Framework thread only - it touches agents and addons.</summary>
    public unsafe void Tick()
    {
        if (!Active || !Svc.Framework.IsInFrameworkUpdateThread)
            return;

        // Preconditions the old version did not have at all: it would drive agents during a
        // loading screen, from Gangos, or on a box that was already in the engagement.
        if (Svc.Objects.LocalPlayer == null ||
            Svc.Condition[ConditionFlag.BetweenAreas] ||
            Svc.Condition[ConditionFlag.BetweenAreas51] ||
            Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent])
            return; // transient - wait it out rather than failing

        if (!FieldState.InFieldZone)
        {
            Cancel($"Not in a Bozja field zone (in {BozjaZones.Name(Svc.ClientState.TerritoryType)}).");
            return;
        }

        // NOT a "you are already in one, stop" test any more, and making it one broke the second
        // half of the flow outright.
        //
        // RegisteredEventId is DynamicEventContainer.CurrentEventIndex >= 0, and that goes true
        // when you REGISTER, not when you are deployed - the container starts naming the
        // engagement as soon as you are associated with it. So on the very first tick after a
        // successful Register press this cancelled the attempt with "Already in engagement #N",
        // which is why sign-up worked and Commence never happened. The container is still the
        // right success signal; it is simply not a precondition once we are underway. It is
        // checked once, at Begin, and after that only as the outcome of Commencing.

        var now = Environment.TickCount64;

        if (now - _startedMs > AttemptTimeoutMs)
        {
            Cancel($"Sign-up gave up after {AttemptTimeoutMs / 1000}s in phase {Phase}. Buttons: {ButtonList()}.");
            return;
        }

        if (now - _lastStepMs < StepMs)
            return;
        _lastStepMs = now;

        try
        {
            // A confirmation, if the game raises one, is the last step of whichever click we just
            // made - so it is answered before anything else.
            if (AnswerConfirmation())
                return;

            Step();
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[BozjaBuddyReborn] Sign-up failed.");
            Cancel($"Sign-up failed: {ex.Message}");
        }
    }

    /// <summary>Prompts that belong to this flow, matched case-insensitively as substrings.</summary>
    private static readonly string[] ConfirmationPhrases =
        ["critical engagement", "deployment", "deploy", "register", "commence"];

    /// <summary>
    /// Answer a Yes/No prompt raised by one of our own clicks.
    ///
    /// DELIBERATELY NARROW, because the previous version of this was the worst thing in the file:
    /// it took the FIRST Yes/No addon it could find, anywhere, on the very first tick of the
    /// attempt - before the agent was even fetched and before anything had been clicked - clicked
    /// Yes without ever reading the prompt, force-enabling the button through its own disabled
    /// guard, and then reported "Signed up and confirmed". Pressing "sign up all" with any
    /// unrelated prompt on screen therefore answered it, on every box at once.
    ///
    /// So: only after one of our clicks has actually gone out, only for a prompt whose text is
    /// about this flow, and never through a disabled button.
    /// </summary>
    private unsafe bool AnswerConfirmation()
    {
        if (_clicks == 0)
            return false;

        foreach (var yesno in ECommons.UIHelpers.AddonFinder.YesNo)
        {
            string text;
            try { text = yesno.Text ?? string.Empty; }
            catch { continue; }

            if (text.Length == 0)
                continue;

            var lowered = text.ToLowerInvariant();
            var mine = false;
            foreach (var phrase in ConfirmationPhrases)
            {
                if (lowered.Contains(phrase, StringComparison.Ordinal))
                {
                    mine = true;
                    break;
                }
            }

            if (!mine)
            {
                Svc.Log.Information($"[BozjaBuddyReborn] Sign-up: leaving an unrelated prompt alone: \"{text}\".");
                continue;
            }

            Svc.Log.Information($"[BozjaBuddyReborn] Sign-up: confirming \"{text}\".");
            yesno.RespectDisabledButtons = true;
            yesno.Yes();
            return true;
        }

        return false;
    }

    private unsafe void Step()
    {
        var agentModule = AgentModule.Instance();
        if (agentModule == null)
            return; // UI not up yet; transient

        var agent = (AgentMycBattleAreaInfo*)agentModule->GetAgentByInternalId(RecruitmentAgent);
        if (agent == null)
        {
            Cancel("The Resistance Recruitment agent is unavailable on this box.");
            return;
        }

        var iface = (AgentInterface*)agent;

        // ONE Show, LATCHED. The old code called Show() on every 350ms step where the agent was
        // not yet active, with no latch and no cap - so a window that failed to come up, or one
        // that a bad callback closed again, produced a Show roughly three times a second for the
        // whole attempt. That is the reported "it just spams the window".
        if (!iface->IsAgentActive())
        {
            if (!_showRequested)
            {
                _showRequested = true;
                iface->Show();
                Status = "Opening the Resistance Recruitment window.";
                Svc.Log.Information("[BozjaBuddyReborn] Sign-up: asked the agent to show the window.");
                return;
            }

            // A window that has closed under us mid-flow is normal - pressing Register can close
            // it, and so can the player. Re-ask on a slow cadence rather than failing, because
            // the Commence press has to happen at that window whether or not anything closed it.
            // Capped so a window that genuinely cannot open still ends the attempt.
            if (PhaseAgeMs > OpenTimeoutMs)
            {
                if (Phase is SignUpPhase.AwaitingSelection && _reopens < MaxReopens)
                {
                    _reopens++;
                    _showRequested = false;
                    _phaseSinceMs = Environment.TickCount64;
                    Svc.Log.Information(
                        $"[BozjaBuddyReborn] Sign-up: window closed while awaiting the draw - reopening ({_reopens}).");
                    return;
                }

                Cancel("The Resistance Recruitment window did not open.");
            }

            return;
        }

        // The agent knows its own addon id, so the addon never has to be looked up by a name we
        // are not sure of. ("MYCBattleAreaInfo" was taken from the game's shipped ULD filename
        // list, which is not the same thing as a runtime addon name.)
        var addonId = iface->GetAddonId();
        var atkManager = RaptureAtkUnitManager.Instance();
        var addon = addonId == 0 || atkManager == null
            ? null
            : atkManager->GetAddonById((ushort)addonId);

        if (addon == null || !addon->IsVisible || addon->UldManager.LoadedState != AtkLoadState.Loaded)
        {
            _readySinceMs = 0;

            if (PhaseAgeMs > OpenTimeoutMs)
                Cancel("The Resistance Recruitment window did not finish loading.");
            else
                Status = "Waiting for the Resistance Recruitment window.";

            return;
        }

        // LET IT SETTLE BEFORE TOUCHING IT. "Loaded and visible" is not the same as "the rows have
        // been populated": the crash was reported while switching from one engagement to another,
        // which rebuilds the list, and a button can exist and read as enabled before the game has
        // attached its event. Waiting a beat after the window becomes ready costs nothing against
        // a phase measured in minutes.
        if (_readySinceMs == 0)
            _readySinceMs = Environment.TickCount64;

        if (Environment.TickCount64 - _readySinceMs < WindowSettleMs)
        {
            Status = "Waiting for the recruitment list to populate.";
            return;
        }

        var buttons = CollectButtons(addon);
        LastButtons = Describe(buttons);
        LogButtonsIfChanged();

        switch (Phase)
        {
            case SignUpPhase.Opening:
                Advance(SignUpPhase.Registering, "Looking for the Register button.");
                goto case SignUpPhase.Registering;

            case SignUpPhase.Registering:
                StepRegister(addon, buttons);
                break;

            case SignUpPhase.AwaitingSelection:
                StepAwaitSelection(addon, buttons);
                break;

            case SignUpPhase.Commencing:
                StepCommencing(buttons);
                break;
        }
    }

    private bool Settling => Environment.TickCount64 < _clickSettleUntilMs;

    private unsafe void StepRegister(AtkUnitBase* addon, List<LabelledButton> buttons)
    {
        if (Settling)
        {
            Status = "Waiting for the button to update.";
            return;
        }

        // Selected already (we were registered before this attempt started, and the lottery has
        // run): skip straight to the second click.
        if (Find(buttons, CommenceLabels) is { } commence)
        {
            // Only advance on a press that actually went out. A refused click means the row is
            // not wired up yet, and advancing anyway would leave the flow waiting on a button
            // nobody ever pressed.
            if (Click(addon, commence, "Commence"))
                Advance(SignUpPhase.Commencing, "Commencing - waiting to be deployed.");

            return;
        }

        // Withdraw means the button has already been pressed once: we are in the lottery.
        if (Find(buttons, WithdrawLabels) is not null)
        {
            Advance(SignUpPhase.AwaitingSelection, "Already registered - waiting for the draw.");
            return;
        }

        if (Find(buttons, RegisterLabels) is { } register)
        {
            _targetEventId = FirstRegisteringEventId();

            if (Click(addon, register, "Register"))
                Advance(SignUpPhase.AwaitingSelection, "Registered - waiting for the draw.");

            return;
        }

        // Nothing pressable. Say WHY, using the engagement list rather than guessing - "no
        // engagement is recruiting" and "the window has no button we recognise" are completely
        // different problems and the old code reported them identically.
        if (PhaseAgeMs <= OpenTimeoutMs)
        {
            Status = "Waiting for a Register button.";
            return;
        }

        Cancel(AnyRegistering()
            ? $"An engagement is recruiting but no Register button was found. Buttons on the window: {ButtonList()}."
            : "No Critical Engagement is currently recruiting.");
    }

    private unsafe void StepAwaitSelection(AtkUnitBase* addon, List<LabelledButton> buttons)
    {
        if (Settling)
        {
            Status = "Waiting for the button to update.";
            return;
        }

        if (Find(buttons, CommenceLabels) is { } commence)
        {
            if (Click(addon, commence, "Commence"))
                Advance(SignUpPhase.Commencing, "Commencing - waiting to be deployed.");

            return;
        }

        if (PhaseAgeMs > SelectionTimeoutMs)
        {
            Cancel("Registered, but no Commence appeared - the lottery did not pick this box.");
            return;
        }

        // Losing the Withdraw button without gaining a Commence means the registration lapsed and
        // a fresh one is open. Required to PERSIST before acting: the label lags a press, so a
        // momentary "Register" straight after our own click is the button not having caught up,
        // and re-pressing it there would withdraw us.
        if (Find(buttons, WithdrawLabels) is null && Find(buttons, RegisterLabels) is not null)
        {
            if (_lapsedSinceMs == 0)
                _lapsedSinceMs = Environment.TickCount64;

            if (Environment.TickCount64 - _lapsedSinceMs > LapsedConfirmMs)
            {
                _lapsedSinceMs = 0;
                Advance(SignUpPhase.Registering, "Registration lapsed - trying again.");
            }

            return;
        }

        _lapsedSinceMs = 0;

        Status = $"Registered - waiting for the draw ({PhaseAgeMs / 1000}s).";
    }

    private void StepCommencing(List<LabelledButton> buttons)
    {
        // NOT "are we registered" - that was already true before the Commence press (the container
        // starts naming the engagement at registration), so testing it here would report success
        // for a press that did nothing at all. The press landing is observable directly: the
        // Commence button goes away.
        if (Find(buttons, CommenceLabels) is null)
        {
            var id = CriticalEngagements.RegisteredEventId;
            Cancel(id is { } joined
                ? $"Commenced - deploying to engagement #{joined}."
                : "Commenced.");
            Phase = SignUpPhase.Done;
            return;
        }

        // Already fighting it: unambiguous, and covers the case where the window never updated.
        if (CriticalEngagements.Current(null) is { IsRunning: true } running)
        {
            Cancel($"Deployed to engagement #{running.EventId} - it is under way.");
            Phase = SignUpPhase.Done;
            return;
        }

        if (PhaseAgeMs > CommenceTimeoutMs)
        {
            Cancel($"Commence was pressed but the button is still there. Buttons: {ButtonList()}.");
            return;
        }

        Status = "Commencing - waiting to be deployed.";
    }

    // ------------------------------------------------------------------ buttons

    /// <summary>English labels for the one button whose text changes through the flow.</summary>
    private static readonly string[] RegisterLabels = ["register", "request deployment", "deploy"];
    private static readonly string[] WithdrawLabels = ["withdraw", "cancel deployment", "cancel"];

    /// <summary>
    /// Labels the second-phase button can carry. Wider than Register's on purpose: this is the
    /// press that was reported not happening, and a label this does not recognise is
    /// indistinguishable from no button at all - so the list is generous and every button seen is
    /// logged whenever it changes, which is what turns "it did not commence" into a name to add.
    /// </summary>
    private static readonly string[] CommenceLabels =
        ["commence", "enter", "join", "deploy now", "proceed", "begin", "start"];

    private readonly record struct LabelledButton(nint Button, string Text);

    private static LabelledButton? Find(List<LabelledButton> buttons, string[] labels)
    {
        foreach (var b in buttons)
        {
            var text = b.Text.Trim().ToLowerInvariant();
            if (text.Length == 0)
                continue;

            foreach (var label in labels)
                if (text == label || text.StartsWith(label, StringComparison.Ordinal))
                    return b;
        }

        return null;
    }

    /// <summary>
    /// Press a button in the recruitment window.
    ///
    /// THIS CRASHED THE CLIENT IN 1.0.26.0/1.0.27.0 AND THE REASON IS WORTH KEEPING. It used
    /// ECommons' convenience overload, whose whole body is:
    ///
    ///     addon-&gt;ReceiveEvent(evt-&gt;State.EventType, (int)evt-&gt;Param, btnRes.AtkEventManager.Event);
    ///
    /// AtkUnitBase.ReceiveEvent takes a FOURTH parameter, AtkEventData*, and that call leaves it
    /// null. Most addons never touch it, which is why that overload works everywhere else in
    /// ECommons - but AddonMYCBattleAreaInfo.ReceiveEvent dereferences it. The crash dump is
    /// unambiguous: an access violation reading address 6 inside
    /// Client::UI::AddonMYCBattleAreaInfo.ReceiveEvent+0x5E, i.e. a null pointer plus a small
    /// field offset, with ClickAddonButton directly above it on the stack. Registering had worked
    /// before because the code path taken inside ReceiveEvent depends on the event type and
    /// param; switching engagements took a path that reads the event data.
    ///
    /// So the press now goes through the full component path, which is what a real click does:
    /// a constructed AtkEvent naming the button as target and the addon as listener, plus a real
    /// (empty) input-data buffer instead of a null. Every pointer between here and there is
    /// checked first - an access violation is a process kill, not an exception, so a try/catch
    /// around this would catch nothing. Guarding has to be preventive.
    /// </summary>
    private unsafe bool Click(AtkUnitBase* addon, LabelledButton target, string what)
    {
        if (addon == null)
            return false;

        var button = (AtkComponentButton*)target.Button;
        if (button == null)
            return false;

        var ownerNode = button->AtkComponentBase.OwnerNode;
        if (ownerNode == null)
        {
            Svc.Log.Warning($"[BozjaBuddyReborn] Sign-up: \"{target.Text}\" has no owner node; not clicking.");
            return false;
        }

        // The button's own attached event tells us which event the game expects. No event means
        // the row is not wired up yet - which happens while the list rebuilds, exactly the moment
        // the crash was reported in.
        var evt = ownerNode->AtkResNode.AtkEventManager.Event;
        if (evt == null)
        {
            Svc.Log.Warning($"[BozjaBuddyReborn] Sign-up: \"{target.Text}\" has no attached event yet; not clicking.");
            return false;
        }

        // Prefer an explicit click event if the chain carries one. Bounded, because a corrupt or
        // circular chain must not hang the framework thread.
        var chosen = evt;
        var node = evt;
        for (var i = 0; i < 16 && node != null; i++)
        {
            var t = node->State.EventType;
            if (t is AtkEventType.MouseClick or AtkEventType.ButtonClick)
            {
                chosen = node;
                break;
            }

            node = node->NextEvent;
        }

        _clicks++;
        _clickSettleUntilMs = Environment.TickCount64 + ClickSettleMs;
        Svc.Log.Information(
            $"[BozjaBuddyReborn] Sign-up: clicking \"{target.Text}\" as {what} " +
            $"(click {_clicks}, event type {chosen->State.EventType}, param {chosen->Param}).");

        using var eventData = EventData.ForNormalTarget(ownerNode, addon);
        using var inputData = InputData.Empty();

        ClickHelper.InvokeReceiveEvent(
            &addon->AtkEventListener,
            (EventType)chosen->State.EventType,
            chosen->Param,
            eventData,
            inputData);

        return true;
    }

    /// <summary>
    /// Every enabled, visible button in the window, with its label.
    ///
    /// Walks into component nodes as well as the top-level list, because the rows of a list addon
    /// are components in their own right and their buttons do not appear in the addon's own node
    /// list.
    /// </summary>
    private static unsafe List<LabelledButton> CollectButtons(AtkUnitBase* addon)
    {
        var found = new List<LabelledButton>();
        if (addon == null)
            return found;

        var mgr = &addon->UldManager;
        Walk(mgr, found, 0);
        return found;

        static void Walk(AtkUldManager* mgr, List<LabelledButton> found, int depth)
        {
            if (mgr == null || depth > 6 || found.Count > 64)
                return;

            // A manager that has not finished loading has a node list that is still being built,
            // so walking it reads half-initialised entries. This is the same class of hazard as
            // the click crash: preventive checks only, because an access violation here kills the
            // client rather than raising something catchable.
            if (mgr->LoadedState != AtkLoadState.Loaded || mgr->NodeList == null)
                return;

            var count = mgr->NodeListCount;
            for (var i = 0; i < count; i++)
            {
                var node = mgr->NodeList[i];
                if (node == null || !node->IsVisible())
                    continue;

                // Component nodes are typed at 1000 and above; anything below is a leaf.
                if ((ushort)node->Type < 1000)
                    continue;

                var component = ((AtkComponentNode*)node)->Component;
                if (component == null)
                    continue;

                if (component->GetComponentType() == ComponentType.Button)
                {
                    var button = (AtkComponentButton*)component;
                    if (button->IsEnabled)
                        found.Add(new LabelledButton((nint)button, ReadText(button)));
                }

                Walk(&component->UldManager, found, depth + 1);
            }
        }

        static string ReadText(AtkComponentButton* button)
        {
            try
            {
                var text = button->ButtonTextNode;
                return text == null ? string.Empty : text->NodeText.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    private static List<string> Describe(List<LabelledButton> buttons)
    {
        var labels = new List<string>(buttons.Count);
        foreach (var b in buttons)
            if (b.Text.Length > 0)
                labels.Add(b.Text);
        return labels;
    }

    private string ButtonList() => LastButtons.Count == 0 ? "none" : string.Join(", ", LastButtons);

    private string _loggedButtons = string.Empty;

    /// <summary>
    /// Log the window's button labels whenever the set changes.
    ///
    /// This is the record that answers "why did it not commence" without another round trip: if
    /// the second-phase button is present under a label not in CommenceLabels - a different
    /// wording, or any non-English client - it appears here verbatim at the moment it appears.
    /// Logged on change rather than per tick so it stays readable.
    /// </summary>
    private void LogButtonsIfChanged()
    {
        var current = ButtonList();
        if (current == _loggedButtons)
            return;

        _loggedButtons = current;
        Svc.Log.Information($"[BozjaBuddyReborn] Sign-up [{Phase}]: window buttons now: {current}");
    }

    // ------------------------------------------------------------- engagements

    /// <summary>Is any engagement in this zone actually taking names right now?</summary>
    private static bool AnyRegistering() => FirstRegisteringEventId() != 0;

    private static ushort FirstRegisteringEventId()
    {
        foreach (var ce in CriticalEngagements.Read(null))
            if (ce.IsJoinable)
                return ce.EventId;

        return 0;
    }
}
