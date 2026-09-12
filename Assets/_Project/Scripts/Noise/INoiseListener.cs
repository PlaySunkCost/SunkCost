namespace SunkCost.Noise
{
    /// <summary>
    /// Anything that reacts to sound. Creatures are the obvious case, but motion
    /// sensors, the sonar screen on the boat, and debug tooling all implement this too.
    ///
    /// The listener decides for itself whether it heard the event. The noise system
    /// does no distance filtering, because hearing range is a property of the listener,
    /// not the sound — a Bell Eater hears the elevator across the whole map, a Mimic
    /// Crab hears nothing beyond three metres.
    /// </summary>
    public interface INoiseListener
    {
        /// <summary>
        /// Called on the SERVER for every noise event in the level.
        /// Implementations must be cheap: this runs for every listener on every noise.
        /// Do the distance check first and return early.
        /// </summary>
        void OnNoise(in NoiseEvent noise);
    }
}
