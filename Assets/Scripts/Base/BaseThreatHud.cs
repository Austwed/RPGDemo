using System.Linq;
using BaseConquest.Battle;
namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private bool enemyRosterDetails;
        private void OpenThreatOverview()
        {
            BeginPanel("敌情与据点");
            RowButton("关闭",-210,400,ClosePanel);RowButton("规则 i",210,400,()=>ThreatRules(OpenThreatOverview));row+=62;
            Line(BaseThreats.Warning(owner.State),56,22);
            foreach(var site in owner.State.Campaign.EnemySites)ThreatSiteLines(site,OpenThreatOverview);
            foreach(var e in owner.State.Campaign.Events.Where(e=>e.Kind=="defense"&&e.Status=="pending").OrderBy(e=>e.AvailableDay))
            {Line(e.Title,48,21);ThreatEventLines(e);}
            if(owner.State.Campaign.EnemySites.Count==0)Line("当前战斗保留原有快照；结束后载入据点会接入持续敌情。",80,19);
            EndPanel();
        }
        private void ThreatSiteLines(BaseEnemySite site,System.Action refresh)
        {
            Line(site.Name+" · "+(site.Owner=="player" ? "己方（第"+site.CapturedDay+"天占领）" : "敌方"),54,22);
            if(site.Owner=="player")Line("该据点停止补员与派兵；已经出发的入侵仍按原日程抵达。当前暂不提供额外收益或驻防管理。",85,18);
            else
            {
                Line("驻军："+BaseThreats.ForceSummary(site.Garrison),65,19);
                Line("最早第"+site.NextDispatchDay+"天再次出兵；行军"+site.TravelDays+"天。同一来源最多一支待处理入侵。",70,18);
            }
            var marching=owner.State.Campaign.Events.Where(e=>e.SourceSiteId==site.Id&&e.Kind=="defense"&&e.Status=="pending").ToArray();
            if(marching.Length>0)Line("已出发："+marching.Length+"支；最近第"+marching.Min(e=>e.AvailableDay)+"天抵达。",54,19);
            if(site.Owner!="enemy")return;
            RowButton(enemyRosterDetails ? "收起驻军详情" : "查看驻军详情",0,800,()=>{enemyRosterDetails=!enemyRosterDetails;refresh();});row+=60;
            if(enemyRosterDetails)foreach(var u in site.Garrison)
                Line(u.Name+(u.Dead?" · 已死亡":u.KnockedDown?" · 倒地":"")+" · "+(u.Kind==SoldierKind.Hero&&u.Stats.HasProfessionKind?BattleSimulation.KindName(u.Stats.ProfessionKind)+"队长":BattleSimulation.KindName(u.Kind))+" · "+u.Stats.Level+"级 · 生命 "+u.Health.ToString("0.#")+"/"+u.Stats.Health.ToString("0.#")+" · 攻 "+u.Stats.Damage.ToString("0.#")+" 防 "+u.Stats.Defense.ToString("0.#"),54,18);
        }
        private void ThreatEventLines(BaseCampaignEvent e)
        {
            if(string.IsNullOrEmpty(e.SourceSiteId))return;
            var site=BaseThreats.Site(owner.State,e.SourceSiteId);
            Line("来源："+site.Name+" · 第"+e.DepartedDay+"天出发 → 第"+e.AvailableDay+"天抵达"+(site.Owner=="player" ? "（来源已占领，在途仍有效）" : ""),68,18);
            if(e.EnemyArmy.Count>0)Line("入侵队："+BaseThreats.ForceSummary(e.EnemyArmy),62,18);
            else if(e.Status=="pending")Line("原有来袭部队保留原模板编制，实际交战时冻结。",52,18);
        }
        private void ThreatRules(System.Action back)
        {
            if(BaseDemo.Active(owner.State)){RuleInfo("Demo残军规则","敌军血量、倒地和死亡随战果保存。同日重新进攻不恢复。经营过日时，倒地者恢复行动，半血以下恢复至半血，半血及以上恢复至满血；死者不复活，不额外补员。首次防守败退按入侵配置扣一次经济损失，残军撤走，次日再袭；胜利才推进主线。",back);return;}
            RuleInfo("持续敌情规则","时间只随经营日推进，走动、读档和查看页面不推进。敌方驻军保留实际生命与战损；默认每日恢复20生命，每2天按驻军总编制空缺补员，近战最多补2人，其余每类最多1人。行军中的人员也占编制，不能同时存在于驻军。\n\n首次日期以敌情面板为准；默认接入敌情3天后首次派兵，之后间隔4天，行军2天；同一来源最多一支待处理入侵，驻军不足时推迟。留下至少一名队长驻防，派出1名队长和有限士兵；实际人数见敌情。\n\n抵达后可主动迎战，或在该日过夜时自动处理，仍是坚守30轮或提前歼灭。胜败、即死、俘获与救援按实际战斗结算。敌军幸存者回到仍属敌方的来源，伤势保留。\n\n攻占据点停止它今后的补员与出兵；已出发的部队继续抵达，来源已失则幸存者溃散。占领奖励只给一次，己方据点不能重复进攻。若守军已无队长，可带主角出征到现场接管。当前敌军补员为配置驱动，敌方经济与占领收益后续接入。",back);
        }
    }
}
