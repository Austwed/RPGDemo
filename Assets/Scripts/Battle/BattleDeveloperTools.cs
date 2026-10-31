using System;
using System.Linq;
namespace BaseConquest.Battle
{
    public enum DeveloperUnitAction { KnockDown, Damage100, Heal, Revive, Invulnerable, Immobile }
    public sealed partial class BattleSimulation
    {
        public bool DeveloperUnit(bool enabled,int id,DeveloperUnitAction action)
        {
            var s=Soldier(id);if(!enabled||Phase==BattlePhase.Finished||s==null||s.Escaped||!Enum.IsDefined(typeof(DeveloperUnitAction),action))return false;
            if(action==DeveloperUnitAction.Revive)
            {
                if(s.Kind!=SoldierKind.Hero||s.IsAlive)return false;
                var cell=Board.Positions().Where(p=>Board.Walkable(p)&&UnitsAt(p).All(u=>u.Side==s.Side)&&UnitsAt(p).Count<2)
                    .OrderBy(p=>GridPosition.Distance(p,s.Cell)).ThenBy(p=>p.Z*Board.Width+p.X).Select(p=>(GridPosition?)p).FirstOrDefault();
                if(!cell.HasValue)return false;
                ReleaseDeveloperRescue(s);s.Cell=s.PreviousCell=cell.Value;s.Health=s.Stats.Health;s.InstantDead=s.DeathResolved=s.Rescued=s.KnockedDown=false;
                s.IsTransit=s.PreviousTransit=s.PreviousShared=s.MovedThisRound=false;s.EscapeCarrierId=-1;s.LastFootwork=0;s.LastDamageTaken=0;
                s.Command=SquadCommand.Auto;s.TargetId=s.FocusTargetId=-1;s.LastAction=RoundAction.Wait;Vision.DeveloperRevived(s.Id);
                // 将原席位的真实成员移回原队，和装备席位一起恢复；只移动引用，不补发兵力。
                var original=CommanderSquad(s.Side,s.OriginCommanderSlot);
                if(original!=null)
                {
                    foreach(var member in Soldiers.Where(u=>u.Side==s.Side&&u.OriginCommanderSlot==s.OriginCommanderSlot).ToArray())
                    {Squads[member.SquadId].Soldiers.Remove(member);original.Soldiers.Add(member);member.SquadId=original.Id;}
                    original.CommanderId=s.Id;original.MergedInto=-1;original.IssuedCommand=SquadCommand.Auto;ClearGuardState(original);original.GuardCells.Clear();
                    original.SupplySlots.Clear();foreach(var q in Squads.Where(q=>q.Side==s.Side))q.SupplySlots.Remove(s.OriginCommanderSlot);original.SupplySlots.Add(s.OriginCommanderSlot);
                }
            }
            else if(action==DeveloperUnitAction.Invulnerable)s.DeveloperInvulnerable=!s.DeveloperInvulnerable;
            else if(action==DeveloperUnitAction.Immobile)s.DeveloperImmobile=!s.DeveloperImmobile;
            else if(action==DeveloperUnitAction.Heal){if(!s.IsAlive)return false;s.Health=s.Stats.Health;}
            else
            {
                if(!s.IsActive)return false;
                if(action==DeveloperUnitAction.Damage100&&s.DeveloperInvulnerable)return false;
                s.LastDamageTaken=action==DeveloperUnitAction.KnockDown ? s.Health : Math.Min(100,s.Health);
                s.Health=Math.Max(0,s.Health-s.LastDamageTaken);
                if(!s.IsAlive){ReleaseDeveloperRescue(s);s.DeathResolved=true;s.InstantDead=false;}
            }
            InvalidateUndo();leadRadii.Clear();Vision.Refresh();return true;
        }
        private void ReleaseDeveloperRescue(BattleSoldier s)
        {
            if(s.CarryingId>=0&&Soldier(s.CarryingId) is BattleSoldier patient){patient.CarriedById=-1;patient.Cell=s.Cell;}
            foreach(var u in Soldiers){if(u.CarryingId==s.Id)u.CarryingId=-1;if(u.RescueTargetId==s.Id){u.RescueTargetId=-1;u.RescueRequested=false;}if(u.ManualRescuerId==s.Id)u.ManualRescuerId=-1;}
            s.CarryingId=s.CarriedById=s.RescueTargetId=s.ManualRescuerId=-1;s.RescueRequested=false;s.LastMovePath.Clear();
        }
        public BattleSoldier DeveloperSpawn(bool enabled,GridPosition cell,SoldierKind kind=SoldierKind.Melee,BattleSide side=BattleSide.A)
        {
            if(!enabled||Phase==BattlePhase.Finished||!Board.Walkable(cell)||kind==SoldierKind.Hero||!Enum.IsDefined(typeof(SoldierKind),kind)||!Enum.IsDefined(typeof(BattleSide),side)||Soldiers.Count>=186||UnitsAt(cell).Any(u=>u.Side!=side)||UnitsAt(cell).Count>=2)return null;
            var q=Squads.FirstOrDefault(q=>q.Side==side&&Soldier(q.CommanderId)?.IsActive==true)??Squads.First(q=>q.Side==side);
            var stats=Scenario.Army(side).Stats(kind).Clone();var s=new BattleSoldier {Id=nextId++,Side=side,Kind=kind,SquadId=q.Id,OriginCommanderSlot=q.CommanderSlot,Cell=cell,PreviousCell=cell,MoveGoal=cell,Stats=stats,Health=stats.Health,DeveloperSpawned=true,BattlePower=stats.BaseBattlePower};
            Soldiers.Add(s);q.Soldiers.Add(s);InvalidateUndo();Vision.Refresh();return s;
        }
        public bool DeveloperPaint(bool enabled,GridPosition cell,TerrainKind kind)
        {
            if(!enabled||Phase==BattlePhase.Finished||cell==ExitCell(BattleSide.A)||cell==ExitCell(BattleSide.B)||cell==new GridPosition(0,Board.Height-1)||cell==new GridPosition(Board.Width-1,0)||Soldiers.Any(s=>!s.Escaped&&s.Cell==cell))return false;
            if(!DeveloperTerrain.Paint(true,Board,cell,kind))return false;
            leadRadii.Clear();InvalidateUndo();Vision.Refresh();return true;
        }
        public void ClearDeveloperEffects()
        {
            foreach(var s in Soldiers.Where(s=>s.DeveloperSpawned).ToArray()){ReleaseDeveloperRescue(s);Squads[s.SquadId].Soldiers.Remove(s);Soldiers.Remove(s);}
            foreach(var s in Soldiers){s.DeveloperInvulnerable=s.DeveloperImmobile=false;}
            DeveloperTerrain.Restore(Board);
            foreach(var s in Soldiers.Where(s=>!s.Escaped&&!Board.Walkable(s.Cell)))
            {
                var cell=Board.Positions().Where(p=>Board.Walkable(p)&&UnitsAt(p).All(u=>u.Side==s.Side)&&UnitsAt(p).Count<2).OrderBy(p=>GridPosition.Distance(p,s.Cell)).ThenBy(p=>p.Z*Board.Width+p.X).Select(p=>(GridPosition?)p).FirstOrDefault();
                if(cell.HasValue){s.Cell=s.PreviousCell=cell.Value;s.LastMovePath.Clear();}
            }
            leadRadii.Clear();InvalidateUndo();Vision.Refresh();if(Report!=null)RefreshBattleReport();
        }
        public bool DeveloperWin(bool enabled)
        {
            if(!enabled||Phase!=BattlePhase.Fighting)return false;
            VictoryDeclared=true;Phase=BattlePhase.Finished;Report=new BattleReport {Outcome=BattleOutcome.SideAWin};RefreshBattleReport();InvalidateUndo();return true;
        }
        public bool DeveloperReadySkills(bool enabled)
        {if(!enabled||Phase!=BattlePhase.Fighting)return false;foreach(var q in Squads){q.SkillReadyRound=RoundNumber;q.SkillChargeRounds=0;}return true;}
    }
    public sealed partial class BattleVision
    {
        public void DeveloperRevived(int id){foreach(var set in confirmedDeaths)set.Remove(id);}
    }
}
