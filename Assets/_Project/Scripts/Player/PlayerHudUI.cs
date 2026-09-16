using System.Collections.Generic;
using SunkCost.Interaction;
using SunkCost.Net;
using SunkCost.World;
using UnityEngine;

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
        }
        public VisorReadout Visor { get; private set; }

        private readonly List<CarryableItem> bracketed = new();
        private readonly List<(HQPlayerController player, Vector3 head, float distance)> crew = new();
        private static readonly Collider[] NearColliders = new Collider[128];
        private HQPlayerController[] othersCache = System.Array.Empty<HQPlayerController>();
        private float othersCachedAt = -1f;

        // What the prompt shows this frame; exposed for the editor test hooks.
        public string PromptText
        {
            get
            {
                if (inventory == null || controller == null) return string.Empty;
                if (controller.TravelLocked) return string.Empty;
                string refusal = inventory.Refusal;
                if (!string.IsNullOrEmpty(refusal)) return refusal;
                CarryableItem target = controller.CurrentTarget;
                if (target == null && controller.CurrentButton != null) return $"Press E to sail to {controller.CurrentButton.Label}";
                if (target == null && controller.CurrentCabinControl != CabinControl.None) return CabinPrompt();
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

        // The visor is on exactly while the player stands in the dive world: that is
        // where the suit is on (section 3.1). No extra state.
        public bool VisorOn => inventory != null && inventory.IsOwner && gameObject.scene == WorldScenes.Scene(WorldId.Dive);

        private void Update()
        {
            if (inventory == null || !inventory.IsOwner) return;
            VisorReadout r = default;
            r.On = VisorOn;
            r.TargetTag = string.Empty;
            r.TargetValue = -1;
            r.NearestCrewDistance = float.PositiveInfinity;
            bracketed.Clear();
            crew.Clear();
            if (!r.On) { Visor = r; return; }

            Camera camera = controller.PlayerCamera;
            Vector3 eye = controller.EyePosition;
            r.AirFraction = 1f;    // the Air card supplies the number; the bar's place is decided here
            r.HealthFraction = 1f; // no damage exists yet
            r.DepthMeters = submersion != null ? submersion.DepthMeters : 0f;
            r.HeadingDeg = Mathf.Repeat(camera != null ? camera.transform.eulerAngles.y : controller.Yaw, 360f);

            // HOME: the tube's doorway at the seafloor, hidden while inside the car.
            SunkCost.Diving.ElevatorController car = WorldSceneFlow.FindCar();
            if (car != null)
            {
                Vector3 doorway = car.transform.TransformDirection(Quaternion.Euler(0f, CabinFrame.CarDoorwayYaw, 0f) * Vector3.forward);
                Vector3 home = car.BottomPosition + doorway * HomeDoorwayMeters;
                bool inside = car.IsInsideCar(transform.position + Vector3.up * 0.5f);
                r.HomeShown = !inside;
                r.HomeDistance = Vector3.Distance(new Vector3(eye.x, 0f, eye.z), new Vector3(home.x, 0f, home.z));
                r.HomeScreenAngleDeg = PlayerVisorMath.ScreenAngleDeg(eye, r.HeadingDeg, home);
                homeWorld = home;
            }

            // Items in view within reach of the visor: brackets; the dot's item: the tag.
            int count = Physics.OverlapSphereNonAlloc(eye, ItemBracketRangeMeters, NearColliders, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                CarryableItem item = NearColliders[i].GetComponentInParent<CarryableItem>();
                if (item == null || !item.IsSpawned || !item.CanGrabFromWorld || bracketed.Contains(item)) continue;
                if (!InThisWorld(item.gameObject.scene)) continue;
                if (!PlayerVisorMath.InView(camera, NearColliders[i].bounds.center)) continue;
                bracketed.Add(item);
            }
            bracketed.Sort((a, b) => Vector3.Distance(eye, a.transform.position).CompareTo(Vector3.Distance(eye, b.transform.position)));
            if (bracketed.Count > MaxBrackets) bracketed.RemoveRange(MaxBrackets, bracketed.Count - MaxBrackets);
            r.BracketCount = bracketed.Count;
            CarryableItem target = controller.CurrentTarget;
            if (target != null && target.CanGrabFromWorld)
            {
                r.TargetTag = TagFor(target);
                r.TargetValue = target.HasValue ? target.Value : -1;
            }

            // Crew: every other diver in this world, in view, within range.
            if (Time.unscaledTime - othersCachedAt > 0.5f) { othersCache = FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude); othersCachedAt = Time.unscaledTime; }
            foreach (HQPlayerController other in othersCache)
            {
                if (other == null || other == controller || other.gameObject.scene != gameObject.scene) continue;
                Vector3 head = other.transform.position + Vector3.up * 1.85f;
                float distance = Vector3.Distance(eye, head);
                if (distance > CrewTagRangeMeters || !PlayerVisorMath.InView(camera, head)) continue;
                crew.Add((other, head, distance));
                r.NearestCrewDistance = Mathf.Min(r.NearestCrewDistance, distance);
            }
            r.CrewTagCount = crew.Count;
            Visor = r;
        }

        private Vector3 homeWorld;

        // An item is in this world when it sits in the player's world scene, or in
        // a scene that is no world at all (a client instantiates the server's spawns
        // into the session scene); an item in another world scene is not.
        private bool InThisWorld(UnityEngine.SceneManagement.Scene scene)
        {
            return scene == gameObject.scene || !WorldScenes.TryParse(scene.name, out _);
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
            if (controller != null && controller.ViewObstructed) { DrawObstructionCover(); return; }
            bool faded = ScreenFade.Instance != null && !ScreenFade.Instance.IsClear;
            bool maskOn = Visor.On && !faded;                    // the mask is on with the suit, car ride included
            bool readoutsOn = maskOn && !controller.TravelLocked;
            if (readoutsOn) DrawVisorWorld();
            if (maskOn) DrawMask();
            if (readoutsOn) DrawVisorGlass();
            DrawAimingDot();
            DrawPrompt();
            DrawSlots();
            DrawWeightMeter();
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
        private void DrawVisorGlass()
        {
            float s = Screen.height / 1080f;
            Color previous = GUI.color;
            float w = Screen.width, h = Screen.height;

            DrawFrameDetails(w, h, s);

            // Top corners: the suit's status line and the mode, with the little dashes.
            Rect tl = PlayerVisorMask.TopLeftLabelRect(w, h), tr = PlayerVisorMask.TopRightLabelRect(w, h);
            GUI.color = VisorText;
            GUI.Label(new Rect(tl.x, tl.y, tl.width, 18f * s), "HELMET VISOR", visorStyle);
            GUI.Label(new Rect(tl.x, tl.y + 18f * s, tl.width, 14f * s), "SYS  v0.1  ·  SUIT ON", visorTinyStyle);
            DrawDashes(tl.x, tl.y + 36f * s, 6, s);
            GUI.color = VisorText;
            GUI.Label(new Rect(tr.x, tr.y, tr.width, 18f * s), "MODE: DIVE", visorRightStyle);
            DrawDashes(tr.xMax - 6f * 10f * s, tr.y + 24f * s, 6, s);

            // Vitals, bottom-left, as the picture: the lungs badge, then O2 and HP as
            // thick bars with the label left and the percentage right, then DEPTH and
            // PRESS (1 atm + one per 10 m).
            Rect vitals = PlayerVisorMask.VitalsRect(w, h);
            float left = vitals.x, barRow = 30f * s, row = 26f * s, barWidth = 200f * s, barHeight = 11f * s;
            float top = vitals.y;
            DrawBar(left, top, barWidth, barHeight, "O2", Visor.AirFraction, s);
            DrawBar(left, top + barRow, barWidth, barHeight, "HP", Visor.HealthFraction, s);
            float textTop = top + barRow * 2f;
            GUI.color = VisorText;
            GUI.Label(new Rect(left, textTop, 70f * s, row), "DEPTH", visorSmallLeftStyle);
            GUI.Label(new Rect(left + 72f * s, textTop - 4f * s, 120f * s, row + 4f * s), $"{Visor.DepthMeters:0.0} m", visorBigStyle);
            GUI.Label(new Rect(left, textTop + row, 70f * s, row), "PRESS", visorSmallLeftStyle);
            GUI.Label(new Rect(left + 72f * s, textTop + row, 120f * s, row), $"{1f + Mathf.Max(0f, Visor.DepthMeters) / 10f:0.00} ATA", visorSmallLeftStyle);
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
                if (cardinal) GUI.Label(new Rect(cx + px.Value - 12f * s, stripTop, 24f * s, 18f * s), cardinals[deg / 90], visorStyle);
            }
            GUI.color = VisorColor;
            DrawTriangle(cx, stripTop + stripH + 1f * s, 5f * s, s, down: false); // the heading mark, on the housing's floor
            Rect heading = PlayerVisorMask.HeadingRect(w, h);
            GUI.Label(new Rect(heading.x, heading.y, heading.width, 18f * s), $"{Mathf.RoundToInt(Visor.HeadingDeg) % 360:000}°", visorSmallStyle);
            if (Visor.HomeShown)
            {
                float? homePx = PlayerVisorMath.CompassOffsetPx(Visor.HeadingDeg, Mathf.Repeat(Visor.HeadingDeg + Visor.HomeScreenAngleDeg, 360f), stripW, CompassArcDeg);
                float markerX = homePx.HasValue ? cx + homePx.Value : (Visor.HomeScreenAngleDeg < 0f ? cx - stripW / 2f : cx + stripW / 2f);
                GUI.color = GoldColor;
                GUI.DrawTexture(new Rect(markerX - 3f * s, stripTop + 2f * s, 6f * s, 6f * s), whiteTexture);
                string homeText = homePx.HasValue ? $"HOME {Visor.HomeDistance:0} m" : (Visor.HomeScreenAngleDeg < 0f ? $"◄ HOME {Visor.HomeDistance:0} m" : $"HOME {Visor.HomeDistance:0} m ►");
                GUI.Label(new Rect(heading.x, heading.y + 18f * s, heading.width, 18f * s), homeText, visorSmallStyle);
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
        private void DrawVisorWorld()
        {
            float s = Screen.height / 1080f;
            Color previous = GUI.color;

            if (Visor.HomeShown && PlayerVisorMath.TryProject(controller.PlayerCamera, homeWorld + Vector3.up * 2.2f, out Vector2 hp) && PlayerVisorMath.InView(controller.PlayerCamera, homeWorld))
            {
                GUI.color = GoldColor;
                GUI.DrawTexture(new Rect(hp.x - 4f * s, hp.y - 4f * s, 8f * s, 8f * s), whiteTexture);
                GUI.Label(new Rect(hp.x - 60f * s, hp.y + 6f * s, 120f * s, 18f * s), $"HOME {Visor.HomeDistance:0} m", visorSmallStyle);
            }

            // Crew tags over heads; brighter when under the dot.
            foreach ((HQPlayerController other, Vector3 head, float distance) in crew)
            {
                if (!PlayerVisorMath.TryProject(controller.PlayerCamera, head, out Vector2 p)) continue;
                bool lookedAt = Mathf.Abs(p.x - Screen.width * 0.5f) < 60f * s && Mathf.Abs(p.y - Screen.height * 0.5f) < 90f * s;
                GUI.color = lookedAt ? VisorColor : VisorDim;
                GUI.Label(new Rect(p.x - 80f * s, p.y - 22f * s, 160f * s, 18f * s), $"{CrewName(other)} · {distance:0} m", lookedAt ? visorStyle : visorSmallStyle);
            }

            // Item brackets; the dot's item in gold with its tag.
            CarryableItem target = controller.CurrentTarget;
            foreach (CarryableItem item in bracketed)
            {
                Collider collider = item.PrimaryCollider;
                if (collider == null || !PlayerVisorMath.TryBracket(controller.PlayerCamera, collider.bounds, 6f * s, out Rect rect)) continue;
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

        private static string CrewName(HQPlayerController other) => "Diver " + other.OwnerId;

        private void DrawBar(float x, float y, float width, float height, string label, float fraction, float s)
        {
            GUI.color = VisorText;
            GUI.Label(new Rect(x, y - 2f * s, 60f * s, 22f * s), label, visorStyle);
            float barX = x + 56f * s, barY = y + 4f * s;
            GUI.color = new Color(0.1f, 0.35f, 0.4f, 0.35f);
            GUI.DrawTexture(new Rect(barX, barY, width, height), whiteTexture);
            float fill = PlayerVisorMath.FillWidthPx(fraction, width);
            GUI.color = fraction < 0.25f ? new Color(1f, 0.35f, 0.3f, 0.95f) : VisorColor;
            if (fill > 0f) GUI.DrawTexture(new Rect(barX, barY, fill, height), whiteTexture);
            GUI.color = VisorText;
            GUI.Label(new Rect(barX + width + 12f * s, y - 2f * s, 60f * s, 22f * s), $"{Mathf.RoundToInt(fraction * 100f)}%", visorStyle);
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
        private void DrawWeightMeter()
        {
            float totalWidth = InventorySlots.Count * SlotSize + (InventorySlots.Count - 1) * SlotGap;
            float left = (Screen.width - totalWidth) * 0.5f;
            float top = SlotsTop(totalWidth) + SlotSize + MeterGap;
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
                float slotsTop = SlotsTop(totalWidth);
                GUI.Label(new Rect(left - 40f, slotsTop - 48f, totalWidth + 80f, 22f), "Too heavy — drop something", promptStyle);
            }
        }

        // The slot row's top: along the bottom on the ship; on the glass just above
        // the mask's nose bridge in the dive.
        private float SlotsTop(float totalWidth)
        {
            if (!Visor.On) return Screen.height - SlotBottomMargin - SlotSize;
            return PlayerVisorMask.SlotRowRect(Screen.width, Screen.height, totalWidth, SlotSize + MeterGap + MeterHeight).y;
        }

        private void DrawPrompt()
        {
            string text = PromptText;
            if (string.IsNullOrEmpty(text)) return;
            float width = 420f;
            GUI.Label(new Rect((Screen.width - width) * 0.5f, Screen.height * 0.5f + 28f, width, 28f), text, promptStyle);
        }

        private void DrawSlots()
        {
            float totalWidth = InventorySlots.Count * SlotSize + (InventorySlots.Count - 1) * SlotGap;
            float left = (Screen.width - totalWidth) * 0.5f;
            float top = SlotsTop(totalWidth);
            int heldSlot = inventory.HeldSlot;

            if (inventory.HoldingOverflow)
                GUI.Label(new Rect(left, top - 24f, totalWidth, 22f), $"In hand: {inventory.HeldItem.DisplayName} (no slot)", labelStyle);

            for (int i = 0; i < InventorySlots.Count; i++)
            {
                Rect rect = new(left + i * (SlotSize + SlotGap), top, SlotSize, SlotSize);
                CarryableItem item = inventory.ItemInSlot(i);
                bool held = i == heldSlot;

                Color previous = GUI.color;
                if (Visor.On)
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
                GUI.Label(new Rect(rect.x + 6f, rect.y + 4f, 20f, 18f), (i + 1).ToString(), Visor.On ? visorNumberStyle : numberStyle);
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
        private void DrawObstructionCover()
        {
            Color previous = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), whiteTexture);
            GUI.color = previous;
        }

        private void DrawAimingDot()
        {
            if (!SessionInputGate.CanPlay || controller.TravelLocked) return;
            if (ScreenFade.Instance != null && !ScreenFade.Instance.IsClear) return;
            PlayerMovementSettings settings = controller.Movement;
            float scale = Screen.height / 1080f;
            float diameter = Mathf.Max(2f, Mathf.Round(settings.DotDiameterPx * scale));
            float outline = Mathf.Round(settings.DotOutlinePx * scale);
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            bool usable = false;
            CarryableItem target = controller.CurrentTarget;
            if (target != null && target.CanGrabFromWorld) usable = inventory.CanStoreOrHold(target);
            else if (controller.CurrentButton != null) usable = CrewDayState.Instance != null && !CrewDayState.Instance.Travelling && !CrewDayState.Instance.Sailing;
            else if (controller.CurrentCabinControl != CabinControl.None) usable = CabinUsable();
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
            visorStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontSize = 14 * s / 10, fontStyle = FontStyle.Bold, wordWrap = false };
            visorStyle.normal.textColor = Color.white;
            visorSmallStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12 * s / 10, wordWrap = false };
            visorSmallStyle.normal.textColor = Color.white;
            tagStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 22 * s / 10, fontStyle = FontStyle.Bold, wordWrap = false }; // big: the value must read at a glance (Dan)
            tagStyle.normal.textColor = Color.white;
            visorTinyStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontSize = 10 * s / 10, wordWrap = false };
            visorTinyStyle.normal.textColor = Color.white;
            visorRightStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight, fontSize = 12 * s / 10, wordWrap = false };
            visorRightStyle.normal.textColor = Color.white;
            visorSmallLeftStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontSize = 12 * s / 10, wordWrap = false };
            visorSmallLeftStyle.normal.textColor = Color.white;
            visorNumberStyle = new GUIStyle(GUI.skin.label) { fontSize = 12 * s / 10, fontStyle = FontStyle.Bold };
            visorNumberStyle.normal.textColor = VisorText;
            whiteTexture = Texture2D.whiteTexture;
            visorBigStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontSize = 18 * s / 10, fontStyle = FontStyle.Bold, wordWrap = false };
            visorBigStyle.normal.textColor = Color.white;
            reticleTexture = PlayerVisorMask.BakeReticle(128);
            ringTexture = PlayerVisorMask.BakeReticle(128, gaps: false);
            lungsTexture = PlayerVisorMask.BakeLungs(64);
            BakeMask(); // here, at the first frame on the ship, not at the first frame in the dive
        }
    }
}
