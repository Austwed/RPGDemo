using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace BaseConquest.Base
{
    public sealed class BaseSaveEntry { public string Id, Name, Error, SavedUtc, Location, SceneTitle; public int Day,Round; public bool GameOver, InBattle,Realtime;public double Seconds;public string CombatProgress=>Realtime?Seconds.ToString("0.0")+"秒":"第"+Round+"轮"; public string TimeLabel=>DateTime.TryParse(SavedUtc,out var time)?time.ToLocalTime().ToString("MM-dd HH:mm"):""; }
    public sealed class BaseSaveCatalog
    {
        private readonly string folder;
        private readonly Func<BaseSaveDocument,string> encode;
        private readonly Func<string,BaseSaveDocument> decode;
        public readonly bool DemoMode;
        public static readonly string[] Slots = Enumerable.Range(1,3).Select(i => "slot" + i).ToArray();
        public static readonly string[] AutoSlots = Enumerable.Range(1,6).Select(i => "auto" + i).ToArray();
        public BaseSaveCatalog(string folder, Func<BaseSaveDocument,string> encode, Func<string,BaseSaveDocument> decode,bool demo=false,bool checkExisting=true)
        {
            this.folder=Path.GetFullPath(folder);this.encode=encode;this.decode=decode;DemoMode=demo;
            if(demo&&checkExisting)DiscardIncompatibleDemoFiles();
        }
        private void DiscardIncompatibleDemoFiles()
        {
            if(!Directory.Exists(folder))return;
            // Only known save files inside this selected Demo profile; never recurse or remove author data.
            foreach(string id in AutoSlots.Concat(Slots).Concat(new[]{"auto","before-battle","slot4","slot5","slot6"}))
            foreach(string suffix in new[]{"",".bak",".tmp"})
            {
                string path=Store(id).Path+suffix;if(!File.Exists(path))continue;
                try{new BaseSaveStore(path,encode,decode,true).Load();}
                catch(ArgumentException){File.Delete(path);}
            }
        }
        public BaseSaveStore Store(string id)
        {
            if (!Slots.Contains(id) && !AutoSlots.Contains(id) && !new[]{"slot4","slot5","slot6","before-battle","auto"}.Contains(id)) throw new ArgumentException("未知存档槽");
            return new BaseSaveStore(Path.Combine(folder,id == "auto" ? "base-save-v1.json" : id + ".json"),encode,decode,DemoMode);
        }
        public List<BaseSaveEntry> Entries()
        {
            var list=new List<BaseSaveEntry>();
            foreach(string id in DemoMode?AutoSlots.Concat(Slots):AutoSlots.Concat(Slots).Concat(new[] { "auto", "before-battle","slot4","slot5","slot6" }))
            {
                var store=Store(id); if (!store.Exists) continue;
                var entry=new BaseSaveEntry { Id=id };
                try { var doc=decode(File.ReadAllText(store.Path)); var s=store.Load(); entry.Name=doc.Label; entry.SavedUtc=doc.SavedUtc; entry.Day=s.Day; entry.GameOver=s.Aftermath.GameOver; entry.InBattle=s.Campaign.HasActiveBattle;entry.Realtime=entry.InBattle&&s.Campaign.ActiveBattle.RealtimeRules;entry.Seconds=entry.Realtime&&s.Campaign.ActiveBattle.HasRealtimeState?s.Campaign.ActiveBattle.RealtimeState.Seconds:0;entry.Location=s.Campaign.Location;entry.SceneTitle=BaseCampaign.LocationName(s);entry.Round=s.Campaign.HasActiveBattle&&s.Campaign.ActiveBattle.HasCheckpoint?s.Campaign.ActiveBattle.Checkpoint.Round:0; }
                catch(Exception e) { entry.Error=e.Message; }
                list.Add(entry);
            }
            return list;
        }
        public void SaveSlot(string id, BaseState state, string name)
        { if (!Slots.Contains(id) || state.Aftermath.GameOver || !string.IsNullOrEmpty(state.SceneBattleId)||state.Campaign.HasActiveBattle&&state.Campaign.ActiveBattle.RealtimeRules) throw new InvalidOperationException("当前不能手动保存；即时战斗结束或放弃本场后再保存"); Store(id).Save(state,name); }
        public BaseState LoadSlot(string id, bool backup=false) => backup ? Store(id).LoadBackup() : Store(id).Load();
        public void SaveAuto(BaseState state,string reason)
        {
            if(BaseSceneCombat.Active(state)||state.Aftermath.GameOver)throw new InvalidOperationException("战中或游戏结束时不写节点自动档");
            var entries=Entries();string slot=AutoSlots.FirstOrDefault(id=>!entries.Any(entry=>entry.Id==id));
            if(slot==null)slot=entries.Where(entry=>AutoSlots.Contains(entry.Id)).OrderBy(entry=>entry.Error==null?1:0).ThenBy(entry=>entry.SavedUtc,StringComparer.Ordinal).ThenBy(entry=>entry.Id,StringComparer.Ordinal).First().Id;
            Store(slot).Save(state,reason);
        }
    }
    public sealed class BasePlaySession
    {
        public readonly BaseSaveStore Store;
        private readonly BaseSaveCatalog catalog;
        private BaseState beforeBattle;
        private bool beforeUnsaved;
        public bool HasUnsavedChanges {get;private set;}
        public string LoadError {get;private set;}
        public BasePlaySession(BaseSaveCatalog catalog,string memoryPath,bool restoreLatest=true)
        {
            this.catalog=catalog;Store=BaseSaveStore.InMemory(memoryPath,ProgressChanged);
            if(!restoreLatest)return;
            try
            {
            var recent=catalog.Entries().Where(entry=>entry.Error==null&&!entry.InBattle&&(BaseSaveCatalog.Slots.Contains(entry.Id)||BaseSaveCatalog.AutoSlots.Contains(entry.Id))).OrderByDescending(entry=>entry.SavedUtc,StringComparer.Ordinal).FirstOrDefault();
            if(recent!=null)Restore(catalog.LoadSlot(recent.Id));
            else if(!catalog.DemoMode&&catalog.Store("auto").Exists)
            {
                var legacy=catalog.Store("auto").Load();
                if(legacy.Campaign.HasActiveBattle&&legacy.Campaign.ActiveBattle.RealtimeRules)
                {
                    var opening=catalog.Store("before-battle").Load();
                    if(opening.Day!=legacy.Campaign.ActiveBattle.StartedDay||opening.Campaign.HasActiveBattle||!opening.Campaign.Events.Any(item=>item.Id==legacy.Campaign.ActiveBattle.EventId&&item.Status=="pending"))throw new InvalidOperationException("旧战中档缺少匹配的战前进度，原文件保留，请手动选择其他槽位");
                    Restore(opening);
                }
                else Restore(legacy);
            }
            }
            catch(Exception error){LoadError=error.Message;}
        }
        public void Restore(BaseState state)
        {
            if(!string.IsNullOrEmpty(state.SceneBattleId))throw new InvalidOperationException("同图遭遇不恢复战中状态，请读取战前节点");
            if(state.Campaign.HasActiveBattle&&state.Campaign.ActiveBattle.RealtimeRules)throw new InvalidOperationException("不再恢复战中检查点，请选择对应战前或其他存档");
            Store.RestoreSession(state);beforeBattle=null;HasUnsavedChanges=false;LoadError=null;
        }
        public void Start(BaseState state){catalog.SaveAuto(state,"开始新游戏");Restore(state);}
        public void RememberBeforeBattle(BaseState state)
        {if(BaseSceneCombat.Active(state))throw new InvalidOperationException("已有战斗，不能覆盖战前回退点");BaseEconomy.Validate(state);beforeBattle=state.Copy();beforeUnsaved=HasUnsavedChanges;}
        public void AbandonBattle()
        {
            var current=Store.Load();
            if(!current.Campaign.HasActiveBattle)return;
            if(beforeBattle==null||beforeBattle.Day!=current.Campaign.ActiveBattle.StartedDay||!beforeBattle.Campaign.Events.Any(item=>item.Id==current.Campaign.ActiveBattle.EventId&&item.Status=="pending"))throw new InvalidOperationException("本次会话没有匹配的战前回退点");
            bool unsaved=beforeUnsaved;Restore(beforeBattle.Copy());HasUnsavedChanges=unsaved;
        }
        private void ProgressChanged(BaseState previous,BaseState next)
        {
            bool wasFighting=previous!=null&&BaseSceneCombat.Active(previous), fighting=BaseSceneCombat.Active(next);
            if(catalog.DemoMode&&previous!=null&&!wasFighting&&fighting)
            {
                catalog.SaveAuto(previous,"确认开战前");
                beforeBattle=previous.Copy();beforeUnsaved=false;
            }
            HasUnsavedChanges=true;
            if(previous==null||fighting||next.Aftermath.GameOver)return;
            bool entered=catalog.DemoMode&&(wasFighting||previous.Campaign.Location!=next.Campaign.Location||previous.Campaign.CurrentMapId!=next.Campaign.CurrentMapId||BaseDemoPrologue.Village(previous)!=BaseDemoPrologue.Village(next));
            string reason=entered?"进入"+BaseCampaign.LocationName(next):next.Day==previous.Day+1&&next.LastCompletedDay==previous.Day?"完整推进一天":previous.Aftermath.Loot.Any(old=>!old.Closed&&next.Aftermath.Loot.Any(item=>item.BattleId==old.BattleId&&item.Closed))?"关卡最终奖励处理完成":BaseDemo.AutoSaveReason(previous,next);
            if(reason!=null){catalog.SaveAuto(next,reason);HasUnsavedChanges=false;}
        }
    }
}
