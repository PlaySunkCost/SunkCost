using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using SunkCost.Interaction;
using SunkCost.Net;
using SunkCost.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SunkCost.Player
{
    // The couch and the TV (Dan, 23 September 2026): "When sitting on couch, it zoom
    // in on tv (95% screen is tv, rest is behind the tv)", and the TV's screen
    // answers E from 6 m (SHIP-043).
    //
    // Sitting is server-decided like every other press (contract section 3): E on a
    // couch asks with the seat's number (ServerRequestSit); the server checks the
    // sitter is alive, on the ship and not riding, within reach, and that nobody
    // else sits there, then writes `seat` (a byte, 0 = standing). The owner does not
    // sit before the server says yes. It then places itself - movement is the
    // owner's, the NetworkTransform carries it - onto the seat point with the
    // capsule off (the ship rider's way of holding a root; no second writer), turns
    // toward the screen and zooms: the zoom is owner-local presentation. Every other
    // peer poses its copy from `seat` (eyes low, the body squashed to a sitting
    // height: the model has no rig), so a friend, a spectator and the host all see
    // the sitter on the couch. A spectator watching a sitter gets the same zoom, from
    // the same geometry on its own machine (design §4: "their screen exactly");
    // nothing about it is sent. Standing up (E, a movement key, Space or Ctrl) is
    // the owner's move and a ServerRequestStand; the server also stands anyone whose
    // seat stops making sense (dead, below, riding, off the ship, away from the seat)
    // four times a second. A sail keeps the sitter seated: the rider carries the
    // seated root like any other, and seat numbers are the same on both ships.
    public sealed partial class HQPlayerController
    {
        private readonly SyncVar<byte> seat = new(0);
        private const float SeatReachMargin = 2f;         // the seat point sits behind the couch's front face the dot rests on
        private const float ServerReachMargin = 1.5f;     // as the shop's: copies are a tick behind
        private const float SeatCheckInterval = 0.25f;
        private const float SeatServerGraceSeconds = 1.5f; // the owner's walk onto the seat after the yes
        private const float SeatServerTolerance = 1.5f;
        private const float SeatRemoteTolerance = 1f;
        private const float SeatRequestTimeout = 2f;
        private const float SeatRefusalSeconds = 2.5f;

        private static readonly List<HQPlayerController> Everyone = new();

        // Every peer: the seat number the server wrote (0 = standing).
        public int SeatNumber => seat.Value;
        // Every peer: this copy is shown sitting (the owner's own state, a remote copy's
        // replicated one once it is at its seat).
        public bool SeatedPose => IsOwner ? seatedLocal : remoteSeatedPose;
        // Owner: sitting now.
        public bool IsSeated => seatedLocal;
        // Owner: the couch seat under the dot within reach (E sits), or null.
        public Transform CurrentSeat { get; private set; }
        public bool CurrentSeatTaken => CurrentSeat != null && SeatTakenByOther(ShipSeats.NumberOf(CurrentSeat));
        public bool SeatUsable => CurrentSeat != null && !CurrentSeatTaken && !seatedLocal;
        public string SeatRefusal => Time.unscaledTime < seatRefusalUntil ? seatRefusal : string.Empty;
        // What the prompt says about the couch this frame (the HUD shows it; empty when
        // there is nothing to say).
        public string SeatPrompt
        {
            get
            {
                if (seatedLocal)
                {
                    CrewDayState day = CrewDayState.Instance;
                    return day != null && day.TvChannel >= 0 ? "E to stand up · left click for the next diver" : "E to stand up";
                }
                string refusal = SeatRefusal;
                if (!string.IsNullOrEmpty(refusal)) return refusal;
                if (CurrentSeat == null) return string.Empty;
                return CurrentSeatTaken ? "Seat taken" : "Press E to sit";
            }
        }
        // Owner: what the dot rests on that can be used, for the highlight (SHIP-054);
        // meaningful only while one of the Current* targets is set.
        public Transform CurrentUsable { get; private set; }
        // Owner: the TV's screen answers from TvReach, everything else from InteractReach.
        public float TvReach => Movement.TvReach;

        // Owner state.
        private bool seatedLocal;
        private Transform ownerSeat, ownerTv;
        private int seatedNumber;
        private float seatedAt;
        private Vector3 sitFromPosition;
        private float sitFromYaw, sitFromPitch;
        private bool sitRequested;
        private int requestedSeat;
        private float sitRequestedAt;
        private string seatRefusal = string.Empty;
        private float seatRefusalUntil = -1f;
        // The zoom (the owner's camera: its own seat, or the seat of the player it spectates).
        private float baseFieldOfView = -1f;
        private float zoomAmount;       // 0..1
        private float zoomFieldOfView;  // the framed view the zoom goes to (kept while it goes back)
        private bool fovWritten;
        // Remote copies: shown seated, checked four times a second against the seat.
        private bool remoteSeatedPose;
        private float nextRemoteSeatCheck;
        private Transform remoteSeat, remoteTv;
        // Server.
        private float nextSeatCheck;
        private float seatAcceptedAt;

        private void OnEnable() { if (!Everyone.Contains(this)) Everyone.Add(this); }
        private void OnDisable() => Everyone.Remove(this);

        private bool SeatTakenByOther(int number)
        {
            if (number == 0) return false;
            foreach (HQPlayerController other in Everyone)
                if (other != this && other != null && other.IsSpawned && other.seat.Value == number && !other.IsDead) return true;
            return false;
        }

        // ---- the owner asks --------------------------------------------------------------

        private void RequestSit(Transform seatMarker)
        {
            int number = ShipSeats.NumberOf(seatMarker);
            if (!IsOwner || number == 0 || dead.Value || seatedLocal) return;
            if (SeatTakenByOther(number)) { RefuseSeat("Seat taken"); return; }
            sitRequested = true;
            requestedSeat = number;
            sitRequestedAt = Time.unscaledTime;
            ServerRequestSit((byte)number);
        }

#if UNITY_EDITOR
        // The matrices' way in: the same request E on that seat makes, and the stand.
        public void RequestSitForChecks(int number)
        {
            Transform marker = ShipSeats.Seat(ShipParts.InScene(gameObject.scene), number);
            if (marker != null) RequestSit(marker);
        }
        public void StandForChecks() => LeaveSeat(place: true, tellServer: true);
#endif

        private void RefuseSeat(string why)
        {
            seatRefusal = why;
            seatRefusalUntil = Time.unscaledTime + SeatRefusalSeconds;
        }

        [ServerRpc]
        private void ServerRequestSit(byte number, NetworkConnection sender = null)
        {
            if (sender != Owner) return;
            if (!ServerMaySit(number, out string why)) { TargetSeatRefused(Owner, why); return; }
            seat.Value = number;
            seatAcceptedAt = Time.unscaledTime;
        }

        [ServerRpc]
        private void ServerRequestStand(NetworkConnection sender = null)
        {
            if (sender != Owner) return;
            seat.Value = 0;
        }

        [TargetRpc]
        private void TargetSeatRefused(NetworkConnection target, string why)
        {
            sitRequested = false;
            RefuseSeat(why);
        }

        // ---- the server decides ----------------------------------------------------------

        [Server]
        private bool ServerMaySit(int number, out string why)
        {
            why = string.Empty;
            CrewDayState day = CrewDayState.Instance;
            if (dead.Value || (day != null && day.IsDead(OwnerId))) { why = "The dead sit nowhere"; return false; }
            if (day != null && (day.Travelling || day.IsRider(OwnerId) || day.IsBelow(OwnerId))) { why = "Not now"; return false; }
            ShipParts ship = ShipParts.InScene(gameObject.scene); // the server's copy stands in its world
            Transform marker = ShipSeats.Seat(ship, number);
            if (marker == null) { why = "No seat here"; return false; }
            if (Vector3.Distance(EyePosition, marker.position) > interactReach + SeatReachMargin) { why = "Step up to the couch"; return false; }
            foreach (HQPlayerController other in Everyone)
                if (other != this && other != null && other.IsSpawned && other.seat.Value == number && other.gameObject.scene == gameObject.scene) { why = "Seat taken"; return false; }
            return true;
        }

        // Four times a second: a seat that stopped making sense is given up.
        [Server]
        private void ServerCheckSeat()
        {
            if (seat.Value == 0) return;
            float now = Time.unscaledTime;
            if (now < nextSeatCheck) return;
            nextSeatCheck = now + SeatCheckInterval;
            if (ServerSeatStillHolds(now, out string why)) return;
            seat.Value = 0;
            Debug.Log("[Seat] " + SunkCost.World.WorldSceneFlow.DisplayName(Owner) + " stood up: " + why);
        }

        private bool ServerSeatStillHolds(float now, out string why)
        {
            why = string.Empty;
            CrewDayState day = CrewDayState.Instance;
            if (dead.Value || (day != null && day.IsDead(OwnerId))) { why = "dead"; return false; }
            if (day != null && day.Travelling) return true; // a sail carries the sitter; the scene move is under way
            if (day != null && (day.IsRider(OwnerId) || day.IsBelow(OwnerId))) { why = "riding or below"; return false; }
            Transform marker = ShipSeats.Seat(ShipParts.InScene(gameObject.scene), seat.Value);
            if (marker == null) { why = "no such seat in its world"; return false; }
            if (now - seatAcceptedAt > SeatServerGraceSeconds && Vector3.Distance(transform.position, marker.position) > SeatServerTolerance) { why = "away from the seat"; return false; }
            return true;
        }

        // SHIP-043: the TV's screen within its own reach of this copy's eyes, plus the
        // usual slack (a copy is a tick behind). For ShipControls' TV request.
        [Server]
        public bool ServerCanReachTv(ShipParts ship)
        {
            Transform screen = ship != null ? ship.TvScreen : null;
            Collider collider = screen != null ? screen.GetComponent<Collider>() : null;
            if (collider == null) return true; // no screen collider: the coarse aboard test stands alone, as before
            Vector3 eye = EyePosition;
            return Vector3.Distance(eye, collider.ClosestPoint(eye)) <= Movement.TvReach + ServerReachMargin;
        }

        // ---- the owner sits and stands ---------------------------------------------------

        // Every owner frame: the server's yes becomes a seat, the server's no (or a seat
        // that is gone with its ship) stands the player up.
        private void PollSeat()
        {
            if (sitRequested)
            {
                if (seat.Value == requestedSeat && !seatedLocal) { sitRequested = false; SitLocal(requestedSeat); }
                else if (Time.unscaledTime > sitRequestedAt + SeatRequestTimeout) sitRequested = false;
            }
            if (!seatedLocal) return;
            if (ownerSeat == null || ownerSeat.gameObject.scene != gameObject.scene)
            {
                // A sail moved the sitter to the other ship (the source is unloaded after
                // the arrival): the same seat number on this one.
                ShipParts ship = ShipParts.InScene(gameObject.scene);
                Transform again = ShipSeats.Seat(ship, seatedNumber);
                if (again == null) { LeaveSeat(place: false, tellServer: true); return; } // no ship here any more
                ownerSeat = again;
                ownerTv = ship.TvScreen;
            }
            if (seat.Value != seatedNumber && Time.unscaledTime > seatedAt + SeatServerGraceSeconds) LeaveSeat(place: true, tellServer: false);
        }

        private void SitLocal(int number)
        {
            ShipParts ship = ShipParts.InScene(gameObject.scene); // the owner's own object: its scene is its world
            Transform marker = ShipSeats.Seat(ship, number);
            if (marker == null) { ServerRequestStand(); return; }
            seatedLocal = true;
            ownerSeat = marker;
            ownerTv = ship.TvScreen;
            seatedNumber = number;
            seatedAt = Time.unscaledTime;
            sitFromPosition = transform.position;
            sitFromYaw = Yaw;
            sitFromPitch = pitch;
            controller.enabled = false; // held like a rider: the root is written, not swept
            stance?.SetDesiredCrouch(false);
            verticalSpeed = 0f;
            jumpBufferedUntil = float.NegativeInfinity;
            lastFlags = CollisionFlags.None;
            ClearTargets();
        }

        // Stand up: on the deck in front of the seat (or wherever the capsule fits), the
        // capsule back on. place: false when something else is about to place the
        // player (the server's TargetPlace) or nothing can be placed (the ship is gone).
        private void LeaveSeat(bool place, bool tellServer)
        {
            if (!seatedLocal) return;
            Transform marker = ownerSeat;
            seatedLocal = false;
            ownerSeat = null;
            ownerTv = null;
            seatedNumber = 0;
            sitRequested = false;
            if (tellServer && IsSpawned) ServerRequestStand();
            if (travelLocked) return; // the rider owns the root; the unlock turns the capsule back on
            if (place && marker != null && !dead.Value) TeleportLocal(StandSpot(marker), Yaw);
            if (!dead.Value) { if (!controller.enabled) Physics.SyncTransforms(); controller.enabled = true; }
        }

        private Vector3 StandSpot(Transform marker)
        {
            PlayerMovementSettings settings = Movement;
            Vector3 forward = Vector3.ProjectOnPlane(marker.forward, Vector3.up);
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : transform.forward;
            for (int step = 0; step < 3; step++) // a little farther out if the first spot is blocked
            {
                Vector3 front = marker.position + forward * (settings.StandForwardMeters + 0.2f * step);
                if (Physics.Raycast(front + Vector3.up * 1f, Vector3.down, out RaycastHit floor, 2.5f, CarryableCollisionPolicy.WorldMask, QueryTriggerInteraction.Ignore) && CapsuleFits(floor.point)) return floor.point;
            }
            if (CapsuleFits(sitFromPosition) && Vector3.Distance(sitFromPosition, marker.position) < interactReach + SeatReachMargin) return sitFromPosition;
            return marker.position; // on the seat itself: the couch's seat is solid (SHIP-024)
        }

        private bool CapsuleFits(Vector3 feet)
        {
            PlayerMovementSettings settings = Movement;
            float r = settings.CapsuleRadius;
            Vector3 bottom = feet + Vector3.up * (r + 0.05f), top = feet + Vector3.up * (settings.StandingHeight - r);
            return !Physics.CheckCapsule(bottom, top, r * 0.95f, CarryableCollisionPolicy.WorldMask, QueryTriggerInteraction.Ignore);
        }

        // The owner's frame while seated: onto the seat, facing the screen, and the few
        // keys a sitter has. Returns after placing the view; the caller skips the motor.
        private void SeatedFrame(bool canPlay)
        {
            ClearTargets();
            PlayerMovementSettings settings = Movement;
            float s = settings.SeatBlendSeconds <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.unscaledTime - seatedAt) / settings.SeatBlendSeconds));
            Vector3 hip = ownerSeat.position;
            if (!travelLocked) transform.position = Vector3.Lerp(sitFromPosition, hip, s); // the rider writes it while travelling
            SeatLook(hip + Vector3.up * settings.SeatedEyeHeight, ownerTv, ownerSeat, out float yaw, out float lookDown);
            transform.rotation = Quaternion.Euler(0f, Mathf.LerpAngle(sitFromYaw, yaw, s), 0f);
            pitch = Mathf.Clamp(Mathf.LerpAngle(sitFromPitch, lookDown, s), -80f, 80f);
            if (playerCamera != null) playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            clearance?.Solve();
            if (!canPlay || travelLocked) return;
            Keyboard keys = ActiveKeyboard;
            bool stand = keys.eKey.wasPressedThisFrame || keys.spaceKey.wasPressedThisFrame || keys.leftCtrlKey.wasPressedThisFrame
                || keys.wKey.wasPressedThisFrame || keys.aKey.wasPressedThisFrame || keys.sKey.wasPressedThisFrame || keys.dKey.wasPressedThisFrame;
            if (stand) { LeaveSeat(place: true, tellServer: true); return; }
            // Left click is the next diver, as it is for a spectator (the dot sits on the
            // screen; E is the way up). The same request as E on the screen.
            if (!SessionInputGate.ClickSuppressedThisFrame && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                GetComponent<ShipControls>()?.RequestTvNext();
        }

        private void ClearTargets()
        {
            CurrentTarget = null;
            CurrentButton = null;
            CurrentColourPanel = null;
            CurrentQuotaBoard = null;
            CurrentShopDisplay = null;
            CurrentTv = null;
            CurrentSeat = null;
            CurrentCabinControl = CabinControl.None;
            CurrentPatient = null;
            ClearPatch();
            grabBufferedUntil = -1f;
            grabConsumed = true;
            jumpBufferedUntil = float.NegativeInfinity;
        }

        // The way a seated head points: at the screen's centre, or along the seat
        // without a screen. Pitch in the controller's sense (positive looks down).
        // From a seat off the screen's axis the near side of the screen looks bigger:
        // the head turns to the middle of the corners' spread, not the geometric centre,
        // so the picture sits centred with a like margin each side.
        public static void SeatLook(Vector3 eye, Transform screen, Transform marker, out float yaw, out float lookDown)
        {
            Vector3 to = screen != null ? screen.position - eye : (marker != null ? marker.forward : Vector3.forward);
            Vector3 flat = new(to.x, 0f, to.z);
            yaw = flat.sqrMagnitude > 1e-6f ? Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg : (marker != null ? marker.eulerAngles.y : 0f);
            lookDown = screen != null ? -Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg : 0f;
            if (screen == null) return;
            MeshFilter filter = screen.GetComponent<MeshFilter>();
            Bounds local = filter != null && filter.sharedMesh != null ? filter.sharedMesh.bounds : new Bounds(Vector3.zero, new Vector3(1f, 1f, 0f));
            float minYaw = float.PositiveInfinity, maxYaw = float.NegativeInfinity, minDown = float.PositiveInfinity, maxDown = float.NegativeInfinity;
            for (int i = 0; i < 4; i++)
            {
                Vector3 corner = screen.TransformPoint(new Vector3(i % 2 == 0 ? local.min.x : local.max.x, i < 2 ? local.min.y : local.max.y, local.center.z)) - eye;
                Vector3 cornerFlat = new(corner.x, 0f, corner.z);
                float cornerYaw = yaw + Mathf.DeltaAngle(yaw, Mathf.Atan2(cornerFlat.x, cornerFlat.z) * Mathf.Rad2Deg);
                float cornerDown = -Mathf.Atan2(corner.y, cornerFlat.magnitude) * Mathf.Rad2Deg;
                minYaw = Mathf.Min(minYaw, cornerYaw); maxYaw = Mathf.Max(maxYaw, cornerYaw);
                minDown = Mathf.Min(minDown, cornerDown); maxDown = Mathf.Max(maxDown, cornerDown);
            }
            yaw = (minYaw + maxYaw) * 0.5f;
            lookDown = (minDown + maxDown) * 0.5f;
        }

        // The vertical field of view that frames the screen's quad from this pose so it
        // fills `fill` of the screen's height or width, whichever fits (Dan: "95% screen
        // is tv, rest is behind the tv"). Negative when the screen is not in front.
        public static float FrameScreenFieldOfView(Vector3 eye, Quaternion look, Transform screen, float aspect, float fill)
        {
            if (screen == null || aspect <= 0f || fill <= 0f) return -1f;
            MeshFilter filter = screen.GetComponent<MeshFilter>();
            Bounds local = filter != null && filter.sharedMesh != null ? filter.sharedMesh.bounds : new Bounds(Vector3.zero, new Vector3(1f, 1f, 0f));
            Quaternion inverse = Quaternion.Inverse(look);
            float tanX = 0f, tanY = 0f;
            for (int i = 0; i < 4; i++)
            {
                Vector3 corner = new(i % 2 == 0 ? local.min.x : local.max.x, i < 2 ? local.min.y : local.max.y, local.center.z);
                Vector3 c = inverse * (screen.TransformPoint(corner) - eye);
                if (c.z <= 0.05f) return -1f;
                tanX = Mathf.Max(tanX, Mathf.Abs(c.x) / c.z);
                tanY = Mathf.Max(tanY, Mathf.Abs(c.y) / c.z);
            }
            float half = Mathf.Max(tanY, tanX / aspect) / fill;
            return 2f * Mathf.Atan(half) * Mathf.Rad2Deg;
        }

        // ---- presentation ----------------------------------------------------------------

        // Owner, after everything moved this frame: the camera's field of view goes to
        // the framed screen while seated - its own seat, or the seat of the player a
        // dead owner watches (SpectatorView puts the camera on their eyes) - and back.
        private void UpdateZoom()
        {
            if (playerCamera == null) return;
            if (baseFieldOfView < 0f) baseFieldOfView = playerCamera.fieldOfView;
            PlayerMovementSettings settings = Movement;
            float target = -1f;
            if (seatedLocal && !dead.Value && ownerTv != null && ownerSeat != null)
            {
                // Framed from where the head ends up, not from the turn under way.
                Vector3 eye = ownerSeat.position + Vector3.up * settings.SeatedEyeHeight;
                SeatLook(eye, ownerTv, ownerSeat, out float yaw, out float lookDown);
                target = FrameScreenFieldOfView(eye, Quaternion.Euler(lookDown, yaw, 0f), ownerTv, playerCamera.aspect, settings.SeatZoomFill);
            }
            else if (dead.Value && Spectator != null && Spectator.Active && Spectator.Target != null && Spectator.Target.SeatedPose && Spectator.Target.remoteTv != null)
            {
                Spectator.Target.EyePose(out Vector3 eye, out Quaternion look);
                target = FrameScreenFieldOfView(eye, look, Spectator.Target.remoteTv, playerCamera.aspect, settings.SeatZoomFill);
            }
            bool zooming = target > 0f && target < baseFieldOfView;
            if (zooming) zoomFieldOfView = target;
            float step = settings.SeatBlendSeconds <= 0f ? 1f : Time.unscaledDeltaTime / settings.SeatBlendSeconds;
            zoomAmount = Mathf.MoveTowards(zoomAmount, zooming ? 1f : 0f, step);
            if (zoomAmount <= 0f)
            {
                if (fovWritten) { playerCamera.fieldOfView = baseFieldOfView; fovWritten = false; } // nothing written while nobody sits
                return;
            }
            playerCamera.fieldOfView = Mathf.Lerp(baseFieldOfView, zoomFieldOfView, Mathf.SmoothStep(0f, 1f, zoomAmount));
            fovWritten = true;
        }

        // A remote copy (and the server's): shown seated while its replicated seat is
        // set and the copy has reached it (or the ship is sailing with it), so the
        // squash never shows on a figure still walking to the couch or already up.
        private void UpdateRemoteSeatPose()
        {
            if (seat.Value == 0) { remoteSeatedPose = false; remoteSeat = null; remoteTv = null; return; }
            float now = Time.unscaledTime;
            if (now < nextRemoteSeatCheck) return;
            nextRemoteSeatCheck = now + SeatCheckInterval;
            CrewDayState day = CrewDayState.Instance;
            if (day == null || day.IsBelow(OwnerId) || dead.Value) { remoteSeatedPose = false; return; }
            // A remote copy's Unity scene on a client is not its world (contract, voice
            // section): the crew's ship world is where a sitter is.
            ShipParts ship = IsServerStarted ? ShipParts.InScene(gameObject.scene) : ShipParts.InWorld(day.World);
            Transform marker = ShipSeats.Seat(ship, seat.Value);
            if (marker != remoteSeat || remoteTv == null) remoteTv = ship != null ? ship.TvScreen : null; // Find walks the ship: only on a new seat
            remoteSeat = marker;
            remoteSeatedPose = remoteSeat != null && (day.Travelling || Vector3.Distance(transform.position, remoteSeat.position) <= SeatRemoteTolerance);
        }
    }
}
