using System;
using System.Linq;
using UnityEngine.SceneManagement;
namespace BaseConquest.Base
{
    public sealed partial class BaseSceneController
    {
        public void SaveSlot(string id) { try { Companions.Capture(State); BaseBattleGateway.Catalog().SaveSlot(id,State,"手动存档 "+id);BaseBattleGateway.Session().Restore(State); Message="已保存"+id; } catch(Exception e) { Message=e.Message; } Hud.OpenSlots(true); }
        public void LoadSlot(string id, bool backup=false)
        {
            try { var next=BaseBattleGateway.Catalog().LoadSlot(id,backup); if(next.Aftermath.GameOver) throw new InvalidOperationException("此档已结束，选择其他存档"); BaseBattleGateway.Session().Restore(next); BaseBattleGateway.MainMenu=false; BaseBattleGateway.RequestedId=null; SceneManager.LoadScene(BaseBattleGateway.ExplorationScene); }
            catch(Exception e) { Message="读档失败，当前进度保留："+e.Message; Hud.OpenSlots(false); }
        }
        public void ToMainMenu() { BaseBattleGateway.MainMenu=true; BaseBattleGateway.RequestedId=null; SceneManager.LoadScene(BaseBattleGateway.ExplorationScene); }
    }
    public sealed partial class BaseHud
    {
        private void RuleInfo(string title,string text,Action back)
        { BeginPanel(title); RowButton("返回",0,800,back); row+=60; Line(text,Math.Max(260,text.Length/32*27+text.Count(c=>c=='\n')*24),20); EndPanel(); }
        public void OpenOptions()
        {
            BeginPanel("游戏选项");
            RowButton("返回",0,800,OpenMenu);row+=60;
            RowButton("和平地图格子描边："+(BaseConquest.Battle.MapDisplayOptions.ShowGridOutlines ? "开启" : "关闭"),0,800,()=>{BaseConquest.Battle.MapDisplayOptions.ShowGridOutlines=!BaseConquest.Battle.MapDisplayOptions.ShowGridOutlines;OpenOptions();});row+=64;
            RowButton("战斗格子描边："+(BaseConquest.Battle.MapDisplayOptions.BattleGridOutlines ? "开启" : "关闭"),0,800,()=>{BaseConquest.Battle.MapDisplayOptions.BattleGridOutlines=!BaseConquest.Battle.MapDisplayOptions.BattleGridOutlines;OpenOptions();});row+=64;
            RowButton("开发者模式："+(BaseConquest.Battle.RuntimeDeveloperOptions.Enabled ? "开启" : "关闭"),0,800,()=>{BaseConquest.Battle.RuntimeDeveloperOptions.Enabled=!BaseConquest.Battle.RuntimeDeveloperOptions.Enabled;OpenOptions();});row+=64;
            if(BaseConquest.Battle.RuntimeDeveloperOptions.Enabled&&BaseConquest.Presentation.HomeTerrainTrial.Eligible(owner.State))
            {RowButton("故乡地面试点："+(BaseConquest.Presentation.HomeTerrainTrial.Enabled?"新版候选":"原版"),0,800,()=>{owner.ToggleHomeTerrainTrial();OpenOptions();});row+=64;}
            Line("战斗格线默认开启，和平地图单独设置。启用开发者模式后，地图上出现开发者按钮；关闭时恢复临时地形、无敌与禁行，开启所有设施产出。",100,19);EndPanel();
        }
        public void OpenMainMenu()
        {
            if(BaseBattleGateway.DemoMode)
            {
                BeginPanel("基地征服 · RPG Demo");
                Line("角色探索与养成：序章→结识伙伴／调查遗迹→领主任务→经营整备→防守／攻营→返乡断剑。Demo独立3手动／6自动槽；默认继续最近存档，无档从序章开始。不兼容旧档会清除并重新开始，不迁移。",140,20);
                if(owner.State!=null&&!owner.State.Aftermath.GameOver){RowButton("继续Demo",0,800,()=>{BaseBattleGateway.MainMenu=false;owner.LoadSaved();});row+=60;}
                RowButton("读取Demo存档",0,800,()=>OpenSlots(false));row+=60;
                if(BaseConquest.Battle.RuntimeDeveloperOptions.Enabled)RowButton("阶段测试档",0,800,OpenDemoStages);row+=60;
                AddTemplates();RowButton("难度设置",0,800,OpenDifficulty);row+=60;RowButton("选项",0,800,OpenOptions);row+=60;EndPanel();return;
            }
            BeginPanel("基地征服 · 即时格战隔离流程");Line("3手动／6节点自动档；退出游戏将丢失未保存的会话更改。",58,18);
            RowButton("进入Demo流程起步（独立存档）",0,800,owner.EnterDemo);row+=60;
            if(owner.State!=null&&!owner.State.Aftermath.GameOver){RowButton("继续本次会话",0,800,()=>{BaseBattleGateway.MainMenu=false;owner.LoadSaved();});row+=60;}
            RowButton("读取存档",0,800,()=>OpenSlots(false));row+=60;
            RowButton("开始推荐测试",0,800,()=>owner.NewTemplate(owner.RecommendedTemplate)); row+=60;
            RowButton("开始新游戏",0,800,()=>{ BeginPanel("选择新开局"); AddTemplates(); EndPanel(); }); row+=60;
            RowButton("难度设置",0,800,OpenDifficulty);row+=60;RowButton("选项",0,800,OpenOptions);row+=60;
            RowButton("难度规则 i",0,800,()=>RuleInfo("难度规则","新游戏默认普通难度。进入后在菜单选择简单／普通／困难；目前只影响战斗伤害。",OpenMainMenu)); row+=60; EndPanel();
        }
        public void OpenGameOver()
        {
            BeginPanel("游戏结束"); RowButton("回到主菜单",0,800,owner.ToMainMenu); row+=66;
            RowButton("读取存档",0,800,()=>OpenSlots(false));row+=66;Line(owner.State.Aftermath.Reason,100,24); EndPanel();
        }
        public void OpenSlots(bool saving)
        {
            var entries=UnityEngine.Application.isPlaying ? BaseBattleGateway.Catalog().Entries() : new System.Collections.Generic.List<BaseSaveEntry>();
            BeginPanel(saving ? "手动保存 · 3个槽位" : "读取游戏 · 3手动／6自动");
            RowButton("返回",-210,400,()=>{if(BaseBattleGateway.MainMenu) OpenMainMenu(); else if(owner.State?.Aftermath.GameOver==true) OpenGameOver(); else OpenMenu();});
            RowButton("存档规则 i",210,400,()=>RuleInfo("存档规则","3个手动槽需主动保存，覆盖前确认；6个自动槽只在新游戏、完整推进一天、关卡最终奖励全部处理后轮换。普通经营操作和战斗中不写盘。会话内退出普通战斗回到确认前，序章追兵战无损重试；Demo遗迹和首次防守失败可回本场战前，胜利保留真实战损。完全退出后读最近磁盘档，不保留未保存的会话更改。Demo序章完成、领主接取、遗迹救助及交付各一次节点；首防战利品／俘虏全部处理完才存，不在战胜瞬间抢存。旧档文件不删除。",()=>OpenSlots(saving))); row+=66;
            if(!saving&&BaseBattleGateway.DemoMode&&BaseConquest.Battle.RuntimeDeveloperOptions.Enabled){RowButton("阶段测试档",0,800,OpenDemoStages);row+=64;}
            foreach(string key in saving ? BaseSaveCatalog.Slots : BaseSaveCatalog.Slots.Concat(BaseSaveCatalog.AutoSlots))
            {
                string id=key; var e=entries.FirstOrDefault(x=>x.Id==id);
                string label=BaseSaveCatalog.AutoSlots.Contains(id) ? "自动 "+id+" · "+(e?.Name??"") : "手动 "+id;
                Line(label+" · "+(e==null ? "空" : e.Error!=null ? "无法读取" : "第"+e.Day+"天"+(e.InBattle ? " · "+e.CombatProgress : " · "+(e.SceneTitle??BaseCampaign.LocationName(e.Location)))+(e.GameOver ? " · 已结束" : "")+" · "+e.TimeLabel),42,19);
                if(saving) RowButton("保存到此槽",0,800,()=>{if(e==null) owner.SaveSlot(id); else Confirm("覆盖"+label,"只替换此槽，原内容保留一份备份。",()=>owner.SaveSlot(id));},owner.State!=null&&!owner.State.Aftermath.GameOver&&!owner.State.Campaign.HasActiveBattle);
                else { RowButton("读取",-210,400,()=>owner.LoadSlot(id),e!=null&&e.Error==null&&!e.GameOver); RowButton("读取此槽备份",210,400,()=>owner.LoadSlot(id,true),e!=null); }
                row+=60;
            }
            EndPanel();
        }
        private void OpenDifficulty()
        {
            BeginPanel("游戏难度");
            var difficulty=new BaseDifficulty();
            foreach(string key in new[] { "easy","normal","hard" }) { string id=key; RowButton((difficulty.Preset==id ? "● " : "")+(id=="easy" ? "简单" : id=="hard" ? "困难" : "普通"),id=="easy" ? -290 : id=="normal" ? 0 : 290,270,()=>{difficulty.Set(id);OpenDifficulty();}); } row+=66;
            RowButton("返回菜单",0,800,OpenMainMenu); row+=60;
            Line("简单：我方造成伤害×2.0，受到伤害×0.5（含遗迹侵蚀）。普通：敌我攻防倍率均为×1.0。困难：我方受到伤害×1.3（含遗迹侵蚀）。",52,19); EndPanel();
        }
        public void OpenPrisoners()=>OpenLoot();
    }
}
