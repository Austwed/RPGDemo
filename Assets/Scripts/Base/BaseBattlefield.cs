using System;
using System.Linq;
using BaseConquest.Battle;
namespace BaseConquest.Base
{
    [Serializable] public sealed class BaseRegionMap
    {
        public string Id;
        public BattleBoard Board;
        public BaseRegionMap Copy() => new BaseRegionMap { Id=Id, Board=Board.Clone() };
    }
    // 运行中只有一张地图；战斗绑定原对象。存读档的副本只用于保存快照。
    public sealed class BaseMapSession
    {
        public readonly BattleBoard Board;
        private BattleBoard peacefulBoard;
        public BattleBoard AppearanceBoard=>peacefulBoard??Board;
        public bool InBattle { get; private set; }
        public BaseMapSession(BattleBoard board) { Board=board??throw new ArgumentNullException(nameof(board)); }
        public void EnterBattle(BattleSimulation battle)
        {
            if (InBattle) throw new InvalidOperationException("地图已在战斗中");
            if (battle.Board.Width!=Board.Width || battle.Board.Height!=Board.Height || !battle.Board.Cells.Select(c=>(int)c.Terrain+":"+(int)c.Zone+":"+c.ObstacleHealth+":"+c.IsStake+":"+c.ObstacleOwner+":"+c.FixedCover+":"+c.Elevation+":"+(int)c.Ramp+":"+(int)c.Surface).SequenceEqual(Board.Cells.Select(c=>(int)c.Terrain+":"+(int)c.Zone+":"+c.ObstacleHealth+":"+c.IsStake+":"+c.ObstacleOwner+":"+c.FixedCover+":"+c.Elevation+":"+(int)c.Ramp+":"+(int)c.Surface))) throw new InvalidOperationException("战斗地图与当前区域不符，拒绝替换当前地图");
            battle.Scenario.Board=Board; InBattle=true; battle.Vision.Refresh();
        }
        public void PrepareCombat(BaseState state,BattleBoard encounterBoard=null)
        {
            if(InBattle||peacefulBoard!=null)throw new InvalidOperationException("当前地图已进入战斗");
            var expected=BaseSceneCombat.Project(state);var projected=encounterBoard??expected;
            if(projected.Width!=Board.Width||projected.Height!=Board.Height)throw new InvalidOperationException("同图战斗不能替换地图尺寸");
            if(projected.Cells.Where((cell,index)=>cell.Terrain!=expected.Cells[index].Terrain||cell.Elevation!=expected.Cells[index].Elevation||cell.Ramp!=expected.Cells[index].Ramp).Any())throw new InvalidOperationException("同图遭遇不能悄悄替换场景地形");
            peacefulBoard=Board.Clone();
            BaseSceneInstances.SaveBoard(state,Board.Clone(),state.Campaign.Location,BaseSceneInstances.Active(state)?state.Campaign.CurrentMapId:null);
            for(int index=0;index<Board.Cells.Length;index++)Board.Cells[index]=projected.Cells[index].Clone();
        }
        public void LeaveBattle() { if(peacefulBoard!=null){for(int index=0;index<Board.Cells.Length;index++)Board.Cells[index]=peacefulBoard.Cells[index].Clone();peacefulBoard=null;}InBattle=false; }
    }
    public static class BaseBattlefield
    {
        public static void MigrateLegacyMap(BaseState state)
        {
            var c=state.Campaign;
            if(c.MapVersion==3)return;
            if(c.MapVersion==2){UpgradeBuildings(state);return;}
            if(c.MapVersion!=0&&c.MapVersion!=1)throw new ArgumentException("未知区域地图版本");
            c.RegionMaps??=new System.Collections.Generic.List<BaseRegionMap>();
            if(c.HasActiveBattle&&c.ActiveBattle?.Scenario?.Board!=null)
            {
                string location=c.Events?.FirstOrDefault(e=>e.Id==c.ActiveBattle.EventId)?.Kind=="defense" ? "perimeter" : "outpost";
                var board=(c.ActiveBattle.HasCheckpoint ? c.ActiveBattle.Checkpoint?.Scenario?.Board : c.ActiveBattle.Scenario.Board)?.Clone();
                if(board==null)throw new ArgumentException("旧战斗缺少地图快照");
                if(location=="perimeter")c.PerimeterBoard=board;
                else {c.RegionMaps.RemoveAll(m=>m.Id==location);c.RegionMaps.Add(new BaseRegionMap {Id=location,Board=board});}
                c.Location=location;
            }
            c.RegionMaps.RemoveAll(m=>m.Id=="world");c.RegionMaps.Add(new BaseRegionMap {Id="world",Board=BaseWorldMap.CreateWorld()});
            if(!BaseMap.Walkable(c.WorldX,c.WorldY,null,false)){c.WorldX=12;c.WorldY=6.5;}
            UpgradeBuildings(state);
        }

        private static void UpgradeBuildings(BaseState state)
        {
            var c=state.Campaign;
            var map=c.RegionMaps?.FirstOrDefault(m=>m.Id=="base");
            if(map?.Board?.Cells!=null)foreach(var p in map.Board.Positions())
            {
                var cell=map.Board.Cell(p);var o=BuildingAt(p);
                if(o!=null&&(cell.Terrain==TerrainKind.Wall||cell.Terrain==TerrainKind.Building)){cell.Terrain=TerrainKind.Building;cell.BuildingId=o.Id;cell.Zone=DeploymentZone.None;}
            }
            foreach(var e in c.Events??new System.Collections.Generic.List<BaseCampaignEvent>())if(e.Kind=="defense"&&e.Status=="pending"&&e.HoldRounds>0&&e.HoldRounds<=12&&(!c.HasActiveBattle||c.ActiveBattle?.EventId!=e.Id))e.HoldRounds=30;
            c.MapVersion=3;
        }
        private static BaseMapObject BuildingAt(GridPosition p) => BaseMap.Objects.FirstOrDefault(o=>o.Blocks&&!o.Id.StartsWith("partner")&&Math.Abs(p.X+.5-o.X)<o.Width/2&&Math.Abs(p.Z+.5-o.Y)<o.Height/2);
        public static BattleBoard Create()
        {
            var b = BattleBoard.CreateDefault(24, 14);
            foreach (var p in b.Positions())
            {
                var c = b.Cell(p); c.Zone = p.X >= 20 ? DeploymentZone.A : p.X < 4 ? DeploymentZone.B : DeploymentZone.None;
                if (p.X == 10 && p.Z >= 2 && p.Z <= 4 || p.X == 16 && p.Z >= 9 && p.Z <= 11) c.Terrain = TerrainKind.Wall;
                if (p.X >= 7 && p.X <= 8 && p.Z >= 9 && p.Z <= 11) c.Terrain = TerrainKind.Water;
                if (p.X >= 11 && p.X <= 14 && p.Z >= 5 && p.Z <= 7) c.Terrain = TerrainKind.Swamp;
            }
            return b;
        }
        public static void Ensure(BaseState s) { if(s.Campaign.PerimeterBoard==null||s.Campaign.PerimeterBoard.Cells==null)s.Campaign.PerimeterBoard=Create(); }
        public static BattleBoard Board(BaseState s, string location = null)
        {
            Ensure(s); location=location??s.Campaign.Location;
            if(BaseSceneInstances.Active(s))
            {
                var layout=BaseSceneInstances.Layout(s,location);
                if(layout.Region=="perimeter")return s.Campaign.PerimeterBoard;
                return s.Campaign.RegionMaps.Single(map=>map.Id==layout.SourceMapId).Board;
            }
            if(location=="perimeter") return s.Campaign.PerimeterBoard;
            s.Campaign.RegionMaps??=new System.Collections.Generic.List<BaseRegionMap>();
            var map=s.Campaign.RegionMaps.SingleOrDefault(m=>m.Id==location);
            if(map!=null) return map.Board;
            var board=location=="world" ? BaseWorldMap.CreateWorld() : BattleBoard.CreateDefault(24,14);
            foreach(var p in board.Positions())
            {
                var cell=board.Cell(p); cell.Zone=location=="world" ? DeploymentZone.None : p.X>=20 ? DeploymentZone.A : p.X<4 ? DeploymentZone.B : DeploymentZone.None;
                if(location=="base" && BuildingAt(p) is BaseMapObject building) { cell.Terrain=TerrainKind.Building;cell.BuildingId=building.Id;cell.Zone=DeploymentZone.None; }
            }
            if(location=="base")
            {
                // A walkable terrace surrounds the barracks; existing/custom saved maps remain authoritative.
                for(int z=8;z<=12;z++)for(int x=8;x<=15;x++)board.Cell(new GridPosition(x,z)).Elevation=1;
                foreach(int x in new[]{11,12})
                {var c=board.Cell(new GridPosition(x,7));c.Elevation=1;c.Ramp=RampAxis.NorthSouth;c.Surface=GroundSurface.Road;}
                foreach(int x in new[]{14,15})
                {var c=board.Cell(new GridPosition(x,12));c.Ramp=RampAxis.NorthSouth;c.Surface=GroundSurface.Road;}
                foreach(int x in new[]{8,15})
                {var c=board.Cell(new GridPosition(x,10));c.Ramp=RampAxis.EastWest;c.Surface=GroundSurface.Road;}
            }
            map=new BaseRegionMap {Id=location,Board=board};s.Campaign.RegionMaps.Add(map);return board;
        }
        public static void KeepLiveBoard(BaseState state, BattleBoard live)
        {
            var saved=Board(state);
            if(saved.Width!=live.Width||saved.Height!=live.Height) throw new InvalidOperationException("地图尺寸变化需要重新载入区域");
            for(int i=0;i<live.Cells.Length;i++){live.Cells[i]=saved.Cells[i].Clone();}
            if(state.Campaign.Location=="perimeter")state.Campaign.PerimeterBoard=live;
            else state.Campaign.RegionMaps.Single(m=>m.Id==BaseSceneInstances.BoardKey(state)).Board=live;
        }
        public static bool Walkable(BaseState s, double x, double y, string location=null)
        {
            return BaseSpatial.Walkable(s,x,y,location);
        }
    }
}
