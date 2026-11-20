using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
namespace BaseConquest.Base
{
    public sealed class BaseSaveEntry
    {
        public string Id, Name, PlayerName, Error, SavedUtc, Location, SceneTitle;
        public int Day, Round;
        public bool GameOver, InBattle, Realtime;
        public bool CanLoad => Error == null && !GameOver && !Realtime;
        public bool CanRecover { get; internal set; }
        public double Seconds;
        public string CombatProgress => Realtime ? Seconds.ToString("0")+"秒" : "第"+Round+"轮";
        public string TimeLabel => DateTime.TryParse(SavedUtc,out var time) ? time.ToLocalTime().ToString("MM-dd HH:mm") : "";
        internal BaseSaveEntry Copy() => (BaseSaveEntry)MemberwiseClone();
    }
    public sealed class BaseSaveCatalog
    {
        private readonly string folder;
        private readonly Func<BaseSaveDocument,string> encode;
        private readonly Func<string,BaseSaveDocument> decode;
        private readonly Func<Stream,string> fileDigest;
        private readonly Dictionary<string,(string Digest, BaseSaveEntry Entry)> summaries = new Dictionary<string,(string, BaseSaveEntry)>();
        private bool checkedExisting, needsCheck;
        public readonly bool DemoMode;
        public static readonly string[] Slots = Enumerable.Range(1,3).Select(i => "slot" + i).ToArray();
        public static readonly string[] AutoSlots = Enumerable.Range(1,6).Select(i => "auto" + i).ToArray();
        public BaseSaveCatalog(string folder, Func<BaseSaveDocument,string> encode, Func<string,BaseSaveDocument> decode,bool demo=false,bool checkExisting=true)
            : this(folder,encode,decode,demo,checkExisting,Sha256) { }
        internal BaseSaveCatalog(string folder, Func<BaseSaveDocument,string> encode, Func<string,BaseSaveDocument> decode,bool demo,bool checkExisting,Func<Stream,string> fileDigest)
        {
            this.folder=Path.GetFullPath(folder);this.encode=encode;this.decode=decode;DemoMode=demo;
            this.fileDigest = fileDigest;
            if(checkExisting)CheckExisting();
        }
        private static string Sha256(Stream file)
        {
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(file));
        }
        internal void RequestCheck() { needsCheck = DemoMode && !checkedExisting; }
        private void CheckExisting(Dictionary<string,(string Path,BaseSaveEntry Entry)> current = null)
        {
            if(!DemoMode || checkedExisting)return;
            if(Directory.Exists(folder))CheckExistingFiles(current);
            checkedExisting = true;
            needsCheck = false;
        }
        private void CheckExistingFiles(Dictionary<string,(string Path,BaseSaveEntry Entry)> current)
        {
            // Only known save files inside this selected Demo profile; never recurse or remove author data.
            foreach(string id in AutoSlots.Concat(Slots).Concat(new[]{"auto","before-battle","slot4","slot5","slot6"}))
            {
                var store = ResolveStore(id);
                foreach(string suffix in new[]{"",".bak",".tmp"})
                {
                    string path = store.Path+suffix;
                    if(!File.Exists(path))continue;
                    try
                    {
                        if(suffix=="")
                        {
                            var entry = ReadEntry(id,store);
                            if(current!=null)current[id] = (path,entry);
                        }
                        else store.DecodeDocument(File.ReadAllText(path,Encoding.UTF8));
                    }
                    catch(ObsoleteDemoSaveException)
                    {
                        File.Delete(path);
                        // A previous named file can become the active slot when its successor is removed.
                        if(suffix=="")store = ResolveStore(id);
                    }
                    catch(Exception error)
                    {
                        // An unreadable slot must stay visible so the player can choose its backup.
                        if(current!=null && suffix=="")current[id] = (path,new BaseSaveEntry {Id=id,Error=error.Message});
                    }
                }
            }
        }
        public BaseSaveStore Store(string id)
        {
            if(needsCheck)CheckExisting();
            return ResolveStore(id);
        }
        private BaseSaveStore ResolveStore(string id)
        {
            if (!Slots.Contains(id) && !AutoSlots.Contains(id) && !new[]{"slot4","slot5","slot6","before-battle","auto"}.Contains(id)) throw new ArgumentException("未知存档槽");
            string filename=id=="auto"?"base-save-v1.json":id+".json";
            string path=DemoMode&&Directory.Exists(folder)
                ?Directory.EnumerateFiles(folder,"*-"+filename)
                    .Where(file=>Path.GetFileName(file).EndsWith("-"+filename,StringComparison.Ordinal))
                    .OrderByDescending(File.GetLastWriteTimeUtc).ThenBy(file=>file,StringComparer.Ordinal).FirstOrDefault()
                :null;
            return CreateStore(path??Path.Combine(folder,filename));
        }
        private BaseSaveStore CreateStore(string path)=>new BaseSaveStore(path,encode,decode,DemoMode);
        private void Save(string id,BaseSaveStore previous,BaseState state,string label,bool validated=false)
        {
            string name=DemoMode?state.Demo?.Intro?.PlayerName:null;
            var destination=BaseDemoIntro.TryPlayerName(name,out var prefix)
                ?CreateStore(Path.Combine(folder,prefix+"-"+id+".json")):previous;
            bool same = string.Equals(previous.Path,destination.Path,
                Path.DirectorySeparatorChar=='\\'?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal);
            var writer = same ? previous : destination;
            if(validated)writer.WriteValidated(state,label);
            else writer.Save(state,label);
            if(same)return;
            if(!previous.Exists)return;
            File.Copy(previous.Path,destination.Path+".bak",true);
            File.Delete(previous.Path);
        }
        public List<BaseSaveEntry> Entries() => Entries(false);
        internal List<BaseSaveEntry> Entries(bool manualOnly)
        {
            Dictionary<string,(string Path,BaseSaveEntry Entry)> current = null;
            if(needsCheck)
            {
                // Only this synchronous listing may use the entries read by its own compatibility scan.
                current = new Dictionary<string,(string,BaseSaveEntry)>();
                CheckExisting(current);
            }
            var list=new List<BaseSaveEntry>();
            var ids = manualOnly ? Slots : DemoMode ? AutoSlots.Concat(Slots) : AutoSlots.Concat(Slots).Concat(new[] { "auto", "before-battle","slot4","slot5","slot6" });
            foreach(string id in ids)
            {
                var store=Store(id); if (!store.Exists) continue;
                try
                {
                    var entry = current!=null && current.TryGetValue(id,out var read) && read.Path==store.Path
                        ? read.Entry : ReadEntry(id,store);
                    var listed = entry.Copy();
                    if (listed.Error != null) listed.CanRecover = HasPlayableBackup(store);
                    list.Add(listed);
                }
                catch(Exception e) { list.Add(new BaseSaveEntry {Id=id,Error=e.Message,CanRecover=HasPlayableBackup(store)}); }
            }
            return list;
        }
        private static bool HasPlayableBackup(BaseSaveStore store)
        {
            if (!File.Exists(store.Path + ".bak")) return false;
            try
            {
                var state = store.LoadBackup();
                return !state.Aftermath.GameOver && BasePlaySession.RestoreError(state) == null;
            }
            catch (Exception) { return false; }
        }
        private BaseSaveEntry ReadEntry(string id, BaseSaveStore store)
        {
            using var file = File.OpenRead(store.Path);
            string digest = fileDigest(file);
            if(summaries.TryGetValue(id,out var cached) && digest==cached.Digest)
                return cached.Entry;

            // Read and validate the same file we hashed, even if a slot is replaced outside the game.
            file.Position = 0;
            using var reader = new StreamReader(file,Encoding.UTF8,true);
            var document = store.DecodeDocument(reader.ReadToEnd());
            var state = document.State;
            var entry = new BaseSaveEntry {Id=id};
            try
            {
                entry.Name = document.Label;
                entry.PlayerName = state.Demo?.Intro?.PlayerName;
                entry.SavedUtc = document.SavedUtc;
                entry.Day = state.Day;
                entry.GameOver = state.Aftermath.GameOver;
                entry.InBattle = state.Campaign.HasActiveBattle;
                var battle = state.Campaign.ActiveBattle;
                entry.Realtime = entry.InBattle && battle.RealtimeRules;
                entry.Seconds = entry.Realtime && battle.HasRealtimeState ? battle.RealtimeState.Seconds : 0;
                entry.Location = state.Campaign.Location;
                entry.SceneTitle = BaseCampaign.LocationName(state);
                entry.Round = entry.InBattle && battle.HasCheckpoint ? battle.Checkpoint.Round : 0;
            }
            catch(Exception e) { entry.Error=e.Message; }
            summaries[id] = (digest,entry);
            return entry;
        }
        public void SaveSlot(string id, BaseState state, string name)
        { if (!Slots.Contains(id) || state.Aftermath.GameOver || !string.IsNullOrEmpty(state.SceneBattleId)||state.Campaign.HasActiveBattle&&state.Campaign.ActiveBattle.RealtimeRules) throw new InvalidOperationException("当前不能手动保存；即时战斗结束或放弃本场后再保存"); Save(id,Store(id),state,name); }
        public BaseState LoadSlot(string id, bool backup=false) => backup ? Store(id).LoadBackup() : Store(id).Load();
        public void SaveAuto(BaseState state,string reason) => SaveAuto(state,reason,false);
        internal void SaveAuto(BaseState state,string reason,bool validated)
        {
            if(BaseSceneCombat.Active(state)||state.Aftermath.GameOver)throw new InvalidOperationException("战中或游戏结束时不写节点自动档");

            string destination = null;
            BaseSaveStore target = null;
            DateTime oldestWrite = DateTime.MaxValue;
            foreach (string id in AutoSlots)
            {
                var store = Store(id);
                var file = new FileInfo(store.Path);
                if (!file.Exists)
                {
                    destination = id;
                    target = store;
                    break;
                }

                DateTime lastWrite = file.LastWriteTimeUtc;
                if (destination == null || lastWrite < oldestWrite)
                {
                    destination = id;
                    target = store;
                    oldestWrite = lastWrite;
                }
            }
            Save(destination,target,state,reason,validated);
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
            if(recent!=null)Restore(catalog.LoadSlot(recent.Id),true);
            else if(!catalog.DemoMode&&catalog.Store("auto").Exists)
            {
                var legacy=catalog.Store("auto").Load();
                if(legacy.Campaign.HasActiveBattle&&legacy.Campaign.ActiveBattle.RealtimeRules)
                {
                    var opening=catalog.Store("before-battle").Load();
                    if(opening.Day!=legacy.Campaign.ActiveBattle.StartedDay||opening.Campaign.HasActiveBattle||!opening.Campaign.Events.Any(item=>item.Id==legacy.Campaign.ActiveBattle.EventId&&item.Status=="pending"))throw new InvalidOperationException("旧战中档缺少匹配的战前进度，原文件保留，请手动选择其他槽位");
                    Restore(opening,true);
                }
                else Restore(legacy,true);
            }
            }
            catch(Exception error){LoadError=error.Message;}
        }
        public void LoadPlayableSlot(string id,bool backup=false)
        {
            catalog.RequestCheck();
            var state=catalog.LoadSlot(id,backup);
            if(state.Aftermath.GameOver)throw new InvalidOperationException("此档已结束，选择其他存档");
            Restore(state,true);
        }
        public void Restore(BaseState state) => Restore(state,false);
        public void SaveExplorationNode(BaseState state, string label)
        {
            catalog.SaveAuto(state, label);
            Restore(state);
        }
        private void Restore(BaseState state,bool ownsDiskCopy)
        {
            var error = RestoreError(state);
            if (error != null) throw new InvalidOperationException(error);
            // Disk loads already validated and detached this state; callers of Restore keep ownership of theirs.
            if(ownsDiskCopy)Store.AdoptLoadedState(state);
            else Store.RestoreSession(state);
            beforeBattle=null;HasUnsavedChanges=false;LoadError=null;
        }
        internal static string RestoreError(BaseState state)
        {
            if (!string.IsNullOrEmpty(state.SceneBattleId)) return "同图遭遇不恢复战中状态，请读取战前节点";
            return state.Campaign.HasActiveBattle && state.Campaign.ActiveBattle.RealtimeRules
                ? "不再恢复战中检查点，请选择对应战前或其他存档" : null;
        }
        private static bool AwaitingName(BaseState state)=>BaseDemoIntro.Daily(state)&&state.Demo.Intro.Step==0;
        public void Start(BaseState state)
        {
            if(!catalog.DemoMode||!AwaitingName(state))catalog.SaveAuto(state,"开始新游戏");
            Restore(state);
        }
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
            if(previous==null||fighting||next.Aftermath.GameOver||catalog.DemoMode&&AwaitingName(next))return;
            bool entered=catalog.DemoMode&&(wasFighting||previous.Campaign.Location!=next.Campaign.Location||previous.Campaign.CurrentMapId!=next.Campaign.CurrentMapId||BaseDemoPrologue.Village(previous)!=BaseDemoPrologue.Village(next));
            string reason=catalog.DemoMode&&AwaitingName(previous)?"开始新游戏":entered?"进入"+BaseCampaign.LocationName(next):next.Day==previous.Day+1&&next.LastCompletedDay==previous.Day?"完整推进一天":previous.Aftermath.Loot.Any(old=>!old.Closed&&next.Aftermath.Loot.Any(item=>item.BattleId==old.BattleId&&item.Closed))?"关卡最终奖励处理完成":BaseDemo.AutoSaveReason(previous,next);
            if(reason!=null){catalog.SaveAuto(next,reason,true);HasUnsavedChanges=false;}
        }
    }
}
