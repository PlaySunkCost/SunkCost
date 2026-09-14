using System;
using System.Collections;
using System.IO;
using System.Linq;
using SunkCost.Interaction;
using SunkCost.Net;
using SunkCost.Player;
using UnityEditor;
using UnityEngine;
using H = SunkCost.Editor.Prototype.HQPrototypeTestHooks;

namespace SunkCost.Editor.Prototype
{
    // Optional editor verifier: calls public client requests over the real Local
    // transport and reads the second process through InventoryVerificationPeer.
    public static class HQInventoryRuntimeChecks
    {
        private static IEnumerator steps;
        private static double next;
        private static int peerId = 100;
        private const string Log = "Temp/inventory-client-matrix.log";
        public static string Status { get; private set; } = "Not run";

        public static void RunRemoteClient()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode and join the Local host first.");
            if (steps != null) throw new InvalidOperationException("Already running");
            File.WriteAllText(Log, "Separate-client matrix started\n");
            Status = "Running";
            steps = Run();
            next = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 0.65;
            try
            {
                if (!EditorApplication.isPlaying) throw new Exception("Play Mode stopped");
                if (steps.MoveNext()) return;
                Status = "MATRIX_PASS";
            }
            catch (Exception e) { Status = "FAIL: " + e; }
            File.AppendAllText(Log, Status + "\n" + InventoryVerificationPeer.Snapshot());
            steps = null;
            EditorApplication.update -= Tick;
        }

        private static PlayerInventory Inv() => UnityEngine.Object.FindObjectsByType<PlayerInventory>(FindObjectsSortMode.None).First(p => p.IsOwner);
        private static void Check(bool value, string label)
        {
            if (!value) throw new Exception(label);
            File.AppendAllText(Log, "PASS " + label + "\n" + H.InventoryText() + "\n" + H.AllItems() + "\n");
        }
        private static void Peer() => File.WriteAllText("Temp/inventory-peer-host/command.json", "{\"id\":" + (++peerId) + ",\"action\":\"snapshot\"}");
        private static void PeerSpawnLight(int count) => File.WriteAllText("Temp/inventory-peer-host/command.json", "{\"id\":" + (++peerId) + ",\"action\":\"spawn_light\",\"slot\":" + count + "}");
        private static void CheckPeer(string state)
        {
            string snapshot = File.ReadAllText("Temp/inventory-peer-host/reply.txt");
            Check(snapshot.Contains("id=" + peerId + ";") && snapshot.Contains(state), "host observes " + state);
            File.AppendAllText(Log, snapshot + "\n");
        }

        private static IEnumerator Run()
        {
            SessionInputGate.OpenMenu();
            Check(!Inv().IsServerStarted, "genuine non-host client");
            int clientId = Inv().OwnerId;
            H.ClientMoveLocalPlayerToItem(); H.ClientLookAtItem(); yield return null;
            H.ClientRequestGrab(); yield return null;
            Check(Inv().HeldItem == H.Item() && Inv().Slots.Get(0) == H.Item().ObjectId, "H3 grab fills slot and hand");
            Check(H.Item().GetComponent<Rigidbody>().isKinematic && !H.Item().PrimaryCollider.enabled && H.Item().WriterOverride == true, "H1 kinematic holder writes");
            H.ClientMoveLocalPlayerTo(new Vector3(0.9f, 0, 0.2f)); H.ClientLookAtItem("Basketball (2)"); yield return null;
            Check(Vector3.Distance(Inv().HeldItem.transform.position, Inv().GetComponent<HQPlayerController>().HoldPoint.position) < 0.005f, "H1 rigid hold while move and pitch");
            Peer(); yield return null; yield return null; CheckPeer("state=Held; holder=" + clientId);
            H.ClientRequestEquip(0); yield return null;
            Check(Inv().HeldItem == null && H.Item().State == ItemState.Stowed && !H.Item().GetComponent<Renderer>().enabled, "H3 put away hides item");
            Peer(); yield return null; yield return null; CheckPeer("state=Stowed; holder=" + clientId); CheckPeer("visible=False");
            H.ClientRequestEquip(0); yield return null; Check(Inv().HeldItem == H.Item(), "H3 equip stowed item");
            H.ClientMoveLocalPlayerToItem("Basketball (2)"); yield return null;
            H.ClientRequestGrab("Basketball (2)"); yield return null;
            Check(Inv().HeldItem == H.Item() && H.Item("Basketball (2)").State == ItemState.Stowed, "H4 silent store keeps existing hand");
            H.ClientRequestEquip(1); yield return null;
            Check(Inv().HeldItem == H.Item("Basketball (2)") && H.Item().State == ItemState.Stowed, "H3 swap equipped slots");
            H.ClientRequestEquip(1); yield return null;
            // The saved fixture has three basketballs (docs/LOOT_WEIGHT plan section 7);
            // the four-slot rows need two more, spawned by the host for this session.
            PeerSpawnLight(2); yield return null; yield return null; yield return null;
            H.NameTestClones();
            for (int i = 3; i <= 4; i++)
            {
                string name = "Basketball (" + i + ")";
                H.ClientMoveLocalPlayerToItem(name); yield return null;
                H.ClientRequestGrab(name); yield return null;
                H.ClientRequestEquip(i - 1); yield return null;
            }
            Check(Inv().Slots.FirstFree() < 0 && Inv().HeldItem == null, "H5 four stowed slots");
            H.ClientRequestEquip(0); yield return null;
            Check(Inv().HeldItem == H.Item(), "H5 equipped item with all slots occupied");
            InventorySlots beforeFifth = Inv().Slots;
            H.ClientMoveLocalPlayerToItem("Basketball (5)"); yield return null;
            H.ClientRequestGrab("Basketball (5)"); yield return null;
            Check(Inv().HoldingOverflow && Inv().HeldItem == H.Item("Basketball (5)") && H.Item().State == ItemState.Stowed,
                "H5 fifth ball replaces equipped item in hand and stows it");
            for (int slot = 0; slot < InventorySlots.Count; slot++)
                Check(Inv().Slots.Get(slot) == beforeFifth.Get(slot), "H5 original slot preserved " + slot);
            H.ClientRequestEquip(0); yield return null; Check(Inv().HoldingOverflow && Inv().Refusal == "Hands full", "H5 equip blocked");
            H.ClientRequestDrop(); yield return null; Check(Inv().HeldItem == null && Inv().Slots.FirstFree() < 0, "H5 dropping overflow leaves four slots");
            H.ClientRequestEquip(0); yield return null; Check(Inv().HeldItem == H.Item(), "H5 keys resume after overflow drop");
            H.ClientRequestDrop(); yield return null; Check(Inv().Slots.Get(0) == -1 && Inv().HeldItem == null, "H6 Q clears slot");
            Check(H.Item().transform.position.y < 0.35f, "H6 Q drops at feet");
            H.ClientRequestEquip(1); yield return null;
            Inv().RequestUse(new Vector3(0, 0.35f, 1)); yield return null;
            Check(Inv().Slots.Get(1) == -1 && H.Item("Basketball (2)").State == ItemState.Released, "H6 non-host throw clears slot");
            for (int t = 0; t < 8; t++) yield return null;
            Check(H.Item("Basketball (2)").State == ItemState.Free && H.Item("Basketball (2)").OwnerId < 0, "H6 rest hands back to server");
            H.ClientMoveLocalPlayerTo(new Vector3(4, 0, 4)); yield return null;
            H.ClientRequestGrabRaw("Basketball"); yield return null;
            Check(Inv().Refusal == "Too far", "H2 out-of-range server refusal");
        }
    }
}
