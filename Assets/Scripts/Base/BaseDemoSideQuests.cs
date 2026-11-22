using System.Linq;
using BaseConquest.Story;

namespace BaseConquest.Base
{
    public enum BaseSideQuestStage { Hidden, Accepted, Ready, Complete }

    public static class BaseDemoSideQuests
    {
        public const string SmithId = "demo:smith-lesson", ZhouId = "demo:zhou-training";
        public const string SmithStonesId = "demo:smith-lesson:stones", ZhouBooksId = "demo:zhou-training:books";
        public const int TrialStones = 3, RewardStones = 100;

        public static string SmithTitle => StoryText.Get("story.quest.smith_lesson.title", "炉前试手");
        public static string ZhouTitle => StoryText.Get("story.quest.zhou_training.title", "阵后读兵书");

        public static BaseSideQuestStage Smith(BaseState s)
            => s?.Demo?.Flow?.SmithLesson ?? BaseSideQuestStage.Hidden;

        public static BaseSideQuestStage Zhou(BaseState s)
        {
            var stage = s?.Demo?.Flow?.ZhouTraining ?? BaseSideQuestStage.Hidden;
            return stage == BaseSideQuestStage.Accepted && Player(s)?.Training.Level >= ZhouTarget(s)
                ? BaseSideQuestStage.Ready : stage;
        }

        public static BaseLeader Player(BaseState s) => s.Leaders.FirstOrDefault(l => l.Id == s.PlayerLeaderId);

        public static int ZhouTarget(BaseState s)
        {
            var flow = s.Demo.Flow;
            if (flow.ZhouTrainingTarget > 0) return flow.ZhouTrainingTarget;
            // Saves accepted before the target was stored used level two.
            return flow.ZhouTraining == BaseSideQuestStage.Hidden ? Player(s).Training.Level + 1 : 2;
        }

        public static bool ZhouNeedsBooks(BaseState s) =>
            Zhou(s) == BaseSideQuestStage.Accepted && !s.Campaign.SettledIds.Contains(ZhouBooksId);

        public static string ZhouBook(BaseState s, int target)
            => BaseTraining.Book(BasePersonalEquipment.Stats(Player(s)).ProfessionKind, target);

        public static string ZhouBookName(BaseState s, int target)
            => s.Backpack.Catalog.Single(d => d.Id == ZhouBook(s, target)).Name;

        public static int ZhouBookCount(BaseState s, int target) => BaseTraining.Books(Player(s), target);

        public static bool SmithOffer(BaseState s)
            => BaseDemoFlow.Active(s) && Smith(s) == BaseSideQuestStage.Hidden && s.Leaders.Any(l => l.Id == "partner_craft");

        public static bool ZhouOffer(BaseState s)
            => BaseDemoFlow.Active(s) && Zhou(s) == BaseSideQuestStage.Hidden && BaseDemoDefense.Won(s) &&
                Player(s)?.Training.Level < 9;

        public static bool HasMarker(BaseState s, string id) => id switch
        {
            "town_blacksmith" => SmithOffer(s) || Smith(s) == BaseSideQuestStage.Ready,
            "demo_rescuer" => ZhouOffer(s) || ZhouNeedsBooks(s) || Zhou(s) == BaseSideQuestStage.Ready,
            _ => false
        };

        public static bool AcceptSmith(BaseState s)
        {
            if (!SmithOffer(s) || !BasePersonalEquipment.AtSmith(s) || BaseBattleLoot.Pending(s)) return false;
            Reward(s, SmithStonesId, SmithTitle, BaseEquipmentCrafting.Stone, TrialStones);
            s.Demo.Flow.SmithLesson = BaseSideQuestStage.Accepted;
            return true;
        }

        public static void Reforged(BaseState s)
        {
            if (Smith(s) == BaseSideQuestStage.Accepted) s.Demo.Flow.SmithLesson = BaseSideQuestStage.Ready;
        }

        public static bool FinishSmith(BaseState s)
        {
            if (Smith(s) != BaseSideQuestStage.Ready || !BasePersonalEquipment.AtSmith(s) || BaseBattleLoot.Pending(s)) return false;
            Reward(s, SmithId, SmithTitle, BaseEquipmentCrafting.Stone, RewardStones);
            s.Demo.Flow.SmithLesson = BaseSideQuestStage.Complete;
            return true;
        }

        public static bool AcceptZhou(BaseState s)
        {
            if (!ZhouOffer(s) || !BaseInteraction.At(s, "base", "demo_rescuer") || BaseBattleLoot.Pending(s)) return false;
            int target = ZhouTarget(s);
            Reward(s, ZhouBooksId, ZhouTitle, ZhouBook(s, target), ZhouBookCount(s, target));
            s.Demo.Flow.ZhouTrainingTarget = target;
            s.Demo.Flow.ZhouTraining = BaseSideQuestStage.Accepted;
            return true;
        }

        public static bool ClaimZhouBooks(BaseState s)
        {
            if (!ZhouNeedsBooks(s) || !BaseInteraction.At(s, "base", "demo_rescuer") || BaseBattleLoot.Pending(s)) return false;
            int target = ZhouTarget(s);
            Reward(s, ZhouBooksId, ZhouTitle, ZhouBook(s, target), ZhouBookCount(s, target));
            return true;
        }

        public static bool FinishZhou(BaseState s)
        {
            if (Zhou(s) != BaseSideQuestStage.Ready || !BaseInteraction.At(s, "base", "demo_rescuer") || BaseBattleLoot.Pending(s)) return false;
            int next = ZhouTarget(s) + 1;
            if (next <= 9)
                Reward(s, ZhouId, ZhouTitle, ZhouBook(s, next), ZhouBookCount(s, next));
            s.Demo.Flow.ZhouTraining = BaseSideQuestStage.Complete;
            return true;
        }

        private static void Reward(BaseState s, string id, string title, string item, int count)
        {
            var limit = s.Backpack.Catalog.Single(d => d.Id == item).MaxStack;
            var stacks = new System.Collections.Generic.List<BaseLootReward>();
            while (count > 0)
            {
                int size = System.Math.Min(limit, count);
                stacks.Add(new BaseLootReward { ItemId = item, Count = size });
                count -= size;
            }
            BaseBattleLoot.RequireRoom(s, stacks);
            s.Campaign.SettledIds.Add(id);
            BaseBattleLoot.Create(s, new BaseCampaignReport { BattleId = id, Title = title }, stacks);
        }
    }
}
