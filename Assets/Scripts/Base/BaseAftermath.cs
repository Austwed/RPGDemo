using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
namespace BaseConquest.Base
{
    [Serializable] public sealed class BaseDifficulty
    {
        public int Version = 1;
        private const string PresetKey="BaseConquest.GlobalDifficulty";
        public string Preset
        {
            get {var value=UnityEngine.PlayerPrefs.GetString(PresetKey,"normal");return value=="easy"||value=="hard"?value:"normal";}
            set {if(!new[]{"easy","normal","hard"}.Contains(value))throw new ArgumentException("未知难度");UnityEngine.PlayerPrefs.SetString(PresetKey,value);UnityEngine.PlayerPrefs.Save();}
        }
        // Demo 快速收尾阶段的裁决：难度只影响战斗伤害（含遗迹侵蚀受伤），即死率／劝降率／奖励倍率／掉落率一律与难度解耦。
        // 下列六个字段仅为存档兼容保留，取值恒等于 Neutral* 基准（等同原 normal 档）；特殊场景仍可临时覆盖（如遗迹战把 DropRate 置 0）。
        // 2026-11-01 再裁：招降概率取消，LeaderRecruit／TroopRecruit 已无读取点，字段留作旧档兼容。
        public const double NeutralLeaderDeath = 0.05, NeutralTroopDeath = 0.65, NeutralLeaderRecruit = 0.2, NeutralTroopRecruit = 0.75, NeutralRewardFactor = 1, NeutralDropRate = 0.8;
        public double LeaderDeath = NeutralLeaderDeath, TroopDeath = NeutralTroopDeath, LeaderRecruit = NeutralLeaderRecruit, TroopRecruit = NeutralTroopRecruit, RewardFactor = NeutralRewardFactor, DropRate = NeutralDropRate;
        public BaseDifficulty Copy() => (BaseDifficulty)MemberwiseClone();
        public void Neutralize()
        {
            LeaderDeath = NeutralLeaderDeath; TroopDeath = NeutralTroopDeath; LeaderRecruit = NeutralLeaderRecruit;
            TroopRecruit = NeutralTroopRecruit; RewardFactor = NeutralRewardFactor; DropRate = NeutralDropRate;
        }
        public void Set(string preset)
        {
            if (!new[] { "easy", "normal", "hard" }.Contains(preset)) throw new ArgumentException("未知难度");
            Preset = preset; Neutralize();
        }
    }
    [Serializable] public sealed class BasePrisoner
    {
        public string Id, BattleId, Name, Status = "held";
        public string CaptureSource="";
        public int CaptureDay;
        public bool IsAlien,IsAwakened;
        public RpgCharacter Rpg;
        public string AppearanceId = "";
        public SoldierKind Kind;
        public double Health;
        public SoldierStats Stats;
        public BasePrisoner Copy() { var p = (BasePrisoner)MemberwiseClone(); p.Stats = Stats.Clone();p.Rpg=Rpg?.Copy(); return p; }
    }
    [Serializable] public sealed class BaseAftermath
    {
        public int Version = 1;
        public bool GameOver;
        public string Reason = "";
        public BaseDifficulty Difficulty = new BaseDifficulty();
        public List<BasePrisoner> Prisoners = new List<BasePrisoner>();
        public List<BaseLootManifest> Loot = new List<BaseLootManifest>();
        public List<BaseLostItems> LostItems=new List<BaseLostItems>();
        public BaseAftermath Copy() => new BaseAftermath { Version = Version, GameOver = GameOver, Reason = Reason, Difficulty = Difficulty.Copy(), Prisoners = Prisoners.Select(p => p.Copy()).ToList(), Loot=Loot.Select(m=>m.Copy()).ToList(),LostItems=LostItems?.Select(x=>x.Copy()).ToList() };
        // 旧档里六个字段可能仍存着 easy/hard 的旧值；读档与每次校验前统一归位到基准，难度解耦才算真正生效。
        public static void Normalize(BaseState s) { if (s?.Aftermath?.Difficulty != null) s.Aftermath.Difficulty.Neutralize();if(s?.Aftermath?.GameOver==true&&s.Aftermath.Reason=="待写"&&s.Demo?.Defense?.SecondOutcome=="failed")s.Aftermath.Reason=BaseDemoDefense.SecondGameOverReason; }
        public static void Validate(BaseState s)
        {
            if(s.Aftermath==null || s.Aftermath.Version==0 && !s.Aftermath.GameOver && (s.Aftermath.Prisoners==null||s.Aftermath.Prisoners.Count==0)) s.Aftermath=new BaseAftermath(); var a = s.Aftermath; var d = a.Difficulty;
            if (a.Version != 1 || d == null || d.Version != 1 || !new[] { "easy", "normal", "hard" }.Contains(d.Preset) || new[] { d.LeaderDeath, d.TroopDeath, d.LeaderRecruit, d.TroopRecruit, d.DropRate }.Any(v => double.IsNaN(v) || double.IsInfinity(v) || v < 0 || v > 1) || double.IsNaN(d.RewardFactor) || d.RewardFactor < 0 || d.RewardFactor > 10 || a.Prisoners == null || a.Prisoners.Count > 10000 || a.Prisoners.Any(p => p == null || string.IsNullOrWhiteSpace(p.Id) || !ValidStats(p.Stats) || !Enum.IsDefined(typeof(SoldierKind), p.Kind) || !new[] { "held", "recruited", "released", "executed", "refused" }.Contains(p.Status) || double.IsNaN(p.Health) || p.Health <= 0 || p.Health > p.Stats.Health) || a.Prisoners.Select(p => p.Id).Distinct().Count() != a.Prisoners.Count) throw new ArgumentException("难度／俘虏数据非法");
        }
        public static void MarkKnownRecruits(BaseState s)
        {
            foreach(var l in s.Leaders.Where(l=>l.Id.StartsWith("partner_recruit:")&&s.Aftermath.Prisoners.Any(p=>p.Status=="recruited"&&l.Id=="partner_recruit:"+p.Id)))
            {if(!l.Recruited)l.Name=UnitDisplayNames.RecruitedName(l.Name);l.Recruited=true;if(!l.HasHomePosition){var home=BasePersonnel.BarracksDoor(s);l.HasHomePosition=true;l.HomeX=home.X;l.HomeY=home.Y;}}
        }
        public static bool CanRecruitLeader(BaseState s)=>s.Leaders.Count<100&&BaseRpg.HasRoom(s);
        public static bool Decide(BaseState s, string id, string action)
        {
            BaseEconomy.Validate(s); if (s.Aftermath.GameOver || s.Campaign.HasActiveBattle) return false;
            var p = s.Aftermath.Prisoners.FirstOrDefault(x => x.Id == id && x.Status == "held"); if (p == null) return false;
            if (action == "release" || action == "execute") { p.Status = action == "release" ? "released" : "executed"; return true; }
            if (action != "recruit") return false;
            bool controller=BaseDemoStories.ControllerDialogue(s,id);
            if(controller&&!s.Demo.Flow.ControllerAccepted)return false;
            if (p.Kind == SoldierKind.Hero ? !CanRecruitLeader(s) : s.Workers.Sum(w => w.Count) >= 10000) return false;
            // 2026-11-01 用户裁决：取消招降概率。战场上没死的目标只要容量允许就固定招募成功，
            // 即死率已承担损失角色；特殊剧情（迦兰）由 ControllerAccepted 前置自行把关。
            if (p.Kind == SoldierKind.Hero) {var door=BasePersonnel.BarracksDoor(s);s.Leaders.Add(new BaseLeader { Id = "partner_recruit:" + p.Id, Name = UnitDisplayNames.RecruitedName(p.Name),AcquisitionSource="战后招降 · "+(string.IsNullOrEmpty(p.CaptureSource)?p.BattleId:p.CaptureSource),AcquisitionDay=s.Day,IsAlien=p.IsAlien,IsAwakened=p.IsAwakened, AppearanceId=p.AppearanceId, Recruited=true,HasHomePosition=true,HomeX=door.X,HomeY=door.Y,HasMapPosition=true,MapX=door.X,MapY=door.Y, Health = p.Health, MaxHealth = p.Stats.Health, Resting = false, UnavailableDay = s.Day, HasCombatProfile = true, CombatStats = p.Stats.Clone(), Training=BaseTraining.FromProfile(p.Stats),Rpg=p.Rpg?.Copy(),PersonalEquipment=p.Rpg?.Version!=1?null:new BasePersonalLoadout {Version=1,Weapon=new[]{"sword","bow","spear"}[p.Rpg.Profession],InitialWeapon=new[]{"sword","bow","spear"}[p.Rpg.Profession],BaseStats=p.Stats.Clone()} });}
            else s.Workers.Add(new BaseWorkerBucket { Kind = BaseCampaign.ToBase(p.Kind), Recruited=true, Count = 1, Health = p.Health, MaxHealth = p.Stats.Health, Resting = p.Health < p.Stats.Health * BaseDevelopment.DutyThreshold, UnavailableDay = s.Day, HasCombatProfile = true, CombatStats = p.Stats.Clone() });
            if(BaseRpg.Active(s)&&p.Kind==SoldierKind.Hero&&s.Adventure.Members.Count<BaseAdventure.Capacity)
                JoinRecruit(s,s.Leaders.Last());
            p.Status = "recruited";if(controller)s.Demo.Flow.ControllerPledged=true; BaseDevelopment.Allocate(s); BaseEconomy.Validate(s); return true;
        }
        private static void JoinRecruit(BaseState s,BaseLeader leader)
        {
            s.Adventure.Members.Add(leader.Id);
            var point=BaseSpatial.Nearest(s,new BasePoint(s.PlayerX,s.PlayerY-.9));
            leader.MapX=point.X;leader.MapY=point.Y;leader.MapRegion=s.Campaign.Location;leader.MapId=s.Campaign.CurrentMapId;
            var party=s.Muster.Parties.FirstOrDefault(p=>p.Leaders.Any(r=>r.LeaderId==s.PlayerLeaderId));
            if(party!=null)
            {
                leader.Away=true;leader.PartyId=party.Id;party.Leaders.Add(new BaseMusterLeader{LeaderId=leader.Id,OriginalFacility=""});
                party.CommanderTroops=BaseMuster.Distribute(party.Leaders.Select(r=>new BaseCommanderAllocation{LeaderId=r.LeaderId,Requested=s.Leaders.Single(l=>l.Id==r.LeaderId).DefaultTroops.Copy()}),k=>s.Workers.Where(w=>w.PartyId==party.Id&&w.Kind==k).Sum(w=>w.Count),true);
                party.Preset.SelectLeaders(party.Leaders.Select(r=>r.LeaderId));
            }
            BaseAchievements.PartyChanged(s);
        }
        public static int StableIdentity(string text) { unchecked { int hash = 17; foreach (char c in text) hash = hash * 31 + c; return hash; } }
        public static bool ValidStats(SoldierStats p)=>p!=null&&p.EquipmentStatsValid&&new[]{p.Health,p.Damage,p.Defense,p.CritChance,p.CritMultiplier}.All(v=>!float.IsNaN(v)&&!float.IsInfinity(v))&&p.Health>=1&&p.Health<=100000&&p.Damage>=0&&p.Defense>=0&&p.Range>=1&&p.Range<=12&&p.MoveDistance>=1&&p.MoveDistance<=12&&p.CritChance>=0&&p.CritChance<=1&&p.CritMultiplier>=1&&p.CritMultiplier<=10;
        public static void Capture(BaseState s, BaseCampaignSession session, bool win)
        {
            if (!win || s.Aftermath.GameOver) return;
            foreach (var enemy in session.Battle.Soldiers.Where(u => u.Side == BattleSide.B && !u.DeveloperSpawned && !u.Escaped && !u.Rescued && !u.InstantDead
                && (session.Battle.Scenario.Objective.Kind != ObjectiveKinds.Hold || !u.IsAlive)))
            {
                bool guaranteed=session.Opening.Rpg!=null&&enemy.Kind==SoldierKind.Hero&&enemy.KnockedDown;
                if (!guaranteed&&BattleSimulation.StableRoll(session.Battle.Scenario.RandomSeed, enemy.Id, 23) >= session.Opening.Difficulty.DropRate) continue;
                string id = session.Opening.Id + ":" + enemy.Id;
                if(s.Aftermath.Prisoners.Count>=10000)break;
                if (s.Aftermath.Prisoners.Any(p => p.Id == id)) continue;
                var bare=BaseItemRecovery.Disarm(s,session,enemy);
                var origin=session.Opening.Enemies.FirstOrDefault(member=>member.Rpg?.Id==enemy.Rpg?.Id);
                s.Aftermath.Prisoners.Add(new BasePrisoner { IsAlien=origin?.IsAlien==true,IsAwakened=origin?.IsAwakened==true||enemy.Rpg?.TemplateId.StartsWith("captain_awakened_")==true,CaptureSource=s.Campaign.Events.FirstOrDefault(e=>e.Id==session.Opening.EventId)?.Title??session.Opening.EventId,CaptureDay=s.Day,Rpg=enemy.Rpg?.Copy(),Id = id, BattleId = session.Opening.Id, Name = session.Battle.UnitName(enemy), Kind = enemy.Kind, AppearanceId=enemy.AppearanceId, Health = Math.Min(bare.Health,Math.Max(1, enemy.Health)), Stats = bare });
            }
        }
    }
}
