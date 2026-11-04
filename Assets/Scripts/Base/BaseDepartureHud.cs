using System;
using System.Linq;
using UnityEngine;
namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private BaseDepartureSelection departure;
        private string departureKey, departureMessage="";
        private BaseTroopKind? departureExpanded;
        private int departureUnitPage;
        private void OpenDepartureReview(string location,string eventId=null,string mode="full",bool bypass=false)
        {
            string purpose=location=="world"||location=="outpost" ? "expedition" : "defense",key=location+":"+eventId;
            var s=owner.DepartureState(location);var existing=s.Muster.Parties.FirstOrDefault(p=>p.Purpose==purpose);
            if(existing==null&&(departureKey!=key||departure==null||departure.Stamp!=BaseMuster.Snapshot(s)))
            {departureKey=key;departure=new BaseDepartureSelection(s,purpose,bypass,bypass ? "combat" : "production");departureExpanded=null;departureUnitPage=0;departureMessage="";}
            BaseMusterPlan plan=null;string error="";
            if(existing==null)try {plan=departure.Preview(s,bypass);}catch(Exception e){error=e.Message;}
            string shownMode=existing==null ? BaseMuster.ModeName(bypass ? "combat" : "production") : string.IsNullOrEmpty(existing.PriorityMode) ? "已有队伍" : BaseMuster.ModeName(existing.PriorityMode);
            BeginPanel((purpose=="expedition" ? "出征 · " : "迎战 · ")+shownMode,"departure:"+key);
            var captured=plan;
            bool valid=existing!=null ? purpose!="expedition"||existing.Leaders.Any(l=>l.LeaderId==s.PlayerLeaderId) : plan!=null&&plan.Leaders.Count>0&&(bypass||plan.Production?.Allowed==true)&&(purpose!="expedition"||plan.Leaders.Contains(s.PlayerLeaderId));
            RowButton(eventId==null ? BaseDemoDefense.Enabled(s)&&s.Campaign.Location=="perimeter"&&location=="perimeter"?"确认队伍并选择军需":"确认队伍并进入区域" : "确认队伍并开战",-290,270,()=>{if(eventId==null)owner.CompleteTravel(location,captured);else owner.LaunchCampaign(eventId,mode,captured);},valid);
            RowButton("返回",0,270,()=>{if(existing!=null)OpenMenu();else OpenDeparture(location,eventId,mode,bypass);});
            RowButton("规则 i",290,270,()=>RuleInfo("逐兵选取规则","点击兵种展开名单，可逐个勾选；加减与名单共用同一选择，不能超出真实可调人数。表内显示基础攻防、当前／最大生命、设施与今天的生产资格；生命低于50%、外出或无法行动者不可选。生产优先需要保障三天正常开工，作战优先可抽调生产人员；兵力按队长预设先后填补。实际出征时再计算军需与临时效果。进入世界地图即出征，走到节点进入区域，迎战在原区域内开战。",()=>OpenDepartureReview(location,eventId,mode,bypass)));row+=64;
            if(existing!=null)
            {
                int carried=s.Workers.Where(w=>w.PartyId==existing.Id&&w.Away).Sum(w=>w.Count);
                Line("队长："+string.Join("、",existing.Leaders.Select(x=>BasePersonnel.LeaderName(s.Leaders.Single(l=>l.Id==x.LeaderId)))),60);
                Line("已携带／可调总量（含本队）："+carried+"／"+(carried+s.Workers.Where(BaseDevelopment.CanFight).Sum(w=>w.Count)),54);
                var groups=BaseMuster.CurrentFormation(s,existing);if(groups!=null)foreach(var group in groups)Line(BasePersonnel.LeaderName(s.Leaders.Single(l=>l.Id==group.LeaderId))+" · "+TroopsText(group.Actual),50,18);
                RowButton("查看本队／军需／战外追加",0,800,()=>OpenParty(existing.Id,()=>OpenDepartureReview(location,eventId,mode,bypass)));row+=60;EndPanel();return;
            }
            void Reopen()=>OpenDepartureReview(location,eventId,mode,bypass);
            RowButton("重新读取默认预设",-210,400,()=>{departure=new BaseDepartureSelection(s,purpose,bypass,bypass ? "combat" : "production");departureMessage="已按可用人数读取预设";Reopen();});
            RowButton("返回模式选择",210,400,()=>OpenDeparture(location,eventId,mode,bypass));row+=60;
            if(error!="")Line(error,76,18);
            if(!bypass&&plan?.Production?.Allowed!=true)Line(plan?.Production?.Problem??"无法保障三天经营",76,18);
            if(departureMessage!="")Line(departureMessage,55,18);
            Line("已选士兵 "+departure.Picks.Count+"人；常驻队长 "+departure.Preset.Candidates.Count+"人（上限3人）",48,20);
            foreach(var kind in Enum.GetValues(typeof(BaseTroopKind)).Cast<BaseTroopKind>())
            {
                var k=kind;var capacity=departure.Capacity(s,k,bypass);int count=capacity.Selected;
                RowButton((departureExpanded==k ? "▾ " : "▸ ")+BaseEconomy.TroopName(k)+" · "+capacity.Summary,-155,500,()=>{departureExpanded=departureExpanded==k ? (BaseTroopKind?)null : k;departureUnitPage=0;Reopen();});
                RowButton("−",160,100,()=>{departure.Adjust(s,k,-1,bypass,out departureMessage);Reopen();},count>0);
                RowButton("+",300,100,()=>{departure.Adjust(s,k,1,bypass,out departureMessage);Reopen();},count<capacity.Limit);row+=52;
                Line(capacity.Detail,60,17);
                if(BaseTown.DemoRecruitment(s))Line("全军普通兵 "+BaseTown.TroopTotal(s)+"人（含外出） · 合计基准 "+BaseDemoCombat.BaselineTotal(s.Demo?.Rpg)+" · 普通兵即死率 "+BaseTown.HireProbability(s).ToString("P0")+"；三兵种共用，调岗出征不改变总数。",62,17);
                if(departureExpanded!=k)continue;
                var rows=BaseMuster.SoldierRows(s,k).ToArray();int pages=Math.Max(1,(rows.Length+7)/8);departureUnitPage=Math.Max(0,Math.Min(pages-1,departureUnitPage));
                Line("基础攻击／防御 · 当前／最大生命 · 所在设施 · 今日生产资格",42,17);
                int number=departureUnitPage*8;
                foreach(var unit in rows.Skip(departureUnitPage*8).Take(8))
                {
                    var pick=unit.Pick;bool selected=departure.Selected(pick);bool allowed=selected||departure.CanToggle(s,pick,bypass,out _);number++;
                    string status=unit.Available ? unit.EffectiveWorker ? "今日可生产" : "今日不生产" : unit.UnavailableReason+" · 今日不生产";
                    Label(content,BaseConquest.Battle.UnitDisplayNames.Mark(BaseEconomy.TroopName(k)+" #"+number,unit.Recruited)+"  攻 "+unit.Stats.Damage.ToString("0.#")+"  防 "+unit.Stats.Defense.ToString("0.#")+"  生命 "+unit.Health.ToString("0.#")+"／"+unit.MaxHealth.ToString("0.#")+"\n"+unit.Facility+" · "+status,new Vector2(-70,-row-38),new Vector2(680,76),18,TextAnchor.MiddleLeft,new Vector2(.5f,1));
                    RowButton(selected ? "● 已选" : allowed ? "选择" : "不可调",350,120,()=>{departure.Toggle(s,pick,bypass,out departureMessage);Reopen();},allowed);row+=80;
                }
                RowButton("上一页",-290,240,()=>{departureUnitPage--;Reopen();},departureUnitPage>0);RowButton("第 "+(departureUnitPage+1)+"／"+pages+" 页",0,240,()=>{},false);RowButton("下一页",290,240,()=>{departureUnitPage++;Reopen();},departureUnitPage+1<pages);row+=60;
            }
            foreach(var leader in BaseAdventure.Members(s))
            {
                Line(BasePersonnel.LeaderName(leader)+" · 生命 "+leader.Health.ToString("0.#")+"/"+leader.MaxHealth.ToString("0.#")+(BaseDevelopment.CanFight(leader) ? " · 随队" : " · 当前不可出战"),50,20);
                var group=plan?.CommanderTroops?.FirstOrDefault(g=>g.LeaderId==leader.Id);if(group!=null)Line("实际 "+TroopsText(group.Actual)+"／需求 "+TroopsText(group.Requested),58,18);
            }
            Line("队长名单固定沿用冒险小队；请回基地与伙伴对话调整。",56,18);
            if(!valid)Line("出征需带主角，且至少选择一名可出战队长。",55,18);
            if(plan!=null)Line("净产出变化：粮 "+(-plan.FoodLoss).ToString("+0;-0;0")+"／产品 "+(-plan.ProductLoss).ToString("+0;-0;0"),55,18);
            if(BaseTown.DemoRecruitment(s))Line("出征人员仍计入全军口粮：每日 "+BaseEconomy.FoodRequired(s)+"；抽走岗位人员的产出影响见上方预览。",65,18);
            EndPanel();
        }
    }
}
