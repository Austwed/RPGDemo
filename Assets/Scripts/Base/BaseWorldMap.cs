using System;
using System.Linq;
using BaseConquest.Battle;
namespace BaseConquest.Base
{
    // 世界只负责区域间导航；节点目的地拥有自己的可自由活动／战斗地图。
    public static class BaseWorldMap
    {
        public static BattleBoard CreateWorld()
        {
            var map=BattleBoard.CreateDefault(24,14);
            foreach(var p in map.Positions()){map.Cell(p).Terrain=TerrainKind.Ground;map.Cell(p).Zone=DeploymentZone.None;}
            return map;
        }
        public static void PrepareAttackRegion(BaseState state,BattleScenarioDocument document)
        {
            BattleScenarioDocument.Validate(document);
            if(BaseSceneInstances.Active(state))return;
            if(state.Campaign.RegionMaps.Any(m=>m.Id=="outpost"))return;
            var map=document.Scenario.Board.Clone();
            map.Cell(new GridPosition(0,map.Height-1)).Terrain=TerrainKind.Ground;
            state.Campaign.RegionMaps.Add(new BaseRegionMap {Id="outpost",Board=map});
        }
        public static bool EnterAttackRegion(BaseState state,BattleScenarioDocument document)
        {
            BaseEconomy.Validate(state);
            if(state.Campaign.Location!="world"||state.Campaign.HasActiveBattle||!state.Muster.Parties.Any(p=>p.Purpose=="expedition"&&p.Leaders.Any(l=>l.LeaderId==state.PlayerLeaderId)))return false;
            PrepareAttackRegion(state,document);return BaseCampaign.Travel(state,"outpost");
        }
    }
}
