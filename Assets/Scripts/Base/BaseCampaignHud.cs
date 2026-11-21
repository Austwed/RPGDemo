using System;
using System.Linq;
using BaseConquest.Battle;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private void OpenTravel(string id)
        {
            if (id == "exit_west") owner.Travel("perimeter");
            else if (id == "exit_east") owner.Travel("town");
            else if (id == "exit_south") owner.Travel("world");
            else if (id == "travel_base") owner.Travel("base");
            else if (id == "campaign_defense") OpenCampaignEvents("defense");
            else if (id == "travel_world") owner.Travel("world");
            else if (id == "campaign_attack") {if(owner.State.Campaign.Location=="world")owner.Travel("outpost");else OpenCampaignEvents("attack");}
            else { BeginPanel("道路封闭"); Line("北侧道路不通。", 100); EndPanel(); }
        }
        public void OpenCampaignEvents(string kind)
        {
            interactionObject="";
            BeginPanel(kind == "defense" ? "迎敌战场 · 敌情" : "据点归属与敌情", "campaign:" + kind);
            RowButton("返回",0,800,ClosePanel);row+=62;
            if(kind=="attack")foreach(var site in owner.State.Campaign.EnemySites.Where(site=>!BaseSceneInstances.Active(owner.State)||site.Id==owner.State.Campaign.CurrentMapId))ThreatSiteLines(site,()=>OpenCampaignEvents(kind));
            else if(!BaseDemo.Active(owner.State))Line(BaseThreats.Warning(owner.State),56,21);
            var events = owner.State.Campaign.Events.Where(e => e.Kind == kind && e.Status=="pending" && (e.Kind!="defense"||BaseRaidIntel.Known(owner.State,e))).OrderBy(e=>e.AvailableDay)
                .Concat(owner.State.Campaign.Events.Where(e=>e.Kind==kind&&e.Status=="resolved"&&(kind!="defense"||!BaseDemo.Active(owner.State)||owner.State.Campaign.Reports.Any(r=>r.EventId==e.Id))).Reverse().Take(4)).ToArray();
            if(kind=="attack"&&BaseSceneInstances.Active(owner.State))events=events.Where(item=>item.SourceSiteId==owner.State.Campaign.CurrentMapId).ToArray();
            if (events.Length == 0) Line(kind == "defense" ? "当前没有待处理入侵，可自由查看外围并返回。" : "当前没有敌点。", 80);
            foreach (var e in events)
            {
                string id = e.Id; Line(e.Title + " · " + (e.Status == "resolved" ? "已处理" : e.AvailableDay > owner.State.Day ? "第" + e.AvailableDay + "天出现" : "待处理"), 52, 22);
                if(e.Kind=="defense")ThreatEventLines(e);
                if (e.Kind == "defense" && e.Status == "pending")
                {
                    string objective = e.Id == BaseDemoDefense.SecondEventId ? "击败来敌" : "坚守 " + (e.HoldRounds > 0 ? e.HoldRounds : 30) + " 秒或击败来敌";
                    string deadline = BaseDeputyBattle.Story(owner.State, id) ? "须亲自迎战" : "最迟第 " + e.DeadlineDay + " 天处理";
                    Line(objective + " · " + deadline, 62, 18);
                }
                var source=BaseThreats.Site(owner.State,e.SourceSiteId);
                bool unopposed=e.Kind=="attack"&&source?.Owner=="enemy"&&!source.Garrison.Any(u=>u.Kind==SoldierKind.Hero);
                RowButton(source?.Owner=="player"&&e.Kind=="attack" ? "已占领" : unopposed ? "接管无指挥者的据点" : kind == "defense" ? "预览迎战" : "预览进攻", 0, 800, () => {if(unopposed)owner.ClaimOutpost(source.Id);else OpenCampaignPreview(id, "full");}, e.Status == "pending" && e.AvailableDay <= owner.State.Day && !owner.State.Campaign.HasActiveBattle); row += 60;
                if (e.Kind == "defense" && e.Status == "pending" && BaseDeputy.CanUse(owner.State)) DeputyRows(id);
            }
            CampaignReports(); EndPanel();
        }
        private bool campaignDetails;
        private void OpenCampaignPreview(string id, string mode)
        {
            var s=owner.State;var e=s.Campaign.Events.Single(x=>x.Id==id);string purpose=e.Kind=="defense"?"defense":"expedition";
            var party=s.Muster.Parties.FirstOrDefault(p=>p.Purpose==purpose);
            if(party==null){OpenDeparture(e.Kind=="defense"?"perimeter":"world",id,mode);return;}
            BeginPanel(e.Title+" · 战前确认","campaign-preview:"+id);
            RowButton("确认进入战场",-210,400,()=>owner.LaunchCampaign(id,mode,null),e.Kind!="attack"||party.Leaders.Any(l=>l.LeaderId==s.PlayerLeaderId));RowButton("返回",210,400,()=>OpenCampaignEvents(e.Kind));row+=64;
            Line("实际队长："+string.Join("、",party.Leaders.Select(x=>BasePersonnel.LeaderName(s.Leaders.Single(l=>l.Id==x.LeaderId))))+"／"+s.Workers.Where(w=>w.Away&&w.PartyId==party.Id).Sum(w=>w.Count)+"兵",60,20);
            var groups=BaseMuster.CurrentFormation(s,party);if(groups!=null)foreach(var group in groups)Line(BasePersonnel.LeaderName(s.Leaders.Single(l=>l.Id==group.LeaderId))+" · "+TroopsText(group.Actual),50,18);
            if(e.Kind=="defense")ThreatEventLines(e);
            else {var source=BaseThreats.Site(s,e.SourceSiteId);if(source!=null)Line("敌方驻军："+BaseThreats.ForceSummary(source.Garrison),64,18);}
            var held=s.Supply.Quotas.FirstOrDefault(q=>q.PartyId==party.Id);
            if(held==null){foreach(string key in BaseSupply.Modes){string option=key;RowButton((mode==option?"● ":"")+BaseSupply.ModeName(option),option=="full"?-290:option=="half"?0:290,270,()=>OpenCampaignPreview(id,option));}row+=62;}
            else Line("沿用本队已准备军需",50,19);
            RowButton(campaignDetails?"收起人员／供给明细":"人员／供给明细",0,800,()=>{campaignDetails=!campaignDetails;OpenCampaignPreview(id,mode);});row+=60;
            if(campaignDetails)try{foreach(var w in s.Workers.Where(w=>w.PartyId==party.Id&&w.Away))Line(BaseEconomy.TroopName(w.Kind)+" "+w.Count+"人，生命 "+w.Health+"/"+w.MaxHealth,44,18);SupplyLines(held??BaseSupply.Preview(s,party.Id,mode).Quota);}catch(Exception error){Line(error.Message,75);}
            var f=s.Aftermath.Difficulty.RewardFactor;Line("胜利奖励：粮 "+(int)(e.FoodReward*f)+"／原料 "+(int)(e.MaterialReward*f)+"／币 "+(int)(e.CoinReward*f),64,19);EndPanel();
        }
        // 副手迎战入口（P43）：剧情防守不给按钮，其余袭击给出后台模拟与观战两条路。
        private void DeputyRows(string id)
        {
            var s = owner.State;
            if (BaseDeputyBattle.Story(s, id))
            {
                Line("剧情防守：必须玩家亲自指挥，不能交给副手。", 60, 18);
                return;
            }
            string problem = BaseDeputyBattle.Problem(s, id);
            if (problem != "")
            {
                Line("副手迎战不可用：" + problem, 58, 18);
                return;
            }
            var deputy = BaseDeputy.Leader(s);
            Line("副手迎战：" + BasePersonnel.LeaderName(deputy) + "带领本地守军，主角与随行队伍留在原地。", 60, 18);
            RowButton("副手迎战 · 后台模拟（直接看结果）", 0, 800, () => owner.LaunchDeputyDefense(id, false));
            row += 58;
            RowButton("副手迎战 · 观战（只看／暂停／调速／改策略）", 0, 800, () => owner.LaunchDeputyDefense(id, true));
            row += 58;
        }
        private void CampaignReports()
        {
            foreach (var r in owner.State.Campaign.Reports.OrderByDescending(r => r.Day).ThenByDescending(r => owner.State.Campaign.Reports.IndexOf(r)).Take(4))
            {
                Line(r.Title + " · 第" + r.Day + "天 · " + (r.Deputy ? "副手迎战" : r.Passive ? "过夜迎战" : "主动战斗") + " · " + BaseCampaign.OutcomeName(r.Outcome), 65, 21);
                if(!string.IsNullOrEmpty(r.RecoveryNote))Line(r.RecoveryNote,65,20);
                if(!string.IsNullOrEmpty(r.RaidNote))Line(r.RaidNote,64,20);
                if(r.Realtime)Line("即时作战 "+r.Seconds.ToString("0")+"秒",48,18);
                Line("部署 " + r.Deployed + "；阵亡 " + r.Killed + "／失联 " + r.Missing + "；返营 " + r.Returned + "／伤兵 " + r.Wounded + "；倒地队长 " + r.LeadersDown + "；军需消耗 " + r.GearLost, 86, 18);
                Line("战利品／劫掠：粮 " + r.FoodChange.ToString("+0;-0;0") + "／原料 " + r.MaterialChange.ToString("+0;-0;0") + "／钱币 " + r.CoinChange.ToString("+0;-0;0"), 67, 18);
                if(r.DropMaterials>0) Line((r.EventId==BaseDemoDefense.EventId?"击倒回收原料 +":"敌军掉落原料 +")+r.DropMaterials,52,18);
                if(!string.IsNullOrEmpty(r.SourceSiteId))Line("敌军：部署 "+r.EnemyDeployed+"／战损 "+r.EnemyLost+"／俘获 "+r.EnemyCaptured+"／撤回来源 "+r.EnemyReturned+"／溃散 "+r.EnemyDispersed,70,18);
            }
        }
    }
}
