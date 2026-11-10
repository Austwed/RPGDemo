using System;
namespace BaseConquest.Presentation
{
    public enum HeroFacing { South, West, East, North }
    public enum HeroPose { Idle, Walk, Attack, Hurt, Down }
    // Atlas indices are presentation data. They never advance simulation or change unit stats.
    public static class PlayerHeroFrames
    {
        public static HeroFacing Face(double dx,double dz,HeroFacing previous)
        {
            var prior=previous==HeroFacing.East?new BaseConquest.RealtimePrototype.PrototypePoint(1,0):previous==HeroFacing.West?new BaseConquest.RealtimePrototype.PrototypePoint(-1,0):previous==HeroFacing.North?new BaseConquest.RealtimePrototype.PrototypePoint(0,1):new BaseConquest.RealtimePrototype.PrototypePoint(0,-1);
            var axis=BaseConquest.RealtimePrototype.PrototypeUnit.MajorAxis(new BaseConquest.RealtimePrototype.PrototypePoint(dx,dz),prior);
            return axis.X>0?HeroFacing.East:axis.X<0?HeroFacing.West:axis.Z>0?HeroFacing.North:HeroFacing.South;
        }
        // 移动贴图与探索交互共用四向轴。微小浮点误差不能把纯纵向移动误判为侧向。
        internal static (int X, int Z) WalkingAxis(double dx, double dz)
        {
            const double epsilon = 1e-6;
            if (Math.Abs(dx) > epsilon) return (Math.Sign(dx), 0);
            return (0, Math.Abs(dz) > epsilon ? Math.Sign(dz) : 0);
        }

        public static HeroFacing FaceWalking(double dx, double dz, HeroFacing previous)
        {
            var axis = WalkingAxis(dx, dz);
            if (axis.X != 0) return axis.X > 0 ? HeroFacing.East : HeroFacing.West;
            if (axis.Z != 0) return axis.Z > 0 ? HeroFacing.North : HeroFacing.South;
            return previous;
        }

        public static HeroFacing FaceTowards(double dx,double dz,HeroFacing previous)
        {
            return Face(dx,dz,previous);
        }
        public static int Frame(HeroPose pose,HeroFacing facing,double time)
        {
            time=Math.Max(0,time);int row=(int)facing,col;
            switch(pose)
            {
                case HeroPose.Walk:col=2+(int)(time*8)%4;break;
                case HeroPose.Hurt:col=6+Math.Min(1,(int)(time*8));break;
                case HeroPose.Attack:row+=4;col=Math.Min(3,(int)(time*4));break;
                case HeroPose.Down:row+=4;col=4+Math.Min(3,(int)(time*6));break;
                default:col=(int)(time*2)%2;break;
            }
            return row*8+col;
        }
        public static bool IsPlayer(bool hero,bool sideA,int originalSlot,int playerSlot)=>hero&&sideA&&playerSlot>=0&&originalSlot==playerSlot;
    }
}
