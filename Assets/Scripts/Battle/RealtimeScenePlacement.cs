using System;
using System.Linq;
using BaseConquest.Base;

namespace BaseConquest.RealtimePrototype
{
    public sealed partial class RealtimeBattlePrototype
    {
        public bool PlaceNearest(PrototypeUnit unit,PrototypePoint origin)
        {
            int source=Arena.CellAt(origin);var distances=Arena.FineWalkable(source)?Arena.Distances(source,true):null;
            int candidate=Enumerable.Range(0,Arena.FineColumns*Arena.FineRows).Where(c=>Fits(unit,c,unit.Facing)&&
                (distances==null||!double.IsPositiveInfinity(distances[c]))&&!Units.Any(other=>other!=unit&&other.Alive&&BodiesCross(unit,Arena.Center(c),other,other.Position)))
                .OrderBy(c=>(Arena.Center(c)-origin).Length).ThenBy(c=>c).DefaultIfEmpty(-1).First();
            if(candidate<0)return false;
            unit.Cell=unit.PreviousCell=candidate;unit.NextCell=-1;unit.Progress=0;unit.Position=unit.Anchor=unit.Goal=Arena.Center(candidate);unit.TargetId=unit.AttackSlot=unit.MoveGoal=-1;CancelWindup(unit);RebuildReservations();return true;
        }
    }
}

namespace BaseConquest.Battle
{
    public static class BattleScenePlacement
    {
        public static void Start(BaseState state,BaseCampaignSession session)
        {
            if(!BaseSceneRules.Active(state)&&!session.Opening.ManualDeployment)return;
            if(session.Battle.Phase==BattlePhase.Deployment)
            {
                if (!session.Opening.ManualDeployment) session.Battle.AutoDeploy(BattleSide.A);
                else if (session.Battle.Soldiers.Count(u=>u.Side==BattleSide.A)!=session.Opening.Troops.Count+session.Opening.Leaders.Count)
                    throw new InvalidOperationException("战场落点已变化，请重新部署");
                session.Battle.AutoDeploy(BattleSide.B);session.Bind();
                if(!session.Battle.Start(out string problem))throw new InvalidOperationException(problem);
            }
        }
        public static void Snap(BaseState state,RealtimeCampaignBattle run)
        {
            if(!BaseSceneRules.Active(state)||run.Session.Opening.HasRealtimeState||run.Session.Opening.ManualDeployment)return;
            foreach(var unit in run.Model.Units.Where(u=>u.Side==RealtimePrototype.PrototypeSide.Ally).OrderByDescending(u=>u.Player).ThenBy(u=>u.Id))
            {
                var leader=run.Session.Opening.Leaders.ElementAtOrDefault(unit.Squad);
                var source=state.Leaders.FirstOrDefault(l=>l.Id==leader?.Id);
                var point=unit.Player?new RealtimePrototype.PrototypePoint(state.PlayerX,state.PlayerY):source?.HasMapPosition==true?new RealtimePrototype.PrototypePoint(source.MapX,source.MapY):new RealtimePrototype.PrototypePoint(state.PlayerX,state.PlayerY);
                if(!run.Model.PlaceNearest(unit,point))throw new InvalidOperationException("当前位置附近没有足够合法落格");
                
            }
            state.PlayerX=run.Model.Player.Position.X;state.PlayerY=run.Model.Player.Position.Z;
        }
        public static void Capture(BaseState state,RealtimeCampaignBattle run)
        {
            if(!BaseSceneRules.Active(state)||run==null)return;
            // Settlement may already have returned the party; keep its destination positions.
            if(state.Campaign.Location!=run.Session.Opening.MapRegion||BaseSceneInstances.Active(state)&&state.Campaign.CurrentMapId!=run.Session.Opening.MapId)return;
            foreach(var unit in run.Model.Units.Where(u=>u.Side==RealtimePrototype.PrototypeSide.Ally&&u.Role==RealtimePrototype.PrototypeRole.Leader))
            {
                var origin=run.Session.Opening.Leaders.ElementAtOrDefault(unit.Squad);var leader=state.Leaders.FirstOrDefault(l=>l.Id==origin?.Id);if(leader==null)continue;
                var point=BaseSpatial.Nearest(state,new BasePoint(unit.Position.X,unit.Position.Z));
                leader.MapX=point.X;leader.MapY=point.Y;leader.HasMapPosition=true;
                leader.MapRegion=state.Campaign.Location;leader.MapId=BaseSceneInstances.Active(state)?state.Campaign.CurrentMapId:null;
            }
        }
    }
}
