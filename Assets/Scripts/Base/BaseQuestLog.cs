using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.Base
{
    // 任务追踪（P34，2026-11-04 裁决）：主线／支线分类，选择后进常驻任务栏，再选一次取消，任务栏可折叠。
    // 判定只在这里：任务列表页与常驻栏都读同一份 Entries，不在 HUD 各写一套。
    // Phase 是界面用的五态，只决定状态文字的配色与追踪按钮的可用性，不参与任何存档判定。
    public enum QuestPhase { Available, Active, Ready, Done, Repeatable }
    public sealed class BaseQuestEntry
    {
        public string Id, Title, Status, Goal, Reward;
        public bool Main, Done;
        public QuestPhase Phase;
        public bool Trackable => !Done;
    }
    public static class BaseQuestLog
    {
        public static string TrackedId(BaseState s) => s?.Quests == null ? "" : s.Quests.TrackedId ?? "";
        public static bool Collapsed(BaseState s) => s?.Quests != null && s.Quests.TrackedCollapsed;
        public static bool ToggleTrack(BaseState s, string id)
        {
            if (s?.Quests == null || string.IsNullOrEmpty(id)) return false;
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
            if (BaseDemo.Active(s)) { Demo(s, list); return list; }
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
                if (BaseDemoDefense.Later(s))
                    list.Add(new BaseQuestEntry { Id = "demo:defend", Main = true, Title = "守住新营地", Status = BaseDemoDefense.Won(s) ? "已完成" : "进行中", Goal = BaseDemoDefense.Objective(s), Reward = "攻营与返乡道路线索", Done = BaseDemoDefense.Won(s), Phase = BaseDemoDefense.Won(s) ? QuestPhase.Done : QuestPhase.Active });
                if (BaseDemoDefense.Won(s))
                    list.Add(new BaseQuestEntry { Id = "demo:attack", Main = true, Title = "攻营与返乡", Status = s.Demo.Stage == BaseDemoStage.Finished ? "已完成" : "进行中", Goal = BaseDemoFinale.Objective(s), Reward = "断剑与幸存者证词", Done = s.Demo.Stage == BaseDemoStage.Finished, Phase = s.Demo.Stage == BaseDemoStage.Finished ? QuestPhase.Done : QuestPhase.Active });
            }
            if (BaseDemoPrologue.Village(s)) return;
            foreach (var id in new[] { "partner_farm", "partner_craft" })
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
        }
    }
}
