using SunkCost.Editor.Prototype;
using SunkCost.Shop;
using UnityEngine;

namespace SunkCost.Editor.Look
{
    public static partial class HQGeneratedLayout
    {
        // The shop block of the depot: the boards over the fronts, the four featured
        // stands, the catalogue counter. Split out of Depot (8 October 2026) so the
        // shop's placement is one file; Depot calls it once the delivery point exists.
        internal static void Shop(GameObject depot, ShopDeliveryPoint delivery, Material tank, Material lamp)
        {
            Label(depot,"office",new Vector3(-20,3.1f,9.6f),3);
            Label(depot,"upgrades",new Vector3(-12,2.95f,9.6f),3);
            Label(depot,"gear",new Vector3(0,2.95f,9.6f),5);

            // Existing catalogue IDs and authority remain unchanged.
            Stand(depot,delivery,ShopCatalog.LargeTankId,new Vector3(-13,0,14),tank,PrimitiveType.Capsule,new Vector3(.3f,.65f,.3f));
            Stand(depot,delivery,ShopCatalog.BrightHeadlampId,new Vector3(-9,0,14),lamp,PrimitiveType.Sphere,new Vector3(.35f,.3f,.3f));
            Stand(depot,delivery,ShopCatalog.AirTankId,new Vector3(-3,0,14),tank,PrimitiveType.Capsule,new Vector3(.25f,.5f,.25f));
            Material kit=HQPrototypeBuilder.GetOrCreateMaterial(PatchKitSetup.MaterialPath,PatchKitSetup.KitColour);
            Stand(depot,delivery,ShopCatalog.PatchKitId,new Vector3(1,0,14),kit,PrimitiveType.Cube,new Vector3(.4f,.2f,.3f));

            // One catalogue counter makes new merchandise independent of scene
            // space. The four existing displays are optional featured samples.
            var catalogueCounter=Group(depot,"Equipment catalogue counter",new Vector3(-6,0,14),180);
            Model(catalogueCounter,"Console",Vector3.zero,0,default,true);
            Box(catalogueCounter,new Vector3(0,.7f,0),new Vector3(1.8f,1.4f,.8f));
            catalogueCounter.AddComponent<ShopDisplay>().ConfigureCatalog(delivery);
            // The counter is the shop's front door: the biggest plate in the depot after
            // the fascia, lit, a marked square on the floor (HQ polish, 27 September 2026:
            // a 2.4 m plate over a ship console was lost between the four pedestals).
            // The mast stands behind the console's monitor, not through it.
            Slab(catalogueCounter,"Catalogue sign mast",new Vector3(0,1.2f,-.62f),new Vector3(.1f,2.4f,.1f),ShipKitMaterials.Steel());
            Label(catalogueCounter,"counter",new Vector3(0,2.6f,-.55f),3.4f,.5f,0);
            Label(catalogueCounter,"counter.sub",new Vector3(0,2.16f,-.55f),3.4f,.3f,0);
            Frame(catalogueCounter,Vector3.zero,2.8f,2.4f);
            Lamp(catalogueCounter,new Vector3(0,2.4f,.9f),5,2.5f);
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
