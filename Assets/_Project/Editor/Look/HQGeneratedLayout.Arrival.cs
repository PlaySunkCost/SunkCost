using UnityEngine;

namespace SunkCost.Editor.Look
{
    public static partial class HQGeneratedLayout
    {
        public static void ArrivalArea(GameObject root)
        {
            // Author around zero, then rotate the entire station towards the HQ.
            // The colour console is ahead of all four spawn points, not behind.
            var pad = Group(root, "Crew Arrival");
            Slab(pad, "Flush arrival pad", new Vector3(0,.025f,0), new Vector3(6,.015f,4), ShipKitMaterials.Steel());
            Frame(pad, Vector3.zero, 6, 4);
            var gantry = Model(pad, "ArrivalGantry", new Vector3(0,0,-1.6f));
            Box(gantry, new Vector3(-1.8f,1.5f,0), new Vector3(.4f,3,.6f));
            Box(gantry, new Vector3(1.8f,1.5f,0), new Vector3(.4f,3,.6f));
            Box(gantry, new Vector3(0,2.8f,0), new Vector3(4,.4f,.6f));
            Label(pad, "arrival", new Vector3(0,2.7f,-1.25f), 3.3f, .35f, 0);
            foreach (float x in new[] { -.75f,.75f }) foreach (float z in new[] { -.5f,1f })
                Frame(pad, new Vector3(x,0,z), .7f,.7f);
            Label(pad, "arrival", new Vector3(0,2.7f,-1.95f), 3.3f, .35f);
            // The crew colour console (Dan, 8 October 2026) where the colour panel stood:
            // the shared console's rig on its footing, facing the pad (local yaw 180 = the
            // panel's old facing), its own screens carrying CHOOSE YOUR CREW COLOUR.
            ColourConsoleBuilder.Place(pad, new Vector3(0,0,3.2f), 180);
            Lamp(pad, new Vector3(0,2.6f,0), 6, 3);
            pad.transform.SetPositionAndRotation(Arrival,ArrivalFacing);
        }
    }
}
