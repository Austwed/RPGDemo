using System;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.Presentation;

namespace BaseConquest.Base
{
    public static class BaseInteraction
    {
        // 交互距离与正面判定（2026-11-06 裁决）：前方 120° 锥 ＋ 1.5 粗格，大物件按包围盒最近点。
        // 背面不显示交互按钮；贴身时不判朝向，避免贴着墙站反而点不到。
        public const double Distance=1.5, FrontCosine=.5, FacingSlack=.15;
        private const double LordDistance = 2.25;
        // 玩家当前朝向：表现层运行时状态，不进存档。只由行走方向更新，停下后保持最后方向。
        public static BasePoint Facing=new BasePoint(0,1);
        public static void Walk(BasePoint direction)
        {
            double length=Math.Sqrt(direction.X*direction.X+direction.Y*direction.Y);
            if(length>1e-6)Facing=new BasePoint(direction.X/length,direction.Y/length);
        }
        internal static void RecordWalk(double dx, double dy)
        {
            var axis = PlayerHeroFrames.WalkingAxis(dx, dy);
            if (axis.X != 0 || axis.Z != 0) Facing = new BasePoint(axis.X, axis.Z);
        }
        public static bool WithinReach(BattleBoard board,BaseMapObject target,double positionX,double positionY)
            =>CanReach(board,target,positionX,positionY,default);
        public static bool CanReach(BattleBoard board,BaseMapObject target,double positionX,double positionY,BasePoint facing)
        {
            if(board==null||target==null||target.VisualOnly||BaseEnvironment.Decorative(target))return false;
            double closestX=positionX,closestY=positionY,gap=double.PositiveInfinity;
            foreach(var part in target.Parts){part.Closest(positionX,positionY,out double px,out double py,out double distance);if(distance<gap){gap=distance;closestX=px;closestY=py;}}
            bool lord = IsLord(target);
            if (!(gap <= (lord ? LordDistance : Distance) + 1e-9)) return false;
            var anchor=target.Parts[0];
            double targetLevel = string.IsNullOrEmpty(target.Shape)
                ? board.SurfaceLevel(target.X, target.Y) : board.SurfaceLevel(anchor.X, anchor.Y);
            double playerLevel = board.SurfaceLevel(positionX, positionY);
            if (Math.Abs(playerLevel - targetLevel) >= 1e-6 &&
                !(lord && NearTopOfStair(board, positionX, positionY, playerLevel, targetLevel))) return false;
            return target.Id == "farm" || gap <= FacingSlack || InFront(closestX - positionX, closestY - positionY, facing);
        }
        public static bool CanReach(BaseState state,BaseMapObject target)
            => state != null && CanReach(state, target, state.PlayerX, state.PlayerY, Facing);

        public static bool CanReach(BaseState state, BaseMapObject target, double positionX, double positionY, BasePoint facing)
        {
            if(state==null||target==null||target.VisualOnly||BaseSceneCombat.Active(state))return false;
            if (IsCompanion(state, target))
                return target.Distance(positionX, positionY) < BaseMap.InteractionDistance &&
                    InFront(target.X - positionX, target.Y - positionY, facing);
            return CanReach(BaseBattlefield.Board(state),target,positionX,positionY,facing);
        }

        public static double DistanceFor(BaseState state, BaseMapObject target)
            => IsLord(target) ? LordDistance : IsCompanion(state, target) ? BaseMap.InteractionDistance : Distance;

        private static bool IsLord(BaseMapObject target) => target?.Id == "town_quest" && target.Kind == "npc";
        private static bool IsCompanion(BaseState state, BaseMapObject target)
            => target != null && state?.Leaders.Any(person => person.Id == target.Id) == true;

        private static bool InFront(double dx, double dy, BasePoint facing)
        {
            double distance = Math.Sqrt(dx * dx + dy * dy);
            double facingLength = Math.Sqrt(facing.X * facing.X + facing.Y * facing.Y);
            return distance < 1e-6 || facingLength < 1e-6 ||
                (dx * facing.X + dy * facing.Y) / (distance * facingLength) >= FrontCosine;
        }

        private static bool NearTopOfStair(BattleBoard board, double x, double y, double playerLevel, double targetLevel)
        {
            if (playerLevel < targetLevel - .5 || playerLevel > targetLevel) return false;
            var position = new GridPosition((int)Math.Floor(x), (int)Math.Floor(y));
            return board.Cell(position)?.Elevation == targetLevel && board.RampLowEdge(position) != 0;
        }
        public static bool At(BaseState state,string region,string id)=>state.Campaign.Location==region&&CanReach(state,BaseSpatial.Objects(state,region).SingleOrDefault(target=>target.Id==id));
    }
}
