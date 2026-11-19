using System;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.Story;
namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        public void OpenSmithFather()
        {
            var smith = BaseScriptNames.Blacksmith;
            var portrait = BaseScriptNames.Portrait("blacksmith");
            if (BaseDemoStories.CanOfferSmithGift(owner.State))
            {
                var giftLines = new[]
                {
                    new NpcLine(smith, portrait, "这小子闲不住。在营里有活干，总比整天守着炉子强。"),
                    new NpcLine("阿铸", "azhu", "爹，我去营里搭把手。您放心。"),
                    new NpcLine(smith, portrait, "这把新手短剑和一块锻造石给你。阿铸性子急，多担待。")
                };
                NpcDialogue(WithInjury(owner.State, "INJ_TOWN_BALONG", giftLines), Array.Empty<NpcReply>(), onCompleted: CompleteSmithGift);
                return;
            }

            var state = owner.State;
            if (BaseDemoSideQuests.Smith(state) == BaseSideQuestStage.Ready && BasePersonalEquipment.AtSmith(state))
            {
                OpenSmithLessonResult();
                return;
            }
            NpcDialogue(WithInjury(state, "INJ_TOWN_BALONG",
                    new NpcLine(smith, portrait, "兵刃护具在这儿挑。营里的军需，得你们自己打。")),
                new[] { new NpcReply("向巴隆请教重铸", OpenSmithLesson,
                             unlocked: BaseDemoSideQuests.SmithOffer(state) && BasePersonalEquipment.AtSmith(state)),
                    TradeEntry(TradeVendor.Smith, TradeShelf.Equipment),
                    TradeEntry(TradeVendor.Smith, TradeShelf.Reforge), new NpcReply("稍后再来", ClosePanel) });
        }

        private void CompleteSmithGift()
        {
            bool awarded = owner.Change(state =>
            {
                if (!BaseDemoStories.Gift(state)) throw new InvalidOperationException("赠礼已领取或阿铸尚未加入");
            }, "");
            if (awarded) OpenLoot();
            else OpenSmithFather();
        }
        private void RecruitPrisoner(string id,Action back)
        {
            owner.Change(s=>{if(!BaseAftermath.Decide(s,id,"recruit"))throw new InvalidOperationException("无法安置队长，俘虏仍保留");},"已尝试招降");back();
        }
        private void OpenWaitingGalan()
        {
            if (!BaseDemoStories.ControllerDialogue(owner.State, owner.State.Demo.Flow.FirstControllerId)) { ClosePanel(); return; }
            GalanQuestions("opening", "那两个家伙误闯遗迹能活到现在，都是因为我的法术在生效。\n我可没伤他们一丝一毫。");
        }

        private void GalanSays(string key, string fallback, NpcReply[] choices = null, Action finished = null, bool resume = false)
        {
            string name = UnitDisplayNames.Mark(BaseScriptNames.Galan, true);
            var lines = StoryText.Get("story.scene.galan." + key, fallback).Split('\n')
                .Select(text => new NpcLine(name, "galan", text.TrimEnd('\r'))).ToArray();
            NpcDialogue(resume ? new[] { lines[lines.Length - 1] } : lines, choices ?? Array.Empty<NpcReply>(),
                lockExitUntilFinished: finished != null, onCompleted: finished);
            if (resume) ContinueDialogue();
        }

        private NpcReply GalanChoice(string key, string fallback, Action next) =>
            new NpcReply(StoryText.Get("story.scene.galan.choice." + key, fallback), next);

        private void GalanQuestions(string key, string text, bool resume = false) => GalanSays(key, text, new[] {
            GalanChoice("control", "你确实控制了他们", GalanAdmission),
            GalanChoice("ruins", "遗迹里有什么？", () => GalanQuestions("ruins", "遗迹地下有一条燃烧的地脉，普通人进去会气血翻腾，足以致死。\n但是看你在里面活蹦乱跳的，想必是异能者吧？\n我们自古以来就靠这东西抵御外敌，就怕你们这些不被地脉影响的怪胎……呃，异能强者。")),
            GalanChoice("village", "关于村子，你长得有点像……", () => GalanQuestions("village", "哎呀，你们人类看异族不都是一个样子嘛。\n村子什么的我真不知道，我就一善良但迷路的古裔罢了。")),
            GalanExecutionChoice(),
            GalanChoice("wait", "你先待在这", ClosePanel)
        }, resume: resume);

        private void GalanAdmission()
        {
            const string admission = "控制他们我认，我就是想着让他们服侍我一段时间。\n毕竟法术施展耗了我不少精力。\n我真打算玩……呃，这两天就放回去的。";
            GalanSays("admission", admission, new[] {
                GalanChoice("attack", "我看你刚才倒挺想杀我的", GalanPledge),
                GalanChoice("trust", "我怎么相信你？", GalanPledge),
                GalanChoice("more", "我还想了解一些事", () => GalanQuestions("admission", admission, resume: true))
            });
        }

        private void GalanPledge() => GalanSays("pledge",
            "诶诶，消消气！你看我的眼神，想必还跟其他古裔结下了不小梁子吧？\n我可以向你介绍我族相关的知识，知己知彼，百战不殆嘛。\n除此之外，我力气也不小。你放了我，我以古裔之名起誓，日后为你出力，\n绝不动操控别人或其他歪心思。请给我一次改过自新的机会！求求了！", new[] {
                GalanChoice("recruit", "我会盯紧你的", RecruitWaitingGalan),
                GalanChoice("later", "我要再想想", () => GalanSays("later", "拜托您再仔细考虑一下吧。", finished: ClosePanel)),
                GalanExecutionChoice()
            });

        private void RecruitWaitingGalan()
        {
            if (!BaseAftermath.CanRecruitLeader(owner.State))
            {
                owner.ReportMessage("营帐已满。腾出名额后，可以回遗迹入口找迦兰。");
                return;
            }
            string id = owner.State.Demo.Flow.FirstControllerId;
            if (!owner.Change(state => {
                if (!BaseDemoStories.AcceptController(state, id)) throw new InvalidOperationException("无法接纳迦兰。");
            }, "")) return;
            owner.RefreshDemoRegion();
            var hud = owner.Hud;
            hud.GalanSays("recruited", "只要不玩命……啊不对，万死不辞啊主人！", finished: hud.ClosePanel);
        }

        private NpcReply GalanExecutionChoice() => GalanChoice("execute", "不论如何，操控百姓当斩",
            () => GalanSays("executed", "啊呀！", finished: owner.ExecuteWaitingGalan));
        private void RecruitLootAll(string battle)
        {
            LootChange(s=>BaseBattleLoot.RecruitAll(s,battle)>0,"已依次处理可招募俘虏，无法安置者仍保留");
        }
    }
}
