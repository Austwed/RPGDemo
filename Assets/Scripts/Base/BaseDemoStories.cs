using System.Linq;
using BaseConquest.Battle;
namespace BaseConquest.Base
{
    public static class BaseDemoStories
    {
        public const string WaitingAtEntrance = "awaiting-story";
        public const string GalanMarker = "demo_galan_waiting";
        public static bool ControllerDialogue(BaseState s,string id)=>BaseDemoFlow.Active(s)&&!string.IsNullOrEmpty(id)&&s.Demo.Flow.FirstControllerId==id&&!s.Demo.Flow.ControllerPledged&&s.Aftermath.Prisoners.Any(p=>p.Id==id&&p.Status==WaitingAtEntrance);
        public static bool AcceptController(BaseState s,string id)
        {
            if(!ControllerDialogue(s,id))return false;s.Demo.Flow.ControllerAccepted=true;
            if(BaseAftermath.Decide(s,id,"recruit"))return true;s.Demo.Flow.ControllerAccepted=false;return false;
        }
        public static bool ExecuteController(BaseState state, string id)
        {
            if (!ControllerDialogue(state, id)) return false;
            state.Aftermath.Prisoners.Single(p => p.Id == id).Status = "executed";
            return true;
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
