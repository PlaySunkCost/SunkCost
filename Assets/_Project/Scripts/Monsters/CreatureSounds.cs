using SunkCost.Audio;
using UnityEngine;

namespace SunkCost.Monsters
{
    // A creature's sounds on every peer, from its replicated pose and serials:
    // its call when it starts to hunt (and the Charger's wind-up), the shot when
    // a bolt leaves, the hit when a strike lands. Placeholders from AudioLibrary
    // until the signature sounds. The Impostor is heard only where it is seen
    // (ImpostorLook). Presentation only; nothing replicates from here.
    public sealed class CreatureSounds : MonoBehaviour
    {
        private Creature creature;
        private CreatureBolts bolts;
        private ImpostorLook impostor;
        private AudioSource voice;
        private AudioLibrary library;
        private float nextCallAt;
        private CreaturePose lastPose;

        public int CallsPlayed { get; private set; }

        private void Awake()
        {
            creature = GetComponent<Creature>();
            bolts = GetComponent<CreatureBolts>();
            impostor = GetComponent<ImpostorLook>();
            library = AudioLibrary.Get();
            var go = new GameObject("Voice source");
            // A modelled monster's voice sits on its Voice anchor (it moves with the head); the placeholders' at chest height.
            CreatureRig rig = GetComponent<CreatureRig>();
            Transform anchor = rig != null ? rig.VoiceAnchor : null;
            go.transform.SetParent(anchor != null ? anchor : transform, false);
            go.transform.localPosition = anchor != null ? Vector3.zero : new Vector3(0f, 1.2f, 0f);
            voice = go.AddComponent<AudioSource>();
            voice.playOnAwake = false; voice.loop = false;
            voice.spatialBlend = 1f; voice.rolloffMode = AudioRolloffMode.Linear;
            voice.minDistance = 3f; voice.maxDistance = 45f; voice.dopplerLevel = 0f;
            AudioDeviceService devices = FindAnyObjectByType<AudioDeviceService>();
            if (devices != null) devices.Route(voice);
        }

        private void OnEnable()
        {
            if (creature != null) { creature.PoseChanged += OnPose; creature.Struck += OnStruck; }
            if (bolts != null) { bolts.Aimed += OnCharge; bolts.Fired += OnBolt; bolts.Ended += OnBeamEnd; bolts.Hit += OnStruck; }
        }
        // The beam (Dan, 22 September 2026): a rising charge for the second before it
        // fires, then the beam's own hum for as long as it burns, from the voice's spot.
        private AudioSource beamLoop;
        private AudioSource BeamLoop
        {
            get
            {
                if (beamLoop != null) return beamLoop;
                beamLoop = voice.gameObject.AddComponent<AudioSource>();
                beamLoop.playOnAwake = false; beamLoop.loop = true;
                beamLoop.spatialBlend = 1f; beamLoop.rolloffMode = AudioRolloffMode.Linear;
                beamLoop.minDistance = 3f; beamLoop.maxDistance = 45f; beamLoop.dopplerLevel = 0f;
                beamLoop.clip = library.BeamLoop; beamLoop.volume = library.BeamLoopVolume;
                AudioDeviceService devices = FindAnyObjectByType<AudioDeviceService>();
                if (devices != null) devices.Route(beamLoop);
                return beamLoop;
            }
        }
        private void OnCharge(BeamCue cue)
        {
            if (!Audible) return;
            voice.PlayOneShot(library.BeamCharge, library.BeamChargeVolume);
        }
        private void OnBolt(BeamCue cue)
        {
            if (!Audible) return;
            voice.PlayOneShot(library.BoltShot, library.BoltShotVolume);
            BeamLoop.Play();
        }
        private void OnBeamEnd(BeamCue cue)
        {
            if (beamLoop != null && beamLoop.isPlaying) beamLoop.Stop();
        }

        private void OnDisable()
        {
            if (creature != null) { creature.PoseChanged -= OnPose; creature.Struck -= OnStruck; }
            if (bolts != null) { bolts.Aimed -= OnCharge; bolts.Fired -= OnBolt; bolts.Ended -= OnBeamEnd; bolts.Hit -= OnStruck; }
            if (beamLoop != null && beamLoop.isPlaying) beamLoop.Stop();
        }


        private bool Audible => impostor == null || impostor.AudibleToLocal;

        private void OnPose(CreaturePose pose)
        {
            bool starts = (pose == CreaturePose.Hunting || pose == CreaturePose.Windup) && lastPose != CreaturePose.Hunting && lastPose != CreaturePose.Windup && lastPose != CreaturePose.Rushing;
            lastPose = pose;
            if (!starts || Time.unscaledTime < nextCallAt || !Audible) return;
            nextCallAt = Time.unscaledTime + 3f;
            voice.PlayOneShot(library.MonsterCall, library.MonsterCallVolume);
            CallsPlayed++;
        }

        private void OnStruck()
        {
            if (!Audible) return;
            voice.PlayOneShot(library.MonsterHit, library.MonsterHitVolume);
        }
    }
}
