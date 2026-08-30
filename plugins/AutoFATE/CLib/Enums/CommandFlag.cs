namespace AutoFATE.CLib.Enums;

/// <summary>
/// Command ids for <see cref="FFXIVClientStructs.FFXIV.Client.Game.GameMain.ExecuteCommand"/>
/// </summary>
// straight copy of https://github.com/AtmoOmen/OmenTools/blob/main/Info/Game/Enums/ExecuteCommandFlag.cs
public enum CommandFlag {

    /// <summary>
    /// Draw or sheathe weapon
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: 1 - Draw, 0 - Sheathe</para>
    /// <para><c>param2</c>: Unknown, fixed at 1</para>
    /// </remarks>
    ToggleWeapon = 1,

    /// <summary>
    /// Auto attack
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Whether to enable auto attack (0 - No, 1 - Yes)</para>
    /// <para><c>param2</c>: Target object ID</para>
    /// <para><c>param3</c>: Whether it is manual operation (0 - No, 1 - Yes)</para>
    /// </remarks>
    ToggleAutoAttack = 2,

    /// <summary>
    /// Select target
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Target Entity ID (no target: 0xE0000000)</para>
    /// </remarks>
    Target = 3,

    /// <summary>
    /// PVP quick chat
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: QuickChat Row ID</para>
    /// <para><c>param2</c>: Parameter 1</para>
    /// <para><c>param3</c>: Parameter 2</para>
    /// </remarks>
    SnedPVPQuickChat = 5,

    /// <summary>
    /// GM command
    /// </summary>
    GMCommand11 = 11,

    /// <summary>
    /// Dismount
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: 0 - Do not enter queue; 1 - Enter queue</para>
    /// </remarks>
    Dismount = 101,

    /// <summary>
    /// Summon pet
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Pet ID</para>
    /// </remarks>
    SummonMinion = 102,

    /// <summary>
    /// Withdraw pet
    /// </summary>
    WithdrawMinion = 103,

    /// <summary>
    /// Remove specified status effect from self
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Status ID</para>
    /// <para><c>param3</c>: Status source GameObjectID, or use 0xE0000000 to remove the first status of this type from any source</para>
    /// </remarks>
    RemoveStatus = 104,

    /// <summary>
    /// Cancel cast
    /// </summary>
    CancelCast = 105,

    /// <summary>
    /// Ride pillion
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Target ID</para>
    /// <para><c>param2</c>: Position index</para>
    /// </remarks>
    RidePillion = 106,

    /// <summary>
    /// Ride pillion ()
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Rider Entity ID</para>
    /// </remarks>
    RidePillionAuto = 107,

    /// <summary>
    /// Load Party Member
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: (0 6)</para>
    ///     <para><c>param2</c>: EntityID</para>
    /// </remarks>
    LoadPartyMember = 108,

    /// <summary>
    /// Withdraw fashion accessory
    /// </summary>
    WithdrawParasolForced = 109,

    /// <summary>
    /// Withdraw fashion accessory
    /// </summary>
    WithdrawParasol = 110,

    /// <summary>
    /// Update Parasol State
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: ID (0 )</para>
    /// </remarks>
    UpdateParasolState = 111,

    /// <summary>
    /// Set Parasol To Auto Use
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: ID (0 )</para>
    /// </remarks>
    SetParasolToAutoUse = 112,

    /// <summary>
    /// Revive
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Operation (5 - Accept revive; 8 - Confirm return to return point)</para>
    /// </remarks>
    Revive = 200,

    /// <summary>
    /// Territory transport
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Transport method</para>
    /// <list type="table">
    ///     <item>
    ///         <term>1</term>
    ///         <description>NPC teleport</description>
    ///     </item>
    ///     <item>
    ///         <term>3</term>
    ///         <description>Boundary transition</description>
    ///     </item>
    ///     <item>
    ///         <term>4</term>
    ///         <description>Normal teleport</description>
    ///     </item>
    ///     <item>
    ///         <term>7</term>
    ///         <description>Return</description>
    ///     </item>
    ///     <item>
    ///         <term>15</term>
    ///         <description>City aetheryte</description>
    ///     </item>
    ///     <item>
    ///         <term>20</term>
    ///         <description>Housing area</description>
    ///     </item>
    /// </list>
    /// <para><c>param2</c>: Position change method within territory</para>
    /// <list type="table">
    ///     <item>
    ///         <term>1</term>
    ///         <description>Story transition</description>
    ///     </item>
    ///     <item>
    ///         <term>2</term>
    ///         <description>Return to safe area</description>
    ///     </item>
    ///     <item>
    ///         <term>25</term>
    ///         <description>Duty interior transition</description>
    ///     </item>
    ///     <item>
    ///         <term>26</term>
    ///         <description>Dive</description>
    ///     </item>
    /// </list>
    /// </remarks>
    StartTerritoryTransport = 201,

    /// <summary>
    /// Teleport to specified aetheryte
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Aetheryte ID</para>
    /// <para><c>param2</c>: Whether to use teleport ticket (0 - No, 1 - Yes)</para>
    /// <para><c>param3</c>: Aetheryte Sub ID</para>
    /// </remarks>
    Teleport = 202,

    /// <summary>
    /// Accept teleport offer
    /// </summary>
    AcceptTeleportOffer = 203,

    /// <summary>
    /// Cancel Teleport
    /// </summary>
    CancelTeleport = 204,

    /// <summary>
    /// Reject Revive
    /// </summary>
    RejectRevive = 205,

    /// <summary>
    /// Unknown
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Event Kind</para>
    ///     <para><c>param2</c>: Unknown</para>
    /// </remarks>
    PublicContentCommand206 = 206,

    /// <summary>
    /// Unknown
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown parameter high 32 bits</para>
    ///     <para><c>param2</c>: Unknown parameter</para>
    ///     <para><c>param3</c>: Unknown parameter</para>
    /// </remarks>
    TeleportCommand207 = 207,

    /// <summary>
    /// Request friend house teleport information
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Unknown</para>
    /// <para><c>param2</c>: Unknown</para>
    /// </remarks>
    RequestFriendHousingTeleportInfo = 210,

    /// <summary>
    /// Teleport to friend house
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Unknown</para>
    /// <para><c>param2</c>: Unknown</para>
    /// <para><c>param3</c>: Aetheryte ID (Personal house - 61, Free company house - 57)</para>
    /// <para><c>param4</c>: Aetheryte Sub ID (appears to be fixed at 1)</para>
    /// </remarks>
    TeleportToFriendHouse = 211,

    /// <summary>
    /// If not Lalafell, return to the nearest safe point in the current area
    /// </summary>
    ReturnToSafePointIfNotLalafell = 213,

    /// <summary>
    /// Return to the nearest safe point on the current map if current race is not Lalafell
    /// </summary>
    ReturnIfNotLalafell = 214,

    /// <summary>
    /// Alias for <see cref="ReturnIfNotLalafell"/> (legacy clib name).
    /// </summary>
    InstantReturn = ReturnIfNotLalafell,

    /// <summary>
    /// Inspect specified player
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Target object ID</para>
    /// </remarks>
    Inspect = 300,

    /// <summary>
    /// Change equipped title
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Title ID</para>
    /// </remarks>
    ChangeTitle = 302,

    /// <summary>
    /// Request title data
    /// </summary>
    RequestTitles = 303,

    /// <summary>
    /// Mark a HowTo as seen
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: HowTo ID</para>
    /// </remarks>
    MarkHowToSeen = 306,

    /// <summary>
    /// Request cutscene data
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Cutscene index in Cutscene.csv</para>
    /// </remarks>
    RequestCutsceneInfo307 = 307,

    /// <summary>
    /// Request challenge log data for specific category
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Category index (starts from 1)</para>
    /// </remarks>
    RequestContentsNoteCategory = 310,

    /// <summary>
    /// Unknown ()
    /// </summary>
    UnknownCommand312 = 312,

    /// <summary>
    /// Clear field markers
    /// </summary>
    ClearFieldMarkers = 313,

    /// <summary>
    /// AutoChangeCameraMode
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown, 1 0</para>
    ///     <para><c>param2</c>: Unknown</para>
    /// </remarks>
    AutoChangeCameraModeCommand314 = 314,

    /// <summary>
    /// Assign or swap Blue Mage action to active slot
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Type (0 - Assign to active slot, 1 - Swap active slot)</para>
    /// <para><c>param2</c>: Slot index (starts from 0, less than 24)</para>
    /// <para><c>param3</c>: Action ID / Slot index (starts from 0, less than 24)</para>
    /// </remarks>
    SetBlueAction = 315,

    /// <summary>
    /// Request world travel data
    /// </summary>
    RequestWorldTravel = 316,

    /// <summary>
    /// Place field marker
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Marker index</para>
    /// <para><c>param2</c>: Coordinate X * 1000</para>
    /// <para><c>param3</c>: Coordinate Y * 1000</para>
    /// <para><c>param4</c>: Coordinate Z * 1000</para>
    /// </remarks>
    PlaceFieldMarker = 317,

    /// <summary>
    /// Remove field marker
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Marker index</para>
    /// </remarks>
    RemoveFieldMarker = 318,

    /// <summary>
    /// Reset striking dummy aggro
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Striking dummy Object ID</para>
    /// </remarks>
    ResetStrikingDummy = 319,

    /// <summary>
    /// Set current retainer market item price
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Item slot</para>
    /// <para><c>param2</c>: Item price</para>
    /// </remarks>
    SetRetainerMarketPrice = 400,

    /// <summary>
    /// Request Monster Note
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: (Agent byte MonsterNote)</para>
    ///     <para><c>param2</c></para>
    ///     <para><c>param3</c>: Unknown, Agent 0, Yes 0</para>
    /// </remarks>
    RequestMonsterNote = 401,

    /// <summary>
    /// Clear Reclaim Notification
    /// </summary>
    ClearReclaimNotification = 402,

    /// <summary>
    /// 1.0 NPC
    /// </summary>
    ReclaimItems = 403,

    /// <summary>
    /// Request specified inventory data
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: (int)InventoryType</para>
    /// </remarks>
    RequestInventory = 404,

    /// <summary>
    /// Move Item Between Inventory
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: InventoryType</para>
    ///     <para><c>param2</c>: InventoryType</para>
    /// </remarks>
    MoveItemBetweenInventory = 405,

    /// <summary>
    /// (Yes, , )
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: InventoryType</para>
    ///     <para><c>param2</c>: InventoryType</para>
    /// </remarks>
    NotifyBlockedInventoryOperation = 406,

    /// <summary>
    /// Enter materia attach state
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Item ID</para>
    /// </remarks>
    EnterMateriaAttachState = 407,

    /// <summary>
    /// Finish Materia Attach
    /// </summary>
    FinishMateriaAttach = 408,

    /// <summary>
    /// Leave materia attach state
    /// </summary>
    LeaveMateriaAttachState = 409,

    /// <summary>
    /// Enter materia attach request state
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown, MateriaRequestManager</para>
    /// </remarks>
    EnterMateriaAttachRequestState = 410,

    /// <summary>
    /// Leave Materia Attach Request State
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown, 0 1</para>
    ///     <para><c>param2</c>: Unknown, 0 1</para>
    /// </remarks>
    LeaveMateriaAttachRequestState = 411,

    /// <summary>
    /// Send Materia Attach Request
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: EntityID</para>
    /// </remarks>
    SendMateriaAttachRequest = 412,

    /// <summary>
    /// Toggle Free Company Crest Decal
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: InventoryType</para>
    ///     <para><c>param2</c>: InventorySlot</para>
    ///     <para><c>param3</c>: 0 - , 1</para>
    /// </remarks>
    ToggleFreeCompanyCrestDecal = 414,

    /// <summary>
    /// Toggle Free Company Crest Decal Batch Equipped
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: 0 - , 1</para>
    /// </remarks>
    ToggleFreeCompanyCrestDecalBatchEquipped = 415,

    /// <summary>
    /// Toggle Free Company Crest Decal Batch
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: (5 - ; 6 - )</para>
    ///     <para><c>param2</c>: 0 - , 1</para>
    /// </remarks>
    ToggleFreeCompanyCrestDecalBatch = 416,

    /// <summary>
    /// Cancel Materia Attach Request Forced
    /// </summary>
    CancelMateriaAttachRequestForced = 418,

    /// <summary>
    /// Cancel materia meld request
    /// </summary>
    FinishInventoryOperation = 419,

    /// <summary>
    /// Deposit gil to free company chest
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Amount</para>
    ///     <para>Requires calling <see cref="MoveItemBetweenInventory" /></para>
    /// </remarks>
    DepositFreeCompanyChestGil = 420,

    /// <summary>
    /// Withdraw gil from free company chest
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Amount</para>
    ///     <para>Requires calling <see cref="MoveItemBetweenInventory" /></para>
    /// </remarks>
    WithdrawFreeCompanyChestGil = 421,

    /// <summary>
    /// Request free company chest log
    /// </summary>
    RequestFreeCompanyChestLog = 422,

    /// <summary>
    /// Request armoire data
    /// </summary>
    RequestCabinet = 423,

    /// <summary>
    /// Store item to armoire
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Item index in Cabinet.csv</para>
    /// </remarks>
    StoreToCabinet = 424,

    /// <summary>
    /// Restore item from armoire
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Item index in Cabinet.csv</para>
    /// </remarks>
    RestoreFromCabinet = 425,

    /// <summary>
    /// Unknown armoire command (executing reports <c>cutscene interrupted</c>)
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Cabinet ID</para>
    ///     <para><c>param2</c>: Inventory Type</para>
    ///     <para><c>param3</c>: Inventory Slot</para>
    /// </remarks>
    CabinetCommand426 = 426,

    /// <summary>
    /// Finish armoire data request (sets flag to 1)
    /// </summary>
    FinishCabinetRequest = 427,

    /// <summary>
    /// Accept mob hunt bill
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Index in UI.MobHunt.AvailableMarkId</para>
    ///     <para><c>param2</c>: Mark ID</para>
    /// </remarks>
    AcceptMobHuntBill = 428,

    /// <summary>
    /// Abandon mob hunt bill
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Index in UI.MobHunt.AvailableMarkId</para>
    ///     <para><c>param2</c>: Mark ID</para>
    /// </remarks>
    AbandonMobHuntBill = 429,

    /// <summary>
    /// Extract materia
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Inventory Type</para>
    /// <para><c>param2</c>: Inventory Slot</para>
    /// </remarks>
    ExtractMateria = 437,

    /// <summary>
    /// Retainer
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Inventory Type</para>
    ///     <para><c>param2</c>: Inventory Slot</para>
    ///     <para><c>param3</c>: Inventory Type</para>
    ///     <para><c>param4</c>: Inventory Slot</para>
    /// </remarks>
    CastRetainerGlamour = 438,

    /// <summary>
    /// Unknown
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Inventory Type</para>
    ///     <para><c>param2</c>: Inventory Slot</para>
    /// </remarks>
    MiragePrismCommand439 = 439,

    /// <summary>
    /// Unknown
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Inventory Type</para>
    ///     <para><c>param2</c>: Inventory Slot</para>
    /// </remarks>
    RelicSphereCommand440 = 440,

    /// <summary>
    /// Change gearset
    /// </summary>
    ChangeGearset = 441,

    /// <summary>
    /// Recover Blocked Item
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: InventoryType</para>
    ///     <para><c>param2</c>: InventorySlot</para>
    /// </remarks>
    RecoverBlockedItem = 442,

    /// <summary>
    /// Request saddlebag data
    /// </summary>
    RequestSaddleBag = 444,

    /// <summary>
    /// Request reconstruction buyback item data
    /// </summary>
    RequestEnclaveBuyBack = 445,

    /// <summary>
    /// Request reconstruction buyback item data
    /// </summary>
    FinishRequestEnclaveBuyBack = 446,

    /// <summary>
    /// Unknown
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Context ID ( 0)</para>
    ///     <para><c>param2</c>: Type ( 725)</para>
    /// </remarks>
    InventoryOperationCommand449 = 449,

    /// <summary>
    /// Send repair request
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Target Entity ID</para>
    /// </remarks>
    SendRepairRequest = 450,

    /// <summary>
    /// Finish Repair Request
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown, AgentRepairRequest.Instance()</para>
    ///     <para><c>param2</c>: Unknown, AgentRepairRequest.Instance()</para>
    ///     <para><c>param3</c>: Unknown, AtkValue</para>
    /// </remarks>
    FinishRepairRequest = 451,

    /// <summary>
    /// Start Repair Request
    /// </summary>
    StartRepairRequest = 452,

    /// <summary>
    /// Cancel repair request
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Target Entity ID</para>
    /// </remarks>
    CancelRepairRequest = 453,

    /// <summary>
    /// Confirm Repair Request
    /// </summary>
    ConfirmRepairRequest = 454,

    /// <summary>
    /// Equip Facewear
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Glasses Slot</para>
    ///     <para><c>param2</c>: Glasses ID</para>
    /// </remarks>
    EquipFacewear = 455,

    /// <summary>
    /// Interrupt current emote
    /// </summary>
    InterruptEmote = 502,

    /// <summary>
    /// Interrupt current special emote
    /// </summary>
    InterruptEmoteSpecial = 503,

    /// <summary>
    /// Change idle posture
    /// </summary>
    /// <remarks>
    /// <para><c>param2</c>: Posture index</para>
    /// </remarks>
    SetIdlePosture = 505,

    /// <summary>
    /// Enter idle posture
    /// </summary>
    /// <remarks>
    /// <para><c>param2</c>: Posture index</para>
    /// </remarks>
    EnterIdlePosture = 506,

    /// <summary>
    /// Exit idle posture
    /// </summary>
    ExitIdlePosture = 507,

    /// <summary>
    /// Cleanup Gimmick Jump State 602
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown (Yes)</para>
    /// </remarks>
    CleanupGimmickJumpState602 = 602,

    /// <summary>
    /// Unknown
    /// </summary>
    ControlCommand604 = 604,

    /// <summary>
    /// Unknown
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: X</para>
    ///     <para><c>param2</c>: Y</para>
    ///     <para><c>param3</c>: Z</para>
    ///     <para><c>param4</c>: Character Rotation</para>
    /// </remarks>
    ControlCommand605 = 605,

    /// <summary>
    /// Unknown
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown ()</para>
    /// </remarks>
    ControlCommand606 = 606,

    /// <summary>
    /// Enter swim state (also forces dismount)
    /// </summary>
    EnterSwimState = 608,

    /// <summary>
    /// Leave swim state
    /// </summary>
    LeaveSwimState = 609,

    /// <summary>
    /// Cleanup Gimmick Jump State 611
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown (Yes)</para>
    /// </remarks>
    CleanupGimmickJumpState611 = 611,

    /// <summary>
    /// Cleanup Gimmick Jump State 613
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown (Yes)</para>
    /// </remarks>
    CleanupGimmickJumpState613 = 613,

    /// <summary>
    /// Enable/disable mounting restriction
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: 0 - Disable; 1 - Enable</para>
    /// </remarks>
    DisableMounting = 612,

    /// <summary>
    /// Unknown
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown, 0 1</para>
    /// </remarks>
    ControlCommand614 = 614,

    /// <summary>
    /// Unknown
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: X</para>
    ///     <para><c>param2</c>: Y</para>
    ///     <para><c>param3</c>: Z</para>
    /// </remarks>
    ControlCommand615 = 615,

    /// <summary>
    /// Enter flight state
    /// </summary>
    EnterFlightState = 616,

    /// <summary>
    /// Craft
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Type (0 - Normal craft, 1 - Quick synthesis; 2 - Craft practice)</para>
    /// <para><c>param2</c>: Recipe ID (in Recipe.csv)</para>
    /// <para><c>param3</c>: Additional parameter (Quick synthesis - quantity, max 255)</para>
    /// </remarks>
    Craft = 700,

    /// <summary>
    /// Fish
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Action (0 - Cast, 1 - Reel, 2 - Hook, 4 - Change bait, 5 - Sit, 10 - Powerful hookset, 11 - Precise hookset, 13 - Patience, 14 - Patience II, 24 - Identical cast, 25 - Mooch)
    /// </para>
    /// <para><c>param2</c>: Additional parameter (If changing bait, item ID; If mooching, bait index)</para>
    /// </remarks>
    Fishing = 701,

    /// <summary>
    /// ()
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: FishingNoteInfo ID</para>
    /// </remarks>
    RequestFishingNote = 702,

    /// <summary>
    /// ()
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: FishingNoteInfo ID</para>
    /// </remarks>
    RequestSpearfishNote = 703,

    /// <summary>
    /// Unknown
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown, 1 2</para>
    ///     <para><c>param2</c>: Unknown</para>
    ///     <para><c>param3</c>: Unknown</para>
    /// </remarks>
    QuestCommand704 = 704,

    /// <summary>
    /// Set Last Read Quest
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown, 1</para>
    ///     <para><c>param2</c>: Quest ID (ushort)</para>
    /// </remarks>
    SetLastReadQuest = 705,

    /// <summary>
    /// Request Gathering Point
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: GatheringPoint ID</para>
    /// </remarks>
    RequestGatheringPoint = 706,

    /// <summary>
    /// Mark Gather Division Level Range Seen
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Division Index</para>
    ///     <para><c>param2</c>: LevelRange Index</para>
    /// </remarks>
    MarkGatherDivisionLevelRangeSeen = 708,

    /// <summary>
    /// Exit craft
    /// </summary>
    MarkCraftDivisionLevelRangeSeen = 711,

    /// <summary>
    /// Leave Quick Synthesis
    /// </summary>
    LeaveQuickSynthesis = 712,

    /// <summary>
    /// Unknown
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown</para>
    ///     <para><c>param2</c>: Action ID</para>
    ///     <para><c>param3</c>: Unknown</para>
    /// </remarks>
    SpearFishingCommand713 = 713,

    /// <summary>
    /// Unknown ()
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown</para>
    ///     <para><c>param2</c>: Unknown, 0, 1, 2</para>
    /// </remarks>
    SpearFishingCommand714 = 714,

    /// <summary>
    /// (, )
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown</para>
    ///     <para><c>param2</c>: PerformanceCount 32</para>
    ///     <para><c>param2</c>: PerformanceCount high 32 bits</para>
    /// </remarks>
    MarkSpearFishingActionUsage = 715,

    /// <summary>
    /// Unknown
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown</para>
    /// </remarks>
    SpearFishingCommand716 = 716,

    /// <summary>
    /// Unknown
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown</para>
    ///     <para><c>param2</c>: Unknown</para>
    ///     <para><c>param3</c>: Unknown</para>
    ///     <para><c>param4</c>: Unknown</para>
    /// </remarks>
    SpearFishingCommand717 = 717,

    /// <summary>
    /// Unknown
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown</para>
    /// </remarks>
    SpearFishingCommand718 = 718,

    /// <summary>
    /// Abandon quest
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Quest ID (not RowID)</para>
    /// </remarks>
    AbandonQuest = 800,

    /// <summary>
    /// Refresh leve quest status
    /// </summary>
    RefreshLeveQuest = 801,

    /// <summary>
    /// Abandon leve quest
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Leve quest ID</para>
    /// </remarks>
    AbandonLeveQuest = 802,

    /// <summary>
    /// Mark Leve Ready To Accept
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Leve ID</para>
    /// </remarks>
    MarkLeveReadyToAccept = 803,

    /// <summary>
    /// Start leve quest
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Leve quest ID</para>
    /// <para><c>param2</c>: Level increase amount</para>
    /// </remarks>
    StartLeveQuest = 804,

    /// <summary>
    /// Unknown
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Quest ID</para>
    /// </remarks>
    CompanyLeveQuestCommand = 805,

    /// <summary>
    /// Content related
    /// </summary>
    RequestContent = 808,

    /// <summary>
    /// Start specified FATE
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: FATE ID</para>
    /// <para><c>param2</c>: Target Object ID</para>
    /// </remarks>
    StartFate = 809,

    /// <summary>
    /// Load FATE information
    /// (When switching maps, all FATE information in the map will be loaded at once)
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: FATE ID</para>
    /// </remarks>
    LoadFate = 810,

    /// <summary>
    /// Enter FATE range (This command will not be sent if FATE spawns directly underfoot)
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: FATE ID</para>
    /// </remarks>
    EnterFate = 812,

    /// <summary>
    /// Level sync for FATE
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: FATE ID</para>
    /// <para><c>param2</c>: Whether to level sync (0 - No, 1 - Yes)</para>
    /// </remarks>
    SyncToFateLevel = 813,

    /// <summary>
    /// FATE mob spawn
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Object ID</para>
    /// </remarks>
    LoadFateMob = 814,

    /// <summary>
    /// Territory transport finish
    /// </summary>
    FinishTerritoryTransport = 816,

    /// <summary>
    /// (Yes?)
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown</para>
    ///     <para><c>param2</c>: Unknown, 0 1</para>
    /// </remarks>
    SaveAnimaWeaponQuestGender = 817,

    /// <summary>
    /// Unknown ()
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Festival ID</para>
    ///     <para><c>param2</c>: Unknown</para>
    ///     <para><c>param3</c>: Unknown</para>
    /// </remarks>
    FestivalQuestWorkCommand818 = 818,

    /// <summary>
    /// Leave duty
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Type (0 - Normal exit, 1 - Inactive for a period)</para>
    /// </remarks>
    LeaveDuty = 819,

    /// <summary>
    /// Sync Timezone Offset
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: UTC</para>
    ///     <para><c>param2</c>: Unknown</para>
    ///     <para><c>param3</c>: Unknown</para>
    ///     <para><c>param3</c>: Unknown</para>
    /// </remarks>
    SyncTimezoneOffset = 820,

    /// <summary>
    /// Unknown
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown</para>
    ///     <para><c>param2</c>: Unknown</para>
    /// </remarks>
    QuestRedoCommand821 = 821,

    /// <summary>
    /// Unknown
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown</para>
    /// </remarks>
    QuestRedoCommand822 = 822,

    /// <summary>
    /// Send solo quest battle request
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Difficulty (0 - Normal, 1 - Easy, 2 - Very Easy)</para>
    /// </remarks>
    StartSoloQuestBattle = 823,

    /// <summary>
    /// New Game+ mode
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: New Game+ chapter index in QuestRedo.csv (0 - Exit New Game+)</para>
    /// </remarks>
    QuestRedo = 824,

    /// <summary>
    /// Continue Quest Redo
    /// </summary>
    ContinueQuestRedo = 825,

    /// <summary>
    /// Delete Quest Redo Save
    /// </summary>
    DeleteQuestRedoSave = 826,

    /// <summary>
    /// Reset Quest Redo UI
    /// </summary>
    ResetQuestRedoUI = 827,

    /// <summary>
    /// FATE
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Fate ID</para>
    ///     <para><c>param2</c>: 0 - , 1</para>
    /// </remarks>
    SyncToFateLevelAuto = 828,

    /// <summary>
    /// Unknown
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: FATE ID</para>
    /// </remarks>
    FateCommand829 = 829,

    /// <summary>
    /// Refresh inventory
    /// </summary>
    RefreshInventory = 830,

    /// <summary>
    /// Request cutscene data
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Cutscene index in Cutscene.csv</para>
    /// </remarks>
    RequestCutscene831 = 831,

    /// <summary>
    /// Unknown EventFramework
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown</para>
    /// </remarks>
    EventFrameworkCommand832 = 832,

    /// <summary>
    /// EventTutorial
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown</para>
    ///     <para><c>param2</c>: EventTutorial ID</para>
    /// </remarks>
    MarkEventTutorialSeen = 833,

    /// <summary>
    /// Request achievement progress data
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Achievement index in Achievement.csv</para>
    /// </remarks>
    RequestAchievement = 1000,

    /// <summary>
    /// Request all achievement overview (excluding specific achievement content)
    /// </summary>
    RequestCompletedAchievement = 1001,

    /// <summary>
    /// Request near completion achievement overview (excluding specific achievement content)
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Unknown, fixed at 1</para>
    /// </remarks>
    RequestNearCompletedAchievement = 1002,

    /// <summary>
    /// Unknown ActorControl
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown</para>
    /// </remarks>
    ActorControlCommand1003 = 1003,

    /// <summary>
    /// FATE
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c></para>
    /// </remarks>
    RequestFateProgressAchievement = 1009,

    /// <summary>
    /// Request All Achievements
    /// </summary>
    RequestAllAchievements = 1010,

    /// <summary>
    /// ()
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Achievement Row ID</para>
    /// </remarks>
    RequestAchievementSpecial = 1011,

    /// <summary>
    /// Build House On Plot
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Ward Index</para>
    /// </remarks>
    BuildHouseOnPlot = 1100,

    /// <summary>
    /// Enter Exterior Fixtures State
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Ward Index</para>
    /// </remarks>
    EnterExteriorFixturesState = 1101,

    /// <summary>
    /// Enter Interior Fixtures State
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Ward Index</para>
    /// </remarks>
    EnterInteriorFixturesState = 1102,

    /// <summary>
    /// Remove House From Plot
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Ward Index</para>
    /// </remarks>
    RemoveHouseFromPlot = 1103,

    /// <summary>
    /// Request Housing Area
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Fixed to 255</para>
    /// </remarks>
    RequestHousingArea = 1104,

    /// <summary>
    /// Request lottery data
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Territory Type</para>
    /// <para><c>param2</c>: Plot index</para>
    /// <code>
    /// <![CDATA[
    /// const int HousesPerArea = 60;
    /// const int AreaOffset = 256;
    /// 
    /// // Ward 1, Plot 41
    /// var wardID = 0;
    /// var districtOffset = wardID * AreaOffset;
    /// var houseID = 40;
    /// var position = districtOffset + houseID]]>
    /// </code>
    /// </remarks>
    RequestHousingLottery = 1105,

    /// <summary>
    /// Request placard data
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Territory Type</para>
    /// <para><c>param2</c>: Plot index</para>
    /// <code>
    /// <![CDATA[
    /// const int HousesPerArea = 60;
    /// const int AreaOffset = 256;
    /// 
    /// // Ward 1, Plot 41
    /// var wardID = 0;
    /// var districtOffset = wardID * AreaOffset;
    /// var houseID = 40;
    /// var position = districtOffset + houseID]]>
    /// </code>
    /// </remarks>
    RequestHousingPlacard = 1106,

    /// <summary>
    /// Request housing area data
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Territory Type</para>
    /// <para><c>param2</c>: Ward index</para>
    /// </remarks>
    RequestHousingWard = 1107,

    /// <summary>
    /// Load Exterior Appearance Inventory
    /// </summary>
    LoadExteriorAppearanceInventory = 1108,

    /// <summary>
    /// Load Interior Appearance Inventory
    /// </summary>
    LoadInteriorAppearanceInventory = 1109,

    /// <summary>
    /// Load Exterior Furnish Inventory
    /// </summary>
    LoadExteriorFurnishInventory = 1110,

    /// <summary>
    /// Load Interior Furnish Inventory
    /// </summary>
    LoadInteriorFurnishInventory = 1111,

    /// <summary>
    /// Store specified item to house storage
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: High 32 bits of HouseID address for HouseManager related area</para>
    /// <code>*(long*)((nint)HousingManager.Instance()->IndoorTerritory + 38560) >> 32</code>
    /// <para><c>param2</c>: HouseID for HouseManager related area</para>
    /// <para><c>param3</c>: InventoryType</para>
    /// <para><c>param4</c>: InventorySlot</para>
    /// </remarks>
    StoreFurniture = 1112,

    /// <summary>
    /// Restore specified furniture from house
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: High 32 bits of HouseID address for HouseManager related area</para>
    /// <code>(long)((nint)HousingManager.Instance()->IndoorTerritory + 38560) >> 32</code>
    /// <para><c>param2</c>: HouseID for HouseManager related area</para>
    /// <para><c>param3</c>: InventoryType (25000 to 25010 / 27000 to 27008)</para>
    /// <para><c>param4</c>: InventorySlot (If >65535, then furniture at slot (i - 65536) is stored to storage)</para>
    /// </remarks>
    RestoreFurniture = 1113,

    /// <summary>
    /// Request housing name setting data
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: High 32 bits of HouseID address for HouseManager related area</para>
    /// <code>*(long*)((nint)HousingManager.Instance()->IndoorTerritory + 38560) >> 32</code>
    /// <para><c>param2</c>: HouseID for HouseManager related area</para>
    /// </remarks>
    RequestHousingName = 1114,

    /// <summary>
    /// Request housing greeting setting data
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: High 32 bits of HouseID address for HouseManager related area</para>
    /// <code>*(long*)((nint)HousingManager.Instance()->IndoorTerritory + 38560) >> 32</code>
    /// <para><c>param2</c>: HouseID for HouseManager related area</para>
    /// </remarks>
    RequestHousingGreeting = 1115,

    /// <summary>
    /// Unknown
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown</para>
    ///     <para><c>param2</c>: Unknown | Unknown &lt;&lt; 8</para>
    /// </remarks>
    HousingCommand1116 = 1116,

    /// <summary>
    /// Request housing guest access setting data
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: High 32 bits of HouseID address for HouseManager related area</para>
    /// <code>*(long*)((nint)HousingManager.Instance()->IndoorTerritory + 38560) >> 32</code>
    /// <para><c>param2</c>: HouseID for HouseManager related area</para>
    /// </remarks>
    RequestHousingGuestAccess = 1117,

    /// <summary>
    /// Save housing guest access settings
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: High 32 bits of HouseID address for HouseManager related area</para>
    /// <code>*(long*)((nint)HousingManager.Instance()->IndoorTerritory + 38560) >> 32</code>
    /// <para><c>param2</c>: HouseID for HouseManager related area</para>
    /// <para><c>param3</c>: Setting enum value combination (Known: 1 - Teleport permission; 65536 - Entry permission)</para>
    /// </remarks>
    SetHousingGuestAccess = 1118,

    /// <summary>
    /// Request housing estate tag setting data
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: High 32 bits of HouseID address for HouseManager related area</para>
    /// <code>*(long*)((nint)HousingManager.Instance()->IndoorTerritory + 38560) >> 32</code>
    /// <para><c>param2</c>: HouseID for HouseManager related area</para>
    /// </remarks>
    RequestHousingEstateTag = 1119,

    /// <summary>
    /// Save housing estate tag settings
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: High 32 bits of HouseID address for HouseManager related area</para>
    /// <code>*(long*)((nint)HousingManager.Instance()->IndoorTerritory + 38560) >> 32</code>
    /// <para><c>param2</c>: HouseID for HouseManager related area</para>
    /// <para><c>param3</c>: Setting enum value combination (Note: Even Tags with the same name have different enum values at different positions)</para>
    /// </remarks>
    SetHousingEstateTag = 1120,

    /// <summary>
    /// Request Placed Furnitures
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: 0 - ; 1</para>
    /// </remarks>
    RequestPlacedFurnitures = 1121,

    /// <summary>
    /// Move to house front gate
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Plot index</para>
    /// </remarks>
    MoveToHouseFrontGate = 1122,

    /// <summary>
    /// Enter "Place furniture/outdoor furnishing" state
    /// </summary>
    /// <remarks>
    /// <para><c>param2</c>: House plot index (0 for apartment)</para>
    /// </remarks>
    EnterFurnishState = 1123,

    /// <summary>
    /// UnknownFC estate
    /// </summary>
    FreeCompanyHousingCommand1124 = 1124,

    /// <summary>
    /// UnknownFC estate
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Room Index</para>
    /// </remarks>
    FreeCompanyHousingPersonalRoomCommand1125 = 1125,

    /// <summary>
    /// View house detail
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Territory Type</para>
    /// <para><c>param2</c>: Plot index</para>
    /// <code>
    /// <![CDATA[
    /// const int HousesPerArea = 60;
    /// const int AreaOffset = 256;
    /// 
    /// // Ward 1, Plot 41
    /// var wardID = 0;
    /// var districtOffset = wardID * AreaOffset;
    /// var houseID = 40;
    /// var position = districtOffset + houseID]]>
    /// </code>
    /// <para><c>param3</c>: (If applicable) Apartment room index</para>
    /// </remarks>
    ViewHouseDetail = 1126,

    /// <summary>
    /// Unknown
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown</para>
    ///     <para><c>param2</c>: Unknown</para>
    /// </remarks>
    HousingCommand1127 = 1127,

    /// <summary>
    /// Request Housing Outdoor Territory
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown, 0 1</para>
    ///     <para><c>param2</c>: Unknown, 0 1</para>
    /// </remarks>
    RequestHousingOutdoorTerritory = 1128,

    /// <summary>
    /// Unknown
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown</para>
    /// </remarks>
    GMCommand1129 = 1129,

    /// <summary>
    /// Unknown
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown</para>
    /// </remarks>
    GMCommand1130 = 1130,

    /// <summary>
    /// Unknown
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: IndoorTerritory HouseID high 32 bits</para>
    ///     <para><c>param2</c>: IndoorTerritory HouseID</para>
    ///     <para><c>param3</c>: Unknown</para>
    ///     <para><c>param4</c>: Unknown</para>
    /// </remarks>
    MannequinCommand1132 = 1132,

    /// <summary>
    /// FC estate
    /// </summary>
    RemoveFreeCompanyHouse = 1133,

    /// <summary>
    /// Retainer
    /// </summary>
    RequestHousingRetainerList = 1134,

    /// <summary>
    /// Request Housing Share Holders
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: 0 - ; 1</para>
    /// </remarks>
    RequestHousingShareHolders = 1135,

    /// <summary>
    /// Unknown
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: HouseID high 32 bits</para>
    ///     <para><c>param2</c>: HouseID</para>
    /// </remarks>
    HousingCommand1136 = 1136,

    /// <summary>
    /// Adjust house lighting
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Brightness level (0 - Brightest, 5 - Darkest)</para>
    /// </remarks>
    SetIndoorEnvironment = 1137,

    /// <summary>
    /// Request Airship
    /// </summary>
    RequestAirship = 1138,

    /// <summary>
    /// Unknown
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c></para>
    /// </remarks>
    AirshipCommand1139 = 1139,

    /// <summary>
    /// Unknown
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c></para>
    /// </remarks>
    AirshipCommand1140 = 1140,

    /// <summary>
    /// Unknown
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c></para>
    ///     <para><c>param2</c>: Unknown</para>
    ///     <para><c>param3</c>: Inventory Type</para>
    ///     <para><c>param4</c>: Inventory Slot</para>
    /// </remarks>
    AirshipCommand1141 = 1141,

    /// <summary>
    /// Unknown
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c></para>
    ///     <para><c>param2</c>: Unknown</para>
    /// </remarks>
    AirshipCommand1142 = 1142,

    /// <summary>
    /// Refresh free company material delivery information
    /// </summary>
    RequestCompanyProject = 1143,

    /// <summary>
    /// Refresh submarine completion information
    /// </summary>
    RequestSubmarine = 1144,

    /// <summary>
    /// Set house background music
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Orchestrion roll index in Orchestrion.csv</para>
    /// </remarks>
    SetHouseBackgroundMusic = 1145,

    /// <summary>
    /// Unknown
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown</para>
    ///     <para><c>param2</c>: Unknown</para>
    ///     <para><c>param3</c>: Unknown</para>
    ///     <para><c>param4</c>: Unknown</para>
    /// </remarks>
    HousingCommand1146 = 1146,

    /// <summary>
    /// Set Orchestrion Playlist
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: ID</para>
    /// </remarks>
    SetOrchestrionPlaylist = 1147,

    /// <summary>
    /// Toggle Orchestrion
    /// </summary>
    ToggleOrchestrion = 1148,

    /// <summary>
    /// Play Next Orchestrion Track
    /// </summary>
    PlayNextOrchestrionTrack = 1149,

    /// <summary>
    /// Retrieve and place specified item from house storage
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: High 32 bits of HouseID address for HouseManager related area</para>
    /// <code>*(long*)((nint)HousingManager.Instance()->IndoorTerritory + 38560) >> 32</code>
    /// <para><c>param2</c>: HouseID for HouseManager related area</para>
    /// <para><c>param3</c>: InventoryType (25000 to 25010 / 27000 to 27008)</para>
    /// <para><c>param4</c>: InventorySlot</para>
    /// </remarks>
    PlaceFurnish = 1150,

    /// <summary>
    /// Request Housing Storeroom
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: HouseManager HouseID high 32 bits</para>
    ///     <para><c>param2</c>: HouseManager HouseID</para>
    /// </remarks>
    RequestHousingStoreroom = 1151,

    /// <summary>
    /// Unknown
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown</para>
    ///     <para><c>param2</c>: Unknown</para>
    ///     <para><c>param3</c>: Inventory Type</para>
    ///     <para><c>param4</c>: Inventory Slot</para>
    /// </remarks>
    HousingCommand1152 = 1152,

    /// <summary>
    /// Repair submarine part
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Submarine index</para>
    /// <para><c>param2</c>: Submarine part index</para>
    /// </remarks>
    RepairSubmarinePart = 1153,

    /// <summary>
    /// Request Housing Guest Book 1154
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: HouseManager HouseID high 32 bits</para>
    ///     <para><c>param2</c>: HouseManager HouseID</para>
    ///     <para><c>param3</c></para>
    /// </remarks>
    RequestHousingGuestBook1154 = 1154,

    /// <summary>
    /// Request Housing Guest Book 1155
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: HouseManager HouseID high 32 bits</para>
    ///     <para><c>param2</c>: HouseManager HouseID</para>
    ///     <para><c>param3</c></para>
    /// </remarks>
    RequestHousingGuestBook1155 = 1155,

    /// <summary>
    /// Request Housing Guest Book 1156
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: HouseManager HouseID high 32 bits</para>
    ///     <para><c>param2</c>: HouseManager HouseID</para>
    ///     <para><c>param3</c></para>
    /// </remarks>
    RequestHousingGuestBook1156 = 1156,

    /// <summary>
    /// Request Housing Guest Book 1157
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: HouseManager HouseID high 32 bits</para>
    ///     <para><c>param2</c>: HouseManager HouseID</para>
    ///     <para><c>param3</c></para>
    /// </remarks>
    RequestHousingGuestBook1157 = 1157,

    /// <summary>
    /// Request Housing Guest Book 1158
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: HouseManager HouseID high 32 bits</para>
    ///     <para><c>param2</c>: HouseManager HouseID</para>
    ///     <para><c>param3</c></para>
    /// </remarks>
    RequestHousingGuestBook1158 = 1158,

    /// <summary>
    /// Unknown
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown</para>
    ///     <para><c>param2</c>: Unknown</para>
    /// </remarks>
    HousingCommand1159 = 1159,

    /// <summary>
    /// Open Housing Retainer Sales Setting UI
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown, 0 2</para>
    /// </remarks>
    OpenHousingRetainerSalesSettingUI = 1160,

    /// <summary>
    /// UnknownRetainer
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Inventory Type</para>
    ///     <para><c>param2</c>: Inventory Slot</para>
    ///     <para><c>param3</c>: Unknown</para>
    ///     <para><c>param4</c>: Unknown</para>
    /// </remarks>
    RetainerMarketCommand1161 = 1161,

    /// <summary>
    /// Unknown
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown</para>
    ///     <para><c>param2</c>: Unknown</para>
    ///     <para><c>param3</c>: Unknown</para>
    /// </remarks>
    HousingCommand1162 = 1162,

    /// <summary>
    /// Open Housing Retainer Buy UI
    /// </summary>
    OpenHousingRetainerBuyUI = 1163,

    /// <summary>
    /// Retainer
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown</para>
    ///     <para><c>param2</c>: Unknown</para>
    ///     <para><c>param3</c>: Unknown</para>
    ///     <para><c>param4</c>: Unknown</para>
    /// </remarks>
    UpdateHousingRetainerPose = 1164,

    /// <summary>
    /// Retainer
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Inventory Type</para>
    ///     <para><c>param2</c>: Inventory Slot</para>
    /// </remarks>
    SetHousingRetainerWeapon = 1165,

    /// <summary>
    /// RetainerYesNo
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: 0 - ; 1</para>
    /// </remarks>
    ToggleHousingRetainerWeapon = 1166,

    /// <summary>
    /// Request Housing
    /// </summary>
    RequestHousing = 1167,

    /// <summary>
    /// Request Housing Interior Design
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown, Fixed to 255</para>
    /// </remarks>
    RequestHousingInteriorDesign = 1168,

    /// <summary>
    /// Request house interior design information
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: House index (starts from 0, ends at 59)</para>
    /// </remarks>
    ChangeHousingInteriorDesign = 1169,

    /// <summary>
    /// Change house interior design style
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: House index (starts from 0, ends at 59)</para>
    /// <para><c>param2</c>: Interior design style (3 - Mist style; 6 - Lavender Beds style; 9 - Goblet style; 12 - Shirogane style; 15 - Empyreum style; 18 - Simple style)</para>
    /// </remarks>
    HouseInteriorPatternCommand1170 = 1170,

    /// <summary>
    /// Collect trophy crystal
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Season (0 - Current season; 1 - Previous season)</para>
    /// </remarks>
    CollectTrophyCrystal = 1200,

    /// <summary>
    /// Select PVP role action
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Role action index</para>
    /// </remarks>
    SelectPVPRoleAction = 1201,

    /// <summary>
    /// Request challenge log data
    /// </summary>
    RequestContentsNote = 1301,

    /// <summary>
    /// Request retainer venture time information
    /// </summary>
    RequestRetainerVentureTime = 1400,

    /// <summary>
    /// Repair item at NPC
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Inventory Type</para>
    /// <para><c>param2</c>: Inventory Slot</para>
    /// <para><c>param3</c>: Item ID</para>
    /// </remarks>
    RepairItemNPC = 1600,

    /// <summary>
    /// Repair all items at NPC
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Category (0 - Main hand/Off hand; 1 - Head/Body/Arms; 2 - Legs/Feet; 3 - Ear/Neck; 4 - Wrist/Ring; 5 - Items)</para>
    /// </remarks>
    RepairAllItemsNPC = 1601,

    /// <summary>
    /// Repair all equipped items at NPC
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Inventory Type (fixed at 1000)</para>
    /// </remarks>
    RepairEquippedItemsNPC = 1602,

    /// <summary>
    /// Switch chocobo combat stance
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Index in BuddyAction.csv</para>
    /// </remarks>
    SetBuddyAction = 1700,

    /// <summary>
    /// Chocobo barding
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Part (0 - Head, 1 - Body, 2 - Legs)</para>
    /// <para><c>param2</c>: Equipment index in BuddyEquip.csv (0 - Remove equipment)</para>
    /// </remarks>
    SetBuddyEquip = 1701,

    /// <summary>
    /// Chocobo learn skill
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Skill index</para>
    /// </remarks>
    LearnBuddySkill = 1702,

    /// <summary>
    /// Request Gold Saucer panel general information
    /// </summary>
    RequestGoldSaucerGeneral = 1850,

    /// <summary>
    /// Request Gold Saucer panel chocobo information
    /// </summary>
    RequestGoldSaucerChocobo = 1900,

    /// <summary>
    /// Start duty record
    /// </summary>
    StartDutyRecord = 1980,

    /// <summary>
    /// End duty record
    /// </summary>
    FinishDutyRecord = 1981,

    /// <summary>
    /// Request Gold Saucer panel Lord of Verminion information
    /// </summary>
    RequestGoldSaucerVerminion = 2010,

    /// <summary>
    /// Confirm Verminion Palette
    /// </summary>
    ConfirmVerminionPalette = 2011,

    /// <summary>
    /// Dissmiss Novice State
    /// </summary>
    DissmissNoviceState = 2100,

    /// <summary>
    /// Set Novice State
    /// </summary>
    SetNoviceState = 2101,

    /// <summary>
    /// Enable/disable auto join novice network setting
    /// </summary>
    SetAutoJoinNoviceNetworkMentor = 2102,

    /// <summary>
    /// YesNo
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: 0 - ; 1</para>
    /// </remarks>
    AcceptNoviceNetworkInvitation = 2103,

    /// <summary>
    /// Dismiss Returner State
    /// </summary>
    DismissReturnerState = 2104,

    /// <summary>
    /// ()
    /// </summary>
    RefreshNoviceNetwork = 2106,

    /// <summary>
    /// YesNo
    /// </summary>
    JoinNoviceNetworkReturner = 2107,

    /// <summary>
    /// Send duel request
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Target GameObject ID</para>
    /// </remarks>
    SendDuel = 2200,

    /// <summary>
    /// Confirm duel request
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: 0 - Confirm; 1 - Cancel; 4 - Force cancel</para>
    /// </remarks>
    RequestDuel = 2201,

    /// <summary>
    /// Confirm duel
    /// </summary>
    ConfirmDuel = 2202,

    /// <summary>
    /// Unknown
    /// </summary>
    ReviveCommand2204 = 2204,

    /// <summary>
    /// Wondrous Tails other operations
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Operation (0 - Think again)</para>
    /// <para><c>param2</c>: Index (left to right, top to bottom, starts from 0)</para>
    /// </remarks>
    ConfirmWondrousTailsSlot = 2253,

    /// <summary>
    /// Wondrous Tails
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: (0 - )</para>
    ///     <para><c>param2</c>: (, 0 )</para>
    /// </remarks>
    WondrousTails = 2254,

    /// <summary>
    /// ENPC
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: ENPC Data ID</para>
    /// </remarks>
    RequestENPC = 2300,

    /// <summary>
    /// Request prism box data
    /// </summary>
    RequestPrismBox = 2350,

    /// <summary>
    /// Store To Prsim Box
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Inventory Type</para>
    ///     <para><c>param1</c>: Inventory Slot</para>
    /// </remarks>
    StoreToPrsimBox = 2351,

    /// <summary>
    /// Restore prism box item
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Prism box internal item ID (MirageManager.Instance().PrismBoxItemIds)</para>
    /// </remarks>
    RestorePrsimBoxItem = 2352,

    /// <summary>
    /// Restore Prsim Box Set Item
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c></para>
    ///     <para><c>param2</c>: 32</para>
    ///     <para><c>param3</c>: high 32 bits</para>
    ///     <para>1 : 4, 8, 16, 32, 64, 128, 256...</para>
    /// </remarks>
    RestorePrsimBoxSetItem = 2353,

    /// <summary>
    /// Request glamour plates data
    /// </summary>
    ApplyGlamour = 2355,

    /// <summary>
    /// Enter/exit glamour plate selection state
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: 0 - Exit, 1 - Enter</para>
    /// <para><c>param2</c>: Unknown, possibly 0 or 1</para>
    /// </remarks>
    RequestGlamourPlate = 2356,

    /// <summary>
    /// Toggle Glamour Plate State
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: 0 - ; 1</para>
    /// </remarks>
    ToggleGlamourPlateState = 2357,

    /// <summary>
    /// Apply glamour plate (must enter glamour plate selection state first)
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Glamour plate index</para>
    /// </remarks>
    ApplyGlamourPlate = 2358,

    /// <summary>
    /// Dispell Glamours
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: AgentMiragePrismMiragePlateData.DispellItemsSelectedBitmask</para>
    /// </remarks>
    DispellGlamours = 2359,

    /// <summary>
    /// Get Fashion Report weekly participation reward
    /// </summary>
    CliamFashionCheckEntryReward = 2450,

    /// <summary>
    /// Get Fashion Report weekly bonus reward
    /// </summary>
    ClaimFashionCheckBonusReward = 2451,

    /// <summary>
    /// Claim Fashion Check New Gear Reward
    /// </summary>
    ClaimFashionCheckNewGearReward = 2452,

    /// <summary>
    /// Unknown
    /// </summary>
    FashionCheckCommand2453 = 2453,

    /// <summary>
    /// Request Enclave
    /// </summary>
    RequestEnclave = 2500,

    /// <summary>
    /// Buy back reconstruction item
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Item index</para>
    /// </remarks>
    BuybackEnclaveItem = 2501,

    /// <summary>
    /// Request Gold Saucer panel Mahjong information
    /// </summary>
    RequestGoldSaucerMahjong = 2550,

    /// <summary>
    /// Request Blue Content Briefing
    /// </summary>
    RequestBlueContentBriefing = 2600,

    /// <summary>
    /// Request Blue Mage spellbook data
    /// </summary>
    RequstBlueNotebook = 2601,

    /// <summary>
    /// Request Trust data
    /// </summary>
    RequestTrustedFriend = 2651,

    /// <summary>
    /// Request Duty Support data
    /// </summary>
    RequestDutySupport = 2653,

    /// <summary>
    /// Send Duty Support application request
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: DawnStory index</para>
    /// <para><c>param2</c>: First four DawnStoryMemberUIParam indices as powers (a1 * 256^0 + a2 * 256^1 + a3 * 256^2 + a4 * 256^3)</para>
    /// <para><c>param3</c>: Last three DawnStoryMemberUIParam indices as powers (a1 * 256^0 + a2 * 256^1 + a3 * 256^2)</para>
    /// </remarks>
    SendDutySupport = 2654,

    /// <summary>
    /// Desynthesize specified item / Recover materia from specified item / Extract from specified item
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Desynthesize: 3735552; Recover materia: 3735553; Extract: 3735554; Repair: 3735555</para>
    /// <para><c>param2</c>: Inventory Type</para>
    /// <para><c>param3</c>: Inventory Slot</para>
    /// <para><c>param4</c>: Item ID</para>
    /// </remarks>
    EventFrameworkAction = 2800,

    /// <summary>
    /// Request Bozja War Result Notebook
    /// </summary>
    RequestBozjaWarResultNotebook = 2900,

    /// <summary>
    /// Bozja assign lost action from holster to slot
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Lost action holster index</para>
    /// <para><c>param2</c>: Slot to assign to</para>
    /// </remarks>
    AssignBozjaActionFromHolster = 2950,

    /// <summary>
    /// Request Bozja Holster Outside
    /// </summary>
    RequestBozjaHolsterOutside = 2951,

    /// <summary>
    /// (Lua )
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown</para>
    ///     <para><c>param2</c>: Unknown</para>
    ///     <para><c>param3</c>: Unknown</para>
    ///     <para><c>param4</c>: Unknown</para>
    /// </remarks>
    PrepareSceneJump = 3000,

    /// <summary>
    /// (Lua )
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown (Yes X )</para>
    ///     <para><c>param2</c>: Unknown (Yes Y )</para>
    ///     <para><c>param3</c>: Unknown (Yes Z )</para>
    ///     <para><c>param4</c>: Unknown (Yes)</para>
    /// </remarks>
    StartSceneJumpLua = 3001,

    /// <summary>
    /// Capture MJI Animal
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: BaseID</para>
    ///     <para><c>param2</c>: EntityID</para>
    ///     <para><c>param3</c>: MJIManager.CurrentMode</para>
    ///     <para><c>param4</c>: MJIManager.CurrentModeItem</para>
    /// </remarks>
    CaptureMJIAnimal = 3050,

    /// <summary>
    /// Request Item Action Unlock State
    /// </summary>
    RequestItemActionUnlockState = 3100,

    /// <summary>
    /// Get Server Value
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: ID</para>
    /// </remarks>
    GetServerValue = 3150,

    /// <summary>
    /// Request portraits list data
    /// </summary>
    RequestPortrait = 3200,

    /// <summary>
    /// Request Character Card
    /// </summary>
    RequestCharacterCard = 3201,

    /// <summary>
    /// Switch Island Sanctuary mode
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Mode (0 - Free; 1 - Harvest; 2 - Plant; 3 - Water; 4 - Remove; 6 - Feed; 7 - Pet; 8 - Call; 9 - Capture)</para>
    /// </remarks>
    SetMJIMode = 3250,

    /// <summary>
    /// Set Island Sanctuary mode parameter, set to 0 when switching modes, set to corresponding item ID when planting, feeding, or capturing
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Parameter</para>
    /// </remarks>
    SetMJIModeParam = 3251,

    /// <summary>
    /// Island Sanctuary settings panel toggle
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: State (1 - Open; 0 - Close)</para>
    /// </remarks>
    ToggleMJISettingPanel = 3252,

    /// <summary>
    /// Request Island Sanctuary workshop schedule data
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Specific day (0 is first day of current cycle, 7 is first day of next cycle)</para>
    /// </remarks>
    RequestMJIWorkshop = 3254,

    /// <summary>
    /// Request MJI Workshop Consumption
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown, 0, 1, 2</para>
    /// </remarks>
    RequestMJIWorkshopConsumption = 3255,

    /// <summary>
    /// Request Island Sanctuary workshop schedule item data
    /// </summary>
    RequestMJIWorkshopAssignment = 3258,

    /// <summary>
    /// Island Sanctuary workshop schedule assignment
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Item and schedule time slot: (8 * (startingHour | (32 * craftObjectId)))</para>
    /// <para><c>param2</c>: Specific day (0 - First day of current cycle, 7 - First day of next cycle)</para>
    /// <para><c>param4</c>: Add/Remove (0 - Add, 1 - Remove)</para>
    /// </remarks>
    AssignMJIWorkshop = 3259,

    /// <summary>
    /// Cancel Island Sanctuary workshop schedule
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Item and schedule time slot: (8 * (startingHour | (32 * craftObjectId)))</para>
    /// <para><c>param2</c>: Specific day (0 - First day of current cycle, 7 - First day of next cycle)</para>
    /// </remarks>
    CancelMJIWorkshopAssignment = 3260,

    /// <summary>
    /// Set Island Sanctuary rest cycles
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Rest day 1</para>
    /// <para><c>param2</c>: Rest day 2</para>
    /// <para><c>param3</c>: Rest day 3</para>
    /// <para><c>param4</c>: Rest day 4</para>
    /// </remarks>
    SetMJIWorkshopRest = 3261,

    /// <summary>
    /// Collect Island Sanctuary granary exploration results
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Granary index</para>
    /// </remarks>
    CollectMJIGranary = 3262,

    /// <summary>
    /// View Island Sanctuary granary exploration destinations
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Granary index</para>
    /// </remarks>
    ViewMJIGranaryDestination = 3263,

    /// <summary>
    /// Island Sanctuary granary dispatch exploration
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Granary index</para>
    /// <para><c>param2</c>: Destination index</para>
    /// <para><c>param3</c>: Exploration days</para>
    /// </remarks>
    AssignMJIGranary = 3264,

    /// <summary>
    /// Release minion on Island Sanctuary
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Minion ID</para>
    /// <para><c>param2</c>: Release area index</para>
    /// </remarks>
    ReleaseMJIMinion = 3265,

    /// <summary>
    /// Release Island Sanctuary pasture animal
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Animal index</para>
    /// </remarks>
    ReleaseMJIAnimal = 3268,

    /// <summary>
    /// Collect Island Sanctuary pasture animal leavings
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Animal index</para>
    /// <para><c>param2</c>: Collection flag</para>
    /// </remarks>
    CollectMJIAnimalLeaving = 3269,

    /// <summary>
    /// Collect all Island Sanctuary pasture animal leavings
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Expected number of leavings to collect (MJIManager.Instance()->PastureHandler->AvailableMammetLeavings)</para>
    /// </remarks>
    CollectMJIAllAnimalLeaving = 3271,

    /// <summary>
    /// Entrust Island Sanctuary pasture animal
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Animal index</para>
    /// <para><c>param2</c>: Feed item ID</para>
    /// </remarks>
    EntrustMJIAnimal = 3272,

    /// <summary>
    /// Recall minion released on Island Sanctuary
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Minion index</para>
    /// </remarks>
    RecallMJIMinion = 3277,

    /// <summary>
    /// Entrust single Island Sanctuary farm plot
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Farm plot index</para>
    /// <para><c>param2</c>: Seed item ID</para>
    /// </remarks>
    EntrustMJIFarm = 3279,

    /// <summary>
    /// Dismiss single Island Sanctuary farm plot
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Farm plot index</para>
    /// </remarks>
    DismissMJIFarmEntrust = 3280,

    /// <summary>
    /// Collect single Island Sanctuary farm plot
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Farm plot index</para>
    /// <para><c>param2</c>: Whether to dismiss after collection (0 - No, 1 - Yes)</para>
    /// </remarks>
    CollectMJIFarm = 3281,

    /// <summary>
    /// Collect all Island Sanctuary farm plots
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: *(int*)MJIManager.Instance()->GranariesState</para>
    /// </remarks>
    CollectMJIAllFarm = 3282,

    /// <summary>
    /// Play Orchestrion Track
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Orchestrion ID</para>
    /// </remarks>
    PlayOrchestrionTrack = 3283,

    /// <summary>
    /// Request Island Sanctuary workshop favor state data
    /// </summary>
    RequestMJIWorkshopFavor = 3292,

    /// <summary>
    /// Aetheryte
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Aetheryte ID</para>
    /// </remarks>
    RemoveFavoriteAetheryte = 3350,

    /// <summary>
    /// Remove Free Aetheryte
    /// </summary>
    RemoveFreeAetheryte = 3351,

    /// <summary>
    /// PlayStation Plus
    /// </summary>
    RemoveFreeAetherytePSPlus = 3352,

    /// <summary>
    /// Nintendo Switch Online
    /// </summary>
    RemoveFreeAetheryteNSO = 3353,

    /// <summary>
    /// Change Wonderous Kaiten mode
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Mode index</para>
    /// </remarks>
    SetWKSMode = 3400,

    /// <summary>
    /// Wonderous Kaiten end interaction 1
    /// </summary>
    FinishWKSInteraction3401 = 3401,

    /// <summary>
    /// Wonderous Kaiten end interaction 2
    /// </summary>
    FinishWKSInteraction3402 = 3402,

    /// <summary>
    /// Unknown ()
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: WKSDevGrade Unknown13</para>
    /// </remarks>
    WKSDevelopmentCommand = 3403,

    /// <summary>
    /// Wonderous Kaiten start mission
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Mission Unit ID</para>
    /// </remarks>
    AcceptWKSMission = 3440,

    /// <summary>
    /// Wonderous Kaiten complete mission
    /// </summary>
    FinishWKSMission = 3441,

    /// <summary>
    /// Wonderous Kaiten abandon mission
    /// </summary>
    AbandonWKSMission = 3442,

    /// <summary>
    /// Wonderous Kaiten start lottery
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Type: 0 - Lunar credit; 1 - Faenna credit</para>
    /// </remarks>
    StartWKSLottery = 3450,

    /// <summary>
    /// Wonderous Kaiten choose lottery wheel
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Type: 0 - Lunar credit; 1 - Faenna credit</para>
    /// <para><c>param2</c>: Wheel type (Left - 0, Right - 1)</para>
    /// </remarks>
    ChooseWKSLotteryType = 3451,

    /// <summary>
    /// Wonderous Kaiten end lottery
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Type: 0 - Lunar credit; 1 - Faenna credit</para>
    /// </remarks>
    FinishWKSLottery = 3452,

    /// <summary>
    /// Wonderous Kaiten request exploration successes data
    /// </summary>
    RequestWKSSuccesses = 3460,

    /// <summary>
    /// Wonderous Kaiten request mecha data
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: WKSMechaEventData Row ID (0 - Currently not started)</para>
    /// </remarks>
    RequestWKSMecha = 3478,

    /// <summary>
    /// Request content inventory (auto on zone change)
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: ContentInventoryProvider 8</para>
    /// </remarks>
    RequestContentInventory = 3500,

    /// <summary>
    /// Request massive PC content (auto-refresh when internal field expires)
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Unknown</para>
    ///     <para><c>param2</c>: Unknown</para>
    /// </remarks>
    RequestMassivePCContent = 3600,

    /// <summary>
    /// Unknown quest-like command
    /// </summary>
    QuestCommand4000 = 4000,

    /// <summary>
    /// Roll dice
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Type (fixed at 0)</para>
    /// <para><c>param2</c>: Maximum value</para>
    /// </remarks>
    RollDice = 9000,

    /// <summary>
    /// Request Yo-kai Watch crossover info (every 5 seconds)
    /// </summary>
    /// <remarks>
    ///     <para><c>param1</c>: Fixed to 39</para>
    /// </remarks>
    RequestYokaiWatchState = 9002,

    /// <summary>
    /// Retainer
    /// </summary>
    Retainer = 9003,
}
