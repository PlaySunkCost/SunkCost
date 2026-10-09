using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SunkCost.Audio;
using SunkCost.Player;
using SunkCost.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using H = SunkCost.Editor.Prototype.HQPrototypeTestHooks;

namespace SunkCost.Editor.Prototype
{
    // Real controller input against the authored world, a ball falling through each
    // generated rim, the catalogue on the shop console, and the HQ cleanup rows of
    // 8 October 2026 (the signs, the displays and the board gone, the sofa's facing,
    // every depot prop grounded, the ambience grid, the bed silent at sea).
    // Networking/travel use the existing loop matrix.
    public static class HQArtRuntimeChecks
    {
        private const string Log="Temp/hq-art-matrix.log";
        private const string Captures="Temp/look/hq-consoles-hq-";
        private static readonly Stack<IEnumerator> steps=new();
        // HQ5 is an audit: a prop out of place is written as SOFT-FAIL and the walk goes on, so one
        // run lists every fault; the verdict is still FAIL when any was recorded.
        private static readonly List<string> softFails=new();
        private static Keyboard keyboard;
        private static SunkCost.Shop.ShopItem extraCatalogRow;
        private static InputSettings.EditorInputBehaviorInPlayMode savedEditor;
        private static InputSettings.BackgroundBehavior savedBackground;
        private static HQPlayerController Player => WorldSceneFlow.LocalPlayer();
        private static CrewDayState Day => CrewDayState.Instance;
        private static Scene HQ => WorldScenes.Scene(WorldId.HQ);

        public static void RunAsHost()
        {
            if(!EditorApplication.isPlaying || Player==null)throw new InvalidOperationException("Host first.");
            File.WriteAllText(Log,"HQ art runtime checks "+DateTime.Now+"\n");
            savedEditor=InputSystem.settings.editorInputBehaviorInPlayMode;
            savedBackground=InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            keyboard=InputSystem.AddDevice<Keyboard>();HQPlayerController.KeyboardForChecks=keyboard;HQPlayerController.BypassInputGateForChecks=true;
            softFails.Clear();steps.Clear();steps.Push(Run());EditorApplication.update+=Tick;
        }

        private static void Tick()
        {
            try
            {
                if(!EditorApplication.isPlaying)throw new Exception("Play Mode stopped");
                while(steps.Count>0)
                {
                    var routine=steps.Peek();
                    if(!routine.MoveNext()){steps.Pop();continue;}
                    if(routine.Current is IEnumerator nested){steps.Push(nested);continue;}
                    return;
                }
                Finish(softFails.Count==0?"MATRIX_PASS":"FAIL: "+softFails.Count+" known gap(s): "+string.Join(" | ",softFails));
            }
            catch(Exception e){Finish("FAIL: "+e);}
        }

        private static void Finish(string result)
        {
            if(extraCatalogRow!=null)
                ((List<SunkCost.Shop.ShopItem>)SunkCost.Shop.ShopCatalog.Resolve().Items).Remove(extraCatalogRow);
            extraCatalogRow=null;
            File.AppendAllText(Log,result+"\n");EditorApplication.update-=Tick;steps.Clear();
            HQPlayerController.KeyboardForChecks=null;HQPlayerController.BypassInputGateForChecks=false;
            if(keyboard!=null)InputSystem.RemoveDevice(keyboard);keyboard=null;
            InputSystem.settings.editorInputBehaviorInPlayMode=savedEditor;InputSystem.settings.backgroundBehavior=savedBackground;
        }
        private static void Check(bool pass,string text)
        {
            if(!pass)throw new Exception(text);File.AppendAllText(Log,"PASS "+text+"\n");
        }
        private static void Soft(bool pass,string text)
        {
            if(pass){File.AppendAllText(Log,"PASS "+text+"\n");return;}
            softFails.Add(text);File.AppendAllText(Log,"SOFT-FAIL "+text+"\n");
        }
        private static void Say(string text)=>File.AppendAllText(Log,"  · "+text+"\n");
        private static void Heading(string text)=>File.AppendAllText(Log,"\n== "+text+"\n");
        private static IEnumerator Wait(float seconds)
        {
            float until=Time.unscaledTime+seconds;while(Time.unscaledTime<until)yield return null;
        }
        private static IEnumerator Expect(Func<bool> condition,float seconds,Func<string> label)
        {
            float deadline=Time.unscaledTime+seconds;
            while(Time.unscaledTime<deadline && !condition())yield return null;
            Check(condition(),label());
        }
        private static void Keys(params Key[] pressed)=>InputSystem.QueueStateEvent(keyboard,new KeyboardState(pressed));
        private static IEnumerator Press(Key key,float holdSeconds=.05f)
        {
            Keys(key);yield return Wait(holdSeconds);Keys();yield return null;
        }
        private static IEnumerator Walk(Vector3 from,Vector3 to)
        {
            Player.TeleportLocal(from,Quaternion.LookRotation(to-from).eulerAngles.y);
            yield return Wait(.2f);
            float deadline=Time.unscaledTime+15f;
            while(Time.unscaledTime<deadline && Vector3.Distance(Player.transform.position,to)>.4f)
            {
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W));
                if(Player.transform.position.y<-.2f)throw new Exception("Fell below the bridge deck at "+Player.transform.position);
                yield return Wait(.1f);
            }
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());
            Check(Vector3.Distance(Player.transform.position,to)<.5f,$"walk {from} -> {to}, ended {Player.transform.position}");
            yield return Wait(.15f);
        }

        // ---- the HQ scene by name -------------------------------------------------------
        // The docked ship lives in the HQ scene too (its own Couch, Toolbox, TvCabinet at
        // x ≈ -53), so props are found nearest a spot, and the board under the depot group.
        // Scanned once a frame (the prop row asks sixty times in one frame).
        private static readonly List<Transform> scanned=new();
        private static readonly List<Renderer> scannedRenderers=new();
        private static int scannedFrame=-1;
        private static IEnumerable<Transform> HQTransforms()
        {
            if(scannedFrame!=Time.frameCount)
            {
                scanned.Clear();scannedRenderers.Clear();scannedFrame=Time.frameCount;
                foreach(GameObject root in HQ.GetRootGameObjects())
                    foreach(Transform t in root.GetComponentsInChildren<Transform>(true))
                    {
                        scanned.Add(t);
                        var r=t.GetComponent<Renderer>();
                        if(r!=null && (r is MeshRenderer || r is SkinnedMeshRenderer) && r.enabled)scannedRenderers.Add(r);
                    }
            }
            return scanned;
        }
        private static Transform Named(string name)=>HQTransforms().FirstOrDefault(t=>t.name==name);
        private static Transform NearestNamed(string name,Vector3 spot,float within)
        {
            Transform best=null;float bestD=within;
            foreach(Transform t in HQTransforms())
            {
                if(t.name!=name)continue;
                float d=Vector3.Distance(new Vector3(t.position.x,0,t.position.z),new Vector3(spot.x,0,spot.z));
                if(d<bestD){best=t;bestD=d;}
            }
            return best;
        }
        private static Transform DepotGroup()=>Named(HQPrototypeBuilder.ShopRoomName);
        // The class names are compared as strings so this file compiles whether or not the
        // dead classes (ShopDisplay, ShopBrowserUI, ColourPanel) are still in the project.
        private static int CountOfType(string typeName)
        {
            int n=0;
            foreach(var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                if(mb!=null && mb.GetType().Name==typeName)n++;
            return n;
        }
        private static IEnumerable<Renderer> HQRenderers(){HQTransforms();return scannedRenderers;}
        private static bool TryBounds(Transform prop,out Bounds b)
        {
            b=default;bool any=false;
            foreach(var r in prop.GetComponentsInChildren<Renderer>(true))
            {
                if(!(r is MeshRenderer || r is SkinnedMeshRenderer))continue;
                if(!any){b=r.bounds;any=true;}else b.Encapsulate(r.bounds);
            }
            return any;
        }
        // The surface under a prop: the highest of (a) a ray down from the bounds' bottom
        // centre + 5 cm (triggers ignored, the prop's own colliders skipped) and (b) the top
        // of any other renderer's bounds under that point that is not above the bottom by
        // more than 3 cm (the HQ kit shelves and pallets carry no colliders).
        private static float SupportBelow(Transform prop,Bounds b,out string how)
        {
            Vector3 from=new(b.center.x,b.min.y+.05f,b.center.z);
            float support=float.NegativeInfinity;how="nothing";
            foreach(var hit in Physics.RaycastAll(from,Vector3.down,6f,~0,QueryTriggerInteraction.Ignore))
            {
                if(hit.collider.transform.IsChildOf(prop))continue;
                if(hit.point.y>support){support=hit.point.y;how="collider "+hit.collider.name;}
            }
            foreach(var r in HQRenderers())
            {
                if(r.transform.IsChildOf(prop))continue;
                Bounds o=r.bounds;
                if(o.max.y>b.min.y+.03f || o.max.y<=support)continue;
                if(from.x<o.min.x || from.x>o.max.x || from.z<o.min.z || from.z>o.max.z)continue;
                support=o.max.y;how="top of "+r.name;
            }
            return support;
        }
        // How deep a prop's bounds run into a wall (WallPanel / Window renderers, the Portal's
        // jamb and lintel boxes): the smallest axis overlap of the two boxes, 0 when apart.
        private static float WallPenetration(Transform prop,Bounds b,out string wall)
        {
            float worst=0f;wall="none";
            foreach(Transform t in HQTransforms())
            {
                if(t.IsChildOf(prop) || prop.IsChildOf(t))continue;
                if(t.name=="WallPanel" || t.name=="Window")
                {
                    if(!TryBounds(t,out Bounds w))continue;
                    float p=Overlap(b,w);if(p>worst){worst=p;wall=t.name+" at "+t.position;}
                }
                else if(t.name=="Portal")
                    foreach(var c in t.GetComponentsInChildren<BoxCollider>())
                    {
                        float p=Overlap(b,c.bounds);if(p>worst){worst=p;wall="Portal box at "+t.position;}
                    }
            }
            return worst;
        }
        private static float Overlap(Bounds a,Bounds b)
        {
            float x=Mathf.Min(a.max.x,b.max.x)-Mathf.Max(a.min.x,b.min.x);
            float y=Mathf.Min(a.max.y,b.max.y)-Mathf.Max(a.min.y,b.min.y);
            float z=Mathf.Min(a.max.z,b.max.z)-Mathf.Max(a.min.z,b.min.z);
            return (x<=0||y<=0||z<=0)?0f:Mathf.Min(x,Mathf.Min(y,z));
        }

        // ---- the shop console ------------------------------------------------------------
        private static ConsoleRig ShopRig()=>ConsoleRig.InScene(HQ,ConsoleKind.Shop);
        private static Vector3 StandSpot(ConsoleRig rig,float side)
        {
            Vector3 front=rig.transform.rotation*Vector3.forward;front.y=0;front.Normalize();
            Vector3 right=Vector3.Cross(Vector3.up,front);
            Vector3 at=rig.transform.position+front*1.6f+right*side;at.y=.05f;return at;
        }
        private static IEnumerator Aim(ConsoleRig rig,string objectName,ConsoleControlKind kind,int index)
        {
            float deadline=Time.unscaledTime+4f;int tries=0;
            while(Time.unscaledTime<deadline)
            {
                ConsoleControl c=Player.CurrentConsoleControl;
                if(c!=null && c.Console==ConsoleKind.Shop && c.Kind==kind && c.Index==index)break;
                if(tries++%45==0)Player.TeleportLocal(StandSpot(rig,0),rig.transform.eulerAngles.y+180f);
                H.ClientLookAtNamed(objectName);
                yield return null;
            }
            ConsoleControl aimed=Player.CurrentConsoleControl;
            Check(aimed!=null && aimed.Console==ConsoleKind.Shop && aimed.Kind==kind && aimed.Index==index,
                "the dot is on "+objectName+" (aim "+(aimed==null?"none":aimed.Console+"/"+aimed.Kind+"/"+aimed.Index)+")");
        }

        private static IEnumerator Run()
        {
            yield return Wait(.5f);
            foreach(var gate in UnityEngine.Object.FindObjectsByType<DockBoardingGate>(FindObjectsSortMode.None))Check(!gate.Closed,"gate opens at HQ: "+gate.name);
            yield return Walk(new Vector3(-28,.05f,-12),new Vector3(-47,.05f,-12));
            Check(ShipParts.InWorld(WorldId.HQ).IsSafelyAboard(Player.transform.position),"walking over the lip reaches the ship's safe deck");
            yield return Walk(new Vector3(-47,.05f,-12),new Vector3(-28,.05f,-12));
            Check(!ShipParts.InWorld(WorldId.HQ).IsAboard(Player.transform.position),"back on HQ is not aboard");
            yield return Walk(new Vector3(-16,.05f,11.7f),new Vector3(6,.05f,11.7f));
            yield return Walk(new Vector3(22,.05f,4),new Vector3(24,.05f,4));
            yield return Walk(new Vector3(18.75f,.05f,14.5f),new Vector3(20,.05f,9));
            yield return Catalogue();
            Player.SetPitchForChecks(0);
            H.ClientMoveLocalPlayerTo(new Vector3(-8,0,-5));
            foreach(string name in new[]{"Hoop W","Hoop E"})
            {
                Transform trigger=GameObject.Find(name).transform.Find("Score Trigger");
                int before=CrewDayState.Instance.Baskets;
                H.Item("Basketball").ServerDropAt(trigger.position+Vector3.up*.85f);
                yield return Wait(2f);
                Check(CrewDayState.Instance.Baskets==before+1,name+" scores through the actual generated rim");
            }
            yield return SignsGone();
            yield return DisplaysGone();
            yield return BoardGone();
            yield return SofaFacesTheRoom();
            yield return PropsGrounded();
            yield return AmbienceGrid();
            yield return Spots();
            H.ClientMoveLocalPlayerTo(SunkCost.Editor.Look.HQGeneratedLayout.Arrival+Vector3.up*.05f);
            Say("capture "+H.CaptureFrom(new Vector3(18.75f,1.65f,14.5f),new Vector3(18,1.4f,10.8f),"Temp/look/hq-runtime-arrival.png"));
            yield return BedSilentAtSea();
        }

        // The catalogue is the shop console: aim at a row and press E. A fifth row added in
        // memory shows that the catalogue, not the scene, decides what is for sale.
        private static IEnumerator Catalogue()
        {
            Heading("HQ0 — the catalogue on the shop console");
            ConsoleRig rig=ShopRig();
            Check(rig!=null && rig.name==ConsoleRig.CatalogueRootName,"the shop console stands in the HQ scene: "+(rig==null?"none":rig.name+" at "+rig.transform.position));
            Player.TeleportLocal(StandSpot(rig,0),rig.transform.eulerAngles.y+180f);
            yield return Wait(.2f);
            var hud=Player.GetComponent<PlayerHudUI>();
            var catalog=SunkCost.Shop.ShopCatalog.Resolve();
            var day=Day;
            Check(day.ShopPage==0,"the console shows page 1 (ShopPage "+day.ShopPage+")");
            yield return Aim(rig,ConsoleRig.RowNamePrefix+"0",ConsoleControlKind.ShopRow,0);
            yield return Wait(.2f);
            Check(hud.PromptText.Contains(catalog.Items[0].Name),"aim at row 0 names the first item: '"+hud.PromptText+"'");
            Check(!SunkCost.Net.SessionInputGate.ShopOpen && CountOfType("ShopBrowserUI")==0,"no HUD browser exists or is open");
            int originalCount=catalog.Items.Count;
            extraCatalogRow=new SunkCost.Shop.ShopItem{Id="hq-catalogue-proof",Name="Catalogue expansion proof",Price=11,Prefab=catalog.Find(SunkCost.Shop.ShopCatalog.AirTankId).Prefab};
            ((List<SunkCost.Shop.ShopItem>)catalog.Items).Add(extraCatalogRow);
            int proofPage=(originalCount)/ShopConsoleRules.RowsPerPage, proofRow=originalCount%ShopConsoleRules.RowsPerPage;
            day.ServerSetShopPageForChecks(proofPage);
            yield return Expect(()=>day.ShopPage==proofPage,2f,()=>"the page set for the proof row: ShopPage "+day.ShopPage+" (wanted "+proofPage+")");
            yield return Wait(.4f);
            yield return Aim(rig,ConsoleRig.RowNamePrefix+proofRow,ConsoleControlKind.ShopRow,proofRow);
            yield return Wait(.3f);
            Check(hud.PromptText.Contains(extraCatalogRow.Name),"fifth item appears on the console without a scene rebuild: '"+hud.PromptText+"'");
            int oldBalance=day.Balance;
            day.ServerSetBalanceForChecks(100);
            yield return Wait(.3f);
            Say("capture "+H.CaptureLocalCamera("Temp/look/hq-shop-catalogue.png"));
            yield return Press(Key.E);
            yield return Expect(()=>day.Balance==89,3f,()=>"server charges the catalogue price for the new row: balance "+day.Balance+(Player.Upgrades==null?"":" refusal='"+Player.Upgrades.Refusal+"'"));
            SunkCost.Interaction.CarryableItem delivered=null;
            yield return Expect(()=>{delivered=SunkCost.Interaction.CarryableItem.Spawned.FirstOrDefault(i=>i!=null && i.name.StartsWith(extraCatalogRow.Name));return delivered!=null;},3f,()=>"the catalogue purchase lands through the shared delivery chute");
            var chute=SunkCost.Shop.ShopDeliveryPoint.Nearest(rig.transform.position,HQ);
            Say("delivered at "+delivered.transform.position+(chute==null?"":" (chute drop "+chute.DropPosition+")"));
            Check(chute!=null && Vector3.Distance(new Vector3(delivered.transform.position.x,0,delivered.transform.position.z),new Vector3(chute.DropPosition.x,0,chute.DropPosition.z))<3f,"the delivery is at the chute, not at the console");
            Check(!SunkCost.Net.SessionInputGate.ShopOpen,"E on a row opened no HUD window");
            FishNet.InstanceFinder.ServerManager.Despawn(delivered.gameObject);
            day.ServerSetBalanceForChecks(oldBalance);
            ((List<SunkCost.Shop.ShopItem>)catalog.Items).Remove(extraCatalogRow);extraCatalogRow=null;
            day.ServerSetShopPageForChecks(0);
            yield return Expect(()=>day.ShopPage==0,2f,()=>"page back to 1 (ShopPage "+day.ShopPage+")");
        }

        private static IEnumerator SignsGone()
        {
            Heading("HQ1 — the signs OFFICE / UPGRADES / GEAR & SUPPLIES and the hanging catalogue sign are gone");
            // The colour panel's own removal (its mast, its sign, the ColourPanel component) is
            // console-colour's K0 row, so this job does not depend on that commit.
            foreach(string name in new[]{"office Sign","upgrades Sign","gear Sign","counter Sign","counter.sub Sign","Catalogue sign mast"})
                Check(Named(name)==null,"no object named '"+name+"' in the HQ scene");
            foreach(string name in new[]{"depot.title Sign","depot.end Sign","pickup Sign","intake Sign","arrival Sign","cargo Sign"})
                Check(Named(name)!=null,"'"+name+"' still stands");
            yield break;
        }

        private static IEnumerator DisplaysGone()
        {
            Heading("HQ2 — the item displays are gone; the shop console is the only shop");
            Check(CountOfType("ShopDisplay")==0,"no ShopDisplay component anywhere");
            var stands=HQTransforms().Where(t=>t.name.StartsWith("Shop Stand")).Select(t=>t.name).ToList();
            Check(stands.Count==0,"no 'Shop Stand *' objects ("+string.Join(", ",stands)+")");
            Check(Named("Equipment catalogue counter")==null,"no 'Equipment catalogue counter'");
            int shopRigs=HQTransforms().Count(t=>{var r=t.GetComponent<ConsoleRig>();return r!=null && r.Kind==ConsoleKind.Shop;});
            Check(shopRigs==1,"exactly one Shop console rig ("+shopRigs+")");
            yield break;
        }

        private static IEnumerator BoardGone()
        {
            Heading("HQ3 — the blank brown board (the office TvCabinet) is gone");
            Transform depot=DepotGroup();
            Check(depot!=null,"the depot group '"+HQPrototypeBuilder.ShopRoomName+"' exists");
            var boards=depot.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("TvCabinet")).Select(t=>t.name+" at "+t.position).ToList();
            Check(boards.Count==0,"no TvCabinet under the depot ("+string.Join(", ",boards)+")");
            Bounds window=new(new Vector3(-22,1.75f,10.65f),new Vector3(4,2.5f,.7f));
            string[] shell={"Window","Office glass","Pillar","Fascia","Portal","RoofCassette","WallPanel","DeckLamp","Brace","Girder"};
            var inside=HQRenderers().Where(r=>window.Intersects(r.bounds) && !shell.Any(s=>r.name.StartsWith(s)) && !r.transform.IsChildOf(ShipParts.InWorld(WorldId.HQ).transform)).Select(r=>r.name+" at "+r.transform.position).ToList();
            Check(inside.Count==0,"nothing but the shell in the office window volume ("+string.Join(", ",inside)+")");
            yield break;
        }

        private static IEnumerator SofaFacesTheRoom()
        {
            Heading("HQ4 — the office sofa faces the room");
            Transform couch=NearestNamed("Couch",new Vector3(-22,0,16),1.5f);
            Check(couch!=null && couch.IsChildOf(DepotGroup()),"the office couch stands near (-22, 0, 16)"+(couch==null?"":" at "+couch.position));
            Vector3 fwd=couch.forward;fwd.y=0;fwd.Normalize();
            Say($"couch forward {fwd} yaw {couch.eulerAngles.y:0}");
            Check(Vector3.Dot(fwd,Vector3.back)>.85f,$"the couch faces south into the room (dot with -Z {Vector3.Dot(fwd,Vector3.back):0.00})");
            // Nothing within 1.5 m along its facing, by physics and by the walls' renderers.
            Vector3 from=couch.position+Vector3.up*.6f;
            bool clear=true;string blocker="";
            foreach(var hit in Physics.RaycastAll(from,fwd,1.5f,~0,QueryTriggerInteraction.Ignore))
                if(!hit.collider.transform.IsChildOf(couch)){clear=false;blocker=hit.collider.name+" at "+hit.distance.ToString("0.00")+" m";break;}
            foreach(Transform t in HQTransforms())
            {
                if(!clear)break;
                if(t.name!="WallPanel" && t.name!="Window" && !t.name.StartsWith("UtilityCabinet"))continue;
                if(!TryBounds(t,out Bounds w))continue;
                Ray ray=new(from,fwd);
                if(w.IntersectRay(ray,out float d) && d<1.5f){clear=false;blocker=t.name+" at "+d.ToString("0.00")+" m";}
            }
            Check(clear,"nothing stands within 1.5 m in front of the couch"+(clear?"":" ("+blocker+")"));
            Vector3 toTable=new Vector3(-21,0,13)-couch.position;toTable.y=0;
            float angle=Vector3.Angle(fwd,toTable);
            Check(angle<45f,$"the couch looks toward the table and the window (angle {angle:0}°)");
            Say("capture "+H.CaptureFrom(new Vector3(-20,1.5f,12),new Vector3(-22,.6f,16),Captures+"sofa.png"));
            yield return null;
        }

        // INTERFACES §11.4: each prop by name nearest its spot (within 1.5 m), grounded on the
        // surface under it (3 cm), upright (10°), not into a wall (2 cm), plus the touch tests
        // for the rack wall and the height limits for the cradles and the lamps.
        private sealed class Prop
        {
            public string Name;public Vector3 Spot;public bool Ground=true,Upright=true,Optional;
            public float MaxTopY=float.NaN,WallZ=float.NaN,MountMin=float.NaN,MountMax=float.NaN;public string Touch;public float TouchTol=.02f;
            public Prop(string name,float x,float y,float z){Name=name;Spot=new Vector3(x,y,z);}
        }
        private static IEnumerable<Prop> PropList()
        {
            foreach(float x in new[]{-12f,-4f,4f})yield return new Prop("Toolbox",x,2.08f,17.36f);
            foreach(float x in new[]{-6f,4f})yield return new Prop("Toolbox",x,.98f,17.36f);
            foreach(float x in new[]{-10f,0f,6f})yield return new Prop("Crate",x,2.08f,17.36f);
            foreach(float x in new[]{-14f,-8f,2f,6f})yield return new Prop("TankCradle",x,.98f,17.36f){MaxTopY=2.0f};
            for(float x=-14;x<=6;x+=2){yield return new Prop("Shelf",x,.9f,17.36f){Ground=false,Touch="DisplayPanel"};yield return new Prop("Shelf",x,2f,17.36f){Ground=false,Touch="DisplayPanel"};}
            for(float x=-14;x<=6;x+=2)yield return new Prop("DisplayPanel",x,1,17.64f){Ground=false,WallZ=17.7f};
            for(float x=-14;x<=6;x+=2)yield return new Prop("DisplayHook",x-.25f,2.7f,17.43f){Ground=false,Touch="DisplayPanel"};
            foreach(float x in new[]{-21f,-19f})yield return new Prop("UtilityCabinet",x,0,17.4f){WallZ=17.7f,TouchTol=.05f};
            yield return new Prop("Table",-21,0,13);
            yield return new Prop("Couch",-22,0,16);
            yield return new Prop("Couch",7,0,-11);
            yield return new Prop("Table",7,0,-13);
            foreach(float x in new[]{-16f,-8f,0f})yield return new Prop("Bench",x,0,6);
            foreach(float x in new[]{-13f,-9f,-3f,1f})yield return new Prop("Counter",x,0,16){Optional=true};
            yield return new Prop("Toolbox",22,0,11);
            foreach(var at in new[]{new Vector3(16,.16f,5),new Vector3(26,.16f,9),new Vector3(26,.16f,12)})yield return new Prop("Crate",at.x,at.y,at.z);
            foreach(var at in new[]{new Vector3(26,0,15),new Vector3(27,0,16),new Vector3(28,0,15)})yield return new Prop("Barrel",at.x,at.y,at.z);
            yield return new Prop("CableCoil",26,0,-6);
            yield return new Prop("Bollard",28,0,9);
            foreach(float x in new[]{-26f,4f,27f})yield return new Prop("Lifebuoy",x,1,-17.5f){Ground=false};
            // The depot-front lamps are mounted in the solid Fascia (y 3.35..4.35): the post
            // inside it, the head above its top (props-impl, 9 October 2026, decision 1).
            for(float x=-22;x<=6;x+=4)yield return new Prop("DeckLamp",x,3.6f,10){Ground=false,Upright=false,MountMin=3.35f,MountMax=4.35f,Optional=true};
        }
        private static IEnumerator PropsGrounded()
        {
            Heading("HQ5 — every prop settled on a real surface, right side up, out of the walls");
            var used=new HashSet<Transform>();
            foreach(Prop p in PropList())
            {
                string where=$"{p.Name} near ({p.Spot.x:0.##}, {p.Spot.z:0.##})";
                Transform t=null;float bestD=1.5f;
                foreach(Transform c in HQTransforms())
                {
                    if(c.name!=p.Name || used.Contains(c))continue;
                    float d=Vector3.Distance(new Vector3(c.position.x,0,c.position.z),new Vector3(p.Spot.x,0,p.Spot.z));
                    if(d<bestD){t=c;bestD=d;}
                }
                if(t==null)
                {
                    if(p.Optional){Say(where+": absent (allowed)");continue;}
                    Check(false,where+": no such prop within 1.5 m");
                }
                used.Add(t);
                Check(TryBounds(t,out Bounds b),where+": has renderers");
                var notes=new List<string>{$"at {t.position} bounds y {b.min.y:0.000}..{b.max.y:0.000} z {b.min.z:0.000}..{b.max.z:0.000}"};
                if(p.Ground)
                {
                    float support=SupportBelow(t,b,out string how);
                    float gap=b.min.y-support;
                    notes.Add($"bottom {gap*100:+0.0;-0.0} cm from {how} ({support:0.000})");
                    Soft(!float.IsInfinity(support) && Mathf.Abs(gap)<=.03f,where+": grounded ("+notes.Last()+")");
                }
                if(p.Upright)
                {
                    float tilt=Vector3.Angle(t.up,Vector3.up);
                    Soft(tilt<10f,where+$": upright (tilt {tilt:0.0}°)");
                }
                if(float.IsNaN(p.MountMin)) // a mounted lamp sits inside the fascia by design
                {
                    float pen=WallPenetration(t,b,out string wall);
                    Soft(pen<=.02f,where+$": not into a wall (penetration {pen*100:0.0} cm, {wall})");
                }
                if(!float.IsNaN(p.MaxTopY))Soft(b.max.y<p.MaxTopY,where+$": top {b.max.y:0.000} below {p.MaxTopY}");
                if(!float.IsNaN(p.MountMin))Soft(b.min.y>p.MountMin && b.min.y<p.MountMax && b.max.y>p.MountMax,where+$": mounted in the fascia (base {b.min.y:0.000} within {p.MountMin}..{p.MountMax}, head {b.max.y:0.000} above it)");
                if(!float.IsNaN(p.WallZ))
                {
                    float gap=p.WallZ-b.max.z;
                    Soft(gap>=-.02f && gap<=p.TouchTol+.01f,where+$": back {gap*100:0.0} cm from the wall face z {p.WallZ}");
                }
                if(p.Touch!=null)
                {
                    Transform partner=NearestNamed(p.Touch,t.position,1.5f);
                    Bounds o=default;
                    Check(partner!=null && TryBounds(partner,out o),where+": its "+p.Touch+" is there");
                    float gap=o.min.z-b.max.z;
                    Soft(gap>=-.02f && gap<=p.TouchTol,where+$": touches the {p.Touch} (gap {gap*100:0.0} cm)");
                }
                Say(where+": "+string.Join("; ",notes));
            }
            Say("capture "+H.CaptureFrom(new Vector3(-4,1.6f,13),new Vector3(-4,1.5f,17.2f),Captures+"racks.png"));
            Say("capture "+H.CaptureFrom(new Vector3(-12,1.6f,13),new Vector3(-12,1.5f,17.2f),Captures+"racks-west.png"));
            Say("capture "+H.CaptureFrom(new Vector3(18,1.7f,8),new Vector3(24,.5f,8),Captures+"cargo.png"));
            yield return null;
        }

        private static IEnumerator AmbienceGrid()
        {
            Heading("HQ6 — the ambience reaches every walkable point of the platform");
            HQAmbience bed=HQAmbience.InHQ();
            Check(bed!=null,"the HQ ambience bed '"+HQAmbience.RootName+"' is in the HQ scene");
            yield return Expect(()=>bed.Playing,3f,()=>"the bed plays at HQ (Playing "+bed.Playing+")");
            string grid=SunkCost.Editor.Look.HQAmbienceProbe.Grid();
            Say(grid.Length>600?grid.Substring(0,600)+" …":grid);
            Match walkable=Regex.Match(grid,@"walkable=(\d+)"),dead=Regex.Match(grid,@"dead=(\d+)");
            Check(walkable.Success && dead.Success,"the probe reports walkable= and dead=");
            Check(int.Parse(dead.Groups[1].Value)==0,"no dead point on the grid (dead "+dead.Groups[1].Value+" of "+walkable.Groups[1].Value+" walkable)");
            Check(int.Parse(walkable.Groups[1].Value)>=150,"the grid covered the platform (walkable "+walkable.Groups[1].Value+")");
            foreach(var spot in new[]{("the arrival pad",new Vector3(18,1.6f,14)),("the plank's base",new Vector3(30,1.6f,-16)),("the pickup chute",new Vector3(10,1.6f,15)),("the crane",new Vector3(24,1.6f,4)),("the intake",new Vector3(-11,1.6f,-14)),("the depot aisle",new Vector3(-6,1.6f,11.7f)),("the court",new Vector3(-8,1.6f,-2))})
            {
                float level=bed.LevelAt(spot.Item2);
                Check(level>=HQAmbience.AudibleLevel,$"ambience at {spot.Item1} {spot.Item2}: level {level:0.000} ≥ {HQAmbience.AudibleLevel}");
            }
        }

        private static IEnumerator Spots()
        {
            Heading("HQ7 — the old board's spot and the depot after the cleanup");
            foreach(var shot in new[]{("board-spot",new Vector3(-20,1.6f,12),new Vector3(-22,1.2f,10.6f)),("office",new Vector3(-17,1.6f,11.5f),new Vector3(-22,.8f,15)),("depot-front",new Vector3(-6,1.7f,7),new Vector3(-6,2.5f,10)),("counter",new Vector3(-6,1.65f,11),new Vector3(-6,1.3f,14))})
            {
                string path=Captures+shot.Item1+".png";
                Say("capture "+H.CaptureFrom(shot.Item2,shot.Item3,path));
                yield return null;
                Check(File.Exists(path),"capture written: "+path);
            }
        }

        // The last row: the bed is gated on the crew's world, so a host at sea (the HQ scene
        // still loaded on a host) hears the ship's own ambience and not the platform's.
        private static IEnumerator BedSilentAtSea()
        {
            Heading("HQ8 — the HQ bed is silent at sea");
            ShipParts ship=ShipParts.InWorld(Day.World);
            Check(ship!=null,"the docked ship");
            H.ClientMoveLocalPlayerTo(ship.SpawnPoint(0).position);yield return Wait(.3f);
            string sail=H.ServerSail("Sea");
            Say("sail: "+sail);
            Check(sail.StartsWith("sailing to"),"the ship sails from the hq job's state: "+sail);
            yield return Expect(()=>Day.Departure.Stage==DepartureStage.Complete && Day.World==WorldId.Sea,45f,()=>"the ship arrived at sea: "+H.FlowStatus());
            yield return Wait(1f);
            // A host at sea unloads the HQ scene (FlowStatus: loaded=Session+ShipAtSea), so the
            // bed is usually gone with it; if it survives, it must have stopped.
            yield return Expect(()=>{var b=HQAmbience.InHQ();return b==null || !b.Playing;},3f,()=>"the HQ bed is silent at sea (found "+(HQAmbience.InHQ()!=null)+", HQ scene loaded "+HQ.isLoaded+")");
            Say("at sea: HQ scene loaded="+HQ.isLoaded+", bed found="+(HQAmbience.InHQ()!=null));
            Check(!ShipAmbience.WindSuppressed,"the ship's wind is not suppressed at sea");
            ShipParts atSea=ShipParts.InWorld(WorldId.Sea);
            var ambience=atSea!=null?atSea.GetComponentInChildren<ShipAmbience>(true):null;
            Say("the ship's ambience at sea: heard="+(ambience==null?"none":ambience.Heard.ToString()));
        }
    }
}
