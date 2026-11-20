using BaseConquest.Presentation;
using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Base;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
namespace BaseConquest.Battle
{
    // 开发工具有独立选取模式。选目标时不下达移动、集火或部署命令。
    public sealed partial class DeveloperPanel : MonoBehaviour
    {
        private BaseSceneController baseOwner;
        private BattleSceneController battleOwner;
        private Font font;
        private GameObject canvas;
        private RectTransform panel,body;
        private Button opener;
        private ScrollRect scroll;
        private Text targetText,resultText,pickingCaption,instructionText;
        private RectTransform liveReadout;
        private Text liveText;
        private float nextLiveRefresh;
        private RectTransform pickingPrompt;
        private int page,unitId=-1;
        private GridPosition? cell;
        private bool open,picking;
        private readonly DeveloperTargetSelection target=new DeveloperTargetSelection();
        private bool lastReady;
        private GameObject shield;
        private string message="";
        private SoldierKind spawnKind=SoldierKind.Melee;
        private float y;
        private int closedFrame=-1;
        private readonly List<RaycastResult> hits=new List<RaycastResult>();
        public bool BlocksGameInput=>RuntimeDeveloperOptions.Enabled&&(open||picking||Application.isPlaying&&closedFrame==Time.frameCount);
        public bool BlocksPointer(Vector2 point)=>BlocksGameInput||opener!=null&&opener.gameObject.activeInHierarchy&&RectTransformUtility.RectangleContainsScreenPoint(opener.GetComponent<RectTransform>(),point);
        private bool Available=>battleOwner!=null ? battleOwner.DeveloperAvailable : baseOwner!=null&&baseOwner.DeveloperAvailable;
        public void Initialize(BaseSceneController owner,Font uiFont){baseOwner=owner;battleOwner=null;Build(uiFont);}
        public void Initialize(BattleSceneController owner,Font uiFont){battleOwner=owner;baseOwner=null;Build(uiFont);}
        private void Build(Font uiFont)
        {
            RuntimeDeveloperOptions.Changed-=ModeChanged;RuntimeDeveloperOptions.Changed+=ModeChanged;
            font=uiFont;target.Cancel();open=picking=false;unitId=-1;cell=null;message="";
            if(canvas!=null){canvas.SetActive(false);Destroy(canvas);}
            canvas=UiPrefabBinding.Create("DeveloperTools","DeveloperTools",transform,true);
            canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;canvas.GetComponent<Canvas>().sortingOrder=4000;
            var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            var shade=Box(canvas.transform,"DeveloperInputShield",Vector2.one*.5f,Vector2.zero,Vector2.zero,new Color(0,0,0,.08f));shade.anchorMin=Vector2.zero;shade.anchorMax=Vector2.one;shade.offsetMin=shade.offsetMax=Vector2.zero;shield=shade.gameObject;shield.SetActive(false);
            pickingPrompt=Box(canvas.transform,"TargetPrompt",new Vector2(.5f,0),new Vector2(0,32),new Vector2(1220,56),new Color(.06f,.10f,.13f,1));
            pickingCaption=Text(pickingPrompt,"",Vector2.zero,new Vector2(1200,48),18);pickingCaption.alignment=TextAnchor.MiddleCenter;pickingPrompt.gameObject.SetActive(false);
            if (baseOwner != null)
            {
                liveReadout = Box(canvas.transform, "ExplorationDiagnosticsReadout", Vector2.up, new Vector2(247, -200),
                    new Vector2(470, 130), new Color(.06f, .10f, .13f, .94f));
                liveReadout.GetComponent<Image>().raycastTarget = false;
                liveText = Text(liveReadout, "", new Vector2(0, 15), new Vector2(448, 93), 16);
                opener = Button(liveReadout, "工具", new Vector2(184, -43), new Vector2(85, 33), Open);
                nextLiveRefresh = 0;
            }
            else
            {
                liveReadout = null;
                opener=Button(canvas.transform,"开发者",new Vector2(-90,-88),new Vector2(140,32),Open,new Vector2(1,1));
            }
            panel=Box(canvas.transform,"开发者面板",new Vector2(1,.5f),new Vector2(-300,0),new Vector2(580,650),new Color(.06f,.10f,.13f,.99f));
            Text(panel,"开发者工具",new Vector2(-120,290),new Vector2(300,40),24);
            Button(panel,"关闭",new Vector2(220,290),new Vector2(100,42),Close);
            string[] names={"单位","地图","战斗","经营","剧情文本"};for(int i=0;i<names.Length;i++){int n=i;Button(panel,names[i],new Vector2(-220+i*110,235),new Vector2(104,44),()=>{target.Cancel();cell=null;page=n;Render();});}
            targetText=Text(panel,"",new Vector2(0,178),new Vector2(540,58),17);
            instructionText=Text(panel,"先选效果，再点地图目标；同格多人时再选具体单位。",new Vector2(0,116),new Vector2(540,44),17);
            var viewport=Box(panel,"工具列表",Vector2.one*.5f,new Vector2(0,-70),new Vector2(548,300),new Color(.11f,.17f,.20f));viewport.gameObject.GetOrAdd<RectMask2D>();
            body=Rect(viewport,"Content",new Vector2(.5f,1),Vector2.zero,new Vector2(540,300));body.pivot=new Vector2(.5f,1);
            scroll=viewport.gameObject.GetOrAdd<ScrollRect>();scroll.viewport=viewport;scroll.content=body;scroll.horizontal=false;scroll.movementType=ScrollRect.MovementType.Clamped;
            resultText=Text(panel,"",new Vector2(0,-264),new Vector2(540,70),17);
            panel.gameObject.SetActive(false);
            UiPrefabBinding.Complete(canvas);
            RefreshVisibility();
        }
        private void ModeChanged(bool enabled)
        {
            if(!enabled){target.Cancel();open=picking=false;if(panel!=null)panel.gameObject.SetActive(false);if(shield!=null)shield.SetActive(false);if(battleOwner!=null)battleOwner.ClearDeveloperEffects();else{baseOwner?.RemoveDeveloperDiagnostics();baseOwner?.ClearDeveloperEffects();}}
            RefreshVisibility();
        }
        private void Open()
        {
            if(!RuntimeDeveloperOptions.Enabled||!Available)return;
            target.Cancel();unitId=-1;cell=null;
            if(battleOwner!=null)battleOwner.PauseForDeveloper();else baseOwner.Hud.ClosePanel();
            open=true;picking=false;panel.gameObject.SetActive(true);Render();
        }
        private void Close(){closedFrame=Time.frameCount;target.Cancel();open=picking=false;panel.gameObject.SetActive(false);shield.SetActive(false);pickingPrompt.gameObject.SetActive(false);}
        private void Arm(Action choose)
        {
            if(battleOwner!=null&&!battleOwner.DeveloperReady){message="等待本轮动画结束后再使用工具";Render();return;}
            choose();unitId=-1;cell=null;picking=true;open=false;panel.gameObject.SetActive(false);shield.SetActive(true);
            message=target.Title+"："+(target.Kind==DeveloperTargetKind.Unit ? "点选目标单位（含倒地／阵亡队长）" : target.Kind==DeveloperTargetKind.Facility ? "点击粮田或工坊" : "点击目标空格");
        }
        private void ApplyUnit(int id)
        {
            string title=target.Title;unitId=id;cell=battleOwner.PreviewSimulation.Soldier(id)?.Cell;
            bool ok=battleOwner.DeveloperApplyUnit(target,id,title);
            FinishTarget(ok,title);
        }
        private void FinishTarget(bool ok,string title)
        {
            message=ok ? "已执行："+title : "此目标不适用；"+title+"仍在等待目标（Esc取消）";
            picking=!ok;open=ok;panel.gameObject.SetActive(open);if(open)Render();
        }
        private bool RefreshVisibility()
        {
            if(canvas==null)return false;bool active=RuntimeDeveloperOptions.Enabled&&Available;canvas.SetActive(active);
            bool uiBlocked=battleOwner!=null ? battleOwner.DeveloperUiBlocked : baseOwner.DeveloperUiBlocked;
            opener.gameObject.SetActive(active&&!open&&!picking&&!uiBlocked);
            if (liveReadout != null)
            {
                bool show = active && !uiBlocked && !open && !picking && !BaseWorldJourney.OnWorld(baseOwner.State);
                if (!show)
                {
                    liveReadout.gameObject.SetActive(false);
                    baseOwner.HideDeveloperDiagnostics();
                }
                else if (!liveReadout.gameObject.activeSelf || Time.unscaledTime >= nextLiveRefresh)
                {
                    liveReadout.gameObject.SetActive(true);
                    nextLiveRefresh = Time.unscaledTime + .1f;
                    liveText.text = baseOwner.RefreshDeveloperDiagnostics();
                }
            }
            if(battleOwner!=null) // 战斗的小信息占右上角，开发入口移到左侧军队栏下方。
            {
                var r=opener.GetComponent<RectTransform>();r.anchorMin=r.anchorMax=new Vector2(0,1);
                float ratio=battleOwner.HudScale/canvas.GetComponent<Canvas>().scaleFactor;
                r.anchoredPosition=new Vector2(70*ratio,-(battleOwner.PreviewSimulation.ModernRules ? 312 : 526)*ratio);r.sizeDelta=new Vector2(118*ratio,36*ratio);
                var label=opener.GetComponentInChildren<Text>();label.fontSize=Mathf.Max(1,Mathf.RoundToInt(16*ratio));
                label.rectTransform.anchorMin=Vector2.zero;label.rectTransform.anchorMax=Vector2.one;
                label.rectTransform.offsetMin=new Vector2(6,2)*ratio;label.rectTransform.offsetMax=new Vector2(-6,-2)*ratio;
            }
            shield.SetActive(active&&(open||picking));
            pickingPrompt.gameObject.SetActive(active&&picking);if(picking)pickingCaption.text=message+"（Esc取消）";
            if(!active){target.Cancel();open=picking=false;panel.gameObject.SetActive(false);return false;}
            return true;
        }
        private void Update()
        {
            if(!RefreshVisibility())return;
            bool ready=battleOwner==null||battleOwner.DeveloperReady;if(open&&lastReady!=ready){lastReady=ready;Render();}
            if(Keyboard.current?.escapeKey.wasPressedThisFrame==true&&(open||picking)){Close();return;}
            if(!picking)return;
            Vector2 point=default;bool clicked=false;
            if(Mouse.current?.leftButton.wasPressedThisFrame==true){point=Mouse.current.position.ReadValue();clicked=true;}
            if(Touchscreen.current!=null)foreach(var touch in Touchscreen.current.touches)if(touch.press.wasPressedThisFrame){point=touch.position.ReadValue();clicked=true;break;}
            if(!clicked)return;
            if(EventSystem.current!=null){hits.Clear();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=point},hits);if(hits.Any(h=>h.gameObject!=shield))return;}
            GridPosition p;bool found=battleOwner!=null ? battleOwner.PreviewBoard.Pick(point,out p) : baseOwner.DeveloperPick(point,out p);
            if(!found)return;cell=p;
            if(target.Kind==DeveloperTargetKind.Unit)
            {
                var units=battleOwner.PreviewSimulation.Soldiers.Where(s=>s.Cell==p&&!s.Escaped).OrderBy(s=>s.Id).ToArray();
                if(units.Length==0){message="此格没有单位；继续点选目标（Esc取消）";return;}
                if(units.Length==1){ApplyUnit(units[0].Id);return;}
                message="此格有多个单位，请选择要施加“"+target.Title+"”的单位。";picking=false;open=true;panel.gameObject.SetActive(true);Render();return;
            }
            string title=target.Title;bool ok=battleOwner!=null ? battleOwner.DeveloperEdit(b=>target.ApplyBattleCell(true,b,p,battleOwner.ControlledSide),title) : baseOwner.DeveloperEdit(s=>target.ApplyBaseCell(true,s,p),title);
            FinishTarget(ok,title);
        }
        private void Render()
        {
            var battle=battleOwner?.PreviewSimulation;bool realtime=battleOwner?.IsRealtimeCampaign==true;var unit=battle?.Soldier(unitId);targetText.fontSize=17;
            targetText.text="单位："+(unit==null ? "未选" : battle.UnitName(unit)+" #"+unit.Id+" 生命 "+CombatNumbers.RemainingHealth(unit.Health)+"/"+unit.Stats.Health.ToString("0"))+"\n格子："+(cell.HasValue ? cell.Value.ToString() : "未选")+"；"+(target.Kind==DeveloperTargetKind.None ? "先选择效果" : "待执行："+target.Title);
            instructionText.text=page==4 ? "编辑对话与剧情；保存立即生效，不修改游戏进度。" : "先选效果，再点地图目标；同格多人时再选具体单位。";
            float offset=body.anchoredPosition.y;foreach(Transform child in body.Cast<Transform>().ToArray()){child.gameObject.SetActive(false);UiPrefabBinding.DestroyView(child.gameObject);}y=8;
            if(target.Kind==DeveloperTargetKind.Unit&&cell.HasValue)
            {
                Line("已选效果："+target.Title+"；选择此格内的具体单位。",60);
                foreach(var u in battle.Soldiers.Where(u=>u.Cell==cell.Value&&!u.Escaped).OrderBy(u=>u.Id))
                {int id=u.Id;Add(battle.UnitName(u)+" #"+id+" · 生命 "+u.Health.ToString("0")+"/"+u.Stats.Health.ToString("0"),()=>ApplyUnit(id));}
                Add("取消此效果",()=>{target.Cancel();cell=null;Render();});
            }
            else if(page==0)
            {
                if(battle==null)Line("单位工具在战斗中使用。先选效果，再点击目标单位。",70);
                else
                {
                    Add("击倒 → 点目标（忽略无敌，不触发即死）",()=>Unit(DeveloperUnitAction.KnockDown,"击倒"));
                    Add("100伤害 → 点目标（忽略防御）",()=>Unit(DeveloperUnitAction.Damage100,"100伤害"));
                    Add("回满生命 → 点目标",()=>Unit(DeveloperUnitAction.Heal,"回满生命"));
                    Add(realtime?"复活队长 · 即时模式待接入":"满血复活队长 → 点目标",()=>Unit(DeveloperUnitAction.Revive,"复活队长"),!realtime);
                    Add("切换无敌 → 点目标",()=>Unit(DeveloperUnitAction.Invulnerable,"切换无敌"));
                    Add("切换禁止移动 → 点目标",()=>Unit(DeveloperUnitAction.Immobile,"切换禁止移动"));
                    Line(realtime?"禁行禁止新步进；已预约的半步完成后仍可攻击。复活待接入。":"禁止移动仍可攻击；复活将原部下移回原队，不补发兵力。",70);
                }
            }
            else if(page==1)
            {
                Line(realtime?"即时模式临时改地形待接入，避免通行缓存与预约不同步。":"临时改格子；关闭开发者模式或清除测试效果可恢复。世界地图不使用地形。",70);
                foreach(TerrainKind kind in Enum.GetValues(typeof(TerrainKind))){var k=kind;Add("临时改为："+DeveloperTerrain.Name(k)+(realtime?" · 待接入":""),()=>Arm(()=>target.Terrain(k)),!realtime);}
                Add("恢复全部临时地形和测试效果",Clear);
            }
            else if(page==2)
            {
                Add("立即强制胜利（我方）",()=>Battle(b=>b.DeveloperWin(true),"已强制胜利，按正常规则结算"),battle?.Phase==BattlePhase.Fighting);
                Add(realtime?"下一轮 · 仅旧回合验证":"下一轮",()=>{battleOwner?.AdvanceOne();message="已请求推进一轮";Render();},!realtime&&battle?.Phase==BattlePhase.Fighting);
                Add("所有队长技能立即冷却完毕"+(realtime?" · 待接入":""),()=>Battle(b=>b.DeveloperReadySkills(true),"技能已冷却"),!realtime&&battle?.Phase==BattlePhase.Fighting);
                Add("战争迷雾："+(battle?.Scenario.FogEnabled==true ? "开启" : "关闭"),()=>Battle(b=>{b.Scenario.FogEnabled=!b.Scenario.FogEnabled;b.Vision.Refresh();return true;},"已切换战争迷雾"),battle!=null);
                Add("生成兵种："+BattleSimulation.KindName(spawnKind),()=>{spawnKind=spawnKind==SoldierKind.Melee ? SoldierKind.Ranged : spawnKind==SoldierKind.Ranged ? SoldierKind.Cavalry : SoldierKind.Melee;Render();},!realtime);
                Add(realtime?"生成临时友军 · 即时模式待接入":"生成临时友军 → 点空格",()=>Arm(()=>target.Spawn(spawnKind)),!realtime&&battle!=null);
                Line("临时友军不消耗或增加基地兵力、军需；清除效果后消失。胜利仍检查主角是否获救。",80);
                Add("清除全部测试效果",Clear);
            }
            else if(page==3)
            {
                Line("经营测试使用当前基地进度，正常存档会保存资源和事件变更。",65);
                Add("获得营造图册1本（不重置设施）",()=>Base(s=>{BaseBackpack.Receive(s,BaseBackpack.Blueprint,1);return true;},"已追加测试营造图册"),baseOwner!=null);
                Add("切换营造入门剧情条件",()=>Base(s=>{if(s.Backpack.StoryFlags.Contains(BaseBackpack.Story))s.Backpack.StoryFlags.Remove(BaseBackpack.Story);else s.Backpack.StoryFlags.Add(BaseBackpack.Story);s.Backpack.Revision++;return true;},"已切换剧情条件；道具与升级状态保留"),baseOwner!=null);
                Add("立即召唤一次入侵（坚守30轮）",()=>Base(s=>BaseDeveloperTools.SummonInvasion(true,s),"测试入侵已出现，前往左侧迎战"),baseOwner!=null);
                Add("停用产出 → 点粮田或工坊",()=>Arm(()=>target.Production(false)),baseOwner?.AtBase==true);
                Add("开启产出 → 点粮田或工坊",()=>Arm(()=>target.Production(true)),baseOwner?.AtBase==true);
                Add("粮草、原料、钱币各增加100",()=>Base(s=>{s.Inventory.Food=Math.Min(100000000,s.Inventory.Food+100);s.Inventory.Materials=Math.Min(100000000,s.Inventory.Materials+100);s.Inventory.Coins=Math.Min(100000000,s.Inventory.Coins+100);return true;},"测试资源已添加"),baseOwner!=null);
                Add("所有基地伤员恢复全部生命",()=>Base(s=>{foreach(var l in s.Leaders.Where(l=>!l.Away&&l.Health>0))l.Health=l.MaxHealth;foreach(var w in s.Workers.Where(w=>!w.Away&&w.Health>0))w.Health=w.MaxHealth;return true;},"基地伤员已恢复生命；当日停工记录保留"),baseOwner!=null);
                Add("恢复临时地形、开启全部设施产出",Clear,baseOwner!=null);
            }
            else RenderStoryText();
            body.sizeDelta=new Vector2(540,Mathf.Max(300,y));Canvas.ForceUpdateCanvases();body.anchoredPosition=new Vector2(0,Mathf.Clamp(offset,0,Mathf.Max(0,y-300)));resultText.text=message;
        }
        private void Unit(DeveloperUnitAction action,string title)=>Arm(()=>target.Unit(action,title));
        private void Battle(Func<BattleSimulation,bool> edit,string text){message=battleOwner?.DeveloperEdit(edit,text)==true ? text : "操作未执行：需在轮次之间选择适用单位／空格；战斗结束后不能修改。";Render();}
        private void Base(Func<BaseState,bool> edit,string text){message=baseOwner?.DeveloperEdit(edit,text)==true ? text : "操作未执行：仅限和平地区；设施、人物和出入口的占地不能覆盖。";Render();}
        private void Clear(){if(battleOwner!=null)battleOwner.ClearDeveloperEffects();else baseOwner?.ClearDeveloperEffects();message="已清除临时测试效果；已经造成的伤害、战果和资源变更保留。";Render();}
        private void Add(string title,Action action,bool enabled=true){var b=Button(body,title,new Vector2(0,-y-23),new Vector2(520,46),action,new Vector2(.5f,1));b.interactable=enabled&&(battleOwner==null||battleOwner.DeveloperReady);y+=54;}
        private void Line(string text,float height){Text(body,text,new Vector2(0,-y-height/2),new Vector2(520,height),17,new Vector2(.5f,1));y+=height;}
        private static RectTransform Rect(Transform parent,string name,Vector2 anchor,Vector2 position,Vector2 size)
        {var r=UiPrefabBinding.Rect(name,parent);r.anchorMin=r.anchorMax=anchor;r.anchoredPosition=position;r.sizeDelta=size;return r;}
        private static RectTransform Box(Transform parent,string name,Vector2 anchor,Vector2 pos,Vector2 size,Color color)
        {var r=Rect(parent,name,anchor,pos,size);r.gameObject.GetOrAdd<Image>().color=color;return r;}
        private Text Text(Transform parent,string text,Vector2 pos,Vector2 size,int fontSize,Vector2? anchor=null)
        {var r=Rect(parent,"Label",anchor??Vector2.one*.5f,pos,size);var t=r.gameObject.GetOrAdd<Text>();t.font=font;t.fontSize=fontSize;t.color=Color.white;t.text=text;t.alignment=TextAnchor.MiddleLeft;t.raycastTarget=false;return t;}
        private Button Button(Transform parent,string text,Vector2 pos,Vector2 size,Action action,Vector2? anchor=null)
        {var r=Box(parent,text,anchor??Vector2.one*.5f,pos,size,new Color(.22f,.32f,.36f));var b=r.gameObject.GetOrAdd<Button>();b.targetGraphic=r.GetComponent<Image>();b.onClick.AddListener(()=>{if(RuntimeDeveloperOptions.Enabled&&Available)action();});var label=Text(r,text,Vector2.zero,size-new Vector2(14,4),18);label.alignment=TextAnchor.MiddleCenter;return b;}
        private void OnDestroy(){RuntimeDeveloperOptions.Changed-=ModeChanged;}
    }
}
