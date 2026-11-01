using System;
using System.Collections.Generic;
using BaseConquest.Battle;
namespace BaseConquest.Base
{
    public enum ConstructionProject { None, Demolish, Trap, Sandbag }
    public sealed class ConstructionPaintResult
    {
        public int Applied, Skipped, MaterialsSpent;
        public string Problem="";
    }
    // 工具选择是临时输入状态；已完成的工程仍由原地图和库存持久化。
    public sealed class ConstructionBrush
    {
        public ConstructionProject Project {get;private set;}
        public bool Active => Project!=ConstructionProject.None;
        public bool Drawing {get;private set;}
        private GridPosition? previous;
        private readonly HashSet<GridPosition> visited=new HashSet<GridPosition>();
        public void Select(ConstructionProject project){EndStroke();Project=project;}
        public void Cancel(){EndStroke();Project=ConstructionProject.None;}
        public void BeginStroke(){EndStroke();Drawing=Active;}
        public void EndStroke(){Drawing=false;previous=null;visited.Clear();}
        // 跨过面板或离开地图时断开连线，但同一次按住仍不重复处理已经过的格子。
        public void BreakLine(){previous=null;}
        public List<GridPosition> Sample(GridPosition cell)
        {
            var result=new List<GridPosition>();if(!Drawing)return result;
            var from=previous??cell;int x=from.X,z=from.Z,dx=Math.Abs(cell.X-x),dz=Math.Abs(cell.Z-z),sx=Math.Sign(cell.X-x),sz=Math.Sign(cell.Z-z),error=dx-dz;
            while(true){var p=new GridPosition(x,z);if(visited.Add(p))result.Add(p);if(x==cell.X&&z==cell.Z)break;int twice=error*2;if(twice>-dz){error-=dz;x+=sx;}if(twice<dx){error+=dx;z+=sz;}}
            previous=cell;return result;
        }
        public string Problem(BaseState state,GridPosition cell) => !Active ? "请先选择建造项目或拆除工具" : BaseConstruction.DemoFieldworkProblem(state,cell,Project);
        public ConstructionPaintResult Apply(BaseState state,IEnumerable<GridPosition> cells)
        {
            var result=new ConstructionPaintResult();var seen=new HashSet<GridPosition>();
            foreach(var cell in cells)
            {
                if(!seen.Add(cell))continue;string problem=Problem(state,cell);
                if(problem!=""){result.Skipped++;if(result.Problem=="")result.Problem=problem;continue;}
                int before=state.Inventory.Materials;bool ok=BaseConstruction.DemoFieldwork(state,cell,Project);
                if(ok){result.Applied++;result.MaterialsSpent+=before-state.Inventory.Materials;}
                else{result.Skipped++;if(result.Problem=="")result.Problem="位置或材料已变化";}
            }
            return result;
        }
    }
}
