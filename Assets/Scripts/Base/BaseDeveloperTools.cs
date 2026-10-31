using System;
using System.Linq;
using BaseConquest.Battle;
namespace BaseConquest.Base
{
    public static class BaseDeveloperTools
    {
        public static bool SummonInvasion(bool enabled,BaseState state)
        {
            if(!enabled||state.Campaign.HasActiveBattle||state.Aftermath.GameOver||state.Campaign.Events.Count>=1000)return false;
            int number=1;while(state.Campaign.Events.Any(e=>e.Id=="developer-invasion-"+number))number++;
            state.Campaign.Events.Add(new BaseCampaignEvent {Id="developer-invasion-"+number,Title="测试入侵 "+number,Kind="defense",ScenarioFile="23_据点迎战.json",AvailableDay=state.Day,DeadlineDay=state.Day,HoldRounds=30,FoodReward=0,MaterialReward=0,CoinReward=0,FoodRaid=0,MaterialRaid=0,CoinRaid=0});return true;
        }
        public static bool Production(bool enabled,BaseState state,string id,bool active)
        {
            var f=state.Muster.Facilities.FirstOrDefault(f=>f.FacilityId==id);if(!enabled||f==null||state.Campaign.HasActiveBattle||state.Aftermath.GameOver)return false;
            f.DeveloperProductionPaused=!active;return true;
        }
        public static bool Paint(bool enabled,BaseState state,GridPosition p,TerrainKind kind)
        {
            if(!enabled||state.Campaign.HasActiveBattle||state.Aftermath.GameOver||state.Campaign.Location=="world")return false;
            var board=BaseBattlefield.Board(state);if(!board.Contains(p)||p==new GridPosition(0,board.Height-1)||p==new GridPosition(board.Width-1,0)||state.Campaign.Location=="perimeter"&&(p==new GridPosition(0,6)||p==new GridPosition(23,6)))return false;
            if(state.Campaign.Location=="base"&&BaseSpatial.AtCell(state,p.X,p.Z)?.Blocks==true)return false;
            if(BaseCampaign.MapObjects(state.Campaign.Location).Any(o=>!o.Id.StartsWith("partner")&&o.Distance(p.X+.5,p.Z+.5)<.8))return false;
            if(Math.Abs(state.PlayerX-p.X-.5)<1&&Math.Abs(state.PlayerY-p.Z-.5)<1||state.Leaders.Any(l=>l.HasMapPosition&&Math.Abs(l.MapX-p.X-.5)<1&&Math.Abs(l.MapY-p.Z-.5)<1))return false;
            return DeveloperTerrain.Paint(true,board,p,kind);
        }
        public static void ClearEffects(BaseState state)
        {
            foreach(var f in state.Muster.Facilities)f.DeveloperProductionPaused=false;
            DeveloperTerrain.Restore(state.Campaign.PerimeterBoard);foreach(var map in state.Campaign.RegionMaps)DeveloperTerrain.Restore(map.Board);
            BasePoint Nearest(double x,double y,string location)
            {return BaseBattlefield.Board(state,location).Positions().Select(p=>new BasePoint(p.X+.5,p.Z+.5)).Where(p=>BaseSpatial.Walkable(state,p.X,p.Y,location)).OrderBy(p=>p.Distance(new BasePoint(x,y))).First();}
            if(!BaseBattlefield.Walkable(state,state.PlayerX,state.PlayerY)){var p=Nearest(state.PlayerX,state.PlayerY,state.Campaign.Location);state.PlayerX=p.X;state.PlayerY=p.Y;}
            foreach(var l in state.Leaders.Where(l=>l.HasMapPosition&&!BaseBattlefield.Walkable(state,l.MapX,l.MapY,BaseAdventure.MapKey(state,l)))){var p=Nearest(l.MapX,l.MapY,BaseAdventure.MapKey(state,l));l.MapX=p.X;l.MapY=p.Y;}
        }
    }
}
