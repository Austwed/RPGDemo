using System;
using System.Linq;
namespace BaseConquest.Battle
{
    public static class DeveloperTerrain
    {
        public static bool Paint(bool enabled,BattleBoard board,GridPosition p,TerrainKind kind,string buildingId="")
        {
            if(!enabled||board?.Contains(p)!=true||!Enum.IsDefined(typeof(TerrainKind),kind))return false;
            var c=board.Cell(p);
            if(!c.HasDeveloperTerrain){c.OriginalTerrain=c.Terrain;c.OriginalZone=c.Zone;c.OriginalBuildingId=c.BuildingId;c.HasDeveloperTerrain=true;}
            c.Terrain=kind;c.BuildingId=kind==TerrainKind.Building ? buildingId??"" : "";
            if(!c.Walkable)c.Zone=DeploymentZone.None;
            return true;
        }
        public static void Restore(BattleBoard board)
        {
            if(board?.Cells==null)return;
            foreach(var c in board.Cells.Where(c=>c.HasDeveloperTerrain))
            {c.Terrain=c.OriginalTerrain;c.Zone=c.OriginalZone;c.BuildingId=c.OriginalBuildingId??"";c.HasDeveloperTerrain=false;c.OriginalTerrain=default;c.OriginalZone=default;c.OriginalBuildingId="";}
        }
        public static string Name(TerrainKind kind) => kind==TerrainKind.Water ? "水域" : kind==TerrainKind.Wall ? "城墙" : kind==TerrainKind.Swamp ? "沼泽" : kind==TerrainKind.Building ? "建筑" : "地面";
    }
}
