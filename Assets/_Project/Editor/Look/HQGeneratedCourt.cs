using SunkCost.Look;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SunkCost.Editor.Look
{
    public static class HQGeneratedCourt
    {
        public static void Apply(GameObject root)
        {
            foreach(string name in new[]{"Court Fence W","Court Fence E"})
            {
                var old=root.transform.Find(name);if(old!=null) Object.DestroyImmediate(old.gameObject);
            }
            foreach(float x in new[]{-20f,4f})
                for(float z=-9;z<=5;z+=2)
                {
                    var fence=HQGeneratedLayout.Model(root,"CourtFence",new Vector3(x,0,z),90);
                    HQGeneratedLayout.Box(fence,new Vector3(0,1.5f,0),new Vector3(2,3,.12f));
                }
            // Preserve the existing score trigger/count component and names used by
            // the multiplayer tests. Replace the entire old visual/collider shell.
            foreach(string name in new[]{"Hoop W","Hoop E"})
            {
                var hoop=root.transform.Find(name);
                if(hoop==null) continue;
                foreach(var c in hoop.GetComponents<Collider>()) Object.DestroyImmediate(c);
                for(int i=hoop.childCount-1;i>=0;i--)
                {
                    var t=hoop.GetChild(i);
                    if(t.name!="Score" && t.name!="Score Trigger") Object.DestroyImmediate(t.gameObject);
                }
                // The imported rim projects towards +Z. Both hoop roots already
                // face the court: don't rotate the art away from its score trigger.
                // Native rim centre Z=1.13, Y=3.05 -> gameplay rim Z=.27.
                var visual=HQGeneratedLayout.Model(hoop.gameObject,"BasketHoop",new Vector3(0,0,-.86f));
                foreach(var mf in visual.GetComponentsInChildren<MeshFilter>())
                    mf.gameObject.AddComponent<MeshCollider>().sharedMesh=mf.sharedMesh;
                var score=hoop.Find("Score");score.localPosition=new Vector3(0,3.8f,-.04f);
                // Net is cosmetic and never blocks the ball.
                for(int i=0;i<10;i++)
                {
                    float a=i*Mathf.PI*2/10;
                    var thread=HQGeneratedLayout.Slab(hoop.gameObject,"Net",new Vector3(Mathf.Cos(a)*.2f,2.64f,.27f+Mathf.Sin(a)*.2f),new Vector3(.012f,.38f,.012f),LookMaterials.Net());
                }
                HQBasketballCourtTuning.ApplyTo(hoop);
            }
        }
    }
}
