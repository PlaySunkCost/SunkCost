using System.Collections;
using FishNet.Connection;
using UnityEngine;

namespace SunkCost.World
{
    // A crew wipe (Dan, 29 September 2026; docs/DESIGN.md §4 "Nobody came back"):
    // when every connected player is dead nobody is left to pull END DAY, so the day
    // ends by itself. The server judges it (a leaver does not count; a joiner still
    // spawning is not in the crew yet), waits for the death to settle — at least
    // `crewWipeSettleSeconds` after it, and until End day itself would be accepted:
    // no ride, the site closed with the dead carried to the ship — then puts the
    // NOBODY CAME BACK card up on every peer (CrewDayState.CrewWipe) for
    // `crewWipeCardSeconds`, and ends the day through the one End day
    // (ServerEndDayBy → CrewDayState.ServerEndDay → ServerReviveAll): the day counts
    // (payday after the last), the storage room keeps what it holds, what the dead
    // carried stayed below with the site, a body not brought up costs its owner the
    // upgrades, and everyone stands up on the deck with a full tank. The card comes
    // down after the revival, under the day card when one follows.
    public sealed partial class WorldSceneFlow
    {
        public const string CrewWipeCardText = "NOBODY CAME BACK";
        private const float CrewWipeFadeSeconds = 1f;
        // A card that cannot end the day this long after its time (a stuck site) comes down anyway.
        private const float CrewWipeEndTimeoutSeconds = 60f;

        private Coroutine crewWipe; // server
        // Checks: the cards this peer has put up (the guest snapshot's wipeCards).
        public static int CrewWipeCardsShown { get; private set; }
        public bool CrewWipePending => crewWipe != null;

        // Every frame on the server (Update): nothing to do until someone is dead.
        private void ServerTickCrewWipe()
        {
            if (crewWipe != null || WorldLoopSettings.CrewWipeDisabledForTests) return;
            if (dayState == null || dayState.Dead.Count == 0) return;
            if (!ServerCrewAllDead()) return;
            crewWipe = StartCoroutine(CrewWipeRoutine(dayState));
        }

        // Every active connection with a player is dead, and there is at least one;
        // at sea only (deaths happen below; the plank and HQ are not this).
        private bool ServerCrewAllDead()
        {
            if (dayState == null || dayState.Dead.Count == 0) return false;
            if (currentWorld != WorldId.Sea || dayState.Phase == DayPhase.Plank) return false;
            int players = 0;
            foreach (NetworkConnection conn in networkManager.ServerManager.Clients.Values)
            {
                if (!conn.IsActive || PlayerOf(conn) == null) continue; // a leaver, or a joiner not yet spawned
                if (!dayState.IsDead(conn.ClientId)) return false;
                players++;
            }
            return players > 0;
        }

        private bool ServerStillOn(CrewDayState state) =>
            networkManager != null && networkManager.ServerManager.Started && dayState != null && dayState == state;

        private IEnumerator CrewWipeRoutine(CrewDayState state)
        {
            float since = Time.unscaledTime;
            Debug.Log($"[CrewWipe] Every connected player is dead (dead=[{string.Join(",", state.Dead)}]); the card waits for the death to settle");

            // 1. The death settles: the fall seen, the site closed with the dead on the
            // ship. A living player in the crew by then (a joiner spawned on the deck)
            // calls it off: they end the day at the console as before.
            CrewSpawner spawner = FindAnyObjectByType<CrewSpawner>();
            while (true)
            {
                if (!ServerStillOn(state)) { crewWipe = null; yield break; }
                if (!ServerCrewAllDead())
                {
                    Debug.Log("[CrewWipe] Called off before the card: " + (state.Dead.Count == 0 ? "nobody is dead any more" : "someone living is in the crew"));
                    crewWipe = null;
                    yield break;
                }
                bool joining = spawner != null && spawner.PendingCount > 0;
                if (!joining && Time.unscaledTime - since >= Settings.CrewWipeSettleSeconds && ServerCanEndDay(out _)) break;
                yield return null;
            }

            // 2. The card on every peer. From here the wipe is decided: a player leaving
            // or a joiner arriving does not stop it (joins are refused while it is up).
            int dayBefore = state.Day;
            state.ServerBeginCrewWipe(networkManager.TimeManager.Tick);
            Debug.Log($"[CrewWipe] NOBODY CAME BACK (card {state.CrewWipe.Serial}, day {dayBefore}, {Settings.CrewWipeCardSeconds:0.#} s)");
            float until = Time.unscaledTime + Settings.CrewWipeCardSeconds;
            while (Time.unscaledTime < until && ServerStillOn(state) && state.DiveDone) yield return null;

            // 3. The day ends as End day ends it. DiveDone false means it has ended
            // (here, or by a living joiner's lever during the card): never twice.
            float giveUpAt = Time.unscaledTime + CrewWipeEndTimeoutSeconds;
            string why = string.Empty;
            bool ended = false;
            while (ServerStillOn(state) && state.DiveDone && Time.unscaledTime < giveUpAt)
            {
                if (ServerEndDayBy("the crew wipe (nobody came back)", out why)) { ended = true; break; }
                yield return null; // a transient refusal (a trip, a ride, the dead still on their way): again next frame
            }
            if (!ServerStillOn(state)) { crewWipe = null; yield break; }
            if (!ended && state.DiveDone) Debug.LogWarning("[CrewWipe] The day could not end: " + why);
            else if (!ended) Debug.Log("[CrewWipe] The day had already ended during the card");
            state.ServerEndCrewWipe();
            crewWipe = null;
        }

        // ---- clients: the card ---------------------------------------------------------

        private void OnCrewWipeChangedForCard(CrewWipeState previous, CrewWipeState next)
        {
            if (!networkManager.ClientManager.Started || ScreenFade.Instance == null) return;
            if (next.Active && next.Serial != previous.Serial)
            {
                CrewWipeCardsShown++;
                ScreenFade.Instance.FadeOut(CrewWipeFadeSeconds, CrewWipeCardText);
            }
            else if (!next.Active && previous.Active) StartCoroutine(CrewWipeCardDown());
        }

        // The day's SyncVars arrive with the card's end; a day card that follows
        // (DAY n OF N, PAYDAY) takes the black over and fades in itself. Otherwise
        // the wipe's card fades in here.
        private IEnumerator CrewWipeCardDown()
        {
            yield return null;
            yield return null;
            ScreenFade fade = ScreenFade.Instance;
            if (fade != null && fade.Text == CrewWipeCardText) fade.FadeIn(CrewWipeFadeSeconds);
        }
    }
}
