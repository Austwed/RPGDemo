using System;
using System.Linq;
namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        public void OpenInn()
        {
            var s=owner.State;activeObject=interactionObject="town_inn";
            // TOWN_04_INN_WELCOME：Demo 内首次进店由麦格招呼一次，之后不再打扰。
            if(BaseDemoFlow.Active(s)&&!s.Demo.Flow.InnWelcomed)
            {
                owner.Change(next=>next.Demo.Flow.InnWelcomed=true,"");
                NpcDialogue(new[]{new NpcLine(BaseScriptNames.InnKeeper,BaseScriptNames.Portrait("innkeeper"),"热汤麦酒管够！随行休息每位队长 "+BaseInn.Fee(s)+" 金币，长期寄宿也是每天 "+BaseInn.Fee(s)+" 金币，童叟无欺。")},
                    new[]{new NpcReply("进店看看",()=>{ClosePanel();OpenInn();})});
                return;
            }
            BeginPanel(BaseInn.Free(s)?BaseScriptNames.Village+" · 临时扎营点":BaseScriptNames.Town+" · 旅店","town-inn");
            Line("当地寄存 "+BaseInn.Occupied(s)+" / "+BaseInn.Capacity+" · 随行 "+s.Adventure.Members.Count+" / 3 · 营帐 "+BaseRpg.Population(s)+" / "+BaseRpg.Capacity(s),60,21);
            Line(BaseInn.Free(s)?"此处免费休息、留队，最多额外留守3名队长。":"住宿费用：每人每天 "+BaseInn.Fee(s)+" 金币（入住预付首日）",55,20);
            Line("过日自动结算各旅店住客费用，钱不足记欠费，接走前补清；免费扎营者不计费。",60,18);
            Line("士兵不寄存；随行队长入住后释放名额，其士兵留在出征队由其他队长接管。口粮仍按全部所属人员每日结算。",75,18);
            int day=s.Day,cost=BaseInn.Fee(s)*BaseAdventure.Members(s).Count();
            RowButton("休息到次日 · "+cost+" 金币",0,820,()=>{owner.Change(next=>{if(!BaseInn.Rest(next,day))throw new InvalidOperationException(BaseInn.RestProblem(next));},"已在旅店休息并结算一天");OpenInn();},BaseInn.RestProblem(s)=="");row+=62;
            // TOWN_04_INN_POOR：付不起店钱时由麦格指一条不花钱的路，具体原因照旧显示，不丢信息。
            if(BaseInn.RestProblem(s)!=""){Line(BaseScriptNames.InnKeeper+"：手头紧？要是实在凑不出住宿钱，回"+BaseScriptNames.Camp+"自家营帐歇息是不花钱的。",60,18);Line(BaseInn.RestProblem(s),48,18);}
            var guests=s.Inn.Guests.Where(g=>g.AreaId==BaseInn.Area(s)).ToArray();
            foreach(var g in guests)
            {
                string id=g.LeaderId;var l=s.Leaders.Single(p=>p.Id==id);
                Line(BasePersonnel.LeaderName(l)+" · 生命 "+l.Health.ToString("0.#")+" / "+l.MaxHealth.ToString("0.#")+" · 欠费 "+g.Debt+" 金币",56,21);
                foreach(bool join in new[]{true,false})
                {bool follow=join;RowButton(join?"接回随行":"送回本领地基地",join?-210:210,400,()=>{owner.Change(next=>{if(!BaseInn.Collect(next,id,follow))throw new InvalidOperationException(BaseInn.CollectProblem(next,id,follow));},follow?"伙伴已恢复随行":"队长已回到本领地基地");OpenInn();},BaseInn.CollectProblem(s,id,join)=="");}row+=62;
                string problem=BaseInn.CollectProblem(s,id,true);if(problem!="")Line(problem,45,18);
            }
            if(guests.Length==0)Line("当前没有寄存的队长。",60,20);
            foreach(var l in BaseAdventure.Members(s).Where(l=>l.Id!=s.PlayerLeaderId).ToArray())
            {string id=l.Id;RowButton("让 "+l.Name+" 暂离并入住",0,820,()=>{owner.Change(next=>{if(!BaseInn.CheckIn(next,id))throw new InvalidOperationException(BaseInn.CheckInProblem(next,id));},"伙伴已入住，随行名额释放");OpenInn();},BaseInn.CheckInProblem(s,id)=="");row+=60;}
            EndPanel();
        }
    }
}
