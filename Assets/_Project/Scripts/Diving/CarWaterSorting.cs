using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SunkCost.Diving
{
    // The transparency order of the glass car in the glass tube (the new elevator, 28
    // September 2026). Unity sorts transparent renderers per renderer, and the right order
    // seen from inside the car is the reverse of the order seen from outside it; the local
    // player's camera, a spectator's and the ship TV's render in the same frame from
    // different places. So for every camera about to render, this sets each group's
    // sortingOrder from whether THAT camera stands inside the car (lower draws first; every
    // other transparent stays at 0 and draws after). Presentation only, per camera, derived
    // from the camera and the car: nothing synced. On the car root.
    [DefaultExecutionOrder(100)]
    public sealed class CarWaterSorting : MonoBehaviour
    {
        public enum Group : byte { TubeGlass = 0, TubeWater = 1, CarGlass = 2, CarWaterSurface = 3, CarWaterFX = 4 }

        //                                       TubeGlass TubeWater CarGlass CarSurface CarFX
        private static readonly int[] InsideOrder = { -6, -5, -4, -3, -2 };
        private static readonly int[] OutsideOrder = { -1, -2, -3, -5, -4 };

        public static int OrderOf(Group group, bool cameraInsideCar) =>
            (cameraInsideCar ? InsideOrder : OutsideOrder)[(int)group];

        // Names the groups are found by (INTERFACES.md §2.6; the builders keep them).
        public const string ShaftTubeName = "Shaft Tube";
        private static readonly string[] CarGlassRoots = { "Glass Shell", "Car Look" };
        private static readonly string[] TubeGlassRoots = { "TubeGlass", "Gate Leaf Right", "Gate Leaf Left" };
        private static readonly string[] TubeWaterNames = { "WaterSurface", "WaterSurface Ring" };

        [SerializeField] private ElevatorController car;

        private readonly List<Renderer>[] groups = { new(), new(), new(), new(), new() };
        private bool collected, tubeFound;
        private int lastApplied = -1;   // -1 none, 0 outside, 1 inside
        private int retryFrame;

        public bool LastCameraInside => lastApplied == 1;
        public int CountOf(Group group) { Collect(); return groups[(int)group].Count; }

        public void Configure(ElevatorController carController) => car = carController;

        private void OnEnable() => RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        private void OnDisable() => RenderPipelineManager.beginCameraRendering -= OnBeginCamera;

        private void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (this == null || camera == null) return;
            if (car == null) car = GetComponent<ElevatorController>();
            if (car == null) return;
            Collect();
            bool inside = camera.cameraType == CameraType.Game && car.IsInsideCar(camera.transform.position);
            int want = inside ? 1 : 0;
            if (want == lastApplied) return;
            Apply(inside);
        }

        // Also callable by a test hook or a capture that wants a given camera's order now.
        public void Apply(bool cameraInsideCar)
        {
            Collect();
            for (int g = 0; g < groups.Length; g++)
            {
                int order = OrderOf((Group)g, cameraInsideCar);
                foreach (Renderer renderer in groups[g])
                    if (renderer != null && renderer.sortingOrder != order) renderer.sortingOrder = order;
            }
            lastApplied = cameraInsideCar ? 1 : 0;
        }

        private void Collect()
        {
            if (collected && (tubeFound || Time.frameCount < retryFrame)) return;
            if (!collected)
            {
                collected = true;
                Transform root = transform;
                foreach (string name in CarGlassRoots) AddUnder(FindDeep(root, name), Group.CarGlass);
                Transform door = root.Find("Elevator Door");
                if (door != null)
                    foreach (Transform child in door)
                        if (child.name.StartsWith("Leaf ")) AddUnder(child, Group.CarGlass);
                Transform surface = root.Find("Cabin Water");
                if (surface != null) AddOne(surface.GetComponent<Renderer>(), Group.CarWaterSurface);
                AddUnder(root.Find("Cabin Water FX"), Group.CarWaterFX);
            }
            // The tube lives in the same scene as the car; it may load after the car.
            retryFrame = Time.frameCount + 60;
            Transform tube = null;
            foreach (GameObject sceneRoot in gameObject.scene.GetRootGameObjects())
                if (sceneRoot.name == ShaftTubeName) { tube = sceneRoot.transform; break; }
            if (tube == null) return;
            tubeFound = true;
            foreach (string name in TubeGlassRoots) AddUnder(FindDeep(tube, name), Group.TubeGlass);
            foreach (string name in TubeWaterNames)
            {
                Transform water = tube.Find(name);
                if (water != null) AddOne(water.GetComponent<Renderer>(), Group.TubeWater);
            }
            lastApplied = -1;
        }

        private void AddUnder(Transform root, Group group)
        {
            if (root == null) return;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true)) AddOne(renderer, group);
        }

        private void AddOne(Renderer renderer, Group group)
        {
            if (renderer == null) return;
            foreach (List<Renderer> list in groups) if (list.Contains(renderer)) return;
            groups[(int)group].Add(renderer);
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
