using SunkCost.Net;
using SunkCost.Player;
using SunkCost.World;
using UnityEngine;

namespace SunkCost.Shop
{
    // Owner-only presentation. Rows come from ShopCatalog; all prices, reach,
    // funds, ownership and delivery are still checked by ServerBuy per request.
    public sealed class ShopBrowserUI : MonoBehaviour
    {
        private HQPlayerController player;
        private ShopDisplay counter;
        private Vector2 scroll;
        private string search = string.Empty;
        private int category;
        private float nextRequest;
        private GUIStyle titleStyle, rowStyle, hintStyle, buttonStyle;
        public bool IsOpen => counter != null && SessionInputGate.ShopOpen;

        private void Awake() => player = GetComponent<HQPlayerController>();

        public void Open(ShopDisplay value)
        {
            if (value == null || !value.BrowsesCatalog || player == null || !player.IsOwner) return;
            counter = value;
            if (!CanBrowse()) { counter = null; return; }
            search = string.Empty; category = 0; scroll = Vector2.zero;
            SessionInputGate.OpenShop();
        }

        public void Close()
        {
            if (counter != null) SessionInputGate.CloseShop();
            counter = null;
        }

        private void OnDisable() => Close();
        private void Update()
        {
            if (counter != null && (!SessionInputGate.ShopOpen || !CanBrowse() ||
                SessionInputGate.MenuOpen || SessionInputGate.OverlayOpen || !SessionInputGate.ApplicationFocused)) Close();
            // A scene unload destroys the referenced counter first.
            else if (counter == null && player != null && player.IsOwner && SessionInputGate.ShopOpen) SessionInputGate.CloseShop();
        }

        private bool CanBrowse()
        {
            CrewDayState day = CrewDayState.Instance;
            if (player == null || !player.IsSpawned || player.IsDead || player.TravelLocked ||
                counter == null || !counter.isActiveAndEnabled || day == null || day.World != WorldId.HQ ||
                day.Travelling || day.Phase == DayPhase.Plank || counter.gameObject.scene != player.gameObject.scene) return false;
            Collider hit = counter.GetComponentInChildren<Collider>();
            Vector3 point = hit != null ? hit.ClosestPoint(player.EyePosition) : counter.transform.position;
            return Vector3.Distance(player.EyePosition, point) <= player.InteractReach + .5f;
        }

        // Shared by the button and runtime checks. This is a request, not a grant.
        public bool Buy(string id)
        {
            if (!IsOpen || !CanBrowse() || Time.unscaledTime < nextRequest) return false;
            ShopItem item = ShopCatalog.Resolve().Find(id);
            if (item == null || player.Upgrades == null) return false;
            nextRequest = Time.unscaledTime + .35f;
            player.Upgrades.RequestBuy(id);
            return true;
        }

        private bool Matches(ShopItem item)
        {
            if (item == null) return false;
            if (category == 1 && item.Kind != ShopItemKind.Consumable) return false;
            if (category == 2 && item.Kind != ShopItemKind.Upgrade) return false;
            return string.IsNullOrWhiteSpace(search) || (item.Name ?? item.Id).IndexOf(search.Trim(), System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public int VisibleItemCount
        {
            get { int count=0; foreach(var item in ShopCatalog.Resolve().Items) if(Matches(item))count++; return count; }
        }

        private void OnGUI()
        {
            if (!IsOpen || !CanBrowse() || SessionInputGate.MenuOpen || SessionInputGate.OverlayOpen) return;
            EnsureStyles();
            Matrix4x4 oldMatrix = GUI.matrix;
            Color oldColor = GUI.color;
            bool oldEnabled = GUI.enabled;
            float scale = Mathf.Min(1.5f, Screen.width / 880f, Screen.height / 660f);
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            float w = Screen.width / scale, h = Screen.height / scale;
            Rect panel = new((w-820)/2,(h-600)/2,820,600);
            try
            {
                GUI.color = new Color(.015f,.025f,.035f,.94f);
                GUI.DrawTexture(new Rect(0,0,w,h),Texture2D.whiteTexture);
                GUI.color = new Color(.055f,.09f,.10f,1);
                GUI.DrawTexture(panel,Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.BeginGroup(panel);
                GUI.Label(new Rect(24,18,590,40),"EQUIPMENT DEPOT",titleStyle);
                if(GUI.Button(new Rect(692,22,104,32),"Close [Esc]",buttonStyle)) Close();
                GUI.Label(new Rect(24,62,740,26),$"Crew funds: ${CrewDayState.Instance.Balance}   •   Purchases use the shared pot",hintStyle);
                GUI.Label(new Rect(24,99,60,28),"Search",hintStyle);
                string typed=GUI.TextField(new Rect(88,96,370,30),search);
                if(typed!=search){search=typed;scroll=Vector2.zero;}
                int selected=GUI.Toolbar(new Rect(478,96,318,30),category,new[]{"All","Supplies","Upgrades"});
                if(selected!=category){category=selected;scroll=Vector2.zero;}
                int count=VisibleItemCount;
                scroll=GUI.BeginScrollView(new Rect(24,144,772,362),scroll,new Rect(0,0,748,Mathf.Max(362,count*84)));
                int row=0;
                foreach(var item in ShopCatalog.Resolve().Items)
                {
                    if(!Matches(item))continue;
                    float y=row++*84;
                    GUI.color=new Color(.09f,.15f,.16f,1);
                    GUI.DrawTexture(new Rect(0,y,740,76),Texture2D.whiteTexture);
                    GUI.color=Color.white;
                    GUI.Label(new Rect(16,y+9,505,30),item.Name,rowStyle);
                    GUI.Label(new Rect(16,y+42,505,24),item.Kind==ShopItemKind.Upgrade?"Personal upgrade • one per player":"Collect at the PICKUP chute",hintStyle);
                    bool owned=item.Kind==ShopItemKind.Upgrade && player.Upgrades!=null && player.Upgrades.Has(item.Upgrade);
                    GUI.Label(new Rect(528,y+20,84,32),$"${item.Price}",rowStyle);
                    GUI.enabled=oldEnabled && !owned && CrewDayState.Instance.Balance>=item.Price && Time.unscaledTime>=nextRequest;
                    if(GUI.Button(new Rect(620,y+18,102,38),owned?"Owned":"Buy",buttonStyle))Buy(item.Id);
                    GUI.enabled=oldEnabled;
                }
                if(count==0)GUI.Label(new Rect(16,20,690,40),"No items match your search.",rowStyle);
                GUI.EndScrollView();
                string refusal=player.Upgrades!=null?player.Upgrades.Refusal:string.Empty;
                GUI.color=string.IsNullOrEmpty(refusal)?new Color(.65f,.83f,.8f):new Color(1,.6f,.35f);
                GUI.Label(new Rect(24,522,772,46),string.IsNullOrEmpty(refusal)?"Supplies arrive at PICKUP. Upgrades apply to you immediately.":refusal,hintStyle);
                GUI.EndGroup();
            }
            finally { GUI.enabled=oldEnabled; GUI.color=oldColor; GUI.matrix=oldMatrix; }
        }

        private void EnsureStyles()
        {
            if(titleStyle!=null)return;
            titleStyle=new GUIStyle(GUI.skin.label){fontSize=27,fontStyle=FontStyle.Bold};
            rowStyle=new GUIStyle(GUI.skin.label){fontSize=20};
            hintStyle=new GUIStyle(GUI.skin.label){fontSize=15,wordWrap=true};
            buttonStyle=new GUIStyle(GUI.skin.button){fontSize=16};
        }
    }
}
