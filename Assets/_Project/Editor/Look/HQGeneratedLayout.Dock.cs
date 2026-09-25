using SunkCost.World;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Look
{
    public static partial class HQGeneratedLayout
    {
        public static void Dock(GameObject prefab)
        {
            var dock=Group(null,"Dock");
            // The bridge spans west from the HQ to the port boarding opening.
            // Its deck is flush with the ship. A narrow overlap bridges mesh edges.
            Slab(dock,"Level HQ bridge",new Vector3(-37, -.8f,-12),new Vector3(14,.8f,3),ShipModelSetup.DeckMaterial(),true);
            for(float x=-31;x>=-43;x-=2)
                foreach(float z in new[]{-13.45f,-10.55f}) Rail(dock,new Vector3(x,0,z));
            foreach(float x in new[]{-32f,-40f})
                foreach(float z in new[]{-13.45f,-10.55f})
                {
                    Model(dock,"DeckLamp",new Vector3(x,0,z),0,default,true);
                    Lamp(dock,new Vector3(x,1.4f,z),7,2);
                }
            var gantry=Model(dock,"ArrivalGantry",new Vector3(-31,0,-12),90,new Vector3(.85f,1,1));
            Box(gantry,new Vector3(-1.8f,1.5f,0),new Vector3(.4f,3,.6f));
            Box(gantry,new Vector3(1.8f,1.5f,0),new Vector3(.4f,3,.6f));
            Label(dock,"BLACK TIDE / BOARDING",new Vector3(-30.6f,2.6f,-12),3,.4f,90);
            var gate=Group(dock,"HQ Boarding Gate",new Vector3(-43.6f,0,-12));
            var barrier=Slab(gate,"Safety barrier",Vector3.zero,new Vector3(.15f,1.2f,3),ShipKitMaterials.Hazard());
            Box(barrier,new Vector3(0,4,0),new Vector3(.15f,8,3));
            gate.AddComponent<DockBoardingGate>().Configure(barrier);
            var ship=(GameObject)PrefabUtility.InstantiatePrefab(prefab);
            ship.transform.SetParent(dock.transform,false);
            ship.transform.SetPositionAndRotation(ShipPosition,Quaternion.Euler(0,180,0));
            var exclusion=Group(dock,"Fixed bridge exclusion",new Vector3(-37,0,-12));
            BoxCollider bridgeVolume=Box(exclusion,new Vector3(0,4,0),new Vector3(14,9,3));
            bridgeVolume.isTrigger=true;
            ship.GetComponent<ShipParts>().ConfigureDockBridge(bridgeVolume);
            // Slip plate belongs to the ship, follows it and overlaps the fixed
            // bridge only at rest. Neither the bridge nor its marker counts aboard.
        }

        public static void ShipBoarding(GameObject parent)
        {
            float x=-ShipDeckDressing.W(-6f)+.08f;
            var gate=Group(parent,"Ship Boarding Gate",new Vector3(x,0,-6));
            var barrier=Slab(gate,"Bulwark Boarding Barrier",new Vector3(0,-.12f,0),new Vector3(.22f,1.32f,4),ShipModelSetup.HullMaterial());
            Box(barrier,new Vector3(0,4,0),new Vector3(.22f,8,4));
            gate.AddComponent<DockBoardingGate>().Configure(barrier);
            Slab(parent,"Boarding threshold",new Vector3(x+.35f,-.07f,-6),new Vector3(1.2f,.07f,3.2f),ShipModelSetup.DeckMaterial(),true);
            foreach(float z in new[]{-8f,-4f})
                Slab(parent,"Boarding jamb",new Vector3(x,0,z),new Vector3(.25f,1.3f,.2f),ShipKitMaterials.Hazard(),true);
        }
    }
}
