using System;
using System.Linq;
using System.Collections.Generic;
using BaseConquest.Battle;
using BaseConquest.Presentation;
using BaseConquest.Story;
using UnityEngine;
using UnityEngine.UI;
namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private int personnelFilter;
        private static readonly Color RosterUnavailable = new Color32(232, 192, 182, 255);
        private static readonly Color RosterAtPost = new Color32(194, 211, 168, 255);

        private Button[] RosterToolbar(Transform parent, SoldierRosterFilter filter, Action refresh)
        {
            var buttons = new Button[4];
            for (int i = 0; i < buttons.Length; i++)
            {
                int index = i;
                buttons[i] = FacilityButton(parent, new[] { "All", "Kind", "Health", "Status" }[i], filter.Label(i), () =>
                {
                    filter.Activate(index);
                    PaintRosterToolbar(buttons, filter);
                    refresh();
                });
            }
            PaintRosterToolbar(buttons, filter);
            return buttons;
        }
        internal static void PaintRosterToolbar(Button[] buttons, SoldierRosterFilter filter)
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].GetComponentInChildren<Text>().text = filter.Label(i);
                buttons[i].targetGraphic.color = filter.Active(i) ? DemoUiTheme.Current.Gold : DemoUiTheme.Current.Button;
            }
        }
        internal static void TintRoster(RectTransform row, bool eligible, bool atPost, bool selected = false)
        {
            var button = row.GetComponent<Button>();
            var colors = button.colors;
            colors.disabledColor = Color.white;
            button.colors = colors;
            button.targetGraphic.color = !eligible ? RosterUnavailable : atPost ? RosterAtPost :
                selected ? Color.Lerp(DemoUiTheme.Current.Button, DemoUiTheme.Current.Gold, .45f) : DemoUiTheme.Current.Button;
        }
        internal static string DutyIcon(BasePersonnelUnit unit)
        {
            if (unit.Wounded || unit.Health <= 0) return "recovery";
            if (unit.FoughtToday) return "fought";
            if (unit.Relocating || unit.Remote || unit.Away) return null;
            if (unit.Post == "farm" || unit.Post == "intro_farm") return "food";
            if (unit.Post == "workshop" || unit.Post == "intro_smith") return "supplies";
            return "idle";
        }
        private static string RosterText(string key, string fallback) => StoryText.Get("story.ui.roster." + key, fallback);
        private static string CampSight(SoldierStats stats) =>
            stats.SightRadius > 0 ? $"基础视野 {stats.SightRadius} 格" :
                $"平地视野 {BattleHeightSight.DefaultRadius} 格 · 演武 {BattleScenario.DefaultVisionRadius} 格";
        private RectTransform AddPersonnelRow(Transform list, BasePersonnelUnit unit, string status, Action select,
            bool available = true, bool selected = false, bool warning = false, Action details = null, bool showFacility = true)
        {
            var card = CloneSceneUi("PersonnelRosterRow", list);
            bool touch = Application.platform == RuntimePlatform.Android;
            if (touch) card.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(card.rect.height, 48));
            Ui<LayoutElement>(card, "").preferredHeight = card.rect.height;
            var button = Ui<Button>(card, "");
            if (select == null) button.transition = Selectable.Transition.None;
            else button.onClick.AddListener(() => { if (!owner.Busy) select(); });
            button.interactable = available && select != null;
            var info = Ui<Button>(card, "Info");
            info.gameObject.SetActive(details != null);
            if (details != null)
            {
                if (touch) ResizeTouchButton(info, 44, 18);
                info.onClick.AddListener(() => { if (!owner.Busy) details(); });
                var textArea = Ui<Text>(card, "Status").rectTransform;
                textArea.offsetMax = new Vector2(((RectTransform)info.transform).offsetMin.x - 4f, textArea.offsetMax.y);
            }
            UpdatePersonnelRow(card, unit, status, showFacility, warning);
            if (selected)
            {
                var background = Ui<Image>(card, "");
                background.color = Color.Lerp(background.color, DemoUiTheme.Current.Gold, .38f);
            }
            if (list == content)
            {
                card.anchorMin = card.anchorMax = new Vector2(.5f, 1);
                card.pivot = new Vector2(.5f, 1);
                card.anchoredPosition = new Vector2(0, -row);
                row += Mathf.CeilToInt(card.rect.height) + 8;
            }
            return card;
        }

        internal static void UpdatePersonnelRow(RectTransform card, BasePersonnelUnit unit, string status,
            bool showFacility = true, bool warning = false)
        {
            Ui<Text>(card, "Label").text = $"{unit.Name}  生命 {unit.Health:0}/{unit.MaxHealth:0}";
            var right = Ui<Text>(card, "Status");
            if (string.IsNullOrEmpty(status)) status = unit.State;
            right.text = showFacility && !string.IsNullOrEmpty(unit.Facility) ? unit.Facility + " · " + status : status;
            if (!unit.IsLeader)
            {
                right.alignment = TextAnchor.MiddleLeft;
                var icon = card.Find("DutyIcon") as RectTransform;
                if (icon == null)
                {
                    var go = new GameObject("DutyIcon", typeof(RectTransform), typeof(Image));
                    icon = (RectTransform)go.transform;
                    icon.SetParent(card, false);
                    var left=Ui<Text>(card,"Label").rectTransform;
                    left.anchorMax=new Vector2(.44f,left.anchorMax.y);
                    right.rectTransform.anchorMin=new Vector2(.44f,right.rectTransform.anchorMin.y);
                    icon.anchorMin = icon.anchorMax = new Vector2(.44f, .5f);
                    icon.pivot = new Vector2(0, .5f);
                    icon.anchoredPosition = new Vector2(1, 0);
                    icon.sizeDelta = new Vector2(22, 22);
                    icon.GetComponent<Image>().raycastTarget = false;
                    right.rectTransform.offsetMin = new Vector2(26, right.rectTransform.offsetMin.y);
                }
                string key = DutyIcon(unit);
                icon.gameObject.SetActive(key != null);
                if (key != null) icon.GetComponent<Image>().sprite = DemoUiIcons.Load(key);
            }
            right.resizeTextMaxSize = right.fontSize;
            right.resizeTextForBestFit = true;
            right.resizeTextMinSize = 12;
            right.horizontalOverflow = HorizontalWrapMode.Wrap;
            right.color = warning ? new Color32(150, 35, 28, 255) : DemoUiTheme.Current.Ink;
        }

        private void PersonnelRoster()
        {
            var s=owner.State;var kinds=BaseTown.TroopKinds(s).Select(BaseCampaign.ToBattle).Prepend(SoldierKind.Hero).ToArray();
            if(BaseRpg.Active(s)){RowButton("俘虏名册 · "+s.Aftermath.Prisoners.Count(p=>p.Status=="held"),0,800,OpenHeldPrisoners);row+=60;}
            personnelFilter = Mathf.Clamp(personnelFilter,0,kinds.Length-1);
            float spacing = 840f / kinds.Length;
            for(int i=0;i<kinds.Length;i++){int index=i;RowButton((personnelFilter==i ? "● " : "")+BattleSimulation.KindName(kinds[i]),(i-(kinds.Length-1)*.5f)*spacing,spacing-10,()=>{personnelFilter=index;OpenBarracks();});}row+=60;
            var kind=kinds[personnelFilter];
            var units=(kind==SoldierKind.Hero ? BasePersonnel.Leaders(s) : BasePersonnel.Soldiers(s,kind)).ToArray();
            Line("共 "+units.Length+" 人",46,18);
            string stamp=BaseMuster.Snapshot(s);
            foreach(var u in units)
            {
                string key=u.Key;
                AddPersonnelRow(content, u, u.State, () => OpenPersonnelDetail(key, stamp));
            }
        }
        private void OpenHeldPrisoners()
        {
            var s=owner.State;BeginPanel("保留俘虏 · 待招降");RowButton("返回名册",0,800,()=>{activePage="roster";OpenBarracks();});row+=60;
            Line("队长 "+BaseRpg.Population(s)+" · 招降空位 "+BaseRpg.RecruitingSlots(s)+"。释放后无法再招降。",62,19);
            foreach(var prisoner in s.Aftermath.Prisoners.Where(p=>p.Status=="held").ToArray())
            {
                string id=prisoner.Id;Line(prisoner.Name+" · 生命 "+prisoner.Health.ToString("0")+"/"+prisoner.Stats.Health.ToString("0"),48,20);
                if(prisoner.Rpg?.Version==1)Line(BaseRpg.Describe(prisoner.Rpg),120,18);
                RowButton("招降",-210,400,()=>RecruitPrisoner(id,OpenHeldPrisoners),prisoner.Kind!=SoldierKind.Hero||BaseAftermath.CanRecruitLeader(s));
                RowButton("释放",210,400,() =>
                {
                    owner.Change(next =>
                    {
                        if (!BaseAftermath.Decide(next, id, "release")) throw new InvalidOperationException("俘虏状态已变化");
                    }, "俘虏已释放");
                    OpenHeldPrisoners();
                });row+=60;
            }
            EndPanel();
        }
        private void OpenPersonnelDetail(string key,string stamp,Action back=null,string backLabel="返回对话")
        {
            var s=owner.State;
            var roster=key.StartsWith("leader:",StringComparison.Ordinal) ? BasePersonnel.Leaders(s) : BasePersonnel.Soldiers(s);
            var u=BaseMuster.Snapshot(s)==stamp ? roster.FirstOrDefault(unit=>unit.Key==key) : null;
            string title = u?.IsLeader == true && BaseDemo.Active(s) ? "队长详情" : "兵营 · 单位详情";
            BeginPanel(title,"personnel:"+key);
            RowButton(back==null?"返回名册":backLabel,-210,400,back??(()=>{activePage="roster";OpenBarracks();}));
            if(u==null){row+=60;Line("人员安排已变化，请从名册重新选择。",80);EndPanel();return;}
            Line(u.Name+" · "+BattleSimulation.KindName(u.Kind),52,24);Line("生命 "+u.Health.ToString("0")+" / "+u.MaxHealth.ToString("0"),46);
            if(u.IsLeader){bool following=BaseAdventure.Contains(s,u.LeaderId);RowButton(following?"人物与背包":"到基地仓库管理此人背包",0,800,()=>{gearLeader=u.LeaderId;OpenPersonalEquipment();},following);row+=56;}
            Line("当前攻击 "+u.Current.Damage.ToString("0")+" · 防御 "+u.Current.Defense.ToString("0")+"；基础攻击 "+u.Basic.Damage.ToString("0")+" · 防御 "+u.Basic.Defense.ToString("0"),62,19);
            Line(CampSight(u.Current),46,19);
            var person=u.IsLeader?s.Leaders.Single(leader=>leader.Id==u.LeaderId):null;
            if(person!=null)Line("获取来源："+(string.IsNullOrEmpty(person.AcquisitionSource)?"未记录":person.AcquisitionSource)+" · 游戏日："+(person.AcquisitionDay>0?"第"+person.AcquisitionDay+"天":"未记录"),64,19);
            var profession=u.IsLeader ? u.Current.HasProfessionKind?u.Current.ProfessionKind:u.Current.HasCombatKind?u.Current.CombatKind:BaseCommanderIdentity.Appearance(person)=="ahe"?SoldierKind.Ranged:BaseCommanderIdentity.Appearance(person)=="azhu"?SoldierKind.Cavalry:SoldierKind.Melee : u.Kind;
            double range=u.IsLeader?BasePersonalEquipment.RealtimeRange(person):BaseRpg.Active(s)?s.Demo.Rpg.Range[BaseRpg.Profession(u.Kind)]:RealtimeCombatRules.Range(u.Kind);
            Line("攻击距离 "+range.ToString("0")+"细格 · 移速 "+(BaseRpg.Active(s)?person==null?s.Demo.Rpg.Speed[BaseRpg.Profession(profession)]:BaseRpg.MovementSpeed(person):RealtimeCombatRules.Speed(profession)).ToString("0")+"细格／秒"+(BaseRpg.Active(s)?"":" · 暴击率 "+(u.Current.CritChance*100).ToString("0")+"%")+" · 暴击伤害 "+u.Current.CritMultiplier.ToString("P0"),60,19);
            Line("所在："+u.Facility+" · "+u.State+(u.IsLeader ? "" : "\n生产资格："+(u.Effective ? "今日可生产" : "今日不生产"))+"；岗位："+(u.Locked ? "手动锁定" : "自动安排"),76,19);
            Line(u.IsLeader ? "管理员增益：粮田 +"+(u.FarmEfficiency*100).ToString("0")+"%／工坊 +"+(u.WorkshopEfficiency*100).ToString("0")+"%" : "工作效率：粮田 "+u.FarmEfficiency.ToString("P0")+"／工坊 "+u.WorkshopEfficiency.ToString("P0"),60,19);
            Line("状态效果\n"+u.Effects.Replace("／","\n"),210,19);
            if(person?.Rpg?.Version==1)Line(BaseRpg.Describe(person.Rpg),140,18);
            EndPanel();
        }
    }
}
