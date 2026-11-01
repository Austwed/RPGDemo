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
            double x=Math.Abs(dx),z=Math.Abs(dz);if(x+z<.00001)return previous;
            // Every diagonal uses its horizontal-facing art, independent of the
            // previous facing or which axis has the larger displacement.
            if(x>.000001)return dx>0 ? HeroFacing.East : HeroFacing.West;
            return dz>0 ? HeroFacing.North : HeroFacing.South;
        }
        // 看向某人（NPC 面向主角等）用主轴四向判定：行走 Face 的"对角线一律用水平"会让
        // 斜下方的观察对象几乎永远左右转头，不会有南北向。这里按分量较大的一轴取四向。
        public static HeroFacing FaceTowards(double dx,double dz,HeroFacing previous)
        {
            double x=Math.Abs(dx),z=Math.Abs(dz);if(x+z<.00001)return previous;
            return x>z ? (dx>0 ? HeroFacing.East : HeroFacing.West) : (dz>0 ? HeroFacing.North : HeroFacing.South);
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
