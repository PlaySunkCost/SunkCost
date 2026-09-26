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
                    Slab(depot,"Office glass",new Vector3(x,1.1f,10.02f),new Vector3(3.2f,1.8f,.02f),SunkCost.Sites.DiveSiteBuilder.GetOrCreateGlassMaterial());
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
            Label(depot,"SUNK COST  /  EQUIPMENT DEPOT",new Vector3(-6,3.75f,9.58f),17,.55f);
            Label(depot,"OFFICE",new Vector3(-20,3.1f,9.6f),3);
            Label(depot,"UPGRADES",new Vector3(-12,2.95f,9.6f),3);
            Label(depot,"GEAR & SUPPLIES",new Vector3(0,2.95f,9.6f),5);

            // Rear displays, two clear lanes between the fronts and the merchandise.
            for(float x=-14;x<=6;x+=2)
            {
                Model(depot,"DisplayPanel",new Vector3(x,1,17.5f));
                Model(depot,"Shelf",new Vector3(x,.9f,17.2f));
                Model(depot,"Shelf",new Vector3(x,2f,17.2f));
                Model(depot,"DisplayHook",new Vector3(x-.25f,2.7f,17.25f));
            }
            foreach(float x in new[]{-13.5f,-11.5f,1.5f,3.5f}) Model(depot,"TankCradle",new Vector3(x,1.1f,17.1f));
            Model(depot,"Toolbox",new Vector3(-7,1,17.05f),0,default,true);
            Model(depot,"Toolbox",new Vector3(5,1,17.05f),0,default,true);
            foreach(float x in new[]{-21f,-19f})
            {
                var cabinet=Model(depot,"UtilityCabinet",new Vector3(x,0,17));
                Box(cabinet,new Vector3(0,1,0),new Vector3(1,2,.6f));
            }
            Model(depot,"Table",new Vector3(-21,0,13),0,default,true);
            Box(depot,new Vector3(-21,.4f,13),new Vector3(1.8f,.8f,1));
            Model(depot,"Couch",new Vector3(-22,0,16),0,default,true);
            Box(depot,new Vector3(-22,.5f,16),new Vector3(2.8f,1,1));
            Model(depot,"TvCabinet",new Vector3(-22,1,10.6f),180,new Vector3(.5f,.5f,.5f),true);
            // Wire cages decorate the end of the depot without closing the aisle.
            for(float z=14;z<=17;z++) Model(depot,"CagePanel",new Vector3(7.5f,0,z),90);

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
            Slab(root,"Pickup sign support",new Vector3(10,2.9f,15),new Vector3(.12f,.45f,.12f),ShipKitMaterials.Steel());
            Label(root,"PICKUP",new Vector3(10,3.2f,15),2);
            Lamp(root,new Vector3(10,3,14),7,3);

            // Existing catalogue IDs and authority remain unchanged.
            Stand(depot,delivery,ShopCatalog.LargeTankId,new Vector3(-13,0,14),tank,PrimitiveType.Capsule,new Vector3(.3f,.65f,.3f));
            Stand(depot,delivery,ShopCatalog.BrightHeadlampId,new Vector3(-9,0,14),lamp,PrimitiveType.Sphere,new Vector3(.35f,.3f,.3f));
            Stand(depot,delivery,ShopCatalog.AirTankId,new Vector3(-3,0,14),tank,PrimitiveType.Capsule,new Vector3(.25f,.5f,.25f));
            Material kit=HQPrototypeBuilder.GetOrCreateMaterial(PatchKitSetup.MaterialPath,PatchKitSetup.KitColour);
            Stand(depot,delivery,ShopCatalog.PatchKitId,new Vector3(1,0,14),kit,PrimitiveType.Cube,new Vector3(.4f,.2f,.3f));
            foreach(float x in new[]{-13f,-9f,-3f,1f})
            {
                var counter=Model(depot,"Counter",new Vector3(x,0,16));
                Box(counter,new Vector3(0,.5f,0),new Vector3(2,1,.8f));
            }

            // One catalogue counter makes new merchandise independent of scene
            // space. The four existing displays are optional featured samples.
            var catalogueCounter=Group(depot,"Equipment catalogue counter",new Vector3(-6,0,14),180);
            Model(catalogueCounter,"Console",Vector3.zero,0,default,true);
            Box(catalogueCounter,new Vector3(0,.7f,0),new Vector3(1.8f,1.4f,.8f));
            catalogueCounter.AddComponent<ShopDisplay>().ConfigureCatalog(delivery);
            Slab(catalogueCounter,"Catalogue sign mast",new Vector3(0,1,-.1f),new Vector3(.1f,1.5f,.1f),ShipKitMaterials.Steel());
            Label(catalogueCounter,"BROWSE ALL EQUIPMENT",new Vector3(0,2.1f,0),2.4f,.4f,0);
            Lamp(catalogueCounter,new Vector3(0,2,.8f),4,1.5f);

            var intake=Group(root,"Intake and quota");
            Model(intake,"IntakeHopper",new Vector3(20,0,15));
            Box(intake,new Vector3(20,.55f,15),new Vector3(2,1.1f,1.5f));
            Model(intake,"Console",new Vector3(16,0,15),180,default,true);
            Box(intake,new Vector3(16,.55f,15),new Vector3(1.2f,1.1f,.8f));
            Slab(intake,"Quota display mount",new Vector3(16,1,15),new Vector3(.18f,.8f,.18f),ShipKitMaterials.Steel());
            HQPlatformBuilder.QuotaBoard(intake,new Vector3(16,1.7f,14.7f));
            foreach(float x in new[]{15.7f,20.3f}) Slab(intake,"Intake sign post",new Vector3(x,0,16.6f),new Vector3(.14f,2.9f,.14f),ShipKitMaterials.Steel(),true);
            Label(intake,"INTAKE / SHIP STORAGE",new Vector3(18,2.7f,16.5f),5);
            Frame(intake,new Vector3(18,0,15),8,5);
            Lamp(intake,new Vector3(18,3.5f,15),8,3);
            // Quota moves to the former arrival pad; face inward from the south.
            intake.transform.SetPositionAndRotation(new Vector3(7,0,1),Quaternion.Euler(0,180,0));
        }

        private static void Stand(GameObject root,ShopDeliveryPoint delivery,string id,Vector3 at,Material material,PrimitiveType shape,Vector3 size)
        {
            var stand=Group(root,"Shop Stand "+id,at,180);
            Model(stand,"Pedestal",Vector3.zero);
            Box(stand,new Vector3(0,.4f,0),new Vector3(1,.8f,1));
            var display=GameObject.CreatePrimitive(shape);display.name="Display";
            display.transform.SetParent(stand.transform,false);display.transform.localPosition=new Vector3(0,.83f+size.y/2,0);
            display.transform.localScale=shape==PrimitiveType.Capsule?new Vector3(size.x,size.y/2,size.z):size;
            display.GetComponent<Renderer>().sharedMaterial=material;
            // Give the four merchandise silhouettes readable details instead of
            // leaving bare prototype primitives on otherwise finished pedestals.
            if(shape==PrimitiveType.Capsule)
            {
                foreach(float y in new[]{.83f+size.y*.25f,.83f+size.y*.75f})
                    Slab(stand,"Tank strap",new Vector3(0,y,-size.z/2),new Vector3(size.x,.055f,.035f),ShipKitMaterials.Steel());
                Slab(stand,"Tank valve",new Vector3(0,.83f+size.y,0),new Vector3(.07f,.09f,.07f),ShipKitMaterials.Steel());
                Slab(stand,"Valve handle",new Vector3(0,.90f+size.y,0),new Vector3(.15f,.035f,.045f),ShipKitMaterials.Hazard());
            }
            else if(shape==PrimitiveType.Sphere)
            {
                Slab(stand,"Headlamp housing",new Vector3(0,.83f,-.06f),new Vector3(.38f,.28f,.15f),ShipKitMaterials.Steel());
                Slab(stand,"Headlamp lens",new Vector3(0,.91f,.15f),new Vector3(.20f,.12f,.02f),LookMaterials.ScreenTeal());
            }
            else
            {
                Slab(stand,"Kit clasp",new Vector3(0,.84f,.16f),new Vector3(.045f,.12f,.02f),ShipKitMaterials.Steel());
                Slab(stand,"Kit handle",new Vector3(0,1.04f,0),new Vector3(.16f,.025f,.03f),ShipKitMaterials.Steel());
            }
            // PlayerHudUI already shows this catalogue item's name, price and
            // purchase status while aimed at. No permanent floating item labels.
            stand.AddComponent<ShopDisplay>().Configure(id,null,delivery);
        }
    }
}
