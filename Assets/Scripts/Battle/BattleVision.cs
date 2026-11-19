using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.Battle
{
    // 双方知识独立保存；已探索地形不是当前敌军视野，库存从不由目击人数反推。
    public sealed partial class BattleVision
    {
        private readonly BattleSimulation battle;
        private readonly bool[][] visible, explored, terrain;
        private readonly BattleBoard map;
        private bool explorationFullVision;
        public bool HeightMode=>battle==null||battle.Scenario.HeightVision;
        public BattleVision(BattleBoard board):this((BattleSimulation)null,board){}
        public BattleVision(BattleSimulation simulation):this(simulation,simulation.Board){}
        private readonly int[][] lastSeen;
        private readonly Dictionary<int, SoldierKind>[] sightings = { new Dictionary<int, SoldierKind>(), new Dictionary<int, SoldierKind>() };
        private readonly HashSet<int>[] confirmedDeaths = { new HashSet<int>(), new HashSet<int>() };
        public int Revision { get; private set; }
        private BattleVision(BattleSimulation simulation,BattleBoard board)
        {
            battle=simulation;map=board;int size=board.Width*board.Height;terrain=new[]{new bool[size],new bool[size]};
            visible = new[] { new bool[size], new bool[size] }; explored = new[] { new bool[size], new bool[size] };
            lastSeen = new[] { Enumerable.Repeat(-1, size).ToArray(), Enumerable.Repeat(-1, size).ToArray() };
        }
        private int Index(GridPosition p) => p.Z * map.Width + p.X;
        public bool FullVision(BattleSide side) => battle==null?explorationFullVision:!battle.Scenario.FogEnabled || battle.Scenario.DefenderVisionEnabled && battle.Scenario.DefenderSide == (int)side;
        public bool DeploymentKnown(BattleSide side, GridPosition p) => battle!=null&&!HeightMode&&map.Cell(p)?.Zone == (side == BattleSide.A ? DeploymentZone.A : DeploymentZone.B);
        public bool Visible(BattleSide side, GridPosition p) => map.Contains(p) && (FullVision(side) || visible[(int)side][Index(p)]);
        public bool Explored(BattleSide side, GridPosition p) => map.Contains(p) && (FullVision(side) || explored[(int)side][Index(p)]);
        public bool TerrainVisible(BattleSide side,GridPosition p)=>map.Contains(p)&&(FullVision(side)||(HeightMode?terrain[(int)side][Index(p)]||explored[(int)side][Index(p)]:Explored(side,p)));
        public void RefreshExploration(IEnumerable<GridPosition> observers,bool fogEnabled=true){explorationFullVision=!fogEnabled;BattleHeightSight.Compute(map,fogEnabled?observers:Enumerable.Empty<GridPosition>(),visible[0],terrain[0]);for(int n=0;n<visible[0].Length;n++)explored[0][n]|=terrain[0][n]||visible[0][n];Revision++;}
        public bool[] KnownTerrain(BattleSide side)=>FullVision(side)?Enumerable.Repeat(true,map.Width*map.Height).ToArray():explored[(int)side].ToArray();
        public void RememberTerrain(BattleSide side,bool[] known)
        {if(known==null)return;if(known.Length!=map.Width*map.Height)throw new ArgumentException("已知地形尺寸不符");for(int n=0;n<known.Length;n++)explored[(int)side][n]|=known[n];Revision++;}
        public int LastSeen(BattleSide side, GridPosition p) => lastSeen[(int)side][Index(p)];
        public bool CanSee(BattleSide observer, BattleSoldier soldier) => soldier != null && (soldier.Side == observer || Visible(observer, soldier.Cell));
        public void Refresh(bool roundResolved = false)
        {
            foreach (BattleSide side in Enum.GetValues(typeof(BattleSide)))
            {
                int i = (int)side; bool[] before = (bool[])visible[i].Clone(); Array.Clear(visible[i], 0, visible[i].Length);
                foreach (GridPosition p in battle.Board.Positions())
                {
                    // 本方部署区是已知地图，库存本身不提供敌军视野。
                    if (battle.Board.Cell(p).Zone == (side == BattleSide.A ? DeploymentZone.A : DeploymentZone.B)) explored[i][Index(p)] = true;
                    if (FullVision(side)) visible[i][Index(p)] = true;
                }
                if(HeightMode&&!FullVision(side))BattleHeightSight.Compute(map,battle.Soldiers.Where(s=>s.Side==side&&s.IsActive).Select(s=>s.Cell),visible[i],terrain[i]);
                else if(!FullVision(side)) foreach (BattleSoldier unit in battle.Soldiers.Where(s => s.Side == side && s.IsActive))
                    foreach (GridPosition p in battle.Board.Positions())
                        if (GridPosition.Distance(unit.Cell, p) <= battle.Scenario.VisionRadius && battle.Board.HasLineOfSight(unit.Cell, p)) visible[i][Index(p)] = true;
                foreach (GridPosition p in battle.Board.Positions())
                    { if(HeightMode&&terrain[i][Index(p)])explored[i][Index(p)]=true;if (visible[i][Index(p)]) { explored[i][Index(p)] = true; lastSeen[i][Index(p)] = battle.RoundNumber; } }
                foreach (BattleSoldier enemy in battle.Soldiers.Where(s => s.Side != side))
                {
                    bool now = Visible(side, enemy.Cell);
                    bool observedDeath = !enemy.IsAlive && (now || (roundResolved && battle.Board.Contains(enemy.PreviousCell) && before[Index(enemy.PreviousCell)]));
                    if (now && !enemy.Escaped || observedDeath) sightings[i][enemy.Id] = enemy.Kind;
                    if (observedDeath) confirmedDeaths[i].Add(enemy.Id);
                }
            }
            Revision++;
            battle.RefreshProximityEffects();
        }
        public ArmyStatus Status(BattleSide observer, BattleSide side, SoldierKind kind)
        {
            bool exact = observer == side || FullVision(observer);
            return new ArmyStatus
            {
                Exact = exact,
                InField = battle.Soldiers.Count(s => s.Side == side && s.Kind == kind && s.IsActive && (exact || CanSee(observer, s))),
                Defeated = exact ? battle.Soldiers.Count(s => s.Side == side && s.Kind == kind && !s.IsAlive) : confirmedDeaths[(int)observer].Count(id => sightings[(int)observer].TryGetValue(id, out SoldierKind seenKind) && seenKind == kind),
                Stock = exact ? battle.Remaining(side, kind) : -1,
                SeenTotal = exact ? battle.Soldiers.Count(s => s.Side == side && s.Kind == kind) : sightings[(int)observer].Values.Count(k => k == kind)
            };
        }
    }
}
