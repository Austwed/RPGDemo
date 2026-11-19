using System;
using BaseConquest.Base;
using BaseConquest.Battle;
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

        // GPU check: compare the gameplay character layer against the same frame with just the occupation visible.
        public static void CheckRenderedPawns()
        {
            const int size = 192;
            var state = JsonUtility.FromJson<BaseState>(Resources.Load<TextAsset>("DemoStages/camp-male").text);
            void At(string id)
            {
                var place = BaseSpatial.Objects(state).Single(obj => obj.Id == id);
                var p = place.Blocks ? BaseStageWalk.Door(state,id) : new BasePoint(place.X,place.Y);
                state.PlayerX = p.X; state.PlayerY = p.Y; BaseAdventure.Arrive(state);
            }
            At("home"); Need(BaseDemo.AcknowledgeWaking(state),"营地苏醒失败");
            At("exit_east"); Need(BaseCampaign.Travel(state,"town"),"进镇失败");
            At("demo_ruins_exit"); Need(BaseCampaign.Travel(state,"outpost"),"到达古窟失败");
            At("demo_ruins_investigate"); var session = BaseDemoRuins.Begin(state,"none");
            var civilians = BaseDemoRuins.Civilians(session).ToArray();
            Need(civilians.Length == 2 && civilians.All(s => s.AppearanceId == "villager"),"古窟建模时两名受控者须已绑定村民");

            var stage = new GameObject("Combat portrait rendering test");
            var camera = stage.AddComponent<Camera>();
            var target = RenderTexture.GetTemporary(size,size,24);
            var old = RenderTexture.active;
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var image = new Texture2D(size,size,TextureFormat.RGB24,false);
            var sheet = new Texture2D(size*4,size*3,TextureFormat.RGB24,false);
            var center = Vector3.right*1500;
            camera.enabled = false;
            camera.transform.SetPositionAndRotation(center+new Vector3(0,15,.84f),Quaternion.Euler(90,0,0));
            camera.targetTexture = target; camera.orthographic = true; camera.orthographicSize = 1.75f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.21f,.23f,.18f);
            Color32[] Capture()
            {
                camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0,0,size,size),0,0); image.Apply();
                return image.GetPixels32();
            }
            void Keep(int col,int row,Color32[] pixels) => sheet.SetPixels32(col*size,(2-row)*size,size,size,pixels);
            BattlePawn2D Create(BattleSoldier soldier)
            {
                var go = new GameObject(soldier.AppearanceId); go.transform.SetParent(stage.transform,true);
                var pawn = go.AddComponent<BattlePawn2D>(); pawn.Initialize(soldier,material);
                if(soldier.Kind==SoldierKind.Hero)Need(pawn.EnableCompanion(soldier.AppearanceId),"未取得骑乘角色");
                return pawn;
            }
            int Delta(Color32 a,Color32 b) => Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b);
            try
            {
                var directions = new[]{new PrototypePoint(0,-1),new PrototypePoint(0,1),new PrototypePoint(-1,0),new PrototypePoint(1,0)};
                for(int row=0;row<2;row++)
                {
                    var soldier = new BattleSoldier { AppearanceId=row==0?"azhu":"rider", Kind=row==0?SoldierKind.Hero:SoldierKind.Cavalry,
                        Side=row==0?BattleSide.A:BattleSide.B, Stats=new SoldierStats(), Health=100 };
                    var pawn = Create(soldier);
                    for(int col=0;col<directions.Length;col++)
                    {
                        var direction = directions[col];
                        var actor = new PrototypeUnit{ Facing=direction,FootprintLength=2,Side=row==0?PrototypeSide.Ally:PrototypeSide.Enemy,Health=100};
                        pawn.SyncRealtime(session.Battle,actor,0,center);
                        var graphic = pawn.transform.Find("PixelDemoCharacter").GetComponent<Renderer>();
                        var ground = pawn.transform.Find("GroundOccupation").GetComponent<Renderer>();
                        var bounds = ground.bounds;
                        bool vertical=direction.Z!=0;
                        Need(Math.Abs(bounds.center.x-center.x)<.001f && Math.Abs(bounds.center.z-center.z)<.001f &&
                            Math.Abs(bounds.size.x-2*actor.BodyHalf.X*BattleBoardView.CellSize)<.001f &&
                            Math.Abs(bounds.size.z-2*actor.BodyHalf.Z*BattleBoardView.CellSize)<.001f,"阵营色偏离实际双格碰撞范围");
                        var dressed=Capture(); Keep(col,row,dressed); graphic.enabled=false; var bare=Capture(); graphic.enabled=true;
                        var vp=camera.WorldToViewportPoint(center);
                        int groundY=Mathf.FloorToInt(vp.y*size), mid=Mathf.FloorToInt(vp.x*size);
                        var tint=bare[groundY*size+mid];
                        Need(row==0?tint.b>tint.r+16:tint.r>tint.b+16,"离屏阵营色不正确");
                        int bottom=-1;
                        for(int y=0;y<size&&bottom<0;y++)
                            for(int x=mid-22;x<=mid+22;x++)
                                if(Delta(dressed[y*size+x],bare[y*size+x])>36){bottom=y;break;}
                        float hoof=(bottom+.5f-groundY)*2*camera.orthographicSize/size/BattleBoardView.CellSize;
                        Need(bottom>=0 && (vertical?hoof>-.5f && hoof<-.02f:Math.Abs(hoof)<.22f),
                            soldier.AppearanceId+" 朝向"+direction.X+","+direction.Z+"，蹄底偏移 "+hoof+" 格");
                        Debug.Log($"[CombatArt] {soldier.AppearanceId} {direction.X},{direction.Z} hoof {hoof:F3} cells");
                    }
                    UnityEngine.Object.DestroyImmediate(pawn.gameObject);
                }

                var run=new RealtimeCampaignBattle(session);
                var pawns=civilians.Select(Create).ToArray(); // Bind the views before the encounter callback.
                BattleScenePlacement.Snap(state,run);
                BaseDemoRuins.Configure(run);
                var controller=run.Model.Units.Single(u=>u.Side==PrototypeSide.Enemy&&u.Role==PrototypeRole.Leader);
                Need(run.Model.PlaceNearest(run.Model.Player,controller.Position+new PrototypePoint(0,-1)),
                    "未找到控制来源身旁的接战位置");
                for(int i=0;i<civilians.Length;i++)
                {
                    var actor=run.Model.Units.Single(a=>a.Id==civilians[i].Id+1);
                    Need(run.Model.PlaceNearest(actor,run.Model.Player.Position + new PrototypePoint(i==0?.6:-.6,0)),
                        "未找到村民攻击测试的相邻站位");
                }
                run.RefreshDeveloperState();run.Model.SetPaused(false);
                var swings=new int[civilians.Length];
                for(int step=0;step<2700 && swings.Any(n=>n<2) && run.Battle.Phase==BattlePhase.Fighting;step++)
                {
                    run.Tick(.02);
                    for(int i=0;i<civilians.Length;i++)
                    {
                        var actor=run.Model.Units.Single(a=>a.Id==civilians[i].Id+1);
                        if(swings[i]>=2 || actor.SwingUntil<=run.Model.Elapsed || !actor.Alive)continue;
                        foreach(var other in pawns)other.gameObject.SetActive(other==pawns[i]);
                        pawns[i].SyncRealtime(session.Battle,actor,run.Model.Elapsed,center);
                        var graphic=pawns[i].transform.Find("PixelDemoCharacter").GetComponent<Renderer>();
                        var block=new MaterialPropertyBlock();graphic.GetPropertyBlock(block);
                        Need(block.GetTexture("_BaseMap")?.name=="villager-attack-v2","受控镇民出手未绑定村民攻击图集");
                        // First windup frame and a later swing frame for each civilian.
                        if(swings[i]==1 && graphic.GetComponent<MeshFilter>().sharedMesh.uv[0].x<.5f)continue;
                        var strike=Capture(); graphic.enabled=false; var empty=Capture(); graphic.enabled=true;
                        Need(strike.Zip(empty,Delta).Count(d=>d>36)>150,"村民出手帧未显示");
                        Keep(i*2+swings[i],2,strike);swings[i]++;
                    }
                }
                Need(swings.All(n=>n==2),"没有观测到两名受控镇民完整的攻击，已播放帧数 "+string.Join(",",swings)+" @ "+run.Model.Elapsed);
                Directory.CreateDirectory("Logs/combat-art");
                sheet.Apply();File.WriteAllBytes("Logs/combat-art/riders-and-ruins.png",sheet.EncodeToPNG());
                Debug.Log("[CombatArt] PASS four directions, both factions and two controlled villager attack renders");
            }
            finally
            {
                camera.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(target);
                foreach(var item in new UnityEngine.Object[]{stage,material,image,sheet})UnityEngine.Object.DestroyImmediate(item);
            }
        }
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
