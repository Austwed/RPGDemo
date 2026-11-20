#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using BaseConquest.Battle;
using UnityEngine;

namespace BaseConquest.Base
{
    public static partial class PowerCalibrationMatrix
    {
        public static void RunProfessionPairs()
        {
            var state=JsonUtility.FromJson<BaseState>(File.ReadAllText("Assets/Resources/DemoStages/defense-male.json"));
            var rules=state.Demo.Rpg;
            string directory=Arg("out","Logs/p54-calibration/professions");
            Directory.CreateDirectory(directory);
            using var dps=new StreamWriter(Path.Combine(directory,"dps.csv"));
            dps.WriteLine("identity,grade,attacker,defender,neutral_dps");
            foreach(string identity in new[]{"soldier","captain_normal","captain_awakened"})
            for(int grade=0;grade<=(identity=="soldier"?2:0);grade++)
            {
                double[] averages=new double[4];
                for(int role=0;role<4;role++)
                for(int target=0;target<3;target++)
                {
                    var a=rules.Templates.Single(t=>t.Id==rules.DefaultTemplateId(identity,role));
                    var b=rules.Templates.Single(t=>t.Id==rules.DefaultTemplateId(identity,target));
                    double attack=a.Attack+(grade==0?0:grade==1?rules.SupplyAttackLow:rules.SupplyAttackHigh);
                    double defense=b.Defense+(grade==0?0:grade==1?rules.SupplyDefenseLow:rules.SupplyDefenseHigh);
                    double result=0;
                    for(int die=1;die<=10;die++)
                    {
                        var roll=new RpgAttackRoll((int)rules.Hit[role],(int)rules.Evasion[target],0,0,die);
                        if(roll.Hit)result+=RpgCombatRules.Damage(rules.AttackDamageType(role),attack,defense,
                            rules.ArmorK,rules.ArmorCap,roll.Critical?rules.CriticalMultiplier:1);
                    }
                    result/=10*RpgCombatRules.Interval(rules.BaseInterval,0,rules.MinimumInterval);
                    averages[role]+=result/3;
                    dps.WriteLine(FormattableString.Invariant($"{identity},{grade},{role},{target},{result}"));
                }
                if(averages.Take(3).Any(d=>d<=averages[3]))
                    throw new InvalidOperationException("法师的同级中性DPS过高："+identity+"/"+grade);
            }
            using var csv=new StreamWriter(Path.Combine(directory,"pairs.csv"));
            csv.WriteLine("role,opponent,grade,seed,mirror,won,seconds,hp_loss,knocked,permanent,shoves");
            var setup=ScenarioFor("flat",state);
            int count=int.Parse(Arg("count","4"));
            for(int a=0;a<4;a++)
            for(int b=a;b<4;b++)
            for(int i=0;i<count;i++)
            for(int flip=0;flip<2;flip++)
            {
                int[] left=new int[4],right=new int[4];left[a]=right[b]=16;
                int seed=266983+i*397;
                var session=StartMatch("flat",setup,state,rules,1,1,seed,flip==1,left,right);
                var run=new RealtimeCampaignBattle(session,"normal");
                var side=flip==1?BattleSide.B:BattleSide.A;
                var army=session.Battle.Soldiers.Where(u=>u.Side==side).ToArray();
                run.Model.SetPaused(false);
                for(int frame=0;frame<480&&session.Battle.Phase!=BattlePhase.Finished;frame++)run.Tick(.25);
                bool won=session.Battle.Report?.Outcome==(flip==1?BattleOutcome.SideBWin:BattleOutcome.SideAWin);
                csv.WriteLine(FormattableString.Invariant($"{a},{b},1,{seed},{flip},{(won?1:0)},{run.Model.Elapsed},{army.Sum(u=>u.Stats.Health-u.Health)},{army.Count(u=>!u.IsAlive)},{army.Count(u=>u.InstantDead)},{run.Model.ShoveMoves}"));
            }
            Debug.Log($"[P54Pairs] PASS · neutral DPS and {count*20} mirrored pure-profession battles");
        }

        public static void RunSweeps()
        {
            var state=JsonUtility.FromJson<BaseState>(File.ReadAllText("Assets/Resources/DemoStages/defense-male.json"));
            var rules=state.Demo.Rpg;
            var setup=ScenarioFor("hideout",state);
            string directory=Arg("out","Logs/p54-calibration/sweep");
            Directory.CreateDirectory(directory);
            using var csv=new StreamWriter(Path.Combine(directory,"sweep.csv"));
            csv.WriteLine("party,grade,seed,advantage,eligible,settled_seconds,actual_hp_loss,actual_knocked,actual_dead");
            int count=int.Parse(Arg("count","48"));
            int admissions=0;
            for (int i=0;i<count;i++)
            for (int grade=0;grade<=2;grade++)
            for (int party=0;party<Parties.Length;party++)
            {
                int seed=872341+i*1009;
                var session=StartMatch("hideout",setup,state,rules,party,grade,seed,false);
                var comparison=BaseAutoResolve.Evaluate(session);
                string problem=BaseAutoResolve.Problem(state,session.Opening.EventId,comparison,BaseAutoResolve.SweepAdvantage);
                bool eligible=problem=="";
                if(!eligible){csv.WriteLine(FormattableString.Invariant($"{PartyNames[party]},{grade},{seed},{comparison.Advantage},0,,,,"));continue;}
                admissions++;
                var before=session.Battle.Soldiers.Where(u=>u.Side==BattleSide.A).ToDictionary(u=>u.Id,u=>u.Health);
                var preview=BaseAutoResolve.Losses(session,comparison);
                int seconds=(int)Math.Ceiling(Math.Max(rules.SweepMinimumSeconds,comparison.Seconds));
                var run=new RealtimeCampaignBattle(session,"normal");
                BaseAutoResolve.Apply(run,preview,seconds);
                var army=session.Battle.Soldiers.Where(u=>u.Side==BattleSide.A).ToArray();
                double hp=army.Sum(u=>before[u.Id]-u.Health);
                if(session.Battle.Report.Outcome!=BattleOutcome.SideAWin ||
                    Math.Abs(hp-preview.Sum(x=>before[x.UnitId]-x.After))>0.01 ||
                    army.Count(u=>u.InstantDead)!=preview.Count(x=>x.Dead))
                    throw new InvalidOperationException("扫荡预估和实结不一致");
                csv.WriteLine(FormattableString.Invariant($"{PartyNames[party]},{grade},{seed},{comparison.Advantage},1,{seconds},{hp},{army.Count(u=>!u.IsAlive)},{army.Count(u=>u.InstantDead)}"));
            }
            Debug.Log($"[P54Sweep] PASS · {count*9} attempts, {admissions} settled");
        }
    }
}
#endif
