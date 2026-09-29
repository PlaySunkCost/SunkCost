using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SunkCost.UI
{
    // Crisp, resolution-independent glow and symbols over the shared metal plate.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MenuButtonDecoration : MaskableGraphic, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        public string Symbol;
        public bool IconOnly;
        public bool Brackets;
        private bool hovered, selected;
        private float brightness;
        private Button button;
        protected override void Awake() { base.Awake(); raycastTarget = false; button = GetComponentInParent<Button>(); }
        public void OnPointerEnter(PointerEventData e) => hovered = true;
        public void OnPointerExit(PointerEventData e) => hovered = false;
        public void OnSelect(BaseEventData e) => selected = true;
        public void OnDeselect(BaseEventData e) => selected = false;
        private void LateUpdate()
        {
            // Selection events target the Button; pointer position also supports the decoration child.
            bool focus = EventSystem.current != null && button != null && EventSystem.current.currentSelectedGameObject == button.gameObject;
            bool pointer = RectTransformUtility.RectangleContainsScreenPoint(rectTransform, UnityEngine.InputSystem.Mouse.current?.position.ReadValue() ?? Vector2.negativeInfinity);
            float wanted = button != null && button.IsInteractable() && (focus || pointer || hovered || selected) ? 1 : 0;
            float next = Mathf.MoveTowards(brightness, wanted, Time.unscaledDeltaTime * 12);
            if (!Mathf.Approximately(next, brightness)) { brightness = next; SetVerticesDirty(); }
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); Rect r = rectTransform.rect;
            if (!IconOnly && brightness > .001f)
            {
                float inset = 2, cut = Mathf.Min(13, r.height * .15f);
                Vector2[] points = { new(r.xMin+cut,r.yMin+inset), new(r.xMax-cut,r.yMin+inset), new(r.xMax-inset,r.yMin+cut), new(r.xMax-inset,r.yMax-cut), new(r.xMax-cut,r.yMax-inset), new(r.xMin+cut,r.yMax-inset), new(r.xMin+inset,r.yMax-cut), new(r.xMin+inset,r.yMin+cut) };
                for (int layer = 4; layer >= 0; layer--)
                {
                    var c = MenuWidgets.Amber; c.a = brightness * (layer == 0 ? 1 : .045f);
                    for (int i=0;i<points.Length;i++) Line(vh, points[i], points[(i+1)%points.Length], layer == 0 ? 2 : 5+layer*5, c);
                }
                if (Brackets)
                {
                    var c=MenuWidgets.Amber; c.a=brightness;
                    for(int side=-1;side<=1;side+=2)
                    {
                        float x=side<0?r.xMin-12:r.xMax+12, mid=r.center.y;
                        Vector2[] p={new(x-side*7,mid+r.height*.32f),new(x,mid+r.height*.32f),new(x,mid+9),new(x-side*5,mid),new(x,mid-9),new(x,mid-r.height*.32f),new(x-side*7,mid-r.height*.32f)};
                        for(int i=0;i<p.Length-1;i++) Line(vh,p[i],p[i+1],3,c);
                    }
                }
            }
            if(string.IsNullOrEmpty(Symbol)) return;
            var tint=Color.Lerp(MenuWidgets.Paper,new Color(1,.82f,.4f),brightness);
            if(button!=null&&!button.IsInteractable()) tint*=.45f;
            float size=Mathf.Min(48,r.height*.52f);
            Vector2 center=IconOnly?r.center:new Vector2(r.xMin+r.height*.72f,r.center.y);
            DrawIcon(vh,Symbol,center,size,tint);
        }
        private static void Line(VertexHelper vh,Vector2 a,Vector2 b,float width,Color c)
        {
            Vector2 n=(b-a).normalized; n=new Vector2(-n.y,n.x)*width*.5f;
            int i=vh.currentVertCount; vh.AddVert(a-n,c,Vector2.zero);vh.AddVert(a+n,c,Vector2.zero);vh.AddVert(b+n,c,Vector2.zero);vh.AddVert(b-n,c,Vector2.zero);vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);
        }
        private static void Disk(VertexHelper vh,Vector2 center,float radius,Color c,int segments=16)
        { int first=vh.currentVertCount;vh.AddVert(center,c,Vector2.zero);for(int i=0;i<=segments;i++){float a=i*Mathf.PI*2/segments;vh.AddVert(center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,c,Vector2.zero);if(i>0)vh.AddTriangle(first,first+i,first+i+1);} }
        private static void DrawIcon(VertexHelper vh,string symbol,Vector2 o,float s,Color c)
        {
            void L(float x,float y,float xx,float yy,float w=.09f)=>Line(vh,o+new Vector2(x,y)*s,o+new Vector2(xx,yy)*s,w*s,c);
            void D(float x,float y,float radius)=>Disk(vh,o+new Vector2(x,y)*s,radius*s,c);
            if(symbol=="host"||symbol=="join")
            { D(-.2f,.23f,.15f);D(.2f,.23f,.15f);L(-.22f,-.08f,-.26f,-.4f,.34f);L(.2f,-.08f,.24f,-.4f,.34f);if(symbol=="host"){L(.4f,-.17f,.4f,-.49f,.09f);L(.24f,-.33f,.56f,-.33f,.09f);} }
            else if(symbol=="options")
            { for(int i=0;i<8;i++){float a=i*Mathf.PI/4;Vector2 n=new(Mathf.Cos(a),Mathf.Sin(a));Line(vh,o+n*s*.25f,o+n*s*.48f,s*.16f,c);}Disk(vh,o,s*.34f,c);Disk(vh,o,s*.14f,new Color(.075f,.078f,.08f)); }
            else if(symbol=="play") { int i=vh.currentVertCount;vh.AddVert(o+new Vector2(-.3f,-.42f)*s,c,Vector2.zero);vh.AddVert(o+new Vector2(-.3f,.42f)*s,c,Vector2.zero);vh.AddVert(o+new Vector2(.42f,0)*s,c,Vector2.zero);vh.AddTriangle(i,i+1,i+2); }
            else if(symbol=="back") { L(.4f,0,-.4f,0);L(-.4f,0,-.08f,.32f);L(-.4f,0,-.08f,-.32f); }
            else if(symbol=="save") { L(-.35f,-.4f,-.35f,.4f);L(-.35f,.4f,.35f,.4f);L(.35f,.4f,.35f,-.4f);L(.35f,-.4f,-.35f,-.4f);L(-.16f,.34f,-.16f,.08f,.15f);L(-.16f,.08f,.19f,.08f);L(.19f,.08f,.19f,.34f);L(-.18f,-.23f,.18f,-.23f,.14f); }
            else if(symbol=="rename") { L(-.3f,-.28f,.27f,.29f,.22f);L(.25f,.32f,.35f,.22f,.22f);L(-.32f,-.33f,-.43f,-.43f,.16f); }
            else if(symbol=="delete") { L(-.26f,-.4f,-.3f,.23f);L(.26f,-.4f,.3f,.23f);L(-.26f,-.4f,.26f,-.4f);L(-.4f,.31f,.4f,.31f);L(-.13f,.45f,.13f,.45f);L(-.1f,-.28f,-.1f,.15f,.06f);L(.1f,-.28f,.1f,.15f,.06f); }
            else if(symbol=="check") { L(-.4f,0,-.12f,-.26f,.12f);L(-.12f,-.26f,.42f,.35f,.12f); }
            else if(symbol=="reset") { for(int i=0;i<20;i++){float a=(i*14+40)*Mathf.Deg2Rad,b=((i+1)*14+40)*Mathf.Deg2Rad;Line(vh,o+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*s*.36f,o+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*s*.36f,s*.085f,c);}L(.27f,.22f,.42f,.3f);L(.27f,.22f,.27f,.42f); }
            else if(symbol=="quit") { L(-.35f,-.4f,-.35f,.4f);L(-.35f,.4f,.12f,.4f);L(-.35f,-.4f,.12f,-.4f);L(-.1f,0,.45f,0);L(.45f,0,.2f,.25f);L(.45f,0,.2f,-.25f); }
        }
    }
}
