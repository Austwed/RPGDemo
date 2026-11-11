using System;
using BaseConquest.RealtimePrototype;
using BaseConquest.Presentation;
using System.Linq;
using System.IO;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static class VerifyCavalryDiagonal
    {
        private static void Need(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        public static void RunAll()
        {
            Run();VerifyCavalrySafety.Run();VerifyDemoCheckpoint.Run();
            var standalone=typeof(VerifyCavalryDiagonal).Assembly.GetType("BaseConquest.Editor.VerifyStandaloneBattleFixture")?.GetMethod("Verify",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
            Need(standalone!=null,"缺少独立战斗夹具");standalone.Invoke(null,null);Debug.Log("[VerifyCavalryDiagonal] all regression PASS");
        }
        public static void Run()
        {
            VerifyGroupDestinations();
            foreach(double degrees in new[]{44d,45d,46d})
            {
                var axis=new PrototypePoint(1,0);int changes=0;
                for(int i=0;i<400;i++)
                {double angle=(degrees+(i%2==0?.01:-.01))*Math.PI/180;var next=PrototypeUnit.MajorAxis(new PrototypePoint(Math.Cos(angle),Math.Sin(angle)),axis);if(next.X!=axis.X||next.Z!=axis.Z)changes++;axis=next;}
                Need(changes==0,"斜向抖动导致轴切换："+degrees);
            }
            foreach(var start in new[]{new PrototypePoint(1,0),new PrototypePoint(0,1)})
            {var axis=start;for(int i=0;i<400;i++){double jitter=i%2==0?.01:-.01;var next=PrototypeUnit.MajorAxis(new PrototypePoint(.707+jitter,.707-jitter),axis);Need(next.X==axis.X&&next.Z==axis.Z,"±0.01 输入抖动改变长轴");axis=next;}}
            foreach(double degrees in new[]{44d,45d,46d})
            {
                var march=new RealtimeBattlePrototype(new PrototypeArena(PrototypeLayout.Open));
                var moving=march.AddUnit(PrototypeSide.Ally,PrototypeRole.Leader,new PrototypePoint(3.25,3.25));
                Need(march.SetFootprint(moving,2),"斜向行军骑兵初始化失败");
                march.AddUnit(PrototypeSide.Enemy,PrototypeRole.Melee,new PrototypePoint(12.25,12.25));
                march.CanMove=u=>u.Id==moving.Id;
                double radians=degrees*Math.PI/180;var goal=moving.Position+new PrototypePoint(Math.Cos(radians)*4,Math.Sin(radians)*4);
                Need(march.IssueOrder(0,moving.Id,PrototypeOrder.Move,goal),"无法下达斜向移动");march.SetPaused(false);
                var initial=moving.Position;var axis=moving.BodyAxis;int changes=0;
                for(int i=0;i<1000;i++)
                {
                    march.Tick(.02);var next=moving.BodyAxis;if(next.X!=axis.X||next.Z!=axis.Z)changes++;axis=next;
                    var cells=march.Footprint(moving,moving.Cell,moving.Facing).ToArray();
                    Need(cells.Length==2&&cells[1]==moving.Cell-(int)axis.X-(int)axis.Z*march.Arena.FineColumns,"斜向行军占地与主轴不一致");
                }
                Need((moving.Position-initial).Length>2&&changes<=1,"斜向行军未推进或主轴反复切换："+degrees+"/"+changes+"/"+(moving.Position-initial).Length);
            }
            var model=new RealtimeBattlePrototype(new PrototypeArena(PrototypeLayout.Open));
            var horse=model.AddUnit(PrototypeSide.Ally,PrototypeRole.Leader,new PrototypePoint(3.25,3.25));Need(model.SetFootprint(horse,2),"骑兵占地初始化失败");
            var foot=model.AddUnit(PrototypeSide.Enemy,PrototypeRole.Melee,new PrototypePoint(6.25,6.25));
            var horizontal=horse.BodyAxis;Need(model.Face(horse,new PrototypePoint(1,1)),"同轴斜向不应等待转身");
            var occupied=model.Footprint(horse,horse.Cell,horse.Facing).ToArray();Need(occupied.Length==2&&occupied[1]==horse.Cell-1&&horse.BodyHalf.X>horse.BodyHalf.Z,"占地与身体长轴不一致");
            Need(PlayerHeroFrames.Face(horse.BodyAxis.X,horse.BodyAxis.Z,HeroFacing.North)==HeroFacing.East&&PlayerHeroFrames.FaceTowards(1,1,HeroFacing.East)==HeroFacing.East,"贴图与身体朝向不一致");
            double beforeA=1.05,beforeB=1.389411;
            double Limit(bool a)
            {double lo=0,hi=5;for(int i=0;i<60;i++){double d=(lo+hi)*.5;foot.Position=horse.Position+new PrototypePoint(d/Math.Sqrt(2),d/Math.Sqrt(2));if((a?RealtimeBattlePrototype.BodyGap(horse,foot):RealtimeBattlePrototype.BodyGap(foot,horse))<=1.05)lo=d;else hi=d;}return lo;}
            double afterA=Limit(true),afterB=Limit(false);Need(Math.Abs(afterA-afterB)<.05,"双向攻击距离不对称");
            var delta=horse.BodyCenter-foot.BodyCenter;var half=horse.BodyHalf+foot.BodyHalf;double gx=Math.Max(0,Math.Abs(delta.X)-half.X),gz=Math.Max(0,Math.Abs(delta.Z)-half.Z);
            Need(Math.Abs(RealtimeBattlePrototype.BodyGap(horse,foot)-Math.Sqrt(gx*gx+gz*gz))<1e-9,"攻击距离与 AABB 间隙不一致");
            var saved=model.Capture();string json=JsonUtility.ToJson(saved);Need(!json.Contains("bodyAxis"),"运行时方向不应新增存档字段");
            var restored=JsonUtility.FromJson<RealtimeBattleState>(json);Need(restored.Units.Single(u=>u.Id==horse.Id).BodyAxis.X==horizontal.X,"读档无法恢复主轴");
            var verticalModel=new RealtimeBattlePrototype(new PrototypeArena(PrototypeLayout.Open));var vertical=verticalModel.AddUnit(PrototypeSide.Ally,PrototypeRole.Leader,new PrototypePoint(4.25,4.25));vertical.Facing=new PrototypePoint(0,1);Need(verticalModel.SetFootprint(vertical,2),"纵向骑兵初始化失败");
            Need(verticalModel.Face(vertical,new PrototypePoint(1,1))&&vertical.BodyAxis.Z==1,"45 度输入未保持纵向滞回");
            var verticalSave=JsonUtility.FromJson<RealtimeBattleState>(JsonUtility.ToJson(verticalModel.Capture()));var restoredVertical=verticalSave.Units.Single();Need(restoredVertical.BodyAxis.Z==1,"斜向时存读丢失纵向占地");verticalModel.Restore(verticalSave);Need(verticalModel.Units.Single().BodyAxis.Z==1,"纵向占地检查点恢复失败");
            var duel=new RealtimeBattlePrototype(new PrototypeArena(PrototypeLayout.Open));var rider=duel.AddUnit(PrototypeSide.Ally,PrototypeRole.Leader,new PrototypePoint(3.25,3.25));Need(duel.SetFootprint(rider,2),"斜向接敌骑兵初始化失败");
            var enemy=duel.AddUnit(PrototypeSide.Enemy,PrototypeRole.Melee,new PrototypePoint(7.25,7.25));duel.AddUnit(PrototypeSide.Ally,PrototypeRole.Melee,new PrototypePoint(3.25,4.25));duel.AddUnit(PrototypeSide.Ally,PrototypeRole.Melee,new PrototypePoint(4.25,3.25));
            duel.CanMove=u=>u.Id==rider.Id;Need(duel.IssueOrder(0,rider.Id,PrototypeOrder.Focus,enemy.Position,enemy.Id),"无法下达斜向攻击");duel.SetPaused(false);
            double enemyHealth=enemy.Health;
            for(int i=0;i<1200&&enemy.Health>=enemyHealth;i++)
            {
                duel.Tick(.02);var a=rider.BodyAxis;var cells=duel.Footprint(rider,rider.Cell,rider.Facing).ToArray();
                Need(cells.Length==2&&cells[1]==rider.Cell-(int)a.X-(int)a.Z*duel.Arena.FineColumns&&((a.X!=0)==(rider.BodyHalf.X>rider.BodyHalf.Z)),"移动中占地与身体长轴不一致");
                var face=PlayerHeroFrames.Face(a.X,a.Z,HeroFacing.South);Need((a.X!=0)==(face==HeroFacing.East||face==HeroFacing.West),"移动中贴图与长轴不一致");
                foreach(var other in duel.Units.Where(u=>u.Alive&&u.Id!=rider.Id))Need(!RealtimeBattlePrototype.BodiesCross(rider,rider.Position,other,other.Position),"斜向接敌出现穿体");
            }
            Need(enemy.Health<enemyHealth,"骑兵斜向密集接敌未在时限内命中敌人");
            var shoveModel=new RealtimeBattlePrototype(new PrototypeArena(PrototypeLayout.Open));
            var shover=shoveModel.AddUnit(PrototypeSide.Ally,PrototypeRole.Melee,new PrototypePoint(3.25,3.25));
            Need(shoveModel.SetFootprint(shover,2),"斜向挤开骑兵初始化失败");
            var blocker=shoveModel.AddUnit(PrototypeSide.Ally,PrototypeRole.Melee,new PrototypePoint(3.75,3.75));
            int wanted=blocker.Cell;
            var shove=typeof(RealtimeBattlePrototype).GetMethod("TryShoveThrough",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            Need(shove!=null&&(bool)shove.Invoke(shoveModel,new object[]{shover,wanted}),"斜向前方友军未让位");
            Need(shoveModel.ShoveMoves==1&&blocker.Cell!=wanted&&!RealtimeBattlePrototype.BodiesCross(shover,shoveModel.Arena.Center(wanted),blocker,blocker.Position),"斜向挤开后仍有穿体");
            Directory.CreateDirectory("Logs");File.WriteAllText("Logs/cavalry-diagonal-verification.txt",$"before horse→foot={beforeA:F6} foot→horse={beforeB:F6}\nafter horse→foot={afterA:F6} foot→horse={afterB:F6}\n");
            Debug.Log("[VerifyCavalryDiagonal] PASS");
        }

        private static void VerifyGroupDestinations()
        {
            var model = new RealtimeBattlePrototype(new PrototypeArena(PrototypeLayout.Open)) { CommanderDoctrine = true };
            var cavalry = model.AddUnit(PrototypeSide.Ally, PrototypeRole.Leader, new PrototypePoint(3.25, 3.25), 2);
            var first = model.AddUnit(PrototypeSide.Ally, PrototypeRole.Leader, new PrototypePoint(3.25, 5.25), 0);
            var second = model.AddUnit(PrototypeSide.Ally, PrototypeRole.Leader, new PrototypePoint(3.25, 7.25), 1);
            model.AddUnit(PrototypeSide.Enemy, PrototypeRole.Melee, new PrototypePoint(21.25, 12.25));
            var commanders = new[] { first, second, cavalry };
            var squads = new[] { 2, 0, 1 };
            Need(model.SetFootprint(cavalry, 2), "多队长夹具骑兵占格不合法");
            foreach (var commander in commanders) commander.Strategy = PrototypeStrategy.Obey;
            var goal = new PrototypePoint(8.25, 5.25);
            Need(model.IssueOrder(-1, -1, PrototypeOrder.Move, goal, squads: squads, leadersOnly: true) &&
                first.Goal.Equals(goal) && commanders.Select(u => model.Arena.CellAt(u.Goal)).Distinct().Count() == 3,
                "多人移动未按 A/B/C 分开落点");
            var previous = commanders.Select(u => (u.Order, u.Goal, u.Anchor)).ToArray();
            model.UnitCellAllowed = (unit, cell) => unit != second || cell == model.Arena.CellAt(goal);
            Need(!model.IssueOrder(-1, -1, PrototypeOrder.Guard, goal, squads: squads, leadersOnly: true) &&
                commanders.Select(u => (u.Order, u.Goal, u.Anchor)).SequenceEqual(previous), "落点不足时改写了原有指令");
            model.UnitCellAllowed = null;
            Need(model.IssueOrder(-1, -1, PrototypeOrder.Guard, goal, squads: squads, leadersOnly: true) &&
                commanders.All(u => u.Anchor.Equals(u.Goal)) && commanders.Select(u => u.Anchor).Distinct().Count() == 3,
                "多人驻守没有沿用各自落点作为锚点");
            model.MarkDirectOrder(squads, PrototypeOrder.Guard, -1, false);
            model.SetPaused(false);
            for (int step = 0; step < 1000 && commanders.Any(u => model.Arena.CellAt(u.Goal) != u.Cell); step++) model.Tick(.02);
            Need(commanders.All(u => model.Arena.CellAt(u.Goal) == u.Cell) &&
                commanders.SelectMany(u => model.Footprint(u, u.Cell, u.BodyAxis)).Distinct().Count() == 4,
                "多人驻守未分别到达含骑兵头尾的四个占格");
            VerifyGroupCavalryArrival();
            Debug.Log("[VerifyCavalryDiagonal] PASS group move, group guard, cavalry footprint and atomic refusal");
        }

        private static void VerifyGroupCavalryArrival()
        {
            var model = new RealtimeBattlePrototype(new PrototypeArena(PrototypeLayout.Open)) { CommanderDoctrine = true };
            var cavalry = model.AddUnit(PrototypeSide.Ally, PrototypeRole.Leader, new PrototypePoint(4.25, 5.25), 0);
            var second = model.AddUnit(PrototypeSide.Ally, PrototypeRole.Leader, new PrototypePoint(4.25, 9.25), 1);
            var blocker = model.AddUnit(PrototypeSide.Ally, PrototypeRole.Leader, new PrototypePoint(7.75, 5.25), 2);
            var enemy = model.AddUnit(PrototypeSide.Enemy, PrototypeRole.Melee, new PrototypePoint(21.25, 12.25));
            foreach (var leader in new[] { cavalry, second, blocker }) leader.Strategy = PrototypeStrategy.Obey;
            model.CanMove = unit => unit.Id != blocker.Id && unit.Id != enemy.Id;
            var squads = new[] { 0, 1 };
            var goal = new PrototypePoint(8.25, 5.25);
            Need(model.SetFootprint(cavalry, 2) && model.IssueOrder(-1, -1, PrototypeOrder.Move, goal, squads: squads, leadersOnly: true) &&
                cavalry.Goal.Equals(goal) && !cavalry.GroupArrivalAxis.Equals(new PrototypePoint(1, 0)), "规划未绕开点击格后方已占用的尾格");
            model.MarkDirectOrder(squads, PrototypeOrder.Move, -1, false);
            var checkpoint = JsonUtility.FromJson<RealtimeBattleState>(JsonUtility.ToJson(model.Capture()));
            model.Restore(checkpoint);
            cavalry = model.Units.Single(unit => unit.Squad == 0 && unit.Side == PrototypeSide.Ally);
            model.SetPaused(false);
            int destination = model.Arena.CellAt(goal);
            for (int step = 0; step < 1500 && cavalry.Cell != destination; step++) model.Tick(.02);
            Need(cavalry.Cell == destination && cavalry.BodyAxis.Equals(cavalry.GroupArrivalAxis) &&
                !model.Footprint(cavalry, cavalry.Cell, cavalry.BodyAxis).Contains(blocker.Cell),
                "读档后的多人骑兵未按已校验的朝向进入落点");
        }
        public static void Baseline()
        {
            var horse=new PrototypeUnit {FootprintLength=2,Position=new PrototypePoint(0,0),Facing=new PrototypePoint(1,1).Normalized};
            var foot=new PrototypeUnit {FootprintLength=1,Facing=new PrototypePoint(-1,-1).Normalized};
            double Limit(bool horseAttacks)
            {
                double lo=0,hi=5;for(int i=0;i<60;i++){double d=(lo+hi)*.5;foot.Position=new PrototypePoint(d/Math.Sqrt(2),d/Math.Sqrt(2));double gap=horseAttacks?RealtimeBattlePrototype.BodyGap(horse,foot):RealtimeBattlePrototype.BodyGap(foot,horse);if(gap<=1.05)lo=d;else hi=d;}return lo;
            }
            Debug.Log("[VerifyCavalryDiagonal] baseline horse→foot="+Limit(true).ToString("F6")+" foot→horse="+Limit(false).ToString("F6"));
        }
    }
}
