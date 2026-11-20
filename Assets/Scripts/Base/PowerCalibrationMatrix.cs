#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using BaseConquest.Battle;

namespace BaseConquest.Base
{
    // This fixture uses the campaign's deployment, shot rolls, collision and casualty resolution.
    public static partial class PowerCalibrationMatrix
    {
        static readonly int[][] Parties = { new[]{6,5,5,0}, new[]{4,4,4,4}, new[]{3,3,2,8} };
        static readonly string[] PartyNames = { "traditional", "balanced", "mage-heavy" };
        static readonly string[] Cases = { "flat", "hideout", "defense", "bandit", "second", "elite", "captain3", "captain5" };
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        static string Arg(string key, string fallback) =>
            Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--" + key + "=", StringComparison.Ordinal))?.Substring(key.Length + 3) ?? fallback;

        public static void Run()
        {
            try { RunMatrix(); Debug.Log("[ForceCalibration] PASS"); if(Application.isBatchMode) UnityEditor.EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogError("[ForceCalibration] FAIL " + error); if(Application.isBatchMode) UnityEditor.EditorApplication.Exit(1); }
        }

        static void RunMatrix()
        {
            var version = Arg("version","current");
            var split = Arg("split","train");
            if (split!="train" && split!="holdout") throw new ArgumentException("split");
            var rulesPath = Arg("rules","Assets/Resources/DemoBalance/DemoBalance.json");
            var rules = JsonUtility.FromJson<DemoBalancePackage>(File.ReadAllText(rulesPath)).Rules;
            rules.Validate();
            var state = JsonUtility.FromJson<BaseState>(File.ReadAllText("Assets/Resources/DemoStages/defense-male.json"));
            state.Demo.Rpg = rules;
            // Rebuild authored encounter rows using the selected snapshot, including extra templates and troop slots.
            state.Demo.Encounters = null;
            BaseDemoIntro.Enable(state,true);
            var root = Arg("out","Logs/p54-calibration/"+version+"-"+split);
            Directory.CreateDirectory(root);
            string[] cases = Arg("cases",string.Join(",",Cases)).Split(',');
            int[] parties=Arg("parties","0,1,2").Split(',').Select(int.Parse).ToArray();
            int[] grades=Arg("grades","0,1,2").Split(',').Select(int.Parse).ToArray();
            int count=int.Parse(Arg("count","32")), start=int.Parse(Arg("start","0"));
            using var matches = new StreamWriter(Path.Combine(root,"matches.csv"));
            using var shots = new StreamWriter(Path.Combine(root,"shots.csv"));
            matches.WriteLine("version,split,case_id,party,grade,seed,mirror,subject_side,outcome,won,seconds,own_count,enemy_count,own_active,enemy_active,own_down,own_dead,enemy_down,enemy_dead,own_hp_loss,enemy_hp_loss,own_loss_fraction,enemy_loss_fraction,pred_win,pred_seconds,shoves,e_supply0,e_supply1,e_supply2,own_xp_damage,enemy_captain_def,enemy_captain_hp_loss,enemy_captain_hp,pred_advantage,pred_own_loss,enemy_elite_slots,enemy_captain_levels,source_data,warrior_damage,archer_damage,knight_damage,mage_damage,warrior_active_s,archer_active_s,knight_active_s,mage_active_s");
            shots.WriteLine("version,split,case_id,party,grade,seed,mirror,side,source_prof,target_prof,attempts,bow_moving_rider,pressured_bow,both_bow_penalties,disadvantage,normal,advantage");
            foreach (var key in cases)
            {
                if(!Cases.Contains(key)) throw new ArgumentException(key);
                if(key=="elite" && rules.Templates.All(t=>t.Id!="blackcrow_armored_elite"))
                {
                    Debug.Log("[ForceCalibration] skipped elite: template absent from selected rules");
                    continue;
                }
                var scene = ScenarioFor(key,state);
                Parallel.For(start,start+count,new ParallelOptions { MaxDegreeOfParallelism=int.Parse(Arg("workers","4")) },index=>
                {
                    foreach(int grade in grades)
                        foreach(int party in parties)
                            for(int mirror=0;mirror< (key=="flat"?2:1);mirror++)
                            {
                                int seed=(split=="train"?11071:47951)+index*997;
                                using var matchRows=new StringWriter(Invariant);
                                using var shotRows=new StringWriter(Invariant);
                                Match(key,scene,state,rules,party,grade,seed,mirror==1,version+","+split,matchRows,shotRows);
                                lock(matches)
                                {
                                    matches.Write(matchRows.ToString());shots.Write(shotRows.ToString());
                                    matches.Flush(); shots.Flush();
                                }
                            }
                });
                Debug.Log("[ForceCalibration] finished "+key);
            }
        }

        static BaseCampaignBattle ScenarioFor(string key,BaseState state)
        {
            bool second=key=="second";
            bool defense=key=="defense" || second;
            var scenario=new BattleScenario { MaxRounds=90, StalemateRounds=90, CommanderControl=true, MobileBattleRules=true,
                IndependentArmyA=true,FogEnabled=false,PlayerLeaderSlot=-1,CustomExits=true,
                CampaignRules=true,InstantDeathEnabled=true,LeaderInstantDeath=state.Aftermath.Difficulty.LeaderDeath,
                TroopInstantDeath=state.Aftermath.Difficulty.TroopDeath };
            var opening=new BaseCampaignBattle { Id="calibrate",EventId="calibrate:"+key,Rpg=state.Demo.Rpg,RealtimeRules=true,
                RealtimeRulesVersion=1,DemoCombat=true,Scenario=scenario,Difficulty=state.Aftermath.Difficulty.Copy(),StartedDay=1 };
            if (key=="flat" || key=="elite" || key.StartsWith("captain"))
            {
                var board=BattleBoard.CreateDefault(14,10);
                foreach(var p in board.Positions()) board.Cell(p).Zone = p.X<3?DeploymentZone.A:p.X>=board.Width-3?DeploymentZone.B:DeploymentZone.None;
                scenario.Board=board;
                scenario.IndependentArmyB=!key.StartsWith("captain");
            }
            else
            {
                // Use the release maps and formations. First defense uses its hold-and-retreat resolver,
                // the second attack uses the full authored main force and the camp uses its commander objective.
                string region=defense?"perimeter":key=="hideout"?BaseBanditHideout.Region:BaseDemoFinale.Camp;
                if(key=="bandit" && !state.Campaign.Layouts.Any(m=>m.SourceMapId==BaseDemoFinale.Camp))
                {
                    state=state.Copy();
                    var camp=state.Demo.StoryScene?.Valid==true ? state.Demo.StoryScene.Map("bandit").Layout.Copy()
                        : BaseDemoRelease.Enabled(state) ? state.Demo.ReleaseMaps.Single(m=>m.Role=="bandit").Layout.Copy()
                        : BaseDemoFinale.CreateCampLayout();
                    camp.Region=region;camp.SourceMapId=region;
                    state.Campaign.Layouts.Add(camp);
                    state.Campaign.RegionMaps.Add(new BaseRegionMap {Id=region,Board=BaseDemoFinale.CreateCampBoard(state)});
                    state.Campaign.EnemySites.Single().Id=region;
                }
                var layout=BaseSpatial.Layout(state,region);
                scenario.Board=BaseSceneCombat.Project(state,region);
                scenario.HeightVision=true; scenario.FogEnabled=true; scenario.ShowEnemiesThroughFog=false;
                opening.MapId=layout.SourceMapId;opening.MapRegion=region;
                opening.TacticalRules=state.Demo.TacticalRules?.Copy();
                opening.TacticalObjects=layout.TacticalObjects.Select(o=>o.Copy()).ToList();
                opening.TacticalBaseDamage=state.Demo.Rpg.Templates.Single(t=>t.Id==state.Demo.Rpg.DefaultTemplateId("soldier",0)).Attack;
                if (defense)
                {
                    opening.EventId=key=="defense"?BaseDemoDefense.EventId:BaseDemoDefense.SecondEventId;
                    BaseDemoDefense.ApplyHomeSight(scenario);
                    if(key=="defense") { scenario.Objective.Kind=ObjectiveKinds.Hold;scenario.Objective.HoldRounds=30; }
                }
                else if(key=="bandit") { opening.EventId=BaseThreats.Site(state,BaseDemoFinale.Camp).AttackEventId;BaseSceneRules.ApplyBanditSight(scenario); }
                else { scenario.BanditHideout=true; opening.EventId=BaseBanditHideout.EventId; }
                if(key!="hideout")
                {
                    var preset=key=="defense"?state.Demo.Encounters.Defense:state.Demo.Encounters.Camp;
                    var site=new BaseEnemySite {Id=key=="defense"?"first-invasion":BaseDemoFinale.Camp};
                    BaseThreats.InstallPreset(site,preset);
                    var roster=site.Garrison;

                    BaseRpg.PrepareEnemies(state,roster,opening.EventId);
                    BaseEnemySupply.Assign(state.Demo.Rpg,second?"second":key,roster);
                    scenario.B=site.Capacity.Clone();
                    BaseThreats.ConfigureEnemy(scenario,roster);
                    if(!second) BaseThreats.ConfigureDeployment(scenario,roster);
                    opening.Enemies=roster;
                }
            }
            scenario.ExitA=BattleSimulation.SideExit(scenario.Board,!defense);
            scenario.ExitB=BattleSimulation.SideExit(scenario.Board,defense);
            return opening;
        }

        static List<BaseEnemyMember> Normals(RpgRules rules,int[] counts,int tier)
        {
            var list=new List<BaseEnemyMember>();
            for(int p=0;p<counts.Length;p++)
            {
                var kind=BaseRpg.Kind(p);
                for(int i=0;i<counts[p];i++)
                {
                    var id="calibration:soldier:"+p+":"+i;
                    var profile=new RpgCharacter { Id=id,Version=1,Level=1,Profession=p,TemplateId=rules.DefaultTemplateId("soldier",p),Rules=rules };
                    var stats=rules.Bare(profile,1);
                    var member=new BaseEnemyMember {Id=id,Rpg=profile,Stats=stats,Health=stats.Health,Kind=kind,CommanderSlot=-1,SupplyTier=tier};
                    list.Add(member);
                }
            }
            return list;
        }

        static ArmyDefinition Independent(IEnumerable<BaseEnemyMember> members)
        {
            var army=new ArmyDefinition {HeroCount=0,CaptainSlot=-1,Capacity=90,Commanders=new List<CommanderLoadout>()};
            foreach(var kind in BattleSimulation.TroopKinds) army.SetCount(kind,members.Count(m=>m.Kind==kind));
            return army;
        }

        static BaseCampaignSession StartMatch(string key, BaseCampaignBattle setup, BaseState state, RpgRules rules, int party, int grade, int seed, bool mirror, int[] allyCounts=null, int[] enemyCounts=null)
        {
            var opening=setup.Copy();
            var scenario=opening.Scenario;
            scenario.RandomSeed=seed;opening.Id="calibration:"+seed;
            var challengers=Normals(rules,allyCounts??Parties[party],grade);
            var opponents=opening.Enemies;
            if(key=="flat") opponents=Normals(rules,enemyCounts??Parties[1],grade);
            if(key=="elite")
            {
                var template=rules.Templates.Single(t=>t.Id=="blackcrow_armored_elite");
                opponents=Normals(rules,new[]{16,0,0,0},2);
                foreach(var member in opponents)
                {
                    member.Rpg.TemplateId=template.Id;
                    member.Stats=rules.Bare(member.Rpg,1);
                    member.Health=member.Stats.Health;
                }
            }
            if(key=="hideout")
            {
                opponents=BaseBanditHideout.DrawEnemies(state,seed,null);
                BaseEnemySupply.Assign(rules,key,opponents);
            }
            if(key.StartsWith("captain"))
            {
                int level=key=="captain3"?3:5;
                var profile=BaseRpg.Character("heavy-captain",0,false,false,rules,level);
                var gear=new BasePersonalLoadout {Version=1,Weapon="sword",InitialWeapon="sword",BaseStats=rules.Bare(profile,level)};
                gear.Items.Add(new BasePersonalItem {Template="novice_heavy_helmet",Slot="helmet",Quality=2});
                gear.Items.Add(new BasePersonalItem {Template="novice_heavy_armor",Slot="armor",Quality=2});
                var leader=new BaseLeader {Id=profile.Id,Rpg=profile,PersonalEquipment=gear};
                leader.Training=BaseTraining.FromProfile(gear.BaseStats);
                var stats=BaseRpg.Stats(leader);
                opponents=new List<BaseEnemyMember>{new BaseEnemyMember{Id=profile.Id,Name="重甲队长",Rpg=profile,Stats=stats,Health=stats.Health,CommanderSlot=0,Kind=SoldierKind.Hero}};
                BaseThreats.ConfigureEnemy(scenario,opponents);
            }
            if(mirror) (challengers,opponents)=(opponents,challengers);
            opening.Enemies=opponents;scenario.A=Independent(challengers);
            if(scenario.Leaderless(BattleSide.B)) scenario.B=Independent(opponents);
            opening.PlayerTroopCounts=BattleSimulation.TroopKinds.Select(k=>challengers.Count(m=>m.Kind==k)).ToArray();
            opening.Troops=challengers.Select((m,i)=>new BaseWorkerBucket { Kind=BaseCampaign.ToBase(m.Kind), HasCombatProfile=true, CombatStats=m.EffectiveStats(rules),
                Health=m.Health,MaxHealth=m.Stats.Health,SoldierNumbers=new List<int>{i+1} }).ToList();
            var session=new BaseCampaignSession(opening);
            session.Battle.AutoDeploy(BattleSide.A);session.Bind();
            if(session.Battle.Soldiers.Count!=challengers.Count+opponents.Count || session.Battle.Soldiers.Any(s=>s.Side==BattleSide.A&&s.Kind==SoldierKind.Hero))
                throw new InvalidOperationException("Incomplete or non-independent deployment: "+key+" "+session.Battle.Soldiers.Count+" expected "+(challengers.Count+opponents.Count));
            if(!session.Battle.Start(out var error))throw new InvalidOperationException(error);
            return session;
        }

        static void Match(string key,BaseCampaignBattle setup,BaseState state,RpgRules rules,int party,int grade,int seed,bool mirror,string prefix,TextWriter matches,TextWriter shots)
        {
            var session=StartMatch(key,setup,state,rules,party,grade,seed,mirror);
            var opponents=session.Opening.Enemies;
            var all=session.Battle.Soldiers;
            var subject=mirror?BattleSide.B:BattleSide.A;
            var own=all.Where(u=>u.Side==subject).ToList();var enemy=all.Where(u=>u.Side!=subject).ToList();
            var forecast=BasePower.Engage(own,enemy,rules,key=="defense"?30:0);
            var run=new RealtimeCampaignBattle(session,"normal");
            if(key=="defense"||key=="second") BaseDemoDefense.Configure(run);
            if(key=="bandit")BaseDemoFinale.Configure(run);
            run.Model.SetPaused(false);
            var activeSeconds=new double[4];
            for(int frame=0;frame<480 && session.Battle.Phase!=BattlePhase.Finished;frame++)
            {
                // Approximate deployed living time to one quarter second, including movement before contact.
                foreach(var u in own) if(u.IsActive) activeSeconds[BaseRpg.Profession(u.ProfessionKind)]+=.25;
                run.Tick(.25);
            }
            string outcome=session.Battle.Report?.Outcome.ToString()??"Timeout";
            bool won=outcome==(mirror?"SideBWin":"SideAWin");
            string id=prefix+","+key+","+PartyNames[party]+","+grade+","+seed+","+(mirror?1:0);
            double Lost(IEnumerable<BattleSoldier> army)=>army.Sum(s=>Math.Max(0,s.Stats.Health-Math.Max(0,s.Health)));
            var cap=enemy.Where(u=>u.Kind==SoldierKind.Hero).ToList();
            var metrics=new object[]{subject,outcome,won?1:0,run.Model.Elapsed,own.Count,enemy.Count,own.Count(u=>u.IsActive),enemy.Count(u=>u.IsActive),
                own.Count(u=>!u.IsAlive),own.Count(u=>u.InstantDead),enemy.Count(u=>!u.IsAlive),enemy.Count(u=>u.InstantDead),
                Lost(own),Lost(enemy),Lost(own)/own.Sum(u=>u.Stats.Health),Lost(enemy)/enemy.Sum(u=>u.Stats.Health),forecast.AllyWins?1:0,forecast.Seconds,run.Model.ShoveMoves,
                opponents.Count(m=>m.Kind!=SoldierKind.Hero && m.SupplyTier==0),opponents.Count(m=>m.Kind!=SoldierKind.Hero && m.SupplyTier==1),opponents.Count(m=>m.Kind!=SoldierKind.Hero && m.SupplyTier==2),
                run.RpgAttackHistory.Where(a=>a.ImpactApplied && a.TargetId>0 && session.Battle.Soldier(a.SourceId-1).Side==subject).Sum(a=>a.DamageApplied),
                cap.Count==0?0:cap.Average(s=>s.Stats.Defense),Lost(cap),cap.Sum(s=>s.Stats.Health),forecast.Advantage,1-forecast.AllySurvival};
            var damage=new double[4];
            foreach(var packet in run.RpgAttackHistory.Where(a=>a.TargetId>0 && a.ImpactApplied))
            {
                var source=session.Battle.Soldier(packet.SourceId-1);
                if(source.Side==subject) damage[BaseRpg.Profession(source.ProfessionKind)]+=packet.DamageApplied;
            }
            var elite=opponents.Where(m=>m.Rpg?.TemplateId=="blackcrow_armored_elite").Select(m=>m.Deployment?.Id??m.Id);
            var levels=opponents.Where(m=>m.Kind==SoldierKind.Hero).Select(m=>m.Stats.Level);
            matches.WriteLine(id+","+string.Join(",",metrics.Select(x=>Convert.ToString(x,Invariant)))+
                ","+string.Join("|",elite)+","+string.Join("|",levels)+","+"table"+
                ","+string.Join(",",damage.Concat(activeSeconds).Select(v=>v.ToString("R",Invariant))));
            if(key!="flat")return;
            foreach(var group in run.RpgAttackHistory.Where(a=>a.TargetId>0&&string.IsNullOrEmpty(a.ObjectId)).GroupBy(a=>
            {
                var source=session.Battle.Soldier(a.SourceId-1);var target=session.Battle.Soldier(a.TargetId-1);
                return (source.Side,Src:BaseRpg.Profession(source.ProfessionKind),Dst:BaseRpg.Profession(target.ProfessionKind));
            }))
            {
                int movement=0,pressure=0,both=0,dis=0,normal=0,adv=0;
                foreach(var a in group)
                {
                    bool rider=(a.Context&RpgShotContext.MovingCavalry)!=0,melee=(a.Context&RpgShotContext.MeleePressure)!=0;
                    if(group.Key.Src==1){if(rider) movement++;if(melee)pressure++;if(rider&&melee)both++;}
                    if(a.NetAdvantage<0)dis++;else if(a.NetAdvantage>0)adv++;else normal++;
                }
                shots.WriteLine(id+","+group.Key.Side+","+group.Key.Src+","+group.Key.Dst+","+group.Count()+","+movement+","+pressure+","+both+","+dis+","+normal+","+adv);
            }
        }
    }
}
#endif
