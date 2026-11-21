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
            RowButton("关闭",0,800,ClosePanel);row+=62;
            if(!BaseDemo.Active(owner.State))Line(BaseThreats.Warning(owner.State),56,22);
            foreach(var site in owner.State.Campaign.EnemySites)ThreatSiteLines(site,OpenThreatOverview);
            foreach(var e in owner.State.Campaign.Events.Where(e=>e.Kind=="defense"&&e.Status=="pending"&&BaseRaidIntel.Known(owner.State,e)).OrderBy(e=>e.AvailableDay))
            {Line(e.Title,48,21);ThreatEventLines(e);}
            if(owner.State.Campaign.EnemySites.Count==0)Line("当前没有驻军情报。",80,19);
            EndPanel();
        }
        private void ThreatSiteLines(BaseEnemySite site,System.Action refresh)
        {
            Line(site.Name+" · "+(site.Owner=="player" ? "己方（第"+site.CapturedDay+"天占领）" : "敌方"),54,22);
            if(site.Owner=="player")Line("该据点已停止派兵。",85,18);
            else
            {
                Line("驻军："+BaseThreats.ForceSummary(site.Garrison),65,19);
                if(!BaseDemo.Active(owner.State))Line("最早第"+site.NextDispatchDay+"天再次出兵；行军"+site.TravelDays+"天。",70,18);
            }
            var marching=owner.State.Campaign.Events.Where(e=>e.SourceSiteId==site.Id&&e.Kind=="defense"&&e.Status=="pending"&&BaseRaidIntel.Known(owner.State,e)).ToArray();
            if(marching.Length>0)Line("已出发："+marching.Length+"支；最近第"+marching.Min(e=>e.AvailableDay)+"天抵达。",54,19);
            if(site.Owner!="enemy")return;
            RowButton(enemyRosterDetails ? "收起驻军详情" : "查看驻军详情",0,800,()=>{enemyRosterDetails=!enemyRosterDetails;refresh();});row+=60;
            if(enemyRosterDetails)foreach(var u in site.Garrison)
                Line(u.Name+(u.Dead?" · 已死亡":u.KnockedDown?" · 倒地":"")+" · "+(u.Kind==SoldierKind.Hero&&u.Stats.HasProfessionKind?BattleSimulation.KindName(u.Stats.ProfessionKind)+"队长":BattleSimulation.KindName(u.Kind))+" · "+u.Stats.Level+"级 · 生命 "+u.Health.ToString("0")+"/"+u.Stats.Health.ToString("0")+" · 攻 "+u.Stats.Damage.ToString("0")+" 防 "+u.Stats.Defense.ToString("0"),54,18);
        }
        private void ThreatEventLines(BaseCampaignEvent e)
        {
            if(string.IsNullOrEmpty(e.SourceSiteId))return;
            var site=BaseThreats.Site(owner.State,e.SourceSiteId);
            Line("来源："+site.Name+" · 第"+e.DepartedDay+"天出发 → 第"+e.AvailableDay+"天抵达"+(site.Owner=="player" ? "（来源已占领，在途仍有效）" : ""),68,18);
            if(e.EnemyArmy.Count>0)Line("入侵队："+BaseThreats.ForceSummary(e.EnemyArmy),62,18);
        }
    }
}
