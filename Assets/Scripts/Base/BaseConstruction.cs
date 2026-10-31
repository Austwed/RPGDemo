using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BaseConquest.Battle;

namespace BaseConquest.Base
{
    [Serializable] public sealed class BaseConstructionState
    {
        public int Version, Revision, ProductLevel=1;
        public bool FieldworksUnlocked;
        public List<BaseProductProgress> AdvancedProgress=new List<BaseProductProgress>();
        public BaseConstructionState Copy() => new BaseConstructionState {Version=Version,Revision=Revision,ProductLevel=ProductLevel,FieldworksUnlocked=FieldworksUnlocked,AdvancedProgress=AdvancedProgress.Select(x=>x.Copy()).ToList()};
    }
    public sealed class BaseUpgradeQuote
    {
        public readonly string Facility, Stamp;
        public readonly int Materials=10, Coins=20;
        internal BaseUpgradeQuote(string facility,string stamp){Facility=facility;Stamp=stamp;}
    }
    public static class BaseConstruction
    {
        public static readonly GridPosition[] FixedCover={new GridPosition(18,5),new GridPosition(18,8)};
        public static void Normalize(BaseState s)
        {
            if(s.Construction!=null&&s.Construction.Version!=0)return;
            var old=s.Construction;if(old!=null&&(old.Revision!=0||old.FieldworksUnlocked||old.AdvancedProgress?.Count>0))throw new ArgumentException("营造数据缺版本，拒绝重置升级");
            s.Construction=new BaseConstructionState {Version=1,ProductLevel=1,AdvancedProgress=BaseEconomy.ProductsFor(s).Select(p=>new BaseProductProgress {Product=p}).ToList()};
        }
        public static int ProductLevel(BaseState s) => s.Construction?.ProductLevel??1;
        public static BaseProductProgress Progress(BaseState s,string product) => (ProductLevel(s)==2 ? s.Construction.AdvancedProgress : s.WorkshopProgress).Single(p=>p.Product==product);
        public static int MaterialCost(BaseState s,string product) => (product=="supply"?s.Demo.Rpg.SupplyMaterial:s.Rules.MaterialCost(product))*ProductLevel(s);
        public static string Snapshot(BaseState s) => s.Construction==null ? "construction:legacy" : "construction:"+s.Construction.Revision+":"+s.Construction.ProductLevel+":"+s.Construction.FieldworksUnlocked+":"+string.Join(",",s.Construction.AdvancedProgress.Select(p=>p.Product+":"+p.Fraction.ToString("R",CultureInfo.InvariantCulture)));
        public static void Validate(BaseState s)
        {
            Normalize(s);var c=s.Construction;
            if(c.Version!=1||c.Revision<0||c.Revision>=int.MaxValue||c.ProductLevel<1||c.ProductLevel>2||c.ProductLevel>s.Muster.Facilities.Single(f=>f.FacilityId=="workshop").MaxProductLevel||c.AdvancedProgress==null||c.AdvancedProgress.Count!=BaseEconomy.ProductsFor(s).Length||c.AdvancedProgress.Any(p=>p==null||!BaseEconomy.ProductsFor(s).Contains(p.Product)||double.IsNaN(p.Fraction)||double.IsInfinity(p.Fraction)||p.Fraction<0||p.Fraction>=1)||c.AdvancedProgress.Select(p=>p.Product).Distinct().Count()!=BaseEconomy.ProductsFor(s).Length)throw new ArgumentException("营造版本、配方等级或高级生产进度非法");
        }
        public static BaseMapObject[] MapObjects(BaseState s) => s.Campaign.Location=="perimeter"&&!BaseThreats.MapObjects(s).Any(o=>o.Id=="fixed_fieldworks") ? BaseThreats.MapObjects(s).Concat(new[]{new BaseMapObject("fixed_fieldworks","固定工事",18,6.5,1,1,false)}).ToArray() : BaseThreats.MapObjects(s);
        private static bool Editable(BaseState s) => !s.Aftermath.GameOver&&!s.Campaign.HasActiveBattle&&!s.Muster.Parties.Any(p=>p.InCombat)&&s.Construction.Revision<int.MaxValue-1;
        private static bool Nearby(BaseState s,string id)
        {
            if(!Editable(s))return false;
            var obj=id=="fieldworks" ? s.Campaign.Location=="perimeter" ? MapObjects(s).Single(o=>o.Id=="fixed_fieldworks") : null : s.Campaign.Location=="base" ? BaseSpatial.Objects(s,"base").SingleOrDefault(o=>o.Id==id) : null;
            return BaseInteraction.CanReach(s,obj);
        }
        public static string UpgradeProblem(BaseState s,string id)
        {
            if(BaseDemo.Active(s))return "Demo使用固定修复，只开放基础功能，不使用旧营造图册升级";
            if(!new[]{"farm","workshop","fieldworks"}.Contains(id))return "未知设施";
            if(!Nearby(s,id))return "请在和平状态接近对应设施后交互";
            if(id=="fieldworks" ? s.Construction.FieldworksUnlocked : s.Muster.Facilities.Single(f=>f.FacilityId==id).Level>=2)return "本版已完成此项升级／解锁";
            if(!s.Backpack.StoryFlags.Contains(BaseBackpack.Story))return "剧情未解锁：营造入门";
            if(BasePartyInventory.AvailableMaterial(s,BaseBackpack.Blueprint)<1)return "缺少可用特殊道具：营造图册 ×1（基地可用仓库，外出需随身携带）";
            if(s.Inventory.Materials<10||s.Inventory.Coins<20)return "需要原料10、钱币20";
            return "";
        }
        public static string ModeProblem(BaseState s) => BaseDemoFlow.Active(s)?!Editable(s)||BaseSceneCombat.Active(s)||s.Campaign.Location!="base"&&s.Campaign.Location!="perimeter"?"只能在基地或迎敌战场战前布置":"":!Nearby(s,"fieldworks") ? "请在迎敌战场固定工事处开启建造模式" : !s.Construction.FieldworksUnlocked ? "先解锁固定工事" : "";
        private static string UpgradeStamp(BaseState s) => BaseMuster.Snapshot(s)+"|bag:"+s.Backpack.Revision+":"+BaseBackpack.Count(s,BaseBackpack.Blueprint)+":"+string.Join(",",s.Backpack.StoryFlags);
        public static BaseUpgradeQuote Preview(BaseState s,string id)
        {BaseEconomy.Validate(s);string problem=UpgradeProblem(s,id);if(problem!="")throw new InvalidOperationException(problem);return new BaseUpgradeQuote(id,UpgradeStamp(s));}
        public static bool Confirm(BaseState s,BaseUpgradeQuote quote)
        {
            BaseEconomy.Validate(s);
            if(quote==null||UpgradeProblem(s,quote.Facility)!=""||quote.Stamp!=UpgradeStamp(s)||!BasePartyInventory.ConsumeMaterial(s,BaseBackpack.Blueprint,1))return false;
            s.Inventory.Materials-=quote.Materials;s.Inventory.Coins-=quote.Coins;
            if(quote.Facility=="fieldworks")
            {
                s.Construction.FieldworksUnlocked=true;
                foreach(var p in FixedCover)BaseBattlefield.Board(s,"perimeter").Cell(p).FixedCover=true;
            }
            else
            {
                var f=s.Muster.Facilities.Single(x=>x.FacilityId==quote.Facility);f.Level=2;f.Capacity=Math.Min(10000,f.Capacity+2);
                if(quote.Facility=="workshop")f.MaxProductLevel=2;
            }
            s.Construction.Revision++;BaseDevelopment.Allocate(s);BaseAchievements.Unlock(s,BaseAchievements.Startup);BaseEconomy.Validate(s);return true;
        }
        public static bool SelectLevel(BaseState s,int level)
        {
            if(!Editable(s)||level<1||level>2||level>s.Muster.Facilities.Single(f=>f.FacilityId=="workshop").MaxProductLevel)return false;
            s.Construction.ProductLevel=level;s.Construction.Revision++;return true;
        }
        public static string BuildProblem(BaseState s,GridPosition p,bool remove=false)
        {
            if(!Editable(s)||BaseSceneCombat.Active(s)||s.Campaign.Location!="base"&&s.Campaign.Location!="perimeter")return "只能在和平的基地或迎敌战场营造";
            if(!BaseDemoFlow.Active(s)&&!s.Construction.FieldworksUnlocked)return "先在迎敌战场固定工事处完成剧情解锁";
            var board=BaseBattlefield.Board(s);var cell=board.Cell(p);if(cell==null)return "格子越界";
            if(remove)return cell.ObstacleHealth>0 ? "" : "只有普通路障可拆除；固定设施不能拆除";
            if(cell.FixedCover||!cell.Walkable||cell.Terrain!=TerrainKind.Ground||cell.Zone!=DeploymentZone.None||cell.HasDeveloperTerrain)return "请选择无部署标记的空地；固定设施、入口和临时地形不能覆盖";
            if(MapObjects(s).Any(o=>(o.Blocks||!o.VisualOnly)&&!o.Id.StartsWith("partner")&&o.Distance(p.X+.5,p.Z+.5)<1)||s.Campaign.Location=="base"&&BaseSpatial.AtCell(s,p.X,p.Z)?.Blocks==true)return "不能覆盖固定设施、工程位或区域入口";
            if(Math.Abs(s.PlayerX-p.X-.5)<1&&Math.Abs(s.PlayerY-p.Z-.5)<1||s.Campaign.Location=="base"&&s.Leaders.Any(l=>l.HasMapPosition&&Math.Abs(l.MapX-p.X-.5)<1&&Math.Abs(l.MapY-p.Z-.5)<1))return "格子上有人或离人物太近";
            if(board.Cells.Count(c=>c.ObstacleHealth>0)>=50)return "每区域最多50处普通路障";
            if(s.Inventory.Materials<(BaseDemoFlow.Active(s)?s.Demo.Flow.ReadyStakes>0?0:1:3))return "原料不足";
            return "";
        }
        public static bool Build(BaseState s,GridPosition p)
        {
            BaseEconomy.Validate(s);if(BuildProblem(s,p)!="")return false;
            bool stake=BaseDemoFlow.Active(s);if(stake&&s.Demo.Flow.ReadyStakes>0)s.Demo.Flow.ReadyStakes--;else s.Inventory.Materials-=stake?1:3;var cell=BaseBattlefield.Board(s).Cell(p);cell.IsStake=stake;cell.ObstacleHealth=stake?100:60;cell.ObstacleOwner=BattleSide.A;s.Construction.Revision++;return true;
        }
        public static bool Remove(BaseState s,GridPosition p)
        {
            BaseEconomy.Validate(s);if(BuildProblem(s,p,true)!="")return false;
            BaseBattlefield.Board(s).Cell(p).ObstacleHealth=0;s.Construction.Revision++;return true;
        }
    }
}
