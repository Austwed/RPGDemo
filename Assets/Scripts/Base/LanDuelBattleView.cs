using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BaseConquest.Base
{
    public sealed partial class LanDuelScreen
    {
        private LanDuelBoardGraphic board;
        private readonly List<Image> spells = new();
        private readonly Dictionary<int,Token> tokens=new();
        private readonly Dictionary<int, Button> commanders=new();
        private int selectedActor=-1, lastVisualTick=-1;
        private Button skillButton, guardButton, pauseButton;
        private Text skillText, pauseText, timeText, commanderText;
        private sealed class Token
        {
            internal RectTransform rect, barFill;
            internal RawImage image;
            internal Image baseTile;
            internal Texture2D idle,walk,down,attack;
            internal Vector2 last, goal;
            internal bool initialized;
        }

        private void ShowBattle()
        {
            notice=""; NewPage("模拟对抗");
            var field=Area(page,"Field",.018f,.1f,.747f,.882f);
            field.gameObject.AddComponent<RectMask2D>();
            board=Area(field,"Board",0,0,1,1).gameObject.AddComponent<LanDuelBoardGraphic>();
            board.Click=FieldClicked; board.Rotated=LocalSide==1;
            timeText=Caption(page,"Clock","",.76f,.803f,.98f,.875f,22);
            commanderText=Caption(page,"LeaderStatus","",.76f,.715f,.985f,.80f,19);
            var cards=VerticalList(page,"Commanders",.756f,.415f,.984f,.71f);
            commanders.Clear();tokens.Clear();spells.Clear();lastVisualTick=-1;
            foreach(var u in Frame.units.Where(u=>u.side==LocalSide&&u.kind==(int)SoldierKind.Hero))
            {
                int id=u.id;commanders[id]=Row(cards,"Leader"+id,u.name,()=>SelectActor(id));
            }
            skillButton=ButtonAt(page,"Skill","",.756f,.31f,.984f,.403f,()=>CommandSelected("skill"),true);
            guardButton=ButtonAt(page,"Guard","原地防守",.756f,.213f,.984f,.302f,()=>CommandSelected("guard"));
            pauseButton=ButtonAt(page,"Pause","请求暂停",.756f,.105f,.984f,.20f,()=>{if(Frame.paused)Resume();else VotePause();});
            skillText=skillButton.GetComponentInChildren<Text>();pauseText=pauseButton.GetComponentInChildren<Text>();
            selectedActor=commanders.Keys.DefaultIfEmpty(-1).First();
            DrawActors();
        }
        private void SelectActor(int id){selectedActor=id;lastVisualTick=-1;}
        private void CommandSelected(string kind){if(selectedActor>=0) Order(kind,selectedActor,-1,0,0);}
        private void FieldClicked(float x,float z)
        {
            if(Phase!="fighting"||Frame==null)return;
            var nearest=Frame.units.Where(u=>u.hp>0)
                .Select(u=>(unit:u,d:(u.x-x)*(u.x-x)+(u.z+.38f-z)*(u.z+.38f-z))).OrderBy(p=>p.d).FirstOrDefault();
            if(nearest.unit!=null && nearest.d<.62f)
            {
                var unit=nearest.unit;
                if(unit.side==LocalSide)
                {
                    var leader=Frame.units.FirstOrDefault(u=>u.side==LocalSide&&u.slot==unit.slot&&u.kind==(int)SoldierKind.Hero&&u.hp>0);
                    if(leader!=null)SelectActor(leader.id);
                }
                else if(selectedActor>=0)Order("attack",selectedActor,unit.id,x,z);
            }
            else if(selectedActor>=0)Order("move",selectedActor,-1,x,z);
        }
        private Token NewToken(LanDuelActor unit)
        {
            var t=new Token { rect=Area(board.transform,"Actor"+unit.id,0,0,0,0) };
            t.rect.pivot=new Vector2(.5f,.16f);
            var tile=Area(t.rect,"Side",.5f,.16f,.5f,.16f);
            t.baseTile=tile.gameObject.AddComponent<Image>();t.baseTile.raycastTarget=false;
            var art=Area(t.rect,"Sprite",0,0,1,1);
            t.image=art.gameObject.AddComponent<RawImage>();t.image.raycastTarget=false;
            string id=unit.appearance;
            if(string.IsNullOrEmpty(id))
                id=unit.kind==(int)SoldierKind.Mage?"mage":unit.kind==(int)SoldierKind.Ranged?"archer":unit.kind==(int)SoldierKind.Cavalry?"cavalry":unit.kind==(int)SoldierKind.Hero?"player":"infantry";
            t.idle=PixelDemoArt.Load("Characters",id)??PixelDemoArt.Load("Characters","player");
            t.walk=PixelDemoArt.Motion(id,"walk"); t.down=PixelDemoArt.Motion(id,"down"); t.attack=PixelDemoArt.Motion(id,"attack");
            var hp=Area(t.rect,"Hp",.1f,.94f,.9f,.995f);Back(hp,Theme.Wood);hp.GetComponent<Image>().raycastTarget=false;
            t.barFill=Area(hp,"Fill",0,0,1,1);
            Back(t.barFill,unit.side==LocalSide?new Color32(77,170,236,255):new Color32(223,74,60,255));
            t.barFill.GetComponent<Image>().raycastTarget=false;
            return t;
        }
        private void DrawActors()
        {
            if(board==null||Frame==null)return;
            var box=board.rectTransform.rect;
            bool update=lastVisualTick!=Frame.tick;
            if(update)
            {
                board.SetSight(Frame.visibleCells);lastVisualTick=Frame.tick;
                foreach(var t in tokens)
                    if(!Frame.units.Any(u=>u.id==t.Key))t.Value.rect.gameObject.SetActive(false);
                foreach(var u in Frame.units)
                {
                    if(!tokens.TryGetValue(u.id,out var t)) tokens[u.id]=t=NewToken(u);
                    t.rect.gameObject.SetActive(true);
                    if(u.hp<=0) t.rect.SetAsFirstSibling();
                    var point=new Vector2(LocalSide==0?u.x:Frame.width-u.x,u.z);
                    t.last=t.initialized?t.last:point;t.goal=point;t.initialized=true;
                    var anchor=t.barFill.anchorMax;anchor.x=Mathf.Clamp01(u.hp/Mathf.Max(1,u.maxHp));t.barFill.anchorMax=anchor;
                    t.baseTile.color=u.id==selectedActor?new Color32(241,186,59,230):u.side==LocalSide?new Color32(50,140,236,185):new Color32(215,61,48,190);
                    if(commanders.TryGetValue(u.id,out var btn)){btn.interactable=u.hp>0;btn.image.color=u.id==selectedActor?Theme.Gold:Theme.Button;}
                }
                var chosen=Frame.units.FirstOrDefault(u=>u.id==selectedActor);
                commanderText.text=chosen==null?"选择己方队长":chosen.name+"  "+Mathf.CeilToInt(chosen.hp)+"/"+Mathf.CeilToInt(chosen.maxHp);
                guardButton.interactable=chosen!=null&&chosen.hp>0&&!Frame.paused;
                skillButton.interactable=guardButton.interactable&&chosen.cooldown<=0;
                skillText.text=chosen==null?"技能":chosen.skill+(chosen.cooldown>.001f?"   "+BaseConquest.Battle.CombatNumbers.Duration(chosen.cooldown):"");
                bool ownVote=LocalSide==0?Frame.voteA:Frame.voteB,peerVote=LocalSide==0?Frame.voteB:Frame.voteA;
                pauseText.text=Frame.paused?"继续":ownVote?"撤回暂停请求":peerVote?"同意暂停":"请求暂停";
                pauseButton.interactable=Frame.resumeIn<=0;
                timeText.text="演武   "+TimeSpan.FromSeconds(Frame.seconds).ToString(@"mm\:ss");
                Hint(Frame.resumeIn>0?$"{Mathf.CeilToInt(Frame.resumeIn)} 秒后继续":Frame.paused?"双方已暂停。":ownVote?"已请求暂停，等待对方同意。":peerVote?"对方请求暂停。":"选择队长，点地面移动，点敌军集火。");
            }
            DrawSpells(box);
            foreach(var u in Frame.units)
            {
                var t=tokens[u.id];t.last=Vector2.MoveTowards(t.last,t.goal,Time.unscaledDeltaTime*16);
                t.rect.anchoredPosition=new Vector2(box.width*t.last.x/Frame.width,box.height*t.last.y/Frame.height);
                var scale=1.48f*Mathf.Min(box.width/Frame.width,box.height/Frame.height);
                if(u.kind==(int)SoldierKind.Cavalry||PixelDemoArt.Mounted(u.appearance??""))scale*=1.24f;
                t.rect.sizeDelta=new Vector2(scale,scale);
                var tile=t.baseTile.rectTransform;
                tile.sizeDelta=new Vector2(box.width/Frame.width,box.height/Frame.height)/BaseConquest.RealtimePrototype.PrototypeArena.Subdivision;
                tile.anchoredPosition=Vector2.zero;
                var face=PlayerHeroFrames.Face(LocalSide==0?u.fx:-u.fx,u.fz,HeroFacing.South);
                var tex=u.hp<=0?t.down:u.attackPose>=0?t.attack:u.moving?t.walk:null;
                if (u.appearance == "mage" || u.appearance == "galan")
                {
                    float cell = tex != null ? tex.height / 4f : 64f;
                    float foot = tex == null ? 60 : PixelDemoArt.FootPx(u.hp <= 0 ? "down" : u.attackPose >= 0 ? "attack" : "walk", cell, null);
                    var rect = t.image.rectTransform;
                    rect.anchorMin = rect.anchorMax = new Vector2(.5f, .16f);
                    rect.pivot = new Vector2(.5f, 1 - foot / cell);
                    rect.anchoredPosition = Vector2.zero;
                    rect.sizeDelta = Vector2.one * (scale * cell / 64f);
                }
                if(tex==null){t.image.texture=t.idle;t.image.uvRect=PixelDemoArt.Frame(face,u.moving?HeroPose.Walk:HeroPose.Idle,Frame.seconds);}
                else
                {
                    int cols=Mathf.Max(1,tex.width/(tex.height/4));
                    int col=u.hp<=0?cols-1:u.attackPose>=0?Mathf.Clamp((int)(cols*u.attackPose),0,cols-1):(int)(Frame.seconds*8)%cols;
                    if (u.hp > 0 && u.attackPose >= 0 && (u.appearance == "mage" || u.appearance == "galan"))
                        col = PixelDemoArt.CastFrame(u.appearance, cols, u.attackPose);
                    t.image.texture=tex;t.image.uvRect=new Rect((float)col/cols,1-((int)face+1)*.25f,1f/cols,.25f);
                }
            }
        }
        private void DrawSpells(Rect box)
        {
            var frame = Frame.spells ?? Array.Empty<MageSpellVisual>();
            for (int i = 0; i < frame.Length; i++)
            {
                if (spells.Count == i)
                {
                    var rect = Area(board.transform, "MageSpell", 0, 0, 0, 0);
                    var sprite = rect.gameObject.AddComponent<Image>();
                    sprite.raycastTarget = false;
                    spells.Add(sprite);
                }
                var fx = frame[i];
                var image = spells[i];
                image.enabled = true;
                image.transform.SetAsLastSibling();
                image.sprite = MageSpellVisual.Art(fx.impact);
                image.color = new Color(1, 1, 1, fx.opacity);
                float x = LocalSide == 0 ? fx.x : Frame.width - fx.x;
                image.rectTransform.anchoredPosition = new Vector2(box.width * x / Frame.width, box.height * (fx.z + .36f) / Frame.height);
                image.rectTransform.sizeDelta = Vector2.one * (2 * fx.radius * Mathf.Min(box.width / Frame.width, box.height / Frame.height));
            }
            for (int i = frame.Length; i < spells.Count; i++) spells[i].enabled = false;
        }
        private void ShowResult()
        {
            notice="";NewPage("模拟对抗 · 战报");
            bool isA=LocalSide==0;string victory=Frame.result==3?"平局":Frame.result==(isA?1:2)?"我方获胜":"对方获胜";
            Words(Area(page,"Outcome",.15f,.65f,.85f,.83f),victory,36,false,TextAnchor.MiddleCenter);
            void Side(float left,string name,int count,int standing)
            {
                var col=Area(page,name,left,.32f,left+.32f,.635f);Back(col,Theme.Inset);
                Caption(col,"Side",name,.09f,.65f,.93f,.95f,25);
                Caption(col,"Deployed","出战  "+count,.09f,.33f,.93f,.65f);
                Caption(col,"Standing","留场  "+standing+"      倒地  "+(count-standing),.09f,0,.93f,.34f);
            }
            Side(.155f,"我方",isA?Frame.aDeployed:Frame.bDeployed,isA?Frame.aStanding:Frame.bStanding);
            Side(.525f,"对方",isA?Frame.bDeployed:Frame.aDeployed,isA?Frame.bStanding:Frame.aStanding);
            ButtonAt(page,"Return","返回兵营",.36f,.15f,.64f,.265f,Leave,true);
            Hint("演武用时 "+Mathf.RoundToInt(Frame.seconds)+" 秒");
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class LanDuelBoardGraphic : MaskableGraphic, IPointerClickHandler,
        IPointerDownHandler, IPointerUpHandler, IDragHandler, IScrollHandler
    {
        internal Action<float,float> Click;
        internal bool Rotated;
        private bool[] sight;
        private Texture2D grass;
        private readonly Dictionary<int,Vector2> touches=new();
        private readonly HashSet<int> dragged=new();
        private RectTransform Viewport => (RectTransform)rectTransform.parent;
        public override Texture mainTexture => grass!=null?grass:(grass=PixelDemoArt.Load("Terrain","grass"));
        internal void SetSight(bool[] value)
        {
            if(value==null||sight!=null&&sight.SequenceEqual(value))return;
            sight=value;SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();var r=rectTransform.rect;
            int w=LanDuelMatch.Width,h=LanDuelMatch.Height;
            for(int z=0;z<h;z++)for(int x=0;x<w;x++)
            {
                bool lit=sight==null||sight[z*w+(Rotated?w-1-x:x)];
                var tint=lit?new Color32(255,255,255,255):new Color32(80,94,112,255);
                float l=r.xMin+r.width*x/w, b=r.yMin+r.height*z/h, dx=r.width/w, dz=r.height/h;
                // A narrow dark seam keeps reachable cells legible at mobile resolution.
                int n=vh.currentVertCount;
                vh.AddVert(new Vector2(l,b),tint,Vector2.zero);vh.AddVert(new Vector2(l,b+dz-.4f),tint,Vector2.up);
                vh.AddVert(new Vector2(l+dx-.4f,b+dz-.4f),tint,Vector2.one);vh.AddVert(new Vector2(l+dx-.4f,b),tint,Vector2.right);
                vh.AddTriangle(n,n+1,n+2);vh.AddTriangle(n,n+2,n+3);
            }
        }
        public void OnPointerClick(PointerEventData data)
        {
            if(dragged.Remove(data.pointerId))return;
            if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform,data.position,data.pressEventCamera,out var pos))return;
            var r=rectTransform.rect;float x=(pos.x-r.xMin)/r.width*LanDuelMatch.Width;
            Click?.Invoke(Rotated?LanDuelMatch.Width-x:x,(pos.y-r.yMin)/r.height*LanDuelMatch.Height);
        }
        private Vector2 ViewPoint(PointerEventData e)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(Viewport,e.position,e.pressEventCamera,out var p);
            return p;
        }
        public void OnPointerDown(PointerEventData e)
        {
            dragged.Remove(e.pointerId);touches[e.pointerId]=ViewPoint(e);
            if(touches.Count>1)foreach(int id in touches.Keys)dragged.Add(id);
        }
        public void OnPointerUp(PointerEventData e)=>touches.Remove(e.pointerId);
        public void OnScroll(PointerEventData e)=>Zoom(rectTransform.localScale.x*Mathf.Pow(1.15f,Mathf.Clamp(e.scrollDelta.y,-2,2)),ViewPoint(e));
        public void OnDrag(PointerEventData e)
        {
            if(!touches.TryGetValue(e.pointerId,out var previous))return;
            var point=ViewPoint(e);dragged.Add(e.pointerId);
            if(touches.Count==2)
            {
                var other=touches.First(pair=>pair.Key!=e.pointerId).Value;
                float span=Vector2.Distance(previous,other);
                if(span>2)Zoom(rectTransform.localScale.x*Vector2.Distance(point,other)/span,other);
            }
            else if(touches.Count==1)MoveView(rectTransform.anchoredPosition+point-previous);
            touches[e.pointerId]=point;
        }
        private void Zoom(float requested,Vector2 anchor)
        {
            float previous=rectTransform.localScale.x, zoom=Mathf.Clamp(requested,1,2.5f);
            rectTransform.localScale=new Vector3(zoom,zoom,1);
            MoveView(anchor+(rectTransform.anchoredPosition-anchor)*(zoom/previous));
        }
        private void MoveView(Vector2 position)
        {
            Vector2 limit=Viewport.rect.size*(rectTransform.localScale.x-1)*.5f;
            rectTransform.anchoredPosition=new Vector2(Mathf.Clamp(position.x,-limit.x,limit.x),Mathf.Clamp(position.y,-limit.y,limit.y));
        }
    }
}
