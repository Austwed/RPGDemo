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
        public static BaseMapObject[] MapObjects(BaseState s) => BaseThreats.MapObjects(s);
        private static bool Editable(BaseState s) => !s.Aftermath.GameOver&&!s.Campaign.HasActiveBattle&&!s.Muster.Parties.Any(p=>p.InCombat)&&s.Construction.Revision<int.MaxValue-1;
        private static bool Nearby(BaseState s,string id)
        {
            if(!Editable(s))return false;
            var obj=s.Campaign.Location=="base" ? BaseSpatial.Objects(s,"base").SingleOrDefault(o=>o.Id==id) : null;
            return BaseInteraction.CanReach(s,obj);
        }
        public static string UpgradeProblem(BaseState s,string id)
        {
            if(BaseDemo.Active(s))return "Demo使用固定修复，只开放基础功能，不使用旧营造图册升级";
            if(!new[]{"farm","workshop"}.Contains(id))return "未知设施";
            if(!Nearby(s,id))return "请在和平状态接近对应设施后交互";
            if(s.Muster.Facilities.Single(f=>f.FacilityId==id).Level>=2)return "本版已完成此项升级／解锁";
            if(!s.Backpack.StoryFlags.Contains(BaseBackpack.Story))return "剧情未解锁：营造入门";
            if(BasePartyInventory.AvailableMaterial(s,BaseBackpack.Blueprint)<1)return "缺少可用特殊道具：营造图册 ×1（基地可用仓库，外出需随身携带）";
            if(s.Inventory.Materials<10||s.Inventory.Coins<20)return "需要原料10、钱币20";
            return "";
        }
        public static string ModeProblem(BaseState s) => !BaseDemoFlow.Active(s)||!Editable(s)||BaseSceneCombat.Active(s)||s.Campaign.Location!="perimeter" ? "只能在迎敌战场战前布置" : "";
        private static string UpgradeStamp(BaseState s) => BaseMuster.Snapshot(s)+"|bag:"+s.Backpack.Revision+":"+BaseBackpack.Count(s,BaseBackpack.Blueprint)+":"+string.Join(",",s.Backpack.StoryFlags);
        public static BaseUpgradeQuote Preview(BaseState s,string id)
        {BaseEconomy.Validate(s);string problem=UpgradeProblem(s,id);if(problem!="")throw new InvalidOperationException(problem);return new BaseUpgradeQuote(id,UpgradeStamp(s));}
        public static bool Confirm(BaseState s,BaseUpgradeQuote quote)
        {
            BaseEconomy.Validate(s);
            if(quote==null||UpgradeProblem(s,quote.Facility)!=""||quote.Stamp!=UpgradeStamp(s)||!BasePartyInventory.ConsumeMaterial(s,BaseBackpack.Blueprint,1))return false;
            s.Inventory.Materials-=quote.Materials;s.Inventory.Coins-=quote.Coins;
            var f=s.Muster.Facilities.Single(x=>x.FacilityId==quote.Facility);f.Level=2;f.Capacity=Math.Min(10000,f.Capacity+2);
            if(quote.Facility=="workshop")f.MaxProductLevel=2;
            s.Construction.Revision++;BaseDevelopment.Allocate(s);BaseAchievements.Unlock(s,BaseAchievements.Startup);BaseEconomy.Validate(s);return true;
        }
        public static bool SelectLevel(BaseState s,int level)
        {
            if(!Editable(s)||level<1||level>2||level>s.Muster.Facilities.Single(f=>f.FacilityId=="workshop").MaxProductLevel)return false;
            s.Construction.ProductLevel=level;s.Construction.Revision++;return true;
        }
        public static string DemoFieldworkProblem(BaseState s,GridPosition p,ConstructionProject project)
        {
            if(!BaseDemoFlow.Active(s)||!Editable(s)||BaseSceneCombat.Active(s)||s.Campaign.Location!="perimeter")return "只能在迎敌战场战前布置";
            var board=BaseBattlefield.Board(s);var cell=board.Cell(p);
            if(cell==null)return "格子越界";
            if(project==ConstructionProject.Demolish)return cell.HiddenTrap||cell.SandbagGroup>0?"":"此处没有可拆除工事";
            var positions=project==ConstructionProject.Sandbag?new[]{p,new GridPosition(p.X+1,p.Z),new GridPosition(p.X+2,p.Z)}:new[]{p};
            if(project!=ConstructionProject.Trap&&project!=ConstructionProject.Sandbag)return "请选择工事";
            if(positions.Any(q=>board.Cell(q)==null||!board.Cell(q).Walkable||board.Cell(q).Terrain!=TerrainKind.Ground||board.Cell(q).Zone!=DeploymentZone.None||board.Cell(q).FixedCover||board.Cell(q).HiddenTrap||board.Cell(q).SandbagGroup>0||board.Cell(q).ObstacleHealth>0||board.Cell(q).HasDeveloperTerrain))return "需要未占用的平地";
            if(positions.Any(q=>MapObjects(s).Any(o=>(o.Blocks||!o.VisualOnly)&&o.Distance(q.X+.5,q.Z+.5)<1)||Math.Abs(s.PlayerX-q.X-.5)<1&&Math.Abs(s.PlayerY-q.Z-.5)<1))return "不能覆盖设施或人物";
            if(project==ConstructionProject.Trap&&board.Cells.Count(c=>c.HiddenTrap)>=10)return "每战场最多10个陷阱";
            return s.Inventory.Materials<(project==ConstructionProject.Trap?4:2)?"原料不足":"";
        }
        public static bool DemoFieldwork(BaseState s,GridPosition p,ConstructionProject project)
        {
            if(DemoFieldworkProblem(s,p,project)!="")return false;
            var board=BaseBattlefield.Board(s);var cell=board.Cell(p);
            if(project==ConstructionProject.Demolish)
            {
                if(cell.HiddenTrap)cell.HiddenTrap=false;
                else {int group=cell.SandbagGroup;foreach(var c in board.Cells.Where(c=>c.SandbagGroup==group))c.SandbagGroup=0;}
            }
            else if(project==ConstructionProject.Trap){s.Inventory.Materials-=4;cell.HiddenTrap=true;}
            else {s.Inventory.Materials-=2;int group=board.Cells.Max(c=>c.SandbagGroup)+1;for(int x=0;x<3;x++)board.Cell(new GridPosition(p.X+x,p.Z)).SandbagGroup=group;}
            s.Construction.Revision++;return true;
        }
    }
}
