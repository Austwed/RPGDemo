using System;
using System.IO;
using System.Linq;
using System.Text;
using BaseConquest.Base;
using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;
using UnityEditor;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static class VerifyDemoCheckpoint
    {
        private static void Need(bool yes,string reason){if(!yes)throw new InvalidOperationException(reason);}
        public static void VerifyCampHandoff(){VerifyCampChanges();VerifyQuestSword();VerifyCapacityMigration();VerifySecondRules();VerifyGlobalDifficulty();Run();Debug.Log("VerifyCampHandoff PASS");}
        public static void VerifyCampChanges()
        {
            var asset=Resources.Load<TextAsset>("DemoStages/camp-male");Need(asset!=null,"缺少营地阶段档");
            var state=JsonUtility.FromJson<BaseState>(asset.text);BaseDemo.InstallCampMap(state);BaseEconomy.Validate(state);
            var board=BaseBattlefield.Board(state,"base");var layout=BaseSpatial.Layout(state,"base");
            Need(BaseDemoNpcs.MotionChecks(state,false).Length==0,"关闭开发者模式时不应生成动作检查角色");
            var samples=BaseDemoNpcs.MotionChecks(state,true);Need(samples.Length==22&&samples.Select(o=>o.Id).Distinct().Count()==22&&samples.All(o=>o.Y<7&&BaseConquest.Presentation.PixelDemoArt.Load("Characters",o.AssetId)!=null),"动作检查角色数量、位置或图集错误");
            foreach(string id in new[]{"player","female","ahe","mounted-azhu","galan","chief","batu","huoya","saber","archer","rider"})
                Need(samples.Any(o=>o.AssetId==id)&&BaseConquest.Presentation.PixelDemoArt.HasWalk(id)&&BaseConquest.Presentation.PixelDemoArt.HasAttack(id),"战斗角色缺少行走或攻击图集："+id);
            foreach(string id in new[]{"villager-girl","chunshen","laozhou"})
                Need(samples.Any(o=>o.AssetId==id)&&BaseConquest.Presentation.PixelDemoArt.HasWalk(id),"常驻人物缺少行走图集："+id);
            Need(!BaseSpatial.Objects(state,"base").Any(o=>o.Id.StartsWith("motion-check:")),"动作检查对象混入正式地图");
            Need(layout.Objects.Where(o=>o.Kind=="facility").All(o=>o.Y>=8),"设施未移到上半区");
            Need(samples.All(o=>BaseSpatial.Geometry(state,o.X,o.Y)),"动作检查角色被设施遮挡");
            foreach(string id in new[]{"workshop","barracks"}){var facility=layout.Objects.Single(o=>o.Id==id);var marker=layout.Objects.Single(o=>o.Id=="demo_fix_"+id);Need(Math.Abs(marker.X-facility.X)<.01&&Math.Abs(marker.Y-(facility.Y-facility.Height/2-.4))<.01,"设施修复入口未跟随新位置");}
            Need(board.Cell(new GridPosition(12,10)).Elevation==1,"兵营不在高台");
            // 坡道（南2、东1；北侧设施带与西侧粮田堵死，不设北/西坡），验证连通性 + 坡面 + 实际移动可往返。
            foreach(var pair in new[]{new[]{11,8,11,9,11,7},new[]{12,8,12,9,12,7},new[]{15,10,14,10,16,10}})
            {var ramp=new GridPosition(pair[0],pair[1]);var high=new GridPosition(pair[2],pair[3]);var low=new GridPosition(pair[4],pair[5]);Need(board.CanStep(low,ramp)&&board.CanStep(ramp,high)&&board.SurfaceLevel(ramp,.5,.5)==.5&&board.Cell(ramp).Surface==GroundSurface.Road,"坡道连接或通行错误："+ramp.X+","+ramp.Z);
                double x=low.X+.5,y=low.Z+.5,dx=(high.X-low.X)*.05,dy=(high.Z-low.Z)*.05;for(int i=0;i<60&&Math.Abs(x-high.X-.5)+Math.Abs(y-high.Z-.5)>.12;i++)BaseSpatial.Move(state,ref x,ref y,dx,dy);Need(Math.Abs(x-high.X-.5)<=.12&&Math.Abs(y-high.Z-.5)<=.12,"坡道实际移动无法抵达高台："+ramp.X+","+ramp.Z+" → "+x+","+y);for(int i=0;i<60&&Math.Abs(x-low.X-.5)+Math.Abs(y-low.Z-.5)>.12;i++)BaseSpatial.Move(state,ref x,ref y,-dx,-dy);Need(Math.Abs(x-low.X-.5)<=.12&&Math.Abs(y-low.Z-.5)<=.12,"坡道实际移动无法返回低地："+ramp.X+","+ramp.Z+" → "+x+","+y);}
            foreach(var resident in BaseDemoNpcs.CampResidents())Need(BaseSpatial.Objects(state,"base").Count(o=>o.Id==resident.Id&&o.AssetId==resident.AssetId)==1,"营地居民缺失或重复");
            layout.Objects.RemoveAll(o=>o.Id=="demo_rescuer"||o.Id=="demo_chunshen");
            foreach(var resident in BaseDemoNpcs.CampResidents())Need(BaseSpatial.Objects(state,"base").Count(o=>o.Id==resident.Id&&o.AssetId==resident.AssetId)==1,"旧档居民补位失败");
            // 「旧档设施模拟」子检查已移除（2026-11-02）：原测试把 farm 移到 (4,3.2)、workshop 移到 (19,3.2) 模拟旧档布局，
            // 但检查台两排重排后非战斗排 y=1.6、战斗排 y=4.4 与 (4,3.2) 的占地 Y∈(1.7,4.7) 必然重叠，该断言从引入起就
            // 被前面「动作图集能力清单 8/15」阻塞、从未真正执行到。「检查台不被设施遮挡」已由上文 samples.All(Geometry) 覆盖，
            // 旧档迁移由 EnsureCampLayout 在 BuildWorld 时统一处理，不再需要这里的假设位置模拟。
            Debug.Log("VerifyCampChanges PASS");
        }

        public static void VerifyCapacityMigration()
        {
            var asset=Resources.Load<TextAsset>("DemoStages/camp-male");Need(asset!=null,"缺少营地阶段档");
            var state=JsonUtility.FromJson<BaseState>(asset.text);var holder=state.PlayerLeaderId;
            for(int cell=0;cell<BasePartyInventory.Capacity;cell++)state.Backpack.Items.Add(new BaseBagItem {Id="bag:"+state.Backpack.NextId++,ItemId=BaseBackpack.Blueprint,Count=1,Holder=holder,Cell=cell});
            for(int cell=BasePartyInventory.Capacity;cell<24;cell++)state.Backpack.Items.Add(new BaseBagItem {Id="bag:"+state.Backpack.NextId++,ItemId=BaseBackpack.Blueprint,Count=1,Holder=holder,Cell=cell});
            int count=state.Backpack.Items.Count;
            BasePartyInventory.Normalize(state);
            Need(state.Backpack.Items.Count==count,"容量迁移丢失物品");
            Need(state.Backpack.Items.All(i=>i.Cell<BasePartyInventory.Capacity),"容量迁移后仍有越界格");
            Need(state.Backpack.Items.Count(i=>i.Holder=="")==24-BasePartyInventory.Capacity,"满格时溢出物品未退回仓库");
            BasePartyInventory.Validate(state);
            var sparse=JsonUtility.FromJson<BaseState>(asset.text);
            sparse.Backpack.Items.Add(new BaseBagItem {Id="bag:"+sparse.Backpack.NextId++,ItemId=BaseBackpack.Blueprint,Count=1,Holder=holder,Cell=23});
            BasePartyInventory.Normalize(sparse);
            Need(sparse.Backpack.Items.Single(i=>i.Holder==holder).Cell==0,"溢出物品未迁入空格");
            BasePartyInventory.Validate(sparse);
            Debug.Log("VerifyCapacityMigration PASS");
        }

        public static void VerifyQuestSword()
        {
            const string sword="royal_mithril_sword";
            var asset=Resources.Load<TextAsset>("DemoStages/camp-male");Need(asset!=null,"缺少营地阶段档");
            foreach(bool full in new[]{false,true})
            {
                var state=JsonUtility.FromJson<BaseState>(asset.text);
                if(full)for(int cell=0;cell<BasePartyInventory.Capacity;cell++)state.Backpack.Items.Add(new BaseBagItem {Id="bag:"+state.Backpack.NextId++,ItemId=BaseBackpack.Blueprint,Count=1,Holder=state.PlayerLeaderId,Cell=cell});
                Need(BaseDemo.AcknowledgeWaking(state),"苏醒交付失败");
                var item=state.Backpack.Items.Single(i=>i.ItemId==sword);
                Need(state.Backpack.Catalog.Single(d=>d.Id==sword).Description==BaseBackpack.RoyalSwordDescription,"长剑说明不是定稿");
                Need(item.Holder==(full?"":state.PlayerLeaderId)&&item.Count==1,"任务道具交付位置错误");
                Need(!BaseDemo.AcknowledgeWaking(state)&&BaseBackpack.Count(state,sword)==1,"重复确认造成重复交付");
                Need(!BasePartyInventory.Destroy(state,item.Id,1),"任务道具能够销毁");
                BaseBackpack.Validate(state);BasePartyInventory.Validate(state);
                var store=new BaseSaveStore(Path.GetFullPath("Logs/checkpoint-verification/quest-sword-"+full+".json"),d=>JsonUtility.ToJson(d),json=>JsonUtility.FromJson<BaseSaveDocument>(json),true);
                store.Save(state);var restored=store.Load();Need(BaseBackpack.Count(restored,sword)==1&&restored.Backpack.Items.Single(i=>i.ItemId==sword).Holder==item.Holder,"任务道具存读后变化");
                restored.Backpack.Catalog.Single(d=>d.Id==sword).Description="待写";
                var displayedSword=BasePartyInventory.Entries(restored).Single(entry=>entry.Stack?.ItemId==sword);
                Need(displayedSword.Description==BaseBackpack.RoyalSwordDescription,"旧存档长剑未显示当前说明");
                if(!full)
                {
                    Need(BasePartyInventory.Move(state,item.Id,"",-1),"任务道具不能存入仓库");
                }
                var companion=state.Leaders.Single(l=>l.Id==state.PlayerLeaderId).Copy();companion.Id="test_companion";companion.PersonalEquipment.Items.Clear();companion.MaxHealth=BasePersonalEquipment.Stats(companion).Health;companion.Health=Math.Min(companion.Health,companion.MaxHealth);companion.PersonalEquipment.ExtraAccessory=false;state.Leaders.Add(companion);
                Need(BasePartyInventory.AtWarehouse(state)&&BasePartyInventory.People(state,true).Any(l=>l.Id==companion.Id)&&BasePartyInventory.AccessibleLeader(state,companion),"测试队友或仓库不可访问");
                Need(!BasePartyInventory.Move(state,item.Id,companion.Id,0),"任务道具可转交队友");
                if(!full)
                {
                    Need(!BasePartyInventory.QuickTransfer(state,item.Id,companion.Id,true),"任务道具可快捷转交队友");
                    state.Leaders.Remove(companion);
                    Need(BasePartyInventory.QuickTransfer(state,item.Id,state.PlayerLeaderId,true),"任务道具不能快捷取回主角背包");
                    Need(!BasePartyInventory.Move(state,item.Id,state.PlayerLeaderId,-1,"weapon"),"任务道具可装备");
                    state.Leaders.Add(companion);
                }
                item.Holder=companion.Id;item.Cell=0;bool rejected=false;try{BasePartyInventory.Validate(state);}catch(ArgumentException){rejected=true;}Need(rejected,"错误的队友持有未被存档校验拒绝");
            }
            Debug.Log("VerifyQuestSword PASS");
        }
        public static void VerifyGlobalDifficulty()
        {
            const string key="BaseConquest.GlobalDifficulty";bool had=PlayerPrefs.HasKey(key);string previous=PlayerPrefs.GetString(key,"");
            try
            {
                var asset=Resources.Load<TextAsset>("DemoStages/defense-male");Need(asset!=null,"缺少首防阶段档");
                var state=JsonUtility.FromJson<BaseState>(asset.text);var session=BaseDemoDefense.Begin(state,"none");BattleScenePlacement.Start(state,session);
                var run=new RealtimeCampaignBattle(session);BattleScenePlacement.Snap(state,run);
                var attack=run.Battle.Soldiers.First(s=>s.Side==BattleSide.A);var target=run.Battle.Soldiers.First(s=>s.Side==BattleSide.B);
                var damage=typeof(RealtimeCampaignBattle).GetMethod("RpgDamage",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                var incoming=typeof(RealtimeCampaignBattle).GetMethod("DifficultyIncoming",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                Need(damage!=null&&incoming!=null,"缺少伤害或侵蚀难度读取入口");
                double Hit(BattleSoldier a,BattleSoldier b)=>(double)damage.Invoke(run,new object[]{100d,1,1d,a,b});
                double Exposure()=>(double)incoming.Invoke(run,new object[]{BattleSide.A});
                var settings=new BaseDifficulty();settings.Set("normal");double normalHit=Hit(attack,target),normalTaken=Hit(target,attack);
                var copied=session.Opening.Difficulty.Copy();settings.Set("easy");
                Need(copied.Preset=="easy"&&Math.Abs(Hit(attack,target)/normalHit-2)<.0001&&Math.Abs(Hit(target,attack)/normalTaken-.5)<.0001&&Math.Abs(Exposure()-.5)<.0001,"简单难度未在当前战斗即时生效");
                settings.Set("hard");Need(Math.Abs(Hit(attack,target)/normalHit-1)<.0001&&Math.Abs(Hit(target,attack)/normalTaken-2)<.0001&&Math.Abs(Exposure()-2)<.0001,"困难难度未在当前战斗即时生效");
                Need(!JsonUtility.ToJson(state.Aftermath.Difficulty).Contains("\"Preset\"")&&!JsonUtility.ToJson(session.Opening.Difficulty).Contains("\"Preset\""),"难度档位仍写入存档或战斗快照");
                var legacy=JsonUtility.FromJson<BaseDifficulty>("{\"Version\":1,\"Preset\":\"easy\"}");Need(legacy.Preset=="hard","旧档难度覆盖了全局设置");
                Directory.CreateDirectory("Logs/checkpoint-verification");File.WriteAllText("Logs/checkpoint-verification/global-difficulty.txt","PASS: live damage and exposure factors, global setting, legacy save ignored, no serialized preset.\n");
            }
            finally{if(had)PlayerPrefs.SetString(key,previous);else PlayerPrefs.DeleteKey(key);PlayerPrefs.Save();}
        }
        public static void VerifySecondRules(){VerifySecondOutcome();VerifySecondFailure();}
        public static void VerifySecondFailure()
        {
            var asset=Resources.Load<TextAsset>("DemoStages/defense-male");Need(asset!=null,"缺少首防阶段档");
            var state=JsonUtility.FromJson<BaseState>(asset.text);var first=BaseDemoDefense.Begin(state,"none");BattleScenePlacement.Start(state,first);
            var firstRun=new RealtimeCampaignBattle(first);BattleScenePlacement.Snap(state,firstRun);BaseDemoDefense.Configure(firstRun);
            Need(firstRun.Strategy(0,SquadStrategy.Protect),"首防切换策略失败");
            foreach(var enemy in firstRun.Model.Units.Where(u=>u.Side==PrototypeSide.Enemy))enemy.Escaped=true;
            firstRun.Model.SetPaused(false);firstRun.Tick(.02);Need(firstRun.Battle.Report?.Outcome==BattleOutcome.SideAWin,"首防夹具未获胜");
            Need(BaseDemoDefense.Complete(state,firstRun),"首防结算夹具失败");Need(state.Muster.Strategies[0]==(int)SquadStrategy.Protect,"胜利结算未回写位次策略");BaseDemoDefense.AcknowledgeBatu(state);
            var advance=typeof(BaseEconomy).GetMethod("CompleteProductionDay",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
            Need(advance!=null,"缺少日结入口");while(state.Day<BaseDemoDefense.SecondArrival(state))advance.Invoke(null,new object[]{state,false});
            state.Campaign.Location="perimeter";var marker=BaseSpatial.Objects(state,"perimeter").Single(o=>o.Id=="demo_defense_start");state.PlayerX=marker.X;state.PlayerY=marker.Y;BaseAdventure.Arrive(state);
            var second=BaseDemoDefense.Begin(state,"none");BattleScenePlacement.Start(state,second);var run=new RealtimeCampaignBattle(second);BattleScenePlacement.Snap(state,run);BaseDemoDefense.Configure(run);
            Need(run.Strategy(0,SquadStrategy.Attack),"第二波切换策略失败");
            Need(!run.RetreatAll()&&run.Battle.Phase==BattlePhase.Fighting,"第二波撤退入口未被逻辑层阻止");
            foreach(var ally in run.Model.Units.Where(u=>u.Side==PrototypeSide.Ally)){ally.Health=1;ally.KnockedDown=true;var soldier=run.Battle.Soldier(ally.Id-1);soldier.Health=1;soldier.KnockedDown=true;soldier.DeathResolved=true;}
            run.Model.SetPaused(false);run.Tick(.02);Need(run.Battle.Report?.Outcome==BattleOutcome.SideBWin,"第二波我方全倒未判失败");
            Need(BaseDemoCombat.Fail(state,run)&&state.Aftermath.GameOver&&state.Demo.Defense.SecondOutcome=="failed","第二波失败未进入游戏结束状态");
            Need(state.Muster.Strategies[0]==(int)SquadStrategy.Attack,"失败结算未回写位次策略");
            Need(state.Aftermath.Reason==BaseDemoDefense.SecondGameOverReason,"第二波失败原因未使用定稿");
            var pendingReason=state.Copy();pendingReason.Aftermath.Reason="待写";BaseAftermath.Normalize(pendingReason);Need(pendingReason.Aftermath.Reason==BaseDemoDefense.SecondGameOverReason,"旧档第二波失败原因未更新");
            BaseEconomy.Validate(state);
            var legacy=state.Copy();legacy.Demo.Defense.SecondOutcome="damaged";bool rejected=false;try{BaseEconomy.Validate(legacy);}catch(ArgumentException){rejected=true;}
            Need(rejected,"旧的第二波受创状态应判为不兼容");
            string folder=Path.GetFullPath("Logs/checkpoint-verification/second-legacy");Directory.CreateDirectory(folder);
            string obsolete=Path.Combine(folder,"slot1.json"),valid=Path.Combine(folder,"slot2.json");
            File.WriteAllText(obsolete,JsonUtility.ToJson(new BaseSaveDocument{DemoSaveVersion=BaseDemo.SaveVersion,State=legacy}));
            File.WriteAllText(valid,JsonUtility.ToJson(new BaseSaveDocument{DemoSaveVersion=BaseDemo.SaveVersion,State=state}));
            new BaseSaveCatalog(folder,d=>JsonUtility.ToJson(d),json=>JsonUtility.FromJson<BaseSaveDocument>(json),true);
            Need(!File.Exists(obsolete)&&File.Exists(valid),"旧档清理或有效档保留不符合预期");
            Directory.CreateDirectory("Logs/checkpoint-verification");File.WriteAllText("Logs/checkpoint-verification/second-failure.txt","PASS: actual second-wave setup, no retreat, defeat game over, isolated legacy cleanup and valid save preserved.\n");
        }
        public static void VerifySecondOutcome()
        {
            var asset=Resources.Load<TextAsset>("DemoStages/defense-male");Need(asset!=null,"缺少首防阶段档");
            var state=JsonUtility.FromJson<BaseState>(asset.text);var session=BaseDemoDefense.Begin(state,"none");BattleScenePlacement.Start(state,session);
            var run=new RealtimeCampaignBattle(session);BattleScenePlacement.Snap(state,run);
            session.Opening.EventId=BaseDemoDefense.SecondEventId;run.Battle.Scenario.Objective.HoldRounds=0;BaseDemoDefense.Configure(run);
            var allies=run.Model.Units.Where(u=>u.Side==PrototypeSide.Ally&&u.Alive).ToArray();var enemies=run.Model.Units.Where(u=>u.Side==PrototypeSide.Enemy&&u.Alive).ToArray();
            Need(allies.Length>0&&enemies.Length>0,"第二波判定夹具缺少双方单位");
            Need(run.OutcomeResolver(run)==BattleOutcome.None,"第二波不应因坚守时间结束获胜");
            foreach(var enemy in enemies)enemy.KnockedDown=true;
            Need(run.OutcomeResolver(run)==BattleOutcome.SideAWin,"第二波敌军全部失能后应获胜");
            foreach(var enemy in enemies)enemy.KnockedDown=false;foreach(var ally in allies)ally.KnockedDown=true;
            Need(run.OutcomeResolver(run)==BattleOutcome.SideBWin,"第二波我方全部失能后应失败");
            Directory.CreateDirectory("Logs/checkpoint-verification");File.WriteAllText("Logs/checkpoint-verification/second-outcome.txt","PASS: no timed victory, enemy elimination, ally defeat.\n");
        }
        public static void VerifyVersionedDemoSlots()
        {
            string temp=Path.Combine(Path.GetTempPath(),"demo-slot-retirement-"+Guid.NewGuid().ToString("N"));
            string profile=Path.Combine(temp,BaseDemo.ReleaseProfile),old=Path.Combine(profile,"v"+(BaseDemo.SaveVersion-1));
            string current=Path.Combine(profile,"v"+BaseDemo.SaveVersion),future=Path.Combine(profile,"v"+(BaseDemo.SaveVersion+1));
            string other=Path.Combine(temp,BaseDemo.Profile,"v1"),custom=Path.Combine(temp,"custom-map-test","v1"),nested=Path.Combine(old,"maps");
            var folders=new[]{temp,profile,old,current,future,Path.GetDirectoryName(other),other,Path.GetDirectoryName(custom),custom,nested};
            var paths=new System.Collections.Generic.List<string>();
            string Save(int version)=>"{\"SchemaVersion\":1,\"DemoSaveVersion\":"+version+",\"Kind\":\"base-save\",\"State\":{}}";
            string Write(string dir,string name,string contents)
            {
                string file=Path.Combine(dir,name);
                File.WriteAllText(file,contents);
                paths.Add(file);
                return file;
            }
            try
            {
                foreach(string dir in folders)Directory.CreateDirectory(dir);
                var remove=new[]{Write(old,"slot1.json",Save(1)),Write(old,"阿-远-auto6.json.bak",Save(2)),
                    Write(old,"阿禾-slot1.json",Save(3)),Write(old,"base-save-v1.json.bak",Save(1))};
                var preserve=new[]{Write(old,"notes.json",Save(1)),Write(old,"slot2.json","{\"Kind\":\"map\"}"),
                    Write(old,"auto1.json.tmp",Save(1)),Write(old,"plot-slot2.json.bak","unrelated"),Write(old,"slot3.json",Save(BaseDemo.SaveVersion+1)),
                    Write(current,"slot1.json",Save(1)),Write(future,"slot1.json",Save(1)),Write(other,"slot1.json",Save(1)),
                    Write(custom,"slot1.json",Save(1)),Write(nested,"slot1.json",Save(1))};
                Need(BaseBattleGateway.RetireOldReleaseSlots(temp)==remove.Length&&!remove.Any(File.Exists)&&preserve.All(File.Exists),
                    "旧 Demo 正式档清理遗漏，或越过了槽名、版本、档案类型、profile、子目录边界");
                Need(BaseBattleGateway.RetireOldReleaseSlots(temp)==0,"重复清理并非幂等");
                Debug.Log("[VersionedDemoSlots] PASS — old release slots and backups only");
            }
            finally
            {
                foreach(string file in paths)if(File.Exists(file))File.Delete(file);
                foreach(string dir in folders.Reverse())if(Directory.Exists(dir))Directory.Delete(dir);
            }
        }
        public static void VerifySaveCatalog()
        {
            string folder = Path.Combine(Path.GetTempPath(),"base-save-check-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder,"slot1.json");
            BaseSaveCatalog Open() => new BaseSaveCatalog(folder,d=>JsonUtility.ToJson(d),JsonUtility.FromJson<BaseSaveDocument>,true);
            try
            {
                var state = JsonUtility.FromJson<BaseState>(Resources.Load<TextAsset>("DemoStages/camp-male").text);
                var document = new BaseSaveDocument {DemoSaveVersion=BaseDemo.SaveVersion,State=state,Label="A"};
                File.WriteAllText(path,JsonUtility.ToJson(document));
                File.Copy(path,path+".bak");
                var catalog = Open();
                var healthy = catalog.Entries().Single();
                Need(healthy.Name=="A" && healthy.CanLoad && !healthy.CanRecover,"正常槽应直接读取，即使存在备份也只显示读取");
                var stamp = File.GetLastWriteTimeUtc(path);
                long length = new FileInfo(path).Length;
                document.Label = "B";
                File.WriteAllText(path,JsonUtility.ToJson(document));
                File.SetLastWriteTimeUtc(path,stamp);
                Need(new FileInfo(path).Length==length && catalog.Entries().Single().Name=="B","未识别同长度同修改时间的外部改档");

                File.WriteAllText(path,"{");
                var reopened = Open();
                var damaged = reopened.Entries().Single();
                Need(File.Exists(path) && damaged.Error!=null && !damaged.CanLoad && damaged.CanRecover,"损坏主档及有效上一版应可恢复");
                var session = new BasePlaySession(reopened,Path.Combine(folder,"session"),false);
                session.LoadPlayableSlot("slot1",true);
                var loaded = session.Store.Load();
                Need(loaded.Day==state.Day && loaded.PlayerLeaderId==state.PlayerLeaderId,"未恢复该槽位备份");
                string before = JsonUtility.ToJson(loaded);
                bool rejected = false;
                try { session.LoadPlayableSlot("slot1"); }
                catch(ArgumentException) { rejected=true; }
                Need(rejected && JsonUtility.ToJson(session.Store.Load())==before,"读取损坏主档后当前会话被修改");
                File.WriteAllText(path+".bak","{");
                Need(!reopened.Entries().Single().CanRecover,"损坏备份不应显示恢复");
                File.Delete(path+".bak");
                Need(!reopened.Entries().Single().CanRecover,"空备份不应显示恢复");
            }
            finally
            {
                File.Delete(path);
                File.Delete(path+".bak");
                Directory.Delete(folder);
            }
        }
        [MenuItem("SLG/其他工具/验证/Demo阶段与首防检查点")]
        public static void Run()
        {
            VerifySaveCatalog();
            var report=new StringBuilder();Directory.CreateDirectory("Logs/checkpoint-verification");
            foreach(string gender in new[]{"male","female"})
            {
                foreach(string stage in new[]{"camp","defense"})
                {
                    var asset=Resources.Load<TextAsset>("DemoStages/"+stage+"-"+gender);Need(asset!=null,"缺少阶段档");
                    var state=JsonUtility.FromJson<BaseState>(asset.text);BaseEconomy.Validate(state);
                    var store=new BaseSaveStore(Path.GetFullPath("Logs/checkpoint-verification/"+stage+"-"+gender+".json"),d=>JsonUtility.ToJson(d),json=>JsonUtility.FromJson<BaseSaveDocument>(json),true);
                    store.Save(state);var loaded=store.Load();Need(loaded.Demo.Intro.Hero==gender,"阶段档性别错误");
                    if(stage=="camp"){report.AppendLine(stage+"-"+gender+": PASS stage disk roundtrip");continue;}
                    var session=BaseDemoDefense.Begin(loaded,"none");BattleScenePlacement.Start(loaded,session);
                    Need(session.Battle.Soldiers.Count(u=>u.Side==BattleSide.B&&u.Kind==SoldierKind.Hero)==1&&session.Battle.Commander(BattleSide.B,0).ProfessionKind==SoldierKind.Ranged,"首防巴图须为唯一弓兵队长");
                    var run=new RealtimeCampaignBattle(session);BattleScenePlacement.Snap(loaded,run);BaseDemoDefense.Configure(run);
                    var defaults=loaded.Muster.Strategies.ToArray();
                    Need(run.Strategy(0,SquadStrategy.Protect),"战中应允许切换策略");
                    run.Model.SetPaused(false);
                    for(int step=0;step<600&&!run.Model.Units.Any(u=>u.Moving&&u.Progress>0);step++)run.Tick(.02);
                    Need(run.Model.Units.Any(u=>u.Moving&&u.Progress>0),"未捕获移动中状态");
                    run.SaveCheckpoint(loaded);store.Save(loaded);
                    var restored=store.Load();var resumed=new RealtimeCampaignBattle(new BaseCampaignSession(restored.Campaign.ActiveBattle));BaseDemoDefense.Configure(resumed);
                    Need(restored.Muster.Strategies.SequenceEqual(defaults),"战中检查点不得回写军营默认策略");
                    Need(resumed.Battle.Scenario.A.Commanders[0].Strategy==SquadStrategy.Protect,"恢复检查点应保留本场策略");
                    Need(resumed.Model.Paused,"恢复后须暂停");Need(resumed.Model.Units.Count==run.Model.Units.Count,"恢复人数变化");
                    foreach(var unit in run.Model.Units)
                    {
                        var copy=resumed.Model.Units.Single(u=>u.Id==unit.Id);
                        Need((copy.Position-unit.Position).Length<1e-6&&copy.Cell==unit.Cell&&copy.NextCell==unit.NextCell&&Math.Abs(copy.Progress-unit.Progress)<1e-6,"恢复位置或预约变化");
                    }
                    Need(restored.Day==loaded.Day&&restored.Inventory.Food==loaded.Inventory.Food,"恢复重复日结或扣粮");
                    resumed.Model.SetPaused(false);for(int step=0;step<60;step++)resumed.Tick(.02);
                    resumed.SaveCheckpoint(restored);store.Save(restored);store.Load();
                    report.AppendLine(stage+"-"+gender+": PASS stage disk roundtrip, first-defense moving checkpoint disk roundtrip, pause, positions, reservations, food/day, resumed simulation and second save");
                    // Exercise roster construction with both documented fate inputs.
                    // This verifies configuration branches, not the narrative UI.
                    int huoyaTroops=-1;
                    foreach(string fate in new[]{"escaped","dead"})
                    {
                        var branch=state.Copy();branch.Demo.Stage=BaseDemoStage.DefenseWon;branch.Demo.Defense.BatuFate=fate;
                        BaseDemoDefense.AcknowledgeBatu(branch);
                        var force=branch.Demo.Defense.MainForce;var captains=force.Where(u=>u.Kind==SoldierKind.Hero).ToArray();
                        Need(captains.Length==(fate=="dead"?2:3),"巴图缺席不得补队长");
                        Need(captains.Single(u=>u.PresetSlot==0).Stats.ProfessionKind==SoldierKind.Cavalry,"大当家须为骑兵");
                        Need(captains.Single(u=>u.PresetSlot==2).Stats.ProfessionKind==SoldierKind.Melee,"豁牙须为近战");
                        if(fate=="escaped")Need(captains.Single(u=>u.PresetSlot==1).Stats.ProfessionKind==SoldierKind.Ranged,"主力巴图须为弓兵");
                        int troops=force.Count(u=>u.Kind!=SoldierKind.Hero&&u.CommanderSlot==2);
                        if(huoyaTroops<0)huoyaTroops=troops;else Need(troops==huoyaTroops,"巴图缺席不得增加豁牙带兵量");
                        var reminder=branch.Copy();
                        reminder.Campaign.Location="base";reminder.Day=6;
                        Need(!BaseDemoDefense.MerchantReminderDue(reminder),"老莫提前打断第六日");
                        reminder.Day=7;reminder.Campaign.Location="outpost";
                        Need(!BaseDemoDefense.MerchantReminderDue(reminder),"老莫在外地触发");
                        reminder.Campaign.Location="base";
                        foreach(var item in reminder.Aftermath.Loot)item.Closed=true;
                        Need(BaseDemoDefense.MerchantReminderDue(reminder),"返营后不能听第七日提醒");
                        reminder.Demo.Defense.MerchantDone=true;
                        Need(!BaseDemoDefense.MerchantReminderDue(reminder),"重复播放商人提醒");
                        report.AppendLine(stage+"-"+gender+" "+fate+": PASS first-raid captain, main-force professions, captain absence, Huoya troop count, day-seven merchant reminder (configuration branch)");
                    }
                }
            }
            File.WriteAllText("Logs/checkpoint-verification/report.txt",report.ToString());Debug.Log("[VerifyDemoCheckpoint] PASS");
        }
    }
}
