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
        public int RuinsVersion, RuinsDay, DeliveredDay;
        public List<BaseLeader> Candidates=new List<BaseLeader>();
        public List<string> MetCompanions=new List<string>();
        public ArmyDefinition RuinsArmy;
        public BaseInventory RuinsReward;
        public BaseDemoDefenseProgress Defense;
        public BaseDemoFinaleProgress Finale;
        public BaseDemoEncounterConfig Encounters;
        public BattleTacticalRules TacticalRules;
        public BaseRuinsRewardState RuinsRewards;
        public BaseDemoFlowState Flow;
        public List<BaseDemoDesignMap> ReleaseMaps=new List<BaseDemoDesignMap>();
        public List<BaseLeader> SceneFollowers=new List<BaseLeader>();
        public List<string> Evidence = new List<string>();
        public BaseDemoProgress Copy() => new BaseDemoProgress { Flow=Flow?.Copy(),EnvironmentVersion=EnvironmentVersion,Rpg=Rpg?.Copy(),Version=Version,ReleaseVersion=ReleaseVersion, WakeDay=WakeDay, AcceptedDay=AcceptedDay, Stage=Stage, TerrainMemories=TerrainMemories?.Select(m=>m.Copy()).ToList(),Rescue=Rescue?.Copy(),Intro=Intro?.Copy(),BalanceSource=BalanceSource, BalanceHash=BalanceHash,ConfigHash=ConfigHash, Evidence=Evidence?.ToList(),PrologueVersion=PrologueVersion,PrologueAttempts=PrologueAttempts,PrologueCompleted=PrologueCompleted,AzhuInvitationSeen=AzhuInvitationSeen,SceneFollowers=SceneFollowers?.Select(person=>person.Copy()).ToList(),RuinsVersion=RuinsVersion,RuinsDay=RuinsDay,DeliveredDay=DeliveredDay,Candidates=Candidates?.Select(person=>person.Copy()).ToList(),MetCompanions=MetCompanions?.ToList(),RuinsArmy=RuinsArmy?.Clone(),RuinsReward=RuinsReward?.Copy(),Defense=Defense?.Copy(),Finale=Finale?.Copy(),Encounters=Encounters?.Copy(),TacticalRules=TacticalRules?.Copy(),RuinsRewards=RuinsRewards?.Copy(),ReleaseMaps=(ReleaseMaps??new List<BaseDemoDesignMap>()).Select(m=>m.Copy()).ToList() };
    }

    public static class BaseDemo
    {
        // Increment only when Demo progress becomes incompatible; old files are discarded, never migrated.
        public const int SaveVersion=18;
        public const string Profile="demo-new-camp-v1", ReleaseProfile="demo-complete-v2", BalanceTemplate="35_配表驱动验证.json", Assignment="demo:lord:ruins-assignment";
        public static bool Active(BaseState state) => state?.Demo?.Version==1;
        public static bool FacilityAvailable(BaseState state,string id) => !BaseDemoPrologue.Village(state)&&(!Active(state)||id!="workshop"&&id!="barracks"||BaseDemoDefense.Repaired(state,id));
        public static string Objective(BaseState state) => !Active(state) ? "" : BaseDemoIntro.Active(state)&&state.Demo.Stage==BaseDemoStage.PrologueBriefing ? BaseDemoIntro.Objective(state) : BaseDemoPrologue.Village(state) ? state.Demo.Stage==BaseDemoStage.PrologueBriefing?"当前目标：听取村民报告，与队长告别":state.Demo.Stage==BaseDemoStage.PrologueEscape?"当前目标：带村民向东撤离":state.Demo.Stage==BaseDemoStage.ProloguePursuit?"掩护村民离开，击败一名追兵并重创另一名":"序章完成 · 重伤与失踪" : state.Demo.Stage==BaseDemoStage.Waking ? "当前目标：在营帐醒来，了解新营地" : state.Demo.Stage==BaseDemoStage.VisitLord ? "当前目标：从东侧出口前往"+BaseScriptNames.Town+"，拜访领主" : BaseDemoRuins.Objective(state);

        public static BaseScenarioDocument CreateOpening(BaseScenarioDocument balance,string sourceJson,bool includePrologue=false,bool release=false,bool includeIntro=false,RpgRules openingRules=null,string configHash=null)
        {
            if(balance==null||balance.UseOpeningRules!=true||balance.Title!="35 · 配表驱动验证"||balance.Opening?.Demo?.Version>0)throw new ArgumentException("Demo需要有效35号配表快照，不回退到26号或全局默认数值");
            openingRules?.Validate();
            if(!string.IsNullOrEmpty(configHash)&&(openingRules==null||configHash.Length!=64||configHash.Any(c=>!"0123456789abcdef".Contains(c))))throw new ArgumentException("Demo数值表来源哈希非法");
            balance.CreateState();
            var state=balance.Opening.Copy();
            string hash;
            using(var algorithm=SHA256.Create())hash=BitConverter.ToString(algorithm.ComputeHash(Encoding.UTF8.GetBytes(sourceJson))).Replace("-","").ToLowerInvariant();
            state.Demo=new BaseDemoProgress {Flow=new BaseDemoFlowState(),Version=1,ReleaseVersion=release?1:0,TacticalRules=release?(openingRules?.OpeningAuxVersion==1?openingRules.OpeningTactical.Copy():new BattleTacticalRules()):null,BalanceSource=BalanceTemplate,BalanceHash=hash,ConfigHash=configHash};
            foreach(var item in BasePersonalEquipment.ShopFor(state).Where(d=>!state.PersonalEquipment.Offers.Any(o=>o.Template==d.Id)))state.PersonalEquipment.Offers.Add(new BasePersonalOffer{Template=item.Id});
            if(openingRules?.OpeningEconomyVersion==1){openingRules.Validate();state.Rules=openingRules.OpeningEconomy.Copy();state.Development.RecoveryPerDay=openingRules.OpeningRecoveryPerDay;}
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
            if(openingRules?.OpeningBattleRewardVersion==1)
            {
                var defense=state.Demo.Defense.Mission;var reward=openingRules.OpeningDefenseReward;var loss=openingRules.OpeningDefenseLoss;
                defense.FoodReward=reward.Food;defense.MaterialReward=reward.Materials;defense.CoinReward=reward.Coins;
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
            BaseRuinsRewards.Enable(state,openingRules?.OpeningAuxVersion==1?openingRules.OpeningRewards:null);
            if(includePrologue)BaseDemoPrologue.PrepareOpening(state);
            BaseRpg.Initialize(state,openingRules);BaseAchievements.Enable(state);
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
                    Placement("home","营帐",2,10.5,3,2,"home-small"),
                    Placement("farm","粮田",6.5,11.5,5,3,"farm"),
                    Placement("workshop","工坊 · 待修复",20,11.5,4,3,"workshop",true),
                    Placement("barracks","兵营 · 待修复",12,10.5,4,2.6,"barracks",true),
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
            BaseSceneRules.Install(state);if(state.Demo.EnvironmentVersion>0)BaseEnvironment.Install(state,true);
            state.PlayerX=state.Campaign.BaseX=4;state.PlayerY=state.Campaign.BaseY=8.8;
        }

        private static BaseMapPlacement Placement(string id,string name,double x,double y,double width,double height,string asset,bool locked=false) => new BaseMapPlacement {Id=id,Name=name,X=x,Y=y,Width=width,Height=height,Blocks=true,SourceObjectId="demo:"+id,AssetId=asset,Kind="facility",VisualOnly=locked};
        private static void InstallCampHeights(BaseState state)
        {
            var board=state.Campaign.RegionMaps.Single(map=>map.Id=="base").Board;
            for(int z=8;z<=11;z++)for(int x=9;x<=14;x++)board.Cell(new GridPosition(x,z)).Elevation=1;
            void Stair(int x,int z,RampAxis axis){var cell=board.Cell(new GridPosition(x,z));cell.Elevation=1;cell.Ramp=axis;cell.Surface=GroundSurface.Road;}
            Stair(11,7,RampAxis.NorthSouth);Stair(12,7,RampAxis.NorthSouth);
            Stair(9,12,RampAxis.NorthSouth);Stair(14,12,RampAxis.NorthSouth);
            Stair(8,8,RampAxis.EastWest);Stair(8,9,RampAxis.EastWest);
            Stair(15,8,RampAxis.EastWest);Stair(15,9,RampAxis.EastWest);
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
            if(state.Campaign.Layouts.Count!=((BaseSceneRules.Active(state)?1:0)+(world?7:BaseDemoDefense.Enabled(state)?4:BaseDemoRuins.Enabled(state)?3:2))||!state.Campaign.Layouts.Any(map=>map.Region=="town")||!BaseDemoPrologue.Village(state)&&state.Campaign.Layouts.Single(map=>map.Region=="base").Objects.Count(obj=>obj.Blocks&&!BaseEnvironment.Decorative(obj.Object))!=4)throw new ArgumentException("Demo地图须保留四个固定建筑位与领主驻地");
            if(!BaseDemoDefense.Repaired(state,"workshop")&&(state.Muster.Facilities.Any(facility=>facility.FacilityId=="workshop"&&(facility.Capacity!=0||facility.MinimumWorkers!=0))||state.Leaders.Any(leader=>leader.FacilityId=="workshop")||state.Workers.Any(worker=>worker.FacilityId=="workshop")))throw new ArgumentException("Demo工坊尚未修复，不能提前生产");
            if(state.Campaign.Layouts.Single(map=>map.Region=="base").Objects.Any(obj=>new[]{"workshop","barracks"}.Contains(obj.Id)&&obj.VisualOnly==BaseDemoDefense.Repaired(state,obj.Id)))throw new ArgumentException("Demo建筑外观与实际修复状态不一致");
            BaseDemoPrologue.Validate(state);BaseDemoIntro.Validate(state);
            BaseDemoRuins.Validate(state);
            BaseRuinsRewards.Validate(state);
            BaseDemoDefense.Validate(state);
            BaseDemoFinale.Validate(state);
        }

        public static bool AcknowledgeWaking(BaseState state)
        {
            if(!Active(state)||state.Demo.Stage!=BaseDemoStage.Waking||!BaseInteraction.At(state,"base","home")&&!BaseInteraction.At(state,"base","demo_rescuer"))return false;
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
