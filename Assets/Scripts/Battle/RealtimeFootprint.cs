using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.RealtimePrototype
{
    public sealed partial class PrototypeUnit
    {
        // Cell is the leading half. Legacy checkpoints without this field remain one fine cell.
        public int FootprintLength=1;
        public double TurnReadyAt;
        public PrototypePoint TurnTarget;
        private const double AxisHysteresis=.2;
        [NonSerialized] private PrototypePoint bodyAxis;
        public static PrototypePoint MajorAxis(PrototypePoint facing,PrototypePoint previous)
        {
            if(facing.Length<1e-9)return previous.Length>0?previous:new PrototypePoint(1,0);
            var f=facing.Normalized;double x=Math.Abs(f.X),z=Math.Abs(f.Z);
            bool horizontal=previous.Length<1e-9?x>=z:previous.X!=0?z-x<=AxisHysteresis:x-z>AxisHysteresis;
            return horizontal?new PrototypePoint(f.X<0?-1:1,0):new PrototypePoint(0,f.Z<0?-1:1);
        }
        public static PrototypePoint Axis(PrototypePoint facing)=>MajorAxis(facing,default);
        public PrototypePoint BodyAxis {get{if(bodyAxis.Length<1e-9)bodyAxis=Axis(Facing);return bodyAxis;}}
        internal void SetBodyAxis(PrototypePoint value){bodyAxis=value;}
        public PrototypePoint BodyCenter=>Position-BodyAxis*(FootprintLength==2?.25:0);
        public PrototypePoint BodyHalf=>FootprintLength==2?(BodyAxis.X==0?new PrototypePoint(.24,.49):new PrototypePoint(.49,.24)):new PrototypePoint(.2,.2);
        public bool Contains(PrototypePoint point)
        {var d=point-BodyCenter;return Math.Abs(d.X)<=BodyHalf.X&&Math.Abs(d.Z)<=BodyHalf.Z;}
    }
    public sealed partial class RealtimeBattlePrototype
    {
        public IEnumerable<int> Footprint(PrototypeUnit unit,int cell,PrototypePoint facing)
        {
            yield return cell;if(unit.FootprintLength!=2)yield break;
            var a=PrototypeUnit.MajorAxis(facing,unit.BodyAxis);int x=cell%Arena.FineColumns-(int)a.X,z=cell/Arena.FineColumns-(int)a.Z;
            yield return x<0||z<0||x>=Arena.FineColumns||z>=Arena.FineRows?-1:z*Arena.FineColumns+x;
        }
        public bool Fits(PrototypeUnit u,int cell,PrototypePoint facing,bool occupied=true)
        {
            foreach(int c in Footprint(u,cell,facing))if(!Arena.FineWalkable(c)||UnitCellAllowed!=null&&!UnitCellAllowed(u,c)||occupied&&reservations.TryGetValue(c,out int owner)&&owner!=u.Id)return false;
            return true;
        }
        public bool Face(PrototypeUnit u,PrototypePoint facing)
        {
            if(facing.Length<.00001||u.Moving)return false;
            if(u.FootprintLength!=2){u.Facing=facing;u.SetBodyAxis(PrototypeUnit.Axis(facing));return true;}
            if(!Fits(u,u.Cell,facing))return false;
            facing=facing.Normalized;
            var old=u.BodyAxis;var next=PrototypeUnit.MajorAxis(facing,old);
            double dot=Math.Max(-1,Math.Min(1,PrototypePoint.Dot(old,next)));
            double angle=Math.Acos(dot);
            if(angle>1e-6)
            {
                if(u.TurnReadyAt<=0||PrototypePoint.Dot(u.TurnTarget.Normalized,next)<.999999)
                {u.TurnTarget=next;u.TurnReadyAt=Elapsed+angle/Math.PI;u.CarrySeconds=0;return false;}
                if(Elapsed+1e-9<u.TurnReadyAt)return false;
            }
            if(old.X!=next.X&&old.Z!=next.Z)
            {
                int corner=Arena.CellAt(Arena.Center(u.Cell)-(old+next)*.5);
                if(!Arena.FineWalkable(corner)||UnitCellAllowed!=null&&!UnitCellAllowed(u,corner)||reservations.TryGetValue(corner,out int owner)&&owner!=u.Id)return false;
            }
            var prior=u.Facing;u.Facing=facing;u.SetBodyAxis(next);
            if(Units.Any(other=>other!=u&&other.Alive&&BodiesCross(u,u.Position,other,other.Moving?Arena.Center(other.NextCell):other.Position)))
            {u.Facing=prior;u.SetBodyAxis(old);return false;}
            u.TurnReadyAt=0;RebuildReservations();return true;
        }
        public bool SetFootprint(PrototypeUnit u,int length)
        {
            if(length!=1&&length!=2)return false;int old=u.FootprintLength;u.FootprintLength=length;
            if(!Fits(u,u.Cell,u.Facing)){u.FootprintLength=old;return false;}
            RebuildReservations();return true;
        }
        private bool BodyStep(PrototypeUnit u,int destination,PrototypePoint facing)
        {
            if(!Fits(u,u.Cell,facing)||!Fits(u,destination,facing))return false;
            var from=Footprint(u,u.Cell,facing).ToArray();var to=Footprint(u,destination,facing).ToArray();
            for(int n=0;n<from.Length;n++)if(!Arena.CanStep(from[n],to[n]))return false;
            return true;
        }
        public static bool BodiesCross(PrototypeUnit a,PrototypePoint toA,PrototypeUnit b,PrototypePoint toB)
        {
            if(a.FootprintLength!=2&&b.FootprintLength!=2)return SegmentGap(a.Position,toA,b.Position,toB)<a.Radius+b.Radius-1e-6;
            var da=toA-a.Position;var db=toB-b.Position;
            // Separating axes of the two swept rectangles; reject geometric crossing even
            // when transit speeds differ, matching the existing conservative reservations.
            foreach(var axis in new[]{new PrototypePoint(1,0),new PrototypePoint(0,1),new PrototypePoint(-da.Z,da.X),new PrototypePoint(-db.Z,db.X)})
            {
                if(axis.Length<1e-9)continue;
                double a0=PrototypePoint.Dot(a.BodyCenter,axis),a1=PrototypePoint.Dot(a.BodyCenter+da,axis),b0=PrototypePoint.Dot(b.BodyCenter,axis),b1=PrototypePoint.Dot(b.BodyCenter+db,axis);
                double ra=Math.Abs(axis.X)*a.BodyHalf.X+Math.Abs(axis.Z)*a.BodyHalf.Z,rb=Math.Abs(axis.X)*b.BodyHalf.X+Math.Abs(axis.Z)*b.BodyHalf.Z;
                if(Math.Max(a0,a1)+ra<=Math.Min(b0,b1)-rb+1e-6||Math.Max(b0,b1)+rb<=Math.Min(a0,a1)-ra+1e-6)return false;
            }
            return true;
        }
        public static double BodyGap(PrototypeUnit a,PrototypeUnit b)=>BodyGapAt(a,b,a.Position,a.BodyAxis);
        public static double BodyGapAt(PrototypeUnit a,PrototypeUnit b,PrototypePoint position,PrototypePoint axis)
        {
            if(a.FootprintLength!=2&&b.FootprintLength!=2)return (b.Position-position).Length;
            var center=position-axis*(a.FootprintLength==2?.25:0);
            var actorHalf=a.FootprintLength==2?(axis.X==0?new PrototypePoint(.24,.49):new PrototypePoint(.49,.24)):new PrototypePoint(.2,.2);
            var d=center-b.BodyCenter;var half=actorHalf+b.BodyHalf;
            return new PrototypePoint(Math.Max(0,Math.Abs(d.X)-half.X),Math.Max(0,Math.Abs(d.Z)-half.Z)).Length;
        }
        public static bool OccupiesCoarse(PrototypeUnit u,int x,int z,PrototypeArena arena,bool includeTransit=true)
        {
            bool Overlap(PrototypePoint center)=>center.X+u.BodyHalf.X>x&&center.X-u.BodyHalf.X<x+1&&center.Z+u.BodyHalf.Z>z&&center.Z-u.BodyHalf.Z<z+1;
            return Overlap(u.BodyCenter)||includeTransit&&u.Moving&&Overlap(u.BodyCenter+arena.Center(u.NextCell)-u.Position);
        }
        private static double BodyArrow(PrototypePoint from,PrototypePoint to,PrototypeUnit u)
        {
            if(u.FootprintLength!=2)return CircleFraction(from,to,u.Position,u.Radius+.06);
            double enter=0,leave=1;var center=u.BodyCenter;var half=u.BodyHalf+new PrototypePoint(.06,.06);var delta=to-from;
            bool Axis(double p,double d,double r)
            {if(Math.Abs(d)<1e-9)return Math.Abs(p)<=r;double a=(-r-p)/d,b=(r-p)/d;enter=Math.Max(enter,Math.Min(a,b));leave=Math.Min(leave,Math.Max(a,b));return enter<=leave;}
            return Axis(from.X-center.X,delta.X,half.X)&&Axis(from.Z-center.Z,delta.Z,half.Z)?enter:double.PositiveInfinity;
        }
    }
}
