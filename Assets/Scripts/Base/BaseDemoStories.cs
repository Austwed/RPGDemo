using System.Linq;
using BaseConquest.Battle;
namespace BaseConquest.Base
{
    public static class BaseDemoStories
    {
        public static bool ControllerDialogue(BaseState s,string id)=>BaseDemoFlow.Active(s)&&s.Demo.Flow.FirstControllerId==id&&!s.Demo.Flow.ControllerPledged&&s.Aftermath.Prisoners.Any(p=>p.Id==id&&p.Status=="held");
        public static bool AcceptController(BaseState s,string id)
        {
            if(!ControllerDialogue(s,id))return false;s.Demo.Flow.ControllerAccepted=true;
            if(BaseAftermath.Decide(s,id,"recruit"))return true;s.Demo.Flow.ControllerAccepted=false;return false;
        }
        public static bool CanOfferSmithGift(BaseState s)=>BaseDemoFlow.Active(s)&&s.Leaders.Any(l=>l.Id=="partner_craft")&&!s.Demo.Flow.SmithGiftOffered;
        public static bool Gift(BaseState s)
        {
            if(!CanOfferSmithGift(s))return false;
            const string id="demo:smith:parting-gift";s.Demo.Flow.SmithGiftOffered=true;
            if(!s.Campaign.SettledIds.Contains(id))s.Campaign.SettledIds.Add(id);
            BaseBattleLoot.Create(s,new BaseCampaignReport {BattleId=id,Title="老铁匠的托付"},new[]{new BaseLootReward {Template="novice_sword",Quality=2,Count=1},new BaseLootReward {ItemId="forging_stone",Count=1}});return true;
        }
    }
}
