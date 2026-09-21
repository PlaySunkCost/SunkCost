using System.Collections.Generic;
using SunkCost.Interaction;
using SunkCost.Net;
using SunkCost.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SunkCost.Player
{
    // Owner-only immediate-mode HUD: the interaction prompt under the crosshair, the
    // four inventory slots along the bottom, and — in the dive — the suit's visor
    // (docs/VISOR_IMPLEMENTATION_PLAN.md): the diving mask's frame around the view
    // (PlayerVisorMask), air and health bars (full until those systems exist),
    // depth, a compass strip with the way home on it, crew tags, brackets on every
    // item in view and name + value on the one under the dot. World-anchored marks
    // draw under the frame (the rim hides them, as glass would); the readouts, the
    // dot, the prompt and the slots sit on the glass. Reads state, never writes it.
    // The visor's numbers are computed in Update (Visor), so the checks can read
    // them whether or not a frame was drawn.
    [RequireComponent(typeof(HQPlayerController), typeof(PlayerInventory))]
    public sealed class PlayerHudUI : MonoBehaviour
    {
        private const float SlotSize = 64f;
        private const float SlotGap = 8f;
        private const float SlotBottomMargin = 24f;
        private const float MeterGap = 6f;
        private const float MeterHeight = 8f;

        // Visor layout at 1080p; everything scales with the screen height.
        private const float ItemBracketRangeMeters = 12f;   // the visor confirms what you can nearly see; the fog is 15-20 m
        private const int MaxBrackets = 12;
        private const float CrewTagRangeMeters = 40f;
        private const float CompassArcDeg = 90f;
        private const float CompassWidthPx = 380f;
        private const float HomeDoorwayMeters = 3.5f;       // the tube doorway's foot, out from the car's centre along its doorway
        private static readonly Color VisorColor = new(0.62f, 0.94f, 1f, 0.95f);
        private static readonly Color VisorDim = new(0.62f, 0.94f, 1f, 0.55f);
        private static readonly Color GoldColor = new(1f, 0.85f, 0.2f, 0.98f);

        private HQPlayerController controller;
        private PlayerInventory inventory;
        private PlayerSubmersion submersion;
        // Dead (card 2): the visor is the watched player's — their world, their
        // slots, their depth — drawn from the spectator camera on their eyes.
        private SpectatorView spectator;
        private static readonly Color OnAirColor = new(1f, 0.25f, 0.2f, 0.98f);
        private static readonly Color HeardColor = new(0.45f, 1f, 0.55f, 0.95f);
        private SunkCost.Audio.ProximityVoice voice;
        private GUIStyle promptStyle;
        private GUIStyle numberStyle;
        private GUIStyle labelStyle;
        private GUIStyle visorStyle;
        private GUIStyle visorSmallStyle;
        private GUIStyle tagStyle;
        private Texture2D whiteTexture;
        private Texture2D maskTexture;
        private Texture2D reticleTexture;
        private Texture2D ringTexture;
        private Texture2D lungsTexture;
        private int maskWidth, maskHeight;
        private GUIStyle visorTinyStyle;
        private GUIStyle visorRightStyle;
        private GUIStyle visorSmallLeftStyle;
        private GUIStyle visorNumberStyle;
        private GUIStyle visorBigStyle;
        private static readonly Color VisorText = new(0.55f, 0.93f, 1f, 0.95f);
        private static readonly Color VisorDark = new(0.02f, 0.06f, 0.07f, 0.85f);

        // What the visor decided this frame (docs/VISOR_IMPLEMENTATION_PLAN.md section 6).
        public struct VisorReadout
        {
            public bool On;
            public float AirFraction, HealthFraction;
            public bool AirLow, AirEmpty, HealthLow; // the visor blinks the bar and says AIR LOW (PlayerVitals thresholds)
            public bool Leaking;               // the suit leaks: LEAK blinks by the HP bar (the monsters, 20 September 2026)
            public bool LampOff;               // the headlamp's switch is off (F): LAMP OFF under the mode
            public float DashReady;            // 0..1, the dash's cooldown run down (Alt); the same on every peer from the cue (Dan, 21 September 2026)
            public string UpgradeMarks; // "L-TANK  LAMP" — what the diver bought (PlayerUpgrades); null = none
            public float DepthMeters;
            public float HeadingDeg;
            public bool HomeShown;
            public float HomeDistance;
            public float HomeScreenAngleDeg;   // clockwise from ahead
            public int BracketCount;
            public string TargetTag;           // "" when nothing is under the dot
            public int TargetValue;            // -1 when none
            public int CrewTagCount;
            public float NearestCrewDistance;  // +inf when none
            public string DayText;             // "DAY 2/3" in the top-left corner (Dan, 16 September 2026)
            public string MoneyText;           // "BOX $100/$200 · ON ME $45" under it (Dan, 16 September 2026)
            public int OnMeValue;              // what the player carries, hands and slots
            public string SpectatingName;      // the watched player's name while dead; "" otherwise (card 2)
            public bool NoSignal;              // dead with nobody living to watch
            public int OnAirCount;             // how many dead watch this player (0 = no ON AIR mark)
        }
        public VisorReadout Visor => own.Readout;

        // One screen's visor: the readout and the world things it draws, computed
        // for any player from any camera (Compute) and drawn by the one drawing
        // path (DrawVisor). The owner's HUD keeps its own; the deck TV asks the
        // local HUD to fill and draw another for the channel diver; a spectator's
        // screen is the owner's frame computed for the watched player. So a change
        // to the visor's look is one edit here, and it shows everywhere.
        public sealed class VisorFrame
        {
            public VisorReadout Readout;
            public readonly List<CarryableItem> Bracketed = new();
            public readonly List<(HQPlayerController player, Vector3 head, float distance)> Crew = new();
            public CarryableItem Target;   // the item under the dot, or null
            public Vector3 HomeWorld;
            public Camera Camera;          // the eyes it was computed from
        }

        private readonly VisorFrame own = new();
        // Everything written on the glass, 1.7× the first cut (Dan, 18 September
        // 2026: "text too small, can't see anything"); rows and label boxes grow
        // with it. Bars, badge and brackets keep their size.
        public const float VisorTextScale = 1.7f;
        private static readonly Collider[] NearColliders = new Collider[128];
        private HQPlayerController[] othersCache = System.Array.Empty<HQPlayerController>();
        private float othersCachedAt = -1f;

        // What the prompt shows this frame; exposed for the editor test hooks.
        public string PromptText
        {
            get
            {
                if (inventory == null || controller == null) return string.Empty;
                if (controller.TravelLocked || controller.IsDead) return string.Empty;
                string plank = PlankPrompt();
                if (plank != null) return plank;
                string refusal = inventory.Refusal;
                if (!string.IsNullOrEmpty(refusal)) return refusal;
                if (controller.Upgrades != null && !string.IsNullOrEmpty(controller.Upgrades.Refusal)) return controller.Upgrades.Refusal;
                CarryableItem target = controller.CurrentTarget;
                if (target == null && controller.CurrentButton != null)
                    return controller.CurrentButton.Action == SunkCost.World.MonitorButton.Kind.EndDay ? "Press E to end the day" : $"Press E to sail to {controller.CurrentButton.Label}";
                if (target == null && controller.CurrentColourPanel != null) return "Press E to pick your colour";
                if (target == null && controller.CurrentQuotaBoard != null) return PayPrompt();
                if (target == null && controller.CurrentShopDisplay != null) return ShopPrompt(controller.CurrentShopDisplay);
                if (target == null && controller.CurrentTv != null) return TvPrompt();
                if (target == null && controller.CurrentCabinControl != CabinControl.None) return CabinPrompt();
                if (target == null && controller.CurrentPatient != null) return PatientPrompt(controller.CurrentPatient);
                if (target == null && inventory.HeldItem != null)
                {
                    // A patch kit in hand: what left click does with it.
                    PatchKitItem kit = inventory.HeldItem.GetComponent<PatchKitItem>();
                    if (kit != null)
                    {
                        if (kit.IsUsed) return string.Empty; // a used kit: nothing to say, Q drops it
                        if (controller.Vitals == null || !controller.Vitals.Leaking) return "Patch kit — left click patches your leak";
                        return "Left click to patch your leak";
                    }
                    // An air tank in hand: what left click does with it.
                    AirTankItem tank = inventory.HeldItem.GetComponent<AirTankItem>();
                    if (tank != null)
                    {
                        if (tank.IsEmpty) return string.Empty; // an empty tank: nothing to say, Q drops it (Dan, 18 September 2026)
                        PlayerSubmersion submersion = controller.GetComponent<PlayerSubmersion>();
                        if (submersion == null || !submersion.IsSubmerged) return "Full air tank — breathe from it underwater";
                        if (controller.Vitals != null && controller.Vitals.AirFraction >= 1f) return "Full air tank — your air is full, keep it for later";
                        return $"Left click to breathe from the tank (+{Mathf.RoundToInt(tank.RefillFraction * 100f)}% air)";
                    }
                }
                if (target == null || !target.CanGrabFromWorld) return string.Empty;
                string name = target.Grip == CarryGrip.TwoHands ? $"{target.DisplayName} (two hands)" : target.DisplayName;
                if (!inventory.CanStoreOrHold(target)) return "Hands full";
                if (inventory.HoldingOverflow) return $"Press E to store {name}";
                return target.State == ItemState.Released ? $"Hold E to catch {name}" : $"Press E to grab {name}";
            }
        }

        // The deck cabin's button and the car's panel: what E would do, or why not.
        private string CabinPrompt()
        {
            CrewDayState day = CrewDayState.Instance;
            if (controller.CurrentCabinControl == CabinControl.DeckCabin)
            {
                if (day == null || day.World != WorldId.Sea) return "Not at sea";
                if (day.Riding) return day.CabinRide.Direction == RideDirection.Down ? "Going down…" : "Coming up…";
                if (day.Elevator.State != SunkCost.Diving.ElevatorState.AtTop) return "Cabin below";
                return "Press E to descend";
            }
            if (day == null) return string.Empty;
            if (day.Riding) return "Cabin moving";
            if (day.Elevator.State != SunkCost.Diving.ElevatorState.AtBottom) return "Cabin moving";
            return "Press E to surface";
        }

        private bool CabinUsable()
        {
            CrewDayState day = CrewDayState.Instance;
            if (day == null || day.Riding) return false;
            if (controller.CurrentCabinControl == CabinControl.DeckCabin) return day.World == WorldId.Sea && day.Elevator.State == SunkCost.Diving.ElevatorState.AtTop;
            return day.Elevator.State == SunkCost.Diving.ElevatorState.AtBottom;
        }

        private void Awake()
        {
            controller = GetComponent<HQPlayerController>();
            inventory = GetComponent<PlayerInventory>();
            submersion = GetComponent<PlayerSubmersion>();
        }

        // ---- the visor's numbers -------------------------------------------------------

        // The day this dive belongs to, from the crew's day state; empty at HQ.
        private static string DayText()
        {
            CrewDayState day = CrewDayState.Instance;
            if (day == null || day.Day <= 0) return string.Empty;
            int days = WorldSceneFlow.Instance != null ? WorldSceneFlow.Instance.Settings.DaysPerCycle : 3;
            return day.Payday ? "PAYDAY" : $"DAY {day.Day}/{days}";
        }

        // The worth of what a player carries: the held item and the four slots.
        private static int OnMeValue(PlayerInventory inventory)
        {
            if (inventory == null) return 0;
            // An equipped item is in a slot and in the hands at once: count it once.
            int sum = 0;
            bool heldInSlot = false;
            for (int i = 0; i < InventorySlots.Count; i++)
            {
                CarryableItem item = inventory.ItemInSlot(i);
                if (item == null) continue;
                sum += item.Value;
                if (item == inventory.HeldItem) heldInSlot = true;
            }
            if (inventory.HeldItem != null && !heldInSlot) sum += inventory.HeldItem.Value;
            return sum;
        }

        // The cycle against the quota — what was handed over at HQ already plus
        // what the box holds (Dan, 18 September 2026: "I paid 400 already, it
        // should say quota x/500") — and what is on you.
        private static string MoneyText(int onMe)
        {
            CrewDayState day = CrewDayState.Instance;
            if (day == null) return string.Empty;
            int quota = WorldSceneFlow.Instance != null ? WorldSceneFlow.Instance.Settings.QuotaPerCycle : 0;
            return $"QUOTA ${day.CycleSales + day.BoxValue}/${quota}  ·  ON ME ${onMe}";
        }

        // The deck TV: E is the next channel; nobody below is NO SIGNAL (card 3).
        private static string TvPrompt()
        {
            CrewDayState day = CrewDayState.Instance;
            if (day == null || day.TvChannel < 0) return "NO SIGNAL " + "—" + " nobody below";
            return "Press E " + "—" + " next channel";
        }

        private string PayPrompt()
        {
            CrewDayState day = CrewDayState.Instance;
            int quota = WorldSceneFlow.Instance != null ? WorldSceneFlow.Instance.Settings.QuotaPerCycle : 0;
            if (day == null) return "Press E to pay the quota";
            if (day.Day == 0 && !day.Payday) return "Nothing to pay yet " + "—" + " dive first";
            return $"Press E to pay the quota (${quota}) " + "—" + $" sells the box (${day.BoxValue})";
        }

        // Dead, watching (card 2): the watched player's name up top and the one
        // command at the bottom; nobody living left: NO SIGNAL over the body.
        private void DrawSpectateLabels()
        {
            float s = Screen.height / 1080f;
            Color previous = GUI.color;
            if (Visor.NoSignal)
            {
                GUI.color = new Color(1f, 0.35f, 0.35f, 1f);
                GUI.Label(new Rect(0f, Screen.height * 0.42f, Screen.width, 30f * s), "NO SIGNAL", tagStyle);
                GUI.color = new Color(0.9f, 0.9f, 0.9f, 0.9f);
                GUI.Label(new Rect(0f, Screen.height * 0.42f + 34f * s, Screen.width, 20f * s), "you are dead — nobody living to watch — back at End day", visorSmallStyle);
            }
            else if (!string.IsNullOrEmpty(Visor.SpectatingName))
            {
                GUI.color = new Color(0f, 0f, 0f, 0.45f);
                GUI.DrawTexture(new Rect(Screen.width * 0.5f - 170f * s, 18f * s, 340f * s, 34f * s), whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(0f, 20f * s, Screen.width, 30f * s), "SPECTATING  " + Visor.SpectatingName.ToUpperInvariant(), tagStyle);
                GUI.color = new Color(0.9f, 0.9f, 0.9f, 0.75f);
                GUI.Label(new Rect(0f, Screen.height - 26f * s, Screen.width, 20f * s), "left click — next player", visorSmallStyle);
            }
            else
            {
                GUI.color = new Color(1f, 0.35f, 0.35f, 1f);
                GUI.Label(new Rect(0f, Screen.height * 0.42f, Screen.width, 30f * s), "YOU ARE DEAD", tagStyle);
            }
            GUI.color = previous;
        }

        // Being watched (card 2): a red mark, top-right, with the count. On the
        // visor it sits under the mode line; on deck, in the corner.
        private void DrawOnAir(VisorReadout r)
        {
            float s = Screen.height / 1080f;
            Rect tr = r.On ? PlayerVisorMask.TopRightLabelRect(Screen.width, Screen.height) : new Rect(Screen.width - 260f * s, 20f * s, 240f * s, 18f * s);
            float y = r.On ? tr.y + 40f * s : tr.y;
            Color previous = GUI.color;
            GUI.color = OnAirColor;
            GUI.DrawTexture(new Rect(tr.xMax - 12f * s, y + 5f * s, 8f * s, 8f * s), whiteTexture);
            GUI.Label(new Rect(tr.x, y, tr.width - 16f * s, 18f * s), $"ON AIR · {r.OnAirCount} watching", visorRightStyle);
            GUI.color = previous;
        }

        // Who this listener hears and where from (Dan, 17 September 2026: "who
        // sounds coming from, Player x / TV, top right"): one line per speaker under
        // the ON AIR mark — "Idan" straight from them, "Idan · TV" through the deck
        // TV, "Idan · via Dan" through the watched player's ears when dead, "Idan ·
        // dead" from the dead to the dead. The listener's own, not the watched
        // player's: drawn beside the visor, not through DrawVisor.
        private void DrawHeard(VisorReadout r)
        {
            if (voice == null) voice = FindAnyObjectByType<SunkCost.Audio.ProximityVoice>();
            if (voice == null || voice.Heard.Count == 0) return;
            float s = Screen.height / 1080f;
            Rect tr = r.On ? PlayerVisorMask.TopRightLabelRect(Screen.width, Screen.height) : new Rect(Screen.width - 260f * s, 20f * s, 240f * s, 18f * s);
            float y = (r.On ? tr.y + 40f * s : tr.y) + (r.OnAirCount > 0 ? 22f * s : 0f);
            Color previous = GUI.color;
            foreach (SunkCost.Audio.ProximityVoice.HeardSpeaker speaker in voice.Heard)
            {
                string text = voice.PeerName(speaker.Id);
                switch (speaker.Route)
                {
                    case SunkCost.Audio.VoiceRoute.TV: text += " · TV"; break;
                    case SunkCost.Audio.VoiceRoute.Spectate: text += string.IsNullOrEmpty(r.SpectatingName) ? " · via them" : " · via " + r.SpectatingName; break;
                    case SunkCost.Audio.VoiceRoute.Dead: text += " · dead"; break;
                }
                GUI.color = HeardColor;
                GUI.DrawTexture(new Rect(tr.xMax - 12f * s, y + 5f * s, 8f * s, 8f * s), whiteTexture);
                GUI.Label(new Rect(tr.x, y, tr.width - 16f * s, 18f * s), text, visorRightStyle);
                y += 20f * s;
            }
            GUI.color = previous;
        }

        // The visor is on exactly while the player stands in the dive world: that is
        // where the suit is on (section 3.1). No extra state.
        public bool VisorOn => inventory != null && inventory.IsOwner && gameObject.scene == WorldScenes.Scene(WorldId.Dive);

        // Whose screen this is: the owner's, or — dead and watching — the target's.
        private HQPlayerController Who => spectator != null && spectator.Active && spectator.Target != null ? spectator.Target : controller;
        private PlayerInventory ShownInventory => Who == controller ? inventory : Who.Inventory;

        // Nothing shows while you merely look at a thing (Dan, 19 September 2026:
        // "I don't like these"). A press that does nothing — "Not at sea", "Hands
        // full", a refusal from the server — shows its words for a moment, once.
        private string notice;
        private float noticeUntil;
        private string lastInventoryRefusal, lastUpgradeRefusal;
        private const float NoticeSeconds = 1.8f;

        private void Notice(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            notice = text;
            noticeUntil = Time.unscaledTime + NoticeSeconds;
        }

        private static bool IsAnOffer(string prompt) =>
            prompt.StartsWith("Press E") || prompt.StartsWith("Hold E") || prompt.StartsWith("Left click") || prompt.Contains("— Press E");

        // A teammate under the dot: only a leaking one has a prompt (the patch).
        private string PatientPrompt(HQPlayerController patient)
        {
            PlayerVitals theirs = patient.Vitals;
            if (theirs == null || !theirs.Leaking) return string.Empty;
            PlayerIdentity identity = patient.GetComponent<PlayerIdentity>();
            string name = identity != null ? identity.DisplayName : PlayerIdentity.Fallback(patient.OwnerId);
            if (theirs.FriendPatchedToday) return $"{name} was patched by a friend today — a kit now";
            float seconds = controller.Vitals != null ? controller.Vitals.Settings.TeammatePatchSeconds : 3f;
            if (controller.PatchProgress > 0f) return $"Hold E to patch {name} — {Mathf.CeilToInt((1f - controller.PatchProgress) * seconds)} s";
            return $"Hold E to patch {name}";
        }

        private void WatchPresses()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            bool pressed = SessionInputGate.CanPlay && ((keyboard != null && keyboard.eKey.wasPressedThisFrame) || (mouse != null && mouse.leftButton.wasPressedThisFrame));
            if (pressed)
            {
                string prompt = PromptText;
                if (!string.IsNullOrEmpty(prompt) && PlankPrompt() == null && !IsAnOffer(prompt)) Notice(prompt);
            }
            string refusal = inventory.Refusal;
            if (refusal != lastInventoryRefusal) { lastInventoryRefusal = refusal; Notice(refusal); }
            string upgrade = controller.Upgrades != null ? controller.Upgrades.Refusal : string.Empty;
            if (upgrade != lastUpgradeRefusal) { lastUpgradeRefusal = upgrade; Notice(upgrade); }
            string patch = controller.Vitals != null ? controller.Vitals.PatchNotice : string.Empty;
            if (patch != lastPatchNotice) { lastPatchNotice = patch; Notice(patch); }
        }
        private string lastPatchNotice;

        private void Update()
        {
            if (inventory == null || !inventory.IsOwner) return;
            if (spectator == null) spectator = controller.Spectator;
            WatchPresses();
            HQPlayerController who = Who;
            bool watching = who != controller;
            Compute(own, who, ShownInventory, controller.PlayerCamera, watching);
            // The owner's own screen state, on top of whoever's visor it shows.
            VisorReadout r = own.Readout;
            r.SpectatingName = watching ? spectator.TargetName : string.Empty;
            r.NoSignal = controller.IsDead && spectator != null && spectator.Active && spectator.Target == null;
            own.Readout = r;
        }

        // The visor of `who` as seen from `camera`: the owner's own eyes, the
        // spectator camera on the watched player's eyes, or the TV camera on the
        // channel diver's. `watching` = `who` is not this HUD's owner: its world is
        // then the replicated day state's Below (a remote copy's Unity scene on a
        // client is not its world — a client instantiates spawns into its active
        // scene) and its target is found from the camera, as its owner would.
        public void Compute(VisorFrame frame, HQPlayerController who, PlayerInventory inv, Camera camera, bool watching)
        {
            CrewDayState day = CrewDayState.Instance;
            bool targetBelow = watching && day != null && day.IsBelow(who.OwnerId);
            VisorReadout r = default;
            r.On = watching ? targetBelow : VisorOn;
            r.TargetTag = string.Empty;
            r.DayText = DayText();
            r.OnMeValue = OnMeValue(inv);
            r.MoneyText = MoneyText(r.OnMeValue);
            r.TargetValue = -1;
            r.NearestCrewDistance = float.PositiveInfinity;
            r.SpectatingName = string.Empty;
            r.OnAirCount = !who.IsDead && day != null ? day.WatchersOf(who.OwnerId) : 0;
            frame.Camera = camera;
            frame.Bracketed.Clear();
            frame.Crew.Clear();
            frame.Target = null;
            if (!r.On) { frame.Readout = r; return; }

            Vector3 eye = camera != null ? camera.transform.position : who.EyePosition;
            Vector3 forward = camera != null ? camera.transform.forward : who.transform.forward;
            UnityEngine.SceneManagement.Scene world = watching ? WorldScenes.Scene(WorldId.Dive) : gameObject.scene;
            // Air and health from the player's vitals (server-written; a spectator and
            // the TV read the watched player's through this same path).
            PlayerVitals vitals = who.Vitals;
            r.UpgradeMarks = UpgradeMarksOf(who.Upgrades);
            r.AirFraction = vitals != null ? vitals.AirFraction : 1f;
            r.HealthFraction = vitals != null ? vitals.HealthFraction : 1f;
            r.AirLow = vitals != null && vitals.AirLow;
            r.AirEmpty = vitals != null && vitals.AirEmpty;
            r.HealthLow = vitals != null && vitals.HealthLow;
            r.Leaking = vitals != null && vitals.Leaking;
            r.LampOff = !who.LampOn;
            r.DashReady = who.DashReady;
            r.HeadingDeg = Mathf.Repeat(camera != null ? camera.transform.eulerAngles.y : who.Yaw, 360f);

            // HOME: the tube's doorway at the seafloor, hidden while inside the car.
            SunkCost.Diving.ElevatorController car = WorldSceneFlow.FindCar();
            // Depth: the owner's own submersion; through someone else's eyes, the
            // spectator camera's height under the site's sea level.
            if (watching) r.DepthMeters = car != null && SunkCost.Diving.ElevatorMath.IsBelowSurface(car.SeaLevelY, eye.y) ? car.SeaLevelY - eye.y : 0f;
            else r.DepthMeters = submersion != null ? submersion.DepthMeters : 0f;
            if (car != null)
            {
                Vector3 doorway = car.transform.TransformDirection(Quaternion.Euler(0f, CabinFrame.CarDoorwayYaw, 0f) * Vector3.forward);
                Vector3 home = car.BottomPosition + doorway * HomeDoorwayMeters;
                bool inside = car.IsInsideCar(who.transform.position + Vector3.up * 0.5f);
                r.HomeShown = !inside;
                r.HomeDistance = Vector3.Distance(new Vector3(eye.x, 0f, eye.z), new Vector3(home.x, 0f, home.z));
                r.HomeScreenAngleDeg = PlayerVisorMath.ScreenAngleDeg(eye, r.HeadingDeg, home);
                frame.HomeWorld = home;
            }

            // Items in view within reach of the visor: brackets; the dot's item: the tag.
            List<CarryableItem> bracketed = frame.Bracketed;
            int count = Physics.OverlapSphereNonAlloc(eye, ItemBracketRangeMeters, NearColliders, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                CarryableItem item = NearColliders[i].GetComponentInParent<CarryableItem>();
                if (item == null || !item.IsSpawned || !item.CanGrabFromWorld || bracketed.Contains(item)) continue;
                if (!InWorld(item.gameObject.scene, world)) continue;
                if (!PlayerVisorMath.InView(camera, NearColliders[i].bounds.center)) continue;
                bracketed.Add(item);
            }
            bracketed.Sort((a, b) => Vector3.Distance(eye, a.transform.position).CompareTo(Vector3.Distance(eye, b.transform.position)));
            if (bracketed.Count > MaxBrackets) bracketed.RemoveRange(MaxBrackets, bracketed.Count - MaxBrackets);
            r.BracketCount = bracketed.Count;
            // The owner's own target is the controller's; through someone else's
            // eyes it is found the same way from the same spot.
            CarryableItem target = watching ? InteractionTargeting.Find(eye, forward, who.transform, who.InteractReach, who.GrabAimRadius) : controller.CurrentTarget;
            if (target != null && target.CanGrabFromWorld)
            {
                r.TargetTag = TagFor(target);
                r.TargetValue = target.HasValue ? target.Value : -1;
                frame.Target = target;
            }

            // Crew: every other living diver in this world, in view, within range.
            if (Time.unscaledTime - othersCachedAt > 0.5f) { othersCache = FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude); othersCachedAt = Time.unscaledTime; }
            foreach (HQPlayerController other in othersCache)
            {
                if (other == null || other == who || other.IsDead) continue;
                if (watching ? !day.IsBelow(other.OwnerId) : other.gameObject.scene != world) continue;
                Vector3 head = other.transform.position + Vector3.up * 1.85f;
                float distance = Vector3.Distance(eye, head);
                if (distance > CrewTagRangeMeters || !PlayerVisorMath.InView(camera, head)) continue;
                frame.Crew.Add((other, head, distance));
                r.NearestCrewDistance = Mathf.Min(r.NearestCrewDistance, distance);
            }
            r.CrewTagCount = frame.Crew.Count;
            frame.Readout = r;
        }

        // An item is in a world when it sits in that world's scene, or in a scene
        // that is no world at all (a client instantiates the server's spawns into
        // the session scene); an item in another world scene is not.
        private static bool InWorld(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.Scene world)
        {
            return scene == world || !WorldScenes.TryParse(scene.name, out _);
        }

        // "Coin · $48"; "$…" until the roll has landed on this peer; a plain name for
        // things that are not loot.
        public static string TagFor(CarryableItem item)
        {
            if (item == null) return string.Empty;
            if (!item.HasValue) return item.DisplayName;
            return item.Value > 0 ? $"{item.DisplayName} · ${item.Value}" : $"{item.DisplayName} · $…";
        }

        // ---- drawing -------------------------------------------------------------------

        private void OnGUI()
        {
            if (inventory == null || !inventory.IsOwner || SessionInputGate.MenuOpen)
                return;
            EnsureStyles();
            if (controller != null && controller.ViewObstructed && !controller.IsDead) { DrawObstructionCover(); return; }
            if (SessionInputGate.PickerOpen) { DrawColourPicker(); return; }
            bool faded = ScreenFade.Instance != null && !ScreenFade.Instance.IsClear;
            bool maskOn = Visor.On && !faded;                    // the mask is on with the suit, car ride included
            bool readoutsOn = maskOn && !Who.TravelLocked;
            DrawVisor(own, ShownInventory, maskOn, readoutsOn, onAir: !faded);
            if (!faded) DrawHeard(own.Readout);
            DrawAimingDot();
            DrawPrompt();
            if (controller != null && controller.IsDead && !faded) DrawSpectateLabels();
        }

        // The one drawing path for a visor frame: the world marks through the
        // glass, the mask, the readouts on the glass, the slots and the weight
        // meter, the ON AIR mark. The owner's screen, a spectator's screen and
        // the deck TV all come through here.
        public void DrawVisor(VisorFrame frame, PlayerInventory inv, bool maskOn, bool readoutsOn, bool onAir)
        {
            EnsureStyles();
            if (readoutsOn) DrawVisorWorld(frame);
            if (maskOn) DrawMask();
            if (readoutsOn) DrawVisorGlass(frame.Readout);
            if (frame.Readout.OnAirCount > 0 && onAir) DrawOnAir(frame.Readout);
            if (inv != null) { DrawSlots(inv, frame.Readout.On); DrawWeightMeter(inv, frame.Readout.On); }
        }

        private void OnDestroy()
        {
            if (maskTexture != null) Destroy(maskTexture);
            if (reticleTexture != null) Destroy(reticleTexture);
            if (ringTexture != null) Destroy(ringTexture);
            if (lungsTexture != null) Destroy(lungsTexture);
        }

        // The frame, stretched over the screen; re-baked when the screen changes size.
        private void DrawMask()
        {
            if (maskTexture == null || maskWidth != Screen.width || maskHeight != Screen.height) BakeMask();
            Color previous = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), maskTexture);
            GUI.color = previous;
        }

        private void BakeMask()
        {
            if (maskTexture != null) Destroy(maskTexture);
            maskWidth = Mathf.Max(Screen.width, 2);
            maskHeight = Mathf.Max(Screen.height, 2);
            maskTexture = PlayerVisorMask.Bake(maskWidth, maskHeight, Mathf.Max(64, maskWidth / 2), Mathf.Max(36, maskHeight / 2));
        }

        // The readouts, laid out as Dan's reference picture: the compass in the
        // housing let into the top edge with the heading under it, status labels
        // in the top corners, AIR / HP / DEPTH / PRESS bottom-left, the reticle
        // around the dot (PlayerVisorMask places them).
        private void DrawVisorGlass(VisorReadout Visor)
        {
            float s = Screen.height / 1080f;
            float t = s * VisorTextScale; // text rows and label boxes
            Color previous = GUI.color;
            float w = Screen.width, h = Screen.height;

            DrawFrameDetails(w, h, s);

            // Top corners: the suit's status line and the mode, with the little dashes.
            Rect tl = PlayerVisorMask.TopLeftLabelRect(w, h), tr = PlayerVisorMask.TopRightLabelRect(w, h);
            GUI.color = VisorText;
            GUI.Label(new Rect(tl.x, tl.y, tl.width + 120f * t, 18f * t), "HELMET VISOR", visorStyle);
            GUI.Label(new Rect(tl.x, tl.y + 18f * t, tl.width + 120f * t, 14f * t), (string.IsNullOrEmpty(Visor.DayText) ? "SYS  v0.1" : Visor.DayText) + "  ·  SUIT ON", visorTinyStyle);
            DrawDashes(tl.x, tl.y + 36f * t, 6, s);
            if (!string.IsNullOrEmpty(Visor.MoneyText)) GUI.Label(new Rect(tl.x, tl.y + 44f * t, tl.width + 200f * t, 14f * t), Visor.MoneyText, visorTinyStyle);
            GUI.color = VisorText;
            GUI.Label(new Rect(tr.x - 120f * t, tr.y, tr.width + 120f * t, 18f * t), Visor.LampOff ? "MODE: DIVE  ·  LAMP OFF (F)" : "MODE: DIVE", visorRightStyle);
            DrawDashes(tr.xMax - 6f * 10f * s, tr.y + 24f * t, 6, s);
            // The dash (Alt) under the mode: DASH and a short bar that fills back over
            // the cooldown; full and bright = ready. From the replicated cue, so it
            // reads the same through a spectator and on the TV.
            float dashW = 90f * s, dashH = 5f * s, dashX = tr.xMax - dashW, dashY = tr.y + 40f * t;
            GUI.color = VisorText;
            GUI.Label(new Rect(dashX - 160f * t, dashY - 8f * s, 152f * t, 18f * t), "DASH (ALT)", visorRightStyle);
            GUI.color = new Color(0.1f, 0.35f, 0.4f, 0.35f);
            GUI.DrawTexture(new Rect(dashX, dashY, dashW, dashH), whiteTexture);
            GUI.color = Visor.DashReady >= 1f ? VisorColor : new Color(VisorText.r, VisorText.g, VisorText.b, 0.45f);
            if (Visor.DashReady > 0f) GUI.DrawTexture(new Rect(dashX, dashY, dashW * Mathf.Clamp01(Visor.DashReady), dashH), whiteTexture);

            // Vitals, bottom-left, as the picture: the lungs badge, then O2 and HP as
            // thick bars with the label left and the percentage right, then DEPTH and
            // PRESS (1 atm + one per 10 m).
            Rect vitals = PlayerVisorMask.VitalsRect(w, h);
            float left = vitals.x, barRow = 30f * t, row = 26f * t, barWidth = 200f * s, barHeight = 11f * s;
            float top = vitals.y;
            DrawBar(left, top, barWidth, barHeight, "O2", Visor.AirFraction, s, Visor.AirLow, Visor.AirEmpty);
            DrawBar(left, top + barRow, barWidth, barHeight, "HP", Visor.HealthFraction, s, Visor.HealthLow, false);
            float textTop = top + barRow * 2f;
            if (!string.IsNullOrEmpty(Visor.UpgradeMarks))
            {
                // What this diver bought (the shop): the marks on a row of their own
                // under the bars; DEPTH and PRESS move down (Dan: they overlapped).
                GUI.color = VisorText;
                GUI.Label(new Rect(left, textTop, barWidth + 160f * t, row), Visor.UpgradeMarks, visorTinyStyle);
                textTop += row;
            }
            if (Visor.AirLow && Blink())
            {
                // AIR LOW beside the O2 bar, blinking with it; "NO AIR" once the tank is dry.
                GUI.color = new Color(1f, 0.35f, 0.3f, 0.95f);
                GUI.Label(new Rect(left + 56f * t + barWidth + 70f * t, top - 2f * s, 160f * t, 22f * t), Visor.AirEmpty ? "NO AIR" : "AIR LOW", visorStyle);
            }
            if (Visor.Leaking && Blink())
            {
                // LEAK beside the HP bar, in phase with the rest: the tank drains 3× until it is patched.
                GUI.color = new Color(1f, 0.35f, 0.3f, 0.95f);
                GUI.Label(new Rect(left + 56f * t + barWidth + 70f * t, top + barRow - 2f * s, 160f * t, 22f * t), "LEAK", visorStyle);
            }
            GUI.color = VisorText;
            GUI.Label(new Rect(left, textTop, 70f * t, row), "DEPTH", visorSmallLeftStyle);
            GUI.Label(new Rect(left + 72f * t, textTop - 4f * s, 160f * t, row + 4f * s), $"{Visor.DepthMeters:0.0} m", visorBigStyle);
            GUI.Label(new Rect(left, textTop + row, 70f * t, row), "PRESS", visorSmallLeftStyle);
            GUI.Label(new Rect(left + 72f * t, textTop + row, 160f * t, row), $"{1f + Mathf.Max(0f, Visor.DepthMeters) / 10f:0.00} ATA", visorSmallLeftStyle);
            if (ringTexture != null && lungsTexture != null)
            {
                float badge = 52f * s, badgeTop = top + barRow - badge * 0.5f + 4f * s, badgeLeft = left - badge - 10f * s;
                GUI.color = VisorDim;
                GUI.DrawTexture(new Rect(badgeLeft, badgeTop, badge, badge), ringTexture);
                GUI.color = VisorColor;
                GUI.DrawTexture(new Rect(badgeLeft + badge * 0.2f, badgeTop + badge * 0.2f, badge * 0.6f, badge * 0.6f), lungsTexture);
            }

            // Compass, in the housing: cardinals and ticks on the dark band, the heading
            // and HOME on the glass under it, the heading mark on the housing's floor.
            float stripW = CompassWidthPx * s;
            Rect compass = PlayerVisorMask.CompassRect(w, h, stripW);
            float cx = w * 0.5f, stripTop = compass.y, stripH = compass.height;
            string[] cardinals = { "N", "E", "S", "W" };
            for (int deg = 0; deg < 360; deg += 15)
            {
                float? px = PlayerVisorMath.CompassOffsetPx(Visor.HeadingDeg, deg, stripW, CompassArcDeg);
                if (px == null) continue;
                bool cardinal = deg % 90 == 0;
                GUI.color = cardinal ? VisorColor : VisorDim;
                float tickH = cardinal ? 10f * s : 5f * s;
                GUI.DrawTexture(new Rect(cx + px.Value - 1f, stripTop + stripH - tickH, 2f, tickH), whiteTexture);
                if (cardinal) GUI.Label(new Rect(cx + px.Value - 12f * t, stripTop - 4f * s, 24f * t, 18f * t), cardinals[deg / 90], visorStyle);
            }
            GUI.color = VisorColor;
            DrawTriangle(cx, stripTop + stripH + 1f * s, 5f * s, s, down: false); // the heading mark, on the housing's floor
            Rect heading = PlayerVisorMask.HeadingRect(w, h);
            GUI.Label(new Rect(heading.x, heading.y, heading.width, 18f * t), $"{Mathf.RoundToInt(Visor.HeadingDeg) % 360:000}°", visorSmallStyle);
            if (Visor.HomeShown)
            {
                float? homePx = PlayerVisorMath.CompassOffsetPx(Visor.HeadingDeg, Mathf.Repeat(Visor.HeadingDeg + Visor.HomeScreenAngleDeg, 360f), stripW, CompassArcDeg);
                float markerX = homePx.HasValue ? cx + homePx.Value : (Visor.HomeScreenAngleDeg < 0f ? cx - stripW / 2f : cx + stripW / 2f);
                GUI.color = GoldColor;
                GUI.DrawTexture(new Rect(markerX - 3f * s, stripTop + 2f * s, 6f * s, 6f * s), whiteTexture);
                string homeText = homePx.HasValue ? $"HOME {Visor.HomeDistance:0} m" : (Visor.HomeScreenAngleDeg < 0f ? $"◄ HOME {Visor.HomeDistance:0} m" : $"HOME {Visor.HomeDistance:0} m ►");
                GUI.Label(new Rect(heading.x, heading.y + 18f * t, heading.width, 18f * t), homeText, visorSmallStyle);
            }

            // The reticle around the dot: the ring, four bracket corners, two dashes.
            if (reticleTexture != null && SessionInputGate.CanPlay)
            {
                float ring = 150f * s;
                GUI.color = VisorDim;
                GUI.DrawTexture(new Rect(cx - ring / 2f, h * 0.5f - ring / 2f, ring, ring), reticleTexture);
                DrawBrackets(new Rect(cx - 100f * s, h * 0.5f - 75f * s, 200f * s, 150f * s), 14f * s, 1.5f);
                GUI.DrawTexture(new Rect(cx - 180f * s, h * 0.5f - 1f, 60f * s, 1.5f), whiteTexture);
                GUI.DrawTexture(new Rect(cx + 120f * s, h * 0.5f - 1f, 60f * s, 1.5f), whiteTexture);
            }
            GUI.color = previous;
        }

        // The picture's tech marks on the rim: tick rows along the top and bottom
        // edges either side of the housings, dash columns down the sides.
        private void DrawFrameDetails(float w, float h, float s)
        {
            float sx = PlayerVisorMask.SideRimPx(w), ty = PlayerVisorMask.TopRimPx(h), by = PlayerVisorMask.BottomRimPx(h), c = h * PlayerVisorMask.ChamferFrac;
            GUI.color = VisorDim;
            for (int i = 0; i < 14; i++)
            {
                float x = sx + c + 40f * s + i * 12f * s;
                float th = i % 4 == 0 ? 8f * s : 4f * s;
                GUI.DrawTexture(new Rect(x, ty - 10f * s - th, 1.5f, th), whiteTexture);                    // top-left run
                GUI.DrawTexture(new Rect(w - x, ty - 10f * s - th, 1.5f, th), whiteTexture);                // top-right run
                GUI.DrawTexture(new Rect(x, h - by + 10f * s, 1.5f, th), whiteTexture);                     // bottom-left run
                GUI.DrawTexture(new Rect(w - x, h - by + 10f * s, 1.5f, th), whiteTexture);                 // bottom-right run
            }
            for (int i = 0; i < 6; i++)
            {
                float y = h * 0.36f + i * 16f * s;
                GUI.DrawTexture(new Rect(sx - 12f * s, y, 4f * s, 1.5f), whiteTexture);                      // left column
                GUI.DrawTexture(new Rect(w - sx + 8f * s, y, 4f * s, 1.5f), whiteTexture);                   // right column
            }
            GUI.color = VisorColor;
            GUI.DrawTexture(new Rect(sx + c * 0.5f, ty - 24f * s, 60f * s, 2f), whiteTexture);              // the corner accents
            GUI.DrawTexture(new Rect(w - sx - c * 0.5f - 60f * s, ty - 24f * s, 60f * s, 2f), whiteTexture);
            GUI.DrawTexture(new Rect(sx + c * 0.5f, h - by + 22f * s, 60f * s, 2f), whiteTexture);
            GUI.DrawTexture(new Rect(w - sx - c * 0.5f - 60f * s, h - by + 22f * s, 60f * s, 2f), whiteTexture);
        }

        private void DrawDashes(float x, float y, int count, float s)
        {
            GUI.color = VisorDim;
            for (int i = 0; i < count; i++) GUI.DrawTexture(new Rect(x + i * 10f * s, y, 6f * s, 2f * s), whiteTexture);
        }

        // A small solid triangle (rows of a widening rectangle), pointing up or down.
        private void DrawTriangle(float cx, float y, float size, float s, bool down)
        {
            int rows = Mathf.Max(2, Mathf.RoundToInt(size));
            for (int i = 0; i < rows; i++)
            {
                float half = (down ? rows - i : i + 1) * 0.5f * (size / rows) * 1.6f;
                GUI.DrawTexture(new Rect(cx - half, y + i, half * 2f, 1f), whiteTexture);
            }
        }

        // Through the glass: the HOME marker on the doorway, crew tags, item brackets
        // and the tag — projected from the world, so the frame may hide them.
        private void DrawVisorWorld(VisorFrame frame)
        {
            float s = Screen.height / 1080f;
            float t = s * VisorTextScale;
            Color previous = GUI.color;
            VisorReadout Visor = frame.Readout;
            Camera camera = frame.Camera;

            if (Visor.HomeShown && PlayerVisorMath.TryProject(camera, frame.HomeWorld + Vector3.up * 2.2f, out Vector2 hp) && PlayerVisorMath.InView(camera, frame.HomeWorld))
            {
                GUI.color = GoldColor;
                GUI.DrawTexture(new Rect(hp.x - 4f * s, hp.y - 4f * s, 8f * s, 8f * s), whiteTexture);
                GUI.Label(new Rect(hp.x - 60f * t, hp.y + 6f * s, 120f * t, 18f * t), $"HOME {Visor.HomeDistance:0} m", visorSmallStyle);
            }

            // The distance to each crew member, under the name plate over their head
            // (PlayerNamePlate carries the name, 20 September 2026); brighter under the dot.
            foreach ((HQPlayerController other, Vector3 head, float distance) in frame.Crew)
            {
                if (!PlayerVisorMath.TryProject(camera, head, out Vector2 p)) continue;
                bool lookedAt = Mathf.Abs(p.x - Screen.width * 0.5f) < 60f * s && Mathf.Abs(p.y - Screen.height * 0.5f) < 90f * s;
                Color crewColour = VisorColor;
                crewColour.a = lookedAt ? 1f : 0.7f;
                GUI.color = crewColour;
                GUI.Label(new Rect(p.x - 80f * t, p.y - 22f * t, 160f * t, 18f * t), $"{distance:0} m", lookedAt ? visorStyle : visorSmallStyle);
            }

            // Item brackets; the dot's item in gold with its tag.
            CarryableItem target = frame.Target;
            foreach (CarryableItem item in frame.Bracketed)
            {
                Collider collider = item.PrimaryCollider;
                if (collider == null || !PlayerVisorMath.TryBracket(camera, collider.bounds, 6f * s, out Rect rect)) continue;
                bool isTarget = item == target;
                GUI.color = isTarget ? GoldColor : VisorDim;
                DrawBrackets(rect, Mathf.Clamp(rect.width * 0.25f, 6f * s, 14f * s), 2f);
                if (isTarget) // above the brackets: the prompt sits under the dot
                {
                    Rect tagRect = new(rect.center.x - 160f * s, rect.yMin - 36f * s, 320f * s, 30f * s);
                    GUI.color = new Color(0f, 0f, 0f, 0.45f);
                    GUI.DrawTexture(new Rect(tagRect.x + 40f * s, tagRect.y + 2f * s, tagRect.width - 80f * s, tagRect.height - 4f * s), whiteTexture);
                    GUI.color = GoldColor;
                    GUI.Label(tagRect, Visor.TargetTag, tagStyle);
                }
            }
            GUI.color = previous;
        }

        // The plank (18 September 2026): the jumper is told to jump and how long it
        // has; the others who is on the board.
        private string PlankPrompt()
        {
            SunkCost.World.CrewDayState day = SunkCost.World.CrewDayState.Instance;
            if (day == null || day.Phase != SunkCost.World.DayPhase.Plank) return null;
            SunkCost.World.PlankState plank = day.Plank;
            if (!plank.Active || plank.Jumper < 0) return "THE RUN IS OVER";
            SunkCost.World.WorldSceneFlow flow = SunkCost.World.WorldSceneFlow.Instance;
            float left = flow != null ? Mathf.Max(0f, flow.Settings.PlankTurnSeconds - flow.ElapsedSince(plank.TurnStartTick)) : 0f;
            if (plank.Jumper == controller.OwnerId) return $"WALK THE PLANK — jump when you are ready · {left:0} s";
            return $"THE RUN IS OVER — {SunkCost.World.WorldSceneFlow.DisplayName(plank.Jumper)} walks the plank";
        }

        // The shop stand under the dot: the item, its price, the pot; "owned" for an
        // upgrade already bought (one each).
        private string ShopPrompt(SunkCost.Shop.ShopDisplay display)
        {
            SunkCost.Shop.ShopItem item = display.Item;
            if (item == null) return "Nothing for sale here";
            SunkCost.World.CrewDayState day = SunkCost.World.CrewDayState.Instance;
            string pot = day != null ? $" (pot ${day.Balance})" : string.Empty;
            if (item.Kind == SunkCost.Shop.ShopItemKind.Upgrade && controller.Upgrades != null && controller.Upgrades.Has(item.Upgrade)) return $"{item.Name} · owned";
            return $"{item.Name} · ${item.Price} — Press E to buy{pot}";
        }

        private static string UpgradeMarksOf(PlayerUpgrades upgrades)
        {
            if (upgrades == null || upgrades.Owned == SunkCost.Shop.PlayerUpgrade.None) return string.Empty;
            string marks = string.Empty;
            foreach (SunkCost.Shop.PlayerUpgrade upgrade in new[] { SunkCost.Shop.PlayerUpgrade.LargeTank, SunkCost.Shop.PlayerUpgrade.BrightHeadlamp })
                if (upgrades.Has(upgrade)) marks += (marks.Length > 0 ? "  " : string.Empty) + SunkCost.Shop.ShopCatalog.UpgradeMark(upgrade);
            return marks;
        }

        // Twice a second, on for 60 % of it: the low-air and low-health blink.
        private static bool Blink() => Mathf.Repeat(Time.unscaledTime, 0.5f) < 0.3f;

        // A vitals bar: red and blinking when low; the track itself red when empty.
        private void DrawBar(float x, float y, float width, float height, string label, float fraction, float s, bool low, bool empty)
        {
            float t = s * VisorTextScale;
            GUI.color = VisorText;
            GUI.Label(new Rect(x, y - 2f * s, 60f * t, 22f * t), label, visorStyle);
            float barX = x + 56f * t, barY = y + 4f * s;
            GUI.color = empty && Blink() ? new Color(0.8f, 0.15f, 0.1f, 0.6f) : new Color(0.1f, 0.35f, 0.4f, 0.35f);
            GUI.DrawTexture(new Rect(barX, barY, width, height), whiteTexture);
            float fill = PlayerVisorMath.FillWidthPx(fraction, width);
            bool red = low || fraction < 0.25f;
            GUI.color = red ? new Color(1f, 0.35f, 0.3f, low && !Blink() ? 0.45f : 0.95f) : VisorColor;
            if (fill > 0f) GUI.DrawTexture(new Rect(barX, barY, fill, height), whiteTexture);
            GUI.color = red ? new Color(1f, 0.35f, 0.3f, 0.95f) : VisorText;
            GUI.Label(new Rect(barX + width + 12f * s, y - 2f * s, 80f * t, 22f * t), $"{Mathf.RoundToInt(fraction * 100f)}%", visorStyle);
        }

        private void DrawBrackets(Rect rect, float arm, float thickness)
        {
            // Four L-shaped corners.
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, arm, thickness), whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, thickness, arm), whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - arm, rect.yMin, arm, thickness), whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.yMin, thickness, arm), whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMax - thickness, arm, thickness), whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMax - arm, thickness, arm), whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - arm, rect.yMax - thickness, arm, thickness), whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.yMax - arm, thickness, arm), whiteTexture);
        }

        // A plain grey bar under the slots: mass / capacity from the server's carried
        // mass. Full is a real state: the bar turns red and the player crawls.
        private void DrawWeightMeter(PlayerInventory inventory, bool visorOn)
        {
            float totalWidth = InventorySlots.Count * SlotSize + (InventorySlots.Count - 1) * SlotGap;
            float left = (Screen.width - totalWidth) * 0.5f;
            float top = SlotsTop(totalWidth, visorOn) + SlotSize + MeterGap;
            bool overloaded = inventory.Overloaded;
            float fill = overloaded ? 1f : inventory.MeterFill;
            float fillWidth = fill <= 0f ? 0f : Mathf.Clamp(Mathf.Round(fill * totalWidth), 1f, totalWidth);

            Color previous = GUI.color;
            GUI.color = new Color(0.16f, 0.16f, 0.16f, 0.85f);
            GUI.DrawTexture(new Rect(left, top, totalWidth, MeterHeight), whiteTexture);
            if (fillWidth > 0f)
            {
                GUI.color = overloaded ? new Color(0.85f, 0.15f, 0.12f, 0.98f) : new Color(0.62f, 0.62f, 0.62f, 0.95f);
                GUI.DrawTexture(new Rect(left, top, fillWidth, MeterHeight), whiteTexture);
            }
            GUI.color = previous;
            if (overloaded)
            {
                // Above the slot row (and its "In hand" line): the bottom margin is too
                // small for a line under the bar.
                float slotsTop = SlotsTop(totalWidth, visorOn);
                GUI.Label(new Rect(left - 40f, slotsTop - 48f, totalWidth + 80f, 22f), "Too heavy — drop something", promptStyle);
            }
        }

        // The slot row's top: along the bottom on the ship; on the glass just above
        // the mask's nose bridge in the dive.
        private float SlotsTop(float totalWidth, bool visorOn)
        {
            if (!visorOn) return Screen.height - SlotBottomMargin - SlotSize;
            return PlayerVisorMask.SlotRowRect(Screen.width, Screen.height, totalWidth, SlotSize + MeterGap + MeterHeight).y;
        }

        private void DrawPrompt()
        {
            // The plank's turn is the one line that stays up; everything else is a notice after a press.
            string text = PlankPrompt();
            if (text == null && Time.unscaledTime < noticeUntil) text = notice;
            if (string.IsNullOrEmpty(text)) return;
            float width = 420f;
            GUI.Label(new Rect((Screen.width - width) * 0.5f, Screen.height * 0.5f + 28f, width, 28f), text, promptStyle);
        }

        private void DrawSlots(PlayerInventory inventory, bool visorOn)
        {
            float totalWidth = InventorySlots.Count * SlotSize + (InventorySlots.Count - 1) * SlotGap;
            float left = (Screen.width - totalWidth) * 0.5f;
            float top = SlotsTop(totalWidth, visorOn);
            int heldSlot = inventory.HeldSlot;

            if (inventory.HoldingOverflow)
                GUI.Label(new Rect(left, top - 24f, totalWidth, 22f), $"In hand: {inventory.HeldItem.DisplayName} (no slot)", labelStyle);

            for (int i = 0; i < InventorySlots.Count; i++)
            {
                Rect rect = new(left + i * (SlotSize + SlotGap), top, SlotSize, SlotSize);
                CarryableItem item = inventory.ItemInSlot(i);
                bool held = i == heldSlot;

                Color previous = GUI.color;
                if (visorOn)
                {
                    // The visor's slots: dark panel, thin cyan border, bracket corners; gold when in hand.
                    GUI.color = held ? GoldColor : VisorDim;
                    GUI.DrawTexture(new Rect(rect.x - 1f, rect.y - 1f, rect.width + 2f, rect.height + 2f), whiteTexture);
                    GUI.color = VisorDark;
                    GUI.DrawTexture(rect, whiteTexture);
                    GUI.color = held ? GoldColor : VisorColor;
                    DrawBrackets(new Rect(rect.x + 3f, rect.y + 3f, rect.width - 6f, rect.height - 6f), 9f, 1.5f);
                }
                else
                {
                    GUI.color = held ? new Color(1f, 0.85f, 0.2f, 0.95f) : new Color(0f, 0f, 0f, 0.55f);
                    GUI.DrawTexture(new Rect(rect.x - 3f, rect.y - 3f, rect.width + 6f, rect.height + 6f), whiteTexture);
                    GUI.color = new Color(0.12f, 0.12f, 0.12f, 0.85f);
                    GUI.DrawTexture(rect, whiteTexture);
                }
                GUI.color = previous;

                if (item != null)
                {
                    if (item.Icon != null)
                        GUI.DrawTexture(new Rect(rect.x + 6f, rect.y + 6f, rect.width - 12f, rect.height - 12f), item.Icon, ScaleMode.ScaleToFit, true);
                    else
                        GUI.Label(new Rect(rect.x, rect.y + rect.height * 0.5f - 10f, rect.width, 20f), item.DisplayName, labelStyle);
                }
                GUI.Label(new Rect(rect.x + 6f, rect.y + 4f, 20f, 18f), (i + 1).ToString(), visorOn ? visorNumberStyle : numberStyle);
            }
        }

        // The centre aiming dot (docs/PLAYER_MOVEMENT_HANDS_IMPLEMENTATION_PLAN.md
        // section 6, "Center aiming dot"): one outlined dot, scaled with the screen
        // height, gold only when the thing under it is locally usable. Hidden with
        // the menu, the overlay, lost focus, a fade and the travel lock; the prompt
        // text, not the colour, carries refusals.
        // No clear camera pose exists (docs/CAMERA_WALL_CLEARANCE_IMPLEMENTATION_PLAN.md
        // section 4 step 6): an opaque cover instead of a view from inside a wall.
        // The travel fade, when up, draws in front of it anyway.
        // The colour panel's wheel: the sixteen swatches round a ring, yours ringed;
        // click one to become it (saved on this machine, asked of the server). Done
        // or Esc closes. Drawn with the cursor free (SessionInputGate.PickerOpen).
        private void DrawColourPicker()
        {
            float s = Screen.height / 1080f;
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f, ring = 200f * s, swatch = 52f * s;
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(cx - 200f * s, cy - ring - 90f * s, 400f * s, 30f * s), "Pick your colour", promptStyle);
            PlayerIdentity identity = GetComponent<PlayerIdentity>();
            int current = identity != null ? identity.ColourIndex : -1;
            Event e = Event.current;
            for (int i = 0; i < PlayerPalette.Count; i++)
            {
                Vector2 unit = PlayerPalette.WheelPosition(i);
                Rect rect = new(cx + unit.x * ring - swatch * 0.5f, cy - unit.y * ring - swatch * 0.5f, swatch, swatch);
                if (i == current)
                {
                    GUI.color = Color.white;
                    GUI.DrawTexture(new Rect(rect.x - 5f * s, rect.y - 5f * s, rect.width + 10f * s, rect.height + 10f * s), whiteTexture);
                }
                GUI.color = PlayerPalette.Get(i);
                GUI.DrawTexture(rect, whiteTexture);
                if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
                {
                    identity?.RequestColour(i);
                    e.Use();
                }
            }
            GUI.color = identity != null ? identity.Colour : Color.gray;
            GUI.DrawTexture(new Rect(cx - 40f * s, cy - 40f * s, 80f * s, 80f * s), whiteTexture);
            GUI.color = Color.white;
            if (GUI.Button(new Rect(cx - 60f * s, cy + ring + 50f * s, 120f * s, 32f * s), "Done")) SessionInputGate.ClosePicker();
            GUI.color = previous;
        }

        // For the checks: the pick the mouse would make.
        public void PickColourForChecks(int index)
        {
            GetComponent<PlayerIdentity>()?.RequestColour(index);
        }

        private void DrawObstructionCover()
        {
            Color previous = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), whiteTexture);
            GUI.color = previous;
        }

        private void DrawAimingDot()
        {
            if (!SessionInputGate.CanPlay || Who.TravelLocked || Visor.NoSignal) return;
            if (ScreenFade.Instance != null && !ScreenFade.Instance.IsClear) return;
            PlayerMovementSettings settings = controller.Movement;
            float scale = Screen.height / 1080f;
            float diameter = Mathf.Max(2f, Mathf.Round(settings.DotDiameterPx * scale));
            float outline = Mathf.Round(settings.DotOutlinePx * scale);
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            bool usable = false;
            CarryableItem target = own.Target;
            if (controller.IsDead) usable = false;
            else if (target != null && target.CanGrabFromWorld) usable = inventory.CanStoreOrHold(target);
            else if (controller.CurrentButton != null) usable = CrewDayState.Instance != null && !CrewDayState.Instance.Travelling && !CrewDayState.Instance.Sailing;
            else if (controller.CurrentTv != null) usable = CrewDayState.Instance != null && CrewDayState.Instance.TvChannel >= 0;
            else if (controller.CurrentCabinControl != CabinControl.None) usable = CabinUsable();
            else if (controller.CurrentPatient != null) usable = controller.CurrentPatient.Vitals != null && controller.CurrentPatient.Vitals.Leaking && !controller.CurrentPatient.Vitals.FriendPatchedToday;
            Color previous = GUI.color;
            if (outline > 0f)
            {
                GUI.color = settings.DotOutlineColor;
                GUI.DrawTexture(new Rect(cx - diameter * 0.5f - outline, cy - diameter * 0.5f - outline, diameter + outline * 2f, diameter + outline * 2f), whiteTexture);
            }
            GUI.color = usable ? settings.DotUsableColor : settings.DotColor;
            GUI.DrawTexture(new Rect(cx - diameter * 0.5f, cy - diameter * 0.5f, diameter, diameter), whiteTexture);
            GUI.color = previous;
        }

        private void EnsureStyles()
        {
            if (promptStyle != null) return;
            promptStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 15, wordWrap = false };
            promptStyle.normal.textColor = Color.white;
            numberStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold };
            numberStyle.normal.textColor = Color.white;
            labelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12 };
            labelStyle.normal.textColor = Color.white;
            int s = Mathf.Max(1, Mathf.RoundToInt(Screen.height / 1080f * 10f));
            int k = Mathf.RoundToInt(10f * VisorTextScale); // the text scale in tenths, on top of the screen scale
            visorStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontSize = 14 * s * k / 100, fontStyle = FontStyle.Bold, wordWrap = false };
            visorStyle.normal.textColor = Color.white;
            visorSmallStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12 * s * k / 100, wordWrap = false };
            visorSmallStyle.normal.textColor = Color.white;
            tagStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 22 * s * k / 100, fontStyle = FontStyle.Bold, wordWrap = false }; // big: the value must read at a glance (Dan)
            tagStyle.normal.textColor = Color.white;
            visorTinyStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontSize = 10 * s * k / 100, wordWrap = false };
            visorTinyStyle.normal.textColor = Color.white;
            visorRightStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight, fontSize = 12 * s * k / 100, wordWrap = false };
            visorRightStyle.normal.textColor = Color.white;
            visorSmallLeftStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontSize = 12 * s * k / 100, wordWrap = false };
            visorSmallLeftStyle.normal.textColor = Color.white;
            visorNumberStyle = new GUIStyle(GUI.skin.label) { fontSize = 12 * s * k / 100, fontStyle = FontStyle.Bold };
            visorNumberStyle.normal.textColor = VisorText;
            whiteTexture = Texture2D.whiteTexture;
            visorBigStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontSize = 18 * s * k / 100, fontStyle = FontStyle.Bold, wordWrap = false };
            visorBigStyle.normal.textColor = Color.white;
            reticleTexture = PlayerVisorMask.BakeReticle(128);
            ringTexture = PlayerVisorMask.BakeReticle(128, gaps: false);
            lungsTexture = PlayerVisorMask.BakeLungs(64);
            BakeMask(); // here, at the first frame on the ship, not at the first frame in the dive
        }
    }
}
