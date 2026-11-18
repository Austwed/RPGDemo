using System;
using System.IO;
using System.Text;

namespace BaseConquest.Base
{
    internal sealed class ObsoleteDemoSaveException : ArgumentException
    {
        public ObsoleteDemoSaveException(string message,Exception inner = null) : base(message,inner) { }
    }
    [Serializable] public sealed class BaseSaveDocument
    {
        public int SchemaVersion = 1;
        public int DemoSaveVersion;
        public string Kind = "base-save";
        public BaseState State;
        public string Label, SavedUtc;
    }
    // JSON适配由调用者提供；存档层本身不依赖Unity，也不读取开局模板。
    public sealed class BaseSaveStore
    {
        public readonly string Path;
        private readonly Func<BaseSaveDocument, string> writeJson;
        private readonly Func<string, BaseSaveDocument> readJson;
        private readonly bool demo;
        private bool memoryOnly;
        private BaseState sessionState;
        private Action<BaseState,BaseState> changed;
        public static BaseSaveStore InMemory(string path,Action<BaseState,BaseState> changed=null)=>new BaseSaveStore(path,null,null){memoryOnly=true,changed=changed};
        public void RestoreSession(BaseState state)
        {if(!memoryOnly)throw new InvalidOperationException("此入口仅用于会话状态");BaseEconomy.Validate(state);sessionState=state.Copy();}
        internal void AdoptLoadedState(BaseState state)
        {
            if(!memoryOnly)throw new InvalidOperationException("此入口仅用于会话状态");
            sessionState=state;
        }
        public BaseSaveStore(string path, Func<BaseSaveDocument, string> encode, Func<string, BaseSaveDocument> decode,bool demo=false)
        { Path = path; writeJson = encode; readJson = decode; this.demo=demo; }
        public bool Exists => memoryOnly ? sessionState!=null : File.Exists(Path);
        public void Save(BaseState state, string label = null, BaseState beforeChange = null)
        {
            BaseEconomy.Validate(state);
            if(memoryOnly){var next=state.Copy();changed?.Invoke(beforeChange??sessionState,next);sessionState=next;return;}
            WriteValidated(state, label);
        }
        internal void WriteValidated(BaseState state, string label)
        {
            string json = writeJson(new BaseSaveDocument { DemoSaveVersion=demo?BaseDemo.SaveVersion:0,State = state.Copy(), Label = label ?? state.TemplateTitle, SavedUtc = DateTime.UtcNow.ToString("o") });
            // 写前回读验证，失败保留旧存档；同卷临时文件替换防止半写入。
            DecodeDocument(json);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path)));
            string temporary = Path + ".tmp";
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            if (File.Exists(Path)) File.Replace(temporary, Path, Path + ".bak"); else File.Move(temporary, Path);
        }
        public BaseState Load() => memoryOnly ? sessionState?.Copy()??throw new InvalidOperationException("尚无本次会话进度") : DecodeDocument(File.ReadAllText(Path, Encoding.UTF8)).State.Copy();
        public BaseState LoadBackup() => memoryOnly ? throw new InvalidOperationException("会话没有磁盘备份，请在读档页选择指定槽位") : DecodeDocument(File.ReadAllText(Path + ".bak", Encoding.UTF8)).State.Copy();
        internal BaseSaveDocument DecodeDocument(string json)
        {
            var document = readJson(json);
            if (document == null || document.SchemaVersion != 1 || document.Kind != "base-save") throw new ArgumentException("不是支持的据点进度存档");
            if(demo && document.DemoSaveVersion > 0 && document.DemoSaveVersion < BaseDemo.SaveVersion &&
                document.State?.Demo != null)
                throw new ObsoleteDemoSaveException("Demo存档版本不兼容，需重新开始");
            if(demo&&(document.DemoSaveVersion!=BaseDemo.SaveVersion||!BaseDemo.Active(document.State)))throw new ArgumentException("Demo存档版本不兼容，需重新开始");
            try { BaseEconomy.Validate(document.State); }
            catch(ArgumentException error) when(demo && document.State?.Demo?.Defense?.SecondOutcome == "damaged")
            {
                // The retired second-raid outcome has no corresponding state in the current rules.
                throw new ObsoleteDemoSaveException(error.Message,error);
            }
            if(!string.IsNullOrEmpty(document.State.SceneBattleId))throw new ArgumentException("不写入或恢复同图遭遇的战中存档，请使用战前节点");
            return document;
        }
    }
}
