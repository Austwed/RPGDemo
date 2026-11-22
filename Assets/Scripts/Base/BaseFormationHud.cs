using System;
using System.Linq;
using UnityEngine;
namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private string compositionLeader;
        private int compositionPage;
        private static string TroopsText(BaseCommanderTroops p)=>"近战 "+p.Melee+"／弓兵 "+p.Ranged+"／骑兵 "+p.Cavalry+"／法师 "+p.Mage;
        private void CommanderPresets()
        {
            var s=owner.State;var all=s.Leaders.ToArray();int pages=Math.Max(1,(all.Length+4)/5);compositionPage=Mathf.Clamp(compositionPage,0,pages-1);
            Line("位次范围：驻守 ≤ 威胁 ≤ 进攻，填入 1～32 的整数",48,20);
            Line("              驻守                     威胁                     进攻",40,18);
            var fields=new UnityEngine.UI.InputField[9];
            for(int slot=0;slot<3;slot++)
            {
                Label(content,((char)('A'+slot))+" 位",new Vector2(-350,-row-26),new Vector2(100,48),20,TextAnchor.MiddleLeft,new Vector2(.5f,1));
                for(int kind=0;kind<3;kind++)
                {
                    int index=slot*3+kind;var box=Box("RangeInput",content,new Vector2(.5f,1),new Vector2(-160+kind*250,-row-26),new Vector2(220,46),buttonColor);
                    var label=Label(box,s.Muster.Ranges[index].ToString(),Vector2.zero,new Vector2(200,40),20,TextAnchor.MiddleCenter);label.raycastTarget=true;
                    var input=box.gameObject.AddComponent<UnityEngine.UI.InputField>();input.targetGraphic=box.GetComponent<UnityEngine.UI.Image>();input.textComponent=label;input.contentType=UnityEngine.UI.InputField.ContentType.IntegerNumber;input.text=s.Muster.Ranges[index].ToString();fields[index]=input;
                }
                row+=56;
            }
            RowButton("保存九项范围",0,800,()=>{if(owner.Change(next=>BaseMuster.SetRanges(next,fields.Select(f=>int.TryParse(f.text,out int n)?n:0).ToArray()),"已保存位次范围"))OpenBarracks();});row+=56;
            if(!all.Any(l=>l.Id==compositionLeader))compositionLeader=s.PlayerLeaderId;
            foreach(var leader in all.Skip(compositionPage*5).Take(5))
            {
                string id=leader.Id;RowButton((id==compositionLeader ? "● " : "")+BasePersonnel.LeaderName(leader)+" · "+TroopsText(leader.DefaultTroops),0,800,()=>{compositionLeader=id;OpenBarracks();});row+=54;
            }
            if(pages>1){RowButton("上一页",-210,400,()=>{compositionPage--;OpenBarracks();},compositionPage>0);RowButton("下一页",210,400,()=>{compositionPage++;OpenBarracks();},compositionPage+1<pages);row+=56;}
            var selected=s.Leaders.Single(l=>l.Id==compositionLeader);
            Line(BasePersonnel.LeaderName(selected)+"的默认带兵组成",48,22);
            foreach(BaseTroopKind k in BaseTown.TroopKinds(s))
            {
                var kind=k;ArmyCounter(BaseEconomy.TroopName(k),selected.DefaultTroops.Count(k),0,30,n=>
                {
                    owner.Change(next=>{var l=next.Leaders.Single(x=>x.Id==compositionLeader);if(l.Away||next.Campaign.HasActiveBattle)throw new InvalidOperationException("队长外出期间保留冻结编制，请返营后修改");BaseMuster.SetPreferenceCount(next,l,kind,n);},"已保存队长预设，未抽调人员");OpenBarracks();
                });
            }
        }
        public void OpenDeparture(string location, string eventId = null, string mode = "full", bool adviceHandled = false)
        {
            interactionObject = "";
            var state = owner.State;
            bool demoDefense = BaseDemoDefense.Enabled(state) && location == "perimeter" &&
                (eventId == null || eventId == BaseDemoDefense.EventId || eventId == BaseDemoDefense.SecondEventId);
            if (demoDefense)
            {
                if (BaseDemoDefense.AnyDue(state) && BaseInteraction.At(state, "perimeter", "demo_defense_start"))
                    OpenBattlePreparation(BaseDemoDefense.SecondDue(state) ? BasePrepRoute.Second : BasePrepRoute.Defense);
                else owner.ReportMessage(BaseDemoDefense.Objective(state));
                return;
            }
            if (!adviceHandled && CompanionAdvice(location, eventId, mode)) return;
            OpenDepartureReview(location, eventId, mode);
        }
    }
}
