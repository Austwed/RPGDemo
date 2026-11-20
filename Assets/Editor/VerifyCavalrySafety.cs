using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BaseConquest.RealtimePrototype;
using BaseConquest.Base;
using BaseConquest.Battle;
using UnityEditor;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static class VerifyCavalrySafety
    {
        private static readonly MethodInfo Shove=typeof(RealtimeBattlePrototype).GetMethod("TryShoveThrough",BindingFlags.Instance|BindingFlags.NonPublic);
        private static bool Try(RealtimeBattlePrototype model,PrototypeUnit rider,int wanted)=>(bool)Shove.Invoke(model,new object[]{rider,wanted});
        private static void Need(bool condition,string reason){if(!condition)throw new InvalidOperationException(reason);}
        // 阻挡者位于骑兵正前方的相邻细格。
        private static RealtimeBattlePrototype Fixture(out PrototypeUnit rider,out PrototypeUnit blocker,bool enemy=false,bool cavalryBlocker=false)
        {
            var model=new RealtimeBattlePrototype(new PrototypeArena(PrototypeLayout.Open));
            rider=model.AddUnit(PrototypeSide.Ally,PrototypeRole.Melee,new PrototypePoint(3.25,3.25));
            rider.Mounted=true;
            blocker=model.AddUnit(enemy?PrototypeSide.Enemy:PrototypeSide.Ally,PrototypeRole.Melee,new PrototypePoint(3.75,3.25));
            blocker.Mounted=cavalryBlocker;
            return model;
        }
        [MenuItem("SLG/其他工具/验证/P53 骑兵与战术")]
        public static void RunP53()
        {
            Run();
            VerifyUnseen(false);
            VerifyUnseen(true);
            VerifyBatu();
            Need(CombatNumbers.RemainingHealth(0)==0 && CombatNumbers.RemainingHealth(.4)==1 &&
                CombatNumbers.RemainingHealth(12.99)==12,"战后生命取整");
            Debug.Log("[P53Combat] PASS");
        }

        private static void VerifyUnseen(bool lan)
        {
            var arena=new PrototypeArena(PrototypeLayout.Open);
            var model=new RealtimeBattlePrototype(arena){CommanderDoctrine=true,AutonomousOutcome=false,BothSidesHuman=lan};
            var ally=model.AddUnit(PrototypeSide.Ally,PrototypeRole.Leader,new PrototypePoint(2.25,5.25),0,true);
            var escort=model.AddUnit(PrototypeSide.Ally,PrototypeRole.Leader,new PrototypePoint(9.25,6.25),1);
            var enemy=model.AddUnit(PrototypeSide.Enemy,PrototypeRole.Leader,new PrototypePoint(20.25,6.25),0);
            enemy.Player=lan;
            var exit=new PrototypePoint(1.25,6.25);
            ally.CommandRadius=2;
            var home=ally.Position;var start=enemy.Position;
            model.CanTarget=(a,b)=>false;
            model.DefaultDestination=u=>arena.CellAt(exit);
            model.SetPaused(false);
            for(int tick=0;tick<1200;tick++)model.Tick(.02);
            Need((home-ally.Position).Length<1.65 && (escort.Position-ally.Position).Length<=3,
                "无敌情时本方队长巡游／随行越界");
            Need(lan ? (enemy.Position-start).Length<1.65 : (enemy.Position-exit).Length<2,
                "单机敌方推进或联机客方待命错误");
            Need(model.Shots==0 && model.Hits==0,"无目标时发生攻击");
        }

        private static void VerifyBatu()
        {
            var state=JsonUtility.FromJson<BaseState>(Resources.Load<TextAsset>("DemoStages/defense-male").text);
            var session=BaseDemoDefense.Begin(state,"none");
            BattleScenePlacement.Start(state,session);
            var run=new RealtimeCampaignBattle(session);
            BattleScenePlacement.Snap(state,run);
            BaseDemoDefense.Configure(run);
            Need(run.Model.Units.All(u=>u.FootprintLength==1),"正式部署使用了多格单位");
            Need(state.Demo.Rpg.Range.Length==4 && state.Demo.Rpg.Range.Take(3).SequenceEqual(new double[]{1,12,2}),"剑弓骑正式攻击距离与配表不符");
            var swamp=session.Battle.Board.Positions().First(p=>session.Battle.Board.Cell(p).Terrain==TerrainKind.Swamp);
            var point=new PrototypePoint(swamp.X+.25,swamp.Z+.25);
            int origin=run.Model.Arena.CellAt(point);
            Need(run.Model.Arena.FineWalkable(origin)&&run.Model.Arena.TravelCost(origin)==2,"首防沼泽应通行并减速");
            var rider=run.Model.Units.FirstOrDefault(u=>u.Mounted);
            Need(rider!=null&&Math.Abs(rider.Reach*2-2)<1e-8,"缺少单格骑兵的正式编制或攻击距离错误");
            Need(run.Model.PlaceNearest(rider,point),"骑兵无法进入首防沼泽");
            var batu=run.Model.Units.Single(u=>u.Side==PrototypeSide.Enemy&&u.Role==PrototypeRole.Leader);
            var hero=run.Model.Player;
            var tree=run.Tactics.Objects.FirstOrDefault(o=>o.Kind=="tree");
            Need(tree!=null,"首防缺少枯树遮挡物");
            {
                var treePoint=new PrototypePoint(tree.X+.5,tree.Y+.5);
                Need(run.Tactics.At(treePoint)==null,"枯树被识别为攻击对象");
                // Put the commander and his opponent on opposite sides of the actual tree.
                run.Model.PlaceNearest(batu,treePoint+new PrototypePoint(-2,0));
                run.Model.PlaceNearest(hero,treePoint+new PrototypePoint(2,0));
                Need(!run.Model.ClearAttack(batu.Position,hero.Position) &&
                     !BattleRangeOverlay.IncludesAttackCell(run,batu,hero.Position),"枯树后的攻击预览与实战遮挡不一致");
                Need(BattleRangeOverlay.IncludesAttackCell(run,batu,batu.Position),"预览错误地阻挡了起点");
            }
            var start=rider.Position;
            run.Model.SetPaused(false);
            for(int tick=0;tick<3000&&session.Battle.Phase==BattlePhase.Fighting;tick++)run.Tick(.02);
            var shots=run.RpgAttackHistory.Where(p=>p.SourceId==batu.Id).ToArray();
            Need((rider.Position-start).Length>1,"骑兵未走出首防沼泽");
            Need(shots.Any(p=>p.ImpactApplied),"巴图未能重新取得射击位置并命中敌方单位");
            Need(shots.All(p=>p.TargetId>=0 && string.IsNullOrEmpty(p.ObjectId) &&
                run.Battle.Soldier(p.TargetId-1)?.Side==BattleSide.A &&
                run.Model.ClearAttack(p.Origin,p.Destination)),"巴图打中了场景物件或隔障出手");
            Debug.Log("[P53Batu] terrain="+swamp+" tree="+tree?.Id+" riderDistance="+(rider.Position-start).Length+
                " shots="+shots.Length+" impact="+shots.Count(p=>p.ImpactApplied)+" target="+
                string.Join(",",shots.Select(p=>run.Battle.UnitName(run.Battle.Soldier(p.TargetId-1))).Distinct()));
        }

        public static void RunShotContexts()
        {
            var state=JsonUtility.FromJson<BaseState>(Resources.Load<TextAsset>("DemoStages/defense-male").text);
            var session=BaseDemoDefense.Begin(state,"none");
            BattleScenePlacement.Start(state,session);
            var run=new RealtimeCampaignBattle(session);
            BattleScenePlacement.Snap(state,run);
            BaseDemoDefense.Configure(run);
            var bow=run.Model.Units.First(u=>u.Side==PrototypeSide.Enemy && u.Role==PrototypeRole.Leader && u.Ranged);
            var rider=run.Model.Units.First(u=>u.Side==PrototypeSide.Ally);
            var press=run.Model.Units.First(u=>u.Side==PrototypeSide.Ally && u.Id!=rider.Id);
            var archer=run.Battle.Soldier(bow.Id-1);
            archer.Stats.HasProfessionKind=true; archer.Stats.ProfessionKind=SoldierKind.Ranged;
            archer.Rpg?.Traits.Clear();
            var mounted=run.Battle.Soldier(rider.Id-1);
            var from = new PrototypePoint(6.25,6.25);
            // This fixture can pick an ordinary soldier; combat kind falls back to its roster kind.
            if(mounted.Kind!=SoldierKind.Hero) mounted.Kind=SoldierKind.Cavalry;
            mounted.Stats.HasProfessionKind=true; mounted.Stats.ProfessionKind=SoldierKind.Cavalry;
            bow.Position=from; rider.Position=from+new PrototypePoint(3,0);
            bow.WindupTarget=rider.Id; rider.NextCell=run.Model.Arena.CellAt(rider.Position)+1;
            press.UsesRangedWeapon=false; press.Role=PrototypeRole.Melee; press.Order=PrototypeOrder.Guard;
            press.Position=from+new PrototypePoint(.5,0);
            press.AttackRange=.5;
            bool blocked=false;
            run.Model.CanTarget=(a,b)=>ReferenceEquals(a,press)&&ReferenceEquals(b,bow);
            run.Model.HeightShotClear=(a,b)=>!blocked;
            session.Opening.Rpg.HeightAdvantage=100;
            RpgAttackPacket Fire()
            {
                run.Model.AttackTriggered(bow);
                return run.RpgAttackHistory.Last();
            }
            var both=Fire();
            Need(both.NetAdvantage==-1 && both.Context==(RpgShotContext.MovingCavalry|RpgShotContext.MeleePressure),"移动骑兵与第三方近战同时施压");
            blocked=true;
            var moving=Fire();
            Need(moving.NetAdvantage==-1 && moving.Context==RpgShotContext.MovingCavalry,"枯树隔断的近战单位不能施压");
            rider.NextCell=-1;
            var clear=Fire();
            Need(clear.NetAdvantage==0&&clear.Second==0&&clear.Context==RpgShotContext.None,"停止移动后立即回普通骰");
            blocked=false;
            var pressure=Fire();
            Need(pressure.NetAdvantage==-1&&pressure.Context==RpgShotContext.MeleePressure,"第三方贴身时射向别处也取劣势");
            press.KnockedDown=true;
            Need(Fire().Context==RpgShotContext.None,"倒地单位不施压");
            archer.Rpg.Traits.Add(new RpgTrait { Id="advantage", Value=1 });
            rider.NextCell=run.Model.Arena.CellAt(rider.Position)+1;
            Need(Fire().NetAdvantage==0,"单项优势抵消对移动骑兵射击的劣势");
            press.KnockedDown=false;
            Need(Fire().NetAdvantage==-1,"一次优势不能同时抵消两项劣势");
            archer.Rpg.Traits.Clear();
            archer.Stats.HasProfessionKind=true; archer.Stats.ProfessionKind=SoldierKind.Mage;
            rider.NextCell=run.Model.Arena.CellAt(rider.Position)+1;
            var spell=Fire();
            Need(spell.Context==both.Context&&spell.NetAdvantage==0&&spell.Second==0&&spell.DamageType=="magic","法师在相同环境下仍用普通骰");
            var melee=run.Battle.Soldier(press.Id-1);
            melee.Rpg?.Traits.Clear();
            if(melee.Kind!=SoldierKind.Hero) melee.Kind=SoldierKind.Melee;
            melee.Stats.HasProfessionKind=true;
            melee.Stats.ProfessionKind=SoldierKind.Melee;
            press.WindupTarget=bow.Id;
            run.Model.AttackTriggered(press);
            Need(run.RpgAttackHistory.Last().NetAdvantage==1,"近战攻击法师仍获优势");
            Debug.Log("[P54ShotContexts] PASS moving, stopped, third-party pressure, blocked, incapacitated, cancellation, magic exemption, melee advantage");
        }

        private static bool Tally(RealtimeBattlePrototype model,string key)=>model.ShoveStats.ContainsKey(key);
        [MenuItem("SLG/其他工具/验证/骑兵安全挤开")]
        public static void Run()
        {
            // 1) 友军步兵：有安全空位就让位，且不造成伤害。
            var model=Fixture(out var rider,out var blocker);int wanted=blocker.Cell;double health=blocker.Health;
            Need(Try(model,rider,wanted),"前方友军有安全空位却未让位");
            Need(blocker.Cell!=wanted&&model.ShoveMoves==1&&blocker.Health==health,"挤开落点、次数或无伤害约束失败");
            Need(!RealtimeBattlePrototype.BodiesCross(rider,model.Arena.Center(wanted),blocker,blocker.Position),"挤开后仍挡住骑兵");
            // 内存档往返（P22 第 4 条）：落点、免疫计时、次数与模型计数都要原样回来。
            int movedCell=blocker.Cell,movedTimes=blocker.ShovedTimes,moves=model.ShoveMoves;double readyAt=blocker.ShoveReadyAt;
            var saved=JsonUtility.FromJson<RealtimeBattleState>(JsonUtility.ToJson(model.Capture()));
            model.Restore(saved);
            var restored=model.Units.First(unit=>unit.Id==blocker.Id);
            Need(restored.Cell==movedCell&&restored.ShovedTimes==movedTimes&&Math.Abs(restored.ShoveReadyAt-readyAt)<1e-9&&model.ShoveMoves==moves,"挤开状态应随内存档往返");

            // 2) 敌方步兵：横冲直撞要能撞开（2026-11-06 裁决）。
            model=Fixture(out rider,out blocker,true);wanted=blocker.Cell;
            Need(Try(model,rider,wanted)&&blocker.Cell!=wanted&&model.ShoveMoves==1,"敌方步兵应被撞开");
            model.Restore(JsonUtility.FromJson<RealtimeBattleState>(JsonUtility.ToJson(model.Capture())));

            // 3) 敌方骑兵：同体积互顶，不撞开。
            model=Fixture(out rider,out blocker,true,true);wanted=blocker.Cell;
            Need(!Try(model,rider,wanted)&&blocker.Cell==wanted&&model.ShoveMoves==0&&Tally(model,"skip:cavalry"),"敌军骑兵不应被撞开");

            model=Fixture(out rider,out blocker,false,true);wanted=blocker.Cell;
            Need(!Try(model,rider,wanted)&&blocker.Cell==wanted&&model.ShoveMoves==0,"友军骑兵不能被顶开");

            // 5) 正在前摇的敌人：撞开即打断前摇，不另加眩晕。
            model=Fixture(out rider,out blocker,true);wanted=blocker.Cell;
            blocker.HitAt=model.Elapsed+1;blocker.HitResolved=false;blocker.TargetId=rider.Id;
            Need(Try(model,rider,wanted)&&blocker.Cell!=wanted,"前摇中的敌人也应被撞开");
            Need(blocker.HitAt<0&&blocker.HitResolved,"撞开前摇单位应取消其前摇");

            // 6) 玩家主角：唯一豁免，被自家骑兵顶开会让操作者莫名其妙。
            model=Fixture(out rider,out blocker,false);wanted=blocker.Cell;blocker.Player=true;
            Need(!Try(model,rider,wanted)&&blocker.Cell==wanted&&Tally(model,"skip:player"),"玩家主角不应被自家骑兵顶开");

            // 7) 冷却期与无落点：照旧等待。
            model=Fixture(out rider,out blocker);wanted=blocker.Cell;blocker.ShoveReadyAt=100;
            Need(!Try(model,rider,wanted)&&blocker.Cell==wanted,"冷却期不应重复挤开");

            model=Fixture(out rider,out blocker);wanted=blocker.Cell;
            foreach(int cell in model.Arena.Neighbours(blocker.Cell).ToArray())
                if(!model.Reservations.ContainsKey(cell))model.AddUnit(PrototypeSide.Ally,PrototypeRole.Melee,model.Arena.Center(cell));
            int blockerId=blocker.Id;model.CanMove=unit=>unit.Id==blockerId;
            Need(!Try(model,rider,wanted)&&blocker.Cell==wanted,"无安全空位时应等待，不能覆盖预约");
            model.Restore(model.Capture());
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/cavalry-safety-verification.txt","PASS: ally and enemy infantry displaced; enemy cavalry and player leader exempt; ally cavalry exempt; windup interrupted; cooldown; fully reserved surroundings wait.\n");
            Debug.Log("[VerifyCavalrySafety] PASS");
        }
    }
}
