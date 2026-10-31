using System.Collections.Generic;
namespace BaseConquest.Battle
{
    // 同一设施的格子共享一张完整贴图；挖去中间一格只露出地面，不重新铺满剩余格。
    public sealed class BuildingFootprint
    {
        public int MinX, MaxX, MinZ, MaxZ;
        public int Width => MaxX - MinX + 1;
        public int Height => MaxZ - MinZ + 1;
        public float U(float gridX) => (gridX - MinX) / Width;
        public float V(float gridZ) => (gridZ - MinZ) / Height;
        public static Dictionary<string, BuildingFootprint> Read(BattleBoard board)
        {
            var result = new Dictionary<string, BuildingFootprint>();
            foreach (var p in board.Positions())
            {
                var cell = board.Cell(p);
                if (cell.Terrain != TerrainKind.Building || string.IsNullOrEmpty(cell.BuildingId)) continue;
                if (!result.TryGetValue(cell.BuildingId, out var area))
                    result[cell.BuildingId] = new BuildingFootprint { MinX = p.X, MaxX = p.X, MinZ = p.Z, MaxZ = p.Z };
                else
                {
                    area.MinX = System.Math.Min(area.MinX, p.X); area.MaxX = System.Math.Max(area.MaxX, p.X);
                    area.MinZ = System.Math.Min(area.MinZ, p.Z); area.MaxZ = System.Math.Max(area.MaxZ, p.Z);
                }
            }
            return result;
        }
    }
}
