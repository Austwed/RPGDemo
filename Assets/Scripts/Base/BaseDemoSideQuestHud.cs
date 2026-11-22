using System;
using System.Collections.Generic;
using BaseConquest.Story;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private NpcLine SmithLessonLine(string key, string text)
            => new NpcLine(BaseScriptNames.Blacksmith, BaseScriptNames.Portrait("blacksmith"),
                StoryText.Get("story.quest.smith_lesson." + key, text));

        private NpcLine ZhouTrainingLine(string key, string text, params object[] values)
        {
            string line = StoryText.Get("story.quest.zhou_training." + key, text);
            return new NpcLine(BaseScriptNames.LaoZhou, BaseScriptNames.Portrait("laozhou"),
                values.Length == 0 ? line : string.Format(line, values));
        }

        private void OpenSmithLesson()
        {
            NpcDialogue(new[]
            {
                SmithLessonLine("offer.1", "阿铸会给枪换杆，可一碰上炉火就心急。你来，我教你看一回火候。"),
                SmithLessonLine("offer.2", "这几颗锻造石给你。挑件武器或护具，先试着重铸一回。")
            }, new[]
            {
                new NpcReply("收下锻造石", () => CollectSideQuestReward(BaseDemoSideQuests.AcceptSmith, BaseDemoSideQuests.SmithStonesId)),
                new NpcReply("改天再试", OpenSmithFather)
            });
        }

        private void OpenSmithLessonResult()
        {
            NpcDialogue(new[]
            {
                SmithLessonLine("finish.1", "石头用得多，炼出好货的把握就大些。出炉前，谁也不敢打包票。"),
                SmithLessonLine("finish.2", "这一袋一百颗，拿去练手。趁手的家伙先留着，有备用的再上炉。")
            }, new[] { new NpcReply("记下了", () => CollectSideQuestReward(BaseDemoSideQuests.FinishSmith, BaseDemoSideQuests.SmithId)) });
        }

        internal bool OfferSmithLessonOnTradeExit()
        {
            if (!PanelOpen || panelViewKey != TradeViewKey || tradeShelf != TradeShelf.Reforge ||
                BaseDemoSideQuests.Smith(owner.State) != BaseSideQuestStage.Ready) return false;
            OpenSmithLessonResult();
            return true;
        }

        internal const string ZhouConstructionNotice =
            "领主的建设许可下来了，我们可以建一些新设施或者移动设施位置，不过不要把人家的路给挡了。";

        internal static readonly string[] ZhouCampTips =
        {
            "你有时候会看到没士兵工作但还是会有一些产出，那是乡亲们自发来帮忙的，大家都是一家人",
            "如果打不过敌人不妨试试更高级的军需",
            "战斗时要是发现敌人不太好打可以回到出口撤离",
            "士兵要是没饭吃会自己走人的，他们也有自己的小家要养",
            "上了战场的士兵当天没力气继续工作，如果受了重伤不仅不能工作还不能外出战斗",
            "军需要用原料造，原料目前需要找商人购买，或许以后会有路子自给自足",
            "要是没钱没兵一穷二白，你可以带上伙伴去遗迹多探索几次，总会有收获的",
            "可以找商人买小道消息，虽然有点贵，但是或许对战斗规划会有所帮助",
            "不要囤积太多士兵，多出来的士兵没地方工作还会消耗粮草，而且镇上的募兵官会担心你势力太大，从而减少兵源并且提高价格",
            "不听老人言，吃亏在眼前"
        };

        private void OpenZhouCamp()
        {
            var state = owner.State;
            bool canBuild = BaseFacilityLayout.CanEnterCampConstruction(state);
            if (!canBuild && OpenZhouTraining()) return;

            bool firstPermitChat = BaseFacilityLayout.NeedsZhouConstructionNotice(state);
            if (firstPermitChat && !owner.Change(next => next.Demo.Flow.ZhouConstructionNoticeSeen = true, "")) return;
            var line = new NpcLine(BaseScriptNames.LaoZhou, BaseScriptNames.Portrait("laozhou"),
                firstPermitChat ? ZhouConstructionNotice : ZhouCampTips[UnityEngine.Random.Range(0, ZhouCampTips.Length)]);
            var replies = new List<NpcReply>();
            if (canBuild) replies.Add(new NpcReply("营地建设", owner.ArmCampPlacement));
            var training = BaseDemoSideQuests.Zhou(state);
            if (canBuild && (training == BaseSideQuestStage.Accepted || training == BaseSideQuestStage.Ready ||
                BaseDemoSideQuests.ZhouOffer(state)))
                replies.Add(new NpcReply(BaseDemoSideQuests.ZhouTitle, () => OpenZhouTraining()));
            replies.Add(new NpcReply("回头见", ClosePanel));
            NpcDialogue(firstPermitChat ? new[] { line } : WithInjury(state, "INJ_CAMP_ZHOU", line), replies.ToArray());
        }

        private bool OpenZhouTraining()
        {
            var s = owner.State;
            var stage = BaseDemoSideQuests.Zhou(s);
            if (stage == BaseSideQuestStage.Ready)
            {
                ShowZhouTrainingResult();
                return true;
            }
            if (stage != BaseSideQuestStage.Accepted && !BaseDemoSideQuests.ZhouOffer(s)) return false;

            bool accepted = stage == BaseSideQuestStage.Accepted;
            var player = BaseDemoSideQuests.Player(s);
            int target = BaseDemoSideQuests.ZhouTarget(s);
            string book = BaseDemoSideQuests.ZhouBookName(s, target);
            int count = BaseDemoSideQuests.ZhouBookCount(s, target);
            if (!accepted || BaseDemoSideQuests.ZhouNeedsBooks(s))
            {
                var books = ZhouTrainingLine("offer.books", "这{1}本{0}给你。去营帐练，练完来找我。", book, count);
                var lines = accepted ? new[] { books } : new[]
                {
                    ZhouTrainingLine("offer.1", "这一仗是守住了。下回呢？学几招总比多挨几刀强。"),
                    books
                };
                NpcDialogue(WithInjury(s, "INJ_CAMP_ZHOU", lines), new[]
                {
                    new NpcReply("收下兵书", () => CollectSideQuestReward(
                        accepted ? BaseDemoSideQuests.ClaimZhouBooks : BaseDemoSideQuests.AcceptZhou, BaseDemoSideQuests.ZhouBooksId)),
                    new NpcReply("回头见", ClosePanel)
                });
            }
            else
            {
                var reminder = BaseTraining.Busy(player.Training)
                    ? new NpcLine(BaseScriptNames.LaoZhou, BaseScriptNames.Portrait("laozhou"),
                        string.Format(StoryText.Get("story.quest.zhou_training.wait", "慢慢练。等到第{0}天，你再来找我。"), player.Training.EndDay))
                    : ZhouTrainingLine("reminder", "去营帐用兵书训练。练完再来找我。");
                NpcDialogue(WithInjury(s, "INJ_CAMP_ZHOU", reminder), new[] { new NpcReply("回头见", ClosePanel) });
            }
            return true;
        }

        private void ShowZhouTrainingResult()
        {
            var state = owner.State;
            int next = BaseDemoSideQuests.ZhouTarget(state) + 1;
            var trained = ZhouTrainingLine("finish.1", "这架势稳了。书上的招肯下功夫练，到了阵前才使得出来。");
            if (next > 9)
            {
                NpcDialogue(new[] { trained }, new[] { new NpcReply("记下了", () =>
                {
                    if (CommitSideQuest(BaseDemoSideQuests.FinishZhou)) ClosePanel();
                }) });
                return;
            }
            var books = ZhouTrainingLine("finish.books", "这{1}本{0}也拿去，够你接着练一级。",
                BaseDemoSideQuests.ZhouBookName(state, next), BaseDemoSideQuests.ZhouBookCount(state, next));
            NpcDialogue(new[] { trained, books }, new[]
            {
                new NpcReply("收下兵书", () => CollectSideQuestReward(BaseDemoSideQuests.FinishZhou, BaseDemoSideQuests.ZhouId))
            });
        }

        private bool CommitSideQuest(Func<BaseState, bool> action) => owner.Change(next =>
        {
            if (!action(next)) throw new InvalidOperationException("请靠近任务人物后再试");
        }, "");

        private void CollectSideQuestReward(Func<BaseState, bool> action, string reward)
        {
            if (CommitSideQuest(action)) OpenLoot(reward);
        }
    }
}
