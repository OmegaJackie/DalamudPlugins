using System.Collections.Generic;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;

namespace BozjaBuddyReborn.Game;

/// <summary>
/// One Critical Engagement as read from the live DynamicEventContainer, snapshotted into
/// managed memory so the UI and the controller can hold it across frames.
/// </summary>
public readonly record struct CeSnapshot(
    int Index,
    ushort EventId,
    DynamicEventState State,
    byte Progress,
    byte Participants,
    byte MaxParticipants,
    uint SecondsLeft,
    uint SecondsDuration,
    Vector3 Position,
    float Radius,
    string Name,
    bool IsDuel)
{
    /// <summary>
    /// Registration is open, i.e. this is the phase in which a Register press is accepted.
    ///
    /// IT DOES NOT MEAN WALKING IN JOINS YOU, which is what this said and what the travel
    /// pipeline still assumes. The Patch 5.35 notes say the opposite: critical engagements "do
    /// not require you to be present in the field to participate. Instead, players must request
    /// deployment via the Resistance Recruitment window." Enrolling is Register, then Commence
    /// once the lottery picks you - see SignUpRunner. Left as-is pending one live check (stand in
    /// a circle through the whole Register phase without touching the window and see whether you
    /// are pulled in); if walking in really is inert, the travel-to-CE machinery is solving a
    /// problem that does not exist.
    /// </summary>
    public bool IsJoinable => State == DynamicEventState.Register;

    /// <summary>Warmup or battle - the engagement has started.</summary>
    public bool IsRunning => State is DynamicEventState.Warmup or DynamicEventState.Battle;

    /// <summary>Any non-Inactive state, i.e. the engagement exists on the field right now.</summary>
    public bool IsLive => State != DynamicEventState.Inactive;

    /// <summary>A usable world position was published for this engagement.</summary>
    public bool HasPosition => Position != Vector3.Zero;

    public string StateText => State switch
    {
        DynamicEventState.Inactive => "Inactive",
        DynamicEventState.Register => "Registering",
        DynamicEventState.Warmup => "Warmup",
        DynamicEventState.Battle => "In battle",
        _ => State.ToString(),
    };
}

/// <summary>
/// Reads the zone's Critical Engagement state out of DynamicEventContainer.
///
/// Verified layout (FFXIVClientStructs InstanceContent/DynamicEventContainer.cs):
///   DynamicEventContainer.GetInstance()          - the container for the current zone
///   .Events                                      - fixed 16-entry DynamicEvent array
///   .CurrentEventIndex / .CurrentEventId         - the engagement the player has joined
///   .GetCurrentEvent()                           - pointer to that engagement
///   DynamicEvent.State                           - Inactive / Register / Warmup / Battle
///   DynamicEvent.MapMarker.Position / .Radius    - world-space centre + radius
///
/// The container only ever holds the current zone's events, so no zone filtering is needed.
/// Framework thread only.
/// </summary>
public static unsafe class CriticalEngagements
{
    /// <summary>Fixed size of DynamicEventContainer.Events.</summary>
    public const int MaxEvents = 16;

    private static DynamicEventContainer* Container()
    {
        try { return DynamicEventContainer.GetInstance(); }
        catch { return null; }
    }

    /// <summary>True when this zone publishes a Critical Engagement container at all.</summary>
    public static bool Available => Container() != null;

    /// <summary>
    /// Snapshot every engagement slot the zone currently publishes. Inactive slots are
    /// included so the UI can show the full roster; filter on <see cref="CeSnapshot.IsLive"/>.
    /// </summary>
    /// <param name="catalog">
    /// Sheet-backed names and duel flags. Optional: callers that only need ids and states (the
    /// sign-up runner asking "is anything recruiting") should not have to hold a catalog.
    /// </param>
    public static List<CeSnapshot> Read(CeCatalog? catalog)
    {
        var result = new List<CeSnapshot>(MaxEvents);
        var container = Container();
        if (container == null)
            return result;

        var events = container->Events;
        for (var i = 0; i < events.Length && i < MaxEvents; i++)
        {
            ref var ev = ref events[i];
            var id = ev.DynamicEventId;
            if (id == 0)
                continue;

            // Prefer the Lumina sheet name: it is stable managed data, whereas the live
            // Utf8String is only populated once the client has streamed the event in.
            var name = catalog?.Name(id) ?? string.Empty;

            result.Add(new CeSnapshot(
                Index: i,
                EventId: id,
                State: ev.State,
                Progress: ev.Progress,
                Participants: ev.Participants,
                MaxParticipants: ev.MaxParticipants,
                SecondsLeft: ev.SecondsLeft,
                SecondsDuration: ev.SecondsDuration,
                Position: ev.MapMarker.Position,
                Radius: ev.MapMarker.Radius,
                Name: name,
                IsDuel: catalog?.IsDuel(id) ?? false));
        }

        return result;
    }

    /// <summary>
    /// The engagement the player is currently registered to, or null.
    /// Reads the container's own current-event pointer rather than inferring from state,
    /// so a Battle-phase engagement the player did NOT join is never mistaken for ours.
    /// </summary>
    /// <param name="catalog">Optional; see <see cref="Read"/>.</param>
    public static CeSnapshot? Current(CeCatalog? catalog)
    {
        var container = Container();
        if (container == null)
            return null;

        DynamicEvent* ev;
        try { ev = container->GetCurrentEvent(); }
        catch { return null; }

        if (ev == null)
            return null;

        var id = ev->DynamicEventId;
        if (id == 0)
            return null;

        return new CeSnapshot(
            Index: container->CurrentEventIndex,
            EventId: id,
            State: ev->State,
            Progress: ev->Progress,
            Participants: ev->Participants,
            MaxParticipants: ev->MaxParticipants,
            SecondsLeft: ev->SecondsLeft,
            SecondsDuration: ev->SecondsDuration,
            Position: ev->MapMarker.Position,
            Radius: ev->MapMarker.Radius,
            Name: catalog?.Name(id) ?? string.Empty,
            IsDuel: catalog?.IsDuel(id) ?? false);
    }

    /// <summary>True when the player is registered to any engagement.</summary>
    public static bool IsRegistered
    {
        get
        {
            var container = Container();
            return container != null && container->CurrentEventIndex >= 0;
        }
    }

    /// <summary>
    /// The id of the engagement this character is actually part of, or null.
    ///
    /// The container's own current-event id, so it answers "am I in one" without a catalog and
    /// without inferring anything from an engagement's public state - which advances on the
    /// game's timer whether or not this character did anything, and is why the sign-up runner
    /// used to report success for having done nothing.
    /// </summary>
    public static ushort? RegisteredEventId
    {
        get
        {
            var container = Container();
            if (container == null || container->CurrentEventIndex < 0)
                return null;

            var id = container->CurrentEventId;
            return id == 0 ? null : id;
        }
    }
}
