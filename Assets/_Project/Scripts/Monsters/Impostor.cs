using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using SunkCost.Player;
using UnityEngine;

namespace SunkCost.Monsters
{
    // The Impostor (docs/DESIGN.md §6): it picks you. It chooses one diver at
    // random among those still underwater — only that diver sees it, and the
    // spectators and the TV watching them (ImpostorLook decides per camera) —
    // and wears the body colour and the name tag of a living crewmate. It comes
    // slowly, then chases; a touch is 30 HP and a leak, and then it runs away
    // and comes back for someone else. Keep away 30 s and it gives up too.
    // Everything replicates: the position, the pose, whose it is, what it wears.
    public sealed class Impostor : Creature
    {
        private enum Phase { Choosing, Approach, Chase, Flee }

        private readonly SyncVar<byte> wornColour = new(0);
        private readonly SyncVar<string> wornName = new(string.Empty);

        private Phase phase = Phase.Choosing;
        private float chaseStartedAt, fleeUntil, nextChoiceAt;
        private Vector3 fleeDir;
        private int lastTarget = -1;

        public int WornColourIndex => wornColour.Value;
        public string WornName => wornName.Value;
        public event System.Action Dressed; // every peer: the colour or the name changed
        public int ServerTouches { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            wornColour.OnChange += (_, _, _) => Dressed?.Invoke();
            wornName.OnChange += (_, _, _) => Dressed?.Invoke();
        }

        protected override void ServerThink(float dt)
        {
            HQPlayerController prey = Living(CreatureSenses.DiverOf(TargetId));
            switch (phase)
            {
                case Phase.Choosing:
                    if (Now < nextChoiceAt) { SetPose(CreaturePose.Idle); return; }
                    Choose();
                    return;
                case Phase.Approach:
                    if (prey == null) { Choose(); return; }
                    if (CreatureSenses.Safe(prey, Settings)) { SetPose(CreaturePose.Idle); return; } // it waits for you to come out
                    FaceToward(prey.transform.position);
                    if (CreatureSenses.Flat(transform.position, prey.transform.position) <= Settings.ImpostorChaseMeters)
                    {
                        phase = Phase.Chase; chaseStartedAt = Now;
                        SetPose(CreaturePose.Hunting);
                        return;
                    }
                    SetPose(CreaturePose.Drawn);
                    MoveToward(prey.transform.position, WalkSpeed * Settings.ImpostorApproachSpeedFactor, dt, Settings.TouchStandoffMeters);
                    return;
                case Phase.Chase:
                    if (prey == null) { Choose(); return; }
                    FaceToward(prey.transform.position);
                    if (WithinReach(prey) && StrikeReady)
                    {
                        if (Strike(prey, Settings.ImpostorDamage)) { ServerTouches++; Flee(prey); }
                        return;
                    }
                    if (Now - chaseStartedAt > Settings.ImpostorChaseSeconds) { Flee(prey); return; }
                    SetPose(CreaturePose.Hunting);
                    MoveToward(prey.transform.position, SprintSpeed * Settings.ImpostorChaseSpeedFactor, dt, Settings.TouchStandoffMeters);
                    return;
                case Phase.Flee:
                    if (Now >= fleeUntil) { phase = Phase.Choosing; nextChoiceAt = Now + 2f; SetTarget(-1); return; }
                    SetPose(CreaturePose.Fleeing);
                    MoveToward(transform.position + fleeDir * 6f, SprintSpeed, dt);
                    FaceToward(transform.position + fleeDir);
                    return;
            }
        }

        private void Flee(HQPlayerController from)
        {
            phase = Phase.Flee;
            fleeUntil = Now + Settings.ImpostorRunSeconds;
            fleeDir = from != null ? transform.position - from.transform.position : -transform.forward;
            fleeDir.y = 0f;
            fleeDir = fleeDir.sqrMagnitude > 0.01f ? fleeDir.normalized : -transform.forward;
            lastTarget = TargetId;
            SetPose(CreaturePose.Fleeing);
        }

        // A random diver still underwater — another one than last time when there
        // is one — and a living crewmate's colour and name to wear (its own when
        // it is alone down there).
        private void Choose()
        {
            IReadOnlyList<HQPlayerController> divers = CreatureSenses.Divers();
            var candidates = new List<HQPlayerController>();
            foreach (HQPlayerController d in divers) if (divers.Count == 1 || d.OwnerId != lastTarget) candidates.Add(d);
            if (candidates.Count == 0) { SetTarget(-1); SetPose(CreaturePose.Idle); nextChoiceAt = Now + 1f; return; }
            HQPlayerController target = candidates[Random.Range(0, candidates.Count)];
            var crew = new List<HQPlayerController>();
            foreach (HQPlayerController d in FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude))
                if (d != null && d.IsSpawned && !d.IsDead && d != target) crew.Add(d);
            HQPlayerController worn = crew.Count > 0 ? crew[Random.Range(0, crew.Count)] : target;
            PlayerIdentity identity = worn.GetComponent<PlayerIdentity>();
            wornColour.Value = (byte)(identity != null ? identity.ColourIndex : 0);
            wornName.Value = identity != null ? identity.DisplayName : PlayerIdentity.Fallback(worn.OwnerId);
            SetTarget(target.OwnerId);
            phase = Phase.Approach;
            SetPose(CreaturePose.Drawn);
            Debug.Log($"[Monsters] the Impostor chose {World.WorldSceneFlow.DisplayName(target.Owner)} and wears {wornName.Value}'s colour and name");
        }

        // The checks pick the victim.
        [Server]
        public void ServerChooseForChecks(int clientId)
        {
            lastTarget = -1;
            HQPlayerController target = CreatureSenses.DiverOf(clientId);
            if (target == null) return;
            var crew = new List<HQPlayerController>();
            foreach (HQPlayerController d in FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude))
                if (d != null && d.IsSpawned && !d.IsDead && d != target) crew.Add(d);
            HQPlayerController worn = crew.Count > 0 ? crew[0] : target;
            PlayerIdentity identity = worn.GetComponent<PlayerIdentity>();
            wornColour.Value = (byte)(identity != null ? identity.ColourIndex : 0);
            wornName.Value = identity != null ? identity.DisplayName : PlayerIdentity.Fallback(worn.OwnerId);
            SetTarget(clientId);
            phase = Phase.Approach;
            SetPose(CreaturePose.Drawn);
        }

        protected override string ServerBrainStatus() => $" phase={phase} wears={wornName.Value}/{wornColour.Value} touches={ServerTouches}";
    }
}
