using System;
using System.Collections.Generic;

namespace BaseConquest.Battle
{
    public static class BattleHeightSight
    {
        public static double Radius(BattleBoard board,GridPosition observer)=>Math.Min(16,10+2*board.Cell(observer).Elevation);
        public static bool Clear(BattleBoard board,double ax,double az,double bx,double bz)
        {
            var from=new GridPosition((int)ax,(int)az);var to=new GridPosition((int)bx,(int)bz);
            if(!board.Contains(from)||!board.Contains(to))return false;
            double eye=board.SurfaceLevel(ax,az)+1.5,end=board.SurfaceLevel(bx,bz)+1.5;
            int steps=Math.Max(1,(int)Math.Ceiling(Math.Max(Math.Abs(bx-ax),Math.Abs(bz-az))*4));
            for(int n=1;n<steps;n++)
            {
                double t=(double)n/steps,x=ax+(bx-ax)*t,z=az+(bz-az)*t;var p=new GridPosition((int)x,(int)z);
                if(p==from||p==to)continue;var c=board.Cell(p);if(c==null)return false;
                double top=board.SurfaceLevel(x,z)+(c.HasSightHeight?c.SightHeight:c.Terrain==TerrainKind.Wall||c.Terrain==TerrainKind.Building?3:0);
                if(top>=eye+(end-eye)*t-1e-7)return false;
            }
            return true;
        }
        public static bool Clear(BattleBoard board,GridPosition a,GridPosition b)=>Clear(board,a.X+.5,a.Z+.5,b.X+.5,b.Z+.5);
        public static double ArcherBonus(BattleBoard board,double ax,double az,double bx,double bz)=>Math.Min(6,Math.Max(0,board.SurfaceLevel(ax,az)-board.SurfaceLevel(bx,bz))*2);
        public static void Compute(BattleBoard board,IEnumerable<GridPosition> observers,bool[] visible,bool[] terrain)
        {
            Array.Clear(visible,0,visible.Length);Array.Clear(terrain,0,terrain.Length);
            foreach(var observer in observers)
            {
                if(!board.Contains(observer))continue;double radius=Radius(board,observer);
                foreach(var p in board.Positions())
                {
                    int i=p.Z*board.Width+p.X;double dx=p.X-observer.X,dz=p.Z-observer.Z;bool near=dx*dx+dz*dz<=radius*radius;
                    if(terrain[i]&&(!near||visible[i]))continue;
                    if(!Clear(board,observer,p))continue;terrain[i]=true;if(near)visible[i]=true;
                }
            }
        }
    }
}
