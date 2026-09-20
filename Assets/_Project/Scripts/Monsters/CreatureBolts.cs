using System;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using SunkCost.Player;
using UnityEngine;

namespace SunkCost.Monsters
{
    // A bolt cue: one shot, replicated once as a SyncVar with a serial and a
    // start tick (the repo's one-shot idiom, never a per-frame stream). Every peer
    // flies the same bolt from the same tick; the server alone tests the hit.
    public struct BoltCue
    {
        public int Serial;
        public Vector3 From;
        public Vector3 To;
        public uint StartTick;
        public bool Dark;
    }

    // The Lure's bolt of light and the Listener's dark one (docs/DESIGN.md §6): a
    // straight flight from the creature's eye through the aim point at BoltSpeed
    // for BoltLifeSeconds, or until it meets a wall. The server carries the
    // flight and hurts the first diver within BoltHitRadius of its path who is
    // not on safe ground: damage and a leak. Fired at where the diver was, so a
    // diver who moves is missed. Presentation: CreatureLook draws it.
    public sealed class CreatureBolts : NetworkBehaviour
    {
        private readonly SyncVar<BoltCue> cue = new(new BoltCue { Serial = 0 });
        private readonly SyncVar<int> hitSerial = new(0);

        private sealed class Flight
        {
            public Vector3 From, Dir, Last;
            public float StartedAt, Damage;
            public string Cause;
        }
        private readonly List<Flight> flights = new();

        public BoltCue Cue => cue.Value;
        public int HitSerial => hitSerial.Value;
        public int ServerFired { get; private set; }
        public int ServerHits { get; private set; }
        public event Action<BoltCue> Fired; // every peer, from the SyncVar (not for a joiner's old value)
        public event Action Hit;

        private int clientStartFrame = -1;

        private void Awake()
        {
            cue.OnChange += OnCueChanged;
            hitSerial.OnChange += (_, next, asServer) => { if (IsServerStarted && !asServer) return; if (next != 0 && Time.frameCount != clientStartFrame) Hit?.Invoke(); };
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            clientStartFrame = Time.frameCount;
        }

        private void OnCueChanged(BoltCue previous, BoltCue next, bool asServer)
        {
            if (IsServerStarted && !asServer) return; // once per peer (the host sees both passes)
            if (next.Serial == 0 || Time.frameCount == clientStartFrame) return; // a joiner's old cue is old news
            Fired?.Invoke(next);
        }

        [Server]
        public void ServerFire(Vector3 from, Vector3 aim, bool dark, float damage, string cause)
        {
            Vector3 dir = aim - from;
            if (dir.sqrMagnitude < 0.01f) return;
            dir.Normalize();
            uint tick = NetworkManager != null && NetworkManager.TimeManager != null ? NetworkManager.TimeManager.Tick : 0u;
            cue.Value = new BoltCue { Serial = cue.Value.Serial + 1, From = from, To = aim, StartTick = tick, Dark = dark };
            flights.Add(new Flight { From = from, Dir = dir, Last = from, StartedAt = Time.time, Damage = damage, Cause = cause });
            ServerFired++;
        }

        private void Update()
        {
            if (!IsServerStarted || flights.Count == 0) return;
            MonsterSettings settings = MonsterSettings.Get();
            for (int i = flights.Count - 1; i >= 0; i--)
            {
                Flight f = flights[i];
                float t = Time.time - f.StartedAt;
                if (t > settings.BoltLifeSeconds) { flights.RemoveAt(i); continue; }
                Vector3 now = f.From + f.Dir * (settings.BoltSpeed * t);
                bool done = false;
                if (!CreatureSenses.ClearLine(f.Last, now)) done = true; // into a wall
                else
                    foreach (HQPlayerController diver in CreatureSenses.Divers())
                    {
                        if (CreatureSenses.Safe(diver, settings)) continue;
                        Vector3 chest = CreatureSenses.Chest(diver);
                        if (DistanceToSegment(chest, f.Last, now) > settings.BoltHitRadius) continue;
                        if (!CreatureSenses.ClearLine(ClosestOnSegment(chest, f.Last, now), chest)) continue; // a wall between the bolt and the diver
                        PlayerVitals vitals = diver.Vitals;
                        if (vitals != null && vitals.ServerDamage(f.Damage, true, f.Cause))
                        {
                            ServerHits++;
                            hitSerial.Value = hitSerial.Value + 1;
                        }
                        done = true;
                        break;
                    }
                f.Last = now;
                if (done) flights.RemoveAt(i);
            }
        }

        private static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b) => Vector3.Distance(point, ClosestOnSegment(point, a, b));
        private static Vector3 ClosestOnSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float len = ab.sqrMagnitude;
            float t = len < 0.0001f ? 0f : Mathf.Clamp01(Vector3.Dot(point - a, ab) / len);
            return a + ab * t;
        }
    }
}
