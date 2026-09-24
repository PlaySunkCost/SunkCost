using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace SunkCost.Player
{
    // Held by a monster (Dan, 24 September 2026: the Long Walker lifts you to its face
    // for about two seconds, then you die; the Weeping Angel embraces you). The server
    // writes the hold (ServerGrab, ServerReleaseGrab); nothing here decides it. The
    // owner, who simulates its own movement (contract section 3), obeys: controls
    // lock, the capsule goes off, the root follows the holder's hold point and the
    // camera is turned to the holder's face with a shake. The eyes come from the
    // same numbers on every peer (EyePose), so a spectator and the deck TV, which
    // render this player's EyePose, see the same view. The release puts everything
    // back: the capsule on (unless dead or otherwise locked), the camera at rest,
    // the look where the hold left it.
    public sealed partial class HQPlayerController
    {
        // A lost release never leaves a diver locked: the owner lets go by itself this long after the catch.
        private const float GrabGiveUpSeconds = 20f;

        private readonly SyncVar<GrabHold> grabHold = new(new GrabHold { HolderId = -1 });
        private bool grabbedLocal;           // owner: the lock is applied
        private int grabbedLocalSerial = -1;
        private Vector3 grabFromLocal;       // owner: where it stood when the hold reached it
        private Vector3 grabCameraRest;
        private int holderLookupId = -1;
        private IGrabHolder holderLookup;

        public bool IsGrabbed => grabHold.Value.Active;
        public GrabHold Grab => grabHold.Value;
        public bool GrabbedLocal => grabbedLocal;
        // Owner diagnostics for the checks: holds felt and let go of.
        public int GrabsFelt { get; private set; }
        public int GrabsReleased { get; private set; }
        public float GrabSeconds => IsGrabbed ? TicksSince(grabHold.Value.StartTick) : 0f;
        public string GrabStatus => $"grabbed={IsGrabbed} local={grabbedLocal} holder={grabHold.Value.HolderId} t={GrabSeconds:0.00} capsule={(controller != null && controller.enabled)}";

        // ---- the server's word -----------------------------------------------------------

        [Server]
        public void ServerGrab(NetworkObject holder, Vector3 caughtAt)
        {
            if (holder == null || dead.Value) return;
            uint tick = NetworkManager != null && NetworkManager.TimeManager != null ? NetworkManager.TimeManager.Tick : 0u;
            grabHold.Value = new GrabHold { Serial = grabHold.Value.Serial + 1, Active = true, HolderId = holder.ObjectId, StartTick = tick, CaughtAt = caughtAt };
        }

        [Server]
        public void ServerReleaseGrab()
        {
            if (!grabHold.Value.Active) return;
            grabHold.Value = new GrabHold { Serial = grabHold.Value.Serial + 1, Active = false, HolderId = -1 };
        }

        // Every frame on the server: a hold whose holder is gone or no longer holds this
        // diver (a despawn, a site unload) is let go of.
        [Server]
        private void ServerCheckGrab()
        {
            if (!grabHold.Value.Active) return;
            IGrabHolder holder = Holder();
            if (holder == null || !holder.ServerHolds(this) || GrabSeconds > GrabGiveUpSeconds) ServerReleaseGrab();
        }

        // ---- every peer: where the held eyes are -------------------------------------------

        // The holder by its object id on this peer (the server's objects on the host).
        private IGrabHolder Holder()
        {
            int id = grabHold.Value.HolderId;
            if (id < 0) return null;
            if (id == holderLookupId && holderLookup is Object alive && alive != null) return holderLookup;
            holderLookupId = id;
            holderLookup = null;
            if (NetworkManager == null) return null;
            NetworkObject nob = null;
            if (IsServerStarted) NetworkManager.ServerManager.Objects.Spawned.TryGetValue(id, out nob);
            if (nob == null && IsClientStarted) NetworkManager.ClientManager.Objects.Spawned.TryGetValue(id, out nob);
            holderLookup = nob != null ? nob.GetComponent<IGrabHolder>() : null;
            return holderLookup;
        }

        // Seconds since a server tick, whole ticks and the part of this one (as the car's).
        private float TicksSince(uint startTick)
        {
            if (NetworkManager == null || NetworkManager.TimeManager == null) return 0f;
            FishNet.Managing.Timing.TimeManager time = NetworkManager.TimeManager;
            uint now = time.Tick;
            if (now < startTick) return 0f;
            return (float)(time.TicksToTime(now - startTick) + time.GetTickElapsedAsDouble());
        }

        // The hold's feet and eyes this frame, when held and the holder is on this peer.
        private bool GrabbedPose(out Vector3 feet, out Vector3 eye, out Quaternion look)
        {
            feet = eye = Vector3.zero; look = Quaternion.identity;
            GrabHold hold = grabHold.Value;
            if (!hold.Active) return false;
            IGrabHolder holder = Holder();
            if (holder == null) return false;
            Vector3 from = IsOwner && grabbedLocal ? grabFromLocal : hold.CaughtAt;
            float seconds = TicksSince(hold.StartTick);
            GrabPose pose = holder.HoldPose(from, seconds);
            feet = pose.Feet;
            eye = feet + Vector3.up * Movement.StandingEyeHeight;
            Vector3 to = pose.Face - eye;
            Quaternion toFace = to.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(to, Vector3.up) : Quaternion.Euler(0f, Yaw, 0f);
            look = toFace * GrabShake(seconds, pose.ShakeDegrees, pose.ShakeHz, hold.Serial);
            return true;
        }

        // A deterministic shake (the same on every peer for the same hold): smooth noise on
        // pitch, yaw and a little more on roll.
        private static Quaternion GrabShake(float seconds, float degrees, float hz, int seed)
        {
            if (degrees <= 0f) return Quaternion.identity;
            float t = seconds * Mathf.Max(0.01f, hz);
            float s = (seed % 97) * 1.37f;
            float p = (Mathf.PerlinNoise(t, 0.31f + s) - 0.5f) * 2f;
            float y = (Mathf.PerlinNoise(0.73f + s, t) - 0.5f) * 2f;
            float r = (Mathf.PerlinNoise(t + 11.1f, 5.3f + s) - 0.5f) * 2f;
            return Quaternion.Euler(p * degrees, y * degrees, r * degrees * 1.4f);
        }

        // ---- the owner obeys ------------------------------------------------------------------

        // Owner, first thing each frame: the lock follows the replicated hold.
        private void SyncGrabbedLocal()
        {
            GrabHold hold = grabHold.Value;
            bool want = hold.Active && !dead.Value;
            if (want && grabbedLocal && TicksSince(hold.StartTick) > GrabGiveUpSeconds)
            {
                Debug.LogWarning("[Grab] no release from the server after " + GrabGiveUpSeconds + " s; letting go locally");
                want = false;
            }
            if (want && grabbedLocalSerial == hold.Serial && !grabbedLocal) want = false; // let go locally: wait for a new hold
            if (want == grabbedLocal) return;
            if (want) BeginGrabbedLocal(hold.Serial); else EndGrabbedLocal();
        }

        private void BeginGrabbedLocal(int serial)
        {
            grabbedLocal = true;
            grabbedLocalSerial = serial;
            grabFromLocal = transform.position;
            GrabsFelt++;
            if (playerCamera != null) grabCameraRest = playerCamera.transform.localPosition;
            controller.enabled = false; // the hold carries the root; the capsule would fight it
            stance?.SetDesiredCrouch(false);
            verticalSpeed = 0f;
            externalMotion = Vector3.zero;
            dashUntil = float.NegativeInfinity;
            lastFlags = CollisionFlags.None;
            ClearCommandsForLock();
        }

        private void EndGrabbedLocal()
        {
            grabbedLocal = false;
            GrabsReleased++;
            if (playerCamera != null)
            {
                playerCamera.transform.localPosition = grabCameraRest;
                playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f); // the look the hold left, unshaken
            }
            bool on = !dead.Value && !travelLocked && !seatedLocal;
            if (on && !controller.enabled) Physics.SyncTransforms(); // never over a stale pose (TeleportLocal)
            controller.enabled = on;
            verticalSpeed = 0f; // let go of in the air, it falls
            lastFlags = CollisionFlags.None;
            clearance?.ResetView();
            ClearCommandsForLock();
        }

        private void ClearCommandsForLock()
        {
            CurrentSeat = null;
            CurrentTarget = null;
            CurrentButton = null;
            CurrentColourPanel = null;
            CurrentQuotaBoard = null;
            CurrentShopDisplay = null;
            CurrentTv = null;
            CurrentPatient = null;
            CurrentCabinControl = CabinControl.None;
            ClearPatch();
            grabBufferedUntil = -1f;
            grabConsumed = true;
            jumpBufferedUntil = float.NegativeInfinity;
        }

        // Owner, in Update while held: no look, move, items or targets.
        private void GrabbedFrame() => ClearCommandsForLock();

        // Owner, in LateUpdate while held: the root to the hold point, facing the holder;
        // the camera to the held eyes. The look the hold gives becomes the owner's own, so
        // a release leaves the view where it was.
        private void HoldGrabbedOwner()
        {
            if (!grabbedLocal) return;
            if (!GrabbedPose(out Vector3 feet, out Vector3 eye, out Quaternion look)) return;
            Vector3 forward = look * Vector3.forward;
            Vector3 flat = new(forward.x, 0f, forward.z);
            Quaternion yaw = flat.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(flat, Vector3.up) : transform.rotation;
            transform.SetPositionAndRotation(feet, yaw);
            float lookPitch = -Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(lookPitch, -80f, 80f);
            if (playerCamera != null) playerCamera.transform.SetPositionAndRotation(eye, look);
        }
    }
}
