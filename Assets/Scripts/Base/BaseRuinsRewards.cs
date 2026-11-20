using System;
using System.Linq;
using BaseConquest.Battle;

namespace BaseConquest.Base
{
    [Serializable] public sealed class BaseRuinsRewardState
    {
        public int Version=1,MaximumEnergy=120,RewardCost=20,Energy=120,EnergyDay=1,Coins=40,ItemCount=1;
        public double ExposureFraction=.03;
        public string PendingBattleId="";
        public string HideoutPendingBattleId="";
        public string HideoutFirstClearBattleId="";
        public bool PendingFirstClear;
        public int FirstCoins=60,FirstFood=20,FirstMaterials=10,EnemyGeneration;
        public BaseRuinsRewardState Copy()=>(BaseRuinsRewardState)MemberwiseClone();
    }
    public static class BaseRuinsRewards
    {
        private const string LegacyHideoutClear = "legacy:bandit-hideout-victory";
        public static int Energy(BaseState s)=>s.Demo.RuinsRewards?.Energy??120;
        public static int Maximum(BaseState s)=>s.Demo.RuinsRewards?.MaximumEnergy??120;
        public static int Cost(BaseState s)=>s.Demo.RuinsRewards?.RewardCost??20;
        public static double Exposure(BaseState s)=>s.Demo.RuinsRewards?.ExposureFraction??.03;
        internal static bool IsDungeon(BasePrepRoute route) => route is BasePrepRoute.Ruins or BasePrepRoute.Hideout;
        internal static string DungeonName(BasePrepRoute route) => route == BasePrepRoute.Hideout ? BaseBanditHideout.Title : BaseScriptNames.Ruins;
        internal static string EventId(BasePrepRoute route) => route == BasePrepRoute.Hideout ? BaseBanditHideout.EventId : BaseDemoRuins.EventId;
        private static BaseCampaignReport FirstVictory(BaseState s, BasePrepRoute route) =>
            s.Campaign.Reports.FirstOrDefault(report => report.EventId == EventId(route) && report.Outcome == "SideAWin");
        public static bool Cleared(BaseState s, BasePrepRoute route) => route == BasePrepRoute.Hideout
            ? !string.IsNullOrEmpty(s.Demo.RuinsRewards?.HideoutFirstClearBattleId)
            : BaseDemoRuins.Enabled(s) && s.Demo.RuinsDay > 0;
        public static string PendingBattle(BaseState s, BasePrepRoute route = BasePrepRoute.Ruins) =>
            (route == BasePrepRoute.Hideout ? s.Demo.RuinsRewards?.HideoutPendingBattleId : s.Demo.RuinsRewards?.PendingBattleId) ?? "";
        private static bool PendingFirstClear(BaseState s, BasePrepRoute route) => route == BasePrepRoute.Hideout
            ? PendingBattle(s, route) != "" && s.Demo.RuinsRewards.HideoutFirstClearBattleId == PendingBattle(s, route)
            : s.Demo.RuinsRewards?.PendingFirstClear == true;
        internal static string HideoutVictoryReceipt(string battle) => battle + ":hideout-victory";
        public static int PendingCost(BaseState s, BasePrepRoute route = BasePrepRoute.Ruins) => PendingFirstClear(s, route) ? 0 : Cost(s);
        public static bool FirstRewardPending(BaseState s)
        {var first=FirstVictory(s,BasePrepRoute.Ruins);return first!=null&&s.Aftermath.Loot.Any(m=>!m.Closed&&m.BattleId==first.BattleId);}
        public static bool Immune(BaseState s,BaseLeader leader)=>leader.IsAlien||leader.IsAwakened||leader.Id==s.PlayerLeaderId||leader.Id=="partner_craft";
        public static string WarningRoster(BaseState s)
        {
            var plan=BaseAdventure.Preview(s,"expedition",true);
            return string.Join(",",plan.Leaders.Where(id=>!Immune(s,s.Leaders.Single(l=>l.Id==id))))+"|"+plan.Takes.Sum(t=>t.Count);
        }
        public static bool VulnerableParty(BaseState s)=>WarningRoster(s)!="|0";
        public static bool RequiresWarning(BaseState s, BaseBattlePreparation draft) => draft == null
            ? VulnerableParty(s)
            : draft.Units.Any(unit => unit.Selected && draft.Positions.ContainsKey(unit.Id) &&
                (unit.Leader == null || !Immune(s, unit.Leader)));
        public static void ValidateDefaults(BaseRuinsRewardState r)
        {
            if(r==null||r.Version!=1||r.MaximumEnergy<1||r.MaximumEnergy>10000||r.RewardCost<1||r.RewardCost>r.MaximumEnergy||r.Energy!=r.MaximumEnergy||r.EnergyDay!=1||r.Coins<0||r.Coins>1000000||r.ItemCount<1||r.ItemCount>99||new[]{r.FirstCoins,r.FirstFood,r.FirstMaterials}.Any(n=>n<0||n>1000000)||double.IsNaN(r.ExposureFraction)||double.IsInfinity(r.ExposureFraction)||r.ExposureFraction<=0||r.ExposureFraction>1||!string.IsNullOrEmpty(r.PendingBattleId)||!string.IsNullOrEmpty(r.HideoutPendingBattleId)||!string.IsNullOrEmpty(r.HideoutFirstClearBattleId)||r.PendingFirstClear||r.EnemyGeneration!=0)throw new ArgumentException("副本奖励默认配置非法");
        }
        public static void Enable(BaseState s,BaseRuinsRewardState defaults=null)
        {if(defaults!=null)ValidateDefaults(defaults);s.Demo.RuinsRewards=defaults?.Copy()??new BaseRuinsRewardState();s.Demo.RuinsRewards.EnergyDay=s.Day;s.Leaders.Single(l=>l.Id==s.PlayerLeaderId).IsAwakened=true;foreach(var l in s.Demo.Candidates)l.IsAwakened=l.Id=="partner_craft";}
        // 2026-11-04 裁决（P33）：体力按天部分恢复，不再跨天直接回满。
        // 原型以 120 上限／每日 40 演示；具体数值等配表定案，这里只提供机制。
        public const int DailyRecovery=40;
        public static void AdvanceDay(BaseState s)
        {
            if(!BaseDemoRuins.Enabled(s))return;
            if(s.Demo.RuinsRewards==null)Enable(s);
            var r=s.Demo.RuinsRewards;
            if(r.EnergyDay<s.Day)
            {
                int days=Math.Max(1,s.Day-r.EnergyDay);
                r.Energy=(int)Math.Min(r.MaximumEnergy,(long)r.Energy+(long)DailyRecovery*days);
                r.EnergyDay=s.Day;
            }
        }
        // 补充入口的数据接口；道具与购买方式等原型定稿后再接。
        public static bool AddEnergy(BaseState s,int amount)
        {
            if(amount<=0)return false;
            var r=s.Demo.RuinsRewards;if(r==null||r.Energy>r.MaximumEnergy-amount)return false;
            r.Energy+=amount;return true;
        }
        internal static void RecordVictory(BaseState s, string battleId, BasePrepRoute route, bool firstClear = false)
        {
            if (s.Demo.RuinsRewards == null) Enable(s);
            var rewards = s.Demo.RuinsRewards;
            if (route == BasePrepRoute.Hideout)
            {
                AddHideoutReceipt(s,battleId);
                if (string.IsNullOrEmpty(rewards.HideoutFirstClearBattleId)) rewards.HideoutFirstClearBattleId = battleId;
                rewards.HideoutPendingBattleId = battleId;
            }
            else
            {
                rewards.PendingBattleId = battleId;
                rewards.PendingFirstClear = firstClear;
            }
            if (PendingFirstClear(s, route) && !Claim(s, route, automatic: true)) throw new InvalidOperationException(ClaimProblem(s, route));
        }
        public static string ClaimProblem(BaseState s, BasePrepRoute route = BasePrepRoute.Ruins)
        {
            var r=s.Demo.RuinsRewards;
            string battle = PendingBattle(s, route);
            if(r==null||battle=="")return "本次没有待领取的副本奖励";
            bool inside = route == BasePrepRoute.Hideout ? BaseBanditHideout.InHideout(s) : BaseDemoRuins.InRuins(s);
            if(!(inside||BaseSceneRules.Entrance(s))||!BasePartyInventory.Peaceful(s))return "请在本次副本战后领取";
            if(r.Energy<PendingCost(s,route))return "体力不足；仍可挑战，但本次不能领取奖励";
            if(s.Campaign.SettledIds.Contains(battle+":ruins-reward"))return "本次奖励已经领取";
            bool first = PendingFirstClear(s, route);
            if(s.Inventory.Coins>100000000-r.Coins-(first?r.FirstCoins:0)||first&&(s.Inventory.Food>100000000-r.FirstFood||s.Inventory.Materials>100000000-r.FirstMaterials))return "资源库存已满";
            return "";
        }
        public static bool Claim(BaseState s, BasePrepRoute route = BasePrepRoute.Ruins, bool automatic = false)
        {
            if(ClaimProblem(s,route)!="")return false;
            var r=s.Demo.RuinsRewards;
            string battle = PendingBattle(s,route), id = battle+":ruins-reward";
            int seed = BaseAftermath.StableIdentity(battle);
            bool first = PendingFirstClear(s,route);
            string[] equipment={"novice_sword","novice_light_helmet","novice_light_armor","novice_bow","novice_spear"};
            int choice=(int)(BattleSimulation.StableRoll(seed,0,71)*equipment.Length);
            var rewards=new System.Collections.Generic.List<BaseLootReward> {new BaseLootReward {Template=equipment[choice],RandomQuality=true},new BaseLootReward {ItemId="forging_stone",Count=r.ItemCount}};
            // 2026-11-03：基础兵书改为掉落产出（城镇不再出售）；高阶兵书 Demo 内不产出，因此队长等级上限为 2 级。
            double bookRoll=BattleSimulation.StableRoll(seed,1,73);
            if(bookRoll<.5)
            {
                var kinds=BaseTraining.SoldierKinds(s);
                var kind=kinds[(int)(BattleSimulation.StableRoll(seed,2,73)*kinds.Length)%kinds.Length];
                rewards.Add(new BaseLootReward {ItemId=BaseTraining.Book(kind,2),Count=1});
            }
            if(rewards.Any(reward=>!BaseBattleLoot.ValidReward(s,reward)))throw new InvalidOperationException("遗迹默认掉落未接入现有道具目录");
            if (!automatic) BaseBattleLoot.RequireRoom(s,rewards);
            s.Campaign.SettledIds.Add(id);
            int coins=r.Coins+(first?r.FirstCoins:0),food=first?r.FirstFood:0,materials=first?r.FirstMaterials:0;
            string title = route == BasePrepRoute.Hideout ? BaseBanditHideout.Title+" · 挑战奖励" : "遗迹挑战奖励";
            BaseBattleLoot.AppendRewards(s,battle,id,title,food,materials,coins,rewards);
            s.Inventory.Coins+=coins;s.Inventory.Food+=food;s.Inventory.Materials+=materials;r.Energy-=PendingCost(s,route);
            Skip(s,route);
            return true;
        }
        public static bool Skip(BaseState s, BasePrepRoute route = BasePrepRoute.Ruins)
        {
            if (PendingBattle(s,route)=="") return false;
            var rewards = s.Demo.RuinsRewards;
            if (route == BasePrepRoute.Hideout) rewards.HideoutPendingBattleId = "";
            else { rewards.PendingBattleId = ""; rewards.PendingFirstClear = false; }
            return true;
        }
        public static void Validate(BaseState s)
        {
            var r=s.Demo.RuinsRewards;if(r==null)return;
            r.HideoutPendingBattleId ??= "";
            r.HideoutFirstClearBattleId ??= "";
            if (r.HideoutFirstClearBattleId == "") MigrateHideoutVictory(s, r);
            if(new[]{r.FirstCoins,r.FirstFood,r.FirstMaterials}.Any(n=>n<0||n>1000000)||r.EnemyGeneration<0||r.PendingFirstClear&&r.PendingBattleId=="")throw new ArgumentException("遗迹首通奖励／敌军生成批次非法");
            if(r.Version!=1||r.MaximumEnergy<1||r.MaximumEnergy>10000||r.RewardCost<1||r.RewardCost>r.MaximumEnergy||r.Energy<0||r.Energy>r.MaximumEnergy||r.EnergyDay<1||r.EnergyDay>s.Day||r.Coins<0||r.Coins>1000000||r.ItemCount<1||r.ItemCount>99||double.IsNaN(r.ExposureFraction)||double.IsInfinity(r.ExposureFraction)||r.ExposureFraction<=0||r.ExposureFraction>1||r.PendingBattleId==null||r.PendingBattleId!=""&&!s.Campaign.Reports.Any(report=>report.BattleId==r.PendingBattleId&&report.EventId==BaseDemoRuins.EventId&&report.Outcome=="SideAWin"))throw new ArgumentException("遗迹体力／掉落／侵蚀快照非法");
            bool ValidHideoutVictory(string battle) => battle == "" ||
                s.Campaign.SettledIds.Contains(battle) && s.Campaign.SettledIds.Contains(HideoutVictoryReceipt(battle));
            if (!ValidHideoutVictory(r.HideoutFirstClearBattleId) || !ValidHideoutVictory(r.HideoutPendingBattleId) ||
                r.HideoutPendingBattleId != "" && r.HideoutFirstClearBattleId == "")
                throw new ArgumentException("土匪窝点通关或待领战果非法");
        }

        private static void MigrateHideoutVictory(BaseState s, BaseRuinsRewardState rewards)
        {
            var mission = s.Campaign.Events.FirstOrDefault(item => item.Id == BaseBanditHideout.EventId);
            if (mission == null) return;
            string battle = FirstVictory(s, BasePrepRoute.Hideout)?.BattleId ??
                s.Aftermath.Loot.FirstOrDefault(item => item.Title == mission.Title &&
                    (item.Coins > 0 || item.Food > 0 || item.Materials > 0 || item.Items.Count > 0))?.BattleId;
            if (battle == null && mission.Status == "resolved") battle = LegacyHideoutClear;
            if (battle == null) return;
            if (!s.Campaign.SettledIds.Contains(battle)) s.Campaign.SettledIds.Add(battle);
            AddHideoutReceipt(s,battle);
            rewards.HideoutFirstClearBattleId = battle;
        }

        private static void AddHideoutReceipt(BaseState s, string battle)
        {
            string receipt = HideoutVictoryReceipt(battle);
            if (!s.Campaign.SettledIds.Contains(receipt)) s.Campaign.SettledIds.Add(receipt);
        }
    }
}
