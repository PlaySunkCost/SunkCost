using System;
using System.Collections.Generic;
using System.IO;
using SunkCost.Editor.Prototype;
using SunkCost.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SunkCost.Editor.Look
{
    public static class HQGeneratedSetup
    {
        [MenuItem("Sunk Cost/Look/Build generated HQ")]
        public static void BuildMenu() => Debug.Log(Apply());

        public static string Apply()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            HQModelSetup.Apply();
            HQPrototypeBuilder.CreateOrUpdate();
            DeckCabinRideSetup.Apply();
            EditorSceneManager.OpenScene(HQPrototypeBuilder.ScenePath);
            Validate();
            return "Generated HQ built, saved and validated. Open Session and press Play to host.";
        }

        public static string Rebuild()
        {
            HQPrototypeBuilder.CreateOrUpdate();
            DeckCabinRideSetup.Apply();
            EditorSceneManager.OpenScene(HQPrototypeBuilder.ScenePath);
            return Validate();
        }

        public static string Capture()
        {
            Directory.CreateDirectory(LookCapture.Folder);
            RenderTexture sky=SunkCost.Look.SkyEnvironment.Refresh();
            try
            {
                LookCapture.Shoot("hq-generated-overview",new Vector3(48,39,-58),new Vector3(-8,0,1),48);
                LookCapture.Shoot("hq-generated-depot",new Vector3(-9,1.7f,6),new Vector3(-7,1.8f,16),83);
                LookCapture.Shoot("hq-generated-bridge",new Vector3(-25,1.65f,-12),new Vector3(-48,1,-12),72);
                LookCapture.Shoot("hq-generated-arrival",new Vector3(-6,3,-20),new Vector3(-11,1,-14),65);
                LookCapture.Shoot("hq-generated-court",new Vector3(-7,2,-9),new Vector3(-17,2.3f,-2),78);
                LookCapture.Shoot("hq-generated-intake",new Vector3(11,2.5f,7),new Vector3(17,1.8f,15),74);
            }
            finally { SunkCost.Look.SkyEnvironment.Restore(sky); }
            return "HQ screenshots: Temp/look/hq-generated-*.png";
        }

        public static string Validate()
        {
            HQPrototypeValidator.ValidateOrThrow();
            Physics.SyncTransforms();
            var errors=new List<string>();
            var ship=UnityEngine.Object.FindFirstObjectByType<ShipParts>();
            if(ship==null || Mathf.Abs(ship.transform.position.y)>.01f)errors.Add("Ship deck not level with HQ.");
            var spawn=GameObject.Find("Spawn Points");
            foreach(Transform point in spawn.transform)
                if(Physics.CheckCapsule(point.position+Vector3.up*.35f,point.position+Vector3.up*1.5f,.3f,~0,QueryTriggerInteraction.Ignore))errors.Add("Blocked spawn "+point.name);
            // A continuous walking surface across the real bridge/ship edge.
            for(float x=-29;x>=-48;x-=.25f)
            {
                if(!Physics.Raycast(new Vector3(x,1,-12),Vector3.down,out var hit,2,~0,QueryTriggerInteraction.Ignore) || Mathf.Abs(hit.point.y)>.12f)
                    errors.Add("Bridge floor gap/step at "+x);
                if(Physics.CheckCapsule(new Vector3(x,.4f,-12),new Vector3(x,1.5f,-12),.28f,~0,QueryTriggerInteraction.Ignore))errors.Add("Boarding obstruction at "+x);
                if(x>=-44 && (ship.IsSafelyAboard(new Vector3(x,0,-12)) || ship.IsAboard(new Vector3(x,0,-12))))errors.Add("Fixed bridge counted as aboard at "+x);
            }
            if(UnityEngine.Object.FindObjectsByType<DockBoardingGate>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length!=2) errors.Add("Need the HQ and ship boarding gates.");
            if(errors.Count>0)throw new InvalidOperationException(string.Join("\n",errors));
            string report="HQ validation passed: four clear spawns, level continuous bridge, two gates, shop, court and plank. "+LookCapture.PerfReport();
            Directory.CreateDirectory("Temp/HQBuild");File.WriteAllText("Temp/HQBuild/validation.txt",report);
            return report;
        }

        public static string OpenSession()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Prototype/Session.unity");
            return "Session scene ready. Press Play, then Host.";
        }
    }
}
