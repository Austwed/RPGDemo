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
        public bool PendingFirstClear;
        public int FirstCoins=60,FirstFood=20,FirstMaterials=10,EnemyGeneration;
        public BaseRuinsRewardState Copy()=>(BaseRuinsRewardState)MemberwiseClone();
    }
    public static class BaseRuinsRewards
    {
        public static int Energy(BaseState s)=>s.Demo.RuinsRewards?.Energy??120;
        public static int Maximum(BaseState s)=>s.Demo.RuinsRewards?.MaximumEnergy??120;
        public static int Cost(BaseState s)=>s.Demo.RuinsRewards?.RewardCost??20;
        public static double Exposure(BaseState s)=>s.Demo.RuinsRewards?.ExposureFraction??.03;
        public static int PendingCost(BaseState s)=>s.Demo.RuinsRewards?.PendingFirstClear==true?0:Cost(s);
        public static bool FirstRewardPending(BaseState s)
        {var first=s.Campaign.Reports.FirstOrDefault(r=>r.EventId==BaseDemoRuins.EventId&&r.Outcome=="SideAWin");return first!=null&&s.Aftermath.Loot.Any(m=>!m.Closed&&m.BattleId==first.BattleId);}
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
            if(r==null||r.Version!=1||r.MaximumEnergy<1||r.MaximumEnergy>10000||r.RewardCost<1||r.RewardCost>r.MaximumEnergy||r.Energy!=r.MaximumEnergy||r.EnergyDay!=1||r.Coins<0||r.Coins>1000000||r.ItemCount<1||r.ItemCount>99||new[]{r.FirstCoins,r.FirstFood,r.FirstMaterials}.Any(n=>n<0||n>1000000)||double.IsNaN(r.ExposureFraction)||double.IsInfinity(r.ExposureFraction)||r.ExposureFraction<=0||r.ExposureFraction>1||!string.IsNullOrEmpty(r.PendingBattleId)||r.PendingFirstClear||r.EnemyGeneration!=0)throw new ArgumentException("遗迹奖励默认配置非法");
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
        public static string ClaimProblem(BaseState s)
        {
            var r=s.Demo.RuinsRewards;
            if(r==null||string.IsNullOrEmpty(r.PendingBattleId))return "本次没有待领取的遗迹奖励";
            if(!(BaseDemoRuins.InRuins(s)||BaseSceneRules.Entrance(s))||!BasePartyInventory.Peaceful(s))return "请在本次遗迹战后领取";
            if(r.Energy<PendingCost(s))return "体力不足；仍可挑战，但本次不能领取奖励";
            if(s.Campaign.SettledIds.Contains(r.PendingBattleId+":ruins-reward"))return "本次奖励已经领取";
            if(s.Inventory.Coins>100000000-r.Coins-(r.PendingFirstClear?r.FirstCoins:0)||r.PendingFirstClear&&(s.Inventory.Food>100000000-r.FirstFood||s.Inventory.Materials>100000000-r.FirstMaterials))return "资源库存已满";
            return "";
        }
        public static bool Claim(BaseState s)
        {
            if(ClaimProblem(s)!="")return false;
            var r=s.Demo.RuinsRewards;string id=r.PendingBattleId+":ruins-reward";
            string[] equipment={"novice_sword","novice_light_helmet","novice_light_armor","novice_bow","novice_spear"};
            int choice=(int)(BattleSimulation.StableRoll(BaseAftermath.StableIdentity(r.PendingBattleId),0,71)*equipment.Length);
            var rewards=new System.Collections.Generic.List<BaseLootReward> {new BaseLootReward {Template=equipment[choice],RandomQuality=true},new BaseLootReward {ItemId="forging_stone",Count=r.ItemCount}};
            // 2026-11-03：基础兵书改为掉落产出（城镇不再出售）；高阶兵书 Demo 内不产出，因此队长等级上限为 2 级。
            double bookRoll=BattleSimulation.StableRoll(BaseAftermath.StableIdentity(r.PendingBattleId),1,73);
            if(bookRoll<.5)
            {
                var kinds=new[]{SoldierKind.Melee,SoldierKind.Ranged,SoldierKind.Cavalry};
                var kind=kinds[(int)(BattleSimulation.StableRoll(BaseAftermath.StableIdentity(r.PendingBattleId),2,73)*kinds.Length)%kinds.Length];
                rewards.Add(new BaseLootReward {ItemId=BaseTraining.Book(kind,2),Count=1});
            }
            if(rewards.Any(reward=>!BaseBattleLoot.ValidReward(s,reward)))throw new InvalidOperationException("遗迹默认掉落未接入现有道具目录");
            s.Campaign.SettledIds.Add(id);
            int coins=r.Coins+(r.PendingFirstClear?r.FirstCoins:0),food=r.PendingFirstClear?r.FirstFood:0,materials=r.PendingFirstClear?r.FirstMaterials:0;
            BaseBattleLoot.AppendRewards(s,r.PendingBattleId,id,"遗迹挑战奖励",food,materials,coins,rewards);
            s.Inventory.Coins+=coins;s.Inventory.Food+=food;s.Inventory.Materials+=materials;r.Energy-=PendingCost(s);r.PendingBattleId="";r.PendingFirstClear=false;return true;
        }
        public static bool Skip(BaseState s)
        {if(s.Demo.RuinsRewards==null||string.IsNullOrEmpty(s.Demo.RuinsRewards.PendingBattleId))return false;s.Demo.RuinsRewards.PendingBattleId="";s.Demo.RuinsRewards.PendingFirstClear=false;return true;}
        public static void Validate(BaseState s)
        {
            var r=s.Demo.RuinsRewards;if(r==null)return;
            if(new[]{r.FirstCoins,r.FirstFood,r.FirstMaterials}.Any(n=>n<0||n>1000000)||r.EnemyGeneration<0||r.PendingFirstClear&&r.PendingBattleId=="")throw new ArgumentException("遗迹首通奖励／敌军生成批次非法");
            if(r.Version!=1||r.MaximumEnergy<1||r.MaximumEnergy>10000||r.RewardCost<1||r.RewardCost>r.MaximumEnergy||r.Energy<0||r.Energy>r.MaximumEnergy||r.EnergyDay<1||r.EnergyDay>s.Day||r.Coins<0||r.Coins>1000000||r.ItemCount<1||r.ItemCount>99||double.IsNaN(r.ExposureFraction)||double.IsInfinity(r.ExposureFraction)||r.ExposureFraction<=0||r.ExposureFraction>1||r.PendingBattleId==null||r.PendingBattleId!=""&&!s.Campaign.Reports.Any(report=>report.BattleId==r.PendingBattleId&&report.EventId==BaseDemoRuins.EventId&&report.Outcome=="SideAWin"))throw new ArgumentException("遗迹体力／掉落／侵蚀快照非法");
        }
    }
}
