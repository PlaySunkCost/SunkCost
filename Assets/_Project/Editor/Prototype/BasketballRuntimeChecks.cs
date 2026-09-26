using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SunkCost.Interaction;
using SunkCost.Player;
using SunkCost.World;
using UnityEditor;
using UnityEngine;
using H = SunkCost.Editor.Prototype.HQPrototypeTestHooks;
using Object = UnityEngine.Object;

namespace SunkCost.Editor.Prototype
{
    public static class BasketballRuntimeChecks
    {
        private const string Log = "Temp/basketball-matrix.log", GuestDir = "Temp/basketball-guest";
        private static readonly Stack<IEnumerator> steps = new();
        private static Process guest;
        private static int serial = 1800;
        private static string reply;
        private static CarryableItem Ball => H.Item("Basketball");
        private static CrewDayState Day => CrewDayState.Instance;
        private static HQPlayerController Host => WorldSceneFlow.LocalPlayer();

        public static void RunAsHost()
        {
            if (!EditorApplication.isPlaying || Host == null) throw new InvalidOperationException("Host first");
            File.WriteAllText(Log, "Basketball checks " + DateTime.Now + "\n");
            steps.Clear(); steps.Push(Run()); EditorApplication.update += Tick;
        }
        private static void Tick()
        {
            try
            {
                if (!EditorApplication.isPlaying) throw new Exception("Play Mode stopped");
                while (steps.Count > 0)
                {
                    var step = steps.Peek();
                    if (!step.MoveNext()) { steps.Pop(); continue; }
                    if (step.Current is IEnumerator nested) { steps.Push(nested); continue; }
                    return;
                }
                Finish("MATRIX_PASS");
            }
            catch (Exception e) { Finish("FAIL: " + e); }
        }
        private static void Finish(string status)
        {
            File.AppendAllText(Log, status + "\n"); EditorApplication.update -= Tick; steps.Clear();
            if (guest != null && !guest.HasExited) guest.Kill(); guest = null;
        }
        private static void Check(bool pass, string label)
        {
            if (!pass) throw new Exception(label); File.AppendAllText(Log, "PASS " + label + "\n");
        }
        private static IEnumerator Wait(float seconds)
        {
            float end = Time.unscaledTime + seconds; while (Time.unscaledTime < end) yield return null;
        }
        private static IEnumerator Until(Func<bool> pass, float seconds, string label)
        {
            float end = Time.unscaledTime + seconds;
            while (!pass() && Time.unscaledTime < end) yield return null;
            Check(pass(), label);
        }
        private static IEnumerator Run()
        {
            Host.TeleportLocal(new Vector3(-8, 0, -5), 0);
            var rb = Ball.GetComponent<Rigidbody>();
            Check(Ball.GetComponent<Collider>().sharedMaterial != null, "rubber physics material assigned");
            Check(Ball.GetComponent<Renderer>().sharedMaterial.GetTexture("_BaseMap") != null, "leather texture assigned");
            // Bottom of ball 1.8 m above the actual painted court; measure the first rebound.
            Vector3 floor = new(-8, 0, -2);
            Check(Physics.Raycast(floor + Vector3.up, Vector3.down, out var hit, 3f, CarryableCollisionPolicy.WorldMask, QueryTriggerInteraction.Ignore), "court floor found");
            Ball.ServerDropAt(hit.point + Vector3.up * (1.8f + Ball.Radius));
            yield return Wait(.05f);
            yield return Until(() => rb.linearVelocity.y > .2f, 2f, "ball bounces upward from court");
            float apex = Ball.transform.position.y;
            while (rb.linearVelocity.y > 0) { apex = Mathf.Max(apex, Ball.transform.position.y); yield return null; }
            float rebound = apex - Ball.Radius - hit.point.y;
            Check(rebound > .95f && rebound < 1.2f, $"first rebound underside={rebound:0.000} m from 1.800 m drop (target ~1.06 m)");
            float first = rebound;
            yield return Until(() => rb.linearVelocity.y < -.5f, 2f, "first rebound falls");
            yield return Until(() => rb.linearVelocity.y > .2f, 2f, "second bounce");
            apex = Ball.transform.position.y;
            while (rb.linearVelocity.y > 0) { apex = Mathf.Max(apex, Ball.transform.position.y); yield return null; }
            Check(apex - Ball.Radius - hit.point.y < first * .8f, "successive bounce loses energy");

            foreach (string name in new[] { "Hoop W", "Hoop E" })
            {
                var hoop = GameObject.Find(name).transform;
                var trigger = hoop.Find("Score Trigger");
                int before = Day.Baskets;
                int burstsBefore = trigger.GetComponent<SunkCost.Look.BasketCelebration>()?.BurstsShown ?? 0;
                Ball.ServerDropAt(trigger.position + Vector3.up * .85f);
                yield return Until(() => Day.Baskets == before + 1, 3f, "basket accepted before celebration check");
                yield return Wait(.12f);
                var celebration = trigger.GetComponent<SunkCost.Look.BasketCelebration>();
                Check(celebration != null && celebration.Active && celebration.BurstsShown == burstsBefore + 1 &&
                    celebration.SoundsPlayed == burstsBefore + 1 && celebration.GetComponent<AudioSource>().isPlaying,
                    name + " one live confetti burst and playing chime");
                if (name == "Hoop W")
                    H.CaptureFrom(trigger.position + hoop.forward*3f + Vector3.up*.8f, trigger.position+Vector3.up*.5f,"Temp/look/basket-confetti.png");
                yield return Wait(2f);
                Check(!celebration.Active, "confetti cleans up after short celebration");
                Check(Day.Baskets == before + 1, name + " clean downward basket counts once");
                Check(hoop.Find("Score").GetComponent<TextMesh>().text == "BASKETS " + Day.Baskets, "backboard shows shared score");
                foreach (float offset in new[] { -.16f, .16f })
                {
                    before = Day.Baskets;
                    Ball.ServerDropAt(trigger.position + hoop.right * offset + Vector3.up * .85f);
                    yield return Wait(2f);
                    Check(Day.Baskets == before + 1, name + " off-centre basket at " + offset + " m clears physical rim and scores");
                }
                before = Day.Baskets;
                Ball.ServerDropAt(trigger.position + hoop.right * .42f + Vector3.up * .5f);
                yield return Wait(1.2f);
                Check(Day.Baskets == before, "beside rim does not count");
                Ball.ServerDropAt(trigger.position - Vector3.up * .6f);
                yield return Wait(.05f); rb.linearVelocity = Vector3.up * 5f;
                yield return Until(() => Ball.transform.position.y > trigger.position.y + .4f, 1f, "ball passes upward through ring");
                Check(Day.Baskets == before, "upward pass does not count");
                Ball.ServerDropAt(new Vector3(-8, 1, -3)); yield return Wait(.2f);
            }
            yield return Shoot(Host, false);
            // Render an actual court basketball at close range before the guest joins.
            Ball.ServerDropAt(new Vector3(-7, .15f, -3)); yield return Wait(1f);
            H.CaptureFrom(new Vector3(-6.6f,.35f,-2.5f), Ball.transform.position, "Temp/look/basketball-close.png");
            Host.TeleportLocal(new Vector3(-8, 0, -5), 0);
            Directory.CreateDirectory(GuestDir);
            foreach (string file in new[] { "command.json", "reply.txt" })
                if (File.Exists(GuestDir + "/" + file)) File.Delete(GuestDir + "/" + file);
            var transport = Object.FindAnyObjectByType<FishNet.Transporting.Tugboat.Tugboat>(FindObjectsInactive.Include);
            guest = Process.Start(new ProcessStartInfo(Path.GetFullPath(HQPrototypeBuild.LocalOutputPath),
                "-screen-width 960 -screen-height 540 -screen-fullscreen 0 -hq-auto-join-local 127.0.0.1 -hq-local-port " + transport.GetPort() +
                " -hq-inventory-test-dir \"" + Path.GetFullPath(GuestDir) + "\" -logFile \"" + Path.GetFullPath(GuestDir + "/player.log") + "\"")
                { UseShellExecute = false, CreateNoWindow = true });
            yield return Until(() => Object.FindObjectsByType<HQPlayerController>(FindObjectsSortMode.None).Any(p => p.IsSpawned && !p.IsOwner), 35f, "separate non-host client joined");
            var other = Object.FindObjectsByType<HQPlayerController>(FindObjectsSortMode.None).First(p => p.IsSpawned && !p.IsOwner);
            yield return Wait(2f);
            yield return Send("\"action\":\"snapshot\"");
            Check(reply.Contains("baskets=" + Day.Baskets + ";"), "late joiner receives existing basket count");
            Check(reply.Contains("hoop=Hoop W; confetti=0; chimes=0") && reply.Contains("hoop=Hoop E; confetti=0; chimes=0"), "late joiner does not replay old celebrations");
            yield return Shoot(other, true);
            yield return Send("\"action\":\"snapshot\"");
            Check(reply.Contains("baskets=" + Day.Baskets + ";") && reply.Contains("BASKETS " + Day.Baskets), "guest receives updated count and backboard text");
            Check(reply.Contains("hoop=Hoop W; confetti=1; chimes=1") && reply.Contains("hoop=Hoop E; confetti=0; chimes=0"), "guest sees/hears one celebration at only the scoring hoop");
            File.WriteAllText(GuestDir + "/scoring-snapshot.txt", reply);
            yield return Send("\"action\":\"leave\"");
        }

        private static IEnumerator Shoot(HQPlayerController player, bool remote)
        {
            Transform hoop = GameObject.Find("Hoop W").transform;
            Vector3 target = hoop.Find("Score Trigger").position + Vector3.up * .2f;
            Vector3 spot = new Vector3(target.x, .05f, target.z) + hoop.forward * 3.3f;
            if (remote) yield return Send("\"action\":\"move\",\"position\":" + Vec(spot));
            else player.TeleportLocal(spot, Quaternion.LookRotation(-hoop.forward).eulerAngles.y);
            Ball.ServerDropAt(spot + Vector3.up * .2f + hoop.right * .8f);
            yield return Wait(.7f);
            if (remote) yield return Send("\"action\":\"grab\",\"item\":\"#" + Ball.ObjectId + "\"");
            else player.Inventory.RequestGrab(Ball);
            yield return Until(() => Ball.IsHeld && Ball.HolderClientId == player.OwnerId, 5f, "shooter holds ball");
            int before = Day.Baskets;
            if (remote) yield return Send("\"action\":\"basketball_shot\",\"position\":" + Vec(target));
            else reply = SunkCost.Net.BasketballShotProbe.Shoot(player, target);
            Check(reply.Contains("shot requested"), "actual camera aim: " + reply.Split('\n')[0]);
            yield return Until(() => Ball.State == ItemState.Released, 3f, "throw uses Released owner physics");
            if (remote) Check(Ball.GetComponent<Rigidbody>().isKinematic, "server observes remote throw without simulating it");
            yield return Until(() => Day.Baskets == before+1, 4f, (remote ? "client" : "host") + " real thrown shot scores");
            yield return Wait(1f);
            Check(Day.Baskets == before+1, "throw scores exactly once");
        }

        private static string Vec(Vector3 v) => JsonUtility.ToJson(v);
        private static IEnumerator Send(string fields)
        {
            int id = ++serial;
            File.WriteAllText(GuestDir + "/command.json", "{\"id\":" + id + "," + fields + "}");
            float end = Time.unscaledTime + 15f;
            while (Time.unscaledTime < end)
            {
                try { reply = File.Exists(GuestDir + "/reply.txt") ? File.ReadAllText(GuestDir + "/reply.txt") : ""; }
                catch (IOException) { reply = ""; }
                if (reply.StartsWith("id=" + id + ";")) yield break;
                yield return null;
            }
            throw new Exception("Guest command timed out: " + fields);
        }
    }
}
