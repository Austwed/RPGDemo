using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.Base
{
    // Demo 的任务册与场景指引读取同一任务目标；只有可用支线可以手动覆盖主线。
    // Phase 是界面用的五态，只决定状态文字的配色与追踪按钮的可用性，不参与任何存档判定。
    public enum QuestPhase { Available, Active, Ready, Done, Repeatable }
    public sealed class BaseQuestEntry
    {
        public string Id, Title, Status, Goal, Reward;
        public bool Main, Done;
        public QuestPhase Phase;
        public bool Trackable => !Done;
    }
    public readonly struct BaseQuestCue
    {
        public readonly string Id, Instruction, ObjectId, Control;
        public BaseQuestCue(string id, string instruction, string objectId = null, string control = null)
        { Id = id; Instruction = instruction; ObjectId = objectId; Control = control; }
    }
    public static class BaseQuestLog
    {
        public static string TrackedId(BaseState s) => s?.Quests == null ? "" : s.Quests.TrackedId ?? "";
        public static bool Collapsed(BaseState s) => s?.Quests != null && s.Quests.TrackedCollapsed;
        public static bool ToggleTrack(BaseState s, string id)
        {
            if (s?.Quests == null || string.IsNullOrEmpty(id)) return false;
            if (BaseDemo.Active(s) && !Entries(s).Any(entry => entry.Id == id && !entry.Main && entry.Trackable)) return false;
            s.Quests.TrackedId = s.Quests.TrackedId == id ? "" : id;
            return true;
        }
        public static void SetCollapsed(BaseState s, bool collapsed)
        {
            if (s?.Quests != null) s.Quests.TrackedCollapsed = collapsed;
        }
        // 常驻栏只显示还没完成、且仍被追踪的那一条；任务完成后自动让位，不残留。
        public static BaseQuestEntry Tracked(BaseState s)
        {
            string id = TrackedId(s);
            return string.IsNullOrEmpty(id) ? null : Entries(s).FirstOrDefault(entry => entry.Id == id && !entry.Done);
        }
        public static List<BaseQuestEntry> Entries(BaseState s)
        {
            var list = new List<BaseQuestEntry>();
            if (s == null) return list;
            if (BaseDemo.Active(s))
            {
                Demo(s, list);
                var main = MainCue(s);
                foreach (var entry in list)
                {
                    var cue = entry.Main ? main : SideCue(s, entry.Id);
                    if (entry.Id == cue.Id) entry.Goal = cue.Instruction;
                }
                return list;
            }
            if (s.Quests != null && (s.Quests.AcceptedDay > 0 || BaseQuests.Ready(s)))
                list.Add(new BaseQuestEntry
                {
                    Id = "quest:town_defence", Main = true, Title = BaseQuests.Title, Status = BaseQuests.Status(s),
                    Goal = "亲自赢得 1 场迎战／进攻，或让荒野据点归属己方。", Reward = "营造图册 ×1 与营造入门剧情条件",
                    Done = s.Quests.ClaimedDay > 0,
                    Phase = s.Quests.ClaimedDay > 0 ? QuestPhase.Done : BaseQuests.Ready(s) ? QuestPhase.Ready : QuestPhase.Active
                });
            return list;
        }
        public static BaseQuestCue Cue(BaseState s)
        {
            if (!BaseDemo.Active(s) || s.Aftermath.GameOver || s.Campaign.HasActiveBattle || BaseSceneCombat.Active(s)) return default;
            var side = SideCue(s, TrackedId(s));
            return side.Id != null ? side : MainCue(s);
        }

        internal static void NormalizeDemoTrack(BaseState s)
        {
            if (BaseDemo.Active(s) && s.Quests != null && TrackedId(s) != "" && SideCue(s, TrackedId(s)).Id == null)
                s.Quests.TrackedId = "";
        }

        private static BaseQuestCue SideCue(BaseState s, string id)
        {
            if (BaseDemoPrologue.Village(s) || s.Demo.Stage == BaseDemoStage.Waking) return default;
            switch (id)
            {
                case "demo:partner_farm":
                    if (!BaseDemoRuins.Enabled(s) || s.Leaders.Any(l => l.Id == "partner_farm")) return default;
                    return s.Campaign.Location == "base"
                        ? new BaseQuestCue(id, "去营地粮田旁找阿禾，邀请他同行", "demo_ahe")
                        : ToCamp(s, id, "回营地找阿禾");
                case "demo:partner_craft":
                    if (!BaseDemoRuins.Enabled(s) || s.Leaders.Any(l => l.Id == "partner_craft")) return default;
                    return ToTown(s, id, "去铁匠铺找阿铸，切磋后邀请他同行", "demo_azhu");
                case "demo:ruins_trial":
                    if (s.Demo.RuinsDay > 0) return ToRuins(s, id, "在地下回廊挑战或扫荡；领奖消耗体力");
                    break;
                case "demo:camp_supplies":
                    if (!BaseDemoFinale.OccupiedBySecondDefense(s) || s.Demo.Finale.CampSuppliesTaken) return default;
                    if (!BaseDemoFinale.Opened(s)) return ToCamp(s, id, "从营地南侧出口进入世界道路", "demo_march_exit");
                    if (s.Campaign.CurrentMapId == BaseDemoFinale.Camp)
                        return new BaseQuestCue(id, "搜查匪营剩下的物资", BaseDemoFinale.CampSuppliesMarker);
                    if (s.Campaign.CurrentMapId == BaseDemoFinale.Home)
                        return new BaseQuestCue(id, "返回旧敌营搜查剩下的物资", "demo_direct_camp");
                    if (s.Campaign.Location == "world") return new BaseQuestCue(id, "沿世界道路前往旧敌营", "bandit_entry");
                    return ToCamp(s, id, "前往匪营搜查剩下的物资", "demo_march_exit");
            }
            return default;
        }

        private static BaseQuestCue ToCamp(BaseState s, string id, string goal, string campTarget = null)
        {
            if (BaseSceneRules.Dungeon(s)) return new BaseQuestCue(id, "先离开地下回廊", "demo_dungeon_exit");
            if (BaseSceneRules.Entrance(s) || s.Campaign.Location == "outpost" && !BaseDemoFinale.Opened(s))
                return new BaseQuestCue(id, "从遗迹入口返回" + BaseScriptNames.Town, "demo_ruins_return");
            var target = s.Campaign.Location switch
            {
                "base" => campTarget, "town" => "travel_base", "perimeter" => "demo_defense_return",
                "world" => "camp_entry", _ => s.Campaign.CurrentMapId == BaseDemoFinale.Camp ? "demo_camp_world" :
                    s.Campaign.CurrentMapId == BaseDemoFinale.Home ? "demo_home_world" : null
            };
            return new BaseQuestCue(id, goal, target);
        }

        private static BaseQuestCue ToTown(BaseState s, string id, string goal, string townTarget)
        {
            if (s.Campaign.Location == "town") return new BaseQuestCue(id, goal, townTarget);
            if (s.Campaign.Location == "base") return new BaseQuestCue(id, "从东侧出口前往" + BaseScriptNames.Town, "exit_east");
            return ToCamp(s, id, "返回" + BaseScriptNames.Town);
        }

        private static BaseQuestCue ToRuins(BaseState s, string id, string goal)
        {
            if (BaseSceneRules.Dungeon(s)) return new BaseQuestCue(id, goal, "demo_ruins_investigate");
            if (BaseSceneRules.Entrance(s) || s.Campaign.Location == "outpost" && !BaseDemoFinale.Opened(s))
                return new BaseQuestCue(id, BaseSceneRules.Active(s) ? "进入遗迹地下回廊" : goal, "demo_ruins_investigate");
            return ToTown(s, id, "从城镇东侧出口前往遗迹", "demo_ruins_exit");
        }

        private static BaseQuestCue MainCue(BaseState s)
        {
            const string intro = "demo:intro", prologue = "demo:prologue", ruins = "demo:ruins", defend = "demo:defend", attack = "demo:attack";
            var demo = s.Demo;
            if (BaseDemoPrologue.Village(s))
            {
                if (demo.Stage == BaseDemoStage.PrologueWounded) return new BaseQuestCue(prologue, "继续对白", control: "dialogue");
                if (demo.Stage == BaseDemoStage.PrologueEscape) return new BaseQuestCue(prologue, "护送阿禾与村民从东山口撤离", "prologue_trigger");
                if (!BaseDemoIntro.Active(s)) return new BaseQuestCue(prologue, "向报信村民了解敌情", "prologue_report");
                var i = demo.Intro;
                return i.Step switch
                {
                    0 => new BaseQuestCue(intro, "选择主角并确认", control: "hero"),
                    1 => new BaseQuestCue(intro, "与" + BaseDemoIntro.PartnerName(s) + "交谈", "demo_captain"),
                    2 => new BaseQuestCue(intro, "去麦田旁和阿禾打招呼", "intro_villager"),
                    9 => new BaseQuestCue(intro, "为粮田调配 4 名工人并确认", "intro_farm", "farm"),
                    10 => new BaseQuestCue(intro, "找" + BaseDemoIntro.PartnerName(s) + "领取佩剑", "demo_captain"),
                    3 => new BaseQuestCue(intro, "打开背包，穿戴旧制短剑", control: "sword"),
                    4 => new BaseQuestCue(intro, "和搭档交谈，开始民兵演练", "demo_captain"),
                    5 => new BaseQuestCue(intro, "等待报信村民抵达"),
                    6 => i.ReporterArrived ? new BaseQuestCue(prologue, "与报信村民交谈", "prologue_report") : new BaseQuestCue(prologue, "等待报信村民抵达"),
                    7 => new BaseQuestCue(prologue, "跟着搭档，准备护送村民", "demo_captain"),
                    _ => new BaseQuestCue(prologue, "掩护村民撤离")
                };
            }
            if (demo.Stage == BaseDemoStage.Waking) return new BaseQuestCue(ruins, "了解在断流滩醒来后的处境", "home");
            if (demo.Stage == BaseDemoStage.VisitLord) return ToTown(s, ruins, "拜访霍恩爵士，听取调查委托", "town_quest");
            if (demo.Stage == BaseDemoStage.RuinsAssigned) return ToRuins(s, ruins, "调查古窟并救出村民");
            if (demo.Stage == BaseDemoStage.RuinsCleared)
            {
                if (BaseSceneRules.Entrance(s) && demo.Rescue?.Stage == 1)
                    return new BaseQuestCue(ruins, "和获救村民交谈", "demo_rescued_1");
                return ToTown(s, ruins, "向领主交付调查结果", "town_quest");
            }
            if (demo.Stage == BaseDemoStage.Finished) return default;
            if (BaseDemoDefense.AnyDue(s)) return s.Campaign.Location == "perimeter"
                ? new BaseQuestCue(defend, "在迎敌战场确认出战", "demo_defense_start")
                : ToCamp(s, defend, "从西侧出口前往迎敌战场", "demo_defense_exit");
            if (!BaseDemoDefense.Won(s))
            {
                if (BaseDemoFlow.Active(s) && !BaseDemoFlow.Recruited(s))
                    return ToTown(s, "demo:recruit", "去募兵处招齐剑士、弓兵和骑兵", "town_recruit");
                if (!demo.Defense.WorkshopRepaired) return ToCamp(s, "demo:rebuild", "修复营地工坊", "demo_fix_workshop");
                if (!demo.Defense.BarracksRepaired) return ToCamp(s, "demo:rebuild", "修复营地兵营", "demo_fix_barracks");
                return ToCamp(s, defend, "在营帐查看日结，整备并等待来袭", "home");
            }
            if (demo.Defense.BatuStoryDay == 0 || BaseDemoFinale.SecondRaidReportPending(s))
                return new BaseQuestCue(attack, "听取战后报告", control: "dialogue");
            if (!BaseDemoFinale.Enabled(s)) return default;
            if (!BaseDemoFinale.Opened(s)) return ToCamp(s, attack, "从营地南侧出口进入世界道路", "demo_march_exit");
            if (s.Campaign.CurrentMapId == BaseDemoFinale.Home)
                return BaseDemoFinale.Rebuilding(s) ? new BaseQuestCue(attack, "建好小粮田、营帐和仓库", control: "rebuild") :
                    new BaseQuestCue(attack, "调查村中的断剑", "demo_broken_sword");
            if (BaseDemoFinale.Later(s))
            {
                if (s.Campaign.Location == "world") return new BaseQuestCue(attack, "沿世界道路前往" + BaseScriptNames.Village, "home_entry");
                if (s.Campaign.CurrentMapId == BaseDemoFinale.Camp)
                    return new BaseQuestCue(attack, "前往" + BaseScriptNames.Village, "demo_direct_home");
            }
            else
            {
                if (s.Campaign.Location == "world") return new BaseQuestCue(attack, "率队前往" + BaseScriptNames.Bandit, "bandit_entry");
                if (s.Campaign.CurrentMapId == BaseDemoFinale.Camp)
                    return new BaseQuestCue(attack, "在营地前确认进攻", "demo_camp_attack");
            }
            return ToCamp(s, attack, "从南侧出口前往世界道路", "demo_march_exit");
        }
        private static void Demo(BaseState s, List<BaseQuestEntry> list)
        {
            string main = BaseDemoPrologue.Village(s) ? "守护" + BaseScriptNames.Village : "追查失踪的队长";
            if (BaseDemoIntro.Daily(s))
                list.Add(new BaseQuestEntry { Id = "demo:intro", Main = true, Title = BaseScriptNames.Village + "的清晨", Status = "进行中", Goal = BaseDemoIntro.Objective(s), Reward = "与搭档巡村，完成日常准备", Phase = QuestPhase.Active });
            else if (BaseDemoPrologue.Village(s))
                list.Add(new BaseQuestEntry { Id = "demo:prologue", Main = true, Title = "掩护百姓撤离", Status = s.Demo.Stage == BaseDemoStage.PrologueBriefing ? "待听取报告" : "进行中", Goal = "带村民撤离，阻挡追兵。", Reward = "序章剧情推进", Done = s.Demo.Stage > BaseDemoStage.PrologueWounded, Phase = s.Demo.Stage > BaseDemoStage.PrologueWounded ? QuestPhase.Done : s.Demo.Stage == BaseDemoStage.PrologueBriefing ? QuestPhase.Available : QuestPhase.Active });
            else
            {
                var reward = s.Demo.RuinsReward;
                list.Add(new BaseQuestEntry
                {
                    Id = "demo:ruins", Main = true, Title = BaseScriptNames.Ruins + "调查与失踪者",
                    Status = s.Demo.DeliveredDay > 0 ? "已领取任务奖赏" : s.Demo.RuinsDay > 0 ? "已完成，可向领主交差" : s.Demo.AcceptedDay > 0 ? "已接取" : "可向领主求助，也可先探索",
                    Goal = "击败控制来源，救助两名百姓；未接任务也可先完成调查。",
                    Reward = reward == null ? "重建许可" : "重建许可、钱币 " + reward.Coins + "、粮食 " + reward.Food + "、原料 " + reward.Materials,
                    Done = s.Demo.DeliveredDay > 0,
                    Phase = s.Demo.DeliveredDay > 0 ? QuestPhase.Done : s.Demo.RuinsDay > 0 ? QuestPhase.Ready : s.Demo.AcceptedDay > 0 ? QuestPhase.Active : QuestPhase.Available
                });
                if (BaseDemoDefense.Delivered(s))
                {
                    list.Add(new BaseQuestEntry { Id = "demo:recruit", Main = true, Title = "招募建设人手", Status = BaseDemoFlow.Recruited(s) ? "已完成" : "进行中", Goal = BaseDemoFlow.RecruitProgress(s), Reward = "开放经营教学", Done = BaseDemoFlow.Recruited(s), Phase = BaseDemoFlow.Recruited(s) ? QuestPhase.Done : QuestPhase.Active });
                    bool rebuilt = s.Demo.Defense.WorkshopRepaired && s.Demo.Defense.BarracksRepaired;
                    list.Add(new BaseQuestEntry { Id = "demo:rebuild", Main = true, Title = "重建营地", Status = rebuilt ? "已修复" : "进行中", Goal = "修复工坊和兵营，安排生产。", Reward = "军需生产与迎战准备", Done = rebuilt, Phase = rebuilt ? QuestPhase.Done : QuestPhase.Active });
                }
                if (BaseDemoDefense.Delivered(s))
                    list.Add(new BaseQuestEntry { Id = "demo:defend", Main = true, Title = "守住新营地", Status = BaseDemoDefense.Won(s) ? "已完成" : "进行中", Goal = BaseDemoDefense.Objective(s), Reward = "攻营与返乡道路线索", Done = BaseDemoDefense.Won(s), Phase = BaseDemoDefense.Won(s) ? QuestPhase.Done : QuestPhase.Active });
                if (BaseDemoDefense.Won(s))
                    list.Add(new BaseQuestEntry { Id = "demo:attack", Main = true, Title = BaseDemoFinale.OccupiedBySecondDefense(s) ? "返乡调查" : "攻营与返乡", Status = s.Demo.Stage == BaseDemoStage.Finished ? "已完成" : "进行中", Goal = BaseDemoFinale.Objective(s), Reward = "断剑与幸存者证词", Done = s.Demo.Stage == BaseDemoStage.Finished, Phase = s.Demo.Stage == BaseDemoStage.Finished ? QuestPhase.Done : QuestPhase.Active });
            }
            if (BaseDemoPrologue.Village(s) || s.Demo.Stage == BaseDemoStage.Waking) return;
            foreach (var id in BaseDemoRuins.Enabled(s) ? new[] { "partner_farm", "partner_craft" } : Array.Empty<string>())
            {
                bool joined = s.Leaders.Any(leader => leader.Id == id);
                list.Add(new BaseQuestEntry
                {
                    Id = "demo:" + id, Main = false,
                    Title = id == "partner_farm" ? "阿禾的邀请" : "阿铸的承诺",
                    Status = BaseAdventure.Contains(s, id) ? "正在同行" : joined ? "已结识，留在营地" : s.Demo.MetCompanions.Contains(id) ? "已交谈，可邀请" : "尚未交谈",
                    Goal = id == "partner_farm" ? "在营地认识阿禾。他是普通人，" + BaseScriptNames.Ruins + "侵蚀会伤害他。" : "在" + BaseScriptNames.Town + "与阿铸交谈、切磋后邀请同行。",
                    Reward = "多一名可带队队长", Done = joined, Phase = joined ? QuestPhase.Done : QuestPhase.Available
                });
            }
            if (s.Demo.RuinsDay > 0)
                list.Add(new BaseQuestEntry
                {
                    Id = "demo:ruins_trial", Main = false, Title = BaseScriptNames.Ruins + "试炼", Status = "可重复挑战",
                    Goal = "体力 " + BaseRuinsRewards.Energy(s) + " / " + BaseRuinsRewards.Maximum(s) + "，每日按 " + BaseRuinsRewards.DailyRecovery + " 点恢复；无体力也能挑战，但不能领奖。",
                    Reward = "装备与兵书掉落", Phase = QuestPhase.Repeatable
                });
            if (BaseDemoFinale.OccupiedBySecondDefense(s))
            {
                bool taken = s.Demo.Finale.CampSuppliesTaken;
                list.Add(new BaseQuestEntry { Id = "demo:camp_supplies", Title = "匪营遗留物资", Status = taken ? "已搜查" : "可搜查",
                    Goal = "前往匪营搜查剩下的物资。", Reward = "匪营物资", Done = taken, Phase = taken ? QuestPhase.Done : QuestPhase.Available });
            }
        }
    }
}
