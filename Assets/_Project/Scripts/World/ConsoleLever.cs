using FishNet;
using FishNet.Managing.Timing;
using UnityEngine;

namespace SunkCost.World
{
    // The lever's swing (the shared console, 27 September 2026): on every peer the
    // handle turns about its hinge - down toward the crew and back - when
    // CrewDayState announces an accepted pull of THIS console's kind (LeverPulled: a
    // new serial, never a joiner's initial value, never a refused pull), anchored on
    // the server tick the pull was accepted on so a peer that hears late lands
    // mid-swing rather than starting over. Presentation only: nothing here decides
    // anything, and the visual can never disagree with the gameplay state because it
    // is driven by that state alone.
    public sealed class ConsoleLever : MonoBehaviour
    {
        public const float SwingSeconds = 0.6f;
        // 30 degrees forward-down about the drum axis: the cheek plates' feet sink a
        // few centimetres into the housing's top at the end of the stroke, inside the
        // slot (the model inspector's limit; MODEL.md §6).
        public const float SwingDegrees = 30f;

        private ConsoleRig rig;
        private CrewDayState hooked;
        private Quaternion rest = Quaternion.identity;
        private bool restKnown;
        private uint startTick;
        private float startTime;
        private bool playing;

        // The last serial animated on this peer (the checks read it); 0 = none yet.
        public int PlayedSerial { get; private set; }
        // The current hinge angle in degrees, 0 at rest.
        public float Angle { get; private set; }
        public bool Playing => playing;

        private void Awake()
        {
            rig = GetComponent<ConsoleRig>();
            Transform hinge = rig != null ? rig.LeverHinge : null;
            if (hinge != null) { rest = hinge.localRotation; restKnown = true; }
        }

        private void OnEnable()
        {
            CrewDayState.InstanceChanged += Hook;
            Hook(CrewDayState.Instance);
        }

        private void OnDisable()
        {
            CrewDayState.InstanceChanged -= Hook;
            Hook(null);
            Rest();
        }

        private void Hook(CrewDayState day)
        {
            if (hooked == day) return;
            if (hooked != null) hooked.LeverPulled -= Play;
            hooked = day;
            if (hooked != null) hooked.LeverPulled += Play;
        }

        // An accepted pull: swing if it is this console's kind. PlayedSerial only ever
        // grows: a serial already played (any caller replaying the SyncVar) swings nothing.
        public void Play(LeverPull pull)
        {
            if (pull.Serial == 0) return;
            if (rig != null && pull.Kind != rig.Kind) return;
            if (pull.Serial <= PlayedSerial) return;
            PlayedSerial = pull.Serial;
            startTick = pull.Tick;
            startTime = Time.time;
            playing = true;
        }

        private void Update()
        {
            if (!playing) return;
            Transform hinge = rig != null ? rig.LeverHinge : null;
            if (hinge == null) { playing = false; return; }
            if (!restKnown) { rest = hinge.localRotation; restKnown = true; }
            float t = Elapsed();
            if (t >= SwingSeconds) { Rest(); return; }
            Angle = SwingDegrees * Profile(t / SwingSeconds);
            hinge.localRotation = rest * Quaternion.AngleAxis(Angle, Vector3.right); // +X: +Y toward +Z, the grip forward and down
        }

        private void Rest()
        {
            playing = false;
            Angle = 0f;
            Transform hinge = rig != null ? rig.LeverHinge : null;
            if (hinge != null && restKnown) hinge.localRotation = rest;
        }

        // Down fast with an easing stop, a short hold, back smoothly.
        private static float Profile(float u)
        {
            if (u < 0.42f) return Mathf.Sin(u / 0.42f * Mathf.PI / 2f);
            if (u < 0.55f) return 1f;
            float back = (u - 0.55f) / 0.45f;
            return 1f - Mathf.SmoothStep(0f, 1f, back);
        }

        // Seconds since the accepted tick by the synchronized clock; the local clock
        // when there is none (the editor, a test without a session).
        private float Elapsed()
        {
            TimeManager time = InstanceFinder.TimeManager;
            if (time != null && startTick != 0 && time.Tick != 0)
            {
                uint elapsedTicks = unchecked(time.Tick - startTick); // wrap-safe
                if (elapsedTicks > int.MaxValue) return 0f;             // the pull is still in this peer's future
                return (float)(time.TicksToTime(elapsedTicks) + time.GetTickElapsedAsDouble());
            }
            return Time.time - startTime;
        }
    }
}
