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
            var lines=joined||gift?new[]{new NpcLine(BaseScriptNames.Blacksmith,BaseScriptNames.Portrait("blacksmith"),"[擦了擦铁锤] 这臭小子总算遇上能降住他的人了。跟着你，比烂在镇上强。"),new NpcLine("阿铸","azhu","爹，我会照顾好自己。也会帮大家守好营地。"),new NpcLine(BaseScriptNames.Blacksmith,BaseScriptNames.Portrait("blacksmith"),"这柄【新手短剑】和一块【锻造石】你拿去。我儿性子急，战场上多担待。")}:new[]{new NpcLine(BaseScriptNames.Blacksmith,BaseScriptNames.Portrait("blacksmith"),"欢迎。武器、护具在这里挑，营地工坊负责生产军需。" )};
            NpcDialogue(WithInjury(owner.State,"INJ_TOWN_BALONG",lines),new[]{new NpcReply("谢谢，我们会互相照应",()=>{if(owner.Change(s=>{if(!BaseDemoStories.Gift(s))throw new InvalidOperationException("赠礼已领取或阿铸尚未加入");},"老铁匠的赠礼已加入奖励清单"))OpenLoot();else OpenSmithFather();},gift),new NpcReply("看看武器和护具",OpenPersonalShop),new NpcReply("购买锻造石",OpenForgeShop),new NpcReply("稍后再来",ClosePanel)});
        }
        private void RecruitPrisoner(string id,Action back)
        {
            if(!BaseDemoStories.ControllerDialogue(owner.State,id)){owner.Change(s=>{if(!BaseAftermath.Decide(s,id,"recruit"))throw new InvalidOperationException("无法安置队长，俘虏仍保留");},"已尝试招降");back();return;}
            NpcDialogue(new[]{new NpcLine(BaseScriptNames.Galan,BaseScriptNames.Portrait("galan"),"[低头致歉] 对不住二位。我为搜寻失散族人暂避于此，情急之下才出此下策。"),new NpcLine("获救百姓","villager","记住你今天说的话。往后的事，要用行动证明。"),new NpcLine(BaseScriptNames.Galan,BaseScriptNames.Portrait("galan"),"我以灰瞳先祖之名起誓，绝不再操控活人心智。若肯容我同行，愿凭此身赎罪。")},new[]{new NpcReply("接纳他，给他证明的机会",()=>{
                if(!owner.Change(s=>{if(!BaseDemoStories.AcceptController(s,id))throw new InvalidOperationException("当前随行与驻地名额已满（或旅店首日费用不足），俘虏已暂留名册待安置；腾出名额后可再次交谈");},"异族队长已加入，身份与装备记录保持")){back();return;}
                NpcDialogue(new[]{new NpcLine(BaseScriptNames.Galan,BaseScriptNames.Portrait("galan"),"铭感大恩。我的血脉与长杖，今后皆听凭你的号令。")},new[]{new NpcReply("从现在开始，用行动证明",back)});
            }),new NpcReply("暂缓接纳，留在俘虏栏",back)});
        }
        private void RecruitLootAll(string battle)
        {
            var special=owner.State.Aftermath.Prisoners.FirstOrDefault(p=>p.BattleId==battle&&BaseDemoStories.ControllerDialogue(owner.State,p.Id));
            if(special!=null){RecruitPrisoner(special.Id,OpenLoot);return;}
            LootChange(s=>BaseBattleLoot.RecruitAll(s,battle)>0,"已依次处理可招募俘虏，无法安置者仍保留");
        }
    }
}
