using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.RealtimePrototype
{
    public enum PrototypeSide { Ally, Enemy }
    public enum PrototypeRole { Leader, Melee, Archer }
    public enum PrototypeOrder { Auto, Move, Guard, Focus, Retreat }
    public enum PrototypeLayout { Open, Choke, Cover }
    public enum PrototypeOutcome { Fighting, Victory, Defeat }

    [Serializable] public struct PrototypePoint
    {
        public double X, Z;
        public PrototypePoint(double positionX, double positionZ) { X=positionX; Z=positionZ; }
        public double Length => Math.Sqrt(X*X+Z*Z);
        public PrototypePoint Normalized => Length<.000001 ? new PrototypePoint() : this*(1/Length);
        public static PrototypePoint operator +(PrototypePoint first,PrototypePoint second)=>new PrototypePoint(first.X+second.X,first.Z+second.Z);
        public static PrototypePoint operator -(PrototypePoint first,PrototypePoint second)=>new PrototypePoint(first.X-second.X,first.Z-second.Z);
        public static PrototypePoint operator *(PrototypePoint point,double factor)=>new PrototypePoint(point.X*factor,point.Z*factor);
        public static double Dot(PrototypePoint first,PrototypePoint second)=>first.X*second.X+first.Z*second.Z;
    }

    [Serializable] public sealed partial class PrototypeUnit
    {
        public int Id,Squad,Cell,NextCell=-1,PreviousCell=-1,TargetId=-1,AttackSlot=-1,MoveGoal=-1;
        public PrototypeSide Side;
        public PrototypeRole Role;
        public bool Player,HitResolved;
        public PrototypePoint Position,Facing,Goal,Anchor,AttackFacing;
        public PrototypeOrder Order;
        public double Health,MaxHealth,ReadyAt,HitAt=-1,SwingUntil,FlashUntil,Progress,BlockedFor,NextThinkAt,DistanceTravelled,CarrySeconds;
        public double Speed=2.35,AttackDamage=-1,AttackRange=-1,AttackInterval=-1;
        public bool Escaped,UsesRangedWeapon;
        public bool Ranged=>Role==PrototypeRole.Archer||UsesRangedWeapon;
        public double MoveSpeed {get=>Speed;set{if(!PrototypeArena.Finite(value)||value<=0||value>12)throw new ArgumentOutOfRangeException(nameof(value));Speed=value;}}
        public bool KnockedDown;
        public bool Alive=>Health>0&&!KnockedDown&&!Escaped;
        public bool Moving=>NextCell>=0;
        public double Radius=>.2;
        public double Reach=>AttackRange>=0 ? AttackRange : Role==PrototypeRole.Archer ? 6.2 : 1.05;
        public double Damage=>AttackDamage>=0 ? AttackDamage : Player ? 24 : Role==PrototypeRole.Leader ? 14 : Role==PrototypeRole.Archer ? 9 : 10;
        public PrototypeUnit Copy(){var copy=(PrototypeUnit)MemberwiseClone();copy.Steering=false;return copy;}
    }

    [Serializable] public sealed class PrototypeArrow
    {
        public bool VisualOnly;public double Speed=7;
        public int Id, SourceId;
        public PrototypeSide Side;
        public PrototypePoint Position, Direction;
        public double Damage, Remaining=8;
        public PrototypeArrow Copy()=>(PrototypeArrow)MemberwiseClone();
    }

    public sealed class PrototypeArena
    {
        public const int Width=24,Height=14,Subdivision=2,FineWidth=Width*Subdivision,FineHeight=Height*Subdivision;
        public int Columns {get;private set;}=Width;
        public int Rows {get;private set;}=Height;
        public int FineColumns=>Columns*Subdivision;
        public int FineRows=>Rows*Subdivision;
        public int NavigationRevision {get;private set;}
        public const double CellSize=.5;
        private readonly bool[] walls=new bool[Width*Height];
        private readonly bool[] shotWalls;
        private readonly Func<int,int,bool> crossing;
        private readonly ushort[] connections;
        private Func<int,double> travelCost;
        public Func<int,double> TravelCost {get=>travelCost;set{travelCost=value;paths.Clear();}}
        private readonly Dictionary<int,double[]> paths=new Dictionary<int,double[]>();
        private readonly List<PathEntry> frontier=new List<PathEntry>();
        private static readonly int[] offsetsX={1,0,-1,0,1,-1,-1,1},offsetsZ={0,1,0,-1,1,1,-1,-1};
        public PrototypeLayout Layout { get; }
        public PrototypeArena(PrototypeLayout layout)
        {
            if(!Enum.IsDefined(typeof(PrototypeLayout),layout))throw new ArgumentException("未知测试地形");
            Layout=layout;
            for(int positionZ=0;positionZ<Rows;positionZ++)for(int positionX=0;positionX<Columns;positionX++)
                walls[positionZ*Columns+positionX]=positionX==0||positionZ==0||positionX==Columns-1||positionZ==Rows-1||
                    layout==PrototypeLayout.Choke&&(positionX==11||positionX==12)&&positionZ!=6&&positionZ!=7||
                    layout==PrototypeLayout.Cover&&((positionX==10||positionX==11)&&(positionZ>=3&&positionZ<=5)||
                    (positionX==12||positionX==13)&&(positionZ>=8&&positionZ<=10));
            shotWalls=walls;
            connections=BuildConnections();
        }
        public PrototypeArena(bool[] blocked,bool[] shots,Func<int,int,bool> canCross=null,int width=Width,int height=Height)
        {
            Columns=width;Rows=height;
            if(width<6||width>64||height<6||height>64||blocked?.Length!=Columns*Rows||shots?.Length!=Columns*Rows)throw new ArgumentException("即时战场尺寸／完整地格非法");
            walls=blocked.ToArray();shotWalls=shots.ToArray();crossing=canCross;connections=BuildConnections();
        }
        public void RefreshObstacles(bool[] movement,bool[] shots)
        {
            if(movement.Length!=walls.Length||shots.Length!=shotWalls.Length)throw new ArgumentException("阻挡尺寸不一致");
            Array.Copy(movement,walls,walls.Length);Array.Copy(shots,shotWalls,shotWalls.Length);Array.Copy(BuildConnections(),connections,connections.Length);paths.Clear();NavigationRevision++;
        }
        public bool Wall(int positionX,int positionZ)=>positionX<0||positionZ<0||positionX>=Columns||positionZ>=Rows||walls[positionZ*Columns+positionX];
        public bool CanStand(PrototypePoint point,double radius=.2)
        {
            if(!Finite(point.X)||!Finite(point.Z)||!Finite(radius)||radius<0||radius>.5||point.X<radius||point.Z<radius||point.X>Columns-radius||point.Z>Rows-radius)return false;
            for(int positionZ=(int)Math.Floor(point.Z-radius);positionZ<=(int)Math.Floor(point.Z+radius);positionZ++)
                for(int positionX=(int)Math.Floor(point.X-radius);positionX<=(int)Math.Floor(point.X+radius);positionX++)
                    if(Wall(positionX,positionZ))
                    {
                        double nearestX=Math.Max(positionX,Math.Min(positionX+1,point.X)), nearestZ=Math.Max(positionZ,Math.Min(positionZ+1,point.Z));
                        if((point.X-nearestX)*(point.X-nearestX)+(point.Z-nearestZ)*(point.Z-nearestZ)<radius*radius+.00000001)return false;
                    }
            return true;
        }
        public double WallFraction(PrototypePoint from,PrototypePoint to,double radius=0)
        {
            double nearest=double.PositiveInfinity;
            for(int positionZ=Math.Max(0,(int)Math.Floor(Math.Min(from.Z,to.Z)-radius));positionZ<=Math.Min(Rows-1,(int)Math.Floor(Math.Max(from.Z,to.Z)+radius));positionZ++)
                for(int positionX=Math.Max(0,(int)Math.Floor(Math.Min(from.X,to.X)-radius));positionX<=Math.Min(Columns-1,(int)Math.Floor(Math.Max(from.X,to.X)+radius));positionX++)
                    if(shotWalls[positionZ*Columns+positionX])
                    {
                        double enter=0,leave=1;
                        if(Slab(from.X,to.X-from.X,positionX-radius,positionX+1+radius,ref enter,ref leave)&&
                            Slab(from.Z,to.Z-from.Z,positionZ-radius,positionZ+1+radius,ref enter,ref leave))nearest=Math.Min(nearest,enter);
                    }
            return nearest;
        }
        private static bool Slab(double origin,double delta,double minimum,double maximum,ref double enter,ref double leave)
        {
            if(Math.Abs(delta)<.00000001)return origin>=minimum&&origin<=maximum;
            double first=(minimum-origin)/delta,second=(maximum-origin)/delta;
            enter=Math.Max(enter,Math.Min(first,second));leave=Math.Min(leave,Math.Max(first,second));return enter<=leave;
        }
        private struct PathEntry {public int Cell;public double Cost;public PathEntry(int cell,double cost){Cell=cell;Cost=cost;}}
        public int CellAt(PrototypePoint point)=>!Finite(point.X)||!Finite(point.Z)||point.X<0||point.Z<0||point.X>=Columns||point.Z>=Rows ? -1 : (int)(point.Z*Subdivision)*FineColumns+(int)(point.X*Subdivision);
        public PrototypePoint Center(int cell)=>new PrototypePoint((cell%FineColumns+.5)*CellSize,(cell/FineColumns+.5)*CellSize);
        public bool FineWalkable(int cell)=>cell>=0&&cell<FineColumns*FineRows&&!Wall(cell%FineColumns/Subdivision,cell/FineColumns/Subdivision);
        public bool CanStep(int from,int to)
        {
            if(from<0||to<0||from>=connections.Length||to>=connections.Length)return false;
            int deltaX=to%FineColumns-from%FineColumns,deltaZ=to/FineColumns-from/FineColumns;
            return Math.Abs(deltaX)<=1&&Math.Abs(deltaZ)<=1&&(connections[from]&(1<<((deltaZ+1)*3+deltaX+1)))!=0;
        }
        private ushort[] BuildConnections()
        {
            var result=new ushort[FineColumns*FineRows];
            for(int cell=0;cell<result.Length;cell++)for(int direction=0;direction<8;direction++)
            {
                int nextX=cell%FineColumns+offsetsX[direction],nextZ=cell/FineColumns+offsetsZ[direction];
                if(nextX<0||nextX>=FineColumns||nextZ<0||nextZ>=FineRows)continue;
                if(CanCross(cell,nextZ*FineColumns+nextX))result[cell]|=(ushort)(1<<((offsetsZ[direction]+1)*3+offsetsX[direction]+1));
            }
            return result;
        }
        private bool CanCross(int from,int to)
        {
            if(!FineWalkable(from)||!FineWalkable(to)||from==to)return false;
            int deltaX=to%FineColumns-from%FineColumns,deltaZ=to/FineColumns-from/FineColumns;
            if(Math.Abs(deltaX)>1||Math.Abs(deltaZ)>1)return false;
            if(crossing!=null&&!crossing(from,to))return false;
            return deltaX==0||deltaZ==0||FineWalkable(from+deltaX)&&FineWalkable(from+deltaZ*FineColumns)&&
                (crossing==null||crossing(from,from+deltaX)&&crossing(from+deltaX,to)&&crossing(from,from+deltaZ*FineColumns)&&crossing(from+deltaZ*FineColumns,to));
        }
        public IEnumerable<int> Neighbours(int cell)
        {
            for(int direction=0;direction<8;direction++)
            {
                int nextX=cell%FineColumns+offsetsX[direction],nextZ=cell/FineColumns+offsetsZ[direction];
                if(nextX<0||nextX>=FineColumns||nextZ<0||nextZ>=FineRows)continue;
                int next=nextZ*FineColumns+nextX;if(CanStep(cell,next))yield return next;
            }
        }
        public double StepCost(int from,int to)=>(Center(to)-Center(from)).Length*Math.Max(1,TravelCost?.Invoke(to)??1);
        public double[] Distances(int goal,bool fromStart=false)
        {
            int key=goal*2+(fromStart?1:0);
            if(paths.TryGetValue(key,out var cached))return cached;
            var distances=Enumerable.Repeat(double.PositiveInfinity,FineColumns*FineRows).ToArray();
            if(!FineWalkable(goal))return distances;
            frontier.Clear();distances[goal]=0;PushPath(new PathEntry(goal,0));
            while(frontier.Count>0)
            {
                var entry=PopPath();if(entry.Cost>distances[entry.Cell])continue;
                foreach(int next in Neighbours(entry.Cell))
                {
                    if(!fromStart&&!CanStep(next,entry.Cell))continue;
                    double cost=entry.Cost+(fromStart?StepCost(entry.Cell,next):StepCost(next,entry.Cell));
                    if(cost+1e-9>=distances[next])continue;
                    distances[next]=cost;PushPath(new PathEntry(next,cost));
                }
            }
            // Moving targets can visit thousands of cells. Bound the shared static distance cache.
            if(paths.Count>=128)paths.Clear();paths.Add(key,distances);return distances;
        }
        private static int ComparePath(PathEntry first,PathEntry second)
        {int order=first.Cost.CompareTo(second.Cost);return order!=0?order:first.Cell.CompareTo(second.Cell);}
        private void PushPath(PathEntry entry)
        {
            int index=frontier.Count;frontier.Add(entry);
            while(index>0){int parent=(index-1)/2;if(ComparePath(frontier[parent],entry)<=0)break;frontier[index]=frontier[parent];index=parent;}
            frontier[index]=entry;
        }
        private PathEntry PopPath()
        {
            var result=frontier[0];int last=frontier.Count-1;var entry=frontier[last];frontier.RemoveAt(last);
            if(last==0)return result;
            int index=0;
            while(index*2+1<last)
            {
                int child=index*2+1;if(child+1<last&&ComparePath(frontier[child+1],frontier[child])<0)child++;
                if(ComparePath(entry,frontier[child])<=0)break;frontier[index]=frontier[child];index=child;
            }
            frontier[index]=entry;return result;
        }
        public static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
    }

    public sealed partial class RealtimeBattlePrototype
    {
        public const double FixedSeconds=1.0/60;
        public const double GuardTargetRadius=3,GuardMoveRadius=3.25;
        public readonly PrototypeArena Arena;
        public readonly List<PrototypeUnit> Units=new List<PrototypeUnit>();
        public readonly List<PrototypeArrow> Arrows=new List<PrototypeArrow>();
        public Func<PrototypeUnit,PrototypeUnit,double> TargetWeight;
        public bool AutonomousOutcome=true;
        public Func<PrototypeUnit,PrototypeUnit,double,double> DamageResolver;
        public Action<PrototypeUnit,PrototypeUnit,double> DamageApplied;
        public Action<PrototypeUnit,int> CellEntered;
        public Func<PrototypeUnit,PrototypeUnit,bool> CanTarget;
        public Func<PrototypeUnit,bool> CanMove;
        public Func<PrototypeUnit,double> AttackPower,MovementSpeed;
        public Func<PrototypeUnit,int,double> MovementCost;
        public Func<PrototypeUnit,int,bool> UnitCellAllowed;
        public Action AfterStep;
        public double DamageFor(PrototypeUnit unit)=>AttackPower?.Invoke(unit)??unit.Damage;
        public Func<PrototypeUnit,int> DefaultDestination;
        private readonly Dictionary<int,int> reservations=new Dictionary<int,int>();
        public IReadOnlyDictionary<int,int> Reservations=>reservations;
        public double Elapsed {get;private set;}
        public bool Paused {get;private set;}=true;
        public PrototypeOutcome Outcome {get;private set;}
        public int Hits {get;private set;}
        public int Shots {get;private set;}
        public int CompletedSteps {get;private set;}
        public int YieldMoves {get;private set;}
        public PrototypeUnit Player=>Units.FirstOrDefault(unit=>unit.Player);
        private double remainder;
        private int nextUnitId=1,nextArrowId=1,simulationStep;
        public RealtimeBattlePrototype(PrototypeArena arena){Arena=arena??throw new ArgumentNullException(nameof(arena));}
        public static RealtimeBattlePrototype Create(PrototypeLayout layout,int total=40)
        {
            if(total!=30&&total!=40&&total!=50)throw new ArgumentException("验证人数只允许30／40／50");
            var battle=new RealtimeBattlePrototype(new PrototypeArena(layout));
            foreach(PrototypeSide side in Enum.GetValues(typeof(PrototypeSide)))for(int index=0;index<total/2;index++)
            {
                double positionX=side==PrototypeSide.Ally ? 2.5+index/7*.9 : 21.5-index/7*.9;
                var point=new PrototypePoint(positionX,1.5+index%7*1.65);
                battle.AddUnit(side,index<3 ? PrototypeRole.Leader : index%4==0 ? PrototypeRole.Archer : PrototypeRole.Melee,point,index%3,side==PrototypeSide.Ally&&index==0);
            }
            return battle;
        }
        public PrototypeUnit AddUnit(PrototypeSide side,PrototypeRole role,PrototypePoint point,int squad=0,bool player=false,int identity=-1)
        {
            int cell=Arena.CellAt(point);
            if(!Enum.IsDefined(typeof(PrototypeSide),side)||!Enum.IsDefined(typeof(PrototypeRole),role)||squad<0||squad>2||!Arena.CanStand(point)||!Arena.FineWalkable(cell)||
                reservations.ContainsKey(cell)||identity>=0&&Units.Any(unit=>unit.Id==identity)||player&&(side!=PrototypeSide.Ally||Player!=null))throw new ArgumentException("测试单位无效或占位重复");
            var position=Arena.Center(cell);
            int id=identity>=0 ? identity : nextUnitId;nextUnitId=Math.Max(nextUnitId,id+1);
            var unit=new PrototypeUnit {Id=id,Side=side,Role=role,Player=player,Squad=squad,Cell=cell,Position=position,Goal=position,Anchor=position,
                Facing=new PrototypePoint(side==PrototypeSide.Ally ? 1 : -1,0),Health=player ? 220 : role==PrototypeRole.Leader ? 120 : 65,
                MoveSpeed=role==PrototypeRole.Leader ? 2.6 : role==PrototypeRole.Archer ? 2.1 : 2.35};
            unit.MaxHealth=unit.Health;Units.Add(unit);RebuildReservations();return unit;
        }
        public void SetPaused(bool paused){Paused=paused;if(paused&&remainder>=FixedSeconds)remainder%=FixedSeconds;}
        // Draw committed transit and any active detour before falling back to the weighted fine-grid field.
        public void PreviewPath(PrototypeUnit unit,List<PrototypePoint> points)
        {
            points.Clear();
            if(unit==null||!unit.Alive||Outcome!=PrototypeOutcome.Fighting||!(CanMove?.Invoke(unit)??true))return;
            if(unit.Role==PrototypeRole.Leader&&unit.Strategy==PrototypeStrategy.Obey&&!unit.ManualOrder&&unit.TargetId<0&&!unit.Moving)return;
            int cell=unit.Moving?unit.NextCell:unit.Cell;
            if(!Arena.FineWalkable(cell))return;
            points.Add(unit.Position);
            if(unit.Moving)points.Add(Arena.Center(cell));
            if(unit.HitAt>=0&&!unit.HitResolved)return;
            int goal=unit.MoveGoal;
            if(unit.NextThinkAt==0||goal<0)
            {
                if(unit.Order==PrototypeOrder.Move||unit.Order==PrototypeOrder.Guard||unit.Order==PrototypeOrder.Retreat)goal=Arena.CellAt(unit.Goal);
                else if(unit.Order==PrototypeOrder.Focus)
                {
                    var target=Units.FirstOrDefault(actor=>actor.Id==unit.TargetId&&actor.Alive&&(CanTarget?.Invoke(unit,actor)??true));
                    goal=target==null?-1:AttackPosition(unit,target);
                }
            }
            if(!Arena.FineWalkable(goal)||goal==cell)return;
            if(PreviewDetour(unit,cell,goal,points))return;
            if(unit.FootprintLength==2)
            {
                var arrival = unit.GroupArrivalAxis;
                Func<PrototypePoint,bool> acceptArrival = arrival.Length > .5 &&
                    (unit.Order==PrototypeOrder.Move||unit.Order==PrototypeOrder.Guard)&&Arena.CellAt(unit.Goal)==goal
                    ? axis => axis.Equals(arrival) : null;
                foreach(int step in PlanFootprint(unit,cell,goal,acceptArrival:acceptArrival))points.Add(Arena.Center(step));
                return;
            }
            var distances=Arena.Distances(goal);
            if(double.IsPositiveInfinity(distances[cell]))return;
            for(int count=0;cell!=goal&&count<Arena.FineColumns*Arena.FineRows;count++)
            {
                int next=-1;double best=double.PositiveInfinity;
                foreach(int candidate in Arena.Neighbours(cell))
                {
                    if(distances[candidate]>=distances[cell]-1e-9)continue;
                    double score=distances[candidate]+Arena.StepCost(cell,candidate);
                    if(score<best){next=candidate;best=score;}
                }
                if(next<0)break;
                points.Add(Arena.Center(next));cell=next;
            }
        }
        public bool IssueOrder(int squad,int commanderId,PrototypeOrder order,PrototypePoint goal,int targetId=-1,IReadOnlyCollection<int> squads=null,bool leadersOnly=false,PrototypeSide side=PrototypeSide.Ally)
        {
            if(Outcome!=PrototypeOutcome.Fighting||!Enum.IsDefined(typeof(PrototypeOrder),order)||squad< -1||squad>2||!Arena.CanStand(goal))return false;
            var target=Units.FirstOrDefault(unit=>unit.Id==targetId&&unit.Alive&&unit.Side!=side);
            if(order==PrototypeOrder.Focus&&target==null)return false;
            var selected=Units.Where(unit=>unit.Alive&&unit.Side==side&&(squads!=null ? squads.Contains(unit.Squad)&&(!leadersOnly||unit.Role==PrototypeRole.Leader) : commanderId>=0 ? unit.Id==commanderId&&unit.Role==PrototypeRole.Leader : squad<0||unit.Squad==squad)).OrderBy(unit=>unit.Role==PrototypeRole.Leader ? 0 : 1).ThenBy(unit=>unit.Id).ToArray();
            if(selected.Length==0)return false;
            var slots = new Dictionary<int, int>();
            var groupRoutes = new Dictionary<int, BodyRoute>();
            bool groupPlacement = leadersOnly && selected.Length > 1 && (order == PrototypeOrder.Move || order == PrototypeOrder.Guard);
            if(order==PrototypeOrder.Move||order==PrototypeOrder.Guard)
            {
                int anchor=Arena.CellAt(goal);var candidates=Enumerable.Range(0,Arena.FineColumns*Arena.FineRows)
                    .Where(cell=>Arena.FineWalkable(cell)&&(Arena.Center(cell)-goal).Length<=4&&
                        Math.Abs(cell%Arena.FineColumns-anchor%Arena.FineColumns)%2==0&&Math.Abs(cell/Arena.FineColumns-anchor/Arena.FineColumns)%2==0).ToArray();
                if (groupPlacement)
                {
                    if (!TryCommanderDestinations(selected, candidates, goal, slots, groupRoutes)) return false;
                }
                else foreach(var unit in selected)
                {
                    var distances=Arena.Distances(unit.Cell,true);int best=-1;double score=double.PositiveInfinity;
                    foreach(int cell in candidates)
                    {
                        if(slots.ContainsValue(cell)||double.IsPositiveInfinity(distances[cell])||Units.Any(other=>other.Alive&&!selected.Contains(other)&&(other.Cell==cell||other.NextCell==cell)))continue;
                        double candidate=(Arena.Center(cell)-Arena.Center(anchor)).Length*4+distances[cell]*.12;
                        if(candidate<score){score=candidate;best=cell;}
                    }
                    if(best<0)return false;slots.Add(unit.Id,best);
                }
            }
            foreach(var unit in selected)
            {
                bodyRoutes.Remove(unit.Id);
                unit.GroupArrivalAxis = default;
                if(AttackTriggered!=null)CancelWindup(unit);
                unit.Order=order;unit.Anchor=Arena.Center(Arena.CellAt(goal));unit.Goal=slots.TryGetValue(unit.Id,out int slot) ? Arena.Center(slot) : unit.Position;
                if (groupPlacement)
                {
                    unit.Anchor = unit.Goal;
                    if (groupRoutes.TryGetValue(unit.Id, out var route))
                    {
                        bodyRoutes[unit.Id] = route;
                        if(unit.FootprintLength==2)unit.GroupArrivalAxis = route.ArrivalAxis;
                    }
                }
                unit.TargetId=order==PrototypeOrder.Focus ? target.Id : -1;unit.AttackSlot=-1;unit.NextThinkAt=0;unit.BlockedFor=0;
                if(order==PrototypeOrder.Retreat)unit.Goal=unit.Anchor;
            }
            return true;
        }

        private bool TryCommanderDestinations(PrototypeUnit[] commanders, int[] candidates, PrototypePoint goal,
            Dictionary<int, int> slots, Dictionary<int, BodyRoute> routes)
        {
            var occupied = new HashSet<int>();
            foreach (var unit in Units.Where(unit => unit.Alive && !commanders.Contains(unit)))
            {
                occupied.UnionWith(Footprint(unit, unit.Cell, unit.BodyAxis));
                if (unit.Moving) occupied.UnionWith(Footprint(unit, unit.NextCell, unit.BodyAxis));
            }

            var rings = candidates.GroupBy(cell => (Arena.Center(cell) - goal).Length).OrderBy(ring => ring.Key).ToArray();
            foreach (var commander in commanders.OrderBy(unit => unit.Squad))
            {
                if (!(CanMove?.Invoke(commander) ?? true)) return false;
                int start = commander.Moving ? commander.NextCell : commander.Cell;
                var distances = Arena.Distances(start, true);
                int chosen = -1;
                int[] chosenFootprint = null;
                List<int> chosenSteps = null;
                PrototypePoint chosenAxis = default;
                foreach (var ring in rings)
                {
                    double bestCost = double.PositiveInfinity;
                    foreach (int cell in ring)
                    {
                        if (occupied.Contains(cell) || double.IsPositiveInfinity(distances[cell])) continue;
                        if (!TryCommanderLanding(commander, start, cell, occupied, distances[cell], out var footprint, out var steps,
                            out var axis, out double cost)) continue;
                        if (cost >= bestCost) continue;
                        chosen = cell;
                        chosenFootprint = footprint;
                        chosenSteps = steps;
                        chosenAxis = axis;
                        bestCost = cost;
                    }
                    if (chosen >= 0) break;
                }
                if (chosen < 0) return false;
                occupied.UnionWith(chosenFootprint);
                slots.Add(commander.Id, chosen);
                if (chosenSteps != null)
                    routes.Add(commander.Id, new BodyRoute { Goal = chosen, Cell = start, Revision = Arena.NavigationRevision,
                        Steps = chosenSteps, ArrivalAxis = chosenAxis });
            }
            return true;
        }

        private bool TryCommanderLanding(PrototypeUnit commander, int start, int cell, HashSet<int> occupied, double distance,
            out int[] footprint, out List<int> steps, out PrototypePoint axis, out double cost)
        {
            int[] arrivalCells = null;
            PrototypePoint arrivalAxis = default;
            bool AcceptArrival(PrototypePoint facing)
            {
                if (!Fits(commander, cell, facing, false)) return false;
                var cells = Footprint(commander, cell, facing).ToArray();
                if (cells.Any(occupied.Contains)) return false;
                arrivalCells = cells;
                arrivalAxis = facing;
                return true;
            }

            cost = distance;
            steps = null;
            if ((commander.FootprintLength == 2 || UnitCellAllowed != null) && start != cell)
            {
                steps = PlanFootprint(commander, start, cell, acceptArrival: AcceptArrival);
                cost = 0;
                int previous = start;
                foreach (int step in steps)
                {
                    cost += Arena.StepCost(previous, step);
                    previous = step;
                }
            }
            else AcceptArrival(commander.BodyAxis);
            footprint = arrivalCells;
            axis = arrivalAxis;
            return footprint != null;
        }
        public void Tick(double seconds)
        {
            if(!PrototypeArena.Finite(seconds)||seconds<0||seconds>.25)throw new ArgumentException("帧间隔必须有限且在0～0.25秒内");
            if(Paused||Outcome!=PrototypeOutcome.Fighting)return;
            remainder+=seconds;
            while(remainder+1e-9>=FixedSeconds&&Outcome==PrototypeOutcome.Fighting&&!Paused){remainder-=FixedSeconds;Step();}
            // A terminal callback can end a frame before its remaining fixed steps are simulated.
            if(Outcome!=PrototypeOutcome.Fighting&&remainder>=FixedSeconds)remainder%=FixedSeconds;
        }
        private void RebuildReservations()
        {
            reservations.Clear();foreach(var unit in Units.Where(actor=>actor.Alive))
            foreach(int cell in Footprint(unit,unit.Cell,unit.Facing).Concat(unit.Moving?Footprint(unit,unit.NextCell,unit.Facing):Array.Empty<int>()).Distinct())
            {if(reservations.TryGetValue(cell,out int owner)&&owner!=unit.Id)throw new InvalidOperationException("即时体积预约重叠");reservations[cell]=unit.Id;}
        }
        private void Step()
        {
            Elapsed+=FixedSeconds;simulationStep++;
            ValidateWindups();
            foreach(var unit in Units.Where(actor=>!actor.Moving))unit.CarrySeconds=0;
            foreach(var unit in Units.Where(actor=>actor.Alive&&actor.Moving&&!actor.SteeringStopped))
                AdvanceTransit(unit,FixedSeconds);
            ValidateWindups();
            StartMovingMeleeWindups();
            RebuildReservations();
            if (SwordContactPinEnabled) RefreshSwordContactPins();
            foreach(var unit in Units.Where(actor=>actor.Alive&&!actor.Moving))Think(unit);
            if (SwordContactPinEnabled)
            {
                RefreshSwordContactPins();
#if UNITY_EDITOR
                SampleSwordContactHolds();
#endif
            }
            // 起步顺序：骑兵先挑格子。给冲锋中的骑兵优先选择通路，
            // 让它先手等价于给它的寻路优先级，其余单位只会从剩下的格子里挑，不会因此卡死。
            foreach(var unit in Units.Where(actor=>actor.Alive&&!actor.Moving&&actor.MoveGoal>=0)
                .OrderBy(actor=>CavalryPathPriority&&IsCavalry(actor)?0:1).ThenByDescending(actor=>actor.BlockedFor).ThenBy(actor=>actor.Id).ToArray())
            {
                if(unit.Moving)continue;
                if (ContactPinActive(unit)) { unit.BlockedFor = 0; continue; }
                var distances=Arena.Distances(unit.MoveGoal);
                var heading=(Arena.Center(unit.MoveGoal)-unit.Position).Normalized;
                var groupRoute=GroupRoute(unit);
                int recovered=RecoverBlockedRoute(unit,groupRoute,distances[unit.Cell]);
                int preferred=recovered;
                if(preferred<0&&unit.BlockedFor<.6)preferred=GroupRouteNext(unit,groupRoute);
                if(preferred<0)preferred=DetourNext(unit);
                var candidates=Arena.Neighbours(unit.Cell).Where(cell=>!double.IsPositiveInfinity(distances[cell])&&GroupArrivalAllowed(unit,groupRoute,cell))
                    .OrderBy(cell=>cell==preferred?-10000:distances[cell]+Arena.StepCost(unit.Cell,cell)+(reservations.TryGetValue(cell,out int owner)&&owner!=unit.Id ? 3 : 0)+(cell==unit.PreviousCell ? .5 : 0)+Math.Max(0,Cross(heading,(Arena.Center(cell)-unit.Position).Normalized))).ToArray();
                bool started=false;
                foreach(int cell in candidates)
                {
                    if(cell==unit.PreviousCell&&(cell!=preferred||groupRoute==null&&recovered<0)&&unit.BlockedFor<.75)continue;
                    if(cell!=preferred&&distances[cell]>=distances[unit.Cell]-.001&&unit.BlockedFor<.6)continue;
                    if(cell!=preferred&&distances[cell]>distances[unit.Cell]+.8&&unit.BlockedFor<.5)continue;
                    if(TryBeginStep(unit,cell)){started=true;break;}
                    if(unit.FootprintLength==2&&unit.TurnReadyAt>Elapsed){started=true;break;}
                }
                if(started)continue;unit.BlockedFor+=FixedSeconds;
                // 骑兵堵够了就把挡路的人挤开一格，然后再试一次。被挤者瞬移，落点仍走完整校验。
                if(CavalryShove&&IsCavalry(unit)&&unit.BlockedFor>=ShoveBlockedDelay)
                    foreach(int cell in candidates)if(TryShoveThrough(unit,cell)&&TryBeginStep(unit,cell)){started=true;break;}
                if(started)continue;
                if(unit.BlockedFor>.35)
                    foreach(int cell in candidates.Take(2))if(reservations.TryGetValue(cell,out int owner)&&owner!=unit.Id)
                    {var blocker=Units.First(actor=>actor.Id==owner);if(TryYield(blocker,unit))break;}
            }
            foreach(var unit in Units.Where(actor=>actor.Alive&&!actor.HitResolved&&actor.HitAt>=0&&actor.HitAt<=Elapsed+1e-9).ToArray())
            {
                if(Paused)return;
                if(AttackTriggered!=null){if(WindupLegal(unit)){unit.HitResolved=true;unit.ReleasedAt=Elapsed;unit.SwingUntil=Elapsed+.16;AttackTriggered(unit);}else CancelWindup(unit);continue;}
                unit.HitResolved=true;
                if(unit.Ranged)
                {Arrows.Add(new PrototypeArrow {Id=nextArrowId++,SourceId=unit.Id,Side=unit.Side,Position=unit.Position,Direction=unit.AttackFacing,Damage=DamageFor(unit),Remaining=unit.AttackRange>=0 ? unit.Reach+.3 : 8});Shots++;}
                else foreach(var target in Units.Where(actor=>actor.Alive&&actor.Side!=unit.Side).ToArray())if(InSwing(unit,target))Hurt(unit,target,DamageFor(unit));
            }
            foreach(var arrow in Arrows.ToArray()){if(Paused)return;AdvanceArrow(arrow);}
            AfterStep?.Invoke();
            if(Paused)return;
            if (AttackTriggered != null)
                foreach (var actor in Units.Where(u => u.HitResolved && u.HitAt >= 0 && u.HitAt <= Elapsed + 1e-9).ToArray())
                    if (WindupLegal(actor) && !ShouldResumeSquadFocus(actor)) StartWindup(actor);
            RebuildReservations();
            if(AutonomousOutcome&&Units.Count>0)
            {
                if(Player!=null&&!Player.Alive||!Units.Any(unit=>unit.Side==PrototypeSide.Ally&&unit.Alive))Outcome=PrototypeOutcome.Defeat;
                else if(!Units.Any(unit=>unit.Side==PrototypeSide.Enemy&&unit.Alive))Outcome=PrototypeOutcome.Victory;
            }
        }
        private PrototypeUnit Target(PrototypeUnit unit)
        {
            var current=Units.FirstOrDefault(enemy=>enemy.Id==unit.TargetId&&enemy.Alive&&enemy.Side!=unit.Side);
            if(current!=null&&!(CanTarget?.Invoke(unit,current)??true))current=null;
            if(unit.Order==PrototypeOrder.Focus)
            {
                if(current!=null)return current;
                unit.Order=PrototypeOrder.Guard;unit.Goal=unit.Anchor=unit.Position;unit.AttackSlot=-1;
            }
            bool Eligible(PrototypeUnit enemy)=>enemy.Alive&&enemy.Side!=unit.Side&&(CanTarget?.Invoke(unit,enemy)??true)&&
                (unit.Order!=PrototypeOrder.Guard||(enemy.Position-unit.Anchor).Length<=GuardTargetRadius)&&
                (unit.Order!=PrototypeOrder.Move||(enemy.Position-unit.Position).Length<=1.3)&&
                (unit.Ranged&&ReadyTarget(unit,enemy)||!double.IsPositiveInfinity(Arena.Distances(enemy.Cell)[unit.Cell]));
            if(current!=null&&Eligible(current)&&Elapsed+1e-9<unit.NextThinkAt)return current;
            if(current==null&&Elapsed+1e-9<unit.NextThinkAt)return null;
            var nearest=PreferredTarget(unit,Units.Where(Eligible));
            if(current!=null&&Eligible(current)&&(TargetWeight==null||!unit.Ranged||ReadyTarget(unit,current))&&nearest!=null&&(current.Position-unit.Position).Length<=(nearest.Position-unit.Position).Length+1.5)nearest=current;
            if(unit.TargetId!=(nearest?.Id??-1))unit.AttackSlot=-1;
            unit.TargetId=nearest?.Id??-1;unit.NextThinkAt=Elapsed+.3;return nearest;
        }
        private void Think(PrototypeUnit unit)
        {
            unit.MoveGoal = -1;
            if (unit.HitAt >= 0 && !unit.HitResolved) { FollowMeleeWindup(unit); return; }
            if (DefendWhileContactPinned(unit)) return;
            if(CommanderDoctrine){ThinkDoctrine(unit);return;}
            if(unit.Order==PrototypeOrder.Retreat)
            {if(unit.Cell==Arena.CellAt(unit.Goal)){unit.Escaped=true;return;}unit.MoveGoal=Arena.CellAt(unit.Goal);return;}
            if(unit.Order==PrototypeOrder.Move&&unit.Cell==Arena.CellAt(unit.Goal))unit.Order=PrototypeOrder.Guard;
            var target=Target(unit);
            if(unit.Order==PrototypeOrder.Guard&&(unit.Position-unit.Anchor).Length>GuardMoveRadius)target=null;
            if(target!=null)
            {
                var offset=target.BodyCenter-unit.Position;
                bool threatened=unit.Ranged&&Units.Any(enemy=>enemy.Alive&&enemy.Side!=unit.Side&&(enemy.Position-unit.Position).Length<1.4);
                if(!threatened&&InAttackRadius(unit,target)&&ClearAttack(unit.Position,target.BodyCenter))
                {unit.AttackSlot=unit.Cell;if(Face(unit,offset.Normalized)&&Elapsed+1e-9>=unit.ReadyAt)BeginAttack(unit);return;}
                int slot=AttackPosition(unit,target);
                if(slot>=0){unit.AttackSlot=slot;unit.MoveGoal=slot;return;}
                unit.AttackSlot=-1;return;
            }
            unit.AttackSlot=-1;
            if(unit.Order==PrototypeOrder.Move||unit.Order==PrototypeOrder.Guard)unit.MoveGoal=Arena.CellAt(unit.Goal);
            else if(unit.Order==PrototypeOrder.Auto)unit.MoveGoal=DefaultDestination?.Invoke(unit)??-1;
            if(unit.MoveGoal==unit.Cell)unit.MoveGoal=-1;
        }
        private int AttackPosition(PrototypeUnit actor,PrototypeUnit target)
        {
            var distances=Arena.Distances(actor.Cell,true);int best=-1;double bestScore=double.PositiveInfinity;
            int radius=(int)Math.Ceiling((actor.Reach+(HeightRangeBonus==null?0:3))*PrototypeArena.Subdivision),targetX=target.Cell%Arena.FineColumns,targetZ=target.Cell/Arena.FineColumns;
            for(int positionZ=Math.Max(0,targetZ-radius);positionZ<=Math.Min(Arena.FineRows-1,targetZ+radius);positionZ++)
                for(int positionX=Math.Max(0,targetX-radius);positionX<=Math.Min(Arena.FineColumns-1,targetX+radius);positionX++)
                {
                    int cell=positionZ*Arena.FineColumns+positionX;if(!Fits(actor,cell,(target.BodyCenter-Arena.Center(cell)).Normalized)||cell==target.Cell||double.IsPositiveInfinity(distances[cell]))continue;
                    var point=Arena.Center(cell);var axis=PrototypeUnit.MajorAxis(target.BodyCenter-point,actor.BodyAxis);double range=BodyGapAt(actor,target,point,axis),centerRange=(point-target.Position).Length;
                    if(!InAttackRadius(actor,target,point,axis)||actor.Ranged&&centerRange<Math.Min(2,actor.Reach*.6)||SlotClaimed(cell,actor)||RecentlyBlockedAttackPosition(actor,target,cell)||
                        reservations.TryGetValue(cell,out int owner)&&owner!=actor.Id||!ClearAttack(point,target.BodyCenter))continue;
                    if(CommanderDoctrine ? !DoctrineSlotAllowed(actor,point) : actor.Order==PrototypeOrder.Guard&&(point-actor.Anchor).Length>GuardMoveRadius)continue;
                    double danger=actor.Ranged ? Units.Count(enemy=>enemy.Alive&&enemy.Side!=actor.Side&&(enemy.Position-point).Length<1.5)*5 : 0;
                    double score=distances[cell]+danger+(actor.Ranged ? Math.Abs(range-Math.Min(4,actor.Reach))*.3 : range*.1)-(cell==actor.AttackSlot ? .6 : 0);
                    if(score<bestScore){best=cell;bestScore=score;}
                }
            return best;
        }
        private bool SlotClaimed(int cell,PrototypeUnit actor)=>Units.Any(other=>other.Alive&&other.Id!=actor.Id&&other.Side==actor.Side&&other.AttackSlot==cell);
        private bool ReservationStepAllowed(PrototypeUnit unit,int from,int destination)
        {
            bool Free(int cell) => !reservations.TryGetValue(cell,out int owner)||owner==unit.Id;
            int deltaX=destination%Arena.FineColumns-from%Arena.FineColumns,deltaZ=destination/Arena.FineColumns-from/Arena.FineColumns;
            return Free(destination)&&(deltaX==0||deltaZ==0||Free(from+deltaX)&&Free(from+deltaZ*Arena.FineColumns));
        }

        private bool TryBeginStep(PrototypeUnit unit,int destination)
        {
            if (ContactPinActive(unit)) return false;
            if(!(CanMove?.Invoke(unit)??true)||!Arena.CanStep(unit.Cell,destination)||!ReservationStepAllowed(unit,unit.Cell,destination))return false;
            var target=Arena.Center(destination);
            var heading=(target-unit.Position).Normalized;
            if(!BodyStep(unit,destination,heading)||!Face(unit,heading))return false;
            foreach(var other in Units.Where(actor=>actor.Alive&&actor.Id!=unit.Id))
                if(BodiesCross(unit,target,other,other.Moving ? Arena.Center(other.NextCell) : other.Position))return false;
            unit.NextCell=destination;unit.Progress=0;unit.BlockedFor=0;foreach(int cell in Footprint(unit,destination,unit.Facing))reservations[cell]=unit.Id;
            if(unit.CarrySeconds>0){double seconds=unit.CarrySeconds;unit.CarrySeconds=0;AdvanceTransit(unit,seconds);}return true;
        }
        private void AdvanceTransit(PrototypeUnit unit,double seconds)
        {
            var start=Arena.Center(unit.Cell);var destination=Arena.Center(unit.NextCell);double distance=(destination-start).Length;
            double speed=unit.JumpDuration>0?distance/unit.JumpDuration:(MovementSpeed?.Invoke(unit)??unit.MoveSpeed)/Math.Max(1,MovementCost?.Invoke(unit,unit.NextCell)??Arena.TravelCost?.Invoke(unit.NextCell)??1);
            double unbounded=unit.Progress+speed*seconds/distance,progress=unbounded>=1-1e-9 ? 1 : unbounded;
            unit.DistanceTravelled+=(progress-unit.Progress)*distance;unit.Progress=progress;unit.Position=start+(destination-start)*progress;
            if(progress>=1)
            {unit.CarrySeconds=Math.Max(0,unbounded-1)*distance/speed;unit.PreviousCell=unit.Cell;unit.Cell=unit.NextCell;unit.NextCell=-1;unit.Progress=0;if(unit.JumpDuration>0){unit.CarrySeconds=0;unit.JumpDuration=0;}CompletedSteps++;CellEntered?.Invoke(unit,unit.Cell);}
        }
        private bool TryYield(PrototypeUnit blocker,PrototypeUnit requester)
        {
            if(blocker.Role==PrototypeRole.Leader&&blocker.Strategy==PrototypeStrategy.Obey&&!blocker.ManualOrder)return false;
            if(!blocker.Alive||blocker.Side!=requester.Side||blocker.Moving||blocker.Order==PrototypeOrder.Guard||blocker.HitAt>=0&&!blocker.HitResolved)return false;
            var enemy=Units.FirstOrDefault(unit=>unit.Id==blocker.TargetId&&unit.Alive);
            if(enemy!=null&&(enemy.Position-blocker.Position).Length<=blocker.Reach)return false;
            foreach(int cell in Arena.Neighbours(blocker.Cell).OrderByDescending(cell=>(Arena.Center(cell)-requester.Position).Length))
                if(cell!=requester.Cell&&TryBeginStep(blocker,cell)){YieldMoves++;return true;}
            return false;
        }
        private void BeginAttack(PrototypeUnit actor)
        {if(AttackTriggered!=null){StartWindup(actor);return;}if(!AttacksAllowed(actor))return;actor.AttackFacing=actor.Facing;actor.HitAt=Elapsed+(actor.Ranged ? .24 : .22);actor.HitResolved=false;actor.SwingUntil=actor.HitAt+.16;actor.ReadyAt=Elapsed+(actor.AttackInterval>0 ? actor.AttackInterval : actor.Ranged ? 1.35 : .95);}
        private bool InSwing(PrototypeUnit actor,PrototypeUnit target)
        {var offset=target.BodyCenter-actor.Position;return BodyGap(actor,target)<=actor.Reach+(actor.FootprintLength==2||target.FootprintLength==2?0:target.Radius)&&PrototypePoint.Dot(offset.Normalized,actor.AttackFacing)>=.5&&double.IsPositiveInfinity(Arena.WallFraction(actor.Position,target.BodyCenter));}
        private void Hurt(PrototypeUnit source,PrototypeUnit target,double damage)
        {double actual=Math.Min(target.Health,Math.Max(0,DamageResolver?.Invoke(source,target,damage)??damage));target.Health-=actual;target.FlashUntil=Elapsed+.12;DamageApplied?.Invoke(source,target,actual);Hits++;}
        private void AdvanceArrow(PrototypeArrow arrow)
        {
            if(arrow.VisualOnly){double distance=Math.Min(arrow.Remaining,arrow.Speed*FixedSeconds);arrow.Position+=arrow.Direction*distance;arrow.Remaining-=distance;if(arrow.Remaining<=1e-8)Arrows.Remove(arrow);return;}
            double travel=Math.Min(arrow.Remaining,7*FixedSeconds);var next=arrow.Position+arrow.Direction*travel;
            double collision=Arena.WallFraction(arrow.Position,next,.04);PrototypeUnit victim=null;
            foreach(var target in Units.Where(unit=>unit.Alive&&unit.Side!=arrow.Side))
            {double fraction=BodyArrow(arrow.Position,next,target);if(fraction<collision){collision=fraction;victim=target;}}
            if(!double.IsPositiveInfinity(collision)){if(victim!=null)Hurt(Units.First(unit=>unit.Id==arrow.SourceId),victim,arrow.Damage);Arrows.Remove(arrow);return;}
            arrow.Position=next;arrow.Remaining-=travel;if(arrow.Remaining<=.00001)Arrows.Remove(arrow);
        }
        private static double CircleFraction(PrototypePoint from,PrototypePoint to,PrototypePoint center,double radius)
        {
            var offset=from-center;var delta=to-from;double constant=PrototypePoint.Dot(offset,offset)-radius*radius;
            if(constant<=0)return 0;double quadratic=PrototypePoint.Dot(delta,delta);if(quadratic<1e-12)return double.PositiveInfinity;
            double linear=PrototypePoint.Dot(offset,delta),discriminant=linear*linear-quadratic*constant;
            if(discriminant<0)return double.PositiveInfinity;double fraction=(-linear-Math.Sqrt(discriminant))/quadratic;
            return fraction>=0&&fraction<=1 ? fraction : double.PositiveInfinity;
        }
        private static double PointSegmentGap(PrototypePoint point,PrototypePoint from,PrototypePoint to)
        {var delta=to-from;double length=PrototypePoint.Dot(delta,delta);double factor=length<1e-12 ? 0 : Math.Max(0,Math.Min(1,PrototypePoint.Dot(point-from,delta)/length));return (point-(from+delta*factor)).Length;}
        private static double Cross(PrototypePoint first,PrototypePoint second)=>first.X*second.Z-first.Z*second.X;
        private static double SegmentGap(PrototypePoint firstStart,PrototypePoint firstEnd,PrototypePoint secondStart,PrototypePoint secondEnd)
        {
            var first=firstEnd-firstStart;var second=secondEnd-secondStart;double divisor=Cross(first,second);
            if(Math.Abs(divisor)>1e-9){double firstTime=Cross(secondStart-firstStart,second)/divisor,secondTime=Cross(secondStart-firstStart,first)/divisor;if(firstTime>=0&&firstTime<=1&&secondTime>=0&&secondTime<=1)return 0;}
            return Math.Min(Math.Min(PointSegmentGap(firstStart,secondStart,secondEnd),PointSegmentGap(firstEnd,secondStart,secondEnd)),Math.Min(PointSegmentGap(secondStart,firstStart,firstEnd),PointSegmentGap(secondEnd,firstStart,firstEnd)));
        }
    }
}
