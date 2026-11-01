using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;

namespace BaseConquest.Base
{
    public enum BaseStandaloneBattlePreset { CavalryVsArchers, Swapped, Mixed }

    // A developer-only fixture.  It deliberately creates a complete in-memory
    // campaign session so it exercises the same realtime adapter as the Demo.
    public static class BaseStandaloneBattle
    {
        private const string Prefix="developer-battle:";
        public static bool Is(BaseCampaignSession session)=>session!=null&&session.Opening.Id?.StartsWith(Prefix,StringComparison.Ordinal)==true;
        public static string Name(BaseStandaloneBattlePreset preset)=>preset==BaseStandaloneBattlePreset.CavalryVsArchers ? "8骑兵 vs 8弓兵" : preset==BaseStandaloneBattlePreset.Swapped ? "8弓兵 vs 8骑兵" : "混编 8人 vs 8人";

        public static BaseCampaignSession Create(BaseState state,BaseStandaloneBattlePreset preset)
        {
            if(state==null||!BaseDemo.Active(state))throw new InvalidOperationException("独立战斗测试仅在已载入的 Demo 中可用");
            var left=Army(state,preset,true);
            var right=Army(state,preset,false);
            var board=BaseBattlefield.Board(state,"perimeter").Clone();
            foreach(var cell in board.Cells)cell.FixedCover=false;
            var scenario=new BattleScenario
            {
                Board=board,A=left,B=right,MaxRounds=90,
                Objective=new ObjectiveConfig {Title="独立开发者战斗测试 · "+Name(preset)},
                RandomSeed=12345,FogEnabled=true,ShowEnemiesThroughFog=true,CommanderControl=true,MobileBattleRules=true,
                PlayerLeaderSlot=0,CampaignRules=true,TestAutoDeployment=false
            };
            var rules=state.Demo?.Rpg?.Copy();
            var opening=new BaseCampaignBattle
            {
                Id=Prefix+(int)preset,EventId=Prefix+(int)preset,MapRegion="perimeter",StartedDay=state.Day,Scenario=scenario,
                Rpg=rules,RealtimeRules=true,RealtimeRulesVersion=1,DemoCombat=true,
                PlayerTroopCounts=new[]{left.MeleeCount,left.RangedCount,left.CavalryCount}
            };
            opening.Leaders.Add(Leader("developer:test:ally","测试队长",left.Hero,rules));
            opening.Troops.AddRange(Troops("developer:test:ally",left));
            opening.Enemies.Add(Enemy("developer:test:enemy:captain","测试队长",SoldierKind.Hero,0,right.Hero,rules));
            int index=0;
            foreach(var kind in BattleSimulation.TroopKinds)
                for(int n=0;n<right.Count(kind);n++)opening.Enemies.Add(Enemy("developer:test:enemy:"+index++,BattleSimulation.KindName(kind),kind,0,right.Stats(kind),rules));
            var session=new BaseCampaignSession(opening);
            session.Battle.AutoDeploy(BattleSide.A);session.Bind();
            if(!session.Battle.Start(out string problem))throw new InvalidOperationException("独立测试部署失败："+problem);
            return session;
        }

        private static ArmyDefinition Army(BaseState state,BaseStandaloneBattlePreset preset,bool ally)
        {
            var source=state.Demo?.Defense?.Army?.Clone()??new ArmyDefinition();
            source.HeroCount=1;source.MeleeCount=source.RangedCount=source.CavalryCount=0;source.Capacity=90;source.CaptainSlot=0;
            source.Commanders=new List<CommanderLoadout>{new CommanderLoadout(),new CommanderLoadout(),new CommanderLoadout()};
            if(preset==BaseStandaloneBattlePreset.Mixed)
            {
                source.MeleeCount=2;source.RangedCount=3;source.CavalryCount=3;
                source.Commanders[0].Melee=2;source.Commanders[0].Ranged=3;source.Commanders[0].Cavalry=3;
            }
            else
            {
                bool cavalry=ally== (preset==BaseStandaloneBattlePreset.CavalryVsArchers);
                if(cavalry){source.CavalryCount=8;source.Commanders[0].Cavalry=8;}
                else {source.RangedCount=8;source.Commanders[0].Ranged=8;}
            }
            return source;
        }
        private static BaseLeader Leader(string id,string name,SoldierStats stats,RpgRules rules)=>new BaseLeader
        {
            Id=id,Name=name,Health=stats.Health,MaxHealth=stats.Health,HasCombatProfile=true,CombatStats=stats.Clone(),
            Rpg=Character(id,SoldierKind.Hero,rules),
            PersonalEquipment=new BasePersonalLoadout {Version=1,Weapon="sword",InitialWeapon="sword",BaseStats=stats.Clone()}
        };
        private static IEnumerable<BaseWorkerBucket> Troops(string id,ArmyDefinition army)
        {
            foreach(var kind in BattleSimulation.TroopKinds)
                for(int n=0;n<army.Count(kind);n++)yield return new BaseWorkerBucket {SourceId=id+":"+(int)kind+":"+n,Kind=BaseCampaign.ToBase(kind),Count=1,Health=army.Stats(kind).Health,MaxHealth=army.Stats(kind).Health,HasCombatProfile=true,CombatStats=army.Stats(kind).Clone()};
        }
        private static BaseEnemyMember Enemy(string id,string name,SoldierKind kind,int slot,SoldierStats stats,RpgRules rules)
        {
            return new BaseEnemyMember {Id=id,Name=name,Kind=kind,CommanderSlot=slot,Stats=stats.Clone(),Health=stats.Health,
            Rpg=Character(id,kind,rules)
        };
        }
        private static RpgCharacter Character(string id,SoldierKind kind,RpgRules rules)
        {
            if(kind==SoldierKind.Hero)return BaseRpg.Character(id,0,false,false,rules);
            int profession=kind==SoldierKind.Hero ? 0 : BaseRpg.Profession(kind);
            return new RpgCharacter {Version=1,Id=id,Profession=profession,TemplateId=RpgRules.Template("soldier",profession),Rules=rules};
        }
    }
}
