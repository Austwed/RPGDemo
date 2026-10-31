using System;
using System.Linq;
using System.Collections.Generic;
using BaseConquest.Base;
using BaseConquest.Battle;

namespace BaseConquest.Battle
{
    public sealed partial class RealtimeCampaignBattle
    {
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
        internal void RecordEnvironmentHit(int targetId,float damage)
        {
            var target=Soldier(targetId);target.LastHitSourceId=-1;target.Health=Math.Max(0,target.Health-damage);target.LastDamageTaken=damage;ResolveCasualties();
            AddEvent(new BattleEvent {SourceId=-1,TargetId=targetId,Damage=damage,RemainingHealth=target.Health,Message="遗迹侵蚀："+UnitName(target)+"受到 "+damage.ToString("0.#")+" 伤害",Lethal=target.InstantDead,Downed=target.KnockedDown,SeenByA=Vision.CanSee(BattleSide.A,target),SeenByB=Vision.CanSee(BattleSide.B,target)});
        }
    }
}
