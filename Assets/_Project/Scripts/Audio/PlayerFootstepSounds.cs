using SunkCost.Noise;
using SunkCost.Player;
using UnityEngine;

namespace SunkCost.Audio
{
    // What a diver's feet sound like (Dan, 18 September 2026: "there are no
    // footsteps sound at all… add jump sound"): a step every stride of ground
    // covered — the sound's own human pace (AudioLibrary, Dan 26 September 2026;
    // the ocean's ears keep NoiseSettings' shorter strides), a recorded set of
    // steps in turn, louder and higher sprinting, none crouched — plus a
    // thud on landing (a takeoff makes no sound: Dan, 18 September 2026).
    // Presentation on every peer from
    // the same transform: the owner from its own motor (grounded, vertical
    // speed), a friend's copy from what the NetworkTransform brings (its
    // height). 3D at the feet; the owner's own steps are quieter. Clips from
    // the AudioLibrary (placeholders until recordings replace them). Nothing
    // here is the gameplay noise — that is PlayerNoise, server only.
    public sealed class PlayerFootstepSounds : MonoBehaviour
    {
        private HQPlayerController controller;
        private PlayerStance stance;
        private AudioSource feet;
        private AudioLibrary library;
        private Vector3 lastPosition;
        private float sinceStep, lastY, airborneSince = -1f;
        private bool primed, wasGrounded = true, left;
        private int stepIndex;

        // For the checks.
        public int StepsPlayed { get; private set; }
        public int LandingsPlayed { get; private set; }

        private void Awake()
        {
            controller = GetComponent<HQPlayerController>();
            stance = GetComponent<PlayerStance>();
            library = AudioLibrary.Get();
            var go = new GameObject("Feet");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.1f, 0f);
            feet = go.AddComponent<AudioSource>();
            feet.playOnAwake = false; feet.loop = false;
            feet.spatialBlend = 1f; feet.rolloffMode = AudioRolloffMode.Linear;
            feet.minDistance = 1.5f; feet.maxDistance = 25f; feet.dopplerLevel = 0f;
            AudioDeviceService devices = FindAnyObjectByType<AudioDeviceService>();
            if (devices != null) devices.Route(feet);
        }

        private void LateUpdate()
        {
            if (controller == null || feet == null) return;
            Vector3 position = transform.position;
            if (!primed) { lastPosition = position; lastY = position.y; primed = true; return; }
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Vector3 flat = position - lastPosition; flat.y = 0f;
            float moved = flat.magnitude;
            float rise = position.y - lastY;
            lastPosition = position; lastY = position.y;
            if (controller.IsDead || controller.TravelLocked) { sinceStep = 0f; return; }
            if (moved > 3f) { sinceStep = 0f; return; } // a teleport
            bool ownerView = controller.IsOwner;
            float ownScale = ownerView ? library.OwnFootstepScale : 1f;

            // Takeoff and landing: the owner knows its motor; a friend's copy is read
            // from its height — a clear rise while it was on the ground, then a fall
            // that stops.
            bool grounded = ownerView ? controller.IsGrounded : Mathf.Abs(rise) < 0.02f;
            if (ownerView)
            {
                // A landing only after real flight (not the ground probe blinking on a teleport or a step).
                if (wasGrounded && !grounded) airborneSince = Time.unscaledTime; // a takeoff is silent; the landing is the sound
                if (!wasGrounded && grounded && airborneSince >= 0f && Time.unscaledTime - airborneSince > 0.15f) Play(library.Land, library.LandVolume * ownScale, 0.95f, ref LandingsPlayedBacking);
                if (grounded) airborneSince = -1f;
            }
            else
            {
                if (airborneSince < 0f && rise > 0.05f) airborneSince = Time.unscaledTime;
                else if (airborneSince >= 0f && grounded && Time.unscaledTime - airborneSince > 0.15f) { airborneSince = -1f; Play(library.Land, library.LandVolume, 0.95f, ref LandingsPlayedBacking); }
            }
            wasGrounded = grounded;
            if (!grounded) return;

            // Footsteps: crouched, none at all (the stealth move, like the noise).
            bool crouched = stance != null && stance.IsCrouched;
            if (crouched || moved <= 0f) { if (crouched) sinceStep = 0f; return; }
            float speed = moved / dt;
            float factor = controller.SpeedFactor;
            bool sprinting = speed > 0.5f * (controller.WalkSpeed + controller.SprintSpeed) * factor;
            // The sound's own pace (a human's, Dan 26 September 2026), not the noise's strides.
            sinceStep += moved;
            float stride = sprinting ? library.FootstepSprintMetres : library.FootstepWalkMetres;
            if (sinceStep < stride) return;
            sinceStep -= stride;
            left = !left;
            float pitch = (left ? 0.96f : 1.04f) * (sprinting ? 1.08f : 1f);
            Play(library.FootstepAt(stepIndex++), (sprinting ? library.SprintFootstepVolume : library.FootstepVolume) * ownScale, pitch, ref StepsPlayedBacking);
        }

        private int StepsPlayedBacking, LandingsPlayedBacking;
        private void Play(AudioClip clip, float volume, float pitch, ref int counter)
        {
            if (clip == null) return;
            feet.pitch = pitch;
            feet.PlayOneShot(clip, volume);
            counter++;
            StepsPlayed = StepsPlayedBacking; LandingsPlayed = LandingsPlayedBacking;
        }
    }
}
