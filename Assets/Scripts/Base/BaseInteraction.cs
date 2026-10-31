using System;
using System.Linq;
using BaseConquest.Battle;

namespace BaseConquest.Base
{
    public static class BaseInteraction
    {
        public const double Distance=1;
        public static bool CanReach(BattleBoard board,BaseMapObject target,double positionX,double positionY)
        {
            if(board==null||target==null||target.VisualOnly||BaseEnvironment.Decorative(target)||!(target.Distance(positionX,positionY)<=Distance+.000000001))return false;
            var anchor=target.Parts[0];
            return Math.Abs(board.SurfaceLevel(positionX,positionY)-board.SurfaceLevel(string.IsNullOrEmpty(target.Shape)?target.X:anchor.X,string.IsNullOrEmpty(target.Shape)?target.Y:anchor.Y))<.000001;
        }
        public static bool CanReach(BaseState state,BaseMapObject target)
        {
            if(state==null||target==null||target.VisualOnly||BaseSceneCombat.Active(state))return false;
            if(state.Leaders.Any(person=>person.Id==target.Id))return target.Distance(state.PlayerX,state.PlayerY)<BaseMap.InteractionDistance;
            return CanReach(BaseBattlefield.Board(state),target,state.PlayerX,state.PlayerY);
        }
        public static bool At(BaseState state,string region,string id)=>state.Campaign.Location==region&&CanReach(state,BaseSpatial.Objects(state,region).SingleOrDefault(target=>target.Id==id));
    }
}
