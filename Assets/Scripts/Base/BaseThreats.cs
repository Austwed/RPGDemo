using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
namespace BaseConquest.Base
{
    public static class BaseThreats
    {
        public static BaseEnemySite Site(BaseState s,string id="outpost") => s.Campaign.EnemySites?.FirstOrDefault(x=>x.Id==(id=="outpost"&&BaseSceneInstances.Active(s)?s.Campaign.Location=="outpost"?s.Campaign.CurrentMapId:s.Campaign.Layouts.First(map=>map.Region=="outpost").SourceMapId:id));
        public static bool Enable(BaseState s)
        {
            var c=s.Campaign;
            if(c.ThreatVersion!=0||c.HasActiveBattle||s.Aftermath.GameOver)return false;
            if(BaseSceneInstances.Active(s))return EnableInstances(s);
            foreach(var e in c.Events)e.EnemyArmy??=new List<BaseEnemyMember>();
            var attack=c.Events.FirstOrDefault(e=>e.Kind=="attack");
            if(attack==null)
            {
                string id="outpost_demo_001";while(c.Events.Any(e=>e.Id==id))id+="_new";
                attack=new BaseCampaignEvent {Id=id,Title="荒野敌军据点",Kind="attack",ScenarioFile="24_据点进攻.json",AvailableDay=s.Day,DeadlineDay=s.Day,FoodReward=18,MaterialReward=12,CoinReward=25,FoodRaid=0,MaterialRaid=0,CoinRaid=0};c.Events.Add(attack);
            }
            var site=new BaseEnemySite {Name=attack.Title,AttackEventId=attack.Id,Owner=attack.Status=="resolved" ? "player" : "enemy",NextDispatchDay=Date(s.Day,3),LastReinforcementDay=s.Day};
            if(site.Owner=="player")site.CapturedDay=c.Reports.Where(r=>r.EventId==attack.Id&&r.Outcome=="SideAWin").Select(r=>r.Day).DefaultIfEmpty(s.Day).Min();
            else foreach(var kind in BattleSimulation.TroopKinds.Prepend(SoldierKind.Hero))
                for(int n=0;n<site.Capacity.Count(kind);n++)site.Garrison.Add(NewMember(site,kind));
            c.EnemySites=new List<BaseEnemySite>{site};c.ThreatVersion=1;c.ThreatDay=s.Day;attack.SourceSiteId=site.Id;
            // 旧入侵已出发，保留原到达日和模板。首次开战才从原模板冻结它自己的编制。
            foreach(var raid in c.Events.Where(e=>e.Kind=="defense"&&e.Status=="pending"&&e.Id=="raid_demo_001"))
            {raid.SourceSiteId=site.Id;raid.DepartedDay=Math.Max(1,Math.Min(s.Day,raid.AvailableDay));}
            return true;
        }
        private static bool EnableInstances(BaseState state)
        {
            var campaign=state.Campaign;var template=campaign.Events.FirstOrDefault(item=>item.Kind=="attack")?.Copy()??new BaseCampaignEvent{Kind="attack",ScenarioFile="24_据点进攻.json"};
            campaign.Events.RemoveAll(item=>item.Kind=="attack");campaign.EnemySites=new List<BaseEnemySite>();
            foreach(var map in campaign.Layouts.Where(map=>map.Region=="outpost"))
            {
                var attack=template.Copy();attack.Id="attack:"+map.SourceMapId;attack.Title=map.Title;attack.Status="pending";attack.SourceSiteId=map.SourceMapId;attack.EnemyArmy.Clear();
                var site=new BaseEnemySite{Id=map.SourceMapId,Name=map.Title.Length>40?map.Title.Substring(0,40):map.Title,AttackEventId=attack.Id,NextDispatchDay=Date(state.Day,3),LastReinforcementDay=state.Day};
                if((map.Enemy?.Version??0)>0)map.Enemy.Apply(site);
                foreach(var kind in BattleSimulation.TroopKinds.Prepend(SoldierKind.Hero))for(int number=0;number<site.Capacity.Count(kind);number++)site.Garrison.Add(NewMember(site,kind,preset:map.Enemy));
                campaign.Events.Add(attack);campaign.EnemySites.Add(site);
            }
            foreach(var raid in campaign.Events.Where(item=>item.Kind=="defense"&&item.Status=="pending")){raid.SourceSiteId=campaign.EnemySites[0].Id;raid.DepartedDay=Math.Max(1,Math.Min(state.Day,raid.AvailableDay));raid.EnemyArmy??=new List<BaseEnemyMember>();}
            campaign.ThreatVersion=1;campaign.ThreatDay=state.Day;return true;
        }
        public static void InstallPreset(BaseEnemySite site,BaseEnemyPreset preset)
        {
            preset.Apply(site);site.Garrison.Clear();
            foreach(var kind in BattleSimulation.TroopKinds.Prepend(SoldierKind.Hero))
                for(int number=0;number<site.Capacity.Count(kind);number++)site.Garrison.Add(NewMember(site,kind,preset:preset));
        }
        private static int Date(int day,int offset)=>Math.Min(1000000,checked(day+offset));
        private static BaseEnemyMember NewMember(BaseEnemySite site,SoldierKind kind,IEnumerable<BaseEnemyMember> away=null,BaseEnemyPreset preset=null)
        {
            int slot=-1;
            if(kind==SoldierKind.Hero&&site.Capacity.CommanderStats.Count>0)
            {
                var used=new HashSet<int>(site.Garrison.Concat(away??Enumerable.Empty<BaseEnemyMember>()).Where(unit=>unit.Kind==SoldierKind.Hero).Select(unit=>unit.PresetSlot));
                slot=Enumerable.Range(0,site.Capacity.HeroCount).First(index=>!used.Contains(index));
            }
            int number=site.NextUnitNumber++;var stats=(slot<0?site.Capacity.Stats(kind):site.Capacity.CommanderProfile(slot)).Clone();
            BaseEnemyDeployment deployment=null;
            if(preset?.Version==2)
            {
                var used=new HashSet<string>(site.Garrison.Concat(away??Enumerable.Empty<BaseEnemyMember>()).Where(unit=>unit.HasDeployment).Select(unit=>unit.Deployment.Id));
                deployment=preset.Units.First(unit=>unit.Kind==kind&&!used.Contains(unit.Id)&&(kind!=SoldierKind.Hero||unit.CommanderSlot==slot)).Copy();
            }
            return new BaseEnemyMember {Deployment=deployment,Id=site.Id+":unit:"+number,Name=kind==SoldierKind.Hero ? "荒野队长"+number : BattleSimulation.KindName(kind)+" "+number,Kind=kind,PresetSlot=slot,CommanderSlot=kind==SoldierKind.Hero?Math.Max(0,slot):deployment?.CommanderSlot??0,Stats=stats,Health=stats.Health};
        }
        public static void AdvanceDay(BaseState s)
        {
            if(BaseDemo.Active(s)){BaseDemoCombat.RecoverEnemies(s);s.Campaign.ThreatDay=s.Day;return;}
            var c=s.Campaign;if(c.ThreatVersion==0||c.ThreatDay==s.Day)return;
            if(c.ThreatDay!=s.Day-1||c.HasActiveBattle)throw new InvalidOperationException("敌情必须逐经营日推进，不能跳日或在战斗中推进");
            foreach(var site in c.EnemySites.Where(x=>x.Owner=="enemy"))
            {
                foreach(var unit in site.Garrison)unit.Health=Math.Min(unit.Stats.Health,unit.Health+site.RecoveryPerDay);
                if(s.Day-site.LastReinforcementDay>=site.ReinforcementInterval)
                {
                    foreach(var kind in BattleSimulation.TroopKinds.Prepend(SoldierKind.Hero))
                    {
                        int add=Math.Min(kind==SoldierKind.Melee ? 2 : 1,Math.Max(0,site.Capacity.Count(kind)-site.Garrison.Count(u=>u.Kind==kind)-c.Events.Where(e=>e.SourceSiteId==site.Id&&e.Kind=="defense"&&e.Status=="pending").Sum(e=>e.EnemyArmy.Count(u=>u.Kind==kind))));
                        for(int n=0;n<add;n++)site.Garrison.Add(NewMember(site,kind,c.Events.Where(item=>item.SourceSiteId==site.Id&&item.Kind=="defense"&&item.Status=="pending").SelectMany(item=>item.EnemyArmy),c.Layouts.FirstOrDefault(map=>map.SourceMapId==site.Id)?.Enemy));
                    }
                    site.LastReinforcementDay=s.Day;
                }
                if(s.Day<site.NextDispatchDay||c.Events.Any(e=>e.SourceSiteId==site.Id&&e.Kind=="defense"&&e.Status=="pending"))continue;
                var leader=site.Garrison.Where(u=>u.Kind==SoldierKind.Hero).Skip(1).FirstOrDefault();
                var troops=site.Garrison.Where(u=>u.Kind==SoldierKind.Melee).Take(site.RaidMelee)
                    .Concat(site.Garrison.Where(u=>u.Kind==SoldierKind.Ranged).Take(site.RaidRanged))
                    .Concat(site.Garrison.Where(u=>u.Kind==SoldierKind.Cavalry).Take(site.RaidCavalry))
                    .Concat(site.Garrison.Where(u=>u.Kind==SoldierKind.Mage).Take(site.RaidMage)).ToList();
                if(leader==null||troops.Count<2){site.NextDispatchDay=Date(s.Day,1);continue;}
                var wave=new List<BaseEnemyMember>{leader};wave.AddRange(troops);
                foreach(var unit in wave)site.Garrison.Remove(unit);
                site.WaveNumber++;string id=site.Id+":raid:"+site.WaveNumber;
                while(c.Events.Any(e=>e.Id==id))id=site.Id+":raid:"+(++site.WaveNumber);
                int arrival=Date(s.Day,site.TravelDays);
                c.Events.Add(new BaseCampaignEvent {Id=id,Title=site.Name+" · 第"+site.WaveNumber+"次入侵",Kind="defense",SourceSiteId=site.Id,DepartedDay=s.Day,AvailableDay=arrival,DeadlineDay=arrival,ScenarioFile="23_据点迎战.json",HoldRounds=30,EnemyArmy=wave.Select(u=>u.Copy()).ToList()});
                site.NextDispatchDay=Date(s.Day,site.DispatchInterval);
            }
            c.ThreatDay=s.Day;Prune(s);
        }
        private static void Prune(BaseState state)
        {
            var c=state.Campaign;
            var keep=c.Events.Where(e=>e.Kind=="defense"&&e.Status=="resolved").Reverse().Take(128).Select(e=>e.Id).ToArray();
            c.Events.RemoveAll(e=>e.Kind=="defense"&&e.Status=="resolved"&&!keep.Contains(e.Id));
            if(c.Reports.Count>256)c.Reports.RemoveRange(0,c.Reports.Count-256);
            if(c.SettledIds.Count>4096)
            {
                var receipts=new HashSet<string>(c.SettledIds.AsEnumerable().Reverse().Take(4096));
                receipts.UnionWith(c.Reports.Select(r=>r.BattleId));
                var rewards=state.Demo?.RuinsRewards;
                if (rewards != null)
                {
                    foreach (string battle in new[] { rewards.HideoutFirstClearBattleId, rewards.HideoutPendingBattleId }.Where(id => !string.IsNullOrEmpty(id)))
                    {
                        receipts.Add(battle);
                        receipts.Add(BaseRuinsRewards.HideoutVictoryReceipt(battle));
                    }
                }
                c.SettledIds.RemoveAll(id=>!receipts.Contains(id));
            }
        }
        public static List<BaseEnemyMember> EnemySnapshot(BaseState s,BaseCampaignEvent e,ArmyDefinition template)
        {
            if(string.IsNullOrEmpty(e.SourceSiteId))return e.Id.StartsWith("recovery:")||BaseDemo.Active(s)?e.EnemyArmy.Select(u=>u.Copy()).ToList():new List<BaseEnemyMember>();
            var site=Site(s,e.SourceSiteId)??throw new InvalidOperationException("敌情来源不存在");
            if(e.Kind=="attack")
            {
                if(site.Owner!="enemy")throw new InvalidOperationException("此据点已归己方，不能重复进攻领赏");
                if(!site.Garrison.Any(u=>u.Kind==SoldierKind.Hero))throw new InvalidOperationException("守军已无指挥者，可直接接管据点");
                return site.Garrison.Select(u=>u.Copy()).ToList();
            }
            if(e.EnemyArmy.Count>0)return e.EnemyArmy.Select(u=>u.Copy()).ToList();
            var result=new List<BaseEnemyMember>();int index=0;
            foreach(var kind in BattleSimulation.TroopKinds.Prepend(SoldierKind.Hero))
                for(int n=0;n<template.Count(kind);n++)result.Add(new BaseEnemyMember {Id=e.Id+":legacy:"+(++index),Kind=kind,Name=kind==SoldierKind.Hero ? "来袭队长"+(n+1) : BattleSimulation.KindName(kind),Stats=template.Stats(kind).Clone(),Health=template.Stats(kind).Health});
            return result;
        }
        public static void ConfigureEnemy(BattleScenario scenario,List<BaseEnemyMember> roster)
        {
            if(roster.Count==0)
            {
                if(scenario.BanditHideout)throw new InvalidOperationException("窝点没有有效的敌军名单");
                return;
            }
            var leaders=roster.Where(u=>u.Kind==SoldierKind.Hero).ToArray();
            if(scenario.BanditHideout)
            {
                if(leaders.Length!=0||roster.Any(u=>!BattleSimulation.TroopKinds.Contains(u.Kind)))throw new InvalidOperationException("窝点敌军只能由普通士兵组成");
            }
            else if(leaders.Length<1||leaders.Length>3)throw new InvalidOperationException("敌军须有1至3名实际指挥者");
            var old=scenario.B;var army=old.Clone();army.Commanders.Clear();army.CommanderNames=leaders.Select(u=>u.Name).ToList();army.CommanderRecruited=leaders.Select(_=>false).ToList();army.CaptainSlot=scenario.BanditHideout?-1:0;army.Capacity=90;
            army.CommanderStats=leaders.Select(unit=>unit.Stats.Clone()).ToList();
            // 立绘跟着名单走：随机入侵复用已有队长形象（chief/batu/huoya 等），普通战斗为空串。
            army.CommanderAppearances=leaders.Select(unit=>unit.Appearance??"").ToList();
            for(int n=0;n<leaders.Length;n++){leaders[n].CommanderSlot=n;var command=(old.Commanders.FirstOrDefault()??new CommanderLoadout()).Clone();command.Melee=command.Ranged=command.Cavalry=command.Mage=0;army.Commanders.Add(command);}
            foreach(var kind in BattleSimulation.TroopKinds.Prepend(SoldierKind.Hero))
            {
                army.SetCount(kind,roster.Count(u=>u.Kind==kind));int n=0;
                foreach(var unit in roster.Where(u=>u.Kind==kind&&kind!=SoldierKind.Hero))
                {
                    if(scenario.BanditHideout){unit.CommanderSlot=-1;continue;}
                    int assigned=!unit.HasDeployment?-1:Array.FindIndex(leaders,leader=>leader.PresetSlot==unit.Deployment.CommanderSlot);
                    unit.CommanderSlot=!unit.HasDeployment?n++%leaders.Length:Math.Max(0,assigned);
                    var command=army.Commanders[unit.CommanderSlot];command.Set(kind,command.Count(kind)+1);
                }
            }
            scenario.B=army;
        }
        public static void ConfigureDeployment(BattleScenario scenario,List<BaseEnemyMember> roster)
        {
            foreach(var unit in roster.OrderBy(unit=>unit.Kind==SoldierKind.Hero?0:1))
            {
                var deployment=unit.Deployment;
                if(!unit.HasDeployment||!scenario.Board.Walkable(deployment.Cell)||scenario.Board.Cell(deployment.Cell).Zone!=DeploymentZone.B||scenario.Placements.Any(item=>item.Cell==deployment.Cell))throw new InvalidOperationException("敌营实际驻军的逐兵身份或站位不可用，请检查B部署格");
                scenario.Placements.Add(new UnitPlacement{Side=BattleSide.B,Kind=unit.Kind,Cell=deployment.Cell,CommanderSlot=unit.CommanderSlot});
            }
        }
        public static void Settle(BaseState s,BaseCampaignEvent e,BaseCampaignSession session,BaseCampaignReport report,bool win)
        {
            if(session.Opening.DemoCombat){BaseDemoCombat.PersistEnemies(s,e,session,report,win);return;}
            if(string.IsNullOrEmpty(e.SourceSiteId)||session.Opening.Enemies.Count==0)return;
            var site=Site(s,e.SourceSiteId);report.SourceSiteId=site.Id;var survivors=new List<BaseEnemyMember>();
            for(int i=0;i<session.Opening.Enemies.Count;i++)
            {
                var original=session.Opening.Enemies[i];var unit=session.UnitForEnemy(i);
                bool captured=unit!=null&&s.Aftermath.Prisoners.Any(p=>p.Id==session.Opening.Id+":"+unit.Id);
                bool lost=unit!=null&&(unit.InstantDead||captured||win&&!unit.IsAlive&&!unit.Escaped&&!unit.Rescued);
                if(unit!=null)report.EnemyDeployed++;
                if(lost){if(captured)report.EnemyCaptured++;else report.EnemyLost++;continue;}
                var survivor=original.Copy();survivor.Health=unit==null ? original.Health : Math.Max(1,CombatNumbers.RemainingHealth(BaseSupply.UnboostedHealth(unit, original.Stats.Health)));survivors.Add(survivor);
            }
            if(e.Kind=="attack")
            {
                site.Garrison.Clear();
                if(win&&!s.Aftermath.GameOver){site.Owner="player";site.CapturedDay=s.Day;report.EnemyDispersed=survivors.Count;}
                else site.Garrison.AddRange(survivors);
            }
            else if(site.Owner=="enemy")site.Garrison.AddRange(survivors);
            else report.EnemyDispersed=survivors.Count;
            report.EnemyReturned=site.Owner=="enemy" ? survivors.Count : 0;
            e.EnemyArmy.Clear();
        }
        public static string ForceSummary(IEnumerable<BaseEnemyMember> members)
        {
            var all=members.ToArray();var units=all.Where(unit=>!unit.Dead).ToArray();return "队长 "+units.Count(u=>u.Kind==SoldierKind.Hero)+" · 近战 "+units.Count(u=>u.Kind==SoldierKind.Melee)+" · 弓兵 "+units.Count(u=>u.Kind==SoldierKind.Ranged)+" · 骑兵 "+units.Count(u=>u.Kind==SoldierKind.Cavalry)+" · 法师 "+units.Count(u=>u.Kind==SoldierKind.Mage)+" · 伤员 "+units.Count(u=>u.Health<u.Stats.Health)+(all.Any(unit=>unit.Dead||unit.KnockedDown)?" · 倒地 "+all.Count(unit=>unit.KnockedDown)+" · 死亡 "+all.Count(unit=>unit.Dead):"");
        }
        public static string Warning(BaseState s)
        {
            if(BaseDemo.Active(s))return BaseDemo.Objective(s);
            int ready=s.Campaign.Events.Count(e=>e.Kind=="defense"&&e.Status=="pending"&&e.AvailableDay<=s.Day);
            if(ready>0)return "入侵 "+ready+"：左侧迎战／过夜自动处理";
            var next=s.Campaign.Events.Where(e=>e.Kind=="defense"&&e.Status=="pending").OrderBy(e=>e.AvailableDay).FirstOrDefault();
            if(next!=null)return "入侵在途：第"+next.AvailableDay+"天抵达";
            var site=s.Campaign.EnemySites?.Where(x=>x.Owner=="enemy").OrderBy(x=>x.NextDispatchDay).FirstOrDefault();
            return site==null ? "当前没有敌方据点威胁" : "敌方整备：最早第"+site.NextDispatchDay+"天出兵";
        }
        public static BaseMapObject[] MapObjects(BaseState s)
        {
            var objects=BaseSpatial.Objects(s);var site=Site(s);if(site==null)return objects;
            return objects.Select(o=>o.Id=="campaign_attack" ? new BaseMapObject(o.Id,s.Campaign.Location=="world" ? "进入"+(site.Owner=="player" ? "己方" : "敌军")+"据点区域" : "据点状态 · "+(site.Owner=="player" ? "己方" : "敌方"),o.X,o.Y,o.Width,o.Height,o.Blocks,o.SourceObjectId,o.AssetId,o.Kind,o.VisualOnly,o.Shape,o.Rotation) : o).ToArray();
        }
        private static bool Finite(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x);
        private static void ValidateMembers(List<BaseEnemyMember> units)
        {
            if(units==null||units.Count>93||units.Any(u=>u==null||u.SupplyTier<0||u.SupplyTier>2||u.SupplyTier>0&&(u.Kind==SoldierKind.Hero||string.IsNullOrEmpty(u.SupplyScene))||string.IsNullOrWhiteSpace(u.Id)||u.Id.Length>120||string.IsNullOrWhiteSpace(u.Name)||u.Name.Length>40||!Enum.IsDefined(typeof(SoldierKind),u.Kind)||!Finite(u.Health)||u.Health<0||u.Health==0&&!u.Dead||u.KnockedDown&&(u.Dead||u.Health!=1)||u.Dead&&u.Health!=0||u.Stats==null||!u.Stats.EquipmentStatsValid||!Finite(u.Stats.Health)||u.Stats.Health<1||u.Health>u.Stats.Health||!Finite(u.Stats.Damage)||u.Stats.Damage<0||!Finite(u.Stats.Defense)||u.Stats.Defense<0||u.Stats.Range<1||u.Stats.Range>12||u.Stats.MoveDistance<1||u.Stats.MoveDistance>12||!Finite(u.Stats.CritChance)||u.Stats.CritChance<0||u.Stats.CritChance>1||!Finite(u.Stats.CritMultiplier)||u.Stats.CritMultiplier<1||u.Stats.CritMultiplier>10)||units.Select(u=>u.Id).Distinct().Count()!=units.Count||units.Count(u=>u.Kind==SoldierKind.Hero)>3||BattleSimulation.TroopKinds.Any(k=>units.Count(u=>u.Kind==k)>30))throw new ArgumentException("敌军实际人员／属性非法");
        }
        public static void Validate(BaseState s)
        {
            var c=s.Campaign;
            if(c.HasActiveBattle&&c.ActiveBattle is BaseCampaignBattle opening)
            {
                bool hideout=opening.EventId==BaseBanditHideout.EventId;
                if(hideout!=(opening.Scenario?.BanditHideout==true)||opening.HasCheckpoint&&hideout!=(opening.Checkpoint?.Scenario?.BanditHideout==true))throw new ArgumentException("窝点规则与活动遭遇不一致");
                if(hideout&&(opening.Enemies==null||opening.Enemies.Count==0||opening.Enemies.Any(unit=>unit==null||unit.Kind==SoldierKind.Hero||unit.CommanderSlot!=-1)))throw new ArgumentException("窝点敌军快照须为无归属的普通士兵");
            }
            if(BaseDemo.Active(s))foreach(var mission in c.Events.Where(mission=>string.IsNullOrEmpty(mission.SourceSiteId))){ValidateMembers(mission.EnemyArmy);BaseDemoEncounterConfig.ValidateRoster(s,mission);if(mission.EnemyRecoveryDay<0||mission.EnemyRecoveryDay>s.Day)throw new ArgumentException("残军恢复日期非法");}
            if(c.ThreatVersion==0){if(c.EnemySites?.Count>0||c.Events.Any(e=>!string.IsNullOrEmpty(e.SourceSiteId)))throw new ArgumentException("敌情缺版本，拒绝丢弃据点");c.EnemySites??=new List<BaseEnemySite>();return;}
            if(c.ThreatVersion!=1||c.ThreatDay!=s.Day||c.EnemySites==null||c.EnemySites.Count!=(BaseSceneInstances.Active(s)?c.Layouts.Count(map=>map.Region=="outpost"):1)||c.EnemySites.Select(site=>site.Id).Distinct().Count()!=c.EnemySites.Count)throw new ArgumentException("持续敌情版本／日期／据点非法");
            foreach(var site in c.EnemySites)
            {
                if(site==null||(BaseSceneInstances.Active(s)?!c.Layouts.Any(map=>map.Region=="outpost"&&map.SourceMapId==site.Id):site.Id!="outpost")||string.IsNullOrWhiteSpace(site.Name)||site.Name.Length>40||site.Owner!="enemy"&&site.Owner!="player"||site.NextDispatchDay<1||site.NextDispatchDay>1000000||site.LastReinforcementDay<1||site.LastReinforcementDay>s.Day||site.CapturedDay<0||site.CapturedDay>s.Day||site.Owner=="enemy"&&site.CapturedDay!=0||site.Owner=="player"&&site.CapturedDay==0||site.DispatchInterval<1||site.DispatchInterval>365||site.TravelDays<1||site.TravelDays>30||site.ReinforcementInterval<1||site.ReinforcementInterval>365||site.RecoveryPerDay<0||site.RecoveryPerDay>10000||site.WaveNumber<0||site.NextUnitNumber<1||site.NextUnitNumber>100000000||new[]{site.RaidMelee,site.RaidRanged,site.RaidCavalry,site.RaidMage}.Any(n=>n<0||n>30)||site.Capacity==null||site.Capacity.HeroCount<1||site.Capacity.HeroCount>3||BattleSimulation.TroopKinds.Any(k=>site.Capacity.Count(k)<0||site.Capacity.Count(k)>30)||!c.Events.Any(e=>e.Id==site.AttackEventId&&e.Kind=="attack"&&e.SourceSiteId==site.Id&&e.Status==(site.Owner=="player" ? "resolved" : "pending")))throw new ArgumentException("敌方据点状态／派兵配置非法");
                ValidateMembers(site.Garrison);if(site.Owner=="player"&&site.Garrison.Count>0)throw new ArgumentException("己方据点不能保留敌军驻防");
                if(site.Capacity.CommanderStats==null||site.Capacity.CommanderStats.Count>0&&(site.Capacity.CommanderStats.Count!=site.Capacity.HeroCount||site.Capacity.CommanderStats.Any(stats=>!BaseAftermath.ValidStats(stats))||site.Garrison.Where(unit=>unit.Kind==SoldierKind.Hero).Any(unit=>unit.PresetSlot<0||unit.PresetSlot>=site.Capacity.HeroCount)||site.Garrison.Where(unit=>unit.Kind==SoldierKind.Hero).Select(unit=>unit.PresetSlot).Distinct().Count()!=site.Garrison.Count(unit=>unit.Kind==SoldierKind.Hero)))throw new ArgumentException("敌营队长预设或来源槽位非法");
                foreach(var kind in BattleSimulation.TroopKinds.Prepend(SoldierKind.Hero))
                {var stats=site.Capacity.Stats(kind);ValidateMembers(new List<BaseEnemyMember>{new BaseEnemyMember{Id="validation",Name="validation",Kind=kind,Stats=stats,Health=stats?.Health??0}});}
            }
            foreach(var site in c.EnemySites)
            {
                var deployed=site.Garrison.Concat(c.Events.Where(item=>item.SourceSiteId==site.Id&&item.Kind=="defense"&&item.Status=="pending").SelectMany(item=>item.EnemyArmy)).Where(unit=>unit.HasDeployment).ToArray();
                var preset=c.Layouts.FirstOrDefault(map=>map.SourceMapId==site.Id)?.Enemy;
                if(preset?.Version==2&&site.Garrison.Any(unit=>!unit.HasDeployment)||deployed.Select(unit=>unit.Deployment.Id).Distinct().Count()!=deployed.Length||deployed.Any(unit=>preset?.Version!=2||!preset.Units.Any(source=>source.Id==unit.Deployment.Id&&source.Kind==unit.Kind&&source.Kind==unit.Deployment.Kind&&source.Cell==unit.Deployment.Cell&&source.CommanderSlot==unit.Deployment.CommanderSlot)))throw new ArgumentException("实际敌军逐兵来源与本营预设不一致");
            }
            var live=c.EnemySites.SelectMany(x=>x.Garrison).Select(u=>u.Id).ToList();
            foreach(var e in c.Events.Where(e=>!string.IsNullOrEmpty(e.SourceSiteId)))
            {
                if(Site(s,e.SourceSiteId)==null||e.DepartedDay<0||e.DepartedDay>e.AvailableDay||e.DepartedDay>s.Day||e.Kind=="defense"&&e.DepartedDay<1)throw new ArgumentException("入侵来源／出发日非法");
                ValidateMembers(e.EnemyArmy);if(e.Kind=="defense"&&e.Status=="pending"){if(e.EnemyArmy.Count>0&&!e.EnemyArmy.Any(u=>u.Kind==SoldierKind.Hero))throw new ArgumentException("行军部队缺指挥者");live.AddRange(e.EnemyArmy.Select(u=>u.Id));}
            }
            if(live.Distinct().Count()!=live.Count)throw new ArgumentException("敌军同时存在于驻军和行军部队");
            foreach(var site in c.EnemySites)
            {
                if(c.Events.Count(e=>e.SourceSiteId==site.Id&&e.Kind=="defense"&&e.Status=="pending")>1)throw new ArgumentException("同一来源有多支未处理入侵");
                string prefix=site.Id+":unit:";
                if(live.Where(id=>id.StartsWith(prefix,StringComparison.Ordinal)).Any(id=>!int.TryParse(id.Substring(prefix.Length),out int n)||n<1||n>=site.NextUnitNumber))throw new ArgumentException("敌军人员编号计数器非法");
                prefix=site.Id+":raid:";
                if(c.Events.Where(e=>e.Id.StartsWith(prefix,StringComparison.Ordinal)).Any(e=>!int.TryParse(e.Id.Substring(prefix.Length),out int n)||n<1||n>site.WaveNumber))throw new ArgumentException("入侵编号计数器非法");
            }
            if(c.Reports.Any(r=>new[]{r.EnemyDeployed,r.EnemyLost,r.EnemyCaptured,r.EnemyReturned,r.EnemyDispersed}.Any(n=>n<0||n>93)||!string.IsNullOrEmpty(r.SourceSiteId)&&Site(s,r.SourceSiteId)==null))throw new ArgumentException("敌军战果或来源记录非法");
            var a=c.ActiveBattle;if(!c.HasActiveBattle||a==null)return;
            a.Enemies??=new List<BaseEnemyMember>();a.EnemyBindings??=new List<BaseEnemyBinding>();ValidateMembers(a.Enemies);
            if(a.Enemies.Count==0){if(c.Events.Any(e=>e.Id==a.EventId&&!string.IsNullOrEmpty(e.SourceSiteId)))throw new ArgumentException("活动敌情缺少真实敌军快照");return;}
            if(a.Scenario?.B==null)return;
            var active=c.Events.Single(e=>e.Id==a.EventId);var expected=active.Kind=="attack" && !string.IsNullOrEmpty(active.SourceSiteId) ? Site(s,active.SourceSiteId)?.Garrison : active.EnemyArmy;
            if(!a.DemoCombat&&string.IsNullOrEmpty(active.SourceSiteId)&&!a.Scenario.BanditHideout||expected==null||expected.Count!=a.Enemies.Count||a.Enemies.Any(u=>!expected.Any(x=>x.Id==u.Id&&x.Kind==u.Kind&&x.Name==u.Name&&x.Health==u.Health&&x.Stats.Level==u.Stats.Level&&x.Stats.Health==u.Stats.Health&&x.Stats.Damage==u.Stats.Damage&&x.Stats.Defense==u.Stats.Defense&&x.Stats.Range==u.Stats.Range&&x.Stats.SightRadius==u.Stats.SightRadius&&x.Stats.MoveDistance==u.Stats.MoveDistance&&x.Stats.CritChance==u.Stats.CritChance&&x.Stats.CritMultiplier==u.Stats.CritMultiplier&&x.Stats.Penetration==u.Stats.Penetration&&x.Stats.Resistance==u.Stats.Resistance&&x.Stats.HasCombatKind==u.Stats.HasCombatKind&&x.Stats.CombatKind==u.Stats.CombatKind&&x.Stats.HasProfessionKind==u.Stats.HasProfessionKind&&x.Stats.ProfessionKind==u.Stats.ProfessionKind))||BattleSimulation.TroopKinds.Prepend(SoldierKind.Hero).Any(k=>a.Scenario.B.Count(k)!=a.Enemies.Count(u=>u.Kind==k))||!a.Scenario.BanditHideout&&a.Enemies.Any(u=>u.CommanderSlot<0||u.CommanderSlot>=a.Scenario.B.HeroCount))throw new ArgumentException("活动敌军快照与编制不一致");
            if(a.HasCheckpoint)
            {
                var battle=BattleSimulation.Restore(a.Checkpoint,a.Rpg!=null);
                if(a.EnemyBindings.Any(x=>x==null||x.MemberIndex<0||x.MemberIndex>=a.Enemies.Count||battle.Soldier(x.SoldierId)==null||battle.Soldier(x.SoldierId).DeveloperSpawned||battle.Soldier(x.SoldierId).Side!=BattleSide.B||battle.Soldier(x.SoldierId).Kind!=a.Enemies[x.MemberIndex].Kind||battle.Soldier(x.SoldierId).OriginCommanderSlot!=a.Enemies[x.MemberIndex].CommanderSlot)||a.EnemyBindings.Select(x=>x.SoldierId).Distinct().Count()!=a.EnemyBindings.Count||a.EnemyBindings.Select(x=>x.MemberIndex).Distinct().Count()!=a.EnemyBindings.Count||a.EnemyBindings.Count!=battle.Soldiers.Count(u=>u.Side==BattleSide.B&&!u.DeveloperSpawned))throw new ArgumentException("敌军战斗检查点来源非法");
            }
        }
    }
}
