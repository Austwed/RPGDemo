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
        private readonly Dictionary<string,Button> captainButtons = new();
        private readonly Dictionary<(string,SoldierKind),InputField> teamCounts = new();
        private readonly Dictionary<int,RectTransform> soldierCards = new();
        private Button[] rosterToolbar;
        private Button gradeButton;
        private Text rosterSummary;
        private SoldierRosterFilter rosterFilter = new();
        private BasePersonnelUnit[] soldiers;
        private Dictionary<string,BasePrepUnit> available;

        private void ShowPreparation()
        {
            NewPage("模拟对抗 · 出战编队");
            Caption(page,"Captains","队长与编队",.028f,.78f,.188f,.865f,22);
            int col = 0;
            foreach (var kind in BattleSimulation.TroopKinds)
            {
                float x = .20f + col++*.062f;
                Caption(page,"Heading"+kind,BattleSimulation.KindName(kind),x,.78f,x+.06f,.865f,18);
            }
            captainButtons.Clear();
            teamCounts.Clear();
            var leaders = Draft.Units.Where(u=>u.Kind==SoldierKind.Hero).ToArray();
            for(int i=0;i<BaseAdventure.Capacity;i++)
            {
                float top=.775f-i*.145f;
                var line=Area(page,"Captain"+i,.025f,top-.135f,.449f,top);
                Back(line,Theme.Inset);
                if(i>=leaders.Length) continue;
                var leader=leaders[i];
                var select=ButtonAt(line,leader.Id,leader.Name,.01f,.47f,.38f,.96f,()=>
                {
                    if(!leader.Selected) leader.Selected=true;
                    prepTeam=leader.Id; RefreshRoster();
                });
                captainButtons.Add(leader.Id,select);
                if(leader.Id!=Draft.PlayerId)
                    ButtonAt(line,"Membership","出 / 入",.01f,.04f,.38f,.45f,()=>
                    {
                        leader.Selected=!leader.Selected;
                        if(!leader.Selected)
                        {
                            foreach(var u in Draft.Units.Where(u=>u.Kind!=SoldierKind.Hero && u.TeamId==leader.Id)) u.Selected=false;
                            if(prepTeam==leader.Id) prepTeam=Draft.PlayerId;
                        }
                        else prepTeam=leader.Id;
                        placements.Clear(); RefreshRoster();
                    });
                int column=0;
                foreach (var kind in BattleSimulation.TroopKinds)
                {
                    float x=.403f+column++*.149f;
                    var slot=Area(line,kind.ToString(),x,.18f,x+.135f,.82f);
                    Back(slot,Theme.Button);
                    var label=Words(Area(slot,"Value",0,0,1,1),"0",20,false,TextAnchor.MiddleCenter);
                    var field=slot.gameObject.AddComponent<InputField>();
                    field.targetGraphic=slot.GetComponent<Image>();
                    field.textComponent=label;
                    field.contentType=InputField.ContentType.IntegerNumber;
                    field.onEndEdit.AddListener(value=>
                    {
                        if (int.TryParse(value,out int target) && Draft.SetTeamCount(leader.Id,kind,target))
                        {
                            prepTeam=leader.Id;
                            placements.Clear();
                        }
                        RefreshRoster();
                    });
                    teamCounts.Add((leader.Id,kind),field);
                }
            }
            rosterSummary=Caption(page,"Count","",.025f,.23f,.449f,.31f,18);
            gradeButton=ButtonAt(page,"Supply","",.025f,.10f,.190f,.21f,()=>
            {
                Draft.SupplyGrade=Draft.SupplyGrade==1?2:1;
                RefreshRoster();
            });
            ButtonAt(page,"Restore","恢复编队",.195f,.10f,.305f,.21f,()=>
            {
                Draft=new BaseBattlePreparation(camp,BasePrepRoute.Travel);
                prepTeam=Draft.PlayerId;
                placements.Clear();
                ShowPreparation();
            });
            ButtonAt(page,"Deploy","进入布阵",.31f,.10f,.449f,.21f,ShowDeployment,true);
            Caption(page,"RosterTitle","士兵名册",.468f,.80f,.975f,.879f,22);
            rosterToolbar=new Button[4];
            for(int i=0;i<4;i++)
            {
                int index=i;
                float x=.464f+i*.129f;
                rosterToolbar[i]=ButtonAt(page,"Filter"+i,"",x,.72f,x+.125f,.80f,()=>
                {
                    rosterFilter.Activate(index);
                    RefreshRoster();
                });
            }
            var listBody=VerticalList(page,"Soldiers",.46f,.097f,.981f,.713f);
            soldiers=BasePersonnel.Soldiers(camp).ToArray();
            available=Draft.Units.Where(u=>u.Kind!=SoldierKind.Hero).ToDictionary(u=>u.Id);
            soldierCards.Clear();
            foreach(var soldier in soldiers)
            {
                var go=Instantiate(owner.SceneUiTemplate("PersonnelRosterRow"),listBody,false);
                go.name=soldier.Key;
                go.SetActive(true);
                var card=(RectTransform)go.transform;
                card.Find("Info").gameObject.SetActive(false);
                card.GetComponent<LayoutElement>().preferredHeight=48;
                var button=card.GetComponent<Button>();
                foreach(var text in card.GetComponentsInChildren<Text>()) text.font=font;
                bool canDeploy=available.TryGetValue(soldier.Key,out var unit);
                button.interactable=canDeploy;
                button.onClick.AddListener(()=>
                {
                    if(Draft.ToggleSoldier(prepTeam,unit)) placements.Clear();
                    RefreshRoster();
                });
                soldierCards.Add(soldier.SoldierNumber,card);
            }
            RefreshRoster();
        }
        private void RefreshRoster()
        {
            foreach(var leader in Draft.Units.Where(u=>u.Kind==SoldierKind.Hero))
            {
                var button=captainButtons[leader.Id];
                button.image.color=leader.Id==prepTeam?Theme.Gold:Theme.Button;
                button.GetComponentInChildren<Text>().text=(leader.Selected?"● ":"○ ")+leader.Name;
                foreach(var kind in BattleSimulation.TroopKinds)
                {
                    var count=teamCounts[(leader.Id,kind)];
                    count.interactable=leader.Selected;
                    if(!count.isFocused) count.SetTextWithoutNotify(Draft.TeamCount(leader.Id,kind).ToString());
                }
            }
            BaseHud.PaintRosterToolbar(rosterToolbar,rosterFilter);
            var filtered=rosterFilter.Apply(soldiers,u=>available.ContainsKey(u.Key)).ToArray();
            var visible=filtered.Select(u=>u.SoldierNumber).ToHashSet();
            foreach(var pair in soldierCards) pair.Value.gameObject.SetActive(visible.Contains(pair.Key));
            int order=0;
            foreach(var u in filtered)
            {
                var card=soldierCards[u.SoldierNumber];
                if(card.GetSiblingIndex()!=order) card.SetSiblingIndex(order);
                order++;
                bool eligible=available.TryGetValue(u.Key,out var pick);
                string status=BasePersonnel.CompactPost(u);
                if(pick?.Selected==true)
                    status=Draft.Units.First(leader=>leader.Id==pick.TeamId).Name+" · "+status;
                BaseHud.UpdatePersonnelRow(card,u,status,false);
                BaseHud.TintRoster(card,eligible,false,pick?.Selected==true);
            }
            int troops=Draft.Units.Count(s=>s.Selected&&s.Kind!=SoldierKind.Hero);
            gradeButton.GetComponentInChildren<Text>().text=$"{Draft.SupplyGrade}级军需  {Draft.AvailableSupply(camp,Draft.SupplyGrade)}/{troops}";
            rosterSummary.text=$"队长 {Draft.Units.Count(s=>s.Selected&&s.Kind==SoldierKind.Hero)}   士兵 {troops}/{BaseMuster.BattleSoldierLimit}";
            Hint(theirs!=null||hostReady?"对方已就绪。":"");
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
                        {SoldierKind.Hero=>"队长", SoldierKind.Melee=>"剑", SoldierKind.Ranged=>"弓", SoldierKind.Mage=>"法", _=>"骑"});
                }
            }
            RefreshPlacement();
            Hint("选择队员，再点部署格。上方靠近敌阵。");
        }
    }
}
