using System;
using SunkCost.Editor.Prototype;
using SunkCost.Look;
using SunkCost.Shop;
using SunkCost.World;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SunkCost.Editor.Look
{
    // The approved 60 x 36 m layout. Coordinates are metres, deck top Y=0,
    // north +Z. Art can change without replacing the shop/crew/plank rules.
    public static partial class HQGeneratedLayout
    {
        public static readonly Vector3 Arrival = new(18f, 0f, 14f);
        public static readonly Quaternion ArrivalFacing = Quaternion.Euler(0,180,0);
        public static Vector3 ShipPosition => new(-44f - ShipDeckDressing.W(-6f) + .08f, 0f, -18f);
        public const float BridgeHalfWidth = 1.5f;

        internal static Material CourtMaterial()
        {
            const string path = "Assets/_Project/Art/HQ/Materials/GeneratedCourt.mat";
            Material m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null) { m=new Material(ShipModelSetup.DeckMaterial());AssetDatabase.CreateAsset(m,path); }
            m.SetColor("_BaseColor",new Color(.38f,.48f,.52f));
            EditorUtility.SetDirty(m);
            return m;
        }

        internal static GameObject Group(GameObject parent, string name, Vector3 at = default, float yaw = 0f)
        {
            GameObject go = new(name);
            if (parent != null) go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return go;
        }

        internal static GameObject Model(GameObject parent, string part, Vector3 at, float yaw = 0f, Vector3 scale = default, bool ship = false)
        {
            string path = ship ? ShipModelSetup.PrefabPath(part) : $"{HQModelSetup.Prefabs}/{part}.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new InvalidOperationException("Missing art: " + path);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = at; go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = scale == default ? Vector3.one : scale;
            return go;
        }

        internal static BoxCollider Box(GameObject parent, Vector3 centre, Vector3 size)
        {
            var c = parent.AddComponent<BoxCollider>(); c.center = centre; c.size = size; return c;
        }

        internal static void SurfaceCollision(GameObject model)
        {
            // Static machinery has open space under/around its mechanism. A box
            // at the prefab origin blocks empty deck when the mesh is offset.
            foreach(var filter in model.GetComponentsInChildren<MeshFilter>())
                filter.gameObject.AddComponent<MeshCollider>().sharedMesh=filter.sharedMesh;
        }

        internal static GameObject Slab(GameObject parent, string name, Vector3 bottom, Vector3 size, Material material, bool solid = false)
        {
            var go = Group(parent, name, bottom);
            go.AddComponent<MeshFilter>().sharedMesh = MeshKit.Box(size);
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            if (solid) Box(go, Vector3.up * size.y / 2f, size);
            return go;
        }

        internal static void Label(GameObject parent, string text, Vector3 at, float width, float height = .55f, float yaw = 180f)
        {
            var label = PropBuilder.SignPlate(parent, text + " Sign", text, at, Quaternion.Euler(0f, yaw, 0f), width, height, height * .65f, new Color(1f, .84f, .58f));
            label.text = text;
            label.GetComponent<PlateText>().Refresh();
            foreach (Renderer r in label.transform.parent.GetComponentsInChildren<Renderer>())
                if (r.name.StartsWith("Frame ")) r.sharedMaterial = ShipKitMaterials.Bezel();
        }

        internal static void Lamp(GameObject parent, Vector3 at, float range = 7f, float intensity = 2f)
        {
            var go = Group(parent, "Warm work light", at);
            var light = go.AddComponent<Light>(); light.type = LightType.Point;
            light.color = new Color(1f, .66f, .30f); light.intensity = intensity;
            light.range = range; light.shadows = LightShadows.None;
        }

        internal static void Frame(GameObject parent, Vector3 centre, float w, float d)
        {
            foreach (float s in new[] { -1f, 1f })
            {
                Slab(parent, "Safety marking", centre + new Vector3(0, .055f, s * d / 2), new Vector3(w, .012f, .12f), ShipKitMaterials.Hazard());
                Slab(parent, "Safety marking", centre + new Vector3(s * w / 2, .055f, 0), new Vector3(.12f, .012f, d), ShipKitMaterials.Hazard());
            }
        }

        public static void Structure(GameObject root)
        {
            var support = Group(root, "Generated support structure");
            foreach (float x in new[] { -24f, 0f, 24f })
                foreach (float z in new[] { -12f, 12f })
                {
                    Slab(support, "Leg footing", new Vector3(x,-16,z), new Vector3(2.6f,.2f,2.6f), ShipKitMaterials.Steel());
                    for (int i = 0; i < 5; i++) Model(support, "LegSection", new Vector3(x, -15.8f + i * 3f, z));
                    Box(support, new Vector3(x, -8.3f, z), new Vector3(2.35f, 15f, 2.35f));
                    if (z < 0) Model(support, "Fender", new Vector3(x, -7f, z - 1.2f));
                }
            foreach (float z in new[] { -17.95f, 17.95f })
                for (float x = -28f; x <= 28f; x += 4f)
                {
                    Model(support, "Fascia", new Vector3(x, -1f, z), z < 0 ? 0 : 180);
                    Model(support, "Girder", new Vector3(x, -1.8f, z));
                }
            foreach (float x in new[] { -29.95f, 29.95f })
                for (float z = -16f; z <= 16f; z += 4f) Model(support, "Fascia", new Vector3(x, -1f, z), x < 0 ? 90 : -90);
            foreach (float z in new[] { -12f, 12f })
                for (float x = -22f; x < 24f; x += 4f)
                {
                    Model(support, "Girder", new Vector3(x, -4f, z));
                    var brace = Model(support, "Brace", new Vector3(x, -6f, z), 0, new Vector3(1.25f, 1, 1));
                    brace.transform.localRotation = Quaternion.Euler(0, 0, ((int)x % 8 == 2 ? 30 : -30));
                }
        }

        private static void Rail(GameObject root, Vector3 at, float yaw = 0)
        {
            var go = Model(root, "Railing", at, yaw, new Vector3(1, 1.98f, 1), true);
            // The ship rail is approximately 2 m wide, 0.706 high. Collision keeps
            // the little gaps between generated bars from catching capsules/balls.
            Box(go, new Vector3(0, .35f, 0), new Vector3(2, .7f, .16f));
        }

        public static void Perimeter(GameObject root)
        {
            var rails = Group(root, "Ship kit rails and lamps");
            for (float x = -29; x < 30; x += 2)
                foreach (float z in new[] { -17.8f, 17.8f }) Rail(rails, new Vector3(x, 0, z));
            for (float z = -17; z < 18; z += 2)
            {
                if (Mathf.Abs(z + 12) > 2) Rail(rails, new Vector3(-29.8f, 0, z), 90);
                if (Mathf.Abs(z + 16) > 1.5f) Rail(rails, new Vector3(29.8f, 0, z), 90);
            }
            // Close to the narrow plank's existing gated opening.
            foreach (float z in new[] { -17.3f, -14.7f })
                Box(rails, new Vector3(29.8f, .7f, z), new Vector3(.2f, 1.4f, 1.8f));
            for (float x = -28; x <= 28; x += 8)
                foreach (float z in new[] { -17.7f, 17.7f })
                {
                    Model(rails, "DeckLamp", new Vector3(x, 0, z), 0, default, true);
                    Lamp(rails, new Vector3(x, 1.4f, z), 9, 2.2f);
                }
        }

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
            Label(pad, "CREW ARRIVAL  /  01–04", new Vector3(0,2.7f,-1.25f), 3.3f, .35f, 0);
            foreach (float x in new[] { -.75f,.75f }) foreach (float z in new[] { -.5f,1f })
                Frame(pad, new Vector3(x,0,z), .7f,.7f);
            Label(pad, "CREW ARRIVAL  /  01-04", new Vector3(0,2.7f,-1.95f), 3.3f, .35f);
            Model(pad, "Console", new Vector3(0,0,3), 180, default, true);
            Box(pad, new Vector3(0,.55f,3), new Vector3(1.8f,1.1f,.7f));
            Slab(pad,"Colour panel mast",new Vector3(0,0,3.2f),new Vector3(.16f,2.3f,.16f),ShipKitMaterials.Steel(),true);
            HQPlatformBuilder.ColourPanel(pad, new Vector3(0,1.4f,2.65f));
            Label(pad, "CHOOSE YOUR CREW COLOUR", new Vector3(0,2.3f,3), 3);
            Lamp(pad, new Vector3(0,2.6f,0), 6, 3);
            pad.transform.SetPositionAndRotation(Arrival,ArrivalFacing);
        }

        public static void Dressing(GameObject root)
        {
            var cargo = Group(root, "Cargo apron");
            SurfaceCollision(Model(cargo,"Crane",new Vector3(24,0,4),-100,new Vector3(2,2,2),true));
            Frame(cargo,new Vector3(24,0,4),5,5);
            SurfaceCollision(Model(cargo,"Winch",new Vector3(27,0,-4),90,default,true));
            Model(cargo,"Container",new Vector3(20,0,-10),90,default,true);
            Box(cargo,new Vector3(20,1.3f,-10),new Vector3(2.5f,2.6f,6));
            foreach (Vector3 at in new[] { new Vector3(16,0,5),new Vector3(26,0,9),new Vector3(26,0,12) })
            {
                Model(cargo,"Pallet",at);
                Model(cargo,"Crate",at+Vector3.up*.16f,0,default,true);
                Box(cargo,at+Vector3.up*.6f,new Vector3(1.1f,1.2f,1.1f));
            }
            foreach (Vector3 at in new[] {new Vector3(26,0,15),new Vector3(27,0,16),new Vector3(28,0,15)})
            {
                Model(cargo,"Barrel",at,0,default,true);Box(cargo,at+Vector3.up*.55f,new Vector3(.8f,1.1f,.8f));
            }
            Model(cargo,"CableCoil",new Vector3(26,0,-6),0,default,true);
            Model(cargo,"Bollard",new Vector3(28,0,9),0,default,true);
            Model(cargo,"Toolbox",new Vector3(22,.16f,11),15,default,true);
            foreach (float x in new[] {-26f,4f,27f}) Model(root,"Lifebuoy",new Vector3(x,1,-17.5f),0,default,true);
            Model(root,"Pipes",new Vector3(-26,-2,-17.9f),0,new Vector3(2,1,1),true);
            var rest = Group(root,"Crew rest");
            Model(rest,"Couch",new Vector3(7,0,-11),180,default,true);
            Box(rest,new Vector3(7,.5f,-11),new Vector3(2.8f,1,1));
            Model(rest,"Table",new Vector3(7,0,-13),0,default,true);
            Box(rest,new Vector3(7,.4f,-13),new Vector3(1.6f,.8f,.8f));
            foreach(float x in new[]{-16f,-8f,0f})
            {
                Model(root,"Bench",new Vector3(x,0,6),180,default,true);
                Box(root,new Vector3(x,.45f,6),new Vector3(2,.9f,.6f));
            }
            foreach(float x in new[]{24.2f,27.8f}) Slab(cargo,"Cargo sign post",new Vector3(x,0,16.8f),new Vector3(.12f,2.2f,.12f),ShipKitMaterials.Steel(),true);
            Label(cargo,"CARGO / KEEP ACCESS CLEAR",new Vector3(26,2,16.7f),4);
            // A compact, physical wayfinding board at the arrival exit. Keep the
            // centre open for walking and ball play instead of filling it with props.
            var wayfinding=Group(root,"Arrival wayfinding",new Vector3(14,0,8));
            foreach(float x in new[]{-1.2f,1.2f})
                Slab(wayfinding,"Sign upright",new Vector3(x,0,0),new Vector3(.1f,2.2f,.1f),ShipKitMaterials.Steel(),true);
            Label(wayfinding,"DEPOT  >",new Vector3(0,2,0),2.7f,.35f,0);
            Label(wayfinding,"SHIP  >     QUOTA  >",new Vector3(0,1.55f,0),2.7f,.35f,0);
            Lamp(wayfinding,new Vector3(0,2.3f,.5f),5,1.5f);
            Lamp(rest,new Vector3(7,2.3f,-12),5,1.8f);
        }
    }
}
