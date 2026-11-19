using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Base
{
    public sealed partial class LanDuelScreen
    {
        private RectTransform page, list;
        private Text feedback;
        private InputField address;
        private string typedAddress = "", prepTeam, deploymentId;
        private readonly Dictionary<string, Vector2Int> placements = new();
        private static DemoUiTheme Theme => DemoUiTheme.Current;
        private static RectTransform Area(Transform parent, string name, float left, float bottom, float right, float top)
        {
            var box = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            box.SetParent(parent, false);
            box.anchorMin = new Vector2(left, bottom); box.anchorMax = new Vector2(right, top);
            box.offsetMin = box.offsetMax = Vector2.zero;
            return box;
        }
        private static void Back(Transform node, Color color)
        {
            node.gameObject.AddComponent<Image>().color = color;
        }
        private Text Words(RectTransform node, string value, int size = 20, bool light = false, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var t = node.gameObject.AddComponent<Text>(); t.font = font;
            t.supportRichText = false; t.fontSize = size; t.color = light ? Theme.Panel : Theme.Ink;
            t.alignment = align; t.text = value; t.raycastTarget = false; return t;
        }
        private Text Caption(Transform parent, string name, string value, float x, float y, float x2, float y2, int size = 20, bool light = false)
            => Words(Area(parent, name, x, y, x2, y2), value, size, light);
        private Button ActionButton(RectTransform node, string value, Action action, bool accent = false)
        {
            var img = node.gameObject.AddComponent<Image>();
            var button = node.gameObject.AddComponent<Button>(); button.targetGraphic = img;
            Theme.StyleButton(button); img.color = accent ? Theme.Gold : Theme.Button;
            button.onClick.AddListener(() => action());
            Words(Area(node,"Label",.025f,0,.975f,1), value, 20, false, TextAnchor.MiddleCenter);
            return button;
        }
        private Button ButtonAt(Transform p, string name, string value, float x, float y, float x2, float y2, Action action, bool accent = false)
            => ActionButton(Area(p,name,x,y,x2,y2),value,action,accent);
        private void NewPage(string title)
        {
            if (page != null) { page.gameObject.SetActive(false); Destroy(page.gameObject); }
            page = Area(transform, "DuelPage", .015f,.018f,.985f,.982f);
            Back(page, Theme.Panel);
            var header = Area(page, "Header",0,.90f,1,1); Back(header, Theme.Wood);
            Caption(header,"Title",title,.025f,0,.7f,1,27,true);
            ButtonAt(header,"Leave","返回兵营",.856f,.15f,.983f,.85f,Leave);
            var foot = Area(page, "Status",0,0,1,.075f); Back(foot, Theme.Wood);
            feedback = Caption(foot,"Feedback",notice,.02f,0,.98f,1,18,true);
        }
        private void Hint(string message)
        {
            notice = message;
            if (feedback != null) feedback.text = message;
        }
        private RectTransform VerticalList(Transform parent,string name,float x,float y,float x2,float y2)
        {
            var outer = Area(parent,name,x,y,x2,y2); Back(outer,Theme.Inset);
            var scroll = outer.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false;
            var view = Area(outer,"Viewport",.012f,.01f,.988f,.99f);
            view.gameObject.AddComponent<RectMask2D>(); var body = Area(view,"Rows",0,1,1,1);
            body.pivot = new Vector2(.5f,1);
            var layout = body.gameObject.AddComponent<VerticalLayoutGroup>(); layout.spacing = 4;
            layout.childControlWidth = true; layout.childControlHeight = true;
            layout.childForceExpandWidth=true; layout.childForceExpandHeight=false;
            body.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = view; scroll.content = body; scroll.movementType = ScrollRect.MovementType.Clamped;
            return body;
        }
        private Button Row(Transform parent, string name, string label, Action action, bool selected=false)
        {
            var row = Area(parent,name,0,0,1,1);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 48;
            return ActionButton(row,label,action,selected);
        }
        private void ShowLobby()
        {
            NewPage("模拟对抗 · 附近营地");
            Caption(page,"RoomsTitle",hosting ? "已开放营地" : joining ? "正在联络营地" : "局域网内的营地",.04f,.77f,.60f,.875f,24);
            list = VerticalList(page,"RoomList",.04f,.14f,.57f,.765f);
            Caption(page,"How",hosting ? "等候其他营地的异能者加入。" : "与同一网络的营地进行演武。双方用自己的队伍出战，伤亡和军需只计入本场。",.61f,.60f,.96f,.83f);
            ButtonAt(page,"Host",hosting || joining ? "返回搜寻" : "开放我的营地",.61f,.48f,.96f,.58f,
                () => { if(hosting || joining) Browse(); else HostRoom(); },true);
            if (hosting) Caption(page,"HostAddress","本机地址  "+wire.LocalAddress,.61f,.29f,.96f,.45f);
            else if (!joining)
            {
                Caption(page,"DirectLabel","直接加入 · 房主地址",.61f,.355f,.96f,.45f);
                var edit = Area(page,"Address",.61f,.25f,.96f,.35f); Back(edit,Color.white);
                address = edit.gameObject.AddComponent<InputField>(); address.targetGraphic = edit.GetComponent<Image>();
                address.textComponent = Words(Area(edit,"Text",.035f,0,.965f,1),"",20);
                address.lineType = InputField.LineType.SingleLine; address.characterLimit = 64;
                address.text = typedAddress; address.onValueChanged.AddListener(s => typedAddress=s);
                ButtonAt(page,"JoinAddress","加入",.61f,.13f,.96f,.23f,()=>JoinRoom(typedAddress));
            }
            UpdateRooms();
        }
        private void UpdateRooms()
        {
            seenRooms = wire.RoomsRevision;
            foreach (Transform child in list) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            if (hosting || joining || wire.Rooms.Count == 0)
            {
                var line = Area(list,"RoomStatus",0,0,1,1);line.gameObject.AddComponent<LayoutElement>().preferredHeight=56;
                Words(line,hosting?"等候另一位指挥官……":joining?"正在连接……":"暂未发现开放的营地",20,false,TextAnchor.MiddleCenter);
                return;
            }
            foreach (var room in wire.Rooms)
                Row(list,"JoinRoom",room.Name + "   ·   " + room.Address,()=>JoinRoom(room.Address+":"+room.Port));
        }
        private void ShowDisconnect()
        {
            NewPage("模拟对抗 · 连接已结束");
            Caption(page,"Reason",notice,.15f,.42f,.85f,.68f,25);
            ButtonAt(page,"Return","返回兵营",.35f,.22f,.65f,.34f,Leave,true);
        }
        private void ShowWaiting()
        {
            NewPage("模拟对抗 · 编队就绪");
            Caption(page,"ReadySummary","已部署 "+mine.units.Length+" 名单位",.22f,.54f,.8f,.70f,26);
            Caption(page,"Waiting","等候对方完成部署……",.22f,.36f,.8f,.52f);
        }
        private LanDuelRoster MakeRoster()
        {
            var roster = LanDuelRoster.From(camp,Draft);
            foreach (var m in roster.units)
                if (placements.TryGetValue(m.id,out var cell)) { m.x=cell.x; m.z=cell.y; }
            return roster;
        }
        private readonly Dictionary<string, Button> rosterButtons = new();
        private Text rosterSummary;
        private Button[] gradeButtons;
        private void ShowPreparation()
        {
            NewPage("模拟对抗 · 出战编队");
            Caption(page,"VictoryRule","全部队长倒地的一方落败，五分钟未分胜负则平局。",.025f,.085f,.728f,.145f,18);
            Caption(page,"Captains","指挥队长",.03f,.78f,.40f,.87f,22);
            var captains = VerticalList(page,"Captains",.025f,.48f,.419f,.78f);
            Caption(page,"Supply","演武军需",.03f,.365f,.40f,.465f,22);
            gradeButtons = new Button[3];
            for(int i=0;i<3;i++)
            {
                int grade=i; float x=.025f + i*.134f;
                gradeButtons[i]=ButtonAt(page,"Supply"+i,i==0?"无配给":i+"级  ×"+Draft.AvailableSupply(camp,i),x,.27f,x+.128f,.365f,
                    ()=>{ Draft.SupplyGrade=grade; RefreshRoster(); });
            }
            rosterSummary=Caption(page,"Count","",.025f,.145f,.428f,.25f,18);
            Caption(page,"Soldiers","士兵名册",.455f,.78f,.86f,.87f,22);
            var soldiers=VerticalList(page,"Soldiers",.45f,.245f,.972f,.78f);
            rosterButtons.Clear();
            foreach(var unit in Draft.Units)
            {
                if(unit.Kind==SoldierKind.Hero)
                {
                    var line=Area(captains,"Captain",0,0,1,1);line.gameObject.AddComponent<LayoutElement>().preferredHeight=50;
                    rosterButtons[unit.Id]=ButtonAt(line,unit.Id,unit.Name,0,0,.65f,1,()=>{unit.Selected=true;prepTeam=unit.Id;RefreshRoster();});
                    if(unit.Id!=Draft.PlayerId)
                        ButtonAt(line,"Toggle","出 / 入",.665f,0,1,1,()=>
                        {
                            unit.Selected=!unit.Selected;
                            if(!unit.Selected){foreach(var s in Draft.Units.Where(s=>s.Kind!=SoldierKind.Hero && s.TeamId==unit.Id)) s.Selected=false;if(prepTeam==unit.Id)prepTeam=Draft.PlayerId;}
                            else prepTeam=unit.Id;
                            placements.Clear();RefreshRoster();
                        });
                }
                else rosterButtons[unit.Id]=Row(soldiers,unit.Id,unit.Name,()=>
                {
                    if(!unit.Selected && Draft.Units.Count(s=>s.Selected&&s.Kind!=SoldierKind.Hero)>=BaseMuster.BattleSoldierLimit)
                    { Hint("出战人数已满。");return; }
                    bool remove=unit.Selected&&unit.TeamId==prepTeam;unit.TeamId=prepTeam;unit.Selected=!remove;
                    placements.Clear();RefreshRoster();
                });
            }
            ButtonAt(page,"Deploy","布阵",.74f,.115f,.972f,.215f,ShowDeployment,true);
            RefreshRoster();
        }
        private void RefreshRoster()
        {
            foreach(var u in Draft.Units)
            {
                var button=rosterButtons[u.Id];
                bool hero=u.Kind==SoldierKind.Hero;
                button.image.color=(hero?u.Id==prepTeam:u.Selected)?Theme.Gold:Theme.Button;
                string team=Draft.Units.FirstOrDefault(t=>t.Id==u.TeamId)?.Name;
                button.GetComponentInChildren<Text>().text=hero?(u.Selected?"● ":"○ ")+u.Name:
                    u.Name+"   "+u.Health.ToString("0")+"/"+u.MaxHealth.ToString("0")+"   "+(u.Selected?team:"待命");
            }
            for(int i=0;i<gradeButtons.Length;i++)gradeButtons[i].image.color=i==Draft.SupplyGrade?Theme.Gold:Theme.Button;
            int troops=Draft.Units.Count(s=>s.Selected&&s.Kind!=SoldierKind.Hero);
            rosterSummary.text=$"队长 {Draft.Units.Count(s=>s.Selected&&s.Kind==SoldierKind.Hero)}   士兵 {troops}/{BaseMuster.BattleSoldierLimit}";
            Hint(theirs!=null||hostReady?"对方已就绪。":"选中队长，再点士兵编入本队。");
        }
        private void ShowDeployment()
        {
            LanDuelRoster roster;
            try { roster=MakeRoster(); }
            catch(InvalidOperationException e){ Hint(e.Message);return; }
            if(roster.units.Length==0){Hint("请至少选择一名队长。");return;}
            if(!roster.units.Any(u=>u.id==deploymentId)) deploymentId=roster.units[0].id;
            NewPage("模拟对抗 · 布阵");
            var placedLabels=new Dictionary<string,Button>();
            var cells=new Dictionary<Vector2Int,Button>();
            var troops=VerticalList(page,"PlacementRoster",.035f,.235f,.39f,.84f);
            foreach(var unit in roster.units)
                placedLabels[unit.id]=Row(troops,unit.id,unit.name,()=>{deploymentId=unit.id;RefreshPlacement();});
            ButtonAt(page,"BackToRoster","返回编队",.035f,.11f,.205f,.215f,ShowPreparation);
            ButtonAt(page,"Ready","准备完成",.22f,.11f,.39f,.215f,Ready,true);
            var grid=Area(page,"DeploymentGrid",.425f,.115f,.966f,.85f);
            // Columns run north–south on this diagram, letting every cell keep a touch-sized hit area.
            for(int z=1;z<=LanDuelMatch.Height-2;z++)
            for(int x=1;x<LanDuelMatch.DeploymentWidth;x++)
            {
                int cx=x,cz=z;
                float a=(z-1)/(float)(LanDuelMatch.Height-2),b=(x-1)/(float)(LanDuelMatch.DeploymentWidth-1);
                cells[new Vector2Int(x,z)]=ButtonAt(grid,"Cell"+x+"_"+z,"",a+.002f,b+.005f,
                    a+1f/(LanDuelMatch.Height-2)-.002f,b+1f/(LanDuelMatch.DeploymentWidth-1)-.005f,()=>
                    {
                        var moved=roster.units.First(u=>u.id==deploymentId);
                        var occupant=roster.units.FirstOrDefault(u=>u.x==cx&&u.z==cz);
                        if(occupant!=null){placements[occupant.id]=new Vector2Int(moved.x,moved.z);occupant.x=moved.x;occupant.z=moved.z;}
                        moved.x=cx;moved.z=cz;placements[deploymentId]=new Vector2Int(cx,cz);RefreshPlacement();
                    });
            }
            void RefreshPlacement()
            {
                foreach(var item in roster.units)
                {
                    var button=placedLabels[item.id];
                    button.image.color=item.id==deploymentId?Theme.Gold:Theme.Button;
                    button.GetComponentInChildren<Text>().text=item.name+"   ("+item.x+", "+item.z+")";
                }
                foreach(var pair in cells)
                {
                    var unit=roster.units.FirstOrDefault(u=>u.x==pair.Key.x&&u.z==pair.Key.y);
                    pair.Value.image.color=unit?.id==deploymentId?Theme.Gold:unit!=null?Theme.Button:Theme.Inset;
                    pair.Value.GetComponentInChildren<Text>().text=unit==null?"·":((SoldierKind)unit.kind switch
                        {SoldierKind.Hero=>"队长", SoldierKind.Melee=>"剑", SoldierKind.Ranged=>"弓", _=>"骑"});
                }
            }
            RefreshPlacement();
            Hint("选择队员，再点部署格。上方靠近敌阵。");
        }
    }
}
