using UnityEngine;

namespace SunkCost.Sites
{
    [CreateAssetMenu(fileName = "DiveSiteSettings", menuName = "Sunk Cost/Dive Site Settings")]
    public sealed class DiveSiteSettings : ScriptableObject
    {
        [SerializeField] private float shaftDepthMeters = 45f;

        public float ShaftDepthMeters => shaftDepthMeters;
    }
}
