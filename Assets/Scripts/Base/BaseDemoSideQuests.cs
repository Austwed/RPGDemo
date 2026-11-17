using System.Linq;
using BaseConquest.Story;

namespace BaseConquest.Base
{
    public enum BaseSideQuestStage { Hidden, Accepted, Ready, Complete }

    public static class BaseDemoSideQuests
    {
        public const string SmithId = "demo:smith-lesson", ZhouId = "demo:zhou-training";
        public const string SmithStonesId = "demo:smith-lesson:stones";
        public const int TrialStones = 3, RewardStones = 1;

        public static string SmithTitle => StoryText.Get("story.quest.smith_lesson.title", "炉前试手");
        public static string ZhouTitle => StoryText.Get("story.quest.zhou_training.title", "阵后读兵书");

        public static BaseSideQuestStage Smith(BaseState s)
            => s?.Demo?.Flow?.SmithLesson ?? BaseSideQuestStage.Hidden;

        public static BaseSideQuestStage Zhou(BaseState s)
        {
            var stage = s?.Demo?.Flow?.ZhouTraining ?? BaseSideQuestStage.Hidden;
            return stage == BaseSideQuestStage.Accepted && Player(s)?.Training.Level >= 2
                ? BaseSideQuestStage.Ready : stage;
        }

        public static BaseLeader Player(BaseState s) => s.Leaders.FirstOrDefault(l => l.Id == s.PlayerLeaderId);

        public static string ZhouBook(BaseState s, int target)
            => BaseTraining.Book(BasePersonalEquipment.Stats(Player(s)).ProfessionKind, target);

        public static string ZhouBookName(BaseState s, int target)
            => s.Backpack.Catalog.Single(d => d.Id == ZhouBook(s, target)).Name;

        public static bool SmithOffer(BaseState s)
            => BaseDemoFlow.Active(s) && Smith(s) == BaseSideQuestStage.Hidden && s.Leaders.Any(l => l.Id == "partner_craft");

        public static bool ZhouOffer(BaseState s)
            => BaseDemoFlow.Active(s) && Zhou(s) == BaseSideQuestStage.Hidden && BaseDemoDefense.Won(s);

        public static bool HasMarker(BaseState s, string id) => id switch
        {
            "town_blacksmith" => SmithOffer(s) || Smith(s) == BaseSideQuestStage.Ready,
            "demo_rescuer" => ZhouOffer(s) || Zhou(s) == BaseSideQuestStage.Ready,
            _ => false
        };

        public static bool AcceptSmith(BaseState s)
        {
            if (!SmithOffer(s) || !BasePersonalEquipment.AtSmith(s) || BaseBattleLoot.Pending(s)) return false;
            s.Demo.Flow.SmithLesson = BaseSideQuestStage.Accepted;
            Reward(s, SmithStonesId, SmithTitle, BaseEquipmentCrafting.Stone, TrialStones);
            return true;
        }

        public static void Reforged(BaseState s)
        {
            if (Smith(s) == BaseSideQuestStage.Accepted) s.Demo.Flow.SmithLesson = BaseSideQuestStage.Ready;
        }

        public static bool FinishSmith(BaseState s)
        {
            if (Smith(s) != BaseSideQuestStage.Ready || !BasePersonalEquipment.AtSmith(s) || BaseBattleLoot.Pending(s)) return false;
            s.Demo.Flow.SmithLesson = BaseSideQuestStage.Complete;
            Reward(s, SmithId, SmithTitle, BaseEquipmentCrafting.Stone, RewardStones);
            return true;
        }

        public static bool AcceptZhou(BaseState s)
        {
            if (!ZhouOffer(s) || !BaseInteraction.At(s, "base", "demo_rescuer")) return false;
            s.Demo.Flow.ZhouTraining = BaseSideQuestStage.Accepted;
            return true;
        }

        public static bool FinishZhou(BaseState s)
        {
            if (Zhou(s) != BaseSideQuestStage.Ready || !BaseInteraction.At(s, "base", "demo_rescuer") || BaseBattleLoot.Pending(s)) return false;
            s.Demo.Flow.ZhouTraining = BaseSideQuestStage.Complete;
            Reward(s, ZhouId, ZhouTitle, ZhouBook(s, 3), 1);
            return true;
        }

        private static void Reward(BaseState s, string id, string title, string item, int count)
        {
            s.Campaign.SettledIds.Add(id);
            BaseBattleLoot.Create(s, new BaseCampaignReport { BattleId = id, Title = title },
                new[] { new BaseLootReward { ItemId = item, Count = count } });
        }
    }
}
