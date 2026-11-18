using System;
using BaseConquest.Story;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private NpcLine SmithLessonLine(string key, string text)
            => new NpcLine(BaseScriptNames.Blacksmith, BaseScriptNames.Portrait("blacksmith"),
                StoryText.Get("story.quest.smith_lesson." + key, text));

        private NpcLine ZhouTrainingLine(string key, string text)
            => new NpcLine(BaseScriptNames.LaoZhou, BaseScriptNames.Portrait("laozhou"),
                StoryText.Get("story.quest.zhou_training." + key, text));

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
                SmithLessonLine("finish.2", "趁手的家伙只有一件，就先留着。等手里有备用的，再拿这颗石头试。")
            }, new[] { new NpcReply("记下了", () => CollectSideQuestReward(BaseDemoSideQuests.FinishSmith, BaseDemoSideQuests.SmithId)) });
        }

        internal bool OfferSmithLessonOnTradeExit()
        {
            if (!PanelOpen || panelViewKey != TradeViewKey || tradeShelf != TradeShelf.Reforge ||
                BaseDemoSideQuests.Smith(owner.State) != BaseSideQuestStage.Ready) return false;
            OpenSmithLessonResult();
            return true;
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
            string book = BaseDemoSideQuests.ZhouBookName(s, 2);
            int count = BaseTraining.Books(player, 2);
            var lines = new[]
            {
                ZhouTrainingLine("offer.1", "这一仗是守住了。下回呢？学几招总比多挨几刀强。"),
                new NpcLine(BaseScriptNames.LaoZhou, BaseScriptNames.Portrait("laozhou"),
                    string.Format(StoryText.Get("story.quest.zhou_training.offer.camp", "老莫那儿有{0}。凑齐{1}本，就去营帐练。练完来找我。"), book, count))
            };
            if (accepted)
            {
                var reminder = BaseTraining.Busy(player.Training)
                    ? new NpcLine(BaseScriptNames.LaoZhou, BaseScriptNames.Portrait("laozhou"),
                        string.Format(StoryText.Get("story.quest.zhou_training.wait", "慢慢练。等到第{0}天，你再来找我。"), player.Training.EndDay))
                    : lines[1];
                NpcDialogue(WithInjury(s, "INJ_CAMP_ZHOU", reminder), new[] { new NpcReply("回头见", ClosePanel) });
            }
            else
            {
                NpcDialogue(WithInjury(s, "INJ_CAMP_ZHOU", lines), new[]
                {
                    new NpcReply("我去营帐练练", () =>
                    {
                        if (!CommitSideQuest(BaseDemoSideQuests.AcceptZhou)) return;
                        if (BaseDemoSideQuests.Zhou(owner.State) == BaseSideQuestStage.Ready) ShowZhouTrainingResult();
                        else ClosePanel();
                    }),
                    new NpcReply("回头见", ClosePanel)
                });
            }
            return true;
        }

        private void ShowZhouTrainingResult()
        {
            NpcDialogue(new[]
            {
                ZhouTrainingLine("finish.1", "这架势稳了。书上的招肯下功夫练，到了阵前才使得出来。"),
                ZhouTrainingLine("finish.2", "我箱角下压着本旧兵书。你拿去，我换块木头垫就是。")
            }, new[] { new NpcReply("收下旧兵书", () => CollectSideQuestReward(BaseDemoSideQuests.FinishZhou, BaseDemoSideQuests.ZhouId)) });
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
