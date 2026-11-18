using System;
using System.Linq;
using BaseConquest.Battle;
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
            if(!BaseDemoStories.ControllerDialogue(owner.State,id)){owner.Change(s=>{if(!BaseAftermath.Decide(s,id,"recruit"))throw new InvalidOperationException("无法安置队长，俘虏仍保留");},"已尝试招降");back();return;}
            string galan=UnitDisplayNames.Mark(BaseScriptNames.Galan,true),portrait=BaseScriptNames.Portrait("galan");
            NpcDialogue(new[]{
                new NpcLine(galan,portrait,"我来找失散的族人。那两个人能活着出去，我出了力。"),
                new NpcLine("你","","你护了他们，也把他们推出来挡刀。这笔账我记着。",true),
                new NpcLine(galan,portrait,"这座窟我比你熟。留我做事，古窟的事我知道多少说多少。")
            },new[]{
                new NpcReply("接纳迦兰",()=>{
                    if(!owner.Change(s=>{if(!BaseDemoStories.AcceptController(s,id))throw new InvalidOperationException("队长名额已满，俘虏仍保留");},"")){back();return;}
                    NpcDialogue(new[]{new NpcLine(galan,portrait,"那就先从这座窟说起。")},Array.Empty<NpcReply>(),onCompleted:back);
                }),
                new NpcReply("先不接纳",()=>NpcDialogue(
                    new[]{new NpcLine(galan,portrait,"等你想谈了，再来找我。")},
                    Array.Empty<NpcReply>(),onCompleted:back))
            });
        }
        private void RecruitLootAll(string battle)
        {
            var special=owner.State.Aftermath.Prisoners.FirstOrDefault(p=>p.BattleId==battle&&BaseDemoStories.ControllerDialogue(owner.State,p.Id));
            if(special!=null){RecruitPrisoner(special.Id,OpenLoot);return;}
            LootChange(s=>BaseBattleLoot.RecruitAll(s,battle)>0,"已依次处理可招募俘虏，无法安置者仍保留");
        }
    }
}
