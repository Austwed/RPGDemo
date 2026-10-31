using System;
using System.Linq;
namespace BaseConquest.Base
{
    [Serializable] public sealed class BaseDemoFlowState
    {
        public int HiredKinds, ReadyStakes;
        public bool RecruitmentNotice, RecruitOpened, InnWelcomed, HomeEnterSeen, RestLessonDone, FarmOfferPending, FarmOfferShown, NoviceShop, StakesGranted, StakesNotice;
        public bool AzhuDuelWon, SmithGiftOffered, ControllerAccepted, ControllerPledged, BanditCleared;
        public string FirstControllerId="";
        public double FarmX,FarmY,FarmNpcX,FarmNpcY;public bool FarmWalking;
        public BaseDemoFlowState Copy()=>(BaseDemoFlowState)MemberwiseClone();
    }
    public sealed class BaseDailyDelta
    {
        public int Food,Materials,Coins,Supply;
        public static BaseDailyDelta Read(BaseState s)
        {
            if(s.Day>=1000000)return new BaseDailyDelta();
            var copy=s.Copy();int supply=copy.Inventory.Equipment.Where(e=>e.Product=="supply").Sum(e=>e.Count);
            BaseEconomy.CompleteProductionDay(copy,false);
            return new BaseDailyDelta{Food=copy.Inventory.Food-s.Inventory.Food,Materials=copy.Inventory.Materials-s.Inventory.Materials,Coins=copy.Inventory.Coins-s.Inventory.Coins,Supply=copy.Inventory.Equipment.Where(e=>e.Product=="supply").Sum(e=>e.Count)-supply};
        }
    }
    public static class BaseDemoFlow
    {
        public static bool Active(BaseState s)=>BaseDemo.Active(s)&&s.Demo.Flow!=null;
        public static bool Recruited(BaseState s)=>Active(s)&&s.Demo.Flow.HiredKinds==7;
        public static string RecruitProgress(BaseState s)=>string.Join("  ",new[]{BaseTroopKind.Melee,BaseTroopKind.Ranged,BaseTroopKind.Cavalry}.Select((k,n)=>((s.Demo.Flow.HiredKinds&(1<<n))!=0?"■ ":"□ ")+(n==0?"剑士":n==1?"弓兵":"骑兵")+"×1"));
        public static void Hired(BaseState s,BaseTroopKind kind){if(Active(s)&&BaseDemoDefense.Delivered(s))s.Demo.Flow.HiredKinds|=1<<(int)kind;}
        public static string RestProblem(BaseState s)
        {
            if(!Active(s)||s.Day<2||s.Demo.Flow.RestLessonDone||!Recruited(s)||!s.Demo.Defense.WorkshopRepaired||!s.Demo.Defense.BarracksRepaired)return "";
            if(BaseEconomy.EligibleWorkers(s,"workshop")<1)return "【工坊】尚无人手值守。外患将至，请先安排至少 1 名工人打制军需再行歇息。";
            return BaseDailyDelta.Read(s).Food>0?"":"眼下口粮入不敷出（预计净增需大于 0）。先去【粮田】增派人手，免得弟兄们明早饿肚子。";
        }
        public static void Validate(BaseState s)
        {
            if(!Active(s))return;var f=s.Demo.Flow;
            var controller=s.Aftermath.Prisoners.FirstOrDefault(p=>p.Id==f.FirstControllerId);
            if(f.FirstControllerId==null||f.FirstControllerId!=""&&controller==null||f.ControllerAccepted&&controller==null||f.ControllerPledged&&(!f.ControllerAccepted||controller?.Status!="recruited"))throw new ArgumentException("首个控制者的身份／接纳记录非法");
            if(new[]{f.FarmX,f.FarmY,f.FarmNpcX,f.FarmNpcY}.Any(v=>double.IsNaN(v)||double.IsInfinity(v))||f.HiredKinds<0||f.HiredKinds>7||f.ReadyStakes<0||f.ReadyStakes>20||!f.StakesGranted&&f.ReadyStakes!=0||double.IsNaN(f.FarmX)||double.IsInfinity(f.FarmX)||double.IsNaN(f.FarmY)||double.IsInfinity(f.FarmY))throw new ArgumentException("Demo经营教学与拒马状态非法");
        }
        public static void GrantStakes(BaseState s)
        {if(Active(s)&&s.Campaign.Location=="perimeter"&&!s.Demo.Flow.StakesGranted){s.Demo.Flow.StakesGranted=true;s.Demo.Flow.ReadyStakes=20;}}
        public static bool SmithVisit(BaseState s)=>Active(s)&&s.Demo.RuinsDay>0&&s.Campaign.Location=="town"&&!BaseAdventure.Following(s,"partner_craft")&&(!s.Leaders.Any(l=>l.Id=="partner_craft")||BaseAdventure.Alive(s.Leaders.Single(l=>l.Id=="partner_craft")));
        public static BasePoint SmithApproach(BaseState s)
        {var smith=BaseSpatial.Layout(s,"town")?.Objects.FirstOrDefault(o=>o.Id=="town_smith");return smith==null?new BasePoint(14,7.5):new BasePoint(smith.X+1.5,smith.Y-smith.Height/2-.6);}
    }
}
