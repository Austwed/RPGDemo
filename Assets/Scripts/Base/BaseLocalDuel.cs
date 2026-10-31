using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;

namespace BaseConquest.Base
{
    public static class BaseLocalDuel
    {
        public const string Id="demo:duel:azhu";
        public static BaseCampaignSession Create(BaseState state,bool battlefield=false)
        {
            var target=state.Leaders.Concat(state.Demo.Candidates).First(l=>l.Id=="partner_craft");
            var marker=BaseSpatial.Objects(state).FirstOrDefault(o=>o.Id=="demo_azhu")??new BaseMapObject(target.Id,target.Name,target.MapX,target.MapY,.5,.5,false);
            if(!BaseSceneRules.Active(state)||!BaseInteraction.CanReach(state,marker)&&!BaseDemoNpcs.NearAzhu(state))throw new InvalidOperationException("请走近阿铸再开始比武");
            if(battlefield)
            {
                if(!BaseDemoDefense.Won(state))throw new InvalidOperationException("完成首次防守后可选择迎敌战场演练");
                state=state.Copy();var field=BaseSpatial.Layout(state,"perimeter");state.Campaign.Location="perimeter";
                if(BaseSceneInstances.Active(state))state.Campaign.CurrentMapId=field.SourceMapId;
                state.PlayerX=field.SpawnX;state.PlayerY=field.SpawnY;
                marker=new BaseMapObject(target.Id,target.Name,state.PlayerX-2,state.PlayerY,.5,.5,false);
            }
            var player=state.Leaders.Single(l=>l.Id==state.PlayerLeaderId).Copy();player.Health=player.MaxHealth;
            var enemy=target.Copy();enemy.Health=enemy.MaxHealth;
            var board=BaseSceneCombat.Project(state);foreach(var c in board.Cells)c.Zone=DeploymentZone.None;
            GridPosition Nearest(double x,double y,GridPosition? avoid=null)=>board.Positions().Where(p=>board.Walkable(p)&&(!avoid.HasValue||p!=avoid.Value)).OrderBy(p=>(p.X+.5-x)*(p.X+.5-x)+(p.Z+.5-y)*(p.Z+.5-y)).ThenBy(p=>p.Z).ThenBy(p=>p.X).First();
            var a=Nearest(state.PlayerX,state.PlayerY);var b=Nearest(marker.X,marker.Y,a);board.Cell(a).Zone=DeploymentZone.A;board.Cell(b).Zone=DeploymentZone.B;
            var stats=BaseCommanderIdentity.Stats(player,new ArmyDefinition().Hero);var opponent=BaseCommanderIdentity.Stats(enemy,new ArmyDefinition().Hero);
            var scenario=new BattleScenario{Board=board,CampaignRules=true,PlayerHeroAppearance=true,PlayerLeaderSlot=0,MaxRounds=180,InstantDeathEnabled=false,
                A=new ArmyDefinition{HeroCount=1,MeleeCount=0,RangedCount=0,CavalryCount=0,Hero=stats},B=new ArmyDefinition{HeroCount=1,MeleeCount=0,RangedCount=0,CavalryCount=0,Hero=opponent},
                Objective=new ObjectiveConfig{Title="与阿铸比武 · 点到为止"},Placements=new List<UnitPlacement>{new UnitPlacement{Side=BattleSide.A,Kind=SoldierKind.Hero,CommanderSlot=0,Cell=a},new UnitPlacement{Side=BattleSide.B,Kind=SoldierKind.Hero,CommanderSlot=0,Cell=b}}};
            var enemies=BaseDemoCombat.CreateEnemies(Id,scenario.B);var first=enemies.Single();first.Rpg=enemy.Rpg.Copy();first.Rpg.Id=first.Id;first.Equipment=enemy.PersonalEquipment.Copy();first.Stats=opponent.Clone();first.Health=(float)enemy.MaxHealth;first.IsAwakened=true;
            BaseSceneRules.Configure(state,scenario,new[]{player});scenario.FogEnabled=false;var session=new BaseCampaignSession(new BaseCampaignBattle{Id=Id,EventId=Id,MapRegion=state.Campaign.Location,Rpg=state.Demo.Rpg.Copy(),StartedDay=state.Day,Scenario=scenario,Leaders=new List<BaseLeader>{player},Enemies=enemies,RealtimeRules=true,RealtimeRulesVersion=1,DemoCombat=true});
            session.Bind();if(!session.Battle.Start(out string error))throw new InvalidOperationException(error);return session;
        }
    }
}
