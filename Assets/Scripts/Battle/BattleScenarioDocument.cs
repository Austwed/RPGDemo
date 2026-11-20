using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.Battle
{
    [Serializable] public sealed class OpeningEffects { public GridPosition Cell; public int InspireRounds, MarchRounds, HealRounds, WeakRounds; }
    [Serializable] public sealed class OpeningHealth { public GridPosition Cell; public float Health = -1; }
    [Serializable] public sealed class BattleScenarioDocument
    {
        public int SchemaVersion = 1;
        public string Kind = "battle", Title = "新对战方案", Description = "", DeploymentMode = "explicit";
        public string[] TestNotes = new string[0];
        public bool StartInCombat;
        public BattleScenario Scenario;
        public List<OpeningHealth> InitialHealth = new List<OpeningHealth>();
        public List<OpeningEffects> InitialEffects = new List<OpeningEffects>();
        public BattleSimulation CreateSimulation()
        {
            Validate(this); var copy = Scenario.Clone(); copy.Placements = copy.Placements.OrderBy(x => x.Kind == SoldierKind.Hero ? 0 : 1).ToList();
            var simulation = new BattleSimulation(copy);
            if (DeploymentMode == "auto") simulation.AutoDeploy(); else if (DeploymentMode == "manual") simulation.AutoDeploy(BattleSide.B);
            simulation.NormalizeOpeningIds();
            if (StartInCombat) { if (!simulation.Start(out var reason)) throw new ArgumentException(reason); simulation.ApplyOpeningHealth(InitialHealth ?? new List<OpeningHealth>()); simulation.ApplyOpeningEffects(InitialEffects ?? new List<OpeningEffects>()); }
            return simulation;
        }
        public static void Validate(BattleScenarioDocument d)
        {
            if (d == null || d.Kind != "battle" || d.SchemaVersion != 1 || d.Scenario == null) throw new ArgumentException("需要SchemaVersion=1、Kind=battle及Scenario");
            if (d.DeploymentMode != "explicit" && d.DeploymentMode != "auto" && d.DeploymentMode != "manual") throw new ArgumentException("DeploymentMode只能为explicit／auto／manual");
            var s = d.Scenario; var b = s.Board; s.Effects ??= new BattleEffectRules(); BattleEffectRules.Validate(s.Effects);
            s.Objective ??= new ObjectiveConfig();
            if (string.IsNullOrWhiteSpace(s.Objective.Kind)) s.Objective.Kind = ObjectiveKinds.Annihilation;
            // 只接受已实现的类型；未知或未实现模式必须明确拒载，不能默认当歼灭或让"坚守"看起来已实现。
            if (!ObjectiveKinds.Implemented(s.Objective.Kind)) throw new ArgumentException("未知或未实现的任务目标类型：" + s.Objective.Kind);
            if (s.Objective.Title == null) s.Objective.Title = "";
            if (s.Objective.Kind == ObjectiveKinds.Hold && (s.Objective.HoldRounds < 1 || s.Objective.HoldRounds > s.MaxRounds)) throw new ArgumentException("坚守回合须为1至MaxRounds");
            if (b == null || b.Width < 6 || b.Width > 64 || b.Height < 6 || b.Height > 64 || b.Cells == null || b.Cells.Length != b.Width * b.Height) throw new ArgumentException("棋盘尺寸或Cells行优先数组不正确");
            if (!s.CommanderControl || !s.MobileBattleRules) throw new ArgumentException("当前方案必须启用队级棋盘规则");
            if (b.Cell(new GridPosition(0, b.Height - 1)).Terrain != TerrainKind.Ground || b.Cell(new GridPosition(b.Width - 1, 0)).Terrain != TerrainKind.Ground) throw new ArgumentException("双方角落撤离格必须为地面");
            foreach (var c in b.Cells) if (c == null || !c.MetadataValid || !Enum.IsDefined(typeof(TerrainKind), c.Terrain) || !Enum.IsDefined(typeof(DeploymentZone), c.Zone) || !c.Walkable && c.Zone != DeploymentZone.None) throw new ArgumentException("地形／部署权限非法");
            if (s.MaxRounds < 1 || s.StalemateRounds < 1 || s.VisionRadius < 1 || s.VisionRadius > 32) throw new ArgumentException("回合限制或视野半径非法");
            if (s.DefenderSide < -1 || s.DefenderSide > 1 || s.CustomExits && (!b.Walkable(s.ExitA) || !b.Walkable(s.ExitB) || s.ExitA == s.ExitB) || new[] { s.LeaderInstantDeath, s.TroopInstantDeath }.Any(v => double.IsNaN(v) || double.IsInfinity(v) || v < 0 || v > 1)) throw new ArgumentException("守方视野／撤离点／即死参数非法");
            if(new[]{s.A,s.B}.Any(army=>army?.CommanderRecruited?.Count>3))throw new ArgumentException("队长来源标记最多3个");
            foreach (var side in new[] { BattleSide.A, BattleSide.B })
            {
                var a = s.Army(side);
                if(a?.CommanderStats!=null&&(a.CommanderStats.Count!=0&&a.CommanderStats.Count!=a.HeroCount||a.CommanderStats.Any(p=>p==null||!p.PersonalProfileValid)))throw new ArgumentException("逐队长战斗属性非法");
                // 随机敌营已作废（2026-11-02），wilderness 仅供旧数据读取。
                if(a?.CommanderAppearances!=null&&(a.CommanderAppearances.Count>3||a.CommanderAppearances.Any(n=>n!=null&&n!=""&&n!="wilderness"&&!BaseConquest.Presentation.PixelDemoArt.IsCast(n))))throw new ArgumentException("未知队长形象");
                if(a?.CommanderNames!=null&&(a.CommanderNames.Count>3||a.CommanderNames.Any(n=>string.IsNullOrWhiteSpace(n)||n.Length>40||n.Any(char.IsControl))))throw new ArgumentException("队长姓名须为1至40字，最多三名");
                if (a == null || a.Capacity < 1 || a.Capacity > 90 || a.Commanders == null) throw new ArgumentException("指挥者人数、职位或编制不正确");
                if (s.Leaderless(side))
                {
                    if (s.Objective.Kind != ObjectiveKinds.Annihilation || a.HeroCount != 0 || a.CaptainSlot != -1 || a.Commanders.Count != 0 || a.CommanderNames?.Count > 0 || a.CommanderRecruited?.Count > 0 || a.CommanderAppearances?.Count > 0 || !BattleSimulation.TroopKinds.Any(kind => a.Count(kind) > 0))
                        throw new ArgumentException("窝点敌军须由至少一名无归属的普通士兵组成");
                }
                else if (a.HeroCount < 1 || a.HeroCount > 3 || a.CaptainSlot < 0 || a.CaptainSlot >= a.HeroCount || a.Commanders.Count < a.HeroCount || a.Commanders.Count > 3) throw new ArgumentException("指挥者人数、职位或编制不正确");
                foreach (var kind in Enum.GetValues(typeof(SoldierKind)).Cast<SoldierKind>())
                {
                    var stats = a.Stats(kind);
                    if (a.Count(kind) < 0 || a.Count(kind) > 30 || stats == null || !stats.EquipmentStatsValid || float.IsNaN(stats.Health) || float.IsInfinity(stats.Health) || stats.Health < 1 || float.IsNaN(stats.Damage) || float.IsInfinity(stats.Damage) || stats.Damage < 0 || float.IsNaN(stats.Defense) || float.IsInfinity(stats.Defense) || stats.Defense < 0 || stats.Range < 1 || stats.Range > 12 || stats.MoveDistance < 1 || stats.MoveDistance > 12 || float.IsNaN(stats.CritChance) || stats.CritChance < 0 || stats.CritChance > 1 || float.IsNaN(stats.CritMultiplier) || stats.CritMultiplier < 1 || stats.CritMultiplier > 10) throw new ArgumentException("兵种数值超出范围");
                }
                foreach (var c in a.Commanders.Take(a.HeroCount)) if (c == null || c.Melee < 0 || c.Ranged < 0 || c.Cavalry < 0 || c.Mage < 0 || c.Total > a.Capacity || c.GuardRange < 1 || c.GuardRange > c.AlertRange || c.AlertRange > c.AttackRange || c.AttackRange > 32 || c.LeadRange < 1 || c.LeadRange > 32 || !Enum.IsDefined(typeof(SquadStrategy), c.Strategy)) throw new ArgumentException("小队编制、范围或策略非法");
                foreach (var kind in BattleSimulation.TroopKinds) if (a.Commanders.Take(a.HeroCount).Sum(c => c.Count(kind)) > a.Count(kind)) throw new ArgumentException("编入小队的兵力超过总库存");
            }
            var occupied = new HashSet<GridPosition>();
            foreach (var placement in s.Placements ?? new List<UnitPlacement>())
            {
                if (placement == null || !Enum.IsDefined(typeof(BattleSide), placement.Side) || !Enum.IsDefined(typeof(SoldierKind), placement.Kind) || !b.Walkable(placement.Cell) || b.Cell(placement.Cell).Zone != (placement.Side == BattleSide.A ? DeploymentZone.A : DeploymentZone.B) || !occupied.Add(placement.Cell)) throw new ArgumentException("初始站位越界、重复或不在本方部署区");
                bool validSlot = s.Leaderless(placement.Side)
                    ? placement.CommanderSlot == -1 && placement.Kind != SoldierKind.Hero
                    : placement.CommanderSlot >= 0 && placement.CommanderSlot < s.Army(placement.Side).HeroCount;
                if (!validSlot) throw new ArgumentException("初始站位的部队归属非法");
            }
            var copy = s.Clone(); copy.Placements = copy.Placements.OrderBy(x => x.Kind == SoldierKind.Hero ? 0 : 1).ToList();
            var simulation = new BattleSimulation(copy);
            if (simulation.Soldiers.Count != copy.Placements.Count) throw new ArgumentException("初始站位超过编制，或麾下士兵缺已部署队长");
            var healthCells = new HashSet<GridPosition>();
            var effectsCells = new HashSet<GridPosition>();
            foreach (var effect in d.InitialEffects ?? new List<OpeningEffects>())
            {
                if (effect == null || !d.StartInCombat || simulation.At(effect.Cell) == null || !effectsCells.Add(effect.Cell) || new[] { effect.InspireRounds, effect.MarchRounds, effect.HealRounds, effect.WeakRounds }.Any(n => n < 0 || n > 30)) throw new ArgumentException("预置效果须有显式站位、开战状态且持续0～30轮");
            }
            foreach (var state in d.InitialHealth ?? new List<OpeningHealth>())
            {
                var unit = simulation.At(state.Cell);
                if (unit == null || !healthCells.Add(state.Cell) || !d.StartInCombat || float.IsNaN(state.Health) || float.IsInfinity(state.Health) || state.Health < 0 || state.Health > unit.Stats.Health) throw new ArgumentException("预设生命须对应显式站位，且StartInCombat=true");
            }
        }
    }
    public sealed partial class BattleSimulation
    {
        internal void NormalizeOpeningIds()
        {
            if (Phase != BattlePhase.Deployment) throw new InvalidOperationException("编号只在初始布置完成后规范化");
            var commanders = Squads.ToDictionary(q => q.Id, q => Soldier(q.CommanderId));
            var ordered = Soldiers.OrderBy(s => s.Side).ThenBy(s => s.Kind == SoldierKind.Hero ? -1 : (int)s.Kind).ThenBy(s => s.OriginCommanderSlot).ThenBy(s => s.Cell.Z).ThenBy(s => s.Cell.X).ToList();
            for (int i = 0; i < ordered.Count; i++) ordered[i].Id = i;
            foreach (var q in Squads) q.CommanderId = commanders[q.Id]?.Id ?? -1;
            nextId = ordered.Count;
        }
        public void ApplyOpeningHealth(IEnumerable<OpeningHealth> states)
        {
            foreach (var state in states) if (At(state.Cell) is BattleSoldier unit) unit.Health = state.Health;
            ResolveLeadership(); Vision.Refresh(); if (Phase == BattlePhase.Fighting) CheckOutcome();
        }
        public void ApplyOpeningEffects(IEnumerable<OpeningEffects> effects)
        {
            foreach (var item in effects)
            {
                var unit = At(item.Cell); if (unit?.IsActive != true) continue;
                unit.InspiredUntil = RoundNumber + item.InspireRounds; unit.MarchUntil = RoundNumber + item.MarchRounds; unit.RegenUntil = RoundNumber + item.HealRounds; unit.WeakCrowdUntil = RoundNumber + item.WeakRounds;
                unit.RegenSourceId = CommanderFor(unit)?.Id ?? unit.Id;
                if (item.InspireRounds > 0 || item.MarchRounds > 0) unit.BuffGainSerial++;
                if (item.HealRounds > 0) unit.HealGainSerial++;
                if (item.WeakRounds > 0) unit.DebuffGainSerial++;
            }
        }
        public bool SettleVictory()
        {
            if (Phase == BattlePhase.Finished) return Report?.Outcome == BattleOutcome.SideAWin;
            if (Report?.Outcome != BattleOutcome.SideAWin && !VictoryDeclared) return false;
            Phase = BattlePhase.Finished; InvalidateUndo(); RefreshBattleReport(); return true;
        }
    }
}
