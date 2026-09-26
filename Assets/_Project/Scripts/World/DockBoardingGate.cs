using UnityEngine;

namespace SunkCost.World
{
    // Collision/presentation derived from the existing crew departure state. No
    // RPC, ownership, physics writer or additional network state. Both the fixed
    // HQ barrier and the ship's boarding gate close before pull-away starts.
    public sealed class DockBoardingGate : MonoBehaviour
    {
        [SerializeField] private GameObject barrier;
        public bool Closed => barrier != null && barrier.activeSelf;
        public void Configure(GameObject value) { barrier = value; barrier.SetActive(false); }
        private void Awake() { if (barrier != null) barrier.SetActive(true); }

        private void Update()
        {
            CrewDayState day = CrewDayState.Instance;
            bool atHQ = WorldScenes.TryParse(gameObject.scene.name, out WorldId world) && world == WorldId.HQ;
            bool open = atHQ && day != null && day.World == WorldId.HQ && !day.Travelling;
            if (barrier != null && barrier.activeSelf == open) barrier.SetActive(!open);
        }
    }
}
