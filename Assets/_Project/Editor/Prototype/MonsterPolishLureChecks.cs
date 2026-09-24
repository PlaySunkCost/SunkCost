using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SunkCost.Diving;
using SunkCost.Monsters;
using SunkCost.Player;
using SunkCost.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using H = SunkCost.Editor.Prototype.HQPrototypeTestHooks;
using M = SunkCost.Editor.Prototype.MonsterTestHooks;
using Object = UnityEngine.Object;

namespace SunkCost.Editor.Prototype
{
    // The Lure's polish checks (mon-lure, 24 September 2026; docs/DESIGN.md §6; Dan's
    // checklist: the LIGHT laser's emitter, path, damage and timing; lamps off; a hit
    // and a miss; a wall; repeated cycles; idle, the drift and the feet). The host
    // alone, below, with the roster empty: each row spawns its own Lure on open
    // seabed, places the host with TeleportLocal and reads the server's brain and
    // beam, the host's own vitals, the drawn beam (MonsterBeamView), the lantern's
    // light and the Animator and bones of the host's copy. Log:
    // Temp/polish-Lure-matrix.log. Started by CameraClearanceMatrixDriver
    // ("polish-Lure"). A guest's view is not covered here.
    public static class MonsterPolishLureChecks
    {
        private const string Log = "Temp/polish-Lure-matrix.log";

        private static readonly Stack<IEnumerator> stack = new();
        private static Keyboard keyboard;
        private static InputSettings.EditorInputBehaviorInPlayMode savedInputBehavior;
        private static InputSettings.BackgroundBehavior savedBackgroundBehavior;
        private static bool inputBehaviorChanged, running;
        private static readonly List<GameObject> props = new();
        public static string Status { get; private set; } = "Not run";

        private static CrewDayState Day => CrewDayState.Instance;
        private static HQPlayerController Host() => WorldSceneFlow.LocalPlayer();
        private static MonsterSettings Settings => MonsterSettings.Get();

        [MenuItem("Sunk Cost/Prototype/Run Lure polish checks (Local host, Play Mode)")]
        public static void RunFromMenu()
        {
            try { RunAsHost(); Debug.Log("Lure polish checks running; result in " + Log); }
            catch (InvalidOperationException e) { Debug.LogError(e.Message); }
        }

        public static void RunAsHost()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode and start the Local host first.");
            if (running) throw new InvalidOperationException("Already running");
            Directory.CreateDirectory("Temp");
            File.WriteAllText(Log, "Lure polish checks started " + DateTime.Now + "\n");
            Status = "Running";
            running = true;
            stack.Clear();
            stack.Push(Run());
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            try
            {
                if (!EditorApplication.isPlaying) throw new Exception("Play Mode stopped");
                while (stack.Count > 0)
                {
                    IEnumerator current = stack.Peek();
                    if (!current.MoveNext()) { stack.Pop(); continue; }
                    if (current.Current is IEnumerator nested) { stack.Push(nested); continue; }
                    return;
                }
                Status = "MATRIX_PASS";
            }
            catch (Exception e) { Status = "FAIL: " + e.Message + "\n" + e.StackTrace; }
            File.AppendAllText(Log, Status + "\n");
            if (Status == "MATRIX_PASS") Debug.Log("Lure polish checks: MATRIX_PASS"); else Debug.LogError("Lure polish checks: " + Status);
            foreach (GameObject p in props) if (p != null) Object.Destroy(p);
            props.Clear();
            try { M.ServerDespawnMonsters(); } catch (Exception) { }
            stack.Clear();
            running = false;
            MonsterSettings.RosterOverrideForTests = null;
            MonsterSettings.GhostChanceOverrideForTests = null;
            PlayerVitalsSettings.TankSecondsOverrideForTests = null;
            HQPlayerController.KeyboardForChecks = null;
            HQPlayerController.BypassInputGateForChecks = false;
            if (keyboard != null) { InputSystem.RemoveDevice(keyboard); keyboard = null; }
            if (inputBehaviorChanged) { InputSystem.settings.editorInputBehaviorInPlayMode = savedInputBehavior; InputSystem.settings.backgroundBehavior = savedBackgroundBehavior; inputBehaviorChanged = false; }
            EditorApplication.update -= Tick;
        }

        // ---- harness ------------------------------------------------------------------

        private static void Say(string text) => File.AppendAllText(Log, "  · " + text + "\n");
        private static void Heading(string text) => File.AppendAllText(Log, "\n== " + text + "\n");
        private static void Check(bool value, string label)
        {
            if (!value) throw new Exception(label + "\n" + H.FlowStatus() + "\n" + M.MonstersText());
            File.AppendAllText(Log, "PASS " + label + "\n");
        }
        private static IEnumerator Wait(float seconds)
        {
            float until = Time.unscaledTime + seconds;
            while (Time.unscaledTime < until) yield return null;
        }
        private static IEnumerator Expect(Func<bool> condition, float seconds, Func<string> label)
        {
            float deadline = Time.unscaledTime + seconds;
            while (Time.unscaledTime < deadline && !condition()) yield return null;
            Check(condition(), label());
        }
        private static void Keys(params Key[] pressed) => InputSystem.QueueStateEvent(keyboard, new KeyboardState(pressed));

        private static IEnumerator Descend()
        {
            H.MoveLocalIntoDeckCabin("Sea"); yield return Wait(0.3f);
            int serial = Day.CabinRide.Serial;
            H.ClientRequestCabin();
            yield return Expect(() => Day.CabinRide.Serial > serial, 3f, () => "the deck cabin took the press (refusal: " + Day.LastRefusal.Text + ")");
            yield return Expect(() => !Day.CabinRide.Active, 70f, () => "the ride down completed");
            Check(Day.CabinRide.Stage == CabinRideStage.Complete && Host().gameObject.scene == WorldScenes.Scene(WorldId.Dive), "down in the dive site: " + H.RideStatus());
            yield return Expect(() => Day.Elevator.State == ElevatorState.AtBottom, 30f, () => "the car is at the bottom");
            yield return Wait(1.0f);
        }

        private static float Flat(Vector3 a, Vector3 b) => CreatureSenses.Flat(a, b);
        private static Vector3 FlatDir(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.zero; }
        private static float YawTo(Vector3 from, Vector3 to) { Vector3 d = FlatDir(to - from); return d == Vector3.zero ? 0f : Quaternion.LookRotation(d, Vector3.up).eulerAngles.y; }

        private static Vector3 Ground(Vector3 p)
        {
            Vector3 from = new(p.x, p.y + 6f, p.z);
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 20f, CreatureSenses.SightMask, QueryTriggerInteraction.Ignore)) return hit.point + Vector3.up * 0.05f;
            return p;
        }

        private static Lure SpawnLure(Vector3 at, float yaw)
        {
            M.ServerDespawnMonsters();
            Creature c = MonsterRoster.ServerSpawnForChecks(MonsterKind.Lure, at);
            Check(c is Lure, "spawned the Lure at " + at.ToString("F1"));
            c.ServerPlaceForChecks(at, yaw);
            return (Lure)c;
        }

        private static IEnumerator HostAt(Vector3 spot, Vector3 facing)
        {
            HQPlayerController host = Host();
            host.TeleportLocal(spot, YawTo(spot, facing));
            yield return null; yield return null;
            M.ClientLookAt(facing + Vector3.up * 1.2f);
            yield return null;
        }

        private static IEnumerator Lamp(bool on)
        {
            HQPlayerController host = Host();
            host.RequestLamp(on);
            yield return Expect(() => host.LampOn == on, 2f, () => "the host's lamp is " + (on ? "on" : "off"));
        }

        private static void Heal() => Host().Vitals.ServerHealForChecks();

        private static bool InState(Creature c, string state)
        {
            CreatureRig rig = c.GetComponent<CreatureRig>();
            if (rig == null || !rig.HasAnimator) return false;
            Animator a = rig.Animator;
            int hash = Animator.StringToHash(state);
            if (a.GetCurrentAnimatorStateInfo(0).shortNameHash == hash) return true;
            return a.IsInTransition(0) && a.GetNextAnimatorStateInfo(0).shortNameHash == hash;
        }

        private static Transform Named(Creature c, string name)
        {
            foreach (Transform t in c.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        private static GameObject Wall(Vector3 centre, Vector3 facing, Vector3 size)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Lure check wall";
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(wall, WorldScenes.Scene(WorldId.Dive));
            wall.transform.SetPositionAndRotation(centre, Quaternion.LookRotation(facing, Vector3.up));
            wall.transform.localScale = size;
            Physics.SyncTransforms();
            props.Add(wall);
            return wall;
        }

        private static float SegmentToPoint(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector3.Distance(a + ab * t, p);
        }

        // A temporary camera that sees the site as the diver's own does: the player camera's
        // settings (its clear, background, mask and URP data: post-processing, volumes), not a
        // default camera's skybox, so the dark site stays dark in the frames and the meters.
        private static Camera GameCamera(string name)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            Camera cam = go.AddComponent<Camera>();
            Camera main = Camera.main;
            if (main != null)
            {
                cam.CopyFrom(main);
                UniversalAdditionalCameraData from = main.GetUniversalAdditionalCameraData(), to = cam.GetUniversalAdditionalCameraData();
                to.renderPostProcessing = from.renderPostProcessing;
                to.antialiasing = from.antialiasing;
                to.volumeLayerMask = from.volumeLayerMask;
                to.renderShadows = from.renderShadows;
            }
            cam.targetTexture = null;
            cam.enabled = false;
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 200f;
            return cam;
        }

        // How lit a view is: the mean brightness (0–1) of a small render, taken twice in the same
        // frame, the beam's lights on and then off. The beam's own drawn line is in both, so the
        // difference is the light it throws on what the camera sees.
        private sealed class Meter
        {
            private readonly Camera cam;
            private readonly RenderTexture rt;
            private readonly Texture2D read;
            public Meter()
            {
                cam = GameCamera("Lure check meter");
                cam.fieldOfView = 45f;
                rt = new RenderTexture(160, 90, 24) { hideFlags = HideFlags.HideAndDontSave };
                read = new Texture2D(160, 90, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
                cam.targetTexture = rt;
            }
            public float Luma(Vector3 from, Vector3 lookAt)
            {
                cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(lookAt - from, Vector3.up));
                cam.Render();
                RenderTexture was = RenderTexture.active;
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                read.Apply();
                RenderTexture.active = was;
                Color32[] px = read.GetPixels32();
                double sum = 0;
                foreach (Color32 c in px) sum += 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;
                return (float)(sum / (px.Length * 255.0));
            }
            public void End()
            {
                cam.targetTexture = null;
                Object.DestroyImmediate(cam.gameObject);
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(read);
            }
        }

        // The beam's lights of one Lure (the path, the splash, and its lantern), switched for a meter's second look.
        private static List<Light> BeamLights(Lure lure)
        {
            var list = new List<Light>();
            MonsterBeamLight bl = lure.GetComponent<MonsterBeamLight>();
            if (bl != null)
            {
                for (int i = 0; i < bl.PathCapacity; i++) { Light l = bl.PathLight(i); if (l != null && l.enabled) list.Add(l); }
                if (bl.SplashLight != null && bl.SplashLight.enabled) list.Add(bl.SplashLight);
            }
            return list;
        }

        private static Light LanternLight(Lure lure)
        {
            foreach (Light l in lure.GetComponentsInChildren<Light>(true)) if (l.name == "Lantern light") return l;
            return null;
        }

        // On and off in the same frame: (lit, unlit).
        private static (float on, float off) LitAgainstUnlit(Meter meter, List<Light> lights, Vector3 from, Vector3 at)
        {
            float on = meter.Luma(from, at);
            foreach (Light l in lights) l.enabled = false;
            float off = meter.Luma(from, at);
            foreach (Light l in lights) l.enabled = true;
            return (on, off);
        }

        // Frames for Dan (a GIF made from them afterwards): a temporary camera beside the
        // action, rendered into a texture every other frame and written as PNGs.
        private sealed class Film
        {
            private readonly string dir;
            private readonly Camera cam;
            private readonly RenderTexture rt;
            private readonly Texture2D read;
            private int n, tick;
            public Film(string dir, int w = 560, int h = 320)
            {
                this.dir = dir;
                if (Directory.Exists(dir)) foreach (string f in Directory.GetFiles(dir, "*.png")) File.Delete(f);
                Directory.CreateDirectory(dir);
                cam = GameCamera("Lure check camera");
                cam.fieldOfView = 50f;
                rt = new RenderTexture(w, h, 24) { hideFlags = HideFlags.HideAndDontSave };
                read = new Texture2D(w, h, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
                cam.targetTexture = rt;
            }
            public void Shot(Vector3 from, Vector3 lookAt, int every = 2)
            {
                if (tick++ % every != 0) return;
                cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(lookAt - from, Vector3.up));
                cam.Render();
                RenderTexture was = RenderTexture.active;
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                read.Apply();
                RenderTexture.active = was;
                File.WriteAllBytes(Path.Combine(dir, $"frame_{n++:000}.png"), read.EncodeToPNG());
            }
            public int Frames => n;
            public void End()
            {
                cam.targetTexture = null;
                Object.DestroyImmediate(cam.gameObject);
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(read);
            }
        }

        // A side view of the line from the Lure to the host, far enough to hold both.
        private static void FilmSide(Film film, Lure lure, Vector3 target)
        {
            Vector3 a = lure.transform.position, b = target;
            Vector3 mid = (a + b) * 0.5f + Vector3.up * 1.3f;
            Vector3 across = Vector3.Cross(Vector3.up, FlatDir(b - a));
            float span = Flat(a, b);
            film.Shot(mid + across * Mathf.Max(6f, span * 0.95f) + Vector3.up * 0.8f, mid);
        }

        // One beam, from the charge to the end of the recovery, sampled every frame: the
        // server's line against the drawn one, the lantern and the body against the beam,
        // the poses, the Animator's states and the position.
        private sealed class BeamRecord
        {
            public List<float> FromGaps = new();
            public float HalfWidth;
            public float WorstFromAt;
            public float WorstFrom, WorstTo, WorstOriginToAnchor, WorstLanternAim, WorstBodyYaw, Moved;
            public int Frames, FiringFrames, AimingEntries;
            public bool PoseAgreed = true, StateAgreed = true;
            public string PoseTrouble = string.Empty, StateTrouble = string.Empty;
            public float HitLineGap = float.NaN;           // the drawn line to the host's body axis at the hit's frame
            public List<CreaturePose> Poses = new();
            public float ChargedAt, FiredAt, EndedAt, HitAt;
            public int HitsBefore, HitsAfter, FiredBefore, FiredAfter;
            public int HealthBefore, HealthAfter;
            public float LanternCharge, LanternFire, LanternIdle, LandingPeak;
            // The beam's light (MonsterBeamLight): the path, the charge's tell, the splash, the diver, and after.
            public int PathLitMax, RentedBefore = -1, RentedAfter = -1, PoolLitAfter = -1, LitAfter = -1;
            public float PathOffLine, ChargePathPeak, BurnPathPeak, DiverReach = float.PositiveInfinity, BeamLength;
            public bool HoldingAfter = true, HadLight;
        }

        private static IEnumerator RecordBeam(Lure lure, BeamRecord r, Action<float> perFrame = null)
        {
            HQPlayerController host = Host();
            CreatureBolts bolts = lure.GetComponent<CreatureBolts>();
            CreatureRig rig = lure.GetComponent<CreatureRig>();
            LureLantern lantern = lure.GetComponent<LureLantern>();
            MonsterBeamLight beamLight = lure.GetComponent<MonsterBeamLight>();
            r.HadLight = beamLight != null;
            Transform head = Named(lure, "Head");
            r.HalfWidth = bolts.HalfWidth;
            r.HitsBefore = bolts.ServerHits; r.FiredBefore = bolts.ServerFired; r.HealthBefore = host.Vitals.Health;
            r.LanternIdle = lantern != null ? lantern.LanternIntensity : 0f;
            r.RentedBefore = MonsterBeamLight.PoolRented;
            yield return Expect(() => bolts.ServerCharging, 6f, () => "the Lure charges (" + lure.ServerStatus + ")");
            r.ChargedAt = bolts.ServerChargedAt;
            Vector3 planted = lure.transform.position;
            int lastState = 0, hits = bolts.ServerHits;
            float t0 = Time.time;
            while (Time.time - t0 < Settings.BeamChargeSeconds + Settings.BeamSeconds + 2.5f)
            {
                yield return null;
                perFrame?.Invoke(Time.time - t0);
                MonsterBeamView view = lure.GetComponentInChildren<MonsterBeamView>();
                bool up = bolts.ServerAiming;
                CreaturePose pose = lure.Pose;
                if (r.Poses.Count == 0 || r.Poses[r.Poses.Count - 1] != pose) r.Poses.Add(pose);
                if (rig != null && rig.HasAnimator)
                {
                    int now = rig.Animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
                    int next = rig.Animator.IsInTransition(0) ? rig.Animator.GetNextAnimatorStateInfo(0).shortNameHash : now;
                    if (next != lastState && next == Animator.StringToHash("Aiming")) r.AimingEntries++;
                    lastState = next;
                }
                if (up)
                {
                    r.Frames++;
                    r.Moved = Mathf.Max(r.Moved, Flat(planted, lure.transform.position));
                    CreaturePose want = bolts.ServerCharging ? CreaturePose.Aiming : CreaturePose.Shooting;
                    if (pose != want && Time.time - (bolts.ServerCharging ? bolts.ServerChargedAt : bolts.ServerFiredAt) > 0.05f) { r.PoseAgreed = false; r.PoseTrouble = $"{pose} while {(bolts.ServerCharging ? "charging" : "firing")}"; }
                    if (!InState(lure, want.ToString()) && Time.time - (bolts.ServerCharging ? bolts.ServerChargedAt : bolts.ServerFiredAt) > 0.3f) { r.StateAgreed = false; r.StateTrouble = $"Animator not in {want}"; }
                    if (view != null && view.Phase != BeamPhase.None)
                    {
                        float gapFrom = Vector3.Distance(view.ShownFrom, bolts.ServerFrom);
                        r.FromGaps.Add(gapFrom);
                        if (gapFrom > r.WorstFrom) { r.WorstFrom = gapFrom; r.WorstFromAt = Time.time - r.ChargedAt; }
                        r.WorstTo = Mathf.Max(r.WorstTo, Vector3.Distance(view.ShownTo, bolts.ServerTo));
                    }
                    if (rig != null && rig.BeamOrigin != null)
                    {
                        r.WorstOriginToAnchor = Mathf.Max(r.WorstOriginToAnchor, Vector3.Distance(bolts.ServerFrom, rig.BeamOrigin.position));
                        if (head != null && Time.time - bolts.ServerChargedAt > 0.35f)
                            r.WorstLanternAim = Mathf.Max(r.WorstLanternAim, Vector3.Angle(rig.BeamOrigin.position - head.position, bolts.ServerTo - head.position));
                    }
                    if (bolts.ServerFiring)
                    {
                        r.FiringFrames++;
                        if (Time.time - bolts.ServerFiredAt > 0.3f)
                            r.WorstBodyYaw = Mathf.Max(r.WorstBodyYaw, Vector3.Angle(FlatDir(lure.transform.forward), FlatDir(bolts.ServerAimDirection)));
                        if (lantern != null) r.LanternFire = Mathf.Max(r.LanternFire, lantern.LanternIntensity);
                        if (beamLight != null && view != null && view.Phase == BeamPhase.Firing)
                        {
                            r.LandingPeak = Mathf.Max(r.LandingPeak, beamLight.SplashIntensityShown);
                            r.BurnPathPeak = Mathf.Max(r.BurnPathPeak, beamLight.PathIntensityShown);
                            r.PathLitMax = Mathf.Max(r.PathLitMax, beamLight.PathLitCount);
                            r.BeamLength = Mathf.Max(r.BeamLength, Vector3.Distance(view.ShownFrom, view.ShownTo));
                            Vector3 chest = CreatureSenses.Chest(host);
                            for (int i = 0; i < beamLight.PathCapacity; i++)
                            {
                                Light l = beamLight.PathLight(i);
                                if (l == null || !l.enabled) continue;
                                r.PathOffLine = Mathf.Max(r.PathOffLine, SegmentToPoint(view.ShownFrom, view.ShownTo, l.transform.position));
                                // How far into a light's reach the diver stands (under 1 = lit by it).
                                r.DiverReach = Mathf.Min(r.DiverReach, Vector3.Distance(l.transform.position, chest) / l.range);
                            }
                        }
                    }
                    else
                    {
                        if (lantern != null) r.LanternCharge = Mathf.Max(r.LanternCharge, lantern.LanternIntensity);
                        if (beamLight != null) r.ChargePathPeak = Mathf.Max(r.ChargePathPeak, beamLight.PathIntensityShown);
                    }
                    if (bolts.ServerHits > hits && view != null)
                    {
                        hits = bolts.ServerHits;
                        CharacterController body = host.GetComponent<CharacterController>();
                        Vector3 centre = host.transform.position + body.center;
                        float half = Mathf.Max(0f, body.height * 0.5f - body.radius);
                        // The drawn line against the body's axis (sampled along it).
                        float best = float.PositiveInfinity;
                        for (int k = 0; k <= 10; k++) best = Mathf.Min(best, SegmentToPoint(view.ShownFrom, view.ShownTo, centre + Vector3.up * Mathf.Lerp(-half, half, k / 10f)));
                        r.HitLineGap = best - body.radius;
                    }
                }
                else if (bolts.ServerEndedAt > r.ChargedAt && Time.time - bolts.ServerEndedAt > 1.2f) break;
            }
            // After the beam: the view out, then its lights back in the pool and off.
            MonsterBeamView after = lure.GetComponentInChildren<MonsterBeamView>();
            float outBy = Time.unscaledTime + 1.5f;
            while (after != null && after.Phase != BeamPhase.None && Time.unscaledTime < outBy) yield return null;
            yield return null; // the light's LateUpdate has seen the view go out
            if (beamLight != null) { r.HoldingAfter = beamLight.Holding; r.LitAfter = beamLight.LitCount; }
            r.RentedAfter = MonsterBeamLight.PoolRented;
            r.PoolLitAfter = MonsterBeamLight.PoolLit;
            r.FiredAt = bolts.ServerFiredAt; r.EndedAt = bolts.ServerEndedAt; r.HitAt = bolts.ServerHitAt;
            r.HitsAfter = bolts.ServerHits; r.FiredAfter = bolts.ServerFired; r.HealthAfter = host.Vitals.Health;
            r.FromGaps.Sort();
            Say($"beam: drawn-vs-damage origin median {(r.FromGaps.Count > 0 ? r.FromGaps[r.FromGaps.Count / 2] * 100f : 0f):0.0} cm, worst {r.WorstFrom * 100f:0.0} cm at +{r.WorstFromAt:0.00} s from the charge");
            Say($"beam: charged {r.ChargedAt:0.00}, fired +{r.FiredAt - r.ChargedAt:0.00} s, ended +{r.EndedAt - r.FiredAt:0.00} s, hit {(r.HitsAfter > r.HitsBefore ? "+" + (r.HitAt - r.FiredAt).ToString("0.00") + " s into the burn" : "none")}; drawn-vs-damage from {r.WorstFrom * 100f:0.0} cm, end {r.WorstTo * 100f:0.0} cm; origin-vs-lantern {r.WorstOriginToAnchor * 100f:0.0} cm; lantern off the beam {r.WorstLanternAim:0.0}°; body off the beam {r.WorstBodyYaw:0.0}°; moved {r.Moved * 100f:0.0} cm; poses {string.Join("→", r.Poses)}; Aiming entered {r.AimingEntries}×; lantern idle {r.LanternIdle:0.0} charge {r.LanternCharge:0.0} fire {r.LanternFire:0.0}, landing {r.LandingPeak:0.0}");
            Say($"beam light: {r.PathLitMax} path lights over {r.BeamLength:0.0} m (worst {r.PathOffLine * 100f:0.0} cm off the drawn line), path {r.ChargePathPeak:0.00} in the charge → {r.BurnPathPeak:0.00} burning, splash {r.LandingPeak:0.00}, the diver at {r.DiverReach:0.00} of the nearest light's reach; after: holding {r.HoldingAfter}, lit {r.LitAfter}, pool rented {r.RentedBefore} → {r.RentedAfter}, pool lights on {r.PoolLitAfter}");
        }

        private static void CheckBeam(BeamRecord r, string row, bool expectHit, bool expectSplash = false)
        {
            MonsterSettings s = Settings;
            Check(r.FiredAfter == r.FiredBefore + 1, $"{row} one beam fired ({r.FiredAfter - r.FiredBefore})");
            Check(Mathf.Abs(r.FiredAt - r.ChargedAt - s.BeamChargeSeconds) < 0.1f, $"{row} the charge lasted {r.FiredAt - r.ChargedAt:0.00} s (≈ {s.BeamChargeSeconds})");
            Check(Mathf.Abs(r.EndedAt - r.FiredAt - s.BeamSeconds) < 0.1f, $"{row} the burn lasted {r.EndedAt - r.FiredAt:0.00} s (≈ {s.BeamSeconds})");
            // The server judges in Update from the bone as last drawn; the view draws after this
            // frame's animation: one frame of the head's motion apart, against a beam 24 cm thick.
            float median = r.FromGaps.Count > 0 ? r.FromGaps[r.FromGaps.Count / 2] : 0f;
            Check(median < 0.01f && r.WorstFrom < 0.08f, $"{row} the drawn beam leaves where the damage starts (median {median * 100f:0.0} cm, worst {r.WorstFrom * 100f:0.0} cm at +{r.WorstFromAt:0.00} s: one frame of the lantern's motion)");
            Check(r.WorstTo < 0.03f, $"{row} the drawn beam ends where the damage ends (worst {r.WorstTo * 100f:0.0} cm)");
            Check(r.WorstOriginToAnchor < 0.08f, $"{row} the damage starts at the lantern's BeamOrigin (worst {r.WorstOriginToAnchor * 100f:0.0} cm, one frame)");
            Check(r.WorstLanternAim < 10f, $"{row} the lantern looks down the beam (worst {r.WorstLanternAim:0.0}°)");
            Check(r.FiringFrames == 0 || r.WorstBodyYaw < 20f, $"{row} the body faces along the beam (worst {r.WorstBodyYaw:0.0}°)");
            Check(r.Moved < 0.05f, $"{row} planted through the charge and the burn (moved {r.Moved * 100f:0.0} cm)");
            Check(r.PoseAgreed, $"{row} the pose is Aiming while it charges and Shooting while it burns {r.PoseTrouble}");
            Check(r.StateAgreed, $"{row} the Animator plays Aiming, then Shooting {r.StateTrouble}");
            Check(r.AimingEntries == 1, $"{row} the Aiming clip starts once (no restarts: {r.AimingEntries})");
            Check(r.Poses.Contains(CreaturePose.Recovering), $"{row} it recovers after the beam ({string.Join("→", r.Poses)})");
            Check(r.LanternCharge > r.LanternIdle && r.LanternFire > r.LanternIdle, $"{row} the lantern swells through the charge and blazes as it burns ({r.LanternIdle:0.0} → {r.LanternCharge:0.0} → {r.LanternFire:0.0})");
            int hits = r.HitsAfter - r.HitsBefore;
            if (expectHit)
            {
                Check(hits == 1, $"{row} one hit, no more ({hits})");
                Check(r.HitAt >= r.FiredAt - 0.001f && r.HitAt <= r.EndedAt + 0.001f, $"{row} the hit landed while it burned (+{r.HitAt - r.FiredAt:0.00} s), never in the charge");
                Check(r.HealthBefore - r.HealthAfter >= s.LureDamage - 1 && r.HealthBefore - r.HealthAfter <= s.LureDamage + 1, $"{row} {s.LureDamage} HP gone ({r.HealthBefore} → {r.HealthAfter})");
                Check(Host().Vitals.Leaking, $"{row} and a leak");
                Check(!float.IsNaN(r.HitLineGap) && r.HitLineGap <= r.HalfWidth + 0.02f, $"{row} the drawn beam touched the body when it hurt (its axis {r.HitLineGap * 100f:0.0} cm past the skin; the drawn half-width is {r.HalfWidth * 100f:0} cm)");
                Check(r.DiverReach < 0.6f, $"{row} the beam's light falls on the diver it passes (the chest at {r.DiverReach:0.00} of the nearest path light's reach)");
            }
            else
            {
                Check(hits == 0, $"{row} no hit ({hits})");
                Check(r.HealthAfter == r.HealthBefore, $"{row} health untouched ({r.HealthBefore} → {r.HealthAfter})");
            }
            // The light (Dan, 24 September 2026: the LIGHT beam lights up the dark site round it).
            Check(r.HadLight, $"{row} the Lure carries a MonsterBeamLight");
            int expectLights = Mathf.Clamp(Mathf.CeilToInt(r.BeamLength / 3f - 0.01f), 1, 6);
            Check(r.PathLitMax >= Mathf.Min(expectLights, 2) && r.PathLitMax <= 6, $"{row} the burn lights its path: {r.PathLitMax} lights along {r.BeamLength:0.0} m");
            Check(r.PathOffLine < 0.05f, $"{row} the path lights sit on the drawn beam (worst {r.PathOffLine * 100f:0.0} cm off it)");
            Check(r.ChargePathPeak > 0.01f && r.ChargePathPeak < r.BurnPathPeak * 0.5f, $"{row} in the charge the path comes up faintly, the tell ({r.ChargePathPeak:0.00} against {r.BurnPathPeak:0.00} burning)");
            if (expectSplash) Check(r.LandingPeak > 0.5f, $"{row} a splash of light where it meets the wall ({r.LandingPeak:0.00})");
            Check(!r.HoldingAfter && r.LitAfter == 0, $"{row} after the beam its lights are off and given back (holding {r.HoldingAfter}, lit {r.LitAfter})");
            Check(r.RentedAfter == r.RentedBefore && (r.RentedBefore != 0 || r.PoolLitAfter == 0), $"{row} the pool has them all again (rented {r.RentedBefore} → {r.RentedAfter}, pool lights on {r.PoolLitAfter})");
        }

        // ---- the run ------------------------------------------------------------------

        private static IEnumerator Run()
        {
            HQPlayerController host = Host();
            Check(host != null && host.IsServerStarted, "editor is the host with a spawned player");
            MonsterSettings s = Settings;
            savedInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            savedBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            inputBehaviorChanged = true;
            keyboard = InputSystem.AddDevice<Keyboard>("LureCheckKeyboard");
            HQPlayerController.KeyboardForChecks = keyboard;
            HQPlayerController.BypassInputGateForChecks = true;
            SunkCost.Net.SessionInputGate.OpenMenu();
            Keys(); yield return null;
            MonsterSettings.RosterOverrideForTests = Array.Empty<MonsterKind>();
            MonsterSettings.GhostChanceOverrideForTests = 0f;
            PlayerVitalsSettings.TankSecondsOverrideForTests = 3600f;

            Heading("S0 — to sea and down, the host alone, no roster");
            H.MoveLocalIntoDeckCabin("HQ"); yield return Wait(0.3f);
            Check(H.ServerSail("Sea").StartsWith("sailing"), "sailing to sea");
            yield return Expect(() => Day.Departure.Stage == DepartureStage.Complete && Day.World == WorldId.Sea, 45f, () => "arrived at sea");
            yield return Expect(() => WorldSceneFlow.LocalRider() != null && !WorldSceneFlow.LocalRider().Locked, 5f, () => "controls back");
            yield return Descend();
            ElevatorController car = WorldSceneFlow.FindCar();
            Vector3 shaft = car.BottomPosition;
            M.ServerDespawnMonsters();
            yield return Wait(0.5f);
            Check(Creature.All.Count == 0, "no roster monsters (" + Creature.All.Count + ")");
            Say($"lure: sees a lamp {s.LureSeeMeters} m, forgets {s.LureForgetSeconds} s, {s.LureDamage} HP, cooldown {s.LureShotCooldownSeconds} s, drifts ×{s.LureApproachSpeedFactor} walk, standoff {s.ShooterStandoffMeters} m; beams charge {s.BeamChargeSeconds} s, burn {s.BeamSeconds} s at {s.BeamSweepDegPerSec}°/s, reach {s.BeamRangeMeters} m");

            // The lane: open seabed 32 m out from the shaft at bearing 150°, running across it.
            Vector3 outward = Quaternion.Euler(0f, 150f, 0f) * Vector3.forward;
            Vector3 lane = Vector3.Cross(Vector3.up, outward).normalized;
            Vector3 middle = Ground(shaft + outward * 32f);
            Vector3 P(float along, float side = 0f) => Ground(middle + lane * along + outward * side);
            Vector3 laneFrom = P(-26f) + Vector3.up * 1.2f, laneTo = P(26f) + Vector3.up * 1.2f;
            bool laneClear = !Physics.SphereCast(laneFrom, 0.6f, (laneTo - laneFrom).normalized, out RaycastHit laneHit, Vector3.Distance(laneFrom, laneTo), CreatureSenses.SightMask, QueryTriggerInteraction.Ignore);
            Say("the lane " + (laneClear ? "is clear" : "meets " + laneHit.collider.name + " at " + laneHit.point.ToString("F1")));
            Check(laneClear, "the test lane on the seabed is open");

            // ---------------------------------------------------------------------------
            Heading("L0 — the prefab: the model, the lantern's anchors, the clips, the rig's layer");
            yield return Lamp(false);
            yield return HostAt(P(10f, 6f), P(-8f));
            Lure lure = SpawnLure(P(-8f), YawTo(P(-8f), P(10f)));
            CreatureRig rig = lure.GetComponent<CreatureRig>();
            CreatureBolts bolts = lure.GetComponent<CreatureBolts>();
            Check(rig != null && rig.HasAnimator, "L0 the Lure wears its model with an Animator");
            Check(rig.Animator.cullingMode == AnimatorCullingMode.AlwaysAnimate, "L0 a beam monster animates off-screen too (its BeamOrigin is where the damage starts)");
            foreach (string clip in new[] { "Idle", "Drawn", "Hunting", "Aiming", "Shooting", "Recovering" })
                Check(rig.Animator.HasState(0, Animator.StringToHash(clip)) && rig.Animated((CreaturePose)Enum.Parse(typeof(CreaturePose), clip)), "L0 it has its own " + clip + " clip");
            Transform origin = rig.BeamOrigin;
            Check(origin != null, "L0 the model has a BeamOrigin");
            Vector3 local = lure.transform.InverseTransformPoint(origin.position);
            Say($"L0 BeamOrigin at {local:F2} (over the feet, in its own frame); eye height {lure.EyeHeight:0.00}");
            Check(local.y > 1.6f && local.y < 2.2f && local.z > 0.15f, $"L0 the BeamOrigin is on the lantern's glass: {local.y:0.00} m up, {local.z:0.00} m in front");
            Check(Mathf.Abs(lure.EyeHeight - local.y) < 0.35f, $"L0 it looks from the lantern (eye {lure.EyeHeight:0.00} m)");
            Check(Named(lure, "ToeL") != null && Named(lure, "ToeR") != null, "L0 the toe anchors are there (the slide check measures them)");
            Check(rig.IsStride(CreaturePose.Drawn) && rig.IsStride(CreaturePose.Hunting), $"L0 its walks carry their authored speeds (Drawn {rig.AuthoredSpeed(CreaturePose.Drawn):0.00}, Hunting {rig.AuthoredSpeed(CreaturePose.Hunting):0.00} m/s)");
            Check(lure.GetComponent<LureLantern>() != null, "L0 the lantern's light is on the prefab");

            // ---------------------------------------------------------------------------
            Heading("I1 — idle: no lamp in sight, it stays, alive, its lantern pulsing");
            yield return Wait(1.5f);
            Check(lure.Pose == CreaturePose.Idle && lure.TargetId == -1, "I1 idle with no target (" + lure.ServerStatus + ")");
            Check(InState(lure, "Idle"), "I1 the Animator plays Idle");
            Transform head = Named(lure, "Head");
            Vector3 start = lure.transform.position, hl = head.position;
            float headTravel = 0f;
            for (float t = 0f; t < 2f; t += Time.deltaTime) { headTravel += Vector3.Distance(head.position, hl); hl = head.position; yield return null; }
            Check(Flat(start, lure.transform.position) < 0.05f, $"I1 it keeps its spot ({Flat(start, lure.transform.position):0.000} m)");
            Check(headTravel > 0.02f && headTravel < 1.5f, $"I1 alive: the lantern moved {headTravel * 100f:0.0} cm over 2 s");
            Check(lure.GetComponent<LureLantern>().LanternIntensity > 0.1f, $"I1 the lantern glows ({lure.GetComponent<LureLantern>().LanternIntensity:0.00})");
            Check(bolts.ServerFired == 0 && lure.ServerAims == 0, "I1 no beam at a dark diver");

            // ---------------------------------------------------------------------------
            Heading("A1 — a hit: the host stands lit 14 m off, the Lure faces away; it turns, plants, charges, burns, hits once, recovers");
            Heal();
            yield return HostAt(P(6f), P(-8f));
            lure.ServerPlaceForChecks(P(-8f), YawTo(P(6f), P(-8f)));  // its back to the host
            yield return Lamp(true);
            var hit = new BeamRecord();
            string tag = "hw" + Mathf.RoundToInt(bolts.HalfWidth * 100f);
            var hitFilm = new Film("Temp/lure-film/beam-hit-" + tag);
            var closeFilm = new Film("Temp/lure-film/beam-close-" + tag);
            var meter = new Meter();
            (float on, float off) glowLure = default, glowFloor = default, glowDiver = default, chargeLure = default;
            bool meteredBurn = false, meteredCharge = false;
            yield return RecordBeam(lure, hit, t =>
            {
                FilmSide(hitFilm, lure, host.transform.position);
                // Over its shoulder, close: the lantern, the pose and the beam's thickness.
                Vector3 fwd = FlatDir(lure.transform.forward), right = Vector3.Cross(Vector3.up, fwd);
                closeFilm.Shot(lure.transform.position - fwd * 2.6f + right * 2.2f + Vector3.up * 2.3f, lure.transform.position + fwd * 3f + Vector3.up * 1.4f);
                // G1: how much the beam's light brightens the Lure, the seabed along the path and the diver,
                // on against off in the same frame. The charge at 0.85 s (the lantern's swell), the burn at +1 s.
                Light lanternLight = LanternLight(lure);
                Vector3 body = lure.transform.position + Vector3.up * 1.1f;
                Vector3 lureCam = body + right * 3.2f + fwd * 1.2f + Vector3.up * 0.4f;
                if (!meteredCharge && bolts.ServerCharging && Time.time - bolts.ServerChargedAt > 0.85f && lanternLight != null)
                {
                    meteredCharge = true;
                    var only = new List<Light> { lanternLight };
                    chargeLure = LitAgainstUnlit(meter, only, lureCam, body);
                }
                if (!meteredBurn && bolts.ServerFiring && Time.time - bolts.ServerFiredAt > 1.0f)
                {
                    MonsterBeamView view = lure.GetComponentInChildren<MonsterBeamView>();
                    List<Light> lights = BeamLights(lure);
                    if (lanternLight != null) lights.Add(lanternLight);
                    if (view != null && lights.Count > 0)
                    {
                        meteredBurn = true;
                        glowLure = LitAgainstUnlit(meter, lights, lureCam, body);
                        Vector3 dir = FlatDir(view.ShownTo - view.ShownFrom);
                        Vector3 side = Vector3.Cross(Vector3.up, dir);
                        float mid = Mathf.Min(Flat(view.ShownFrom, host.transform.position) * 0.5f, 8f);
                        Vector3 floor = Ground(lure.transform.position + dir * mid);
                        glowFloor = LitAgainstUnlit(meter, lights, floor + side * 3.5f + Vector3.up * 2.5f, floor);
                        Vector3 chest = CreatureSenses.Chest(host);
                        glowDiver = LitAgainstUnlit(meter, lights, chest - dir * 1.2f + side * 2.4f + Vector3.up * 0.3f, chest - Vector3.up * 0.3f);
                    }
                }
            });
            meter.End();
            hitFilm.End();
            closeFilm.End();
            Say($"G1 brightness off → on (0–1): the Lure in the charge {chargeLure.off:0.000} → {chargeLure.on:0.000}; burning: the Lure {glowLure.off:0.000} → {glowLure.on:0.000}, the seabed along the path {glowFloor.off:0.000} → {glowFloor.on:0.000}, the diver {glowDiver.off:0.000} → {glowDiver.on:0.000}");
            Check(meteredCharge && chargeLure.on > chargeLure.off * 1.2f + 0.003f, $"G1 the charge's glow lights the Lure ({chargeLure.off:0.000} → {chargeLure.on:0.000})");
            Check(meteredBurn && glowLure.on > glowLure.off * 1.2f + 0.003f, $"G1 the burn lights the Lure ({glowLure.off:0.000} → {glowLure.on:0.000})");
            Check(glowFloor.on > glowFloor.off * 1.3f + 0.005f, $"G1 the burn lights the seabed along its path ({glowFloor.off:0.000} → {glowFloor.on:0.000})");
            Check(glowDiver.on > glowDiver.off * 1.2f + 0.003f, $"G1 the burn lights the diver and the ground round him ({glowDiver.off:0.000} → {glowDiver.on:0.000})");
            Say($"A1 filmed {hitFilm.Frames} frames into Temp/lure-film/beam-hit-{tag}");
            Say($"A1 it began the charge {lure.ServerAimStartYawError:0.0}° off the lamp");
            Check(lure.ServerAimStartYawError <= 12.5f, $"A1 it turned onto the lamp before charging ({lure.ServerAimStartYawError:0.0}° off)");
            CheckBeam(hit, "A1", expectHit: true);
            yield return Expect(() => lure.Pose == CreaturePose.Hunting, 2f, () => "A1 back to the hunt after the recovery (" + lure.ServerStatus + ")");

            // ---------------------------------------------------------------------------
            Heading("W1 — a wall: a wall goes up between them during the charge; the beam stops on it, drawn and judged, no hurt");
            Heal();
            yield return Expect(() => bolts.ServerCharging, s.LureShotCooldownSeconds + 4f, () => "W1 the next charge (" + lure.ServerStatus + ")");
            GameObject wall = null;
            Vector3 lureAt = lure.transform.position;
            Vector3 toHost = FlatDir(host.transform.position - lureAt);
            float wallAt = 4f;
            var walled = new BeamRecord();
            float worstOnWall = 0f; int onWallFrames = 0;
            var wallFilm = new Film("Temp/lure-film/beam-wall-" + tag);
            yield return RecordBeam(lure, walled, t =>
            {
                FilmSide(wallFilm, lure, host.transform.position);
                if (wall == null && t > 0.2f) wall = Wall(Ground(lureAt + toHost * wallAt) + Vector3.up * 1.5f, toHost, new Vector3(6f, 3.4f, 0.4f));
                if (wall != null && bolts.ServerFiring)
                {
                    // The end sits on the wall's near face.
                    float along = Vector3.Dot(bolts.ServerTo - lureAt, toHost);
                    worstOnWall = Mathf.Max(worstOnWall, Mathf.Abs(along - (wallAt - 0.2f)));
                    onWallFrames++;
                }
            });
            wallFilm.End();
            Say($"W1 the beam's end against the wall's face: worst {worstOnWall * 100f:0.0} cm over {onWallFrames} frames");
            Check(onWallFrames > 10 && worstOnWall < 0.35f, $"W1 the beam ends on the wall ({worstOnWall * 100f:0.0} cm off its face)");
            CheckBeam(walled, "W1", expectHit: false, expectSplash: true);
            foreach (GameObject p in props) if (p != null) Object.Destroy(p);
            props.Clear();
            Physics.SyncTransforms();

            // ---------------------------------------------------------------------------
            Heading("M1 — a miss: once it charges, the host is out of the beam's reach; it burns after him and falls short");
            Heal();
            yield return Expect(() => lure.Pose == CreaturePose.Hunting && !bolts.ServerAiming, 4f, () => "M1 hunting again (" + lure.ServerStatus + ")");
            yield return HostAt(P(6f), lure.transform.position);
            var missed = new BeamRecord();
            bool moved = false;
            float missGap = float.PositiveInfinity;
            yield return RecordBeam(lure, missed, t =>
            {
                if (!moved && bolts.ServerCharging && t > 0.3f)
                {
                    // Along the lane, past the beam's reach (it still turns after him).
                    Vector3 far = Ground(lure.transform.position + lane * (s.BeamRangeMeters + 6f));
                    Say($"M1 the host goes to {far:F1}, {Flat(far, lure.transform.position):0.0} m off");
                    host.TeleportLocal(far, host.Yaw);
                    moved = true;
                }
                MonsterBeamView view = lure.GetComponentInChildren<MonsterBeamView>();
                if (bolts.ServerFiring && view != null) missGap = Mathf.Min(missGap, SegmentToPoint(view.ShownFrom, view.ShownTo, CreatureSenses.Chest(host)));
            });
            Check(moved, "M1 the host left the beam's reach during the charge");
            Check(missGap > 1f, $"M1 the drawn beam never reached him ({missGap:0.0} m short at the closest)");
            CheckBeam(missed, "M1", expectHit: false);

            // ---------------------------------------------------------------------------
            Heading("D1 — lamps off during the charge: the beam still burns (only a wall or a dash saves you) and it stays planted");
            Heal();
            yield return HostAt(P(6f), lure.transform.position);
            yield return Expect(() => bolts.ServerCharging, s.LureShotCooldownSeconds + 6f, () => "D1 the next charge (" + lure.ServerStatus + ")");
            var dark = new BeamRecord();
            bool off = false;
            // N1: once the lamp is off, a second Lure beside the beam's path, 2.5 m off it, in its light.
            // The beam's light must never draw a Lure (its senses read the lamp bit, not a Light).
            Lure bystander = null;
            MonsterBeamLight darkLight = lure.GetComponent<MonsterBeamLight>();
            float bystanderLitReach = float.PositiveInfinity;
            bool bystanderStirred = false;
            string bystanderNote = string.Empty;
            yield return RecordBeam(lure, dark, t =>
            {
                if (!off && t > 0.2f) { host.RequestLamp(false); off = true; }
                if (off && bystander == null && !host.LampOn && bolts.ServerCharging)
                {
                    Vector3 a = lure.transform.position, b = host.transform.position;
                    Vector3 across = Vector3.Cross(Vector3.up, FlatDir(b - a));
                    Vector3 at = Ground(Vector3.Lerp(a, b, 0.5f) + across * 2.5f);
                    bystander = (Lure)MonsterRoster.ServerSpawnForChecks(MonsterKind.Lure, at);
                    bystander.ServerPlaceForChecks(at, YawTo(at, Vector3.Lerp(a, b, 0.5f)));
                }
                if (bystander != null)
                {
                    if (bystander.TargetId != -1 || bystander.ServerAims > 0 || bystander.Pose != CreaturePose.Idle)
                    {
                        if (!bystanderStirred) bystanderNote = $"at +{t:0.00} s: {bystander.ServerStatus}";
                        bystanderStirred = true;
                    }
                    if (darkLight != null && bolts.ServerFiring)
                        for (int i = 0; i < darkLight.PathCapacity; i++)
                        {
                            Light l = darkLight.PathLight(i);
                            if (l != null && l.enabled) bystanderLitReach = Mathf.Min(bystanderLitReach, Vector3.Distance(l.transform.position, bystander.EyePoint) / l.range);
                        }
                }
            });
            Check(!host.LampOn, "D1 the host's lamp went off during the charge");
            CheckBeam(dark, "D1", expectHit: true);
            Check(bystander != null, "N1 a second Lure stood beside the beam's path");
            Say($"N1 the second Lure stood at {bystanderLitReach:0.00} of a lit path light's reach; {(bystanderStirred ? "it stirred " + bystanderNote : "it never stirred")}");
            Check(bystanderLitReach < 0.8f, $"N1 it stood in the beam's light ({bystanderLitReach:0.00} of a path light's reach)");
            // (Short: the first Lure must still remember the lamp for F1, LureForgetSeconds after it last saw it.)
            yield return Wait(1.0f);
            Check(!bystanderStirred && bystander.TargetId == -1 && bystander.ServerAims == 0 && bystander.GetComponent<CreatureBolts>().ServerFired == 0 && bystander.Pose == CreaturePose.Idle,
                $"N1 the beam's light drew no Lure: the second stayed idle, no target, no beam ({bystander.ServerStatus}) {bystanderNote}");
            if (bystander != null && bystander.IsSpawned) bystander.NetworkObject.Despawn();
            yield return Wait(0.3f);

            // ---------------------------------------------------------------------------
            Heading("F1 — lamps off: it drifts to where it last saw the lamp, then loses you; the drift eases, weaves and walks where it faces, feet planted");
            Heal();
            // The host steps away in the dark so the drift has room; it goes to where it saw the lamp.
            yield return HostAt(P(12f, 6f), lure.transform.position);
            yield return Expect(() => lure.Pose == CreaturePose.Drawn, 3f, () => "F1 drawn to the last lit spot (" + lure.ServerStatus + ")");
            // Its last sight of the lamp was the host at P(6): send it further by lighting the lamp far away for a moment.
            yield return HostAt(P(20f), lure.transform.position);
            yield return Lamp(true);
            yield return Expect(() => lure.TargetId == host.OwnerId && (lure.Pose == CreaturePose.Hunting || bolts.ServerAiming), 3f, () => "F1 it sees the far lamp (" + lure.ServerStatus + ")");
            if (bolts.ServerAiming) yield return Expect(() => !bolts.ServerAiming && !lure.ServerRecovering, s.BeamChargeSeconds + s.BeamSeconds + 2f, () => "F1 the beam in the way ends");
            yield return Lamp(false);
            Heal();
            yield return HostAt(P(20f, 8f), lure.transform.position);
            yield return Expect(() => lure.Pose == CreaturePose.Drawn && lure.ServerDriftSpeed > 0.05f, 3f, () => "F1 drifting in the dark (" + lure.ServerStatus + ")");
            // The drift: sampled a frame at a time.
            var driftFilm = new Film("Temp/lure-film/drift");
            var speeds = new List<float>(); var crab = new List<float>(); var planted = new List<float>(); var rates = new List<float>();
            var sideways = new List<float>();
            Transform toeL = Named(lure, "ToeL"), toeR = Named(lure, "ToeR");
            Vector3 lp = lure.transform.position, tl = toeL.position, tr = toeR.position;
            Vector3 lineFrom = lure.transform.position, lineTo = P(20f);
            float lowL = float.PositiveInfinity, lowR = float.PositiveInfinity;
            float cruise = host.WalkSpeed * s.LureApproachSpeedFactor;
            int lastFrame = Time.frameCount;
            var crabNotes = new List<string>();
            for (float t = 0f; t < 3.5f && lure.Pose == CreaturePose.Drawn; t += Time.deltaTime)
            {
                yield return null;
                float dt = Time.deltaTime;
                // One player frame since the last look, or the step and dt do not match.
                bool oneFrame = Time.frameCount == lastFrame + 1;
                lastFrame = Time.frameCount;
                if (dt <= 0f || !oneFrame) { lp = lure.transform.position; tl = toeL.position; tr = toeR.position; continue; }
                Vector3 p = lure.transform.position;
                float v = Flat(p, lp) / dt;
                speeds.Add(v);
                if (v > 0.3f)
                {
                    float a = Vector3.Angle(FlatDir(p - lp), FlatDir(lure.transform.forward));
                    crab.Add(a);
                    if (a > 20f && crabNotes.Count < 6) crabNotes.Add($"t={t:0.00} v={v:0.00} angle={a:0} {lure.ServerDriftNote}");
                }
                sideways.Add(SegmentToPoint(new Vector3(lineFrom.x, 0f, lineFrom.z), new Vector3(lineTo.x, 0f, lineTo.z), new Vector3(p.x, 0f, p.z)));
                // Heights over its own feet (the seabed is not flat).
                float hL = toeL.position.y - p.y, hR = toeR.position.y - p.y;
                lowL = Mathf.Min(lowL, hL); lowR = Mathf.Min(lowR, hR);
                // The planted toe: the lower one, within 3 cm of its lowest, while the body is at speed.
                bool leftLow = hL < hR;
                Transform low = leftLow ? toeL : toeR;
                float lowest = leftLow ? lowL : lowR;
                Vector3 last = leftLow ? tl : tr;
                if (v > cruise * 0.7f && (leftLow ? hL : hR) < lowest + 0.03f && t > 0.6f) planted.Add(Flat(low.position, last) / dt);
                if (v > cruise * 0.7f) rates.Add(rig.PlaybackRate);
                lp = p; tl = toeL.position; tr = toeR.position;
                Vector3 side = Vector3.Cross(Vector3.up, FlatDir(lure.transform.forward));
                driftFilm.Shot(p + side * 4.5f + Vector3.up * 1.4f - FlatDir(lure.transform.forward) * 1.0f, p + Vector3.up * 1.0f);
            }
            driftFilm.End();
            float ramp = speeds.Take(Mathf.Min(6, speeds.Count)).DefaultIfEmpty(0f).Max();
            float top = speeds.DefaultIfEmpty(0f).Max();
            planted.Sort();
            float plantedMedian = planted.Count > 0 ? planted[planted.Count / 2] : float.NaN;
            float worstCrab = crab.DefaultIfEmpty(0f).Max();
            Say($"F1 drift: first frames up to {ramp:0.00} m/s, top {top:0.00} (cruise {cruise:0.00}); heading against path worst {worstCrab:0.0}°; weave up to {sideways.DefaultIfEmpty(0f).Max():0.00} m; planted toe median {plantedMedian:0.000} m/s over {planted.Count} frames; playback {rates.DefaultIfEmpty(1f).Average():0.00}× (authored {rig.AuthoredSpeed(CreaturePose.Drawn):0.00} m/s)");
            Check(top > cruise * 0.8f && top < cruise * 1.15f, $"F1 it drifts at its speed ({top:0.00} m/s, cruise {cruise:0.00})");
            foreach (string note in crabNotes) Say("F1 crab: " + note);
            Check(worstCrab < 20f, $"F1 it moves where it faces (worst {worstCrab:0.0}°): no crab, no moonwalk");
            Check(InState(lure, "Drawn") || lure.Pose != CreaturePose.Drawn, "F1 the Animator plays Drawn");
            Check(planted.Count >= 8, $"F1 the feet were sampled planted ({planted.Count} frames)");
            Check(plantedMedian < 0.3f, $"F1 the planted foot stays put (median {plantedMedian:0.000} m/s against a body at {top:0.00}): no slide");
            float expectRate = top / Mathf.Max(0.01f, rig.AuthoredSpeed(CreaturePose.Drawn));
            Check(rates.Count > 0 && Mathf.Abs(rates.Average() - expectRate) < 0.2f, $"F1 the walk plays at the ground speed over its authored one ({rates.DefaultIfEmpty(0f).Average():0.00}× ≈ {expectRate:0.00}×)");
            yield return Expect(() => lure.Pose == CreaturePose.Idle && lure.TargetId == -1, s.LureForgetSeconds + 4f, () => "F1 in the dark it loses you and goes idle (" + lure.ServerStatus + ")");
            float stoppedFrom = Flat(lure.transform.position, P(20f));
            Say($"F1 it stopped {stoppedFrom:0.0} m from the last lit spot (standoff {s.ShooterStandoffMeters})");
            Check(stoppedFrom > s.ShooterStandoffMeters - 0.6f, "F1 it kept its standoff: it shoots, it does not touch");
            int firedDark = bolts.ServerFired;
            yield return Wait(2f);
            Check(bolts.ServerFired == firedDark && lure.Pose == CreaturePose.Idle, "F1 no beam at a dark diver");

            // ---------------------------------------------------------------------------
            Heading("R1 — repeated cycles: three beams at a lit diver standing still; each hits once, no sticking");
            yield return HostAt(P(4f), lure.transform.position);
            lure.ServerPlaceForChecks(P(-8f), YawTo(P(-8f), P(4f)));
            yield return Lamp(true);
            float lastCharge = float.NegativeInfinity;
            for (int cycle = 1; cycle <= 3; cycle++)
            {
                Heal();
                var r = new BeamRecord();
                yield return RecordBeam(lure, r);
                CheckBeam(r, "R1." + cycle, expectHit: true);
                if (cycle > 1)
                {
                    float gap = r.ChargedAt - lastCharge;
                    float least = s.BeamChargeSeconds + s.BeamSeconds + s.LureShotCooldownSeconds;
                    Check(gap >= least - 0.05f && gap < least + 1.5f, $"R1.{cycle} the next beam came {gap:0.00} s after the last (≥ {least:0.00}, the cooldown kept, no stall)");
                }
                lastCharge = r.ChargedAt;
            }

            // ---------------------------------------------------------------------------
            Heading("T2 — two beams at once: two Lures burn together; the pool lends each its own lights and has them all back, and again when both are despawned mid-burn");
            yield return Lamp(false);
            M.ServerDespawnMonsters();
            yield return Wait(0.5f);
            int per = 0;
            {
                Check(MonsterBeamLight.PoolRented == 0 && MonsterBeamLight.PoolLit == 0, $"T2 the pool starts with nothing lent and nothing lit (rented {MonsterBeamLight.PoolRented}, lit {MonsterBeamLight.PoolLit})");
                Heal();
                Lure one = (Lure)MonsterRoster.ServerSpawnForChecks(MonsterKind.Lure, P(-8f, -2.5f));
                Lure two = (Lure)MonsterRoster.ServerSpawnForChecks(MonsterKind.Lure, P(-8f, 2.5f));
                one.ServerPlaceForChecks(P(-8f, -2.5f), YawTo(P(-8f, -2.5f), P(4f)));
                two.ServerPlaceForChecks(P(-8f, 2.5f), YawTo(P(-8f, 2.5f), P(4f)));
                CreatureBolts b1 = one.GetComponent<CreatureBolts>(), b2 = two.GetComponent<CreatureBolts>();
                MonsterBeamLight l1 = one.GetComponent<MonsterBeamLight>(), l2 = two.GetComponent<MonsterBeamLight>();
                per = l1.PathCapacity + 1;
                yield return HostAt(P(4f), P(-8f));
                yield return Lamp(true);
                yield return Expect(() => b1.ServerFiring && b2.ServerFiring, 8f, () => $"T2 both burn at once ({one.ServerStatus} | {two.ServerStatus})");
                yield return null; yield return null;
                int rentedBoth = MonsterBeamLight.PoolRented, litBoth = MonsterBeamLight.PoolLit;
                Say($"T2 both burning: pool rented {rentedBoth}, lit {litBoth} (the first {l1.LitCount}, the second {l2.LitCount}), created {MonsterBeamLight.PoolCreated}");
                Check(l1.Holding && l2.Holding && rentedBoth == 2 * per, $"T2 each beam holds its own lights ({rentedBoth} lent = 2 × {per})");
                Check(l1.PathLitCount >= 2 && l2.PathLitCount >= 2 && litBoth == l1.LitCount + l2.LitCount, $"T2 both paths are lit ({l1.PathLitCount} and {l2.PathLitCount} path lights; {litBoth} on in the pool)");
                yield return Lamp(false);
                yield return Expect(() => !b1.ServerAiming && !b2.ServerAiming, s.BeamSeconds + 1f, () => "T2 both beams end");
                yield return Expect(() => MonsterBeamLight.PoolRented == 0, 1.5f, () => $"T2 all lights back when both are out (rented {MonsterBeamLight.PoolRented})");
                Check(MonsterBeamLight.PoolLit == 0 && !l1.Holding && !l2.Holding, $"T2 and all off (pool lights on {MonsterBeamLight.PoolLit})");
                Check(MonsterBeamLight.PoolFree >= 2 * per && MonsterBeamLight.PoolCreated <= 2 * per, $"T2 the pool grew only to the most lit at once (created {MonsterBeamLight.PoolCreated}, free {MonsterBeamLight.PoolFree})");
                // Again, and despawned while both burn.
                Heal();
                yield return Lamp(true);
                yield return Expect(() => b1.ServerFiring && b2.ServerFiring, s.LureShotCooldownSeconds + 6f, () => $"T2 both burn again ({one.ServerStatus} | {two.ServerStatus})");
                yield return null;
                Check(MonsterBeamLight.PoolRented == 2 * per, $"T2 lent again from the pool, none new (rented {MonsterBeamLight.PoolRented}, created {MonsterBeamLight.PoolCreated})");
                M.ServerDespawnMonsters();
                yield return null; yield return null;
                Check(MonsterBeamLight.PoolRented == 0 && MonsterBeamLight.PoolLit == 0, $"T2 despawned mid-burn: every light back and off (rented {MonsterBeamLight.PoolRented}, lit {MonsterBeamLight.PoolLit})");
                Check(MonsterBeamLight.PoolCreated <= 2 * per, $"T2 still no light made beyond the two beams' ({MonsterBeamLight.PoolCreated})");
                yield return Lamp(false);
            }
            yield return Lamp(false);
            M.ServerDespawnMonsters();
            Heal();
        }
    }
}
