using System;
using System.Collections.Generic;

namespace BaseConquest.Battle
{
    [Serializable]
    public sealed class BattleBoard
    {
        public int Width = 20, Height = 14;
        public BoardCell[] Cells;
        public bool Contains(GridPosition p) => p.X >= 0 && p.X < Width && p.Z >= 0 && p.Z < Height;
        public BoardCell Cell(GridPosition p) => Contains(p) ? Cells[p.Z * Width + p.X] : null;
        public bool Walkable(GridPosition p) => Cell(p)?.Walkable ?? false;
        public double SurfaceLevel(double positionX,double positionZ)
        {var cell=new GridPosition((int)Math.Floor(positionX),(int)Math.Floor(positionZ));return SurfaceLevel(cell,positionX-cell.X,positionZ-cell.Z);}
        public double SurfaceLevel(GridPosition position,double localX,double localZ)
        {
            var cell=Cell(position);if(cell==null)return 0;
            int low=RampLowEdge(position);
            if(low==0)return cell.Elevation;
            double along=Math.Max(0,Math.Min(1,cell.Ramp==RampAxis.EastWest ? localX : localZ));
            return cell.Elevation-(low<0?1-along:along);
        }
        public int RampLowEdge(GridPosition position)
        {
            var cell=Cell(position);
            if(cell==null||cell.Ramp==RampAxis.None)return 0;
            var step=cell.Ramp==RampAxis.EastWest ? new GridPosition(1,0) : new GridPosition(0,1);
            if(Cell(position+new GridPosition(-step.X,-step.Z))?.Elevation==cell.Elevation-1)return -1;
            return Cell(position+step)?.Elevation==cell.Elevation-1?1:0;
        }
        public bool HeightConnection(GridPosition from,GridPosition to)
        {
            if(!Contains(from)||!Contains(to)||GridPosition.Distance(from,to)!=1)return false;
            var a=Cell(from);var b=Cell(to);int difference=Math.Abs(a.Elevation-b.Elevation);
            if(difference==0)return true;
            var axis=from.X!=to.X ? RampAxis.EastWest : RampAxis.NorthSouth;
            return difference==1&&(a.Ramp==axis||b.Ramp==axis);
        }
        public bool CanStep(GridPosition from,GridPosition to)=>Walkable(from)&&Walkable(to)&&HeightConnection(from,to);
        public string HeightDescription(GridPosition p)
        {var c=Cell(p);return c==null ? "" : "高度"+c.Elevation+(c.Ramp==RampAxis.EastWest ? " · 东西向坡道" : c.Ramp==RampAxis.NorthSouth ? " · 南北向坡道" : "");}
        public IEnumerable<GridPosition> Positions()
        {
            for (int z = 0; z < Height; z++) for (int x = 0; x < Width; x++) yield return new GridPosition(x, z);
        }
        public void EnsureValid()
        {
            Width = Math.Max(6, Math.Min(64, Width)); Height = Math.Max(6, Math.Min(64, Height));
            if (Cells == null || Cells.Length != Width * Height)
            {
                Cells = new BoardCell[Width * Height];
                foreach (GridPosition p in Positions()) Cells[p.Z * Width + p.X] = new BoardCell { Zone = p.X < Width / 4 ? DeploymentZone.A : p.X >= Width - Width / 4 ? DeploymentZone.B : DeploymentZone.None };
            }
            for (int i = 0; i < Cells.Length; i++)
            {
                Cells[i] ??= new BoardCell();
                if(Cells[i].SandbagGroup<0||Cells[i].HiddenTrap&&Cells[i].SandbagGroup>0)throw new ArgumentException("防御工事状态非法");
                if(Cells[i].Elevation<0||Cells[i].Elevation>3||!Enum.IsDefined(typeof(RampAxis),Cells[i].Ramp))throw new ArgumentException("地格高度须为0～3，坡道方向非法");
                if (!Cells[i].Walkable) Cells[i].Zone = DeploymentZone.None;
            }
        }
        public static BattleBoard CreateDefault(int width = 20, int height = 14)
        {
            var board = new BattleBoard { Width = width, Height = height }; board.EnsureValid(); return board;
        }
        public BattleBoard Clone()
        {
            var board = new BattleBoard { Width = Width, Height = Height, Cells = new BoardCell[Width * Height] };
            if (Cells != null && Cells.Length == board.Cells.Length)
                for (int i = 0; i < Cells.Length; i++) board.Cells[i] = Cells[i]?.Clone() ?? new BoardCell();
            else board.Cells = null;
            board.EnsureValid(); return board;
        }
        public bool Paint(GridPosition p, BoardBrush brush)
        {
            BoardCell cell = Cell(p);
            if (cell == null) return false;
            if(brush>=BoardBrush.Height0&&brush<=BoardBrush.Height3){cell.Elevation=(int)brush-(int)BoardBrush.Height0;return true;}
            if(brush==BoardBrush.RampEastWest||brush==BoardBrush.RampNorthSouth||brush==BoardBrush.ClearRamp)
            {if(!cell.Walkable&&brush!=BoardBrush.ClearRamp)return false;cell.Ramp=brush==BoardBrush.RampEastWest ? RampAxis.EastWest : brush==BoardBrush.RampNorthSouth ? RampAxis.NorthSouth : RampAxis.None;return true;}
            if (brush == BoardBrush.ZoneA || brush == BoardBrush.ZoneB)
            {
                if (!cell.Walkable) return false;
                cell.Zone = brush == BoardBrush.ZoneA ? DeploymentZone.A : DeploymentZone.B;
            }
            else if (brush == BoardBrush.ClearZone) cell.Zone = DeploymentZone.None;
            else
            {
                cell.Terrain = brush == BoardBrush.Ground ? TerrainKind.Ground : brush == BoardBrush.Water ? TerrainKind.Water : brush == BoardBrush.Swamp ? TerrainKind.Swamp : brush == BoardBrush.Building ? TerrainKind.Building : TerrainKind.Wall;
                cell.BuildingId = "";
                cell.ObstacleHealth=0;cell.FixedCover=false;
                if (!cell.Walkable) cell.Zone = DeploymentZone.None;
            }
            return true;
        }

        // Supercover：斜线正好穿过角点时，角点两侧的格子也纳入遮挡检查。
        public bool HasLineOfSight(GridPosition from, GridPosition to)
        {
            if (!Contains(from) || !Contains(to)) return false;
            int x = from.X, z = from.Z, dx = to.X - x, dz = to.Z - z;
            int sx = Math.Sign(dx), sz = Math.Sign(dz);
            double tx = dx == 0 ? double.PositiveInfinity : 0.5 / Math.Abs(dx);
            double tz = dz == 0 ? double.PositiveInfinity : 0.5 / Math.Abs(dz);
            double stepx = dx == 0 ? double.PositiveInfinity : 1.0 / Math.Abs(dx);
            double stepz = dz == 0 ? double.PositiveInfinity : 1.0 / Math.Abs(dz);
            while (x != to.X || z != to.Z)
            {
                if (Math.Abs(tx - tz) < 0.0000001)
                {
                    var here=new GridPosition(x,z);var across=new GridPosition(x+sx,z+sz);var sideX=new GridPosition(x+sx,z);var sideZ=new GridPosition(x,z+sz);
                    if(!HeightConnection(here,sideX)||!HeightConnection(here,sideZ)||!HeightConnection(sideX,across)||!HeightConnection(sideZ,across))return false;
                    if (Blocks(new GridPosition(x + sx, z), from, to) || Blocks(new GridPosition(x, z + sz), from, to)) return false;
                    x += sx; z += sz; tx += stepx; tz += stepz;
                }
                else if (tx < tz) {if(!HeightConnection(new GridPosition(x,z),new GridPosition(x+sx,z)))return false; x += sx; tx += stepx; }
                else {if(!HeightConnection(new GridPosition(x,z),new GridPosition(x,z+sz)))return false; z += sz; tz += stepz; }
                if (Blocks(new GridPosition(x, z), from, to)) return false;
            }
            return true;
        }
        private bool Blocks(GridPosition p, GridPosition from, GridPosition to) => p != from && p != to && (!Contains(p) || Cell(p).BlocksShots);

        public static BattleBoard Siege(bool aSurroundsB)
        {
            BattleBoard board = CreateDefault();
            int cx = board.Width / 2, cz = board.Height / 2;
            foreach (GridPosition p in board.Positions())
            {
                BoardCell cell = board.Cell(p); cell.Zone = DeploymentZone.None;
                bool edge = p.X < 2 || p.X >= board.Width - 2 || p.Z < 2 || p.Z >= board.Height - 2;
                bool center = Math.Abs(p.X - cx) <= 2 && Math.Abs(p.Z - cz) <= 2;
                if (edge) cell.Zone = aSurroundsB ? DeploymentZone.A : DeploymentZone.B;
                if (center) cell.Zone = aSurroundsB ? DeploymentZone.B : DeploymentZone.A;
                bool wall = (Math.Abs(p.X - cx) == 3 && Math.Abs(p.Z - cz) <= 3) || (Math.Abs(p.Z - cz) == 3 && Math.Abs(p.X - cx) <= 3);
                if (wall && !(p.Z == cz && Math.Abs(p.X - cx) == 3)) board.Paint(p, BoardBrush.Wall);
            }
            return board;
        }
    }
}
