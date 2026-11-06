using System;
using System.Linq;
using BaseConquest.Battle;

namespace BaseConquest.Base
{
    public static class BaseInteraction
    {
        // 交互距离与正面判定（2026-11-06 裁决）：前方 120° 锥 ＋ 1.5 粗格，大物件按包围盒最近点。
        // 背面不显示交互按钮；贴身时不判朝向，避免贴着墙站反而点不到。
        public const double Distance=1.5, FrontCosine=.5, FacingSlack=.15;
        // 玩家当前朝向：表现层运行时状态，不进存档。只由行走方向更新，停下后保持最后方向。
        public static BasePoint Facing=new BasePoint(0,1);
        public static void Walk(BasePoint direction)
        {
            double length=Math.Sqrt(direction.X*direction.X+direction.Y*direction.Y);
            if(length>1e-6)Facing=new BasePoint(direction.X/length,direction.Y/length);
        }
        public static bool CanReach(BattleBoard board,BaseMapObject target,double positionX,double positionY)
            =>CanReach(board,target,positionX,positionY,Facing);
        public static bool CanReach(BattleBoard board,BaseMapObject target,double positionX,double positionY,BasePoint facing)
        {
            if(board==null||target==null||target.VisualOnly||BaseEnvironment.Decorative(target)||!(target.Distance(positionX,positionY)<=Distance+.000000001))return false;
            var anchor=target.Parts[0];
            if(Math.Abs(board.SurfaceLevel(positionX,positionY)-board.SurfaceLevel(string.IsNullOrEmpty(target.Shape)?target.X:anchor.X,string.IsNullOrEmpty(target.Shape)?target.Y:anchor.Y))>=.000001)return false;
            double closestX=positionX,closestY=positionY,gap=double.PositiveInfinity;
            foreach(var part in target.Parts){part.Closest(positionX,positionY,out double px,out double py,out double distance);if(distance<gap){gap=distance;closestX=px;closestY=py;}}
            if(gap<=FacingSlack)return true;
            double fx=facing.X,fy=facing.Y,facingLength=Math.Sqrt(fx*fx+fy*fy);
            if(facingLength<1e-6)return true;
            double dx=closestX-positionX,dy=closestY-positionY;
            return (dx*fx+dy*fy)/(gap*facingLength)>=FrontCosine;
        }
        public static bool CanReach(BaseState state,BaseMapObject target)
        {
            if(state==null||target==null||target.VisualOnly||BaseSceneCombat.Active(state))return false;
            if(state.Leaders.Any(person=>person.Id==target.Id))return target.Distance(state.PlayerX,state.PlayerY)<BaseMap.InteractionDistance;
            return CanReach(BaseBattlefield.Board(state),target,state.PlayerX,state.PlayerY);
        }
        public static bool At(BaseState state,string region,string id)=>state.Campaign.Location==region&&CanReach(state,BaseSpatial.Objects(state,region).SingleOrDefault(target=>target.Id==id));
    }
}
