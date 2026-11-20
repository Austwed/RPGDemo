using System;
using System.Linq;
using UnityEngine;
namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private string compositionLeader;
        private int compositionPage;
        private bool formationForecastOpen;
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
            Line("保存期望编制，不提前占用士兵。出征按常驻小队顺序补兵。",60,18);
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
            Line("各队长可保存预设，实际出战全军最多24名士兵，超出部分显示缺编。未随行者保存预设但不会自动入队。",86,18);
        }
        public void OpenDeparture(string location,string eventId=null,string mode="full",bool bypass=false,bool adviceHandled=false)
        {
            interactionObject="";
            if(!adviceHandled&&CompanionAdvice(location,eventId,mode,bypass))return;
            var s=owner.DepartureState(location);string purpose=location=="world"||location=="outpost" ? "expedition" : "defense";
            if(s.Muster.Parties.Any(p=>p.Purpose==purpose)){OpenDepartureReview(location,eventId,mode,bypass);return;}
            BaseMusterPlan production=null,combat=null;string problem="";
            try{production=BaseMuster.PreviewFormation(s,purpose,"production");combat=BaseMuster.PreviewFormation(s,purpose,"combat");}catch(Exception e){problem=e.Message;}
            BeginPanel((purpose=="expedition" ? "出征" : "迎战")+" · 选择兵力安排","departure:choice:"+location+":"+eventId);
            bool CanUse(BaseMusterPlan p)=>p!=null&&p.Leaders.Count>0&&(purpose!="expedition"||p.Leaders.Contains(s.PlayerLeaderId));
            RowButton("生产优先",-210,400,()=>{departure=null;OpenDepartureReview(location,eventId,mode,false);},CanUse(production)&&production.Production?.Allowed==true);
            RowButton("作战优先",210,400,()=>{departure=null;OpenDepartureReview(location,eventId,mode,true);},CanUse(combat));row+=64;
            if(problem!="")Line(problem,80,18);
            Line("点击模式查看名单并最终确认；此页不占人、不扣资源。",52,18);
            if(production!=null)
            {
                Line(production.Production?.Allowed==true ? "生产优先：可保障三天经营 · 携带 "+production.Total+"兵" : "生产优先不可用："+(production.Production?.Problem??"无可出战队长"),76,18);
                if(production.ConserveMaterials)Line("原料紧张：外出期间工坊按最低开工人数生产，余员保留在基地。",58,18);
            }
            if(combat!=null)Line("作战优先：携带 "+combat.Total+"兵；预计日产变化：粮 "+(-combat.FoodLoss).ToString("+0;-0;0")+"／产品 "+(-combat.ProductLoss).ToString("+0;-0;0"),60,18);
            if(combat?.CommanderTroops!=null)foreach(var entry in combat.CommanderTroops)
            {
                var safe=production?.CommanderTroops?.FirstOrDefault(x=>x.LeaderId==entry.LeaderId);
                Line(BasePersonnel.LeaderName(s.Leaders.Single(l=>l.Id==entry.LeaderId))+" · 需求 "+TroopsText(entry.Requested)+"\n生产优先 "+(safe==null ? "无" : TroopsText(safe.Actual))+"\n作战优先 "+TroopsText(entry.Actual),106,19);
            }
            RowButton(formationForecastOpen ? "收起三天预测" : "三天预测／规则",0,800,()=>{formationForecastOpen=!formationForecastOpen;OpenDeparture(location,eventId,mode,bypass);});row+=58;
            if(formationForecastOpen)
            {
                if(production?.Production!=null)foreach(var d in production.Production.Days)Line("第"+d.Day+"天：粮田 "+d.FarmWorkers+"人／工坊 "+d.WorkshopWorkers+"人，产粮 "+d.FoodProduced+"，耗原料 "+d.MaterialsSpent+"；日结及次晨后粮 "+d.FoodAfter+"／原料 "+d.MaterialsAfter,76,18);
                Line("生产优先按管理所最低人数（至少1人）保留各产线，允许减产；逐日计算现有粮草与原料，不计战利品或交易收入。预测不承诺入侵战果。作战优先可抽调生产人员，兵源不足按队长顺序缺编；生命低于30%或外出者不可选，守家迎战不受这条限制。自动迎战采用作战优先。",132,18);
            }
            EndPanel();
        }
    }
}
