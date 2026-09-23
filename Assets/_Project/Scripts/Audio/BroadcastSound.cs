using SunkCost.Player;
using SunkCost.World;
using UnityEngine;

namespace SunkCost.Audio
{
    // A game sound the deck TV carries (docs/SPECTATING_IMPLEMENTATION_PLAN.md
    // card 3): put it next to an AudioSource in the dive world. On a ship client
    // whose TV is live, whenever the source starts playing within earshot of the
    // channel diver, the same clip plays once more at the TV's speaker, as loud
    // as the diver hears it. Local presentation only; nothing is replicated —
    // the source plays on every peer already. No source is flagged yet (the
    // winch and the cabin doors have no clips); this is the hook for them.
    [RequireComponent(typeof(AudioSource))]
    public sealed class BroadcastSound : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float earshotMeters = 20f;

        private AudioSource source;
        private AudioSource echo;
        private bool wasPlaying;

        private void Awake()
        {
            source = GetComponent<AudioSource>();
        }

        private void Update()
        {
            bool playing = source.isPlaying;
            bool started = playing && !wasPlaying;
            wasPlaying = playing;
            if (!started || source.clip == null) return;
            ShipParts ship = ShipParts.InWorld(WorldId.Sea);
            ShipTV tv = ship != null ? ship.GetComponent<ShipTV>() : null;
            HQPlayerController diver = tv != null && tv.Live ? tv.Diver : null;
            if (diver == null) return;
            float distance = Vector3.Distance(diver.EyePosition, transform.position);
            if (distance > earshotMeters) return;
            if (echo == null)
            {
                GameObject go = new("TvEcho (" + source.name + ")");
                go.transform.SetParent(ship.transform, false);
                echo = go.AddComponent<AudioSource>();
                echo.spatialBlend = 1f;
                echo.rolloffMode = AudioRolloffMode.Linear;
                // Heard as far as the picture is drawn, through the chosen output like
                // every other source (ship audit SHIP-085, 23 September 2026).
                echo.maxDistance = ShipTV.ViewerMetres;
                AudioDeviceService.RouteSource(echo);
            }
            echo.transform.position = tv.SpeakerPosition;
            echo.PlayOneShot(source.clip, source.volume * (1f - distance / earshotMeters));
        }
    }
}
