#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BaseConquest.Battle;
using UnityEngine;

namespace BaseConquest.Base
{
    public static partial class PowerCalibrationMatrix
    {
        public static void RunPureSoldierMatrix()
        {
            try
            {
                WritePureSoldierMatrix();
                Debug.Log("[PureSoldierMatrix] PASS");
                if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogError("[PureSoldierMatrix] FAIL " + error);
                if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(1);
            }
        }

        private static void WritePureSoldierMatrix()
        {
            var state = JsonUtility.FromJson<BaseState>(File.ReadAllText("Assets/Resources/DemoStages/defense-male.json"));
            var rules = JsonUtility.FromJson<DemoBalancePackage>(File.ReadAllText("Assets/Resources/DemoBalance/DemoBalance.json")).Rules;
            rules.Validate();
            state.Demo.Rpg = rules;
            string split = Arg("split", "train");
            if (split is not ("train" or "holdout")) throw new ArgumentException("split");
            bool sensitivity = Arg("archer-cavalry-sensitivity", "false") == "true";
            bool holdout = split == "holdout";
            if (sensitivity && !holdout) throw new ArgumentException("Sensitivity uses independent seeds.");
            double[] points = Arg("points", "100,100,100,100").Split(',').Select(value => double.Parse(value, Invariant)).ToArray();
            if (points.Length != 4 || points.Any(value => !double.IsFinite(value) || value <= 0))
                throw new ArgumentException("points");
            int count = int.Parse(Arg("count", holdout ? "4" : "2"));
            var samples = holdout ? new[] { (0, 0) } : new[] { (8, 8), (16, 16), (24, 24), (12, 16), (16, 12) };
            var jobs = (from constrained in new[] { false, true }
                        from grade in Enumerable.Range(0, 3)
                        from a in Enumerable.Range(0, 4)
                        from b in Enumerable.Range(a + 1, 3 - a)
                        where !sensitivity || a == 1 && b == 2
                        from size in samples
                        from index in Enumerable.Range(0, count)
                        from mirror in new[] { false, true }
                        select (constrained, grade, a, b, size, index, mirror)).ToArray();
            var rows = new string[jobs.Length];
            Parallel.For(0, jobs.Length, new ParallelOptions { MaxDegreeOfParallelism = int.Parse(Arg("workers", "4")) }, i =>
            {
                var job = jobs[i];
                var setup = ScenarioFor("flat", state);
                var board = setup.Scenario.Board;
                if (job.constrained)
                {
                    // A symmetric obstacle forces two approaches without changing deployment capacity.
                    for (int x = 6; x <= 7; x++)
                        for (int z = 2; z <= 7; z++)
                            board.Cell(new GridPosition(x, z)).Terrain = TerrainKind.Wall;
                }
                int countA = holdout ? EqualBudgetSoldiers(points[job.a]) : job.size.Item1;
                int countB = holdout ? EqualBudgetSoldiers(points[job.b]) : job.size.Item2;
                // Try the neighboring archer count closest to the rounded cavalry budget.
                if (sensitivity)
                    countA = Math.Clamp((int)Math.Round(points[job.b] * countB / points[job.a]), 1, 24);
                int seed = (sensitivity ? 862381 : holdout ? 644903 : 152117) + job.index * 1009;
                rows[i] = PureSoldierMatch(state, rules, setup, job.a, job.b, countA, countB,
                    job.grade, seed, job.constrained, job.mirror, points);
            });
            string directory = Arg("out", "Logs/pure-soldier-power");
            Directory.CreateDirectory(directory);
            using var csv = new StreamWriter(Path.Combine(directory, sensitivity ? "sensitivity.csv" : split + ".csv"));
            csv.WriteLine("map,grade,role_a,role_b,count_a,count_b,points_a,points_b,seed,mirror,outcome,seconds,hp_a,hp_b,max_hp_a,max_hp_b");
            foreach (string row in rows) csv.WriteLine(row);
            Debug.Log($"[PureSoldierMatrix] {split}: {rows.Length} matches");
        }

        private static int EqualBudgetSoldiers(double points) =>
            (int)Math.Clamp(Math.Round(1000 / points, MidpointRounding.AwayFromZero), 1, 24);

        private static string PureSoldierMatch(BaseState state, RpgRules rules, BaseCampaignBattle setup,
            int roleA, int roleB, int countA, int countB, int grade, int seed, bool constrained, bool mirror, double[] points)
        {
            int[] left = new int[4], right = new int[4];
            left[roleA] = countA;
            right[roleB] = countB;
            var session = StartMatch("flat", setup, state, rules, 0, grade, seed, mirror, left, right);
            var run = new RealtimeCampaignBattle(session, "normal");
            run.Model.SetPaused(false);
            for (int frame = 0; frame < 480 && session.Battle.Phase != BattlePhase.Finished; frame++) run.Tick(.25);
            BattleSide sideA = mirror ? BattleSide.B : BattleSide.A;
            var a = session.Battle.Soldiers.Where(unit => unit.Side == sideA).ToArray();
            var b = session.Battle.Soldiers.Where(unit => unit.Side != sideA).ToArray();
            var result = session.Battle.Report?.Outcome ?? BattleOutcome.RoundLimit;
            string outcome = result is BattleOutcome.SideAWin or BattleOutcome.SideBWin
                ? ((result == BattleOutcome.SideAWin) == !mirror ? "a" : "b")
                : result == BattleOutcome.RoundLimit ? "timeout" : "draw";
            return FormattableString.Invariant($"{(constrained ? "obstacle" : "flat")},{grade},{roleA},{roleB},{countA},{countB},{points[roleA] * countA:F2},{points[roleB] * countB:F2},{seed},{(mirror ? 1 : 0)},{outcome},{run.Model.Elapsed:F2},{a.Sum(unit => unit.Health):F2},{b.Sum(unit => unit.Health):F2},{a.Sum(unit => unit.Stats.Health):F2},{b.Sum(unit => unit.Stats.Health):F2}");
        }

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
