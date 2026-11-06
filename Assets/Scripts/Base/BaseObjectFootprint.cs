using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;

namespace BaseConquest.Base
{
    public readonly struct BaseObjectRect
    {
        public readonly double X,Y,Width,Height;
        public BaseObjectRect(double x,double y,double width,double height){X=x;Y=y;Width=width;Height=height;}
        public bool Contains(double x,double y,double padding=0)=>Math.Abs(x-X)<Width/2+padding&&Math.Abs(y-Y)<Height/2+padding;
        public bool Touches(int x,int y)=>x<X+Width/2-.00001&&x+1>X-Width/2+.00001&&y<Y+Height/2-.00001&&y+1>Y-Height/2+.00001;
        public double Distance(double x,double y){double dx=Math.Max(Math.Abs(x-X)-Width/2,0),dy=Math.Max(Math.Abs(y-Y)-Height/2,0);return Math.Sqrt(dx*dx+dy*dy);}
        // 包围盒上的最近点：交互朝向锥用它判断「物件在我的哪一侧」。
        public void Closest(double x,double y,out double px,out double py,out double distance)
        {
            px=Math.Max(X-Width/2,Math.Min(x,X+Width/2));py=Math.Max(Y-Height/2,Math.Min(y,Y+Height/2));
            double dx=x-px,dy=y-py;distance=Math.Sqrt(dx*dx+dy*dy);
        }
    }
    public static class BaseObjectFootprint
    {
        public static BaseObjectRect[] Parts(BaseMapObject obj)
        {
            var local=obj.Shape=="l" ? new[]{new BaseObjectRect(0,(1-obj.Height)/2,obj.Width,1),new BaseObjectRect((1-obj.Width)/2,.5,1,obj.Height-1)} : new[]{new BaseObjectRect(0,0,obj.Width,obj.Height)};
            for(int index=0;index<local.Length;index++)
            {
                var part=local[index];
                for(int turn=0;turn<Math.Min(270,Math.Max(0,obj.Rotation));turn+=90)part=new BaseObjectRect(part.Y,-part.X,part.Height,part.Width);
                local[index]=new BaseObjectRect(obj.X+part.X,obj.Y+part.Y,part.Width,part.Height);
            }
            return local;
        }
        public static IEnumerable<GridPosition> Cells(BaseMapObject obj)
        {
            for(int row=(int)Math.Floor(obj.Y-obj.BoundsHeight/2+.00001);row<(int)Math.Ceiling(obj.Y+obj.BoundsHeight/2-.00001);row++)
                for(int column=(int)Math.Floor(obj.X-obj.BoundsWidth/2+.00001);column<(int)Math.Ceiling(obj.X+obj.BoundsWidth/2-.00001);column++)
                    if(obj.Parts.Any(part=>part.Touches(column,row)))yield return new GridPosition(column,row);
        }
        public static void Validate(BaseMapPlacement obj)
        {
            if(!new[]{0,90,180,270}.Contains(obj.Rotation)||!string.IsNullOrEmpty(obj.Shape)&&!new[]{"rect","bar","l"}.Contains(obj.Shape))throw new ArgumentException("格形或旋转非法："+obj.Name);
            if(string.IsNullOrEmpty(obj.Shape)){if(obj.Rotation!=0)throw new ArgumentException("旧矩形不可直接旋转："+obj.Name);return;}
            if(obj.Kind!="facility"&&obj.Kind!="obstacle"||obj.Width!=Math.Truncate(obj.Width)||obj.Height!=Math.Truncate(obj.Height)||obj.Width<1||obj.Height<1||obj.Width>24||obj.Height>24||obj.Shape=="bar"&&obj.Width!=1&&obj.Height!=1||obj.Shape=="l"&&(obj.Width<2||obj.Height<2))throw new ArgumentException("格形须为设施／障碍的整数预设："+obj.Name);
            var shape=obj.Object;
            double left=obj.X-shape.BoundsWidth/2,bottom=obj.Y-shape.BoundsHeight/2;
            if(Math.Abs(left-Math.Round(left))>.000001||Math.Abs(bottom-Math.Round(bottom))>.000001)throw new ArgumentException("格形须对齐整数地格："+obj.Name);
        }
        public static void ValidateBoard(BaseMapLayout layout,BattleBoard board)
        {
            foreach(var placement in layout.Objects.Where(obj=>!string.IsNullOrEmpty(obj.Shape)))
            {
                var obj=placement.Object;var cells=Cells(obj).ToArray();
                if(placement.Kind=="facility"&&(cells.Select(cell=>board.Cell(cell).Elevation).Distinct().Count()!=1||cells.Any(cell=>board.Cell(cell).Ramp!=RampAxis.None)))throw new ArgumentException("格形地基必须同高且无坡道："+placement.Name);
                if(obj.Blocks&&cells.Any(cell=>board.Cell(cell).Terrain!=TerrainKind.Building||board.Cell(cell).BuildingId!=obj.Id))throw new ArgumentException("格形与地图阻挡不一致："+placement.Name);
                if(obj.Blocks&&board.Positions().Any(cell=>board.Cell(cell).BuildingId==obj.Id&&!cells.Contains(cell)))throw new ArgumentException("格形缺口不能标为建筑："+placement.Name);
            }
        }
    }
}
