using System;
using System.Collections.Generic;
using System.Linq;
namespace BaseConquest.Battle
{
    [Serializable] public sealed class BattleTacticalRules
    {
        public int Version=1,TreeLength=3,RockDistance=4;
        public double TreeHits=2,RockSpeed=4,RockDamage=1.5;
        public BattleTacticalRules Copy()=>(BattleTacticalRules)MemberwiseClone();
        public void Validate()
        {
            bool Number(double n,double max)=>!double.IsNaN(n)&&!double.IsInfinity(n)&&n>0&&n<=max;
            if(Version!=1||TreeLength<1||TreeLength>8||RockDistance<1||RockDistance>16||!Number(TreeHits,20)||!Number(RockSpeed,32)||!Number(RockDamage,100))throw new ArgumentException("战术物件规则快照非法");
        }
    }
    [Serializable] public sealed class BattleTacticalObject
    {
        public string Id,Kind;
        public int X,Y,DirectionX,DirectionY,Steps;
        public double Health=-1;
        public bool Fallen;
        public List<GridPosition> Trunk=new List<GridPosition>();
        public BattleTacticalObject Copy()=>new BattleTacticalObject {Id=Id,Kind=Kind,X=X,Y=Y,DirectionX=DirectionX,DirectionY=DirectionY,Steps=Steps,Health=Health,Fallen=Fallen,Trunk=(Trunk??new List<GridPosition>()).ToList()};
        public static void Validate(List<BattleTacticalObject> values,int width,int height,BattleTacticalRules rules=null)
        {
            if(values==null||values.Count>32||values.Select(o=>o?.Id).Distinct().Count()!=values.Count||values.Any(o=>o==null||string.IsNullOrWhiteSpace(o.Id)||o.Id.Length>80||o.Kind!="tree"&&o.Kind!="rock"&&o.Kind!="dummy"||o.X<0||o.Y<0||o.X>=width||o.Y>=height||double.IsNaN(o.Health)||double.IsInfinity(o.Health)||o.Health< -1||o.Steps<0||o.Steps>(rules?.RockDistance??16)||Math.Abs(o.DirectionX)>1||Math.Abs(o.DirectionY)>1||o.Trunk==null||o.Trunk.Count>(rules?.TreeLength??8)||o.Trunk.Any(p=>p.X<0||p.Z<0||p.X>=width||p.Z>=height)))throw new ArgumentException("战术物件身份、位置或状态非法");
        }
    }
}
