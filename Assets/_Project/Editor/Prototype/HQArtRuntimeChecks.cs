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
            H.CaptureFrom(new Vector3(-11,1.65f,-13),new Vector3(-8,1.6f,13),"Temp/look/hq-runtime-arrival.png");
        }
    }
}
