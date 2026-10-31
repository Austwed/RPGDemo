using System;
using BaseConquest.Battle;
namespace BaseConquest.Base
{
    public enum DeveloperTargetKind { None,Unit,Cell,Facility }
    // 效果是待执行请求；选错目标、取消或切模式不能对旧目标生效。
    public sealed class DeveloperTargetSelection
    {
        public DeveloperTargetKind Kind {get;private set;}
        public string Title {get;private set;}="";
        private DeveloperUnitAction unitAction;
        private TerrainKind terrain;
        private SoldierKind spawn;
        private bool spawning,production;
        public void Cancel(){Kind=DeveloperTargetKind.None;Title="";}
        public void Unit(DeveloperUnitAction action,string title){Cancel();if(!Enum.IsDefined(typeof(DeveloperUnitAction),action))throw new ArgumentException("未知单位效果");Kind=DeveloperTargetKind.Unit;unitAction=action;Title=title;}
        public void Terrain(TerrainKind kind){Cancel();if(!Enum.IsDefined(typeof(TerrainKind),kind))throw new ArgumentException("未知地形");Kind=DeveloperTargetKind.Cell;terrain=kind;spawning=false;Title="临时改为"+DeveloperTerrain.Name(kind);}
        public void Spawn(SoldierKind kind){Cancel();if(kind==SoldierKind.Hero||!Enum.IsDefined(typeof(SoldierKind),kind))throw new ArgumentException("只能生成临时士兵");Kind=DeveloperTargetKind.Cell;spawn=kind;spawning=true;Title="生成临时"+BattleSimulation.KindName(kind);}
        public void Production(bool active){Cancel();Kind=DeveloperTargetKind.Facility;production=active;Title=active ? "开启设施产出" : "停用设施产出";}
        public bool ApplyUnit(bool enabled,BattleSimulation battle,int id)
        {if(!enabled||Kind!=DeveloperTargetKind.Unit||battle==null||!battle.DeveloperUnit(true,id,unitAction))return false;Cancel();return true;}
        public bool ApplyRealtimeUnit(bool enabled,RealtimeCampaignBattle battle,int id)
        {if(!enabled||Kind!=DeveloperTargetKind.Unit||battle==null||!battle.DeveloperUnit(true,id,unitAction))return false;Cancel();return true;}
        public bool ApplyBattleCell(bool enabled,BattleSimulation battle,GridPosition cell,BattleSide side)
        {if(!enabled||Kind!=DeveloperTargetKind.Cell||battle==null)return false;bool ok=spawning ? battle.DeveloperSpawn(true,cell,spawn,side)!=null : battle.DeveloperPaint(true,cell,terrain);if(ok)Cancel();return ok;}
        public bool ApplyBaseCell(bool enabled,BaseState state,GridPosition cell)
        {
            if(!enabled||state==null)return false;bool ok=false;
            if(Kind==DeveloperTargetKind.Cell&&!spawning)ok=BaseDeveloperTools.Paint(true,state,cell,terrain);
            if(Kind==DeveloperTargetKind.Facility&&state.Campaign.Location=="base")
            {string id=BaseSpatial.AtCell(state,cell.X,cell.Z)?.Id;if(id=="farm"||id=="workshop")ok=BaseDeveloperTools.Production(true,state,id,production);}
            if(ok)Cancel();return ok;
        }
    }
}
