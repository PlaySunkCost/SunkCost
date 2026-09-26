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
            // Last three metres lift before the ship moves. Only the leaf's small
            // tip overlaps the ship; its surface sits 4 cm above the deck, avoiding
            // two coplanar floor meshes and their flickering seam.
            Slab(dock,"Level HQ bridge",new Vector3(-35.5f, -.8f,-12),new Vector3(11,.8f,3),ShipModelSetup.DeckMaterial(),true);
            for(float x=-31;x>=-39;x-=2)
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
            var hinge=Group(dock,"Lifting HQ gangway",new Vector3(-41,0,-12));
            hinge.AddComponent<DockGangway>();
            Slab(hinge,"Gangway leaf",new Vector3(-1.6f,-.12f,0),new Vector3(3.2f,.16f,3),ShipModelSetup.DeckMaterial(),true);
            foreach(float z in new[]{-1.42f,1.42f})
            {
                Slab(hinge,"Gangway edge",new Vector3(-1.6f,.045f,z),new Vector3(3.2f,.02f,.12f),ShipKitMaterials.Hazard());
                Slab(hinge,"Gangway handrail",new Vector3(-1.6f,1.1f,z),new Vector3(3.2f,.08f,.08f),ShipKitMaterials.Steel());
                foreach(float x in new[]{-.2f,-1.6f,-3f})
                    Slab(hinge,"Gangway upright",new Vector3(x,.04f,z),new Vector3(.08f,1.1f,.08f),ShipKitMaterials.Steel());
                Box(hinge,new Vector3(-1.6f,.6f,z),new Vector3(3.2f,1.2f,.12f));
            }
            foreach(float z in new[]{-13.6f,-10.4f})
                Slab(dock,"Hinge housing",new Vector3(-41,-.2f,z),new Vector3(.45f,.6f,.3f),ShipKitMaterials.Hazard(),true);
            var gate=Group(dock,"HQ Boarding Gate",new Vector3(-40.8f,0,-12));
            var barrier=Slab(gate,"Safety barrier",Vector3.zero,new Vector3(.15f,1.2f,3),ShipKitMaterials.Hazard());
            Box(barrier,new Vector3(0,4,0),new Vector3(.15f,8,3));
            gate.AddComponent<DockBoardingGate>().Configure(barrier);
            var ship=(GameObject)PrefabUtility.InstantiatePrefab(prefab);
            ship.transform.SetParent(dock.transform,false);
            ship.transform.SetPositionAndRotation(ShipPosition,Quaternion.Euler(0,180,0));
            var exclusion=Group(dock,"Fixed bridge exclusion",new Vector3(-37.1f,0,-12));
            BoxCollider bridgeVolume=Box(exclusion,new Vector3(0,4,0),new Vector3(14.2f,9,3));
            bridgeVolume.isTrigger=true;
            ship.GetComponent<ShipParts>().ConfigureDockBridge(bridgeVolume);
            // The entire bridge footprint, including the lifting leaf, stays HQ.
        }

        public static void ShipBoarding(GameObject parent)
        {
            float x=-ShipDeckDressing.W(-6f)+.08f;
            var gate=Group(parent,"Ship Boarding Gate",new Vector3(x,0,-6));
            var barrier=Slab(gate,"Bulwark Boarding Barrier",new Vector3(0,-.12f,0),new Vector3(.22f,1.32f,4),ShipModelSetup.HullMaterial());
            Box(barrier,new Vector3(0,4,0),new Vector3(.22f,8,4));
            gate.AddComponent<DockBoardingGate>().Configure(barrier);
            foreach(float z in new[]{-8f,-4f})
                Slab(parent,"Boarding jamb",new Vector3(x,0,z),new Vector3(.25f,1.3f,.2f),ShipKitMaterials.Hazard(),true);
        }
    }
}
