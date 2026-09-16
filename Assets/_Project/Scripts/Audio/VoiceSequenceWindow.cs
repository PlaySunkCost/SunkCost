namespace SunkCost.Audio
{
    // Accept out-of-order frames once within a bounded 16-packet window, including wrap.
    public sealed class VoiceSequenceWindow
    {
        private bool initialized;
        private ushort newest;
        private uint seen;
        public void Reset() { initialized = false; seen = 0; }
        public bool Accept(ushort sequence)
        {
            if (!initialized) { initialized = true; newest = sequence; seen = 1; return true; }
            int delta = (short)(sequence - newest);
            if (delta > 0) { seen = delta >= 16 ? 1u : (seen << delta) | 1u; newest = sequence; return true; }
            int age = -delta;
            if (age >= 16 || (seen & (1u << age)) != 0) return false;
            seen |= 1u << age; return true;
        }
    }
}
