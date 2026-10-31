using System;
using System.Linq;
namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private void OpenEquipmentTradeHome(){if(BasePersonalEquipment.AtSmith(owner.State))OpenSmithFather();else OpenTownMarket();}
        public void OpenSmithFather(bool joined=false)
        {
            bool gift=owner.State.Leaders.Any(l=>l.Id=="partner_craft")&&!owner.State.Demo.Flow.SmithGiftOffered;
            var lines=joined||gift?new[]{new NpcLine("老铁匠","chenan","这孩子总想出去看看。他手快，心也热，就是缺些历练。路上请多照应他。"),new NpcLine("阿铸","azhu","爹，我会照顾好自己。也会帮大家守好营地。"),new NpcLine("老铁匠","chenan","这把新手短剑和一枚锻造石带上。路还长，装备该修就修。")}:new[]{new NpcLine("老铁匠","chenan","欢迎。武器、护具在这里挑，营地工坊负责生产军需。" )};
            NpcDialogue(lines,new[]{new NpcReply("谢谢，我们会互相照应",()=>{if(owner.Change(s=>{if(!BaseDemoStories.Gift(s))throw new InvalidOperationException("赠礼已领取或阿铸尚未加入");},"老铁匠的赠礼已加入奖励清单"))OpenLoot();else OpenSmithFather();},gift),new NpcReply("看看武器和护具",OpenPersonalShop),new NpcReply("购买锻造石",OpenForgeShop),new NpcReply("稍后再来",ClosePanel)});
        }
        private void RecruitPrisoner(string id,Action back)
        {
            if(!BaseDemoStories.ControllerDialogue(owner.State,id)){owner.Change(s=>{if(!BaseAftermath.Decide(s,id,"recruit"))throw new InvalidOperationException("无法安置队长，俘虏仍保留");},"已尝试招降");back();return;}
            NpcDialogue(new[]{new NpcLine("异族队长","controller","对不起。我操控了你们的意志，也伤害了你们。我保证，永远不再尝试操控任何人。"),new NpcLine("获救百姓","villager","记住你今天说的话。往后的事，要用行动证明。"),new NpcLine("异族队长","controller","请给我赎罪的机会。我愿跟随你，用自己的力量保护别人。")},new[]{new NpcReply("接纳他，给他证明的机会",()=>{
                if(!owner.Change(s=>{if(!BaseDemoStories.AcceptController(s,id))throw new InvalidOperationException("基地或旅店暂时无法安置，俘虏仍保留；腾出名额后可再次交谈");},"异族队长已加入，身份与装备记录保持")){back();return;}
                NpcDialogue(new[]{new NpcLine("异族队长","controller","我向你宣誓效忠。此后我会尊重每一个人的意志，守护与你并肩的人。")},new[]{new NpcReply("从现在开始，用行动证明",back)});
            }),new NpcReply("先暂缓决定",back)});
        }
        private void RecruitLootAll(string battle)
        {
            var special=owner.State.Aftermath.Prisoners.FirstOrDefault(p=>p.BattleId==battle&&BaseDemoStories.ControllerDialogue(owner.State,p.Id));
            if(special!=null){RecruitPrisoner(special.Id,OpenLoot);return;}
            LootChange(s=>BaseBattleLoot.RecruitAll(s,battle)>0,"已依次处理可招募俘虏，无法安置者仍保留");
        }
    }
}
