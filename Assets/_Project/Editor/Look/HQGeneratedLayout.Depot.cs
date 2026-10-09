using SunkCost.Editor.Prototype;
using SunkCost.Shop;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Look
{
    public static partial class HQGeneratedLayout
    {
        public static void Depot(GameObject root, Material tank, Material lamp)
        {
            var depot = Group(root, HQPrototypeBuilder.ShopRoomName);
            // One continuous building, 32 x 8 m. The front has six open bays;
            // the office occupies the west 8 m without splitting the shop aisle.
            for (float x = -22; x <= 6; x += 4)
            {
                var back = Model(depot,"WallPanel",new Vector3(x,0,17.8f));
                Box(back,new Vector3(0,2,0),new Vector3(4,4,.2f));
                foreach(float z in new[]{12f,16f})
                {
                    var roof = Model(depot,"RoofCassette",new Vector3(x,4,z));
                    Box(roof,new Vector3(0,.15f,0),new Vector3(4,.3f,4));
                }
                Model(depot,"Fascia",new Vector3(x,3.35f,9.85f));
                if(x>=-14)
                {
                    var portal = Model(depot,"Portal",new Vector3(x,0,10));
                    Box(portal,new Vector3(-1.85f,1.8f,0),new Vector3(.3f,3.6f,.4f));
                    Box(portal,new Vector3(1.85f,1.8f,0),new Vector3(.3f,3.6f,.4f));
                    Box(portal,new Vector3(0,3.8f,0),new Vector3(4,.4f,.4f));
                }
                else if(x==-22)
                {
                    var window=Model(depot,"Window",new Vector3(x,0,10));
                    // Window keeps its opening; transparent glass supplies the barrier.
                    Box(window,new Vector3(0,2,0),new Vector3(4,4,.12f));
                    Slab(depot,"Office glass",new Vector3(x,1.0f,10.02f),new Vector3(3.2f,1.8f,.02f),SunkCost.Sites.DiveSiteBuilder.GetOrCreateGlassMaterial());
                }
                else
                {
                    var door=Model(depot,"Portal",new Vector3(x,0,10));
                    Box(door,new Vector3(-1.85f,2,0),new Vector3(.3f,4,.4f));
                    Box(door,new Vector3(1.85f,2,0),new Vector3(.3f,4,.4f));
                    Box(door,new Vector3(0,3.8f,0),new Vector3(4,.4f,.4f));
                }
                Model(depot,"DeckLamp",new Vector3(x,3.6f,10),180,new Vector3(.8f,.8f,.8f),true);
                Lamp(depot,new Vector3(x,3.4f,13),6,3);
            }
            foreach(float x in new[]{-24f,8f}) foreach(float z in new[]{12f,16f})
            {
                var wall=Model(depot,"WallPanel",new Vector3(x,0,z),90);
                Box(wall,new Vector3(0,2,0),new Vector3(4,4,.2f));
            }
            for(float x=-24;x<=8;x+=4) Model(depot,"Pillar",new Vector3(x,0,10));
            // Readable, editable boards; generated geometry carries no baked prices.
            Label(depot,"depot.title",new Vector3(-6,3.75f,9.58f),17,.55f);

            // Rear displays, two clear lanes between the fronts and the merchandise.
            for(float x=-14;x<=6;x+=2)
            {
                Model(depot,"DisplayPanel",new Vector3(x,1,17.5f));
                Model(depot,"Shelf",new Vector3(x,.9f,17.2f));
                Model(depot,"Shelf",new Vector3(x,2f,17.2f));
                Model(depot,"DisplayHook",new Vector3(x-.25f,2.7f,17.25f));
            }
            // Stock on the shelves: a depot that has something in it, not bare panels (HQ polish, 27 September 2026).
            // Every piece stands on a shelf top (lower .98, upper 2.08) at a shelf's x (even x: the
            // shelves are 1 m wide every 2 m), not in the gaps (Dan, 8 October 2026: the two orange
            // toolboxes hung in mid-air between shelves and the cradles straddled the shelf ends).
            foreach(float x in new[]{-12f,-4f,4f}) Model(depot,"Toolbox",new Vector3(x,2.08f,17.2f),0,new Vector3(.8f,.8f,.8f),true);
            foreach(float x in new[]{-10f,0f,6f}) Model(depot,"Crate",new Vector3(x,2.08f,17.2f),0,new Vector3(.45f,.45f,.45f),true);
            foreach(float x in new[]{-14f,-8f,2f,6f}) Model(depot,"TankCradle",new Vector3(x,.98f,17.2f)); // 1 m tall: top 1.98, under the upper shelf (2.0)
            foreach(float x in new[]{-6f,4f}) Model(depot,"Toolbox",new Vector3(x,.98f,17.2f),0,new Vector3(.8f,.8f,.8f),true);
            foreach(float x in new[]{-21f,-19f})
            {
                var cabinet=Model(depot,"UtilityCabinet",new Vector3(x,0,17));
                Box(cabinet,new Vector3(0,1,0),new Vector3(1,2,.6f));
            }
            Model(depot,"Table",new Vector3(-21,0,13),0,default,true);
            Box(depot,new Vector3(-21,.4f,13),new Vector3(1.8f,.8f,1));
            // The office couch faces south into the room and its window (Dan, 8 October 2026:
            // at yaw 0 it faced the back wall beside the lockers). The TV cabinet that stood in
            // the window at (-22, 1, 10.6) is gone: from the room it was a blank brown board on legs.
            Model(depot,"Couch",new Vector3(-22,0,16),180,default,true);
            Box(depot,new Vector3(-22,.5f,16),new Vector3(2.8f,1,1));
            // Wire cages decorate the end of the depot without closing the aisle.
            for(float z=14;z<=17;z++) Model(depot,"CagePanel",new Vector3(7.5f,0,z),90);
            // The east end wall is the first face of the depot the arrivals walk toward:
            // it says what the building is (HQ polish, 27 September 2026: it was blank).
            // On the wall's south half, high: the pickup chute stands before the north half.
            Label(depot,"depot.end",new Vector3(8.22f,3.3f,12.2f),4.4f,.6f,90);
            Label(depot,"depot.end.sub",new Vector3(8.22f,2.82f,12.2f),4.4f,.34f,90);
            Lamp(depot,new Vector3(9.2f,3.5f,12.2f),6,2.5f);

            var chuteArt=Model(root,"PickupChute",new Vector3(10,0,15));
            // Use a mesh collision surface for the chute so purchases slide out.
            foreach(var mf in chuteArt.GetComponentsInChildren<MeshFilter>())
                mf.gameObject.AddComponent<MeshCollider>().sharedMesh=mf.sharedMesh;
            var marker=Group(root,ShopDeliveryPoint.DefaultName,HQPlatformBuilder.PickupChute);
            var delivery=marker.AddComponent<ShopDeliveryPoint>();
            var deliverySettings=new SerializedObject(delivery);
            deliverySettings.FindProperty("scatterRadius").floatValue=.15f;
            deliverySettings.ApplyModifiedPropertiesWithoutUndo();
            Frame(root,new Vector3(10,0,12),4,4);
            // The sign clears the chute's lip from the deck (HQ polish, 27 September 2026:
            // at 3.2 m its lower half hid behind the discharge from six metres away).
            Slab(root,"Pickup sign support",new Vector3(10,3.3f,15),new Vector3(.12f,1,.12f),ShipKitMaterials.Steel());
            Label(root,"pickup",new Vector3(10,4.05f,15),2.6f,.5f);
            Label(root,"pickup.sub",new Vector3(10,3.62f,15),2.6f,.3f);
            Lamp(root,new Vector3(10,3.6f,14),7,3);

            // The shop block (the boards over the fronts, the stands, the catalogue counter): HQGeneratedLayout.Shop.cs.
            Shop(depot,delivery,tank,lamp);
            foreach(float x in new[]{-13f,-9f,-3f,1f})
            {
                var counter=Model(depot,"Counter",new Vector3(x,0,16));
                Box(counter,new Vector3(0,.5f,0),new Vector3(2,1,.8f));
            }

            var intake=Group(root,"Intake and quota");
            Model(intake,"IntakeHopper",new Vector3(20,0,15));
            Box(intake,new Vector3(20,.55f,15),new Vector3(2,1.1f,1.5f));
            // The quota board is the shared console (Dan, 27 September 2026): 2.38 x 2.17 x 1.20 m
            // on its footing between the intake sign's posts (x 15.7 / 20.3), the hopper to
            // its east, its front toward the aisle; the lever housing on the reader's right.
            HQPlatformBuilder.QuotaConsole(intake,new Vector3(17.2f,0,15),180);
            foreach(float x in new[]{15.7f,20.3f}) Slab(intake,"Intake sign post",new Vector3(x,0,16.6f),new Vector3(.14f,3.2f,.14f),ShipKitMaterials.Steel(),true);
            Label(intake,"intake",new Vector3(18,2.88f,16.5f),5,.5f);
            Label(intake,"intake.sub",new Vector3(18,2.45f,16.5f),5,.3f);
            Frame(intake,new Vector3(18,0,15),8,5);
            Lamp(intake,new Vector3(18,3.5f,15),8,3);
            // Quota moves to the former arrival pad; face inward from the south.
            intake.transform.SetPositionAndRotation(new Vector3(7,0,1),Quaternion.Euler(0,180,0));
        }
    }
}
