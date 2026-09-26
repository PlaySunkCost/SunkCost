using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using SunkCost.Player;
using SunkCost.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using H = SunkCost.Editor.Prototype.HQPrototypeTestHooks;

namespace SunkCost.Editor.Prototype
{
    // Real controller input against the authored world, plus a ball falling
    // through each generated rim. Networking/travel use the existing loop matrix.
    public static class HQArtRuntimeChecks
    {
        private const string Log="Temp/hq-art-matrix.log";
        private static readonly Stack<IEnumerator> steps=new();
        private static Keyboard keyboard;
        private static SunkCost.Shop.ShopItem extraCatalogRow;
        private static InputSettings.EditorInputBehaviorInPlayMode savedEditor;
        private static InputSettings.BackgroundBehavior savedBackground;
        private static HQPlayerController Player => WorldSceneFlow.LocalPlayer();

        public static void RunAsHost()
        {
            if(!EditorApplication.isPlaying || Player==null)throw new InvalidOperationException("Host first.");
            File.WriteAllText(Log,"HQ art runtime checks "+DateTime.Now+"\n");
            savedEditor=InputSystem.settings.editorInputBehaviorInPlayMode;
            savedBackground=InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            keyboard=InputSystem.AddDevice<Keyboard>();HQPlayerController.KeyboardForChecks=keyboard;HQPlayerController.BypassInputGateForChecks=true;
            steps.Clear();steps.Push(Run());EditorApplication.update+=Tick;
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
                Finish("MATRIX_PASS");
            }
            catch(Exception e){Finish("FAIL: "+e);}
        }

        private static void Finish(string result)
        {
            if(extraCatalogRow!=null)
                ((List<SunkCost.Shop.ShopItem>)SunkCost.Shop.ShopCatalog.Resolve().Items).Remove(extraCatalogRow);
            extraCatalogRow=null;
            if(Player!=null)Player.GetComponent<SunkCost.Shop.ShopBrowserUI>()?.Close();
            File.AppendAllText(Log,result+"\n");EditorApplication.update-=Tick;steps.Clear();
            HQPlayerController.KeyboardForChecks=null;HQPlayerController.BypassInputGateForChecks=false;
            if(keyboard!=null)InputSystem.RemoveDevice(keyboard);keyboard=null;
            InputSystem.settings.editorInputBehaviorInPlayMode=savedEditor;InputSystem.settings.backgroundBehavior=savedBackground;
        }
        private static void Check(bool pass,string text)
        {
            if(!pass)throw new Exception(text);File.AppendAllText(Log,"PASS "+text+"\n");
        }
        private static IEnumerator Wait(float seconds)
        {
            float until=Time.unscaledTime+seconds;while(Time.unscaledTime<until)yield return null;
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
            foreach(var stand in UnityEngine.Object.FindObjectsByType<SunkCost.Shop.ShopDisplay>(FindObjectsSortMode.None))
            {
                if(stand.BrowsesCatalog)continue;
                Player.TeleportLocal(stand.transform.position+stand.transform.forward*1.5f+Vector3.up*.05f,0);
                yield return Wait(.2f);
                Vector3 to=stand.transform.Find("Display").position-Player.EyePosition;
                Player.transform.rotation=Quaternion.LookRotation(new Vector3(to.x,0,to.z));
                Player.SetPitchForChecks(-Mathf.Atan2(to.y,new Vector2(to.x,to.z).magnitude)*Mathf.Rad2Deg);
                yield return Wait(.3f);
                var hud=Player.GetComponent<PlayerHudUI>();
                Check(Player.CurrentShopDisplay==stand && hud.PromptText.Contains(stand.Item.Name),"aim shows item name: "+stand.ItemId);
                Player.transform.rotation*=Quaternion.Euler(0,180,0);
                yield return Wait(.2f);
                Check(Player.CurrentShopDisplay==null && !hud.PromptText.Contains(stand.Item.Name),"look away hides item name: "+stand.ItemId);
            }
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
            H.ClientMoveLocalPlayerTo(SunkCost.Editor.Look.HQGeneratedLayout.Arrival+Vector3.up*.05f);
            H.CaptureFrom(new Vector3(18.75f,1.65f,14.5f),new Vector3(18,1.4f,11.35f),"Temp/look/hq-runtime-arrival.png");
        }

        private static IEnumerator Catalogue()
        {
            SunkCost.Shop.ShopDisplay counter=null;
            foreach(var display in UnityEngine.Object.FindObjectsByType<SunkCost.Shop.ShopDisplay>(FindObjectsSortMode.None))
                if(display.BrowsesCatalog)counter=display;
            Check(counter!=null,"one counter offers the entire catalogue");
            Player.TeleportLocal(counter.transform.position+counter.transform.forward*1.8f+Vector3.up*.05f,0);
            yield return Wait(.2f);
            Vector3 to=counter.transform.position+Vector3.up-Player.EyePosition;
            Player.transform.rotation=Quaternion.LookRotation(new Vector3(to.x,0,to.z));
            Player.SetPitchForChecks(-Mathf.Atan2(to.y,new Vector2(to.x,to.z).magnitude)*Mathf.Rad2Deg);
            yield return Wait(.3f);
            Check(Player.CurrentShopDisplay==counter,"aim finds the catalogue counter");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.E));
            yield return Wait(.1f);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());
            yield return Wait(.2f);
            var browser=Player.GetComponent<SunkCost.Shop.ShopBrowserUI>();
            var catalog=SunkCost.Shop.ShopCatalog.Resolve();
            Check(browser.IsOpen && SunkCost.Net.SessionInputGate.ShopOpen,"E opens the catalogue and captures gameplay input");
            Check(browser.VisibleItemCount==catalog.Items.Count,"menu lists every catalogue entry");
            ScreenCapture.CaptureScreenshot("Temp/look/hq-shop-catalogue.png");
            yield return Wait(.5f);
            int originalCount=catalog.Items.Count;
            extraCatalogRow=new SunkCost.Shop.ShopItem{Id="hq-catalogue-proof",Name="Catalogue expansion proof",Price=11,Prefab=catalog.Find(SunkCost.Shop.ShopCatalog.AirTankId).Prefab};
            ((List<SunkCost.Shop.ShopItem>)catalog.Items).Add(extraCatalogRow);
            Check(browser.VisibleItemCount==originalCount+1,"fifth item appears without a new stand or scene rebuild");
            var day=CrewDayState.Instance;
            int oldBalance=day.Balance;
            day.ServerSetBalanceForChecks(100);
            Check(browser.Buy(extraCatalogRow.Id),"catalogue submits new-item purchase");
            yield return Wait(.5f);
            Check(day.Balance==89,"server charges catalogue price for item with no dedicated display");
            SunkCost.Interaction.CarryableItem delivered=null;
            foreach(var item in SunkCost.Interaction.CarryableItem.Spawned)
                if(item.name.StartsWith(extraCatalogRow.Name))delivered=item;
            Check(delivered!=null,"catalogue purchase uses shared delivery chute");
            FishNet.InstanceFinder.ServerManager.Despawn(delivered.gameObject);
            day.ServerSetBalanceForChecks(oldBalance);
            ((List<SunkCost.Shop.ShopItem>)catalog.Items).Remove(extraCatalogRow);extraCatalogRow=null;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Escape));
            yield return Wait(.1f);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());
            Check(!browser.IsOpen && !SunkCost.Net.SessionInputGate.ShopOpen,"Escape closes the catalogue");
            browser.Open(counter);
            Player.TeleportLocal(new Vector3(0,.05f,0),0);
            yield return Wait(.2f);
            Check(!browser.IsOpen,"moving away closes the catalogue");
        }
    }
}
