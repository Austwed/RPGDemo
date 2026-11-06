using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Base;
using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;

namespace BaseConquest.RealtimePrototype
{
    [Serializable] public sealed class RealtimeBattleState
    {
        public int Version=1,NextUnit,NextArrow,Step,Hits,Shots,CompletedSteps,YieldMoves,HitSerial;
        public double Seconds,Remainder;
        public bool RuinsCivilianDamaged;
        public List<PrototypeUnit> Units=new List<PrototypeUnit>();
        public List<PrototypeArrow> Arrows=new List<PrototypeArrow>();
        public List<RpgAttackPacket> RpgAttacks=new List<RpgAttackPacket>();
        public int RpgAttackSerial;
        public List<RealtimeCommanderSkill> Skills=new List<RealtimeCommanderSkill>();
        public RealtimeBattleState Copy()=>new RealtimeBattleState {Version=Version,NextUnit=NextUnit,NextArrow=NextArrow,Step=Step,Hits=Hits,Shots=Shots,
            CompletedSteps=CompletedSteps,YieldMoves=YieldMoves,HitSerial=HitSerial,Seconds=Seconds,Remainder=Remainder,RuinsCivilianDamaged=RuinsCivilianDamaged,
            RpgAttackSerial=RpgAttackSerial,RpgAttacks=RpgAttacks.Select(a=>a.Copy()).ToList(),Units=Units.Select(unit=>unit.Copy()).ToList(),Arrows=Arrows.Select(arrow=>arrow.Copy()).ToList(),Skills=(Skills??new List<RealtimeCommanderSkill>()).Select(skill=>skill.Copy()).ToList()};
    }
    public sealed partial class RealtimeBattlePrototype
    {
        public RealtimeBattleState Capture()=>new RealtimeBattleState {NextUnit=nextUnitId,NextArrow=nextArrowId,Step=simulationStep,Hits=Hits,Shots=Shots,
            CompletedSteps=CompletedSteps,YieldMoves=YieldMoves,Seconds=Elapsed,Remainder=remainder,
            Units=Units.Select(unit=>{var copy=unit.Copy();if(copy.FootprintLength==2)copy.Facing=unit.BodyAxis;return copy;}).ToList(),Arrows=Arrows.Select(arrow=>arrow.Copy()).ToList()};
        // RPG units may combine up to 1,000,000 base attack, 10,000 supply,
        // and a 2x wine multiplier. Identity checks still verify the source.
        public const double RpgAttackDamageLimit=2020010;
        public void Restore(RealtimeBattleState state,double attackDamageLimit=1000000)
        {
            bool Finite(double value)=>PrototypeArena.Finite(value);
            bool Point(PrototypePoint point)=>Finite(point.X)&&Finite(point.Z);
            if(state==null||state.Version!=1||!Finite(state.Seconds)||state.Seconds<0||state.Seconds>10000||!Finite(state.Remainder)||state.Remainder< -1e-8||state.Remainder>=FixedSeconds+1e-8||
                state.Units==null||state.Units.Count>186||state.Arrows==null||state.Arrows.Count>1000||new[]{state.Step,state.Hits,state.Shots,state.CompletedSteps,state.YieldMoves,state.HitSerial}.Any(value=>value<0)||
                state.NextUnit<=state.Units.Select(unit=>unit?.Id??int.MaxValue).DefaultIfEmpty(0).Max()||state.NextArrow<=state.Arrows.Select(arrow=>arrow?.Id??int.MaxValue).DefaultIfEmpty(0).Max())throw new ArgumentException("即时战斗检查点版本／时钟／集合非法");
            foreach(var unit in state.Units)
            {
                if(unit==null||unit.Id<0||unit.Squad<0||unit.Squad>2||!Enum.IsDefined(typeof(PrototypeRole),unit.Role)||!Enum.IsDefined(typeof(PrototypeSide),unit.Side)||!Enum.IsDefined(typeof(PrototypeOrder),unit.Order)||
                    !new[]{unit.Health,unit.MaxHealth,unit.Speed,unit.AttackDamage,unit.AttackRange,unit.AttackInterval,unit.ReadyAt,unit.HitAt,unit.SwingUntil,unit.FlashUntil,unit.Progress,unit.BlockedFor,unit.NextThinkAt,unit.DistanceTravelled,unit.CarrySeconds,unit.WindupStarted,unit.ReleasedAt,unit.ObjectWindupAt,unit.JumpDuration,unit.EngagedUntil,unit.TurnReadyAt}.All(Finite)||unit.TurnReadyAt<0||!Point(unit.TurnTarget)||
                    unit.JumpDuration<0||unit.JumpDuration>1||unit.Health<0||unit.Health>unit.MaxHealth||unit.KnockedDown&&unit.Health!=1||unit.MaxHealth<=0||unit.Speed<=0||unit.Speed>12||unit.AttackDamage< -1||unit.AttackDamage>attackDamageLimit||unit.AttackRange< -1||unit.AttackRange>12||unit.AttackInterval< -1||unit.Progress<0||unit.Progress>=1||
                    !Point(unit.Position)||!Point(unit.Goal)||!Point(unit.Anchor)||!Point(unit.Facing)||!Point(unit.AttackFacing)||unit.Cell<0||unit.Cell>=Arena.FineColumns*Arena.FineRows||unit.Alive&&!Arena.FineWalkable(unit.Cell)||unit.Moving&&!(unit.Alive?(unit.JumpDuration>0?Arena.FineWalkable(unit.NextCell):Arena.CanStep(unit.Cell,unit.NextCell)):unit.NextCell>=0&&unit.NextCell<Arena.FineColumns*Arena.FineRows))throw new ArgumentException("即时单位检查点非法");
                var expected=Arena.Center(unit.Cell);if(unit.Moving)expected+=(Arena.Center(unit.NextCell)-expected)*unit.Progress;
                if((unit.Position-expected).Length>1e-6||!unit.Moving&&unit.Progress!=0)throw new ArgumentException("即时插值与预约不一致");
                if(unit.NextCell< -1||unit.MoveGoal< -1||unit.MoveGoal>=0&&(unit.Alive?!Arena.FineWalkable(unit.MoveGoal):unit.MoveGoal>=Arena.FineColumns*Arena.FineRows)||unit.AttackSlot< -1||unit.AttackSlot>=0&&(unit.Alive?!Arena.FineWalkable(unit.AttackSlot):unit.AttackSlot>=Arena.FineColumns*Arena.FineRows)||(unit.Alive?!Arena.CanStand(unit.Goal)||!Arena.CanStand(unit.Anchor):Arena.CellAt(unit.Goal)<0||Arena.CellAt(unit.Anchor)<0))throw new ArgumentException("即时检查点落点／路径目标非法");
            }
            if(state.Units.Select(unit=>unit.Id).Distinct().Count()!=state.Units.Count||state.Units.Count(unit=>unit.Player)>1)throw new ArgumentException("即时单位身份重复");
            if(state.Units.Any(unit=>unit.TargetId< -1||unit.TargetId>=0&&!state.Units.Any(other=>other.Id==unit.TargetId&&other.Side!=unit.Side)))throw new ArgumentException("即时目标引用非法");
            var occupied=new HashSet<int>();
            foreach(var unit in state.Units.Where(unit=>unit.Alive))
            {
                if(unit.FootprintLength<0||unit.FootprintLength>2||!Fits(unit,unit.Cell,unit.Facing,false)||unit.Moving&&!Fits(unit,unit.NextCell,unit.Facing,false))throw new ArgumentException("即时体积非法");
                foreach(int cell in Footprint(unit,unit.Cell,unit.Facing).Concat(unit.Moving?Footprint(unit,unit.NextCell,unit.Facing):Array.Empty<int>()).Distinct())if(!occupied.Add(cell))throw new ArgumentException("即时预约重叠");
                if(unit.DoctrineVersion<0||unit.DoctrineVersion>1||!Enum.IsDefined(typeof(PrototypeStrategy),unit.Strategy)||!new[]{unit.GuardRadius,unit.ThreatRadius,unit.CommandRadius,unit.NextPatrolAt}.All(Finite)||unit.GuardRadius<0||unit.ThreatRadius<unit.GuardRadius||unit.CommandRadius<unit.ThreatRadius||unit.CommandRadius>64||unit.SquadTargetId< -1||unit.SquadTargetId>=0&&!state.Units.Any(e=>e.Id==unit.SquadTargetId&&e.Side!=unit.Side)||unit.PatrolGoal< -1||unit.PatrolGoal>=0&&!Arena.FineWalkable(unit.PatrolGoal))throw new ArgumentException("即时策略检查点非法");
            }
            foreach(var unit in state.Units.Where(unit=>unit.Alive))foreach(var other in state.Units.Where(other=>other.Alive&&other.Id>unit.Id))
                if(BodiesCross(unit,unit.Moving?Arena.Center(unit.NextCell):unit.Position,other,other.Moving?Arena.Center(other.NextCell):other.Position))throw new ArgumentException("即时检查点含穿体或交叉预约");
            if(state.Arrows.Any(arrow=>arrow==null||arrow.Id<1||!state.Units.Any(unit=>unit.Id==arrow.SourceId&&unit.Side==arrow.Side)||!Point(arrow.Position)||!Point(arrow.Direction)||Math.Abs(arrow.Direction.Length-1)>1e-6||!Finite(arrow.Damage)||arrow.Damage<0||!Finite(arrow.Remaining)||arrow.Remaining<=0||arrow.Remaining>13)||state.Arrows.Select(arrow=>arrow.Id).Distinct().Count()!=state.Arrows.Count)throw new ArgumentException("即时弹道检查点非法");
            Units.Clear();Units.AddRange(state.Units.Select(unit=>unit.Copy()));Arrows.Clear();Arrows.AddRange(state.Arrows.Select(arrow=>arrow.Copy()));
            Elapsed=state.Seconds;remainder=state.Remainder;nextUnitId=state.NextUnit;nextArrowId=state.NextArrow;simulationStep=state.Step;
            Hits=state.Hits;Shots=state.Shots;CompletedSteps=state.CompletedSteps;YieldMoves=state.YieldMoves;Paused=true;RebuildReservations();
        }
    }
}

namespace BaseConquest.Battle
{
    public static class RealtimeCombatRules
    {
        public static int Range(SoldierKind kind,int quality=2)=>kind==SoldierKind.Ranged ? 12+BasePersonalEquipment.ExtraRange(quality) : kind==SoldierKind.Cavalry ? 3 : 2;
        public static double Speed(SoldierKind kind)=>kind==SoldierKind.Cavalry ? 6 : kind==SoldierKind.Ranged ? 3 : 4;
    }
    public sealed partial class RealtimeCampaignBattle
    {
        public readonly BaseCampaignSession Session;
        public readonly RealtimeBattlePrototype Model;
        public Func<PrototypeUnit,PrototypeUnit,double,double> DamageLimit;
        public Func<RealtimeCampaignBattle,BattleOutcome> OutcomeResolver;
        public Func<bool> ImmediateVictory;
        private int damageBatch;
        private void ResolveImmediateVictory()
        {
            if(damageBatch!=0||Battle.Phase!=BattlePhase.Fighting||ImmediateVictory?.Invoke()!=true)return;
            Synchronize();Battle.FinishRealtime(BattleOutcome.SideAWin);Model.SetPaused(true);
        }
        private int hitSerial;
        private readonly HashSet<int> visionActive=new HashSet<int>();
        public Action<int> TutorialAction;
        public Func<string> TutorialProgress;
        public BattleSimulation Battle=>Session.Battle;
        public static double SpeedCells(BaseCampaignSession session,BattleSoldier soldier)=>session.Opening.Rpg!=null?session.Opening.Rpg.Speed[BaseRpg.Profession(soldier.ProfessionKind)]:session.Opening.RealtimeRulesVersion==0 ? soldier.Stats.MoveDistance*PrototypeArena.Subdivision : RealtimeCombatRules.Speed(soldier.ProfessionKind);
        public static double RangeCells(BaseCampaignSession session,BattleSoldier soldier)
        {
            if(session.Opening.RealtimeRulesVersion==0)return soldier.Stats.Range*PrototypeArena.Subdivision;
            BasePersonalLoadout equipment=null;
            if(soldier.Kind==SoldierKind.Hero)
                equipment=soldier.Side==BattleSide.A ? session.Opening.Leaders[soldier.OriginCommanderSlot].PersonalEquipment : session.Opening.Enemies.FirstOrDefault(unit=>unit.Kind==SoldierKind.Hero&&unit.CommanderSlot==soldier.OriginCommanderSlot)?.Equipment;
            var weapon=equipment?.Items?.FirstOrDefault(item=>item.Slot=="weapon");
            if(session.Opening.Rpg!=null)return session.Opening.Rpg.Range[BaseRpg.Profession(soldier.CombatKind)]+(soldier.Kind==SoldierKind.Hero&&soldier.CombatKind==SoldierKind.Ranged?session.Opening.Rpg.BowQualityRange[weapon?.Quality??2]:0);
            return RealtimeCombatRules.Range(soldier.CombatKind,weapon?.Quality??2);
        }
        public BattleTacticalRuntime Tactics {get;private set;}
        public RealtimeCampaignBattle(BaseCampaignSession session)
        {
            Session=session??throw new ArgumentNullException(nameof(session));
            if(session.Opening.RealtimeRulesVersion<0||session.Opening.RealtimeRulesVersion>1)throw new ArgumentException("未知即时战斗数值版本");
            if(!session.Opening.RealtimeRules||Battle.Phase==BattlePhase.Deployment)throw new InvalidOperationException("即时战斗需已确认开战的真实队伍");
            ValidateBoard(Battle.Board);session.Bind();
            var board=Battle.Board;
            GridPosition Coarse(int cell)=>new GridPosition(cell%(board.Width*2)/2,cell/(board.Width*2)/2);
            bool Cross(int from,int to)
            {
                var first=Coarse(from);var second=Coarse(to);if(first==second)return true;
                if(GridPosition.Distance(first,second)==1)return board.CanStep(first,second);
                var horizontal=new GridPosition(second.X,first.Z);var vertical=new GridPosition(first.X,second.Z);
                return board.CanStep(first,horizontal)&&board.CanStep(horizontal,second)&&board.CanStep(first,vertical)&&board.CanStep(vertical,second);
            }
            var arena=new PrototypeArena(board.Cells.Select(cell=>!cell.Walkable).ToArray(),board.Cells.Select(cell=>cell.BlocksShots).ToArray(),Cross,board.Width,board.Height);
            arena.TravelCost=cell=>board.Cell(Coarse(cell)).FootworkCost;
            Model=new RealtimeBattlePrototype(arena) {AutonomousOutcome=false,DamageResolver=Damage};
            Model.CellEntered=(actor,cellIndex)=>
            {
                var cell=board.Cell(Coarse(cellIndex));
                if(!session.Opening.DemoCombat||actor.Side!=PrototypeSide.Enemy||!actor.Alive||!cell.HiddenTrap)return;
                cell.HiddenTrap=false;var soldier=Battle.Soldier(actor.Id-1);
                float damage=soldier.DeveloperInvulnerable?0:(float)Math.Min(actor.Health,actor.MaxHealth*.3);
                Battle.RecordEnvironmentHit(soldier.Id,damage,"陷阱");actor.Health=soldier.Health;actor.KnockedDown=soldier.KnockedDown;actor.FlashUntil=Model.Elapsed+.12;
                ResolveImmediateVictory();
            };
            if(session.Opening.DemoCombat)Battle.CasualtyChance=unit=>BaseDemoCombat.DeathChance(session,unit);
            Model.DamageApplied=(source,target,actual)=>{Battle.RecordRealtimeHit(source.Id-1,target.Id-1,(float)actual,(float)target.Health);var soldier=Battle.Soldier(target.Id-1);target.Health=soldier.Health;target.KnockedDown=soldier.KnockedDown;ResolveImmediateVictory();};
            Model.CanTarget=(source,target)=>Battle.Vision.CanSee(source.Side==PrototypeSide.Ally?BattleSide.A:BattleSide.B,Battle.Soldier(target.Id-1));
            Model.CanMove=actor=>!Battle.Soldier(actor.Id-1).DeveloperImmobile;
            Model.DefaultDestination=source=>
            {
                var side=source.Side==PrototypeSide.Ally?BattleSide.A:BattleSide.B;if((int)side==Battle.Scenario.DefenderSide)return -1;
                var exit=Battle.ExitCell(side==BattleSide.A?BattleSide.B:BattleSide.A);return arena.CellAt(new PrototypePoint(exit.X+.25,exit.Z+.25));
            };
            if(session.Opening.HasRealtimeState)
            {
                Battle.RuinsCivilianDamaged=session.Opening.RealtimeState.RuinsCivilianDamaged;
                Model.Restore(session.Opening.RealtimeState,session.Opening.Rpg!=null?RealtimeBattlePrototype.RpgAttackDamageLimit:1000000);hitSerial=session.Opening.RealtimeState.HitSerial;
                if(Model.Units.Count!=Battle.Soldiers.Count)throw new ArgumentException("即时检查点与真实编制数量不一致");
                foreach(var unit in Model.Units)ValidateIdentity(unit);
            }
            else foreach(var soldier in Battle.Soldiers.OrderBy(unit=>unit.Id))
            {
                int length=soldier.CombatKind==SoldierKind.Cavalry?2:1;
                var probe=new PrototypeUnit {Id=soldier.Id+1,FootprintLength=length,Facing=new PrototypePoint(soldier.Side==BattleSide.A?1:-1,0)};
                var positions=new[]{new PrototypePoint(soldier.Cell.X+(soldier.Side==BattleSide.A?.75:.25),soldier.Cell.Z+.25),new PrototypePoint(soldier.Cell.X+(soldier.Side==BattleSide.A?.75:.25),soldier.Cell.Z+.75),new PrototypePoint(soldier.Cell.X+.25,soldier.Cell.Z+.25),new PrototypePoint(soldier.Cell.X+.75,soldier.Cell.Z+.75)};
                var position=positions.First(point=>Model.Fits(probe,arena.CellAt(point),probe.Facing));
                var actor=Model.AddUnit(soldier.Side==BattleSide.A ? PrototypeSide.Ally : PrototypeSide.Enemy,
                    soldier.Kind==SoldierKind.Hero ? PrototypeRole.Leader : soldier.CombatKind==SoldierKind.Ranged ? PrototypeRole.Archer : PrototypeRole.Melee,
                    position,Math.Max(0,soldier.OriginCommanderSlot),soldier.Side==BattleSide.A&&soldier.Kind==SoldierKind.Hero&&soldier.OriginCommanderSlot==Battle.Scenario.PlayerLeaderSlot,soldier.Id+1);
                if(!Model.SetFootprint(actor,length))throw new InvalidOperationException("骑兵部署体积不足");
                if(soldier.Kind==SoldierKind.Hero && soldier.Side==BattleSide.A)soldier.ProtectFromInstantDeath=true;
                actor.Health=soldier.Health;actor.KnockedDown=soldier.KnockedDown;actor.MaxHealth=soldier.Stats.Health;actor.MoveSpeed=SpeedCells(session,soldier)/PrototypeArena.Subdivision;
                actor.AttackDamage=soldier.Stats.Damage*soldier.DrinkMultiplier;actor.AttackRange=RangeCells(session,soldier)/PrototypeArena.Subdivision;actor.AttackInterval=1;actor.UsesRangedWeapon=soldier.CombatKind==SoldierKind.Ranged;
            }
            if(Battle.Scenario.HeightVision)
            {
                Model.HeightShotClear=(a,b)=>BattleHeightSight.Clear(board,a.X,a.Z,b.X,b.Z);
                Model.HeightRangeBonus=(a,b,p)=>a.Ranged?BattleHeightSight.ArcherBonus(board,p.X,p.Z,b.Position.X,b.Position.Z)/PrototypeArena.Subdivision:0;
            }
            Model.CommanderDoctrine=true;
            foreach(var actor in Model.Units.Where(u=>u.DoctrineVersion==0))
            {
                var config=Battle.Scenario.Army(actor.Side==PrototypeSide.Ally?BattleSide.A:BattleSide.B).Commanders[actor.Squad];
                actor.DoctrineVersion=1;actor.Strategy=(PrototypeStrategy)(int)config.Strategy;actor.GuardRadius=config.GuardRange;actor.ThreatRadius=config.AlertRange;actor.CommandRadius=config.AttackRange;
            }
            foreach(var actor in Model.Units.Where(u=>u.Role==PrototypeRole.Leader))Battle.Scenario.Army(actor.Side==PrototypeSide.Ally?BattleSide.A:BattleSide.B).Commanders[actor.Squad].Strategy=(SquadStrategy)(int)actor.Strategy;
            Model.ExploredCell=(actor,cell)=>Battle.Vision.Explored(actor.Side==PrototypeSide.Ally?BattleSide.A:BattleSide.B,Coarse(cell));
            Model.OtherSquadVision=(actor,cell)=>Model.Units.Any(other=>other.Alive&&other.Side==actor.Side&&other.Squad!=actor.Squad&&(other.Position-arena.Center(cell)).Length<=(Battle.Scenario.HeightVision?BattleHeightSight.Radius(Battle.Board,new GridPosition((int)other.Position.X,(int)other.Position.Z)):Battle.Scenario.VisionRadius)&&(Battle.Scenario.HeightVision?BattleHeightSight.Clear(Battle.Board,other.Position.X,other.Position.Z,arena.Center(cell).X,arena.Center(cell).Z):double.IsPositiveInfinity(arena.WallFraction(other.Position,arena.Center(cell)))));
            InitializeSkills();InitializeRpgCombat();Tactics=new BattleTacticalRuntime(this);
            Synchronize(true);
        }
        public static void ValidateBoard(BattleBoard board)
        {if(board==null||board.Width<6||board.Width>64||board.Height<6||board.Height>64||board.Cells?.Length!=board.Width*board.Height)throw new ArgumentException("即时战场需6～64列／6～64行完整地图，不截取或改写");}
        private void ValidateIdentity(PrototypeUnit actor)
        {
            var soldier=Battle.Soldier(actor.Id-1);
            var role=soldier?.Kind==SoldierKind.Hero ? PrototypeRole.Leader : soldier?.CombatKind==SoldierKind.Ranged ? PrototypeRole.Archer : PrototypeRole.Melee;
            if(soldier==null||actor.Role!=role||actor.Side!=(soldier.Side==BattleSide.A?PrototypeSide.Ally:PrototypeSide.Enemy)||actor.Squad!=Math.Max(0,soldier.OriginCommanderSlot)||
                actor.Player!=(soldier.Side==BattleSide.A&&soldier.Kind==SoldierKind.Hero&&soldier.OriginCommanderSlot==Battle.Scenario.PlayerLeaderSlot)||
                Math.Abs(actor.MaxHealth-soldier.Stats.Health)>.001||Math.Abs(actor.Health-soldier.Health)>.001||actor.Escaped!=soldier.Escaped||actor.KnockedDown!=soldier.KnockedDown||
                Math.Abs(actor.MoveSpeed-SpeedCells(Session,soldier)/PrototypeArena.Subdivision)>.001||Math.Abs(actor.Damage-soldier.Stats.Damage*soldier.DrinkMultiplier)>.001||actor.Reach!=RangeCells(Session,soldier)/PrototypeArena.Subdivision||actor.AttackInterval!=1||actor.UsesRangedWeapon!=(soldier.CombatKind==SoldierKind.Ranged))throw new ArgumentException("即时检查点身份／属性与真实人员不一致");
        }
        internal void ApplyTacticalHits(PrototypeUnit source,System.Collections.Generic.IEnumerable<PrototypeUnit> targets,double raw)
        {
            damageBatch++;
            try{foreach(var target in targets)ApplyTacticalHit(source,target,raw);}
            finally{damageBatch--;}
            ResolveImmediateVictory();
        }
        public void ApplyTacticalHit(PrototypeUnit source,PrototypeUnit target,double raw)
        {
            if(Battle.Phase!=BattlePhase.Fighting)return;
            var attacker=Battle.Soldier(source.Id-1);var defender=Battle.Soldier(target.Id-1);
            double damage=defender.DeveloperInvulnerable?0:Session.Opening.Rpg!=null?RpgDamage(raw,attacker.Stats.Level,1,attacker,defender):CombatNumbers.Hit(attacker.Stats,defender.Stats,(float)raw,false);
            damage=DamageLimit?.Invoke(source,target,damage)??damage;double actual=Math.Min(target.Health,Math.Max(0,damage));target.Health-=actual;
            attacker.LastAttackCritical=false;Battle.RecordRealtimeHit(attacker.Id,defender.Id,(float)actual,(float)target.Health);target.Health=defender.Health;target.KnockedDown=defender.KnockedDown;target.FlashUntil=Model.Elapsed+.12;
            ResolveImmediateVictory();
        }
        private double Damage(PrototypeUnit source,PrototypeUnit target,double raw)
        {
            var attacker=Battle.Soldier(source.Id-1);var defender=Battle.Soldier(target.Id-1);
            if(defender.DeveloperInvulnerable)return 0;
            bool critical=BattleSimulation.StableRoll(Battle.Scenario.RandomSeed,attacker.Id,1000+hitSerial++)<attacker.Stats.CritChance;
            attacker.LastAttackCritical=critical;
            double damage=CombatNumbers.Hit(attacker.Stats,defender.Stats,(float)raw,critical);
            return DamageLimit?.Invoke(source,target,damage)??damage;
        }
        public bool Issue(IEnumerable<int> commanderIds,PrototypeOrder order,PrototypePoint point,int target=-1,bool commanderOnly=false,bool squadFocus=false)
        {
            if(Battle.Phase!=BattlePhase.Fighting)return false;
            var commanders=commanderIds.Select(Battle.Soldier).Where(unit=>unit?.IsActive==true&&unit.Side==BattleSide.A&&unit.Kind==SoldierKind.Hero).ToArray();
            var targetUnit=Battle.Soldier(target);
            if(commanders.Length==0||order==PrototypeOrder.Focus&&(targetUnit?.IsActive!=true||targetUnit.Side!=BattleSide.B||!Battle.PlayerCanSee(BattleSide.A,targetUnit)))return false;
            Tactics.Cancel(commanders.Select(l=>l.OriginCommanderSlot));
            var slots=commanders.Select(leader=>leader.OriginCommanderSlot).Distinct().ToArray();
            var anchors=Model.Units.Where(u=>u.Role==PrototypeRole.Leader&&u.Side==PrototypeSide.Ally&&slots.Contains(u.Squad)).ToDictionary(u=>u.Id,u=>u.Anchor);
            bool accepted=Model.IssueOrder(-1,-1,order,point,target<0 ? -1 : target+1,slots,true);
            if(accepted)
            {
                foreach(var u in Model.Units.Where(u=>anchors.ContainsKey(u.Id)))if(order==PrototypeOrder.Focus)u.Anchor=anchors[u.Id];
                Model.MarkDirectOrder(slots,order,target<0?-1:target+1,squadFocus);
            }
            if(accepted){Battle.RecordRealtimeOrder();TutorialAction?.Invoke(BaseDemoIntro.Select|(order==PrototypeOrder.Move?BaseDemoIntro.Move:order==PrototypeOrder.Guard?BaseDemoIntro.Guard:order==PrototypeOrder.Focus?BaseDemoIntro.Attack:0));}
            Synchronize();return accepted;
        }
        public bool SteerCommander(int id,PrototypePoint direction)
        {
            if(Battle.Phase!=BattlePhase.Fighting||!Model.SteerLeader(id+1,direction))return false;
            var actor=Model.Units.First(u=>u.Id==id+1);Tactics.Cancel(new[]{actor.Squad});
            Battle.RecordRealtimeOrder();TutorialAction?.Invoke(BaseDemoIntro.Select|BaseDemoIntro.Move);Synchronize();return true;
        }
        public bool Strategy(int slot,SquadStrategy strategy)
        {
            var actor=Model.Units.FirstOrDefault(u=>u.Alive&&u.Side==PrototypeSide.Ally&&u.Role==PrototypeRole.Leader&&u.Squad==slot);if(actor==null)return false;
            Model.SetStrategy(actor,(PrototypeStrategy)(int)strategy);Battle.Scenario.Army(BattleSide.A).Commanders[slot].Strategy=strategy;if(strategy==SquadStrategy.Attack)TutorialAction?.Invoke(BaseDemoIntro.AutoAttack);return true;
        }
        public bool DefensePursuitReady=>Session.Opening.EventId==BaseDemoDefense.EventId&&Battle.Phase==BattlePhase.Fighting&&Model.Elapsed+1e-8>=Battle.Scenario.Objective.HoldRounds&&!BaseDemoCombat.Defeated(this);
        public bool FinishDefensePursuit()
        {
            if(!DefensePursuitReady)return false;
            foreach(var unit in Model.Units.Where(unit=>unit.Side==PrototypeSide.Enemy&&unit.Alive))unit.Escaped=true;
            Synchronize();Battle.FinishRealtime(BattleOutcome.SideAWin);Model.SetPaused(true);return true;
        }
        public bool RetreatAll()
        {
            if(Session.Opening.EventId==BaseDemoDefense.SecondEventId)return false;
            if(Session.Opening.DemoCombat){if(Battle.Phase!=BattlePhase.Fighting)return false;Synchronize();Battle.FinishRealtime(BattleOutcome.Retreat);Model.SetPaused(true);return true;}
            var exit=Battle.ExitCell(BattleSide.A);var point=new PrototypePoint(exit.X+.25,exit.Z+.25);bool accepted=false;
            for(int squad=0;squad<3;squad++)accepted|=Model.IssueOrder(squad,-1,PrototypeOrder.Retreat,point);
            if(accepted)Battle.RecordRealtimeOrder();
            Synchronize();return accepted;
        }
        public bool DeveloperUnit(bool enabled,int id,DeveloperUnitAction action)
        {
            var actor=Model.Units.FirstOrDefault(unit=>unit.Id==id+1);
            if(!enabled||action==DeveloperUnitAction.Revive||actor==null||!Battle.DeveloperUnit(true,id,action))return false;
            actor.Health=Battle.Soldier(id).Health;
            if(action==DeveloperUnitAction.KnockDown||action==DeveloperUnitAction.Damage100)actor.FlashUntil=Model.Elapsed+.12;
            Synchronize(true);return true;
        }
        public void RefreshDeveloperState()=>Synchronize(true);
        public void Tick(double seconds)
        {
            if(Battle.Phase!=BattlePhase.Fighting||Model.Paused)return;
            Model.Tick(seconds);if(Battle.Phase!=BattlePhase.Fighting){Synchronize();return;}Tactics.Tick(seconds);Synchronize();if(Model.Paused)return;
            if(OutcomeResolver!=null)
            {
                var outcome=OutcomeResolver(this);if(outcome!=BattleOutcome.None){Battle.FinishRealtime(outcome);Model.SetPaused(true);}return;
            }
            bool allies=Model.Units.Any(unit=>unit.Side==PrototypeSide.Ally&&unit.Alive),enemies=Model.Units.Any(unit=>unit.Side==PrototypeSide.Enemy&&unit.Alive);
            BattleOutcome result=BattleOutcome.None;
            if(!allies)result=Model.Units.Any(unit=>unit.Side==PrototypeSide.Ally&&unit.Escaped) ? BattleOutcome.Retreat : enemies ? BattleOutcome.SideBWin : BattleOutcome.Draw;
            else if(!enemies||Battle.Scenario.Objective.Kind==ObjectiveKinds.Hold&&Model.Elapsed+1e-8>=Battle.Scenario.Objective.HoldRounds)result=BattleOutcome.SideAWin;
            else if(Battle.RealtimeVictorySatisfied)result=BattleOutcome.SideAWin;
            else if(Model.Elapsed+1e-8>=Battle.Scenario.MaxRounds)result=BattleOutcome.RoundLimit;
            if(result!=BattleOutcome.None){Battle.FinishRealtime(result);Model.SetPaused(true);}
        }
        private void Synchronize(bool forceVision=false)
        {
            bool visionChanged=forceVision||Battle.RoundNumber!=(int)Math.Floor(Model.Elapsed+1e-8);
            foreach(var actor in Model.Units)
            {
                var previous=Battle.Soldier(actor.Id-1);visionChanged|=previous.Cell.X!=(int)actor.Position.X||previous.Cell.Z!=(int)actor.Position.Z||visionActive.Contains(actor.Id)!=actor.Alive;
                var soldier=Battle.Soldier(actor.Id-1);soldier.Health=(float)actor.Health;soldier.KnockedDown=actor.KnockedDown;soldier.Escaped=actor.Escaped;soldier.IsTransit=false;
                if(Model.Elapsed>=actor.FlashUntil)soldier.LastDamageTaken=0;
                soldier.PreviousCell=soldier.Cell;soldier.Cell=new GridPosition((int)actor.Position.X,(int)actor.Position.Z);
                soldier.Command=actor.Order==PrototypeOrder.Move ? SquadCommand.Move : actor.Order==PrototypeOrder.Guard ? SquadCommand.Hold : actor.Order==PrototypeOrder.Focus ? SquadCommand.Focus : actor.Order==PrototypeOrder.Retreat ? SquadCommand.Retreat : SquadCommand.Auto;
                soldier.FocusTargetId=actor.TargetId<0 ? -1 : actor.TargetId-1;soldier.MoveGoal=new GridPosition((int)actor.Goal.X,(int)actor.Goal.Z);
                soldier.LastAction=actor.Moving ? RoundAction.Move : actor.SwingUntil>Model.Elapsed ? RoundAction.Attack : RoundAction.Wait;
                if(actor.TargetId>=0){var target=Model.Units.FirstOrDefault(unit=>unit.Id==actor.TargetId);if(target!=null)soldier.LastTargetCell=new GridPosition((int)target.Position.X,(int)target.Position.Z);}
            }
            Battle.SetRealtimeClock(Model.Elapsed);Battle.ResolveCasualties();
            if(Session.Opening.DemoCombat)foreach(var actor in Model.Units){var soldier=Battle.Soldier(actor.Id-1);actor.Health=soldier.Health;actor.KnockedDown=soldier.KnockedDown;}
            if(visionChanged){Battle.Vision.Refresh();visionActive.Clear();foreach(var actor in Model.Units)if(actor.Alive)visionActive.Add(actor.Id);}
        }
        public RealtimeBattleState CaptureState()
        {
            Synchronize();var result=Model.Capture();result.Skills=skills.Select(skill=>skill.Copy()).ToList();result.HitSerial=hitSerial;result.RuinsCivilianDamaged=Battle.RuinsCivilianDamaged;SaveRpgCombat(result);return result;
        }
        public void SaveCheckpoint(BaseState state)
        {
            Synchronize();
            if(Rpg!=null)foreach(var l in Session.Opening.Leaders)state.Leaders.Single(p=>p.Id==l.Id).Rpg.AutoSkill=l.Rpg.AutoSkill;
            if(Session.Opening.TacticalObjects?.Count>0)
            {var layout=BaseSpatial.Layout(state,string.IsNullOrEmpty(Session.Opening.MapId)?Session.Opening.MapRegion:Session.Opening.MapId);layout.TacticalObjects=Session.Opening.TacticalObjects.Select(o=>o.Copy()).ToList();}
            Session.Opening.HasRealtimeState=true;Session.Opening.RealtimeState=CaptureState();Session.SaveCheckpoint(state);
        }
    }
    public sealed partial class BattleSimulation
    {
        public HashSet<int> RuinsCivilianIds=new HashSet<int>();
        public bool RuinsCivilianDamaged;
        internal bool RealtimeVictorySatisfied=>CommanderVictorySatisfied;
        internal void RecordRealtimeOrder()=>CommandsIssued++;
        internal void SetRealtimeClock(double seconds){RoundNumber=(int)Math.Floor(seconds+1e-8);LastRoundSummary="即时作战 "+seconds.ToString("0.0")+"秒";}
        internal void FinishRealtime(BattleOutcome outcome)
        {Phase=BattlePhase.Finished;VictoryDeclared=outcome==BattleOutcome.SideAWin;Report=new BattleReport {Outcome=outcome};RefreshBattleReport();InvalidateUndo();}
        internal void RecordRealtimeHit(int sourceId,int targetId,float damage,float health)
        {
            var source=Soldier(sourceId);var target=Soldier(targetId);source.DamageDealt+=damage;target.LastHitSourceId=sourceId;target.Health=health;target.LastDamageTaken=damage;
            if(damage>0&&source.Side==BattleSide.A&&RuinsCivilianIds.Contains(targetId))RuinsCivilianDamaged=true;
            if(CasualtyChance!=null)ResolveCasualties();
            if(damage>0)AddEvent(new BattleEvent {SourceId=sourceId,TargetId=targetId,Damage=damage,RemainingHealth=target.Health,Critical=source.LastAttackCritical,Lethal=CasualtyChance!=null?target.InstantDead:health<=0&&target.Kind!=SoldierKind.Hero,Downed=CasualtyChance!=null?target.KnockedDown:health<=0&&target.Kind==SoldierKind.Hero,SeenByA=Vision.CanSee(BattleSide.A,source)||Vision.CanSee(BattleSide.A,target),SeenByB=Vision.CanSee(BattleSide.B,source)||Vision.CanSee(BattleSide.B,target)});
        }
    }
}
