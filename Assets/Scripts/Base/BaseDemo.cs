using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BaseConquest.Battle;

namespace BaseConquest.Base
{
    public enum BaseDemoStage { Waking, VisitLord, RuinsAssigned, PrologueBriefing, PrologueEscape, ProloguePursuit, PrologueWounded, RuinsCleared, RuinsDelivered, DefenseWarned, DefenseWon, AttackWon, Finished }

    [Serializable] public sealed class BaseDemoProgress
    {
        public int Version, WakeDay, AcceptedDay, ReleaseVersion;
        public RpgRules Rpg;
        public BaseDemoStage Stage;
        public string BalanceSource, BalanceHash, ConfigHash;
        public int PrologueVersion, PrologueAttempts;
        public bool PrologueCompleted, AzhuInvitationSeen;
        public BaseDemoIntroState Intro;
        public BaseRescueJourney Rescue;
        public List<BaseTerrainMemory> TerrainMemories=new List<BaseTerrainMemory>();
        public int EnvironmentVersion;
        // 营地布局版本：地图布局随存档序列化，改坐标只影响新档。旧档读入时按这个版本做一次性迁移。
        public int CampLayoutVersion;
        public int RuinsVersion, RuinsDay, DeliveredDay;
        public List<BaseLeader> Candidates=new List<BaseLeader>();
        public List<string> MetCompanions=new List<string>();
        public ArmyDefinition RuinsArmy;
        public BaseInventory RuinsReward;
        public BaseDemoDefenseProgress Defense;
        public BaseDemoFinaleProgress Finale;
        // 主线之后的周期随机入侵（BaseDemoRaids）；旧档没有这一段，Normalize 里补。
        public BaseDemoRaidState Raid;
        public BaseDemoEncounterConfig Encounters;
        public BattleTacticalRules TacticalRules;
        public BaseRuinsRewardState RuinsRewards;
        public BaseDemoFlowState Flow;
        public BaseStoryScenePackage StoryScene;
        public List<BaseDemoDesignMap> ReleaseMaps=new List<BaseDemoDesignMap>();
        public List<BaseLeader> SceneFollowers=new List<BaseLeader>();
        public List<string> Evidence = new List<string>();
        public BaseDemoProgress Copy() => new BaseDemoProgress { StoryScene=StoryScene?.Copy(),Raid=Raid?.Copy(),Flow=Flow?.Copy(),EnvironmentVersion=EnvironmentVersion,CampLayoutVersion=CampLayoutVersion,Rpg=Rpg?.Copy(),Version=Version,ReleaseVersion=ReleaseVersion, WakeDay=WakeDay, AcceptedDay=AcceptedDay, Stage=Stage, TerrainMemories=TerrainMemories?.Select(m=>m.Copy()).ToList(),Rescue=Rescue?.Copy(),Intro=Intro?.Copy(),BalanceSource=BalanceSource, BalanceHash=BalanceHash,ConfigHash=ConfigHash, Evidence=Evidence?.ToList(),PrologueVersion=PrologueVersion,PrologueAttempts=PrologueAttempts,PrologueCompleted=PrologueCompleted,AzhuInvitationSeen=AzhuInvitationSeen,SceneFollowers=SceneFollowers?.Select(person=>person.Copy()).ToList(),RuinsVersion=RuinsVersion,RuinsDay=RuinsDay,DeliveredDay=DeliveredDay,Candidates=Candidates?.Select(person=>person.Copy()).ToList(),MetCompanions=MetCompanions?.ToList(),RuinsArmy=RuinsArmy?.Clone(),RuinsReward=RuinsReward?.Copy(),Defense=Defense?.Copy(),Finale=Finale?.Copy(),Encounters=Encounters?.Copy(),TacticalRules=TacticalRules?.Copy(),RuinsRewards=RuinsRewards?.Copy(),ReleaseMaps=(ReleaseMaps??new List<BaseDemoDesignMap>()).Select(m=>m.Copy()).ToList() };
    }

    public static class BaseDemo
    {
        // Increment only when Demo progress becomes incompatible; old files are discarded, never migrated.
        public const int SaveVersion=18;
        public const string Profile="demo-new-camp-v1", ReleaseProfile="demo-complete-v2", BalanceTemplate="35_配表驱动验证.json", Assignment="demo:lord:ruins-assignment";
        public static bool Active(BaseState state) => state?.Demo?.Version==1;
        public static bool FacilityAvailable(BaseState state,string id) => !BaseDemoPrologue.Village(state)&&(!Active(state)||id!="workshop"&&id!="barracks"||BaseDemoDefense.Repaired(state,id))&&(id!="caravan"||BaseFacilityLayout.Built(state,"caravan"))&&(!Active(state)||id!="farm"&&id!="workshop"||BaseFacilityLayout.Built(state,id));
        public static string Objective(BaseState state) => !Active(state) ? "" : BaseDemoIntro.Active(state)&&state.Demo.Stage==BaseDemoStage.PrologueBriefing ? BaseDemoIntro.Objective(state) : BaseDemoPrologue.Village(state) ? state.Demo.Stage==BaseDemoStage.PrologueBriefing?"当前目标：听取村民报告，与队长告别":state.Demo.Stage==BaseDemoStage.PrologueEscape?"当前目标：带村民向东撤离":state.Demo.Stage==BaseDemoStage.ProloguePursuit?"掩护村民离开，击败一名追兵并重创另一名":"序章完成 · 重伤与失踪" : state.Demo.Stage==BaseDemoStage.Waking ? "当前目标：在营帐醒来，了解新营地" : state.Demo.Stage==BaseDemoStage.VisitLord ? "当前目标：从东侧出口前往"+BaseScriptNames.Town+"，拜访领主" : BaseDemoRuins.Objective(state);

        public static BaseScenarioDocument CreateOpening(BaseScenarioDocument balance,string sourceJson,bool includePrologue=false,bool release=false,bool includeIntro=false,RpgRules openingRules=null,string configHash=null,BaseStoryScenePackage story=null)
        {
            if(balance==null||balance.UseOpeningRules!=true||balance.Title!="35 · 配表驱动验证"||balance.Opening?.Demo?.Version>0)throw new ArgumentException("Demo需要有效35号配表快照，不回退到26号或全局默认数值");
            if(story!=null){if(!includePrologue||!includeIntro||release)throw new ArgumentException("剧情场景暂需完整序章与普通Demo开局");story.Validate();}
            openingRules?.Validate();
            if(!string.IsNullOrEmpty(configHash)&&(openingRules==null||configHash.Length!=64||configHash.Any(c=>!"0123456789abcdef".Contains(c))))throw new ArgumentException("Demo数值表来源哈希非法");
            balance.CreateState();
            var state=balance.Opening.Copy();
            string hash;
            using(var algorithm=SHA256.Create())hash=BitConverter.ToString(algorithm.ComputeHash(Encoding.UTF8.GetBytes(sourceJson))).Replace("-","").ToLowerInvariant();
            state.Demo=new BaseDemoProgress {Flow=new BaseDemoFlowState(),Version=1,ReleaseVersion=release?1:0,TacticalRules=release?(openingRules?.OpeningAuxVersion==1?openingRules.OpeningTactical.Copy():new BattleTacticalRules()):null,BalanceSource=BalanceTemplate,BalanceHash=hash,ConfigHash=configHash};
            foreach(var item in BasePersonalEquipment.ShopFor(state).Where(d=>!state.PersonalEquipment.Offers.Any(o=>o.Template==d.Id)))state.PersonalEquipment.Offers.Add(new BasePersonalOffer{Template=item.Id});
            if(openingRules?.OpeningEconomyVersion==1){openingRules.Validate();state.Rules=openingRules.OpeningEconomy.Copy();state.Development.RecoveryFraction=openingRules.OpeningRecoveryPerDay;}
            else state.Rules.FarmPerWorker=4;
            if(openingRules?.OpeningInventory!=null)state.Inventory=openingRules.OpeningInventory.Copy();
            BaseDemoRuins.PrepareOpening(state);
            if(openingRules?.OpeningLordReward!=null)state.Demo.RuinsReward=openingRules.OpeningLordReward.Copy();
            if(openingRules?.OpeningFacilityCapacity!=null)
            {
                string[] ids={"farm","workshop"};
                for(int i=0;i<ids.Length;i++){var facility=state.Muster.Facilities.Single(f=>f.FacilityId==ids[i]);facility.Capacity=openingRules.OpeningFacilityCapacity[i];facility.MinimumWorkers=openingRules.OpeningFacilityMinimum[i];}
            }
            BaseDemoDefense.PrepareOpening(state);
            BaseDemoRaids.PrepareOpening(state);
            if(openingRules?.OpeningBattleRewardVersion==1)
            {
                var defense=state.Demo.Defense.Mission;var reward=openingRules.OpeningDefenseReward;var loss=openingRules.OpeningDefenseLoss;
                defense.FoodReward=reward.Food;defense.MaterialReward=reward.Materials;defense.CoinReward=reward.Coins;
                // 这三个是「每名敌军带走多少」，FoodRaid 表示口粮天数（见 BaseRaidLoot），不是整包损失。
                defense.FoodRaid=loss.Food;defense.MaterialRaid=loss.Materials;defense.CoinRaid=loss.Coins;
                var camp=state.Campaign.Events.Single(e=>e.Id==state.Campaign.EnemySites[0].AttackEventId);reward=openingRules.OpeningCampReward;
                camp.FoodReward=reward.Food;camp.MaterialReward=reward.Materials;camp.CoinReward=reward.Coins;
            }
            BaseDemoFinale.PrepareOpening(state);
            state.Leaders.RemoveAll(leader=>leader.Id!=state.PlayerLeaderId);
            state.Workers.Clear();
            var player=state.Leaders.Single();player.FacilityId=player.DesiredFacility="";player.HasMapPosition=player.HasHomePosition=false;
            player.AcquisitionSource="主角 · "+BaseScriptNames.Village+"序章";player.AcquisitionDay=state.Day;
            state.Adventure.Members=new List<string>{state.PlayerLeaderId};
            foreach(var preset in state.Muster.Presets){preset.Candidates=new List<string>{state.PlayerLeaderId};preset.LeaderCount=1;}
            foreach(var worker in state.Workers){worker.FacilityId=worker.DesiredFacility="";worker.PostLocked=false;}
            var workshop=state.Muster.Facilities.Single(facility=>facility.FacilityId=="workshop");workshop.MinimumWorkers=workshop.Capacity=0;
            state.Backpack.Items.Clear();state.Backpack.StoryFlags.Clear();state.PersonalEquipment.Warehouse.Clear();
            state.Quests=new BaseQuestState {Version=1};
            state.Campaign.Events.RemoveAll(mission=>mission.Kind=="defense");
            InstallCampMap(state);
            if(story!=null)state.Demo.StoryScene=story.Copy();
            BaseRuinsRewards.Enable(state,openingRules?.OpeningAuxVersion==1?openingRules.OpeningRewards:null);
            if(includePrologue)BaseDemoPrologue.PrepareOpening(state);
            BaseRpg.Initialize(state,openingRules);BaseAchievements.Enable(state);
            state.Muster.Strategies=new List<int>{(int)SquadStrategy.Obey,(int)SquadStrategy.Attack,(int)SquadStrategy.Attack};
            BaseTown.EnableDemoRecruitment(state);
            if(includePrologue&&includeIntro)BaseDemoIntro.Enable(state,openingRules!=null);
            BaseEnvironment.Install(state);BaseDevelopment.Allocate(state);
            var opening=new BaseScenarioDocument {Title="Demo · 序章与新营地",Description="序章→伙伴／"+BaseScriptNames.Ruins+"→修复供军→首防→世界道路攻营→返乡断剑。灰盒流程，非正式美术或难度定稿。",UseOpeningRules=true,Opening=state};
            opening.CreateState();return opening;
        }
        public static void InstallCampMap(BaseState state)
        {
            state.Campaign.Layouts=new List<BaseMapLayout> {
                new BaseMapLayout {Region="base",Title="新营地",SpawnX=4,SpawnY=8.8,Objects=new List<BaseMapPlacement> {
                    // X/Y 是**中心点**：占地为 X±宽/2、Y±高/2。上半区一条带：营帐／粮田／台面／工坊，
                    // 南边留出 Y<9.2 的通道，保证出生点 (4,8.8) 不被粮田盖住。
                    Placement("home","营帐",1.8,11.6,3,2,"home-small"),
                    Placement("farm","粮田",6.2,11,5,3,"farm"),
                    Placement("workshop","工坊 · 待修复",19,11,4,3,"workshop",true),
                    Placement("barracks","兵营 · 待修复",12.5,10.9,3.2,2.0,"barracks",true),
                    new BaseMapPlacement {Id="exit_east",Name="拜访领主 →",X=23.3,Y=6.5,Width=1.2,Height=2}
                }},
                new BaseMapLayout {Region="town",Title="领主驻地",SpawnX=12,SpawnY=6.5,Objects=new List<BaseMapPlacement> {
                    new BaseMapPlacement {Id="travel_base",Name="← 返回营地",X=.7,Y=6.5,Width=1.2,Height=2},
                    new BaseMapPlacement {Id="town_quest",Name="领主 · "+BaseScriptNames.Ruins+"调查",X=19,Y=9.5,Width=1,Height=1},
                    // 募兵处贴图是整栋兵营（内容约 205x126，aspect 1.63），2x1.5 太小；按贴图比例放大到 3.2 格宽。
                    new BaseMapPlacement {Id="town_recruit",Name="募兵处 · 普通士兵",X=11,Y=3,Width=3.2,Height=3.2/1.627,Blocks=true,AssetId="barracks",Kind="facility",SourceObjectId="demo:town:recruit"},
                    // TOWN_04_NPC_*：镇上三个闲聊路人，只提供世界观与线索，不推进流程。
                    new BaseMapPlacement {Id="town_guard",Name="镇卫兵",X=21,Y=4,Width=.6,Height=.6},
                    new BaseMapPlacement {Id="town_peddler",Name="行商老莫",X=14,Y=7.75,Width=.6,Height=.6},
                    new BaseMapPlacement {Id="town_girl",Name="镇民小芸",X=15,Y=11.5,Width=.6,Height=.6}
                }}
            };
            state.Campaign.Layouts.Single(map=>map.Region=="base").Objects.AddRange(BaseDemoNpcs.CampResidents());
            foreach(var layout in state.Campaign.Layouts)
            {
                var board=BattleBoard.CreateDefault(24,14);
                foreach(var cell in board.Cells){cell.Terrain=TerrainKind.Ground;cell.Zone=DeploymentZone.None;}
                state.Campaign.RegionMaps.RemoveAll(map=>map.Id==layout.Region);
                state.Campaign.RegionMaps.Add(new BaseRegionMap {Id=layout.Region,Board=board});
            }
            InstallCampHeights(state);InstallTownHeights(state);
            if(BaseDemoRuins.Enabled(state))BaseDemoRuins.InstallMap(state);
            if(BaseDemoDefense.Enabled(state))BaseDemoDefense.InstallMap(state);
            BaseDemoFinale.InstallMarker(state);
            if(BaseDemoRelease.Enabled(state))BaseDemoRelease.Install(state);
            // 编辑器设计的六张剧情图最后覆盖：内置布局与发布快照都不再作数。
            if(state.Demo?.StoryScene?.Valid==true)state.Demo.StoryScene.InstallCamp(state);
            BaseSceneRules.Install(state);if(state.Demo.EnvironmentVersion>0)BaseEnvironment.Install(state,true);
            // 没有剧情场景包时，开局点按当前营帐实际坐标推算，不再写死旧布局的 (4, 8.8)——
            // 那样营帐一挪位，玩家就出生在离仓库两格开外，连仓库都打不开。
            if(state.Demo?.StoryScene?.Valid!=true)
            {
                var home=BaseSpatial.Objects(state).FirstOrDefault(o=>o.Id=="home");
                if(home!=null){state.PlayerX=state.Campaign.BaseX=home.X+.5;state.PlayerY=state.Campaign.BaseY=home.Y-home.Height/2-.6;}
                else{state.PlayerX=state.Campaign.BaseX=4;state.PlayerY=state.Campaign.BaseY=8.8;}
            }
            if(state.Demo!=null)state.Demo.CampLayoutVersion=2;
        }

        private static BaseMapPlacement Placement(string id,string name,double x,double y,double width,double height,string asset,bool locked=false) => new BaseMapPlacement {Id=id,Name=name,X=x,Y=y,Width=width,Height=height,Blocks=true,SourceObjectId="demo:"+id,AssetId=asset,Kind="facility",VisualOnly=locked};
        private static void InstallCampHeights(BaseState state)
        {
            var map=state.Campaign.RegionMaps.FirstOrDefault(entry=>entry.Id=="base");if(map==null)return;
            var board=map.Board;
            // 旧档可能残留上一版台面：先把这片区域压平再重建，避免重复调用叠出孤立高格。
            for(int z=6;z<=13;z++)for(int x=8;x<=18;x++){var stale=board.Cell(new GridPosition(x,z));stale.Elevation=0;stale.Ramp=RampAxis.None;}
            // 台面格 x10~14 × z9~11（物理范围 10~15 × 9~12），兵营(12.5,10.9) 3.2×2.0 落在其中。
            // 2026-11-02 修「上不了台面」：旧版兵营 4×2.6 几乎占满台面，南北东西坡通向的台面高台格全被兵营占地
            //（含角色半径 0.28）堵死。台面北扩会撞地图北边界（北坡需 z14 低地，越界），故改为缩小兵营到 3.2×2.0 并北移，
            // 让南坡、东坡恢复通行；北坡（设施带方向）与西坡（粮田堵死）不再设置。
            for(int z=9;z<=11;z++)for(int x=10;x<=14;x++)board.Cell(new GridPosition(x,z)).Elevation=1;
            void Stair(int x,int z,RampAxis axis){var cell=board.Cell(new GridPosition(x,z));cell.Elevation=1;cell.Ramp=axis;cell.Surface=GroundSurface.Road;}
            Stair(11,8,RampAxis.NorthSouth);Stair(12,8,RampAxis.NorthSouth);
            Stair(15,10,RampAxis.EastWest);
        }
        // 旧档布局迁移：地图布局随存档序列化，改坐标只影响新档。这里把营地设施拉回当前标准位置、
        // 清掉已废弃的场景摆件、重装高台，旧档不必重开就能看到新布局。
        public static void EnsureCampLayout(BaseState state)
        {
            if(state?.Demo==null||state.Campaign?.Layouts==null)return;
            if(state.Demo.CampLayoutVersion>=2)return;
            var camp=state.Campaign.Layouts.FirstOrDefault(map=>map.Region=="base");
            if(camp!=null)
            {
                void Move(string id,double x,double y,double w,double h)
                {
                    var placement=camp.Objects.FirstOrDefault(o=>o.Id==id);if(placement==null)return;
                    placement.X=x;placement.Y=y;placement.Width=w;placement.Height=h;
                }
                // 设施一律排进上半区一条带（Y≥9.5），南侧留出 Y<9.2 的通道；
                // 坐标是中心点，粮田放到 (5,9) 会盖住出生点 (4,8.8)，这一版已避开。
                Move("home",1.8,11.6,3,2);
                Move("farm",6.2,11,5,3);
                Move("workshop",19,11,4,3);
                Move("barracks",12.5,10.9,3.2,2.0);
                // 营帐里的长剑摆件已并入剧情物品（老周当面交付长剑），场景里的那支撤掉。
                camp.Objects.RemoveAll(o=>o.Id=="demo_long_sword");
                // 迎敌战场入口移到左侧边缘正中，和东侧「拜访领主」同一高度。
                var exit=camp.Objects.FirstOrDefault(o=>o.Id=="demo_defense_exit");
                if(exit!=null)exit.Y=6.5;
                // 修复提示牌挂在设施下方，设施挪了它也得跟着挪。
                foreach(string id in new[]{"workshop","barracks"})
                {
                    var marker=camp.Objects.FirstOrDefault(o=>o.Id=="demo_fix_"+id);var facility=camp.Objects.FirstOrDefault(o=>o.Id==id);
                    if(marker!=null&&facility!=null){marker.X=facility.X;marker.Y=facility.Y-facility.Height/2-.4;}
                }
            }
            InstallCampHeights(state);
            RelocateStrandedCharacters(state);
            state.Demo.CampLayoutVersion=2;
        }
        // 设施换位置后，原本站在新占地上的角色会变成「不可通行」而让存档校验直接抛错（曾把出生点盖在粮田下）。
        // 这里把挡住的角色挪到最近的空位；伙伴的待命点作废后由系统重新安排，不再指向被占的格子。
        private static void RelocateStrandedCharacters(BaseState state)
        {
            if(state.Campaign.Location=="base"&&!BaseSpatial.Geometry(state,state.PlayerX,state.PlayerY))
                for(int radius=0;radius<=8;radius++)
                {
                    bool placed=false;
                    for(int dx=-radius;dx<=radius&&!placed;dx++)
                        for(int dz=-radius;dz<=radius&&!placed;dz++)
                        {
                            double x=4+dx*.75,y=8.8+dz*.75;
                            if(!BaseSpatial.Geometry(state,x,y))continue;
                            state.PlayerX=state.Campaign.BaseX=x;state.PlayerY=state.Campaign.BaseY=y;placed=true;
                        }
                    if(placed)break;
                }
            if(state.Leaders==null)return;
            foreach(var leader in state.Leaders.Where(l=>l.HasHomePosition))
                if(!BaseSpatial.Geometry(state,leader.HomeX,leader.HomeY,"base"))leader.HasHomePosition=false;
        }
        private static void InstallTownHeights(BaseState state)
        {
            var board=state.Campaign.RegionMaps.Single(map=>map.Id=="town").Board;
            for(int z=8;z<=12;z++)for(int x=16;x<=22;x++)board.Cell(new GridPosition(x,z)).Elevation=1;
            for(int z=9;z<=12;z++)for(int x=17;x<=22;x++)board.Cell(new GridPosition(x,z)).Elevation=2;
            void Stair(int x,int z,int level,RampAxis axis){var cell=board.Cell(new GridPosition(x,z));cell.Elevation=level;cell.Ramp=axis;cell.Surface=GroundSurface.Road;}
            for(int x=18;x<=19;x++){Stair(x,7,1,RampAxis.NorthSouth);Stair(x,8,2,RampAxis.NorthSouth);}
        }

        public static void Validate(BaseState state)
        {
            var progress=state.Demo;BaseDemoFlow.Validate(state);
            if(progress==null)return;
            if(progress.StoryScene?.Valid==true){if(progress.Version!=1)throw new ArgumentException("剧情场景包缺少有效Demo");progress.StoryScene.Validate();}
            if(!string.IsNullOrEmpty(progress.ConfigHash)&&(progress.ConfigHash.Length!=64||progress.ConfigHash.Any(c=>!"0123456789abcdef".Contains(c))))throw new ArgumentException("Demo数值表来源哈希非法");
            if(progress.EnvironmentVersion<0||progress.EnvironmentVersion>1||progress.EnvironmentVersion>0&&!BaseSceneRules.Active(state))throw new ArgumentException("环境快照版本非法");
            BaseDemoRelease.Validate(state);
            progress.Encounters?.Validate();
            if(progress.Version==0&&(progress.Encounters?.Version??0)!=0)throw new ArgumentException("非Demo不能含专用战斗编排");
            if(progress.Version==0)
            {
                if(progress.Stage!=0||progress.WakeDay!=0||progress.AcceptedDay!=0||!string.IsNullOrEmpty(progress.BalanceSource)||!string.IsNullOrEmpty(progress.BalanceHash)||progress.Evidence?.Count>0||progress.PrologueVersion!=0||progress.PrologueCompleted||progress.AzhuInvitationSeen||progress.PrologueAttempts!=0||progress.SceneFollowers?.Count>0||progress.RuinsVersion!=0||progress.Candidates?.Count>0||progress.MetCompanions?.Count>0||progress.RuinsDay!=0||progress.DeliveredDay!=0||progress.Defense?.Version>0||progress.Finale?.Version>0)throw new ArgumentException("Demo进度缺版本，拒绝重置");
                return;
            }
            if(progress.Version!=1||!Enum.IsDefined(typeof(BaseDemoStage),progress.Stage)||progress.BalanceSource!=BalanceTemplate||progress.BalanceHash==null||progress.BalanceHash.Length!=64||progress.BalanceHash.Any(value=>!"0123456789abcdef".Contains(value))||progress.Evidence==null)throw new ArgumentException("Demo版本、阶段或数值来源非法");
            bool accepted=progress.AcceptedDay>0,awake=progress.Stage==BaseDemoStage.VisitLord||BaseDemoRuins.Assigned(state);
            if(awake?(progress.WakeDay<1||progress.WakeDay>state.Day):progress.WakeDay!=0)throw new ArgumentException("Demo醒来日期与阶段不一致");
            if(accepted?(progress.AcceptedDay<progress.WakeDay||progress.AcceptedDay>state.Day||(BaseDemoRuins.Enabled(state)?!progress.Evidence.Contains(Assignment):!progress.Evidence.SequenceEqual(new[]{Assignment}))):(progress.AcceptedDay!=0||progress.Evidence.Contains(Assignment)))throw new ArgumentException("Demo调查委托与证据状态不一致");
            bool world=BaseDemoFinale.Opened(state);
            if(!world&&(state.Campaign.InstanceVersion!=0||!string.IsNullOrEmpty(state.Campaign.DesignId)||!new[]{"base","town"}.Contains(state.Campaign.Location)&&!(BaseDemoRuins.Enabled(state)&&state.Campaign.Location=="outpost")&&!BaseSceneRules.Dungeon(state)&&!(BaseDemoDefense.Enabled(state)&&state.Campaign.Location=="perimeter"))||state.Campaign.HasActiveBattle&&!BaseDemoRuins.BattleActive(state)&&!BaseDemoDefense.BattleActive(state)&&!BaseDemoFinale.BattleActive(state)||state.Muster.Parties.Any(party=>party.Purpose=="expedition"?!BaseDemoRuins.Enabled(state):party.Purpose!="defense"||!BaseDemoDefense.Enabled(state))||state.Campaign.Events.Any(mission=>mission.Kind=="defense"&&mission.Status=="pending"&&(!BaseDemoDefense.Enabled(state)||mission.Id!=BaseDemoDefense.EventId&&mission.Id!=BaseDemoDefense.SecondEventId)))throw new ArgumentException("Demo仅开放指定场景与剧情战");
            if(state.Campaign.Layouts.Count!=((BaseSceneRules.Active(state)?1:0)+(world?7:BaseDemoDefense.Enabled(state)?4:BaseDemoRuins.Enabled(state)?3:2))||!state.Campaign.Layouts.Any(map=>map.Region=="town")||!BaseDemoPrologue.Village(state)&&new[]{"home","barracks"}.Any(id=>!state.Campaign.Layouts.Single(map=>map.Region=="base").Objects.Any(obj=>obj.Id==id&&obj.Blocks)))throw new ArgumentException("Demo地图须保留营帐、兵营与领主驻地");
            if(!BaseDemoDefense.Repaired(state,"workshop")&&(state.Muster.Facilities.Any(facility=>facility.FacilityId=="workshop"&&(facility.Capacity!=0||facility.MinimumWorkers!=0))||state.Leaders.Any(leader=>leader.FacilityId=="workshop")||state.Workers.Any(worker=>worker.FacilityId=="workshop")))throw new ArgumentException("Demo工坊尚未修复，不能提前生产");
            if(state.Campaign.Layouts.Single(map=>map.Region=="base").Objects.Any(obj=>new[]{"workshop","barracks"}.Contains(obj.Id)&&obj.VisualOnly==BaseDemoDefense.Repaired(state,obj.Id)))throw new ArgumentException("Demo建筑外观与实际修复状态不一致");
            BaseDemoPrologue.Validate(state);BaseDemoIntro.Validate(state);
            BaseDemoRuins.Validate(state);
            BaseRuinsRewards.Validate(state);
            BaseDemoDefense.Validate(state);
            BaseDemoFinale.Validate(state);
            BaseDemoRaids.Validate(state);
        }

        public static bool AcknowledgeWaking(BaseState state)
        {
            // 醒来对话只能由「营帐／老周」交互或进入营地的自动触发弹出，弹出即代表玩家已确认醒来；
            // 不再要求玩家此刻仍站在营帐旁——对话弹出后玩家可能已走开，点击「起身」应直接完成醒来，
            // 否则玩家不在营帐旁时点「起身」会抛「请回到营帐同层一格以内」且对话框无法关闭（卡死）。
            if(!Active(state)||state.Demo.Stage!=BaseDemoStage.Waking)return false;
            const string sword=BaseBackpack.RoyalSword;
            if(state.Backpack.Catalog.Any(d=>d.Id==sword)||state.Backpack.Catalog.Count>=1000)return false;
            var definition=new BaseBagDefinition {Id=sword,Name="王都秘银长剑",Category="quest",Description=BaseBackpack.RoyalSwordDescription,MaxStack=1};
            state.Backpack.Catalog.Add(definition);
            if(!BaseBackpack.Add(state,sword,1)){state.Backpack.Catalog.Remove(definition);return false;}
            int cell=BasePartyInventory.FreeCell(state,state.PlayerLeaderId);
            if(cell>=0){var item=state.Backpack.Items.Single(i=>i.ItemId==sword);item.Holder=state.PlayerLeaderId;item.Cell=cell;}
            state.Demo.Stage=BaseDemoStage.VisitLord;state.Demo.WakeDay=state.Day;return true;
        }
        public static bool AcceptLordQuest(BaseState state)
        {
            if(!Active(state)||state.Demo.AcceptedDay>0||state.Demo.Stage!=BaseDemoStage.VisitLord&&state.Demo.Stage!=BaseDemoStage.RuinsCleared||!BaseInteraction.At(state,"town","town_quest"))return false;
            state.Demo.Evidence.Insert(0,Assignment);state.Demo.AcceptedDay=state.Day;if(state.Demo.RuinsDay==0)state.Demo.Stage=BaseDemoStage.RuinsAssigned;return true;
        }
        public static string TravelProblem(BaseState state,string target)
        {
            if(!Active(state))return "";
            if(BaseDemoFinale.Opened(state))return "";
            if(BaseDemoPrologue.Village(state))return "请先完成村庄撤离与追兵剧情";
            if(state.Demo.Stage==BaseDemoStage.Waking)return "请先在营帐了解当前处境";
            if(BaseDemoRuins.Enabled(state))return BaseDemoRuins.TravelProblem(state,target);
            if(target!="base"&&target!="town")return "本批只开放新营地与领主驻地，后续区域尚未接入";
            return BaseInteraction.At(state,"base","exit_east")&&target=="town"||BaseInteraction.At(state,"town","travel_base")&&target=="base" ? "" : "请到对应出口同层一格以内通行";
        }
        public static string AutoSaveReason(BaseState previous,BaseState next) => Active(previous)&&Active(next) ? previous.Demo.Stage==BaseDemoStage.VisitLord&&next.Demo.Stage==BaseDemoStage.RuinsAssigned ? "主线节点：接取"+BaseScriptNames.Ruins+"调查" : previous.Demo.Stage==BaseDemoStage.ProloguePursuit&&next.Demo.Stage==BaseDemoStage.PrologueWounded ? "主线节点：序章撤离完成" : previous.Demo.Stage==BaseDemoStage.RuinsAssigned&&next.Demo.Stage==BaseDemoStage.RuinsCleared ? "主线节点："+BaseScriptNames.Ruins+"救助完成" : previous.Demo.Stage==BaseDemoStage.RuinsCleared&&next.Demo.Stage==BaseDemoStage.RuinsDelivered ? "主线节点："+BaseScriptNames.Ruins+"证据交付" : previous.Demo.Stage==BaseDemoStage.DefenseWarned&&next.Demo.Stage==BaseDemoStage.DefenseWon&&!BaseBattleLoot.Pending(next)?"主线节点：首次防守战果处理完成":previous.Demo.Stage==BaseDemoStage.DefenseWon&&next.Demo.Stage==BaseDemoStage.AttackWon&&!BaseBattleLoot.Pending(next)?"主线节点：攻营战果处理完成":previous.Demo.Stage==BaseDemoStage.AttackWon&&next.Demo.Stage==BaseDemoStage.Finished?"主线节点：返乡断剑与Demo结束":null : null;
    }
}
