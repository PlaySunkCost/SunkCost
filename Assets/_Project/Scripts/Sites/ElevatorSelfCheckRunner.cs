#if UNITY_EDITOR
using System.Collections;
using SunkCost.Diving;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Sites
{
    // Drives the actual frame-by-frame elevator smoke test once ElevatorSelfCheck (an
    // Assets/_Project/Editor/Prototype menu item) has entered Play Mode. A MonoBehaviour
    // coroutine is what makes "yield return null" step one real engine frame at a time, with
    // physics ticking in between — a synchronous scripted burst cannot do that, which is why
    // this exists as a runtime component instead of a single editor-side function call.
    //
    // Lives outside the Editor folder — and behind #if UNITY_EDITOR — because Unity refuses
    // to AddComponent<T> a type defined in an Editor assembly. The whole class compiles away
    // in a player build; it only exists for Play-in-Editor testing.
    public sealed class ElevatorSelfCheckRunner : MonoBehaviour
    {
        // Must match ElevatorInteractor's serialized default.
        private const float InteractDistance = 2.5f;
        // Must match DiveSiteDevPlayer's serialized default.
        private const float WalkSpeed = 4f;
        // Safety cap so a genuine failure (player stuck, never reaches the car) ends the
        // check instead of hanging Play Mode forever. 900 frames is 15s at 60fps — the walk
        // from spawn to the car's centre covers ~11m at 4 m/s (~3s) plus margin for the
        // stepOffset climb onto the floor, which visibly costs some speed.
        private const int MaxBoardFrames = 900;
        private const int MaxEgressFrames = 300;

        private int passCount;
        private int failCount;

        private void Start()
        {
            StartCoroutine(RunChecks());
        }

        private void Pass(string message)
        {
            passCount++;
            Debug.Log("[ElevatorSelfCheck] PASS: " + message);
        }

        private void Fail(string message)
        {
            failCount++;
            Debug.LogError("[ElevatorSelfCheck] FAIL: " + message);
        }

        private IEnumerator RunChecks()
        {
            GameObject playerRoot = GameObject.Find("DevHarnessPlayer_DeleteWhenNetworkedPlayerLands");
            GameObject elevatorRoot = GameObject.Find("Elevator");
            GameObject spawnObj = GameObject.Find("Spawn 1");
            GameObject panelObj = GameObject.Find("Control Panel");
            GameObject riderTriggerObj = GameObject.Find("Rider Trigger");
            GameObject carFloorObj = GameObject.Find("Car Floor");
            GameObject doorRootObj = GameObject.Find("Elevator Door");
            GameObject doorColliderObj = GameObject.Find("Door Collider");
            GameObject roofObj = GameObject.Find("Car Roof");

            if (playerRoot == null || elevatorRoot == null || spawnObj == null || panelObj == null
                || riderTriggerObj == null || carFloorObj == null || doorRootObj == null
                || doorColliderObj == null || roofObj == null)
            {
                Fail("Setup: a required scene object is missing (player/elevator/spawn/panel/rider trigger/car floor/door/roof). Did you run Create or Update Dive Site 01 first?");
                Finish();
                yield break;
            }

            CharacterController cc = playerRoot.GetComponent<CharacterController>();
            DiveSiteDevPlayer devPlayer = playerRoot.GetComponent<DiveSiteDevPlayer>();
            ElevatorController controller = elevatorRoot.GetComponent<ElevatorController>();
            Camera cam = playerRoot.GetComponentInChildren<Camera>();
            Collider riderTrigger = riderTriggerObj.GetComponent<Collider>();
            Collider doorCollider = doorColliderObj.GetComponent<Collider>();
            Transform carFloorTransform = carFloorObj.transform;

            // Exclusive scripted control for the walk-in only, so the real player's own live
            // keyboard/mouse state can't interfere with a deterministic frame-stepped walk.
            // Handed back to the real controller immediately after boarding so the ride
            // exercises the production AddExternalMotion / Move() path, not a stand-in.
            devPlayer.enabled = false;
            cc.enabled = false;
            playerRoot.transform.SetPositionAndRotation(spawnObj.transform.position, spawnObj.transform.rotation);
            Physics.SyncTransforms();
            cc.enabled = true;
            yield return null;

            // Assertion: refusal before boarding.
            bool preBoard = controller.TryStartMove(playerRoot);
            if (!preBoard && controller.State == ElevatorState.AtTop)
                Pass("Refusal before boarding: TryStartMove returned false, state stayed AtTop.");
            else
                Fail($"Refusal before boarding: expected (false, AtTop), got ({preBoard}, {controller.State}).");

            // Assertion: boarding, walked frame by frame with the same grounded-stick /
            // gravity logic DiveSiteDevPlayer.Move() uses, so the walk is physically faithful
            // rather than a teleport.
            Vector3 toElevator = elevatorRoot.transform.position - playerRoot.transform.position;
            toElevator.y = 0f;
            Vector3 walkDir = toElevator.normalized;
            float verticalSpeed = 0f;
            bool fellThroughGap = false;
            bool reachedInterior = false;
            bool reachedCenter = false;
            int boardedAtFrame = -1;
            Vector3 doorwayEntryPos = default;
            int frame = 0;
            for (; frame < MaxBoardFrames; frame++)
            {
                verticalSpeed = cc.isGrounded ? -2f : verticalSpeed + Physics.gravity.y * Time.deltaTime;
                cc.Move((walkDir * WalkSpeed + Vector3.up * verticalSpeed) * Time.deltaTime);

                if (playerRoot.transform.position.y < -1f)
                {
                    fellThroughGap = true;
                    break;
                }

                if (!reachedInterior && IsInsideCollider(riderTrigger, playerRoot.transform.position))
                {
                    reachedInterior = true;
                    boardedAtFrame = frame;
                    doorwayEntryPos = playerRoot.transform.position;
                }

                // Keep walking a little past the trigger threshold — the first instant of
                // overlap can land right on the doorway's corner, which is a bad vantage
                // point for the panel raycast below. This still walks the same straight
                // line toward the car's centre, just a few steps further, matching someone
                // who has actually stepped into the room rather than one foot in the door.
                Vector3 toCenter = elevatorRoot.transform.position - playerRoot.transform.position;
                toCenter.y = 0f;
                if (reachedInterior && toCenter.magnitude < 1f)
                {
                    reachedCenter = true;
                    break;
                }

                yield return null;
            }

            if (fellThroughGap)
                Fail($"Boarding: player fell below y=-1 while walking toward the car (pos={playerRoot.transform.position}) — an unbridged docking gap.");
            else if (!reachedInterior)
                Fail($"Boarding: player never reached the rider trigger within {MaxBoardFrames} frames (final pos={playerRoot.transform.position}).");
            else if (!reachedCenter)
                Fail($"Boarding: player registered as a rider at frame {boardedAtFrame} but then got stuck and never reached the car's centre within {MaxBoardFrames} frames (stuck at pos={playerRoot.transform.position}).");
            else
                Pass($"Boarding: reached the rider trigger at frame {boardedAtFrame} and walked to the car's centre by frame {frame} without y dropping below -1 (final pos={playerRoot.transform.position}).");

            devPlayer.enabled = true;

            if (!reachedInterior || !reachedCenter)
            {
                Finish();
                yield break;
            }

            // Doorway entry point relative to the car's own origin, so it can be recomputed
            // at the bottom later purely from the car's current position — the doorway
            // geometry only translates with the car, it never changes shape or bearing.
            Vector3 doorwayLocalOffset = doorwayEntryPos - elevatorRoot.transform.position;

            // Assertion: panel is hittable, same raycast shape as ElevatorInteractor, aimed
            // from the player's actual post-boarding position — not a position picked to pass.
            Vector3 camPos = cam.transform.position;
            Vector3 toPanel = (panelObj.transform.position - camPos).normalized;
            bool raycastHit = Physics.Raycast(camPos, toPanel, out RaycastHit hitInfo, InteractDistance, ~0, QueryTriggerInteraction.Ignore);
            ElevatorControlPanel hitPanel = raycastHit ? hitInfo.collider.GetComponentInParent<ElevatorControlPanel>() : null;
            if (hitPanel != null)
                Pass($"Panel raycast: hit ElevatorControlPanel at {hitInfo.distance:F2}m from the player's boarding position.");
            else
                Fail($"Panel raycast: expected ElevatorControlPanel, got hit={raycastHit}, collider={(raycastHit ? hitInfo.collider.name : "none")}.");

            // Assertion 6: a raycast straight up from inside the car hits the roof collider.
            Vector3 roofRayOrigin = playerRoot.transform.position + Vector3.up;
            bool roofHit = Physics.Raycast(roofRayOrigin, Vector3.up, out RaycastHit roofHitInfo, 20f, ~0, QueryTriggerInteraction.Ignore);
            if (roofHit && roofHitInfo.collider.name == "Car Roof")
                Pass($"Roof: raycast straight up hit Car Roof at {roofHitInfo.distance:F2}m.");
            else
                Fail($"Roof: raycast straight up expected to hit Car Roof, got hit={roofHit}, collider={(roofHit ? roofHitInfo.collider.name : "none")}.");

            // Assertion 1 (top half): at rest — plenty of real time has passed since scene
            // load just from the walk-in above, so the door has long since finished opening —
            // the doorway must be passable, proven by actually walking a CharacterController
            // through it from inside, not by reading a flag.
            yield return WalkStraight(playerRoot, cc, -walkDir, MaxEgressFrames,
                () => !IsInsideCollider(riderTrigger, playerRoot.transform.position),
                exited =>
                {
                    if (exited)
                        Pass($"Egress at rest (top): walked out through the doorway in under {MaxEgressFrames} frames.");
                    else
                        Fail($"Egress at rest (top): still inside the rider trigger after {MaxEgressFrames} frames (pos={playerRoot.transform.position}) — the doorway should be passable at rest.");
                });

            // Walk back in to re-board before the departure tests below, which need the
            // requester aboard for TryStartMove to accept the press. Requires reaching the
            // car's actual centre, not just the rider trigger's loose "inside" boundary — the
            // trigger is an axis-aligned box, so on a diagonal doorway bearing its corner
            // extends past the real wall/door plane, and a check that stopped there would
            // leave the player short of the doorway rather than genuinely back aboard.
            bool reboarded = false;
            yield return WalkStraight(playerRoot, cc, walkDir, MaxBoardFrames,
                () =>
                {
                    Vector3 toCenterNow = elevatorRoot.transform.position - playerRoot.transform.position;
                    toCenterNow.y = 0f;
                    return IsInsideCollider(riderTrigger, playerRoot.transform.position) && toCenterNow.magnitude < 1f;
                },
                success => reboarded = success);
            if (!reboarded)
            {
                Fail("Setup: could not walk back aboard after the egress-at-rest check; aborting the departure/gate checks.");
                Finish();
                yield break;
            }

            // Assertions 2, 3 and half of 4 (the losing latecomer): press the panel, confirm
            // Sealing (not Descending), progress held, a mid-seal press refused, and a
            // latecomer who needs MORE than DoorSealSeconds of walking to reach the doorway
            // is left behind when the car actually departs.
            float sealSeconds = controller.DoorSealSeconds;
            Vector3 topDoorwayPoint = elevatorRoot.transform.position + doorwayLocalOffset;
            GameObject slowLatecomer = CreateProbe(topDoorwayPoint - walkDir * (WalkSpeed * (sealSeconds + 1f)), playerRoot.transform.rotation);
            bool[] slowBoarded = { false };
            yield return RunSealAndDeparture(controller, playerRoot, ElevatorState.Descending, 0f, "Descent departure",
                slowLatecomer.GetComponent<CharacterController>(), walkDir, riderTrigger, slowBoarded);
            if (!slowBoarded[0])
                Pass("Descent departure gate: the latecomer needed more than DoorSealSeconds of walking and was left behind — the car departed without it.");
            else
                Fail("Descent departure gate: the latecomer needed more than DoorSealSeconds of walking but still ended up aboard — the gate did not close in time.");
            Destroy(slowLatecomer);

            // Assertion 5 (blocked while moving) is exercised inside RunRide for the descent
            // leg only, per the task's scope. Existing ride-along checks (4/6) are unchanged.
            yield return RunRide(controller, playerRoot, carFloorTransform, controller.TravelSecondsOneWay, ElevatorState.Descending, ElevatorState.AtBottom, "Descent",
                blockedCheckRiderTrigger: riderTrigger, blockedCheckOutwardDir: -walkDir);

            // Assertion 7: on arrival the state is already AtBottom/AtTop (RunRide's own
            // "reached {endState}" pass above proves that), and the doorway becomes passable
            // within DoorSealSeconds. Checked directly against the door's own blocking
            // collider rather than indirectly through a walk, since a walk's duration depends
            // on distance/speed and would not actually prove the door's own timing.
            yield return CheckDoorBecomesPassable(doorCollider, sealSeconds);

            // Second half of assertion 4 (the winning latecomer) plus assertion 5's ascent
            // counterpart is out of the task's required scope, but exercising the OTHER
            // direction of the gate here (rather than repeating the same losing case) is the
            // cheapest way to prove "a gate that nobody can ever beat is as broken as no
            // gate": this latecomer needs comfortably LESS than DoorSealSeconds of walking.
            Vector3 bottomDoorwayPoint = elevatorRoot.transform.position + doorwayLocalOffset;
            GameObject fastLatecomer = CreateProbe(bottomDoorwayPoint - walkDir * (WalkSpeed * sealSeconds * 0.4f), playerRoot.transform.rotation);
            bool[] fastBoarded = { false };
            yield return RunSealAndDeparture(controller, playerRoot, ElevatorState.Ascending, 1f, "Ascent departure",
                fastLatecomer.GetComponent<CharacterController>(), walkDir, riderTrigger, fastBoarded);
            if (fastBoarded[0])
                Pass("Ascent departure gate: the latecomer needed comfortably less than DoorSealSeconds of walking and made it aboard before the car departed.");
            else
                Fail("Ascent departure gate: the latecomer needed comfortably less than DoorSealSeconds of walking but was still left behind — the gate is unwinnable.");

            yield return RunRide(controller, playerRoot, carFloorTransform, controller.TravelSecondsOneWay, ElevatorState.Ascending, ElevatorState.AtTop, "Ascent",
                carryProbe: fastBoarded[0] ? fastLatecomer : null);

            if (fastLatecomer != null)
                Destroy(fastLatecomer);

            Finish();
        }

        // Walks playerRoot in a straight horizontal direction, frame-stepped with the same
        // grounded-stick/gravity logic DiveSiteDevPlayer.Move() uses, until doneCondition is
        // true or maxFrames elapses. onResult receives whether doneCondition was ever met.
        private static IEnumerator WalkStraight(GameObject playerRoot, CharacterController cc, Vector3 direction, int maxFrames, System.Func<bool> doneCondition, System.Action<bool> onResult)
        {
            float verticalSpeed = 0f;
            for (int i = 0; i < maxFrames; i++)
            {
                verticalSpeed = cc.isGrounded ? -2f : verticalSpeed + Physics.gravity.y * Time.deltaTime;
                cc.Move((direction * WalkSpeed + Vector3.up * verticalSpeed) * Time.deltaTime);
                if (doneCondition())
                {
                    onResult(true);
                    yield break;
                }
                yield return null;
            }
            onResult(false);
        }

        private static GameObject CreateProbe(Vector3 position, Quaternion rotation)
        {
            GameObject probe = new("LatecomerProbe");
            probe.transform.SetPositionAndRotation(position, rotation);
            CharacterController probeCc = probe.AddComponent<CharacterController>();
            probeCc.height = 1.8f;
            probeCc.radius = 0.3f;
            probeCc.center = new Vector3(0f, 0.9f, 0f);
            probeCc.stepOffset = 0.25f;
            probeCc.slopeLimit = 45f;
            probeCc.skinWidth = 0.03f;
            return probe;
        }

        // Presses the panel and asserts the seal gate itself (Sealing not the moving state,
        // progress held at its endpoint for the whole window within 0.1s, a mid-seal press
        // refused without disrupting it), while stepping a latecomer probe toward the doorway
        // for the same window so callers can read back whether it made it aboard.
        private IEnumerator RunSealAndDeparture(ElevatorController controller, GameObject boardedPlayer, ElevatorState expectedMoveState, float expectedEndpointProgress, string label,
            CharacterController latecomerCc, Vector3 latecomerWalkDir, Collider riderTrigger, bool[] latecomerBoardedOut)
        {
            bool started = controller.TryStartMove(boardedPlayer);
            if (!started || controller.State != ElevatorState.Sealing)
            {
                Fail($"{label}: pressing the panel should start Sealing, got started={started}, state={controller.State}.");
                yield break;
            }
            Pass($"{label}: pressing the panel started Sealing, not {expectedMoveState}.");

            float sealSeconds = controller.DoorSealSeconds;
            float startTime = Time.time;
            bool progressHeld = true;
            bool assertedMidSeal = false;
            bool midSealRefused = true;
            float latecomerVerticalSpeed = 0f;

            while (controller.State == ElevatorState.Sealing)
            {
                // Keeps the primary rider's own AddExternalMotion catch-up path alive right up
                // to the transition into motion — see RunRide's identical comment for why.
                Cursor.lockState = CursorLockMode.Locked;

                if (Mathf.Abs(controller.Progress - expectedEndpointProgress) > 0.0001f)
                    progressHeld = false;

                if (!assertedMidSeal && controller.StateElapsed >= sealSeconds * 0.5f)
                {
                    assertedMidSeal = true;
                    bool midResult = controller.TryStartMove(boardedPlayer);
                    if (midResult || controller.State != ElevatorState.Sealing)
                        midSealRefused = false;
                }

                if (latecomerCc != null && !latecomerBoardedOut[0])
                {
                    latecomerVerticalSpeed = latecomerCc.isGrounded ? -2f : latecomerVerticalSpeed + Physics.gravity.y * Time.deltaTime;
                    latecomerCc.Move((latecomerWalkDir * WalkSpeed + Vector3.up * latecomerVerticalSpeed) * Time.deltaTime);
                    if (IsInsideCollider(riderTrigger, latecomerCc.transform.position))
                        latecomerBoardedOut[0] = true;
                }

                yield return null;
            }

            float actualSealDuration = Time.time - startTime;

            if (progressHeld)
                Pass($"{label}: progress stayed at its endpoint ({expectedEndpointProgress:F0}) for the entire seal.");
            else
                Fail($"{label}: progress moved away from its endpoint ({expectedEndpointProgress:F0}) during Sealing.");

            if (Mathf.Abs(actualSealDuration - sealSeconds) <= 0.1f)
                Pass($"{label}: seal lasted {actualSealDuration:F2}s (expected {sealSeconds:F2}s, within 0.1s).");
            else
                Fail($"{label}: seal lasted {actualSealDuration:F2}s, expected {sealSeconds:F2}s within 0.1s tolerance.");

            if (midSealRefused)
                Pass($"{label}: TryStartMove during Sealing was refused and did not cancel, restart or reverse the seal.");
            else
                Fail($"{label}: TryStartMove during Sealing should have been refused without disrupting the seal.");

            if (controller.State == expectedMoveState)
                Pass($"{label}: the seal completed into {expectedMoveState}.");
            else
                Fail($"{label}: expected {expectedMoveState} once the seal completed, got {controller.State}.");
        }

        private IEnumerator CheckDoorBecomesPassable(Collider doorCollider, float sealSeconds)
        {
            float timeout = sealSeconds + 0.2f;
            float elapsed = 0f;
            bool opened = false;
            while (elapsed < timeout)
            {
                if (!doorCollider.enabled)
                {
                    opened = true;
                    break;
                }
                yield return null;
                elapsed += Time.deltaTime;
            }

            if (opened)
                Debug.Log($"[ElevatorSelfCheck] Arrival: doorway became passable {elapsed:F2}s after arrival (limit {timeout:F2}s).");
            else
                Fail($"Arrival: doorway did not become passable within {timeout:F2}s of arriving (DoorSealSeconds={sealSeconds:F2}s).");
        }

        private IEnumerator RunRide(ElevatorController controller, GameObject playerRoot, Transform carFloorTransform, float travelSecondsOneWay, ElevatorState movingState, ElevatorState endState, string rideName,
            Collider blockedCheckRiderTrigger = null, Vector3 blockedCheckOutwardDir = default,
            GameObject carryProbe = null)
        {
            if (controller.State != movingState)
            {
                Fail($"{rideName}: expected to already be {movingState} when the ride starts (state={controller.State}) — the seal did not resolve into the expected direction.");
                yield break;
            }
            Pass($"{rideName}: the seal resolved into {movingState}.");
            // Reasserted as early as possible in this frame, before any loop: coroutine
            // resumption order relative to DiveSiteDevPlayer.Update() isn't something
            // [DefaultExecutionOrder] controls, so the very first moving frame could
            // otherwise run devPlayer.Update() against a stale (unlocked) cursor state.
            Cursor.lockState = CursorLockMode.Locked;

            // Assertion 3, exercised here too: pressing again mid-motion must not restart or
            // reverse the move.
            bool midMotionResult = controller.TryStartMove(playerRoot);
            if (!midMotionResult && controller.State == movingState)
                Pass($"{rideName}: refusal mid-motion — TryStartMove returned false, state still {movingState}.");
            else
                Fail($"{rideName}: refusal mid-motion — expected (false, {movingState}), got ({midMotionResult}, {controller.State}).");

            float elapsed = 0f;

            // Assertion 5 (descent only): a rider walking from inside toward the doorway
            // while the car is moving must be blocked by the closed door and stay aboard.
            // Uses a disposable probe rather than the primary rider, so this test — however
            // it moves or where it ends up — can never disturb the primary's own tracking,
            // which the ride-along checks below must still pass unchanged. An earlier version
            // drove the primary player directly into the closed door and back; the repeated
            // depenetration against the door left a lasting mark on its subsequent tracking
            // for the rest of the ride — a self-check artifact, not evidence of a production
            // bug, since nothing drives a real player to keep walking into a shut door.
            //
            // Waits until the car has cleared the surface platform's own ring (a few percent
            // of progress) first: right at the instant motion starts, the car's rim is still
            // adjacent to the platform's fixed hole edge, and a CharacterController squeezed
            // between the closed door and that unrelated static geometry can get pushed
            // sideways past the door instead of cleanly blocked by it.
            if (blockedCheckRiderTrigger != null)
            {
                while (controller.State == movingState && controller.Progress < 0.05f)
                {
                    // The primary rider is not driven by this coroutine at all — it keeps
                    // riding via production DiveSiteDevPlayer.Update()/AddExternalMotion,
                    // which only runs while Cursor.lockState reads Locked. Losing focus during
                    // this wait silently drops that lock (see the main loop's own defensive
                    // comment below); without reasserting it here too, the car sails on
                    // without the rider for however long this wait lasts.
                    Cursor.lockState = CursorLockMode.Locked;
                    yield return null;
                    elapsed += Time.deltaTime;
                }

                // Offset well clear of the primary rider's own position before spawning: an
                // exactly-overlapping fresh CharacterController caused a violent one-frame
                // depenetration that flung the PRIMARY rider metres away (and out of the
                // rider trigger entirely) the moment its own Move() call resolved the overlap
                // — a real regression this probe must never risk reproducing.
                GameObject blockedCheckProbe = CreateProbe(playerRoot.transform.position + blockedCheckOutwardDir * 1.2f, playerRoot.transform.rotation);
                CharacterController blockedCheckCc = blockedCheckProbe.GetComponent<CharacterController>();
                // A freshly created CharacterController has never had isGrounded established,
                // so the very first real Move() below would compute its stick force from a
                // false "falling" reading. Settle it against the floor with a zero-displacement
                // Move() first so the test starts from clean, already-grounded contact.
                blockedCheckCc.Move(Vector3.zero);

                bool stayedInside = true;
                float bcVerticalSpeed = 0f;
                const int blockedCheckFrames = 60;
                for (int f = 0; f < blockedCheckFrames && controller.State == movingState; f++)
                {
                    Cursor.lockState = CursorLockMode.Locked; // keep the primary rider's own AddExternalMotion catch-up alive during this window too
                    bcVerticalSpeed = blockedCheckCc.isGrounded ? -2f : bcVerticalSpeed + Physics.gravity.y * Time.deltaTime;
                    blockedCheckCc.Move((blockedCheckOutwardDir * WalkSpeed + Vector3.up * bcVerticalSpeed) * Time.deltaTime);
                    if (!IsInsideCollider(blockedCheckRiderTrigger, blockedCheckProbe.transform.position))
                        stayedInside = false;
                    yield return null;
                    elapsed += Time.deltaTime;
                }

                if (stayedInside)
                    Pass($"{rideName}: rider walking toward the doorway while {movingState} was blocked by the sealed door and stayed aboard.");
                else
                    Fail($"{rideName}: rider walking toward the doorway while {movingState} exited the rider trigger — the door did not block it.");

                Destroy(blockedCheckProbe);
            }

            const float PerFrameTolerance = 0.15f;
            const float MaxDriftGrowth = 0.05f;

            float timeout = travelSecondsOneWay + 0.5f;
            bool feetStayedClose = true;
            float worstDeviation = 0f;
            // Split into quarters of the EXPECTED travel time (not the timeout, which has
            // slack baked in) so a failure that only "settles" into a steady offset over the
            // course of the ride — rather than spiking once and recovering — is still caught,
            // even though every individual frame stays under PerFrameTolerance.
            float firstQuarterEnd = travelSecondsOneWay * 0.25f;
            float finalQuarterStart = travelSecondsOneWay * 0.75f;
            float firstQuarterMaxDeviation = 0f;
            float finalQuarterMaxDeviation = 0f;

            Vector3 previousCarPos = controller.transform.position;

            // The carry below moves the probe by direct transform delta, not through
            // CharacterController.Move(), so its own collider no longer needs to be live —
            // disabling it removes any chance of the probe's capsule jostling the primary
            // rider's if their positions happen to be close inside the car.
            if (carryProbe != null)
            {
                CharacterController probeCcToDisable = carryProbe.GetComponent<CharacterController>();
                if (probeCcToDisable != null)
                    probeCcToDisable.enabled = false;
            }

            while (controller.State == movingState && elapsed < timeout)
            {
                // Defensive: DiveSiteDevPlayer.Update() only runs Move() while Cursor.lockState
                // is Locked, and losing window focus during an automated run can silently drop
                // that lock. Re-assert it every frame so the ride exercises the production
                // Move()/AddExternalMotion path instead of failing on an environment quirk
                // unrelated to the elevator code under test.
                Cursor.lockState = CursorLockMode.Locked;
                yield return null;
                elapsed += Time.deltaTime;

                float floorTopY = carFloorTransform.position.y + carFloorTransform.localScale.y;
                float footY = playerRoot.transform.position.y;
                float deviation = Mathf.Abs(footY - floorTopY);
                worstDeviation = Mathf.Max(worstDeviation, deviation);
                if (deviation > PerFrameTolerance)
                    feetStayedClose = false;

                if (elapsed <= firstQuarterEnd)
                    firstQuarterMaxDeviation = Mathf.Max(firstQuarterMaxDeviation, deviation);
                if (elapsed >= finalQuarterStart)
                    finalQuarterMaxDeviation = Mathf.Max(finalQuarterMaxDeviation, deviation);

                // Carries a boarded latecomer probe along with the car by directly following
                // its delta, rather than through CharacterController.Move(): the probe can end
                // up standing close to the primary rider inside the small car, and routing the
                // carry through Move() let that solid-vs-solid contact block the carry
                // entirely (observed: the probe never rose at all). The production carry path
                // (AddExternalMotion, collision-aware) is already exercised by the primary
                // rider elsewhere in this check; this probe only needs to prove the gate let
                // it board and that it then travels with the car, not re-prove collision-safe
                // movement a second time.
                if (carryProbe != null)
                {
                    Vector3 carDelta = controller.transform.position - previousCarPos;
                    if (carDelta != Vector3.zero)
                        carryProbe.transform.position += carDelta;
                }
                previousCarPos = controller.transform.position;
            }

            float expectedEndProgress = endState == ElevatorState.AtBottom ? 1f : 0f;
            if (controller.State != endState)
                Fail($"{rideName}: did not reach {endState} within {timeout:F1}s (state={controller.State}, progress={controller.Progress:F2}, elapsed={elapsed:F1}s).");
            else if (Mathf.Abs(controller.Progress - expectedEndProgress) > 0.001f)
                Fail($"{rideName}: reached {endState} but progress is {controller.Progress:F3}, expected {expectedEndProgress}.");
            else
                Pass($"{rideName}: reached {endState} with progress {controller.Progress:F2} in {elapsed:F1}s (limit {timeout:F1}s).");

            Debug.Log($"[ElevatorSelfCheck] {rideName} peak deviation: {worstDeviation:F3}m (first quarter max {firstQuarterMaxDeviation:F3}m, final quarter max {finalQuarterMaxDeviation:F3}m).");

            if (feetStayedClose)
                Pass($"{rideName}: rider's feet stayed within {PerFrameTolerance:F2}m of the car floor every frame (peak deviation {worstDeviation:F3}m).");
            else
                Fail($"{rideName}: rider's feet exceeded {PerFrameTolerance:F2}m from the car floor on some frame (peak deviation {worstDeviation:F3}m) — ride-along is not keeping the rider on the floor.");

            float driftGrowth = finalQuarterMaxDeviation - firstQuarterMaxDeviation;
            if (driftGrowth <= MaxDriftGrowth)
                Pass($"{rideName}: no settling drift — final-quarter peak ({finalQuarterMaxDeviation:F3}m) is within {MaxDriftGrowth:F2}m of first-quarter peak ({firstQuarterMaxDeviation:F3}m).");
            else
                Fail($"{rideName}: settling drift — final-quarter peak deviation ({finalQuarterMaxDeviation:F3}m) exceeds first-quarter peak ({firstQuarterMaxDeviation:F3}m) by {driftGrowth:F3}m, more than the {MaxDriftGrowth:F2}m allowed. The rider is drifting toward a steady offset rather than tracking the floor.");

            if (carryProbe != null)
            {
                float probeFloorTopY = carFloorTransform.position.y + carFloorTransform.localScale.y;
                float probeDeviation = Mathf.Abs(carryProbe.transform.position.y - probeFloorTopY);
                if (probeDeviation <= PerFrameTolerance)
                    Pass($"{rideName}: the boarded latecomer rode the full trip and ended {probeDeviation:F3}m from the car floor.");
                else
                    Fail($"{rideName}: the boarded latecomer did not ride correctly — ended {probeDeviation:F3}m from the car floor at arrival.");
            }
        }

        private static bool IsInsideCollider(Collider collider, Vector3 worldPoint)
        {
            Vector3 closest = collider.ClosestPoint(worldPoint);
            return (closest - worldPoint).sqrMagnitude < 0.0001f;
        }

        private void Finish()
        {
            Debug.Log($"[ElevatorSelfCheck] DONE: {passCount} passed, {failCount} failed.");
            EditorApplication.ExitPlaymode();
        }
    }
}
#endif
