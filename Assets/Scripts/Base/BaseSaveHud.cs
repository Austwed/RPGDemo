using System;
using System.Linq;
using BaseConquest.Battle;
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
        public void ToMainMenu() { if(BaseBattleGateway.DemoMode){BaseBattleGateway.ExitDemoSession(false);return;} BaseBattleGateway.MainMenu=true; BaseBattleGateway.RequestedId=null; SceneManager.LoadScene(BaseBattleGateway.ExplorationScene); }
    }
    public sealed partial class BaseHud
    {
        private void RuleInfo(string title,string text,Action back)
        { BeginPanel(title); RowButton("返回",0,800,back); row+=60; Line(text,Math.Max(260,text.Length/32*27+text.Count(c=>c=='\n')*24),20); EndPanel(); }
        public void OpenOptions()
        {
            BeginPanel("游戏选项");
            RowButton("返回",0,800,OpenMenu);row+=60;
            RowButton("声音设置",0,800,OpenAudioOptions);row+=64;
            RowButton("和平地图格子描边："+(BaseConquest.Battle.MapDisplayOptions.ShowGridOutlines ? "开启" : "关闭"),0,800,()=>{BaseConquest.Battle.MapDisplayOptions.ShowGridOutlines=!BaseConquest.Battle.MapDisplayOptions.ShowGridOutlines;OpenOptions();});row+=64;
            RowButton("战斗格子描边："+(BaseConquest.Battle.MapDisplayOptions.BattleGridOutlines ? "开启" : "关闭"),0,800,()=>{BaseConquest.Battle.MapDisplayOptions.BattleGridOutlines=!BaseConquest.Battle.MapDisplayOptions.BattleGridOutlines;OpenOptions();});row+=64;
            if (RuntimeDeveloperOptions.Available)
            {
                RowButton("开发者模式："+(RuntimeDeveloperOptions.Enabled ? "开启" : "关闭"),0,800,()=>{RuntimeDeveloperOptions.Enabled=!RuntimeDeveloperOptions.Enabled;OpenOptions();});
                row+=64;
            }
            if(RuntimeDeveloperOptions.Enabled&&BaseConquest.Presentation.HomeTerrainTrial.Eligible(owner.State))
            {RowButton("故乡地面试点："+(BaseConquest.Presentation.HomeTerrainTrial.Enabled?"新版候选":"原版"),0,800,()=>{owner.ToggleHomeTerrainTrial();OpenOptions();});row+=64;}
            string hint = "战斗格线默认开启，和平地图单独设置。";
            if (RuntimeDeveloperOptions.Available) hint += "启用开发者模式后，地图上出现开发者按钮；关闭时恢复临时地形、无敌与禁行，开启所有设施产出。";
            Line(hint,100,19);EndPanel();
        }
        private void OpenAudioOptions()
        {
            BeginPanel("声音设置", "audio-settings");
            RowButton("返回选项", 0, 800, OpenOptions);
            row += 68;
            BaseConquest.Presentation.GameAudioVolume.AddControls(content, font, new UnityEngine.Vector2(.5f, 1), new UnityEngine.Vector2(0, -row - 58), 780);
            row += 116;
            EndPanel();
        }
        public void OpenMainMenu()
        {
            if(BaseBattleGateway.DemoMode)
            {
                BeginPanel("基地征服 · RPG Demo");
                Line("从白桦村出发，结识伙伴、经营营地，追查王室断剑。可从序章开始，或读取已有存档。",92,20);
                if(owner.State!=null&&!owner.State.Aftermath.GameOver){RowButton("继续Demo",0,800,()=>{BaseBattleGateway.MainMenu=false;owner.LoadSaved();});row+=60;}
                RowButton("读取Demo存档",0,800,()=>OpenSlots(false));row+=60;
                if(BaseConquest.Battle.RuntimeDeveloperOptions.Enabled)RowButton("阶段测试档",0,800,OpenDemoStages);row+=60;
                AddTemplates();RowButton("难度设置",0,800,OpenDifficulty);row+=60;RowButton("选项",0,800,OpenOptions);row+=60;RowButton("退出游戏",0,800,()=>BaseBattleGateway.ExitDemoSession(true));row+=60;EndPanel();return;
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
            BeginPanel((BaseBattleGateway.CustomMapTestMode ? "地图测试 · " : "") + (saving ? "手动保存 · 3个槽位" : "读取游戏 · 3手动／6自动"));
            RowButton("返回",-210,400,()=>{if(BaseBattleGateway.MainMenu) OpenMainMenu(); else if(owner.State?.Aftermath.GameOver==true) OpenGameOver(); else OpenMenu();});
            RowButton("存档规则 i",210,400,()=>RuleInfo("存档规则",BaseBattleGateway.DemoMode
                ?"确认姓名后建立新档，存档文件名以主角姓名开头。3 个手动槽覆盖前确认。6 个自动槽在进图、开战前及剧情节点轮换。回主菜单或退出后，继续游戏会读取最近的磁盘进度。"
                :"3 个手动槽需主动保存，覆盖前确认。6 个自动槽在新游戏、完整推进一天及关卡奖励处理完成后轮换。回主菜单或退出后，继续游戏会读取最近的磁盘进度。",()=>OpenSlots(saving))); row+=66;
            if(!saving&&BaseBattleGateway.DemoMode&&!BaseBattleGateway.CustomMapTestMode&&BaseConquest.Battle.RuntimeDeveloperOptions.Enabled){RowButton("阶段测试档",0,800,OpenDemoStages);row+=64;}
            foreach(string key in saving ? BaseSaveCatalog.Slots : BaseSaveCatalog.Slots.Concat(BaseSaveCatalog.AutoSlots))
            {
                string id=key; var e=entries.FirstOrDefault(x=>x.Id==id);
                string label=BaseSaveCatalog.AutoSlots.Contains(id) ? "自动 "+id+" · "+(e?.Name??"") : "手动 "+id;
                Line(label+" · "+(e==null ? "空" : e.Error!=null ? "无法读取" : (string.IsNullOrEmpty(e.PlayerName)?"":e.PlayerName+" · ")+"第"+e.Day+"天"+(e.InBattle ? " · "+e.CombatProgress : " · "+(e.SceneTitle??BaseCampaign.LocationName(e.Location)))+(e.GameOver ? " · 已结束" : "")+" · "+e.TimeLabel),42,19);
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
