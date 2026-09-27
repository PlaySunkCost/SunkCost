using UnityEngine;

namespace SunkCost.World
{
    // The red GIVE UP button on the HQ board's desk (Dan, 27 September 2026): aim and
    // press E to vote (again to take the vote back). A marker for the aim; the vote,
    // its count and the ending are the server's (WorldSceneFlow.GiveUp).
    public sealed class GiveUpButton : MonoBehaviour
    {
        public const string ButtonName = "Give Up Button";
    }
}
