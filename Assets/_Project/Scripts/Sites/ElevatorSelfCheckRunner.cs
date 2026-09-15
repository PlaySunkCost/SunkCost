#if UNITY_EDITOR
using System.Collections;
using SunkCost.Diving;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Sites
{
    // Drives the elevator smoke test once ElevatorSelfCheck (an Assets/_Project/Editor/
    // Prototype menu item) has entered Play Mode.
    //
    // The networked-player work retired this check's boarding/riding/departure-gate
    // assertions: they drove a scripted CharacterController through DiveSiteDevPlayer's
    // exact Move() ordering and then handed control back to it so the ride exercised the
    // real production path. DiveSiteDevPlayer is deleted and DiveSite01 no longer bakes a
    // player into the scene at all — the player is a networked HQPlayerController spawned
    // by CrewSpawner (docs/WORLD_LOOP_IMPLEMENTATION_PLAN.md section 3.3) once a
    // connection's world is DiveSite01, and HQPlayerController only runs its own Move()/
    // AddExternalMotion for the owning client of a live FishNet connection (see
    // HQPlayerController.Update()'s IsOwner gate). This editor-only, no-network Play Mode
    // harness has no host/client session and no Session.unity network root running, so
    // there is no live rider to walk, board, or ride with. A green run of THIS check
    // proves the static elevator geometry and components are wired correctly; it proves
    // nothing about netcode and nothing about boarding, riding, or the departure gate —
    // that needs a real host plus a separate non-host client (see NETWORK_CONTRACT.md
    // §11), not this harness.
    //
    // Lives outside the Editor folder — and behind #if UNITY_EDITOR — because Unity refuses
    // to AddComponent<T> a type defined in an Editor assembly. The whole class compiles away
    // in a player build; it only exists for Play-in-Editor testing.
    public sealed class ElevatorSelfCheckRunner : MonoBehaviour
    {
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
            GameObject elevatorRoot = GameObject.Find("Elevator");
            GameObject panelObj = GameObject.Find("Control Panel");
            GameObject riderTriggerObj = GameObject.Find("Rider Trigger");
            GameObject doorRootObj = GameObject.Find("Elevator Door");
            GameObject doorColliderObj = GameObject.Find("Door Collider");
            GameObject roofObj = GameObject.Find("Car Roof");

            if (elevatorRoot == null || panelObj == null || riderTriggerObj == null
                || doorRootObj == null || doorColliderObj == null || roofObj == null)
            {
                Fail("Setup: a required scene object is missing (elevator/panel/rider trigger/door/roof). Did you run Create or Update Dive Site 01 first?");
                Finish();
                yield break;
            }

            yield return null; // let Awake()/OnEnable() on the freshly loaded scene settle first

            if (elevatorRoot.GetComponent<ElevatorController>() != null)
                Pass("Elevator has its ElevatorController component.");
            else
                Fail("Elevator is missing its ElevatorController component.");

            Collider panelCollider = panelObj.GetComponent<Collider>();
            if (panelCollider != null && panelObj.GetComponent<ElevatorControlPanel>() != null)
                Pass("Control Panel has a collider and its ElevatorControlPanel component.");
            else
                Fail("Control Panel is missing a collider or its ElevatorControlPanel component.");

            Collider riderTrigger = riderTriggerObj.GetComponent<Collider>();
            if (riderTrigger != null && riderTrigger.isTrigger && riderTriggerObj.GetComponent<ElevatorRiderTrigger>() != null)
                Pass("Rider Trigger has a trigger collider and its ElevatorRiderTrigger component.");
            else
                Fail("Rider Trigger is missing a trigger collider or its ElevatorRiderTrigger component.");

            if (doorRootObj.GetComponent<ElevatorDoor>() != null && doorColliderObj.GetComponent<Collider>() != null)
                Pass("Elevator Door has its ElevatorDoor component and Door Collider has a collider.");
            else
                Fail("Elevator Door is missing its ElevatorDoor component or Door Collider is missing a collider.");

            // Straight-up raycast from the car's own origin, not from a player position —
            // there is no live rider position to raycast from in this harness anymore.
            Vector3 roofRayOrigin = elevatorRoot.transform.position + Vector3.up;
            bool roofHit = Physics.Raycast(roofRayOrigin, Vector3.up, out RaycastHit roofHitInfo, 20f, ~0, QueryTriggerInteraction.Ignore);
            if (roofHit && roofHitInfo.collider.name == "Car Roof")
                Pass($"Roof: raycast straight up from the car origin hit Car Roof at {roofHitInfo.distance:F2}m.");
            else
                Fail($"Roof: raycast straight up from the car origin expected to hit Car Roof, got hit={roofHit}, collider={(roofHit ? roofHitInfo.collider.name : "none")}.");

            Debug.Log("[ElevatorSelfCheck] Boarding, riding and departure-gate assertions are retired — see this file's header comment. Untested by this harness.");

            Finish();
        }

        private void Finish()
        {
            Debug.Log($"[ElevatorSelfCheck] DONE: {passCount} passed, {failCount} failed.");
            EditorApplication.ExitPlaymode();
        }
    }
}
#endif
