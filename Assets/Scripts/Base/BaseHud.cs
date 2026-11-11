using BaseConquest.Presentation;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using BaseConquest.Battle;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private readonly BaseSceneController owner;
        private readonly Font font;
        private readonly RectTransform root, modal, inspection, mapLabels, controls;
        private readonly Text summary, hint, feedback, regionStatus;
        private Text trackedQuest;
        private readonly Text inspectionTitle, inspectionDetails;
        private Text panelFeedback;
        private readonly Button interact;
        private readonly UnityEngine.UI.Button construction;
        private readonly Button reserveRules;
        private readonly RectTransform[] labels;
        private readonly Text[] npcMarkers;
        private readonly BaseMapObject[] labelObjects;
        private RectTransform panel, content;
        private ScrollRect panelScroll;
        private string panelViewKey;
        private float restoreScrollOffset;
        private readonly Dictionary<string, float> panelScrollOffsets = new Dictionary<string, float>();
        private float row;
        private bool developerTemplatesOpen;
        private string nearbyId;
        private string activeObject = "", activePage = "";
        private string interactionObject="";
        private static readonly HashSet<string> facilityInteractions=new HashSet<string>{"home","government","farm","workshop","barracks","warehouse","office","caravan","town_market","town_inn","town_recruit"};
        private float nextInspectionRefresh;
        public BaseJoystick Joystick { get; }
        public bool PanelOpen => modal.gameObject.activeSelf;
        public string InteractionProblem
        {
            get
            {
                if(!PanelOpen||interactionObject=="")return "";
                return !owner.PlayerAway&&BaseInteraction.CanReach(owner.State,owner.PresentedObject(interactionObject)) ? "" : "请在设施边缘一格以内、与地基同层操作";
            }
        }
        public void SetVisible(bool visible) { if (!visible) CloseResourcePopover(); Joystick.ResetInput(); root.gameObject.SetActive(visible); }
        private readonly Color ink = DemoUiTheme.Current.Ink, buttonColor = DemoUiTheme.Current.Button;

        public BaseHud(BaseSceneController owner, Transform parent, Font font)
        {
            this.owner = owner; this.font = font;
            var sceneShell = BaseBattleGateway.DemoMode ? owner.SceneUiTemplate("BaseShell") : null;
            var canvas = UiPrefabBinding.Create("BaseShell", "据点主界面 · UGUI", parent, true,
                sceneShell ?? (owner.AuthoredHud != null ? owner.AuthoredHud : BaseBattleGateway.SessionHud), sceneShell != null);
            canvas.transform.SetParent(parent, false); canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            root = canvas.GetComponent<RectTransform>();
            // 先地图文字，再操作栏、顶部栏、格子信息、模态页；动态伙伴也只能加入地图文字层。
            mapLabels = StretchLayer("MapLabels", root); mapLabels.offsetMin = new Vector2(12, ExplorationHudLayout.FooterHeight); mapLabels.offsetMax = new Vector2(-12, -ExplorationHudLayout.HeaderHeight);
            mapLabels.gameObject.GetOrAdd<RectMask2D>();
            controls = StretchLayer("Controls", root);
            var header = Box("Header", root, new Vector2(.5f, 1), new Vector2(0, -ExplorationHudLayout.HeaderHeight / 2), new Vector2(0, ExplorationHudLayout.HeaderHeight), new Color(.10f, .17f, .18f, 1));
            header.anchorMin = new Vector2(0, 1); header.anchorMax = Vector2.one;
            summary = Label(header, "正在读取据点…", Vector2.zero, Vector2.zero, 20, TextAnchor.UpperLeft);
            summary.lineSpacing=1.5f;
            summary.rectTransform.anchorMin = Vector2.zero; summary.rectTransform.anchorMax = Vector2.one;
            summary.rectTransform.offsetMin = new Vector2(18, 42); summary.rectTransform.offsetMax = new Vector2(-410, -10);
            bool authoredResources = BaseBattleGateway.DemoMode && owner.SceneUiTemplate("ResourceStrip") != null;
            MakeButton(header, "菜单", new Vector2(-90, -36), new Vector2(140, 48), OpenMenu, new Vector2(1, 1));
            DemoUiIcons.Attach(MakeButton(header, "背包", new Vector2(-270, -36), new Vector2(86, 44), ToggleBackpack, new Vector2(1, 1)),"equipment");
            if (!authoredResources)
            {
                var summaryLink = summary.gameObject.GetOrAdd<Button>(); summaryLink.targetGraphic = summary; summaryLink.onClick.AddListener(OpenResources);
                DemoUiIcons.Attach(MakeButton(header, "任务", new Vector2(-364, -36), new Vector2(86, 44), ToggleQuests, new Vector2(1, 1)), "quest");
                reserveRules = MakeButton(header, "i", new Vector2(-194, -36), new Vector2(44, 44), () => OpenResourceInfo(ClosePanel), new Vector2(1, 1));
            }
            if (!BaseBattleGateway.DemoMode)
            {
                var questBar = Box("TrackedQuest", root, new Vector2(0, 1), new Vector2(168, -118), new Vector2(320, 74), new Color(.08f, .14f, .15f, .86f));
                trackedQuest = Label(questBar, "", Vector2.zero, new Vector2(300, 66), 17, TextAnchor.UpperLeft);
                var questLink = questBar.gameObject.GetOrAdd<Button>();
                questLink.targetGraphic = questBar.GetComponent<Image>();
                questLink.onClick.AddListener(ToggleTrackedQuestBar);
                questBar.gameObject.SetActive(false);
            }
            regionStatus = Label(header, "", new Vector2(220, 17), new Vector2(404, 28), 17, TextAnchor.MiddleLeft, new Vector2(0, 0));
            regionStatus.raycastTarget=true;var threatLink=regionStatus.gameObject.GetOrAdd<Button>();threatLink.targetGraphic=regionStatus;threatLink.onClick.AddListener(()=>{if(owner.State!=null&&!owner.Busy)OpenThreatOverview();});
            feedback = Label(header, "", Vector2.zero, Vector2.zero, 17);
            feedback.rectTransform.anchorMin = Vector2.zero; feedback.rectTransform.anchorMax = new Vector2(1, 0);
            feedback.rectTransform.offsetMin = new Vector2(446, 3); feedback.rectTransform.offsetMax = new Vector2(-250, 31);
            feedback.horizontalOverflow = HorizontalWrapMode.Wrap; // 单行高度截断，完整反馈仍在功能页下方可看。
            var stick = Box("Joystick", controls, new Vector2(0, 0), new Vector2(125, 118), new Vector2(160, 160), new Color(0.93f, 0.95f, 0.88f, 0.72f));
            var knob = Box("Handle", stick, Vector2.one * 0.5f, Vector2.zero, new Vector2(64, 64), new Color(0.57f, 0.73f, 0.67f, 0.9f)); knob.GetComponent<Image>().raycastTarget = false;
            Joystick = stick.gameObject.GetOrAdd<BaseJoystick>(); Joystick.Handle = knob;
            Label(stick, "移动", new Vector2(0, -106), new Vector2(160, 32), 18, TextAnchor.MiddleCenter);
            interact = MakeButton(controls, "接近对象", new Vector2(-140, 118), new Vector2(200, 74), owner.Interact, new Vector2(1, 0));
            construction = MakeButton(controls, "战前建造", new Vector2(-360, 118), new Vector2(180, 64), owner.ArmConstruction, new Vector2(1, 0));
            construction.gameObject.SetActive(false);
            hint = Label(controls, "", new Vector2(0, 30), new Vector2(730, 50), 18, TextAnchor.MiddleCenter, new Vector2(0.5f, 0));
            labelObjects = owner.DisplayObjects.Where(o=>!BaseEnvironment.Decorative(o)&&o.Id!="demo_sword_prop").ToArray(); labels = new RectTransform[labelObjects.Length];npcMarkers=new Text[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                labels[i] = Label(mapLabels, labelObjects[i].Name.Replace("伙伴 · ", ""), Vector2.zero, new Vector2(180, 36), 18, TextAnchor.MiddleCenter).rectTransform;
                var marker=Label(labels[i],"",new Vector2(0,33),new Vector2(42,42),34,TextAnchor.MiddleCenter);marker.name="NpcStatusMarker";marker.color=new Color(1,.78f,.12f,1);marker.fontStyle=FontStyle.Bold;npcMarkers[i]=marker;
            }
            inspection = Box("CellInformation", root, new Vector2(0.5f, 0), new Vector2(0, 123), new Vector2(740, 180), new Color(0.969f, 0.965f, 0.925f, 0.98f));
            inspectionTitle = Label(inspection, "", new Vector2(-30, 64), new Vector2(630, 34), 22);
            inspectionDetails = Label(inspection, "", new Vector2(0, -7), new Vector2(695, 120), 18, TextAnchor.UpperLeft);
            MakeButton(inspection, "×", new Vector2(340, 65), new Vector2(42, 38), owner.ClearSelection);
            var dismissInspection=inspection.gameObject.GetOrAdd<Button>();dismissInspection.targetGraphic=inspection.GetComponent<Image>();dismissInspection.onClick.AddListener(owner.ClearSelection);
            inspection.gameObject.SetActive(false);
            modal = Box("ModalShade", root, Vector2.one * 0.5f, Vector2.zero, Vector2.zero, new Color(0.02f, 0.06f, 0.07f, 0.80f));
            modal.anchorMin = Vector2.zero; modal.anchorMax = Vector2.one; modal.offsetMin = modal.offsetMax = Vector2.zero; modal.gameObject.SetActive(false);
            BuildConstructionPalette();
            // EventSystem 不能挂在 canvas 下：SetVisible(false) 会禁用 canvas，连带停掉整个 UI 的事件派发
            // （世界地图隐藏主 HUD 后所有按钮失效即由此引起）。挂在 canvas 之外的父级上。
            if (Application.isPlaying && EventSystem.current == null) { var eventSystem = new GameObject("BaseEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)); eventSystem.transform.SetParent(owner.transform, false); eventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions(); }
            UiPrefabBinding.Complete(canvas);
            BindResourceStrip(header);
            foreach (var label in labels) StyleMapName(label.GetComponent<Text>());
        }
        private static void StyleMapName(Text text)
        {
            text.color = DemoUiTheme.Current.Panel;
            var outline = text.gameObject.GetOrAdd<Outline>();
            outline.effectColor = DemoUiTheme.Current.Line;
            outline.effectDistance = new Vector2(1.25f, -1.25f);
            outline.useGraphicAlpha = true;
        }
        private RectTransform StretchLayer(string name, Transform parent)
        {
            var layer = Rect(name, parent, Vector2.one * .5f, Vector2.zero, Vector2.zero);
            layer.anchorMin = Vector2.zero; layer.anchorMax = Vector2.one; layer.offsetMin = layer.offsetMax = Vector2.zero; return layer;
        }
        private void PlaceMapLabel(RectTransform label, Vector3 world,float raise=0)
        {
            var screen = owner.WorldCamera.WorldToScreenPoint(world);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(mapLabels, screen, root.GetComponent<Canvas>().worldCamera, out var local);
            local.y+=raise;
            label.anchoredPosition = local;
            label.gameObject.SetActive(screen.z > 0 && local.y - label.rect.height * .5f >= mapLabels.rect.yMin && local.y + label.rect.height * .5f <= mapLabels.rect.yMax);
        }
        private void PlaceCompanionLabel(RectTransform label,double x,double y,float raise=1.8f)
        {
            // Anchor to the actual feet, not terrain sampled beyond the character across a ledge.
            PlaceMapLabel(label,owner.WorldPoint(x,y)+owner.WorldCamera.transform.up*raise,label.rect.height*.5f+6);
        }
        private RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var obj = UiPrefabBinding.Rect(name, parent).gameObject;
            var rect = obj.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = anchor; rect.pivot = Vector2.one * 0.5f;
            rect.anchoredPosition = position; rect.sizeDelta = size; return rect;
        }
        private RectTransform Box(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, Color color)
        { var rect = Rect(name, parent, anchor, position, size); var image=rect.gameObject.GetOrAdd<Image>();image.color=color;if(color.a>.8f)DemoUiTheme.Current.Surface(image);return rect; }
        // 需要精确颜色的小色块（圆点、下划线、标记）：走半透明创建避开 Surface，再把颜色写成目标值。
        private RectTransform Solid(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, Color color)
        { var rect = Box(name, parent, anchor, position, size, new Color(color.r, color.g, color.b, .78f)); rect.gameObject.GetComponent<Image>().color = color; return rect; }
        private Text Label(Transform parent, string text, Vector2 position, Vector2 size, int fontSize = 20, TextAnchor alignment = TextAnchor.MiddleLeft, Vector2? anchor = null)
        {
            var rect = Rect("Text", parent, anchor ?? Vector2.one * 0.5f, position, size); var label = rect.gameObject.GetOrAdd<Text>();
            label.font = font; label.fontSize = fontSize; label.text = text; label.color = ink; label.alignment = alignment;
            label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate; label.raycastTarget = false;
            var shadow = rect.gameObject.GetOrAdd<Shadow>(); shadow.effectColor = new Color(0, 0, 0, 0.8f); shadow.effectDistance = new Vector2(1, -1);
            return label;
        }
        private Button MakeButton(Transform parent, string text, Vector2 position, Vector2 size, Action action, Vector2? anchor = null)
        {
            var rect = Box(text, parent, anchor ?? Vector2.one * 0.5f, position, size, buttonColor);
            var button = rect.gameObject.GetOrAdd<Button>(); button.targetGraphic = rect.GetComponent<Image>();
            var colors = button.colors; colors.highlightedColor = new Color(0.8f, 0.96f, 0.89f); colors.pressedColor = new Color(0.6f, 0.81f, 0.74f); colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.5f); button.colors = colors;
            DemoUiTheme.Current.StyleButton(button);
            button.onClick.AddListener(() => { if (!owner.Busy) action(); });
            Label(rect, text, Vector2.zero, size - new Vector2(12, 6), DemoUiMetrics.FontSubhead, TextAnchor.MiddleCenter); return button;
        }
        public void RefreshSummary()
        {
            var s = owner.State; feedback.text = owner.Message;feedback.gameObject.SetActive(owner.Message.Contains("失败")||owner.Message.Contains("未保存"));
            bool selectingHero = heroChoiceLayer != null && heroChoiceLayer.gameObject.activeSelf;
            summary.transform.parent.gameObject.SetActive(!selectingHero && !owner.HomeRebuildOverview && (!PanelOpen || panelViewKey != "npc-dialogue"));
            if (reserveRules != null) reserveRules.interactable = s != null && !s.Aftermath.GameOver && !owner.Busy;
            if (panelFeedback != null) panelFeedback.text = owner.Message;
            owner.InvalidateObjects();
            if (resourceStrip != null) RefreshResourceStrip(s);
            if (s == null) { summary.text = owner.Busy ? "正在读取据点…" : "没有有效进度，请选择新开局或恢复备份"; regionStatus.text = ""; interact.interactable = false; construction.gameObject.SetActive(false); Joystick.gameObject.SetActive(false); mapLabels.gameObject.SetActive(false); return; }
            mapLabels.gameObject.SetActive(!selectingHero && !PanelOpen && !owner.RebuildPlacementMode); controls.gameObject.SetActive(!selectingHero && !PanelOpen);
            if (resourceStrip == null)
            {
                var delta=BaseDailyDelta.Read(s);summary.supportRichText=true;
                string Amount(string name,int count,int change)=>(DemoUiIcons.Load(name=="粮食"?"food":name=="原料"?"materials":name=="钱币"?"coins":"supplies")!=null?"　　":"")+name+" "+count+"（<color="+(change>0?"#a96354":change<0?"#426b50":"#718078")+">"+change.ToString("+0;-0;0")+"</color>）";
                summary.text=BaseCampaign.LocationName(s)+(BaseDemoPrologue.Village(s)?"":" · 第 "+s.Day+" 天")+"   "+Amount("粮食",s.Inventory.Food,delta.Food)+"   "+Amount("原料",s.Inventory.Materials,delta.Materials)+"   "+Amount("钱币",s.Inventory.Coins,delta.Coins)+"\n"+Amount("军需",s.Inventory.Equipment.Where(e=>e.Product=="supply").Sum(e=>e.Count),delta.Supply)+"   兵力：剑士 "+s.Workers.Where(w=>w.Kind==BaseTroopKind.Melee).Sum(w=>w.Count)+"／弓兵 "+s.Workers.Where(w=>w.Kind==BaseTroopKind.Ranged).Sum(w=>w.Count)+"／骑兵 "+s.Workers.Where(w=>w.Kind==BaseTroopKind.Cavalry).Sum(w=>w.Count);
                if(BaseDemoRuins.Enabled(s))summary.text+=" · 体力 "+BaseRuinsRewards.Energy(s)+"/"+BaseRuinsRewards.Maximum(s);
            }
            if(panel!=null&&panelViewKey!="npc-dialogue"){var header=summary.transform.parent;header.SetAsLastSibling();}
            int raids = s.Campaign.Events.Count(e => e.Kind == "defense" && e.Status == "pending" && e.AvailableDay <= s.Day);
            regionStatus.text = BaseDemo.Active(s) ? (raids>0 ? "入侵已到达" : "") : s.Campaign.ThreatVersion>0 ? BaseThreats.Warning(s) : raids > 0 ? "入侵 " + raids + "：左侧迎战／过夜自动处理" : "";
            Joystick.gameObject.SetActive(Application.isMobilePlatform && !PanelOpen && !owner.Busy && !owner.PlayerAway && !s.Campaign.HasActiveBattle);
            if(BaseDemoPrologue.Village(s)){introObjectiveKey=-1;RefreshIntroObjective();}
            if (!owner.DeploymentWorldActive) RefreshTrackedQuest();
            nearbyId = null; RefreshMovement(); RefreshInspection();
        }
        private int introObjectiveKey=-1;
        public void RefreshIntroObjective()
        {
            var s=owner.State;if(!BaseDemoPrologue.Village(s)||!BaseDemoIntro.Active(s))return;
            var i=s.Demo.Intro;int key=i.Step*100+i.FarmWorkers*4+(i.Walked>0?1:0)+(i.Zoomed?2:0);if(key==introObjectiveKey)return;introObjectiveKey=key;
            if (resourceStrip != null) { RefreshResourceStrip(s); return; }
            int food=BaseDemoIntro.FarmOutput(s),need=BaseDemoIntro.VillageFood(s),net=food-need;string color=net>0?"#a96354":net<0?"#426b50":"#718078";
            summary.text=BaseScriptNames.Village+" · "+(BaseDemoIntro.Daily(s)?"清晨巡村":"风起村头")+"   粮食 "+s.Inventory.Food+"（<color="+color+">"+net.ToString("+0;-0;0")+"</color>） 日产 "+food+"／口粮 "+need+"  工坊 "+BaseDemoIntro.SmithWork(s)+"工点";
        }
        public void RefreshMovement()
        {
            RefreshIntroObjective();var nearby = owner.Nearby;
            interact.gameObject.SetActive(!owner.ConstructionMode&&nearby!=null&&!PanelOpen);
            bool canBuild=owner.State!=null&&BaseDemoFlow.Active(owner.State)&&owner.State.Campaign.Location=="perimeter"&&!owner.State.Campaign.HasActiveBattle&&!owner.State.Aftermath.GameOver&&!owner.Busy&&!PanelOpen&&!owner.ConstructionMode;
            construction.gameObject.SetActive(canBuild);
            construction.interactable=canBuild;
            Joystick.gameObject.SetActive(Application.isMobilePlatform&&!PanelOpen&&!owner.HomeRebuildOverview&&!owner.Busy&&!owner.PlayerAway&&owner.State?.Campaign.HasActiveBattle!=true);
            interact.interactable = nearby != null && !PanelOpen && !owner.Busy;
            string id = nearby?.Id ?? "";
            if (nearbyId != id)
            {
                nearbyId = id;
                interact.GetComponentInChildren<Text>().text = nearby == null ? "接近对象后交互" : (nearby.Kind=="npc"||owner.State.Leaders.Any(l=>l.Id==nearby.Id)||nearby.Id.StartsWith("demo_villager")||nearby.Id=="prologue_ahe") ? "交谈 · " + nearby.Name.Replace("伙伴 · ","").Replace(" · 交谈","") : (nearby.Kind=="facility"?"管理 · ":nearby.Id.Contains("exit")||nearby.Id.Contains("travel")||nearby.Id.Contains("trigger")?"前往 · ":"使用 · ")+nearby.Name;
                hint.text = owner.PlayerAway ? "主角外出中：入侵信息查看敌情，兵营查看外出队伍" : nearby == null ? "设施需在边缘一格内且同层；伙伴靠近对话\n电脑：WASD／方向键移动，E交互 · B背包 · N任务 · M地图" : "附近：" + nearby.Name + "\n点击右侧交互，或按 E";
            }
            for (int i = 0; i < labels.Length; i++)
            {
                var obj = owner.PresentedObject(labelObjects[i].Id); labels[i].gameObject.SetActive(obj != null);
                if (obj == null) continue;
                labels[i].GetComponent<Text>().text = obj.Name.Replace("伙伴 · ", "");
                npcMarkers[i].text=BaseDemoNpcs.StatusMarker(owner.State,obj.Id);npcMarkers[i].gameObject.SetActive(npcMarkers[i].text!=""&&owner.SceneObjectVisible(obj));
                // 骑马的单位渲染体量放大到 1.42 倍，名牌要跟着抬高，否则会压在马上。
                if(obj.Kind=="npc"||obj.Id.StartsWith("partner"))PlaceCompanionLabel(labels[i],obj.X,obj.Y,BaseConquest.Presentation.PixelDemoArt.Mounted(obj.AssetId)?2.6f:1.8f);
                else PlaceMapLabel(labels[i],owner.WorldPoint(obj.X,obj.Y+obj.Height/2+.6));
            }
            RefreshCompanionLabels();
            hint.gameObject.SetActive(owner.SelectedCell == null && !PanelOpen && !owner.ConstructionMode);RefreshConstructionPalette();
        }
        public void RefreshInspection(bool force = true)
        {
            bool visible = owner.State != null && owner.SelectedCell != null && !PanelOpen && !owner.Busy && inspectionNotice.Visible(Time.unscaledTime);
            inspection.gameObject.SetActive(visible);
            if (!visible || !force && Time.unscaledTime < nextInspectionRefresh) return;
            nextInspectionRefresh = Time.unscaledTime + 0.2f;
            var cell = owner.SelectedCell; var facility = owner.DisplayObjects.FirstOrDefault(o => !BaseEnvironment.Decorative(o) && cell.X < o.X + o.Width / 2 && cell.X + 1 > o.X - o.Width / 2 && cell.Y < o.Y + o.Height / 2 && cell.Y + 1 > o.Y - o.Height / 2);
            if(owner.State.Campaign.Location=="world")
            {
                inspectionTitle.text="世界坐标（"+cell.X+", "+cell.Y+"） · "+(facility?.Name??"区域导航");
                inspectionDetails.text=facility==null ? "世界地图用于行走与选择区域。接近基地或敌军据点节点，再交互前往该区域。" : facility.Id=="travel_base" ? "基地节点；接近后交互返营。" : BaseThreats.Site(owner.State)?.Owner=="player" ? "己方据点入口；占领状态已保存，仍可进入区域。已出发的敌军仍会抵达。" : "敌方据点入口；进入区域查看驻军，再在原地图进攻。";
                return;
            }
            string engineering=FortificationInfo.Description(BaseBattlefield.Board(owner.State).Cell(new GridPosition(cell.X,cell.Y)));
            if(engineering!=""){inspectionTitle.text="格子（"+cell.X+", "+cell.Y+"） · 工程";inspectionDetails.text=engineering;return;}
            inspectionTitle.text = "格子（" + cell.X + ", " + cell.Y + "） · " + (facility?.Name ?? TerrainLabel(cell.X,cell.Y));
            if (facility != null)
            {
                if (!owner.AtBase) inspectionDetails.text = facility.Id == "travel_base" ? "基地入口；接近后交互返回。" : facility.Id=="travel_world" ? "区域出口；接近后回到世界地图原节点，保持本队与军需。" : facility.Id == "campaign_defense" ? "查看入侵并预览迎战；确认才抽调人员。" : facility.Id == "campaign_attack" ? owner.State.Campaign.Location=="world" ? "区域入口；接近后进入据点区域。世界地图仅用于区域导航。" : BaseThreats.Site(owner.State)?.Owner=="player" ? "本区域已归己方，停止产生新入侵；接近后查看据点状态。" : "查看本区域驻军与威胁，使用真实队伍与军需在本图进攻。" : facility.Id=="town_quest" ? "镇务官：接取城镇防务委托，完成后交付领取营造图册。" : facility.Id=="town_market" ? "交易摊位：买卖粮草、原料和装备；接近后查看当日供货和报价。" : facility.Id=="town_recruit" ? (BaseDemoFlow.Active(owner.State)?"募兵处：付费招募三种普通兵，加入基地；今日即可安排生产。":"募兵处：付费招募三种普通兵，加入基地；今日不生产，次日正常分配。") : "城镇交互点。";
                else { var info = BaseFacilityInfo.Read(owner.State, facility.Id); inspectionDetails.text = (info.Administrator == "" ? "" : "管理员：" + info.Administrator + "\n工人：" + info.Workers + "\n") + info.Production + "\n" + info.Status; }
            }
            else
            {
                var people = owner.Companions.Agents.Where(a => a.Visible && (int)Math.Floor(a.Position.X) == cell.X && (int)Math.Floor(a.Position.Y) == cell.Y)
                    .Select(a => a.Name + (a.TemporarilyDetached?"（暂离·占位）":a.Following ? "（随行）" : a.FacilityId == "" ? "（闲置）" : "（" + BaseEconomy.FacilityName(a.FacilityId) + (a.Arrived ? "管理员）" : "，前往中）"))).ToList();
                if ((int)Math.Floor(owner.State.PlayerX) == cell.X && (int)Math.Floor(owner.State.PlayerY) == cell.Y) people.Add("主角");
                inspectionDetails.text = "地面：" + TerrainLabel(cell.X,cell.Y) + "\n人物：" + (people.Count == 0 ? "无人" : string.Join("、", people)) + "\n点击格子查看；接近对象后可交互";
            }
        }
        private string TerrainLabel(int x,int y)
        {
            var cell=BaseBattlefield.Board(owner.State).Cell(new GridPosition(x,y));
            string terrain=cell?.Terrain==TerrainKind.Water ? "水域 · 不可通行" : cell?.Terrain==TerrainKind.Wall ? "墙体 · 不可通行" : cell?.Terrain==TerrainKind.Building ? "建筑 · 不可通行" : cell?.Terrain==TerrainKind.Swamp ? "沼泽" : "空地";
            if(cell?.Terrain==TerrainKind.Ground){var surface=GroundSurfaceRules.Resolve(cell.Surface,owner.State.Campaign.Location,x,y);terrain=surface==GroundSurface.Grass ? "草地" : surface==GroundSurface.Sand ? "沙场" : surface==GroundSurface.Road ? "道路" : surface==GroundSurface.Cobble ? "鹅卵石路" : surface==GroundSurface.DirtPath ? "泥径" : "空地";}
            return terrain+" · "+BaseBattlefield.Board(owner.State).HeightDescription(new GridPosition(x,y));
        }
        private void BeginPanel(string title, string viewKey = null)
        {
            CloseResourcePopover();
            // 刷新设置仍在同一逻辑页；用内容偏移保留正在操作的行，不随内容总高度变化。
            if (PanelOpen && panelScroll != null && panelViewKey != null)
                panelScrollOffsets[panelViewKey] = Mathf.Max(0, panelScroll.content.anchoredPosition.y);
            if (BaseBattleGateway.DemoMode && owner.SceneUiTemplate("CommonPanel") != null)
            {
                var view = OpenScenePanel("CommonPanel", viewKey ?? title);
                panelScrollOffsets.TryGetValue(panelViewKey, out restoreScrollOffset);
                Ui<Text>(view, "Title").text = title;
                var close = Ui<Button>(view, "Close");
                close.onClick.AddListener(ClosePanel);
                close.interactable = !(BaseDemoIntro.Daily(owner.State) && owner.State.Demo.Intro.Step == 0)
                    && owner.State?.Aftermath.GameOver != true && !BaseBattleGateway.MainMenu && !BaseBattleLoot.Pending(owner.State);
                panelScroll = Ui<ScrollRect>(view, "Viewport");
                content = panelScroll.content;
                panelFeedback = Ui<Text>(view, "Feedback");
                panelFeedback.gameObject.SetActive(false);
                row = 0;
                RefreshSummary();
                return;
            }
            panelViewKey = viewKey ?? title;
            panelScrollOffsets.TryGetValue(panelViewKey, out restoreScrollOffset);
            if (panel != null)
            {
                ClearPanelSelection();
                panel.gameObject.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(panel.gameObject);
                else UnityEngine.Object.DestroyImmediate(panel.gameObject);
            }
            modal.GetComponent<Image>().color=DemoUiTheme.Current.Shade;dialogueContinue=null;
            modal.gameObject.SetActive(true); Joystick.ResetInput(); Joystick.gameObject.SetActive(false);
            panel = UiPrefabBinding.Create(UiPrefabBinding.PageKey(title,viewKey), title, modal,false,owner.PageTemplate(UiPrefabBinding.PageKey(title,viewKey))).GetComponent<RectTransform>();
            panel.anchorMin=panel.anchorMax=panel.pivot=Vector2.one*.5f;panel.anchoredPosition=new Vector2(0,-36);panel.sizeDelta=new Vector2(960,540);panel.gameObject.GetOrAdd<Image>().color=DemoUiTheme.Current.Panel;
            Label(panel, title, new Vector2(-85, 230), new Vector2(710, 55), DemoUiMetrics.FontPanelTitle);
            var closeButton=MakeButton(panel, "关闭", new Vector2(390, 230), new Vector2(130, 50), ClosePanel);
            closeButton.interactable = !(BaseDemoIntro.Daily(owner.State)&&owner.State.Demo.Intro.Step==0) && owner.State?.Aftermath.GameOver != true && !BaseBattleGateway.MainMenu && !BaseBattleLoot.Pending(owner.State);
            var viewport = Box("Viewport", panel, Vector2.one * 0.5f, new Vector2(0, -28), new Vector2(900, 440), DemoUiTheme.Current.Panel); viewport.gameObject.GetOrAdd<RectMask2D>();
            content = Rect("Content", viewport, new Vector2(0.5f, 1), Vector2.zero, new Vector2(880, 440)); content.pivot = new Vector2(0.5f, 1); content.gameObject.GetOrAdd<UiDynamicContent>(); content.GetComponent<UiVisualNode>().UseAuthoredLayout=false;
            panelScroll = viewport.gameObject.GetOrAdd<ScrollRect>(); panelScroll.viewport = viewport; panelScroll.content = content; panelScroll.horizontal = false; panelScroll.movementType = ScrollRect.MovementType.Clamped;
            panelFeedback = Label(panel, "", new Vector2(0, -256), new Vector2(880, 24), DemoUiMetrics.FontCaption);panelFeedback.gameObject.SetActive(false);
            row = 0; RefreshSummary();
        }
        private void EndPanel()
        {
            if (panel.name == "CommonPanel" && panel.GetComponent<UiPrefabBinding>() == null)
            {
                content.sizeDelta = new Vector2(content.sizeDelta.x, Mathf.Max(panelScroll.viewport.rect.height, row + 12));
                Canvas.ForceUpdateCanvases();
                panelScroll.StopMovement();
                content.anchoredPosition = new Vector2(0, Mathf.Clamp(restoreScrollOffset, 0, Mathf.Max(0, content.rect.height - panelScroll.viewport.rect.height)));
                return;
            }
            content.sizeDelta = new Vector2(880, Mathf.Max(440, row + 12));
            // 页面模板收尾会把重复节点的样式还原成原型（按钮文字居中、字色统一）。
            // 动态列表里的字号、对齐与配色是本页明确指定的，收尾前留一份、收尾后写回去。
            var texts = content.GetComponentsInChildren<Text>(true);
            var textColors = new Color[texts.Length]; var textAnchors = new TextAnchor[texts.Length]; var textSizes = new int[texts.Length];
            for (int i = 0; i < texts.Length; i++) { textColors[i] = texts[i].color; textAnchors[i] = texts[i].alignment; textSizes[i] = texts[i].fontSize; }
            var images = content.GetComponentsInChildren<Image>(true);
            var imageColors = new Color[images.Length];
            for (int i = 0; i < images.Length; i++) imageColors[i] = images[i].color;
            var outlines = content.GetComponentsInChildren<Outline>(true);
            var outlineColors = new Color[outlines.Length]; var outlineDistances = new Vector2[outlines.Length];
            for (int i = 0; i < outlines.Length; i++) { outlineColors[i] = outlines[i].effectColor; outlineDistances[i] = outlines[i].effectDistance; }
            var authored = new System.Collections.Generic.HashSet<Outline>(outlines);
            UiPrefabBinding.Complete(panel.gameObject);
            // Complete 会给「无描边但有底色」的图补一圈铜边；代码新造的小色块（圆点、下划线）不需要。
            foreach (var added in content.GetComponentsInChildren<Outline>(true))
                if (!authored.Contains(added)) UiPrefabBinding.DestroyView(added);
            for (int i = 0; i < texts.Length; i++) if (texts[i] != null) { texts[i].color = textColors[i]; texts[i].alignment = textAnchors[i]; texts[i].fontSize = textSizes[i]; }
            for (int i = 0; i < images.Length; i++) if (images[i] != null) images[i].color = imageColors[i];
            for (int i = 0; i < outlines.Length; i++) if (outlines[i] != null) { outlines[i].effectColor = outlineColors[i]; outlines[i].effectDistance = outlineDistances[i]; }
            // 内容视口在 content 之外，不会被上面的快照覆盖；模板会把它的底色还原成旧的深色，这里单独写回。
            if (panelScroll != null && panelScroll.viewport != null)
            {
                var viewportImage = panelScroll.viewport.GetComponent<Image>();
                if (viewportImage != null) viewportImage.color = DemoUiTheme.Current.Panel;
            }
            DemoUiTheme.Current.Frame(panel);
            panel.GetComponent<UiPageLayout>()?.Apply(content,content.sizeDelta.y);
            Canvas.ForceUpdateCanvases(); panelScroll.StopMovement();
            content.anchoredPosition = new Vector2(0, Mathf.Clamp(restoreScrollOffset, 0, Mathf.Max(0, content.rect.height - panelScroll.viewport.rect.height)));
        }
        private void Line(string text, float height = 22, int size = DemoUiMetrics.FontBody)
        { Label(content, text, new Vector2(0, -row - height / 2), new Vector2(850, height), size, TextAnchor.MiddleLeft, new Vector2(0.5f, 1)); row += height; }
        private Button RowButton(string text, float x, float width, Action action, bool enabled = true)
        {
            var button = MakeButton(content, text, new Vector2(x, -row - 26), new Vector2(width, 48), action, new Vector2(0.5f, 1)); button.interactable = enabled; return button;
        }
        public void ClosePanel()
        {
            if (ResourcePopoverOpen) { CloseResourcePopover(); return; }
            if (ClosePreparationHelp()) return;
            if (DialogueExitLocked) return;
            if(owner.State?.Aftermath.GameOver==true || BaseBattleGateway.MainMenu || BaseDemoIntro.Daily(owner.State)&&owner.State.Demo.Intro.Step==0) return;
            if(BaseBattleLoot.Pending(owner.State)){OpenLoot();return;}
            workerDraft=null;workerMoves.Clear();ClearPanelSelection();
            dialogueContinue=null;modal.gameObject.SetActive(false); activeObject = "";interactionObject=""; panelViewKey = null; panelScrollOffsets.Clear();
            BattlePreparationPanelClosed();
            if (owner.State != null) RefreshSummary();
            owner.NotifyPanelClosed();
        }
        private void ClearPanelSelection()
        {
            var events=UnityEngine.EventSystems.EventSystem.current;
            if(events==null)return;var selected=events.currentSelectedGameObject;
            if(selected==null||panel!=null&&selected.transform.IsChildOf(panel))events.SetSelectedGameObject(null);
        }
        public void OpenObject(string id)
        {
            if (owner.State == null) return;
            // 剧情场景里的第二份同名人物落成「人偶」：与美术检查台同一套交互——站在原地不动，
            // 只在面板里选待机／行走／攻击，不弹对白、不推进剧情、不进存档（2026-11-03 用户定）。
            if(id!=null&&id.StartsWith(BaseDemoNpcs.StoryShellPrefix,StringComparison.Ordinal))
            {
                var shell=owner.PresentedObject(id);
                if(shell==null)return;
                if(owner.PlayerAway||!BaseInteraction.CanReach(owner.State,shell)){owner.ReportMessage("请走到人物旁边一格以内再交互");return;}
                string shellArt=shell.AssetId;activeObject=id;interactionObject="";BeginPanel(shell.Name);
                Line("场景人偶 · 只播放动作，不说话也不推进剧情",42,19);
                RowButton("待机",0,800,()=>owner.SetMotionCheck(id,HeroPose.Idle));row+=60;
                if(BaseConquest.Presentation.PixelDemoArt.HasWalk(shellArt)){RowButton("行走",0,800,()=>owner.SetMotionCheck(id,HeroPose.Walk));row+=60;}
                if(BaseConquest.Presentation.PixelDemoArt.HasAttack(shellArt)){RowButton("攻击",0,800,()=>owner.SetMotionCheck(id,HeroPose.Attack));row+=60;}
                EndPanel();return;
            }
            if(owner.MapObjects.Any(o=>o.Id==id&&o.VisualOnly)){owner.ReportMessage("仅场景外观／待接入内容，当前不可使用");return;}
            if(BaseBattleLoot.Pending(owner.State)){OpenLoot();return;}
            if(owner.PlayerAway||!BaseInteraction.CanReach(owner.State,owner.PresentedObject(id))){owner.ReportMessage("请在设施边缘一格以内、与地基同层交互");return;}
            if(id.StartsWith("motion-check:"))
            {
                string art=owner.PresentedObject(id).AssetId;activeObject=id;BeginPanel(art);
                RowButton("待机",0,800,()=>owner.SetMotionCheck(id,HeroPose.Idle));row+=60;
                // 按钮跟「这套贴图有没有对应动作图集」走，不跟单位类别走（行为／表现解耦，2026-11-02 定）。
                if(BaseConquest.Presentation.PixelDemoArt.HasWalk(art)){RowButton("行走",0,800,()=>owner.SetMotionCheck(id,HeroPose.Walk));row+=60;}
                if(BaseConquest.Presentation.PixelDemoArt.HasAttack(art)){RowButton("攻击",0,800,()=>owner.SetMotionCheck(id,HeroPose.Attack));row+=60;}
                EndPanel();return;
            }
            if(BaseDemo.Active(owner.State)&&(id=="demo_rescuer"||id=="demo_chunshen"))
            {
                if(owner.State.Demo.Stage==BaseDemoStage.Waking){OpenDemoStory();return;}
                bool zhou=id=="demo_rescuer";NpcDialogue(WithInjury(owner.State,zhou?"INJ_CAMP_ZHOU":"INJ_CAMP_CHUN",new NpcLine(zhou?BaseScriptNames.LaoZhou:BaseScriptNames.ChunShen,BaseScriptNames.Portrait(zhou?"laozhou":"chunshen"),zhou?"留得青山在，总能寻回你那位同伴的。":"饿了就回营帐歇歇，哪怕天塌下来也得先填饱肚子。")),new[]{new NpcReply("回头见",ClosePanel)});return;
            }
            if(id=="town_inn"){OpenInn();return;}
            if(BaseDemo.Active(owner.State)&&id=="town_quest"){OpenDemoLord();return;}
            if(BaseDemo.Active(owner.State)&&(id=="town_guard"||id=="town_peddler"||id=="town_girl")){OpenTownChat(id);return;}
            if(BaseDemoPrologue.Village(owner.State)){if(!OpenIntroInteraction(id))OpenDemoStory();return;}
            if(OpenDemoInteraction(id))return;
            if(BaseWorldJourney.EntryForObject(owner.State,id)!=null||BaseDesignRoutes.ForObject(owner.State,id)!=null){owner.TravelConnection(id);return;}
            if(id.StartsWith("recovery_marker:")){var loss=owner.State.Aftermath.LostItems.Single(x=>x.Id==id.Substring(16));OpenCampaignPreview(loss.EventId,"full");return;}
            activeObject = id;interactionObject=facilityInteractions.Contains(id)?id:"";
            if(id=="warehouse"){OpenWarehouse();return;}
            if(id=="town_quest"){OpenQuests();return;}
            if (id=="town_market") { OpenTownMarket(); return; }
            if (id=="town_recruit") { OpenTownRecruit(); return; }
            if (id.StartsWith("exit_") || id.StartsWith("travel_") || id.StartsWith("campaign_")) { OpenTravel(id); return; }
            if (id == "home") OpenHome();
            else if (owner.State.Leaders.Any(l=>l.Id==id)) OpenPartner(id);
            else if (id == "government") { activePage = "workers"; OpenGovernment(); }
            else if (id == "office") { activeObject = "government"; activePage = "development"; OpenGovernment(); }
            else if (id == "caravan") OpenCaravan();
            else if (id == "farm" || id == "workshop") { activePage = "workers"; OpenFacility(); }
            else if (id == "barracks") { activePage = "armyinfo"; facilityPersonnelDetail = null; OpenBarracks(); }
        }
        public void OpenMenu()
        {
            interactionObject="";
            owner.StopConstruction();
            if (owner.State == null) { OpenRecovery(); return; }
            if(BaseBattleGateway.MainMenu) { OpenMainMenu(); return; }
            if(owner.State.Aftermath.GameOver) { OpenGameOver(); return; }
            if(BaseBattleLoot.Pending(owner.State)){OpenLoot();return;}
            activeObject = "menu";
            if (BaseBattleGateway.DemoMode && owner.SceneUiTemplate("CampMenu") != null)
            {
                OpenSceneCampMenu();
                return;
            }
            BeginPanel("据点菜单");
            // 阶段测试档属开发期入口（P16）：正常游玩不显示，确认开发者模式后才出现。
            if(BaseBattleGateway.DemoMode&&RuntimeDeveloperOptions.Enabled){RowButton("阶段测试档",0,800,OpenDemoStages);row+=60;}
            Line("3个手动／6个节点自动档；经营设置不会持续落盘。"+(UnityEngine.Application.isPlaying&&BaseBattleGateway.Session().HasUnsavedChanges?"\n有未手动保存的会话更改。":""),65,18);
            DemoUiIcons.Attach(RowButton("手动保存",-210,400,()=>OpenSlots(true),!owner.State.Campaign.HasActiveBattle),"save");RowButton("读取存档",210,400,()=>OpenSlots(false));row+=60;
            if(owner.State.Campaign.HasActiveBattle){RowButton("恢复当前战斗",0,800,owner.ResumeCampaign);row+=60;}
            else if(!owner.AtBase){RowButton(BaseDesignRoutes.Active(owner.State) ? "经绑定出口返回基地" : "返回基地",0,800,()=>owner.Travel("base"),!BaseDesignRoutes.Active(owner.State)||BaseDesignRoutes.Reachable(owner.State,"base")!=null);row+=60;}
            if(BaseAchievements.Active(owner.State)){RowButton("成就 · "+owner.State.Achievements.Records.Count+"/9",0,800,OpenAchievements);row+=60;}
            RowButton("领地地图 · M",-210,400,OpenLandMap,BaseLandMap.Available(owner.State));
            RowButton("日结报告",210,400,OpenReport);row+=60;
            if(BaseDemoFlow.Active(owner.State)&&!BaseDemoPrologue.Village(owner.State)&&owner.State.Campaign.Location=="perimeter"&&!BaseSceneCombat.Active(owner.State)){RowButton("战前布置工事",0,800,owner.ArmConstruction);row+=60;}
            RowButton("选项",0,800,OpenOptions);row+=60;
            RowButton("帮助",-210,400,OpenHelp);RowButton("回主菜单",210,400,owner.ToMainMenu);row+=60;
            if(BaseDemo.Active(owner.State)&&RuntimeDeveloperOptions.Enabled){RowButton("独立即时战斗测试",0,800,OpenStandaloneBattleTest);row+=60;}
            if(BaseDemo.Active(owner.State)){EndPanel();return;}
            RowButton(developerTemplatesOpen ? "开发存档 ▴" : "开发存档 ▾",0,800,()=>{developerTemplatesOpen=!developerTemplatesOpen;OpenMenu();});row+=60;
            if(developerTemplatesOpen)AddTemplates();
            EndPanel();
        }
        private void OpenStandaloneBattleTest()
        {
            if(!RuntimeDeveloperOptions.Enabled||owner.State==null||owner.State.Campaign.HasActiveBattle)return;
            BeginPanel("独立即时战斗测试");
            Line("使用正式 Demo 的即时战斗、细格碰撞、骑兵体积与伤害规则。每方额外的测试队长仅用于维持正式小队指挥链，不计入下列 8 名兵种数量。退出、失败与重选均不写剧情存档。",92,18);
            RowButton("默认 · 8 骑兵 vs 8 弓兵",0,800,()=>owner.StartStandaloneBattle(BaseStandaloneBattlePreset.CavalryVsArchers));row+=60;
            RowButton("攻守互换 · 8 弓兵 vs 8 骑兵",0,800,()=>owner.StartStandaloneBattle(BaseStandaloneBattlePreset.Swapped));row+=60;
            RowButton("混编预设 · 剑 2／弓 3／骑 3 vs 同构",0,800,()=>owner.StartStandaloneBattle(BaseStandaloneBattlePreset.Mixed));row+=60;
            RowButton("返回菜单",0,800,OpenMenu);row+=60;EndPanel();
        }
        public void OpenRecovery()
        {
            activeObject = "recovery";
            BeginPanel("开发开局不可用");Line(owner.Message,90,18);AddTemplates();EndPanel();
        }

        private void AddTemplates()
        {
            if(BaseBattleGateway.DemoMode){RowButton("开始新游戏",0,830,()=>Confirm("开始新游戏","从序章开始，并重新选择主角。",()=>owner.NewTemplate(BaseDemo.Profile)));row+=60;return;}
            if(BaseDesignRoutes.Active(owner.State)){RowButton("重新开始当前设计组合",0,830,()=>Confirm("重开设计组合","只替换当前设计的隔离实验进度？",()=>owner.NewTemplate(owner.RecommendedTemplate)));row+=60;return;}
            foreach(var entry in BaseSceneController.TemplateEntries)
            {
                string name=entry.File;
                RowButton((name==owner.RecommendedTemplate ? "推荐 · " : "")+name+" · "+entry.Title,0,830,
                    ()=>Confirm("确认开发开局","使用“"+name+"”替换本次测试进度？",()=>owner.NewTemplate(name)));
                row+=60;
            }
        }

        private void Confirm(string title, string text, Action yes)
        {
            BeginPanel(title); Line(text, 110);
            RowButton("确认", -155, 270, yes); RowButton("取消", 155, 270, OpenMenu); row += 60; EndPanel();
        }
        // 营帐的请离基地面板：只列非剧情角色，资格用 DismissProblem 统一判，确认后永久移除。
        private void OpenDismiss()
        {
            var s=owner.State;
            var people=s.Leaders.Where(l=>l.Id!=s.PlayerLeaderId&&l.Rpg!=null&&!l.Rpg.Story).OrderBy(l=>l.Id,StringComparer.Ordinal).ToArray();
            BeginPanel("营帐 · 请离基地");
            Line("请离基地即永久离开，装备与物品退回仓库，普通兵保留，此人不能再次加入。主角与剧情伙伴不可请离基地。",84,19);
            foreach(var person in people)
            {
                string id=person.Id;string problem=BaseRpg.DismissProblem(s,id);
                var record = BasePersonnel.Units(s).First(u => u.LeaderId == id);
                AddPersonnelRow(content, record, problem == "" ? RosterText("dismiss", "点击办理请离") : problem, () => ConfirmDismiss(id), problem == "");
            }
            if(people.Length==0)Line("营地暂无可以请离基地的角色。",48,19);
            RowButton("返回",0,270,OpenHome);row+=60;EndPanel();
        }
        private void ConfirmDismiss(string id)
        {
            var person=owner.State.Leaders.FirstOrDefault(l=>l.Id==id);
            if(person==null){OpenHome();return;}
            BeginPanel("确认请离基地 · "+person.Name);
            Line("永久离开：所有装备与背包物品退回基地仓库，普通兵保留；此人不能再次加入。",92,19);
            RowButton("确认请离基地",-210,400,()=>{owner.Change(next=>{if(!BaseRpg.Dismiss(next,id))throw new InvalidOperationException(BaseRpg.DismissProblem(next,id));},"已请离基地，物品已返仓");OpenHome();});
            RowButton("取消",210,400,OpenDismiss);row+=60;EndPanel();
        }
        public void OpenHome()
        {
            if(!BaseInteraction.At(owner.State,"base","home")){owner.ReportMessage("请在家边缘一格以内、与地基同层交互");return;}
            if(BaseDemo.Active(owner.State)&&owner.State.Demo.Stage==BaseDemoStage.Waking){OpenDemoStory();return;}
            if (UseFacilityPanel) { OpenHomeFacility(); return; }
            activeObject=interactionObject="home";
            var s = owner.State; var p = BaseEconomy.Preview(s); int day = s.Day;
            BeginPanel(BaseDemo.Active(s)?"营帐 · 休息与管理":"家 · 休息一天");
            if(BaseDemo.Active(s))
            {
                RowButton("休息至明天",-290,270,()=>owner.Rest(day));RowButton("营地管理",0,270,()=>{activeObject="government";activePage="workers";OpenGovernment();});RowButton("关闭",290,270,ClosePanel);row+=65;
                RowButton("仓库",0,800,OpenWarehouse,BasePartyInventory.AtWarehouse(s));row+=60;
                if(BaseDeputy.Unlocked(s)){var deputy=BaseDeputy.Leader(s);RowButton("副手："+(deputy==null?"未指定":BasePersonnel.LeaderName(deputy))+" · 更换",0,800,OpenDeputy);row+=60;}
                // 请离基地只在营帐办理（2026-11-06 裁决）：兵营名册不再提供这个入口。
                if(s.Leaders.Any(l=>l.Id!=s.PlayerLeaderId&&l.Rpg!=null&&!l.Rpg.Story)){RowButton("请离基地 · 请人永久离开",0,800,OpenDismiss);row+=60;}
                if(BaseRpg.Active(s)){var rules=s.Demo.Rpg;Line("营帐 "+rules.CampLevel+"级 · 队长名额 "+BaseRpg.Population(s)+" / "+BaseRpg.Capacity(s)+"（含主角、伙伴与外出队长）",60,20);if(rules.CampLevel<3){Line("升级：原料 "+rules.CampMaterials[rules.CampLevel-1]+"、钱币 "+rules.CampCoins[rules.CampLevel-1]+"；容量 "+rules.Capacity[rules.CampLevel],48,19);RowButton("升级营帐",0,800,()=>{owner.Change(next=>{if(!BaseRpg.UpgradeCamp(next))throw new InvalidOperationException(BaseRpg.CampUpgradeProblem(next));},"营帐已升级");OpenHome();},BaseRpg.CampUpgradeProblem(s)=="");row+=60;}}
            }
            else
            {
            RowButton("休息至明天",-290,270,()=>owner.Rest(day));RowButton("暂不休息",0,270,ClosePanel);RowButton("日结规则 i",290,270,()=>RuleInfo("日结规则",BaseExperienceRules.Home,OpenHome));row+=65;
            }
            foreach (var e in BaseCampaign.Due(s)) Line("待处理入侵：" + e.Title + "；产出为战前估算。", 64, 19);
            Line("第"+day+"天 → 第"+(day+1)+"天",48,22);
            Line("预计粮草 +" + p.FoodProduced + "；" + p.ProductLevel + "级" + BaseEconomy.ProductName(p.Product) + " +" + p.ProductsMade + "；原料 -" + p.MaterialsSpent);
            Line("明日口粮 " + p.FoodRequired + "；扣粮后库存 " + p.NextFood + (p.NextMissing > 0 ? "；缺粮 " + p.NextMissing : "；粮草充足"));
            Line("今日生产效率 " + (p.ProductionFactor * 100).ToString("0.#") + "%", 48, 18);
            EndPanel();
        }
        public void OpenReport()
        {
            if (owner.State == null) return;
            if(owner.State.Aftermath.GameOver){OpenGameOver();return;}
            activeObject = "report";interactionObject="";
            BeginPanel("日结报告");
            RowButton("关闭",0,800,ClosePanel);row+=60;DailyReportContent();EndPanel();
        }
        private void DailyReportContent()
        {
            var r=owner.State.LastReport;
            if (!owner.State.HasReport) { Line("尚未休息过。到家旁交互可预览今天的生产。", 90); CampaignReports(); return; }
            Line("第" + r.CompletedDay + "天 → 第" + r.NewDay + "天", 52, 24);
            Line("粮草：" + r.FoodBefore + " + 产粮" + r.FoodProduced + " - 口粮" + r.FoodConsumed + " = " + r.FoodAfter);
            Line("工坊：" + Math.Max(1,r.ProductLevel) + "级" + BaseEconomy.ProductName(r.Product) + " +" + r.ProductsMade + "；原料 -" + r.MaterialsSpent);
            Line("昨天生产效率：" + (r.ProductionFactor * 100).ToString("0.#") + "%");
            Line("今天口粮：需要" + r.FoodRequired + "／已供" + r.FoodConsumed + "／缺少" + r.FoodMissing);
            Line("昨日恢复生命：" + r.PeopleRecovered + "人（全员自动恢复）");
            CampaignReports();
            Line(r.FoodMissing > 0 ? "今天缺粮会降低产能，人员不会直接死亡。增加粮田人数可恢复供给。" : "今天粮草充足。关闭报告可继续安排工作。", 78);
        }
        private void ConfirmAdventure(string id,bool join)
        {
            var s=owner.State;var copy=s.Copy();var before=BaseEconomy.Preview(s);
            if(!BaseAdventure.SetMember(copy,id,join)){OpenPartner(id);return;}
            var after=BaseEconomy.Preview(copy);
            BeginPanel(join ? "确认邀请伙伴" : "确认留在基地","adventure-confirm:"+id);
            RowButton("确认",-210,400,()=>{owner.Change(next=>{if(owner.Nearby?.Id!=id||!BaseAdventure.SetMember(next,id,join))throw new InvalidOperationException("伙伴状态或距离已变化，请重新对话");},join ? "伙伴已随行，实际岗位已释放" : "伙伴已留在基地，恢复按策略安排");OpenPartner(id);});
            RowButton("取消",210,400,()=>OpenPartner(id));row+=65;
            Line(join ? "加入后跟随主角，暂不担任设施管理员。" : "离队后仍保留本人状态；今日有实际外出记录则今天不生产。",85,20);
            Line("预计日产量变化：粮 "+(after.FoodProduced-before.FoodProduced).ToString("+0;-0;0")+"／军需 "+(after.ProductsMade-before.ProductsMade).ToString("+0;-0;0"),65,20);EndPanel();
        }
        private void OpenGovernment()
        {
            if (owner.State == null || activeObject != "government") return;
            BeginPanel(BaseDemo.Active(owner.State)?"营地管理 · 库存与人员":"据点管理所 · 库存与人员", "government:" + activePage + (activePage == "army" ? ":" + armyPurpose + ":" + armyPage : ""));
            ManagementTabs(OpenGovernment);
            if(!BaseDemo.Active(owner.State)){RowButton("规则 i",0,800,()=>FacilityRules(OpenGovernment));row+=60;}
            if (activePage == "report") DailyReportContent(); else if (activePage == "administrators") Administrators(); else if (activePage == "workers") Workers(BaseDemo.Active(owner.State)?(BaseFacilityLayout.Built(owner.State,"caravan")?new[]{"farm","caravan"}:new[]{"farm"}):new[] { "farm", "workshop", "caravan" }, OpenGovernment); else if (activePage == "army") Army(); else if (activePage == "development") Development(); else Inventory(); EndPanel();
        }
        private void ManagementTabs(Action refresh)
        {
            var pages = BaseDemo.Active(owner.State)&&activeObject=="government"?new[]{"workers","inventory","development","report"}:BaseFacilityPages.ForObject(activeObject);
            if (!pages.Contains(activePage)) activePage = pages[0];
            float step = Mathf.Min(290, 880f / pages.Length), width = step - 10;
            for (int i = 0; i < pages.Length; i++)
            {
                string page = pages[i]; float x = (i - (pages.Length - 1) / 2f) * step;
                RowButton((activePage == page ? "● " : "") + BaseFacilityPages.PageName(page), x, width, () => { activePage = page; refresh(); });
            }
            row += 70;
        }
        private void OpenFacility()
        {
            if (owner.State == null || activeObject != "farm" && activeObject != "workshop") return;
            if (UseFacilityPanel) { OpenProductionFacility(); return; }
            string facility = activeObject;
            BeginPanel(BaseEconomy.FacilityName(facility), facility + ":" + activePage); ManagementTabs(OpenFacility);
            RowButton("规则 i",0,800,()=>FacilityRules(OpenFacility));row+=60;
            if (activePage == "administrators") FacilityAdministrators(facility);
            else if (activePage == "workers") Workers(new[] { facility }, OpenFacility);
            else if (activePage == "production" && facility == "workshop") Production();
            else if(activePage=="upgrade")FacilityUpgrade(facility,OpenFacility);
            else
            {
                var info = BaseFacilityInfo.Read(owner.State, facility);
                Line("管理员：" + info.Administrator, 54, 22); Line("工人：" + info.Workers, 54, 22);
                Line(info.Production, 68); Line("状态：" + info.Status, 68);
                Line(facility == "farm" ? "日末产粮并存入仓库。管理员提供增益，工人提供基础工作量。" : "选择产品后于日末制作，完成产品才消耗原料。", 68, 18);
            }
            EndPanel();
        }
        private void FacilityAdministrators(string facility)
        {
            var s = owner.State; var manager = s.Leaders.FirstOrDefault(l=>l.FacilityId==facility);
            Line("本设施管理员：" + (manager?.Name ?? "无人") + "；最多一名管理员。", 56);
            foreach (var person in s.Leaders)
            {
                var leader = person;
                bool assigned = leader.FacilityId == facility;
                var record = BasePersonnel.Units(s).First(u => u.LeaderId == leader.Id);
                AddPersonnelRow(content, record, assigned ? RosterText("unassign_manager", "点击解除任职") : RosterText("assign_manager", "点击任命为管理员"),
                    () => ConfirmManager(leader.Id, assigned ? "" : facility, OpenFacility),
                    BaseEconomy.CanAssignLeader(s, leader.Id, assigned ? "" : facility), assigned);
                LeaderLock(leader.Id, OpenFacility);
            }
        }
        private void Inventory()
        {
            var s = owner.State; var p = BaseEconomy.Preview(s);
            Line("粮草 " + s.Inventory.Food + "    统一原料 " + s.Inventory.Materials + "    钱币 " + s.Inventory.Coins, 52, 23);
            Line("今天已供口粮 " + s.TodayFoodConsumed + "／" + s.TodayFoodRequired + "；预计产粮 " + p.FoodProduced + "；明天需要 " + p.FoodRequired);

            foreach (var product in BaseEconomy.ProductsFor(s))
            {
                int low = s.Inventory.Equipment.Single(x => x.Product == product && x.Level == 1).Count;
                int high = s.Inventory.Equipment.Single(x => x.Product == product && x.Level == 2).Count;
                Line(BaseEconomy.ProductName(product) + "：总量 一级 " + low + "／二级 " + high, 40);
                Line("已锁 " + BaseSupply.Locked(s, product, 1) + "/" + BaseSupply.Locked(s, product, 2) + "；可用 " + BaseSupply.Available(s, product, 1) + "/" + BaseSupply.Available(s, product, 2), 36, 18);
            }

        }
        private void Administrators()
        {
            var s = owner.State;

            foreach (var leader in s.Leaders)
            {
                Line(BasePersonnel.LeaderName(leader) + " · " + BaseEconomy.FacilityName(leader.FacilityId) + (leader.Away ? "（外出）" : ""), 40);
                ManagerButtons(leader.Id, OpenGovernment);
            }
        }
        private BaseState workerDraft;
        private string workerDraftStamp;
        private readonly System.Collections.Generic.List<(BaseTroopKind kind,string from,string to,int? bucket)> workerMoves =
            new System.Collections.Generic.List<(BaseTroopKind,string,string,int?)>();
        private int workerRosterPage;
        private static void LockCurrentPosts(BaseState state)
        {
            foreach (var worker in state.Workers)
            {
                worker.DesiredFacility = worker.FacilityId;
                worker.PostLocked = true;
            }
        }
        private void Workers(string[] facilities, Action refresh)
        {
            EnsureWorkerDraft();
            var s = workerDraft;
            Line("安排方案：确认后实际调动并固定工人岗位；解锁岗位可恢复自动安排。", 58, 22);
            RowButton("确认方案",-210,400,()=>CommitWorkerDraft(refresh),workerMoves.Count>0);
            RowButton("取消方案",210,400,()=>{ResetWorkerDraft();refresh();});row+=60;
            foreach (string facility in facilities)
            {
                Line(BaseEconomy.FacilityName(facility) + "：今日生产人数 " + BaseEconomy.EligibleWorkers(s, facility) + "/" + s.Muster.Facilities.Single(f => f.FacilityId == facility).Capacity, 42);
                RowButton(RosterText("open", "点名调配"), 0, 800, () => { workerRosterPage = 0; OpenWorkerRoster(facility, refresh); });row += 56;
                foreach (BaseTroopKind kind in Enum.GetValues(typeof(BaseTroopKind)))
                {
                    string target = facility; BaseTroopKind troop = kind;
                    int available = s.Workers.Where(w => w.Kind == kind && w.FacilityId == "" && BaseDevelopment.Effective(s, w)).Sum(w => w.Count);
                    Label(content, BaseEconomy.TroopName(kind) + " " + BaseEconomy.WorkersAt(s, target, kind) + "人 · 闲置 · 今日可生产 " + available, new Vector2(-230, -row - 26), new Vector2(430, 48), 19, TextAnchor.MiddleLeft, new Vector2(0.5f, 1));
                    RowButton("−1", 70, 90, () => Adjust(troop, target, "", refresh), BaseEconomy.WorkersAt(s, target, kind) > 0);
                    RowButton("+1", 180, 90, () => Adjust(troop, "", target, refresh), available > 0 && BaseEconomy.WorkersAt(s, target) < s.Muster.Facilities.Single(f => f.FacilityId == target).Capacity);
                    RowButton("解锁岗位", 310, 140, () => {workerDraft=null;workerMoves.Clear();owner.Change(next => { foreach (var w in next.Workers.Where(w => w.Kind == troop && (w.FacilityId == target || w.DesiredFacility == target))) w.PostLocked = false; }, "已解锁工人，由策略安排"); refresh(); }); row += 56;
                }
            }
            foreach (string facility in facilities) Line(BaseEconomy.FacilityName(facility) + "：" + BaseFacilityInfo.Read(s, facility).Production, 56);
        }
        private void ManagerButtons(string leaderId, Action refresh)
        {
            var s = owner.State; var leader = s.Leaders.Single(x => x.Id == leaderId);
            foreach (string facility in BaseEconomy.Facilities)
            {
                string target = facility;
                bool available = BaseEconomy.CanAssignLeader(s,leaderId,target);
                RowButton((leader.FacilityId == target ? "● " : "") + BaseEconomy.FacilityName(target), target == "" ? -290 : target == "farm" ? 0 : 290, 265,
                    () => ConfirmManager(leaderId,target,refresh), available);
            }
            row += 62;
            LeaderLock(leaderId, refresh);
        }
        private void OpenWorkerRoster(string facility, Action back)
        {
            var state = workerDraft;
            if (state == null) { back(); return; }
            BeginPanel(BaseEconomy.FacilityName(facility) + " · 人员名册", "workers-roster:" + facility);
            RowButton("返回调配方案", 0, 800, back); row += 60;
            var available = BasePersonnel.Units(state).Where(u => !u.IsLeader && !u.Away &&
                (state.Workers[u.BucketIndex].FacilityId == facility || state.Workers[u.BucketIndex].FacilityId == ""))
                .OrderByDescending(u => state.Workers[u.BucketIndex].FacilityId == facility).ThenBy(u => u.Kind).ToArray();
            const int pageSize = 8;
            int pages = Math.Max(1, (available.Length + pageSize - 1) / pageSize);
            workerRosterPage = Mathf.Clamp(workerRosterPage, 0, pages - 1);
            int capacity = state.Muster.Facilities.Single(f => f.FacilityId == facility).Capacity;
            bool room = BaseEconomy.WorkersAt(state, facility) < capacity;
            Action refresh = () => OpenWorkerRoster(facility, back);
            foreach (var unit in available.Skip(workerRosterPage * pageSize).Take(pageSize))
            {
                var worker = state.Workers[unit.BucketIndex];
                bool assigned = worker.FacilityId == facility;
                string status = assigned ? RosterText("unassign", "点击调回闲置") : !unit.Effective ? unit.State :
                    room ? RosterText("assign", "点击安排上岗") : RosterText("full", "岗位已满");
                AddPersonnelRow(content, unit, status,
                    () => Adjust(worker.Kind, worker.FacilityId, assigned ? "" : facility, refresh, unit.BucketIndex),
                    assigned || (unit.Effective && room), assigned);
            }
            if (available.Length == 0) Line("当前没有可调人员。", 52, 18);
            if (pages > 1)
            {
                RowButton("上一页", -210, 400, () => { workerRosterPage--; refresh(); }, workerRosterPage > 0);
                RowButton((workerRosterPage + 1) + "/" + pages + " 下一页", 210, 400,
                    () => { workerRosterPage++; refresh(); }, workerRosterPage + 1 < pages); row += 60;
            }
            EndPanel();
        }
        private void Adjust(BaseTroopKind kind, string from, string to, Action refresh, int? bucket = null)
        {
            if(workerDraft!=null&&BaseEconomy.MoveWorkers(workerDraft,kind,from,to,1,bucket))workerMoves.Add((kind,from,to,bucket));
            refresh();
        }
        private void ConfirmManager(string leaderId,string facility,Action refresh)
        {
            var leader=owner.State.Leaders.Single(l=>l.Id==leaderId);
            string extra=facility!=""&&BaseAdventure.Contains(owner.State,leaderId)?"；任职后退出随行小队":"";
            NpcDialogue(new[]{new NpcLine("岗位安排","",leader.Name+" → "+BaseEconomy.FacilityName(facility)+extra)},new[]{new NpcReply("确认方案",()=>{owner.Change(next=>{if(!BaseEconomy.AssignLeader(next,leaderId,facility))throw new InvalidOperationException("设施或人员状态已变化，请重新安排");},"已确认管理员安排"+extra);refresh();}),new NpcReply("取消",refresh)});
        }
        private void Production()
        {
            var s = owner.State; var p = BaseEconomy.Preview(s);
            RowButton("一级军需",-210,400,()=>{owner.Change(next=>{if(!BaseConstruction.SelectLevel(next,1))throw new InvalidOperationException("当前不能改配方");},"已选一级生产");OpenFacility();});
            if (s.Muster.Facilities.Single(f=>f.FacilityId=="workshop").MaxProductLevel>=2)
                RowButton("二级军需",210,400,()=>{owner.Change(next=>{if(!BaseConstruction.SelectLevel(next,2))throw new InvalidOperationException("工坊未解锁二级生产");},"已选二级生产");OpenFacility();});
            row+=62;
            Line("今日产品："+p.ProductLevel+"级" + BaseEconomy.ProductName(s.WorkshopProduct) + "。选择不会立即发放产物。", 62);
            foreach (string product in BaseEconomy.ProductsFor(s))
            {
                string id = product;
                RowButton((s.WorkshopProduct == id ? "● " : "") + BaseEconomy.ProductName(id) + " · 原料" + BaseConstruction.MaterialCost(s,id), id=="supply"?0:id == "sword" ? -290 : id == "bow" ? 0 : 290, 265,
                    () => { owner.Change(next => BaseEconomy.SelectProduct(next, id), "已选择生产" + BaseEconomy.ProductName(id)); OpenFacility(); });
            }
            row += 75;
            Line("工人 " + BaseEconomy.WorkersAt(s, "workshop") + "人；今日可完成工作量 " + p.WorkCapacity + "；预计完成 " + p.ProductsMade + "件");
            Line("需原料 " + p.MaterialsSpent + "；库存 " + s.Inventory.Materials + (p.ProductsMade < p.WorkCapacity ? "；原料不足，剩余整数工作点不储存" : "；足够本次生产"), 62);


        }
    }
}
