using System;
using System.Linq;
using System.Collections.Generic;
using BaseConquest.Base;
using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;

namespace BaseConquest.Battle
{
    public sealed partial class RealtimeCampaignBattle
    {
        public bool RuinsExitOpen { get; private set; }
        public bool AwaitingRescueDialogue { get; private set; }
        public bool RuinsHalfHealthRetreat { get; private set; }

        internal BattleOutcome CheckRuinsHalfHealthRetreat()
        {
            var player = Model.Player;
            if (!Session.Opening.RuinsFirstClear || RuinsExitOpen || player?.Alive != true ||
                player.Health > player.MaxHealth * .5) return BattleOutcome.None;
            RuinsHalfHealthRetreat = true;
            return BattleOutcome.Retreat;
        }
        private readonly HashSet<int> waitingOnRuinsExit=new HashSet<int>();
        public void ContinueRuinsExit() => AwaitingRescueDialogue = false;
        public bool EscortWithoutCaptain => RuinsExitOpen &&
            !Model.Units.Any(u => u.Side == PrototypeSide.Ally && u.Role == PrototypeRole.Leader &&
                u.Alive && (Model.CanMove?.Invoke(u) ?? true));
        public bool OrderRuinsExit(PrototypePoint goal,IReadOnlyCollection<int> selected)
        {
            if(!RuinsExitOpen || AwaitingRescueDialogue)return false;
            var exit=Battle.ExitCell(BattleSide.A);
            if((int)goal.X!=exit.X || (int)goal.Z!=exit.Z)return false;
            var center=new PrototypePoint(exit.X+.5,exit.Z+.5);
            if (EscortWithoutCaptain)
            {
                bool accepted=Model.IssueOrder(-1,-1,PrototypeOrder.Retreat,center);
                if(accepted)waitingOnRuinsExit.Clear();
                return accepted;
            }
            var captains=Model.Units.Where(u=>u.Side==PrototypeSide.Ally&&u.Alive&&u.Role==PrototypeRole.Leader)
                .OrderByDescending(u=>selected?.Contains(u.Id-1)==true)
                .ThenBy(u=>(u.Position-center).Length);
            foreach(var captain in captains)
                if(Issue(new[]{captain.Id-1},PrototypeOrder.Retreat,center))return true;
            return false;
        }

        private bool OccupiesRuinsExit(PrototypeUnit actor,GridPosition exit) =>
            (int)actor.Position.X == exit.X && (int)actor.Position.Z == exit.Z &&
            (actor.FootprintLength==1||Model.Footprint(actor,actor.Cell,actor.BodyAxis).All(cell=>
                cell>=0&&cell%Model.Arena.FineColumns/2==exit.X&&cell/Model.Arena.FineColumns/2==exit.Z));

        internal bool CheckRuinsExit()
        {
            var exit = Battle.ExitCell(BattleSide.A);
            if (!RuinsExitOpen)
            {
                RuinsExitOpen = true;
                AwaitingRescueDialogue = Session.Opening.RuinsFirstClear &&
                    (!Session.Opening.HasRealtimeState || !Session.Opening.RealtimeState.RuinsRescueDialogueComplete);
                Model.CanTarget = (_, _) => false;
                Model.OnlyExitMovement = true;
                var canMove = Model.CanMove;
                Model.CanMove = actor => (actor.Side == PrototypeSide.Ally || Battle.IsFreedRuinsCivilian(Battle.Soldier(actor.Id - 1)))
                    && (canMove?.Invoke(actor) ?? true);
                Model.AfterStep = null;
                DamageLimit = (_, _, _) => 0;
                Model.Arrows.Clear();
                foreach (var packet in rpgAttacks.Where(attack => !attack.Settled)) packet.Settled = true;
                foreach (var actor in Model.Units)
                {
                    Model.CancelWindup(actor);
                    actor.ObjectOrder = false;
                    actor.ObjectTargetId = "";
                    actor.TargetId = actor.AttackSlot = actor.MoveGoal = -1;
                    if (actor.Side == PrototypeSide.Ally && OccupiesRuinsExit(actor, exit)) waitingOnRuinsExit.Add(actor.Id);
                }
                if (Session.Opening.RuinsFirstClear) ReleaseRuinsCivilians(exit);
                if (AwaitingRescueDialogue) Model.SetPaused(true);
            }
            var movable = Model.Units.Where(unit => unit.Side == PrototypeSide.Ally && unit.Alive &&
                (Model.CanMove?.Invoke(unit) ?? true)).ToArray();
            if (AwaitingRescueDialogue) return false;
            if (movable.Length == 0) return true;
            if (HasBattlefieldExits && !movable.Any(unit => unit.Role == PrototypeRole.Leader) &&
                movable.Any(unit => unit.Order != PrototypeOrder.Retreat))
            {
                Model.IssueOrder(-1, -1, PrototypeOrder.Retreat, AllyExitPoint);
                waitingOnRuinsExit.Clear();
            }
            bool escort=EscortWithoutCaptain;
            foreach(var actor in Model.Units)
            {
                if(actor.Side!=PrototypeSide.Ally || !actor.Alive&&!actor.Escaped)continue;
                if(!OccupiesRuinsExit(actor,exit)){waitingOnRuinsExit.Remove(actor.Id);continue;}
                if(!waitingOnRuinsExit.Contains(actor.Id) && (actor.Role==PrototypeRole.Leader || escort&&actor.Order==PrototypeOrder.Retreat))return true;
            }
            return false;
        }

        private void ReleaseRuinsCivilians(GridPosition exit)
        {
            foreach (var civilian in BaseDemoRuins.Civilians(Session))
            {
                var actor = Model.Units.Single(unit => unit.Id == civilian.Id + 1);
                civilian.Rescued = true;
                if (actor.Escaped) continue;
                // An ally may now occupy the cell where this civilian was knocked down.
                if (!actor.Alive)
                {
                    if (!Model.PlaceNearest(actor, actor.Position)) continue;
                    actor.Health = civilian.Health = Math.Max(1, civilian.Health);
                    actor.KnockedDown = civilian.KnockedDown = civilian.InstantDead = false;
                    civilian.DeathResolved = false;
                }
                actor.ManualOrder = actor.Steering = actor.SteeringStopped = false;
                actor.FlashUntil = Model.Elapsed;
                actor.NextThinkAt = 0;
                actor.Order = PrototypeOrder.Retreat;
                actor.Anchor = actor.Goal = new PrototypePoint(exit.X + .5, exit.Z + .5);
            }
        }

        public double RuinsExposureFraction {get;private set;}
        public void EnableRuinsExposure(double fraction)
        {
            if(RuinsExposureFraction>0)return;
            if(double.IsNaN(fraction)||double.IsInfinity(fraction)||fraction<=0||fraction>1)throw new ArgumentException("遗迹侵蚀率非法");
            RuinsExposureFraction=fraction;int last=(int)Math.Floor(Model.Elapsed+1e-8);
            var immuneEnemies=new HashSet<int>();
            for(int n=0;n<Session.Opening.Enemies.Count;n++)
            {
                var enemy=Session.Opening.Enemies[n];var soldier=Session.UnitForEnemy(n);
                if(soldier!=null&&(enemy.IsAlien||enemy.IsAwakened||Session.Opening.RuinsExposureFraction==0))immuneEnemies.Add(soldier.Id);
            }
            Model.AfterStep+=()=>
            {
                if(Model.Paused||Battle.RuinsCivilianIds.Count>0&&BaseDemoRuins.ControllerDefeated(this))return;
                int second=(int)Math.Floor(Model.Elapsed+1e-8);if(second<=last)return;int ticks=second-last;last=second;
                foreach(var actor in Model.Units.Where(u=>u.Alive).ToArray())
                {
                    var soldier=Battle.Soldier(actor.Id-1);bool immune=false;
                    if(soldier.Side==BattleSide.A&&soldier.Kind==SoldierKind.Hero)
                    {
                        var leader=Session.Opening.Leaders[soldier.OriginCommanderSlot];
                        immune=leader.IsAlien||leader.IsAwakened||actor.Player||leader.Id=="partner_craft";
                    }
                    if(soldier.Side==BattleSide.B)
                    {
                        immune=immuneEnemies.Contains(soldier.Id)||Battle.RuinsCivilianIds.Contains(soldier.Id)&&Battle.Commander(BattleSide.B,0)?.IsActive==true;
                    }
                    if(immune||soldier.DeveloperInvulnerable)continue;
                    float damage=(float)Math.Min(actor.Health,actor.MaxHealth*fraction*ticks*DifficultyIncoming(soldier.Side));
                    Battle.RecordEnvironmentHit(soldier.Id,damage);actor.Health=soldier.Health;actor.KnockedDown=soldier.KnockedDown;actor.FlashUntil=Model.Elapsed+.12;
                }
            };
        }
    }
}
namespace BaseConquest.Battle
{
    public sealed partial class BattleSimulation
    {
        internal void RecordEnvironmentHit(int targetId,float damage,string cause="遗迹侵蚀")
        {
            var target=Soldier(targetId);target.LastHitSourceId=-1;target.Health=Math.Max(0,target.Health-damage);target.LastDamageTaken=damage;ResolveCasualties();
            AddEvent(new BattleEvent {SourceId=-1,TargetId=targetId,Damage=damage,RemainingHealth=target.Health,Message=cause+"："+UnitName(target)+"受到 "+damage.ToString("0")+" 伤害",Lethal=target.InstantDead,Downed=target.KnockedDown,SeenByA=Vision.CanSee(BattleSide.A,target),SeenByB=Vision.CanSee(BattleSide.B,target)});
        }
    }
}
