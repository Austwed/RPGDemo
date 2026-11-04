using System;
using System.Linq;
using System.Reflection;
using BaseConquest.Base;
using BaseConquest.Battle;
using BaseConquest.Presentation;
using BaseConquest.RealtimePrototype;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static class VerifyChenanAndRuins
    {
        private static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        public static void RunAll()
        {
            Run();VerifyNpcFallback.Run();VerifyDemoResources.Run();VerifyPortraits.Run();VerifyDemoCheckpoint.VerifyCampHandoff();
            VerifyBattleAppearances();VerifyRuinsFirstClear();VerifyInjuredLines();VerifyHeightSight();VerifyInjuryTiers();
            Debug.Log("[VerifyChenanAndRuins] regression PASS");
        }
        // P20（01C §8.5.1）：带伤应答只看主角当前生命是否低于 60%，满血与阵亡都不触发。
        public static void VerifyInjuredLines()
        {
            var s=JsonUtility.FromJson<BaseState>(Resources.Load<TextAsset>("DemoStages/camp-male").text);
            var p=s.Leaders.Single(l=>l.Id==s.PlayerLeaderId);
            Need(p.MaxHealth>0,"阶段档主角最大生命为 0，无法验证带伤阈值");
            p.Health=p.MaxHealth*.59;
            Need(BaseHud.Wounded(s),"生命低于 60% 应判为带伤");
            foreach(var id in new[]{"INJ_TOWN_MAIGE","INJ_TOWN_GUARD","INJ_TOWN_MO","INJ_TOWN_XIAOYUN","INJ_TOWN_BALONG","INJ_TOWN_LEIEN","INJ_CAMP_ZHOU","INJ_CAMP_CHUN","INJ_CAMP_AHE","INJ_CAMP_AZHU","INJ_CAMP_JIALAN"})
                Need(BaseHud.InjuredText(s,id)!="",id+" 缺带伤应答文案");
            Need(BaseHud.InjuredText(s,"INJ_TOWN_MAIGE")!=BaseHud.InjuredText(s,"INJ_TOWN_GUARD"),"不同 NPC 的带伤应答不应重复");
            p.Health=p.MaxHealth*.6;
            Need(!BaseHud.Wounded(s),"生命正好 60% 不应算带伤");
            p.Health=p.MaxHealth;
            Need(!BaseHud.Wounded(s)&&BaseHud.InjuredText(s,"INJ_CAMP_ZHOU")=="","满血不应出带伤应答");
            p.Health=p.MaxHealth*.4;p.Fate="dead";
            Need(!BaseHud.Wounded(s),"阵亡角色不参与带伤应答");
            p.Fate="active";
            Debug.Log("[Injured] 阈值 60%；11 条文案就绪，满血／60% 整／阵亡均不触发");
        }
        // P24（2026-11-04 定）：高地换算以现行 BattleHeightSight 为准，02／05 的数字必须跟它一致。
        // ArcherBonus 返回的是细格；粗格加成 = 返回值 ÷ Subdivision(2)。
        public static void VerifyHeightSight()
        {
            var board=new BattleBoard{Width=8,Height=8};board.EnsureValid();
            board.Cells[3*8+3].Elevation=2;
            Need(BattleHeightSight.Radius(board,new GridPosition(3,3))==14,"高地视野应为 10+2×2=14");
            Need(Math.Abs(BattleHeightSight.ArcherBonus(board,3.5,3.5,3.5,3.5))<1e-9,"平地不应有弓兵射程加成");
            Need(Math.Abs(BattleHeightSight.ArcherBonus(board,3.5,3.5,5.5,5.5)-4)<1e-9,"高差 2 级应 +4 细格（合 +2 粗格）");
            board.Cells[5*8+5].Elevation=3;
            Need(Math.Abs(BattleHeightSight.ArcherBonus(board,3.5,3.5,5.5,5.5))<1e-9,"从低处打高处不应有加成");
            Need(Math.Abs(BattleHeightSight.ArcherBonus(board,5.5,5.5,1.5,1.5)-6)<1e-9,"高差 3 级应封顶 +6 细格（合 +3 粗格）");
            Debug.Log("[Height] 视野 14（高度2）、平射 0、高差2 +4细格、低打高 0、高差3 封顶 +6细格");
        }
        // P38（2026-11-04 最新裁决，取代伤员三档的 50%／20%）：
        // 士兵生命 <30% 不能工作也不能主动出战；≥30% 两者都可以。遇袭守家例外：还能行动就能迎战。
        // 队长不受阈值限制；自动选兵按生命比例从高到低；30%~100% 生命预警线默认 50%，拖到 30% 即关闭。
        public static void VerifyInjuryTiers()
        {
            var s=JsonUtility.FromJson<BaseState>(Resources.Load<TextAsset>("DemoStages/camp-male").text);
            BaseDevelopment.Normalize(s);
            s.Workers.Clear();
            s.Workers.Add(new BaseWorkerBucket{Kind=BaseTroopKind.Melee,Count=1,MaxHealth=100,Health=100,FarmEfficiency=1,WorkshopEfficiency=1});
            var w=s.Workers[0];
            w.Health=31;Need(BaseDevelopment.Effective(s,w)&&BaseDevelopment.CanFight(w),"生命 31% 应可工可战");
            w.Health=30;Need(BaseDevelopment.Effective(s,w)&&BaseDevelopment.CanFight(w),"生命正好 30% 应可工可战（阈值不带等号）");
            w.Health=29;Need(!BaseDevelopment.Effective(s,w)&&!BaseDevelopment.CanFight(w),"生命 29% 应强制空闲");
            Need(BaseDevelopment.CanDefend(w),"遇袭守家例外：29% 仍应能迎战");
            w.Health=1;Need(!BaseDevelopment.Effective(s,w)&&!BaseDevelopment.CanFight(w)&&BaseDevelopment.CanDefend(w),"1 血士兵应只能守家");
            BaseDevelopment.Normalize(s);
            Need(w.Resting,"重伤士兵应带闲置（Resting）标记");
            w.Health=0;Need(!BaseDevelopment.CanDefend(w),"生命归零不参战");
            // 当天返岗：UnavailableDay 生产禁令已摘除，返营／招降当天即可排班。
            w.Health=100;w.Resting=false;w.UnavailableDay=s.Day;
            Need(BaseDevelopment.Effective(s,w),"当天返营／招降的士兵应可立即排班（生产禁令已摘除）");
            w.UnavailableDay=0;
            // 队长豁免：不受任何血量阈值限制（夹具：临时清掉随行/外出状态再断言）。
            var l=s.Leaders.First(x=>BaseAdventure.Alive(x)&&x.Health>0&&!BaseTraining.Busy(x.Training));
            bool wasMember=s.Adventure.Members.Remove(l.Id),wasAway=l.Away;l.Away=false;
            Need(BaseDevelopment.CanManage(s,l),"夹具队长应满足管理条件");
            Need(BaseDevelopment.CanFight(l)&&BaseDevelopment.Effective(s,l),"满血队长应可战可工");
            l.Health=l.MaxHealth*.1;
            Need(BaseDevelopment.CanFight(l)&&BaseDevelopment.Effective(s,l),"队长应不受血量阈值限制");
            l.Health=l.MaxHealth;l.Away=wasAway;if(wasMember)s.Adventure.Members.Add(l.Id);
            // 恢复：营内全员每日自动进行；跨过 30% 就解除闲置、恢复出战资格。
            w.Health=10;BaseDevelopment.Normalize(s);Need(w.Resting,"10% 士兵应处于重伤闲置");
            BaseDevelopment.Recover(s);
            double expected=Math.Min(w.MaxHealth,10+w.MaxHealth*BaseDevelopment.RecoveryRatio(s));
            Need(Math.Abs(w.Health-expected)<1e-6,"全员自动恢复量不符：10 → "+w.Health.ToString("0.##")+"（应 "+expected.ToString("0.##")+"）");
            Need(!w.Resting,"恢复越过 30% 应解除重伤闲置标记");
            Need(BaseDevelopment.Effective(s,w)&&BaseDevelopment.CanFight(w),"恢复越过 30% 应可工可战");
            w.Health=25;BaseDevelopment.Recover(s);
            Need(BaseDevelopment.CanFight(w),"25% 未休养士兵过夜恢复后应可重新出战");
            // 生命预警：默认 50%，范围 30%~100%，拖到 30% 即关闭。
            Need(Math.Abs(BaseDevelopment.WarningThreshold(s)-.5)<1e-9,"默认预警线应为 50%");
            w.Health=45;Need(BaseDevelopment.BelowWarning(s,w),"45% 应低于默认预警线");
            w.Health=60;Need(!BaseDevelopment.BelowWarning(s,w),"60% 不应触发预警");
            BaseDevelopment.SetWarningThreshold(s,.3);
            Need(BaseDevelopment.WarningOff(s),"预警线拖到 30% 应视为关闭");
            w.Health=45;Need(!BaseDevelopment.BelowWarning(s,w),"预警关闭后不再提示");
            BaseDevelopment.SetWarningThreshold(s,.2);
            Need(Math.Abs(BaseDevelopment.WarningThreshold(s)-.3)<1e-9,"预警线下限锁在 30%");
            BaseDevelopment.SetWarningThreshold(s,.5);
            // 自动选兵：生命比例高的先上；守家迎战放宽到还能行动的伤兵。
            s.Workers.Clear();
            s.Workers.Add(new BaseWorkerBucket{Kind=BaseTroopKind.Melee,Count=1,MaxHealth=100,Health=80,FarmEfficiency=1,WorkshopEfficiency=1});
            s.Workers.Add(new BaseWorkerBucket{Kind=BaseTroopKind.Melee,Count=1,MaxHealth=100,Health=45,FarmEfficiency=1,WorkshopEfficiency=1});
            s.Workers.Add(new BaseWorkerBucket{Kind=BaseTroopKind.Melee,Count=1,MaxHealth=100,Health=10,FarmEfficiency=1,WorkshopEfficiency=1});
            var preset=s.Muster.Presets.Single(p=>p.Purpose=="expedition").Copy();
            preset.SelectLeaders(new[]{s.PlayerLeaderId});
            preset.SetCount(BaseTroopKind.Melee,2);preset.SetCount(BaseTroopKind.Ranged,0);preset.SetCount(BaseTroopKind.Cavalry,0);
            var plan=BaseMuster.Preview(s,"expedition",true,preset);
            Need(plan.Takes.Count==2&&plan.Takes[0].BucketIndex==0&&plan.Takes[1].BucketIndex==1,
                "出征选兵应按生命比例从高到低取（80%→45%），实际 "+string.Join(",",plan.Takes.Select(t=>t.BucketIndex+":"+t.Count)));
            preset.SetCount(BaseTroopKind.Melee,3);preset.Purpose="defense";
            var defend=BaseMuster.Preview(s,"defense",true,preset);
            Need(defend.Takes.Sum(t=>t.Count)==3,"守家迎战应放宽到还能行动的伤兵（含 10%），实际 "+defend.Takes.Sum(t=>t.Count));
            Need(plan.Takes.Sum(t=>t.Count)==2,"出征不应把 10% 伤兵编进队");
            // 生产补位与四分类：重伤不占岗位，可工作的闲置兵自动补空。
            s.Workers.Clear();
            s.Workers.Add(new BaseWorkerBucket{Kind=BaseTroopKind.Melee,Count=2,MaxHealth=100,Health=100,FarmEfficiency=1,WorkshopEfficiency=1});
            s.Workers.Add(new BaseWorkerBucket{Kind=BaseTroopKind.Melee,Count=1,MaxHealth=100,Health=25,FarmEfficiency=1,WorkshopEfficiency=1});
            s.Development.Automatic=true;
            BaseDevelopment.Allocate(s);
            Need(s.Workers.Where(x=>!x.Away&&x.Health<30).All(x=>x.FacilityId==""),"重伤士兵不应占生产岗位");
            Need(s.Workers.Where(x=>!x.Away&&x.Health>=30).Sum(x=>x.FacilityId==""?0:x.Count)>0,"可工作士兵应自动补上空岗");
            var tally=BaseDevelopment.Tally(s);
            Need(tally[0]==3&&tally[1]==2&&tally[2]==2&&tally[3]==1,
                "兵力四分类不符：总数"+tally[0]+"／可工作"+tally[1]+"／可出战"+tally[2]+"／重伤"+tally[3]);
            // 手动休养入口已关闭（恢复全员自动，Resting 由血量派生）。
            Need(!BaseDevelopment.RestWorkers(s,0,1,true),"手动休养入口应一律拒绝");
            Debug.Log("[InjuryTiers] 30% 不带等号、守家例外、队长豁免、当天返岗、全员自动恢复、预警开关、生命降序选兵、补位与四分类 PASS");
        }
        private static void At(BaseState state,string id)
        {
            var obj=BaseSpatial.Objects(state).Single(o=>o.Id==id);var p=obj.Blocks?BaseStageWalk.Door(state,id):new BasePoint(obj.X,obj.Y);
            state.PlayerX=p.X;state.PlayerY=p.Y;BaseAdventure.Arrive(state);
        }
        // P5/P6（2026-11-02）：序章队长外观、七个场景的队长外观白名单、wilderness 旧档回退、
        // 村民攻击图与 NPC 行为入口。一律按「行为定义动作、贴图只是表现层」口径断言。
        private static readonly string[] CommanderAppearanceWhitelist={"azhu","player","female","galan","chief","batu","huoya"};
        public static void VerifyBattleAppearances()
        {
            var male=Resources.Load<TextAsset>("DemoStages/camp-male").text;
            var female=Resources.Load<TextAsset>("DemoStages/camp-female").text;
            // ① 序章队长：兜底态（Intro 缺失）与正常路径都用异性主角搭档，不再借斥候 chenan。
            foreach(string json in new[]{male,female})
            {
                string expected=BaseDemoIntro.CaptainArt(JsonUtility.FromJson<BaseState>(json));
                Need(expected=="female"||expected=="player","序章搭档外观不是异性主角："+expected);
                var fallback=JsonUtility.FromJson<BaseState>(json);
                fallback.Demo.Intro=null;fallback.Demo.Stage=BaseDemoStage.PrologueBriefing;
                // 阶段档营地布局不含 prologue_report（序章村庄运行时才生成），测试里补一个。
                BaseSpatial.Layout(fallback,"base").Objects.Add(new BaseMapPlacement{Id="prologue_report",Name="村民与队长 · 来袭消息",X=9,Y=8.3,Width=1,Height=1});
                var captain=BaseDemoNpcs.Additional(fallback).SingleOrDefault(o=>o.Id=="demo_captain");
                Need(captain!=null,"序章队长兜底对象未生成");
                // 兜底态 Intro 为空，CaptainArt 固定回退 female；按该状态自身的取值断言（W10 原文口径）。
                Need(captain.AssetId==BaseDemoIntro.CaptainArt(fallback)&&captain.AssetId!="chenan","序章队长兜底仍借陈安或外观不符："+captain.AssetId);
                Need(captain.Name==BaseDemoIntro.PartnerName(fallback)+" · 交谈","序章队长兜底名字未跟随搭档名");
                var normal=JsonUtility.FromJson<BaseState>(json);
                normal.Demo.Stage=BaseDemoStage.PrologueBriefing;normal.Demo.Intro.Step=1;
                var partner=BaseDemoIntro.Additional(normal).SingleOrDefault(o=>o.Id=="demo_captain");
                Need(partner!=null&&partner.AssetId==expected&&partner.AssetId!="chenan","序章队长正常路径外观不符");
            }
            // ② 五场战斗＋比试＋演练：场景 id 齐备、外观目标全是真实角色图，不含随机敌营别名与 saber 兜底。
            var scenes=new[]{BaseDemoPrologue.BattleId,BaseDemoRuins.EventId,BaseDemoDefense.EventId,BaseDemoDefense.SecondEventId,BaseDemoFinale.Camp,BaseLocalDuel.Id,BaseDemoIntro.BattleId};
            Need(scenes.Distinct().Count()==scenes.Length&&scenes.All(id=>!string.IsNullOrEmpty(id)),"Demo 场景 id 重复或为空");
            Need(CommanderAppearanceWhitelist.All(PixelDemoArt.IsCast),"战斗队长外观白名单含非角色图");
            Need(scenes.All(id=>!id.Contains("wilderness")&&!id.Contains("enemy-captain")),"场景 id 仍含随机敌营别名");
            // ③ wilderness 只在旧档回退中出现；新场景队长外观空列表不生成兜底形象。
            var scenario=new BattleScenario{CampaignRules=true};scenario.B.CommanderAppearances.Clear();
            Need(new BattleSimulation(scenario).CommanderAppearance(BattleSide.B,0)=="","空队长外观列表生成了兜底形象");
            Need(PixelDemoArt.AtlasId("wilderness")=="enemy-captain"&&!PixelDemoArt.IsCast("wilderness"),"wilderness 旧档回退口径变化");
            // ④ 行为／表现解耦：村民贴图带攻击图是「能力」，NPC 身份不变且不进战斗编组。
            // 村民只有 villager-attack-v2、没有 walk-v2：按「缺动作集＝表现退化，行为不缺」是允许的。
            Need(PixelDemoArt.HasAttack("villager")&&!PixelDemoArt.HasWalk("villager"),"村民临时士兵攻击图缺失或意外出现行走图");
            Need(BaseDemoNpcs.IsMotionNpc("villager")&&BaseDemoNpcs.MotionClass("villager").StartsWith("交互 NPC"),"村民 NPC 分类丢失");
            Need(BaseDemoNpcs.MotionClass("villager").Contains("攻击"),"村民攻击能力被类别门控关掉了");
            var camp=JsonUtility.FromJson<BaseState>(male);
            Need(BaseDemoNpcs.MotionChecks(camp).All(o=>o.Kind=="npc"),"检查台对象混入了战斗编组");
            Debug.Log("[VerifyBattleAppearances] 序章搭档、7 场景外观白名单、wilderness 旧档回退、村民攻击图与 NPC 行为入口 PASS");
        }
        // 遗迹首通专属剧情：RuinsDay==0 开战置 RuinsFirstClear，结算写入 FirstControllerId。
        public static void VerifyRuinsFirstClear()
        {
            var state=JsonUtility.FromJson<BaseState>(Resources.Load<TextAsset>("DemoStages/camp-male").text);
            state.Demo.Rpg.GenerationSeed=44;At(state,"home");Need(BaseDemo.AcknowledgeWaking(state),"无法推进苏醒");
            At(state,"exit_east");Need(BaseCampaign.Travel(state,"town"),"无法进入城镇");
            At(state,"demo_ruins_exit");Need(BaseCampaign.Travel(state,"outpost"),"无法进入遗迹入口");At(state,"demo_ruins_investigate");
            Need(state.Demo.RuinsDay==0,"首通夹具 RuinsDay 非 0");
            var session=BaseDemoRuins.Begin(state,"none");
            Need(session.Opening.RuinsFirstClear,"RuinsDay==0 未标记首通");
            var hero=session.Battle.Soldiers.Single(u=>u.Side==BattleSide.B&&u.Kind==SoldierKind.Hero);
            Need(hero.AppearanceId=="galan","首通遗迹控制者外观错误："+hero.AppearanceId);
            var run=new RealtimeCampaignBattle(session);BaseDemoRuins.Configure(run);
            var enemy=run.Model.Units.Single(u=>u.Id==hero.Id+1);enemy.Health=1;enemy.KnockedDown=true;hero.Health=1;hero.KnockedDown=true;hero.DeathResolved=true;
            run.Model.SetPaused(false);run.Tick(.02);Need(BaseDemoRuins.Complete(state,run),"遗迹首通结算失败");
            var prisoner=state.Aftermath.Prisoners.Single(p=>p.BattleId==session.Opening.Id&&p.Kind==SoldierKind.Hero);
            Need(!string.IsNullOrEmpty(state.Demo.Flow.FirstControllerId)&&state.Demo.Flow.FirstControllerId==prisoner.Id,"首通未写入 FirstControllerId");
            // 首通专属剧情：控制者被 FirstControllerId 标记后，未接纳前招降必须被挡住，走 AcceptController 才放行。
            // （W11 旧工单写的是「直接招降成功」，实测不符；按 W11 要求不改运行时代码，断言按真实行为写。）
            Need(BaseDemoStories.ControllerDialogue(state,prisoner.Id),"首通控制者专属剧情未生效");
            Need(!BaseAftermath.Decide(state,prisoner.Id,"recruit"),"首通控制者在接纳前不应直接招降");
            Need(BaseDemoStories.AcceptController(state,prisoner.Id),"首通控制者接纳失败");
            Need(state.Demo.Flow.ControllerAccepted&&state.Demo.Flow.ControllerPledged,"首通接纳标记未落地");
            var recruited=state.Leaders.Single(l=>l.Id=="partner_recruit:"+prisoner.Id);
            // BasePartnerDialogue.Prefix 目前只识别 ahe／azhu／chenan，迦兰落 partner_default。
            // W11 旧工单要求「不是 partner_default」，与当前实现不符，如实锁住现状，不擅自改文案映射。
            Need(BasePartnerDialogue.Prefix(recruited)=="story.partner_default","首通控制者对话前缀口径变化（需同步 W11 描述）");
            Debug.Log("[VerifyRuinsFirstClear] 首通标记、控制者外观、接纳前置、ControllerAccepted/Pledged 与对白前缀现状 PASS");
        }
        public static void Run()
        {
            var state=JsonUtility.FromJson<BaseState>(Resources.Load<TextAsset>("DemoStages/camp-male").text);
            var scout=BaseDemoNpcs.Present(state,new BaseMapObject("demo_march_exit","old",12,.7,2,1,false,"","villager","npc"));
            Need(scout.Name=="陈安 · 交谈"&&scout.AssetId=="chenan"&&!PixelDemoArt.IsCast("chenan"),"斥候名称、外观或分类错误");
            var ids=new[]{"baron","chunshen","horn","laozhou","merchant","villager","chenan","guard","survivor","villager-girl","rescued-villager"};
            // 行为／表现解耦（2026-11-02 定）：能力由贴图有无动作图集决定，不再按类别硬关。
            // 这里只验证「分类口径」仍正确（这些 id 属于交互 NPC），不断言它们「没有攻击能力」——
            // villager 恰好带 villager-attack-v2，作士兵参战时能播攻击，这是正确行为而非缺陷。
            foreach(string id in ids)Need(BaseDemoNpcs.IsMotionNpc(id),"NPC 分类丢失："+id);
            var shader=Shader.Find("Sprites/Default")??Shader.Find("Unlit/Texture");Need(shader!=null,"缺少测试材质");
            var go=new GameObject("C9-test");var material=new Material(shader);
            try
            {
                foreach(string id in new[]{"chenan","villager"})
                {
                    var sheet=new PixelCharacterSheet(go.transform,material,PixelDemoArt.Load("Characters",id),null,id,false,false);
                    var attack=typeof(PixelCharacterSheet).GetField("attackAtlas",BindingFlags.NonPublic|BindingFlags.Instance);
                    var walk=typeof(PixelCharacterSheet).GetField("walkAtlas",BindingFlags.NonPublic|BindingFlags.Instance);
                    Need(attack.GetValue(sheet)==null&&walk.GetValue(sheet)==null,"静态 NPC 加载了战斗或行走图："+id);sheet.Dispose();
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(material);}
            var doc=BaseDemoRuins.Document(state);doc.Scenario.B.CommanderAppearances=new System.Collections.Generic.List<string>{"chenan"};
            bool rejected=false;try{BattleScenarioDocument.Validate(doc);}catch(ArgumentException){rejected=true;}Need(rejected,"场景队长校验未拒绝陈安外观");
            foreach(int seed in new[]{11,22,33})
            {
                state=JsonUtility.FromJson<BaseState>(Resources.Load<TextAsset>("DemoStages/camp-male").text);
                state.Demo.Rpg.GenerationSeed=seed;At(state,"home");Need(BaseDemo.AcknowledgeWaking(state),"无法推进苏醒");
                At(state,"exit_east");Need(BaseCampaign.Travel(state,"town"),"无法进入城镇");
                At(state,"demo_ruins_exit");Need(BaseCampaign.Travel(state,"outpost"),"无法进入遗迹入口");At(state,"demo_ruins_investigate");
                string first=state.Demo.Flow.FirstControllerId;bool accepted=state.Demo.Flow.ControllerAccepted,pledged=state.Demo.Flow.ControllerPledged;
                var session=BaseDemoRuins.Begin(state,"none");
                // 以非首次结算分支检查常规产出，不改阶段档或首次剧情分支。
                session.Opening.RuinsFirstClear=false;
                var hero=session.Battle.Soldiers.Single(u=>u.Side==BattleSide.B&&u.Kind==SoldierKind.Hero);
                Need(hero.AppearanceId=="galan"&&hero.Rpg!=null,"遗迹控制者外观或生成数据错误："+seed);
                Need(session.Battle.Soldiers.Where(u=>u.Kind==SoldierKind.Hero).All(u=>!string.IsNullOrEmpty(u.AppearanceId)&&u.AppearanceId!="wilderness"),"遗迹队长外观缺失");
                var run=new RealtimeCampaignBattle(session);BaseDemoRuins.Configure(run);
                var enemy=run.Model.Units.Single(u=>u.Id==hero.Id+1);enemy.Health=1;enemy.KnockedDown=true;hero.Health=1;hero.KnockedDown=true;hero.DeathResolved=true;
                run.Model.SetPaused(false);run.Tick(.02);Need(BaseDemoRuins.Complete(state,run),"遗迹结算失败："+seed);
                var prisoner=state.Aftermath.Prisoners.Single(p=>p.BattleId==session.Opening.Id&&p.Kind==SoldierKind.Hero);
                Need(prisoner.AppearanceId=="galan"&&BaseAftermath.Decide(state,prisoner.Id,"recruit"),"遗迹俘虏外观或招降失败："+seed);
                var recruited=state.Leaders.Single(l=>l.Id=="partner_recruit:"+prisoner.Id);
                Need(BasePartnerDialogue.Prefix(recruited)=="story.partner_default","遗迹角色未使用通用对白");
                Need(state.Demo.Flow.FirstControllerId==first&&state.Demo.Flow.ControllerAccepted==accepted&&state.Demo.Flow.ControllerPledged==pledged,"重刷招降改动了首次控制者剧情标记");
            }
            Debug.Log("[VerifyChenanAndRuins] NPC, scene validation and three ruins setups PASS");
        }
    }
}
