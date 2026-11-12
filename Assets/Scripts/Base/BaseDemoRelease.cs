using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
namespace BaseConquest.Base
{
    [Serializable] public sealed class BaseDemoDesignMap
    {
        public string Role;
        public BaseMapLayout Layout;
        public BattleBoard Board;
        public BaseDemoDesignMap Copy()=>new BaseDemoDesignMap {Role=Role,Layout=Layout?.Copy(),Board=Board?.Clone()};
    }
    public static class BaseDemoRelease
    {
        public static bool Enabled(BaseState s)=>BaseDemo.Active(s)&&s.Demo.ReleaseVersion==1;
        public static BattleBoard Board(bool defense=false)
        {
            var b=BattleBoard.CreateDefault(32,24);
            foreach(var p in b.Positions())
            {var c=b.Cell(p);c.Terrain=TerrainKind.Ground;c.Zone=p.X>=(defense?25:3)&&p.X<=(defense?28:6)?DeploymentZone.A:p.X>=(defense?2:25)&&p.X<=(defense?5:28)?DeploymentZone.B:DeploymentZone.None;
             if(p.X==15&&p.Z>=4&&p.Z<=19&&p.Z!=8&&p.Z!=9&&p.Z!=16&&p.Z!=17){c.Terrain=TerrainKind.Wall;c.Zone=DeploymentZone.None;}}
            if(defense)foreach(var p in b.Positions().Where(p=>(p.X>=9&&p.X<=11&&p.Z>=11&&p.Z<=13)||(p.X>=19&&p.X<=21&&p.Z>=5&&p.Z<=6)))b.Cell(p).Terrain=TerrainKind.Swamp;
            return b;
        }
        public static void Configure(BaseMapLayout m)
        {
            m.Columns=32;m.Rows=24;
            m.TacticalObjects=new List<BattleTacticalObject>{new BattleTacticalObject {Id="tree_gate",Kind="tree",X=12,Y=8},new BattleTacticalObject {Id="rock_flank",Kind="rock",X=18,Y=16}};
        }
        public static void Install(BaseState s)
        {
            // 营帐中的长剑摆件已撤：长剑改为老周当面交付的剧情物品，场景里不再单独立一支。
            var town=s.Campaign.Layouts.Single(m=>m.Region=="town");town.Objects.Add(new BaseMapPlacement {Id="town_market",Name="补给摊位 · 买卖与战利品出售",X=14,Y=9,Width=3,Height=1.5,Blocks=true,AssetId="market-stall",Kind="facility",SourceObjectId="demo:town:market"});
            town.Objects.Add(new BaseMapPlacement {Id="town_smith",Name="铁匠铺 · 武器与护具",X=10,Y=10.5,Width=2.4,Height=1.5,Blocks=true,AssetId="workshop-small",Kind="facility"});
            town.Objects.Add(new BaseMapPlacement {Id="town_blacksmith",Name="老铁匠 · 交易",X=9.5,Y=9,Width=.5,Height=.5,AssetId="baron",Kind="npc"});
            foreach(string region in new[]{"outpost","perimeter"})
            {var m=s.Campaign.Layouts.Single(x=>x.Region==region);Configure(m);if(region=="perimeter")s.Campaign.PerimeterBoard=Board(true);else s.Campaign.RegionMaps.Single(x=>x.Id==region).Board=Board();}
            s.Demo.Finale.CampBoard=Board();
            if(s.Demo.ReleaseMaps.Count==0)
            {
                var camp=BaseDemoFinale.CreateCampLayout();Configure(camp);
                s.Demo.ReleaseMaps=new List<BaseDemoDesignMap>{new BaseDemoDesignMap {Role="ruins",Layout=s.Campaign.Layouts.Single(m=>m.Region=="outpost").Copy(),Board=BaseBattlefield.Board(s,"outpost").Clone()},new BaseDemoDesignMap {Role="defense",Layout=s.Campaign.Layouts.Single(m=>m.Region=="perimeter").Copy(),Board=s.Campaign.PerimeterBoard.Clone()},new BaseDemoDesignMap {Role="bandit",Layout=camp,Board=s.Demo.Finale.CampBoard.Clone()}};
            }
            else foreach(var map in s.Demo.ReleaseMaps)
            {
                if(map.Role=="bandit"){s.Demo.Finale.CampBoard=map.Board.Clone();continue;}
                s.Campaign.Layouts.RemoveAll(m=>m.Region==map.Layout.Region);s.Campaign.Layouts.Add(map.Layout.Copy());
                if(map.Role=="defense")s.Campaign.PerimeterBoard=map.Board.Clone();else s.Campaign.RegionMaps.Single(m=>m.Id=="outpost").Board=map.Board.Clone();
            }
        }
        public static BasePoint Landing(BaseState s,string map,BaseMapPlacement marker)
        {
            var board=BaseBattlefield.Board(s,map);var layout=BaseSpatial.Layout(s,map);
            if(marker.VisualOnly&&s.Demo.StoryScene?.Version==2){marker=marker.Copy();marker.VisualOnly=false;}
            var points=board.Positions().Select(p=>new BasePoint(p.X+.5,p.Z+.5)).Where(p=>BaseSpatial.Walkable(s,p.X,p.Y,map)&&BaseInteraction.WithinReach(board,marker.Object,p.X,p.Y)).OrderBy(p=>p.Distance(new BasePoint(layout.SpawnX,layout.SpawnY))).ToArray();
            if(points.Length==0)throw new ArgumentException("出入口没有安全落点："+marker.Id);return points[0];
        }
        public static void Validate(BaseState s)
        {
            if(s.Demo==null)return;
            if(s.Demo.ReleaseVersion<0||s.Demo.ReleaseVersion>1||s.Demo.ReleaseVersion>0&&!BaseDemo.Active(s))throw new ArgumentException("Demo交付版本非法");
            if(Enabled(s))
            {
                if(s.Demo.TacticalRules==null)throw new ArgumentException("缺少战术规则快照");s.Demo.TacticalRules.Validate();
                if(s.Demo.ReleaseMaps==null||s.Demo.ReleaseMaps.Count!=3||!s.Demo.ReleaseMaps.Select(m=>m?.Role).OrderBy(r=>r).SequenceEqual(new[]{"bandit","defense","ruins"}))throw new ArgumentException("交付Demo须保留三场设计快照");
                foreach(var map in s.Demo.ReleaseMaps){if(map.Layout==null||map.Board?.Width!=32||map.Board.Height!=24||map.Layout.Columns!=32||map.Layout.Rows!=24)throw new ArgumentException("Demo设计须32×24");new BaseMapDesign {Layout=map.Layout,Board=map.Board}.Validate();BattleTacticalObject.Validate(map.Layout.TacticalObjects,32,24);}
            }
            // 剧情场景包自带尺寸（10～64），不受旧版 24×14 与禁战术物件的限制。
            bool story=s.Demo?.StoryScene?.Valid==true;
            foreach(var m in s.Campaign.Layouts??new List<BaseMapLayout>())
            {if(!Enabled(s)&&!story&&(m.Columns!=24||m.Rows!=14||m.TacticalObjects?.Count>0))throw new ArgumentException("旧版地图不能隐式扩图或添加战术物件");
             BattleTacticalObject.Validate(m.TacticalObjects,m.Columns,m.Rows);}
        }
    }
}
