using System.Collections.Generic;
using UnityEngine;

namespace SunkCost.Noise
{
    /// <summary>
    /// The spine of the game.
    ///
    /// Every loud action in Sunk Cost funnels through here: footsteps, sprinting,
    /// thrusters, the elevator, a dropped statue, a tank swap. Creatures subscribe.
    /// That is the whole design — "the noise tells the ocean where you are" is not a
    /// metaphor, it is this class.
    ///
    /// BUILD THIS IN WEEK ONE. It is trivial now and agony to retrofit once six
    /// creatures each have their own bespoke hearing logic.
    ///
    /// SERVER ONLY. Clients may play a sound effect locally for feedback, but they must
    /// never call Emit — see docs/NETWORK_CONTRACT.md section 3.
    /// </summary>
    public static class NoiseSystem
    {
        static readonly List<INoiseListener> Listeners = new List<INoiseListener>(64);

        /// <summary>Set true on the server at startup. Guards against client-side emits.</summary>
        public static bool IsServer;

        /// <summary>Draws every noise event in the scene view. Turn on while tuning creatures.</summary>
        public static bool DebugDraw;

        public static void Register(INoiseListener listener)
        {
            if (listener == null || Listeners.Contains(listener)) return;
            Listeners.Add(listener);
        }

        public static void Unregister(INoiseListener listener)
        {
            Listeners.Remove(listener);
        }

        /// <summary>
        /// Report a sound. Server only.
        /// Radius is in metres — how far the sound carries.
        /// </summary>
        public static void Emit(in NoiseEvent noise)
        {
            if (!IsServer)
            {
                Debug.LogError($"[Noise] Emit called on a client: {noise}. " +
                               "Noise is server-authoritative. See docs/NETWORK_CONTRACT.md.");
                return;
            }

            if (DebugDraw)
                Debug.DrawLine(noise.Position, noise.Position + Vector3.up * noise.Radius,
                               Color.yellow, 1.5f);

            // Iterate backwards so a listener can unregister itself while handling.
            for (int i = Listeners.Count - 1; i >= 0; i--)
            {
                var listener = Listeners[i];
                if (listener == null) { Listeners.RemoveAt(i); continue; }
                listener.OnNoise(in noise);
            }
        }

        /// <summary>Convenience overload for the common case.</summary>
        public static void Emit(Vector3 position, float radius, NoiseKind kind, int sourceId = 0)
            => Emit(new NoiseEvent(position, radius, kind, sourceId));

        /// <summary>Call when a level unloads. Listeners are per-scene.</summary>
        public static void Clear() => Listeners.Clear();
    }
}
