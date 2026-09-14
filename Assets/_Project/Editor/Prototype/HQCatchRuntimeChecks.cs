using System;
using System.IO;
using System.Linq;
using SunkCost.Interaction;
using SunkCost.Net;
using SunkCost.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace SunkCost.Editor.Prototype
{
    public static class HQCatchRuntimeChecks
    {
        private static double deadline;
        private static bool sawReleased;
        private static Vector3 previous;
        private static float travelled;
        private static Keyboard testKeyboard;
        public static string Status { get; private set; } = "Not run";
        private const string Log = "Temp/inventory-moving-catch.log";

        public static void Prepare(Vector3 position, Vector3 direction)
        {
            SessionInputGate.OpenMenu();
            HQPrototypeTestHooks.ClientMoveLocalPlayerTo(position);
            var player = Local();
            player.transform.rotation = Quaternion.LookRotation(new Vector3(direction.x, 0, direction.z));
            player.GetComponentInChildren<Camera>(true).transform.rotation = Quaternion.LookRotation(direction);
            float pitch = -Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg;
            typeof(HQPlayerController).GetField("pitch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(player, pitch);
        }

        public static void ArmAndThrow(int hostCommandId)
        {
            if (testKeyboard != null) throw new InvalidOperationException("Catch check already running");
            var gameView = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
            EditorWindow.GetWindow(gameView).Focus();
            SessionInputGate.SetApplicationFocus(true);
            SessionInputGate.Resume();
            testKeyboard = InputSystem.AddDevice<Keyboard>("CatchVerificationKeyboard");
            InputSystem.QueueStateEvent(testKeyboard, new KeyboardState(Key.E));
            sawReleased = false; travelled = 0f;
            previous = HQPrototypeTestHooks.Item().transform.position;
            deadline = EditorApplication.timeSinceStartup + 8;
            Status = "Running";
            File.WriteAllText(Log, "Real non-host E-key input; host throws at 8 m/s\n" + InventoryVerificationPeer.Snapshot());
            File.WriteAllText("Temp/inventory-peer-host/command.json", "{\"id\":" + hostCommandId + ",\"action\":\"throw\",\"aim\":{\"x\":1,\"y\":0.18,\"z\":0}}");
            EditorApplication.update += Observe;
        }

        private static void Observe()
        {
            var item = HQPrototypeTestHooks.Item();
            if (item != null && item.State == ItemState.Released)
            {
                sawReleased = true;
                travelled += Vector3.Distance(previous, item.transform.position);
                File.AppendAllText(Log, $"FLYING pos={item.transform.position}; owner={item.OwnerId}; target={Local().CurrentTarget?.name}; gate={SessionInputGate.CanPlay}; cursor={Cursor.lockState}\n");
            }
            if (item != null) previous = item.transform.position;
            bool held = item != null && Local().Inventory.HeldItem == item && item.IsOwner;
            if (!held && EditorApplication.timeSinceStartup < deadline) return;
            Status = held && sawReleased && travelled > 0.1f ? "MOVING_CATCH_PASS" : "MOVING_CATCH_FAIL";
            File.AppendAllText(Log, Status + "; travelled=" + travelled + "\n" + InventoryVerificationPeer.Snapshot());
            InputSystem.RemoveDevice(testKeyboard);
            testKeyboard = null;
            SessionInputGate.OpenMenu();
            EditorApplication.update -= Observe;
        }

        private static HQPlayerController Local() => UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsSortMode.None).First(p => p.IsOwner);
    }
}
