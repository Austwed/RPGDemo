using System;
namespace BaseConquest.Presentation
{
    public readonly struct GameFrameRect
    {
        public readonly double X,Y,Width,Height;
        public double Scale=>Width/1280;
        public GameFrameRect(double x,double y,double width,double height){X=x;Y=y;Width=width;Height=height;}
    }
    public static class GameFrameLayout
    {
        public static GameFrameRect Fit(double width,double height,double safeX=0,double safeY=0,double safeWidth=0,double safeHeight=0)
        {
            if(double.IsNaN(width)||double.IsNaN(height)||double.IsInfinity(width)||double.IsInfinity(height)||width<=0||height<=0)throw new ArgumentException("屏幕尺寸无效");
            if(double.IsNaN(safeX)||double.IsNaN(safeY)||double.IsNaN(safeWidth)||double.IsNaN(safeHeight)||double.IsInfinity(safeX)||double.IsInfinity(safeY)||double.IsInfinity(safeWidth)||double.IsInfinity(safeHeight)||safeWidth<=0||safeHeight<=0){safeX=safeY=0;safeWidth=width;safeHeight=height;}
            double x=Math.Max(0,Math.Min(width,safeX)),y=Math.Max(0,Math.Min(height,safeY));
            double right=Math.Max(x,Math.Min(width,safeX+safeWidth)),top=Math.Max(y,Math.Min(height,safeY+safeHeight));
            if(right<=x||top<=y){x=y=0;right=width;top=height;}
            double scale=Math.Min((right-x)/1280,(top-y)/720),w=1280*scale,h=720*scale;
            return new GameFrameRect(x+(right-x-w)/2,y+(top-y-h)/2,w,h);
        }
    }
}
