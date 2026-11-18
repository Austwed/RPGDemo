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
                SmithLessonLine("offer.2", "桌上那几颗锻造石送你。拿件武器或护具来，重铸一次，我再跟你说这里头的门道。")
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
                SmithLessonLine("finish.1", "这炉算你亲手试过了。添石能让升档更有把握，可火候没人说得准。"),
                SmithLessonLine("finish.2", "手上只有一件趁手家伙，就先留着打仗。这颗石头等你有了备用的，再拿来练。")
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
                    string.Format(StoryText.Get("story.quest.zhou_training.offer.camp", "老莫那儿有{0}。买够{1}本去营帐练，练成了回来找我。"), book, count))
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
                    new NpcReply("我去兵营练一回", () =>
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
                ZhouTrainingLine("finish.2", "我箱子角上还压着一本旧书，是早年留下的。给你吧，搁我那儿也只剩垫箱子的用处。")
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
