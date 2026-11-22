using System;
using System.Linq;
using BaseConquest.Battle;
using UnityEngine.SceneManagement;
namespace BaseConquest.Base
{
    public sealed partial class BaseSceneController
    {
        public void SaveSlot(string id)
        {
            try
            {
                Companions.Capture(State);
                BaseBattleGateway.Catalog().SaveSlot(id, State, "手动存档 " + id);
                BaseBattleGateway.Session().Restore(State);
                Message = "已保存" + id;
            }
            catch (Exception e) { Message = e.Message; }
            Hud.OpenSlots(true);
        }
        public void LoadSlot(string id, bool backup=false)
        {
            try { BaseBattleGateway.Session().LoadPlayableSlot(id,backup); BaseBattleGateway.MainMenu=false; BaseBattleGateway.RequestedId=null; SceneLoadingCover.LoadExploration(); }
            catch(Exception e) { Message="读档失败，当前进度保留："+e.Message; Hud.OpenSlots(false); }
        }
        public void ToMainMenu() { if(BaseBattleGateway.DemoMode){BaseBattleGateway.ExitDemoSession(false);return;} BaseBattleGateway.MainMenu=true; BaseBattleGateway.RequestedId=null; SceneLoadingCover.LoadExploration(); }
    }
    public sealed partial class BaseHud
    {
        public void OpenOptions()
        {
            BeginPanel("游戏选项");
            RowButton("返回",0,800,OpenMenu);row+=60;
            if(BaseBattleGateway.MainMenu)
            {
                RowButton("难度设置",0,800,OpenDifficulty);row+=64;
                RowButton("帮助",0,800,OpenTitleHelp);row+=64;
            }
            RowButton("声音设置",0,800,OpenAudioOptions);row+=64;
            RowButton("和平地图格子描边："+(BaseConquest.Battle.MapDisplayOptions.ShowGridOutlines ? "开启" : "关闭"),0,800,()=>{BaseConquest.Battle.MapDisplayOptions.ShowGridOutlines=!BaseConquest.Battle.MapDisplayOptions.ShowGridOutlines;OpenOptions();});row+=64;
            RowButton("战斗格子描边："+(BaseConquest.Battle.MapDisplayOptions.BattleGridOutlines ? "开启" : "关闭"),0,800,()=>{BaseConquest.Battle.MapDisplayOptions.BattleGridOutlines=!BaseConquest.Battle.MapDisplayOptions.BattleGridOutlines;OpenOptions();});row+=64;
            RowButton("角色范围："+(MapDisplayOptions.BattleRanges ? "开启" : "关闭"),0,800,()=>{MapDisplayOptions.BattleRanges=!MapDisplayOptions.BattleRanges;OpenOptions();});row+=64;
            RowButton("战斗线条："+(MapDisplayOptions.BattleLines ? "开启" : "关闭"),0,800,()=>{MapDisplayOptions.BattleLines=!MapDisplayOptions.BattleLines;OpenOptions();});row+=64;
            if(RuntimeDeveloperOptions.Enabled&&BaseConquest.Presentation.HomeTerrainTrial.Eligible(owner.State))
            {RowButton("故乡地面试点："+(BaseConquest.Presentation.HomeTerrainTrial.Enabled?"新版候选":"原版"),0,800,()=>{owner.ToggleHomeTerrainTrial();OpenOptions();});row+=64;}
            EndPanel();
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
                OpenVillageTitle();
                return;
            }
            BeginPanel("DEMO · 即时格战隔离流程");Line("3手动／6节点自动档；退出游戏将丢失未保存的会话更改。",58,18);
            RowButton("进入Demo流程起步（独立存档）",0,800,owner.EnterDemo);row+=60;
            if(owner.State!=null&&!owner.State.Aftermath.GameOver){RowButton("继续本次会话",0,800,()=>{BaseBattleGateway.MainMenu=false;owner.LoadSaved();});row+=60;}
            RowButton("读取存档",0,800,()=>OpenSlots(false));row+=60;
            RowButton("开始推荐测试",0,800,()=>owner.NewTemplate(owner.RecommendedTemplate)); row+=60;
            RowButton("开始新游戏",0,800,()=>{ BeginPanel("选择新开局"); AddTemplates(); EndPanel(); }); row+=60;
            RowButton("难度设置",0,800,OpenDifficulty);row+=60;RowButton("选项",0,800,OpenOptions);row+=60;
            RowButton("帮助",0,800,OpenTitleHelp);row+=60;EndPanel();
        }
        public void OpenGameOver()
        {
            BeginPanel("游戏结束"); RowButton("回到主菜单",0,800,owner.ToMainMenu); row+=66;
            RowButton("读取存档",0,800,()=>OpenSlots(false));row+=66;Line(owner.State.Aftermath.Reason,100,24); EndPanel();
        }
        private bool SlotListOpen => panelViewKey == "save-slots" || panelViewKey == "load-slots";

        public void OpenSlots(bool saving)
        {
            var entries=UnityEngine.Application.isPlaying ? BaseBattleGateway.Catalog().Entries(saving) : new System.Collections.Generic.List<BaseSaveEntry>();
            BeginPanel((BaseBattleGateway.CustomMapTestMode ? "地图测试 · " : "") + (saving ? "手动保存 · 3个槽位" : "读取游戏 · 3手动／6自动"), saving ? "save-slots" : "load-slots");
            if(!saving&&BaseBattleGateway.DemoMode&&!BaseBattleGateway.CustomMapTestMode&&BaseConquest.Battle.RuntimeDeveloperOptions.Enabled){RowButton("阶段测试档",0,800,OpenDemoStages);row+=64;}
            foreach(string key in saving ? BaseSaveCatalog.Slots : BaseSaveCatalog.Slots.Concat(BaseSaveCatalog.AutoSlots))
            {
                string id=key; var e=entries.FirstOrDefault(x=>x.Id==id);
                string label=BaseSaveCatalog.AutoSlots.Contains(id) ? "自动 "+id+" · "+(e?.Name??"") : "手动 "+id;
                Line(label+" · "+(e==null ? "空" : e.Error!=null ? "无法读取" : (string.IsNullOrEmpty(e.PlayerName)?"":e.PlayerName+" · ")+"第"+e.Day+"天"+(e.InBattle ? " · "+e.CombatProgress : " · "+(e.SceneTitle??BaseCampaign.LocationName(e.Location)))+(e.GameOver ? " · 已结束" : "")+" · "+e.TimeLabel),42,19);
                if(saving) RowButton(BaseBattleGateway.DemoMode&&e!=null?"覆盖此槽（保留备份）":"保存到此槽",0,800,()=>
                {
                    if(e==null||BaseBattleGateway.DemoMode)owner.SaveSlot(id);
                    else Confirm("覆盖"+label,"只替换此槽，原内容保留一份备份。",()=>owner.SaveSlot(id));
                },owner.State!=null&&!owner.State.Aftermath.GameOver&&!owner.State.Campaign.HasActiveBattle);
                else if (e?.CanRecover == true) RowButton("恢复上次存档",0,800,()=>owner.LoadSlot(id,true));
                else RowButton("读取",0,800,()=>owner.LoadSlot(id),e?.CanLoad == true);
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
            EndPanel();
        }
        public void OpenPrisoners()=>OpenLoot();
    }
}
