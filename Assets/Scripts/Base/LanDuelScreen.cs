using System;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        public LanDuelScreen OpenLanDuel()
        {
            if (owner.DuelScreen != null) return owner.DuelScreen;
            if (owner.State == null || owner.Busy || owner.State.Campaign.Location != "base" || owner.State.Campaign.HasActiveBattle)
                return null;
            var node = new GameObject("LanDuel", typeof(RectTransform));
            node.transform.SetParent(root, false);
            var screen = node.AddComponent<LanDuelScreen>();
            owner.DuelScreen = screen;
            try { screen.Open(owner, font); }
            catch { screen.Leave(); throw; }
            return screen;
        }
    }

    // This session owns the only writable camp copy. It never opens a campaign battle or a save slot.
    public sealed partial class LanDuelScreen : MonoBehaviour
    {
        [Serializable] private class Packet
        {
            public string type;
            public int version;
            public LanDuelRoster roster;
            public LanDuelCommand command;
            public LanDuelFrame frame;
        }
        private const int Protocol = 4;
        private BaseSceneController owner;
        private BaseState camp;
        private Font font;
        private CanvasGroup campInputs;
        private bool campWasInteractive, campReceivedClicks;
        private LanDuelTransport wire;
        private LanDuelRoster mine, theirs;
        private LanDuelMatch match;
        private float nextBroadcast;
        private int commandSeq, seenRooms = -1;
        private bool peerConnected, joining, hosting, localReady;
        private string notice = "";
        public string Phase { get; private set; } = "lobby";
        public bool Connected => wire?.Connected == true;
        public bool IsHost => wire?.IsHost == true;
        public int LocalSide => IsHost ? 0 : 1;
        public BaseBattlePreparation Draft { get; private set; }
        public LanDuelFrame Frame { get; private set; }
        private void OpenSessionDraft()
        {
            Draft = new BaseBattlePreparation(camp, BasePrepRoute.Travel);
            prepTeam = Draft.PlayerId;
            placements.Clear();
            mine = theirs = null;
            localReady = false;
        }
        internal void Open(BaseSceneController controller, Font uiFont)
        {
            owner = controller; font = uiFont;
            camp = controller.State.Copy();
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            gameObject.AddComponent<Image>().color = DemoUiTheme.Current.Wood;
            // Keyboard navigation must not submit a camp button behind the duel.
            gameObject.AddComponent<CanvasGroup>().ignoreParentGroups = true;
            if (!transform.parent.TryGetComponent(out campInputs))
                campInputs = transform.parent.gameObject.AddComponent<CanvasGroup>();
            campWasInteractive = campInputs.interactable; campReceivedClicks = campInputs.blocksRaycasts;
            campInputs.interactable = false; campInputs.blocksRaycasts = false;
            EventSystem.current?.SetSelectedGameObject(null);
            wire = new LanDuelTransport();
            OpenSessionDraft();
            wire.Browse();
            ShowLobby();
        }
        public void HostRoom()
        {
            if (Phase != "lobby") return;
            wire.Host(Draft.Units.First(u => u.Id == Draft.PlayerId).Name + "的营地");
            hosting = wire.IsHost; joining = false; notice = wire.Error ?? "";
            ShowLobby();
        }
        public void JoinRoom(string address)
        {
            if (Phase != "lobby" || string.IsNullOrWhiteSpace(address)) return;
            wire.Join(address.Trim()); joining = string.IsNullOrEmpty(wire.Error); hosting = false;
            notice = wire.Error ?? ""; ShowLobby();
        }
        private void Browse()
        {
            wire.Browse(); hosting = joining = peerConnected = false;
            notice = wire.Error ?? ""; Phase = "lobby"; ShowLobby();
        }
        private bool Send(Packet packet)
        {
            packet.version = Protocol;
            if (wire.Send(JsonUtility.ToJson(packet))) return true;
            Fail("连接已中断。"); return false;
        }
        public void Ready()
        {
            if (Phase != "preparing" || !Connected) return;
            try
            {
                mine = MakeRoster();
                string error = mine.Problem(camp.Demo.Rpg);
                if (!string.IsNullOrEmpty(error)) { Hint(error); return; }
                localReady = true;
                if (!IsHost && !Send(new Packet { type = "ready", roster = mine })) return;
                if (IsHost && !Send(new Packet { type = "host-ready" })) return;
                Phase = "waiting"; ShowWaiting(); StartIfReady();
            }
            catch (InvalidOperationException ex) { Hint(ex.Message); }
        }
        private void StartIfReady()
        {
            if (!IsHost || !localReady || theirs == null || match != null) return;
            try { match = new LanDuelMatch(mine, theirs, camp); }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            { Fail("编队无法开战：" + ex.Message); return; }
            Phase = "fighting"; Frame = match.View(0);
            if (!Send(new Packet { type = "start", frame = match.View(1) })) return;
            ShowBattle();
        }
        private bool hostReady;
        private void Receive(string json)
        {
            var packet = JsonUtility.FromJson<Packet>(json);
            if (packet == null || packet.version != Protocol) { Fail("双方游戏版本不一致。"); return; }
            if (packet.type == "leave") { if (Phase != "result") Fail("对方已离开演武。"); return; }
            if (IsHost)
            {
                if (packet.type == "ready" && theirs == null && (Phase == "preparing" || Phase == "waiting"))
                {
                    if (packet.roster == null || !string.IsNullOrEmpty(packet.roster.Problem(camp.Demo.Rpg)))
                    { Fail("对方的编队无法进入演武。"); return; }
                    theirs = packet.roster;
                    Hint("对方已就绪。"); StartIfReady();
                }
                else if (packet.type == "order" && Phase == "fighting" && packet.command != null)
                    match.Apply(1, packet.command);
                return;
            }
            if (packet.type == "host-ready" && (Phase == "preparing" || Phase == "waiting"))
            { hostReady = true; Hint("房主已就绪。"); }
            if (packet.type != "start" && packet.type != "frame") return;
            var snapshot = packet.frame;
            if (snapshot == null || snapshot.width != LanDuelMatch.Width || snapshot.height != LanDuelMatch.Height ||
                snapshot.visibleCells?.Length != snapshot.width * snapshot.height || snapshot.units == null ||
                snapshot.units.Length > 2 * (3 + BaseMuster.BattleSoldierLimit) ||
                snapshot.units.Any(u => u == null || (uint)u.side > 1 || !float.IsFinite(u.x) || !float.IsFinite(u.z)) ||
                !(snapshot.seconds >= 0 && snapshot.seconds <= 3600))
            { Fail("双方战场数据不一致，请重新发起演武。"); return; }
            if (packet.type == "start" && Phase == "waiting")
            { Frame = packet.frame; Phase = "fighting"; ShowBattle(); }
            else if (packet.type == "frame" && Phase == "fighting")
                PresentFrame(packet.frame);
        }
        public void Order(string kind, int actor, int target, float x, float z)
        {
            if (Phase != "fighting" || !Connected) return;
            var order = new LanDuelCommand { seq = ++commandSeq, kind = kind, actor = actor, target = target, x = x, z = z };
            if (IsHost) match.Apply(0, order); else Send(new Packet { type = "order", command = order });
        }
        public void VotePause() => Order("pause", 0, -1, 0, 0);
        public void Resume() => Order("resume", 0, -1, 0, 0);
        private void Update()
        {
            if (wire == null) return;
            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true) { Leave(); return; }
            try
            {
                wire.Poll();
                if (!peerConnected && Connected && Phase == "lobby")
                { peerConnected = true; Phase = "preparing"; notice = ""; ShowPreparation(); }
                while (wire != null && wire.TryReceive(out string message)) Receive(message);
                if (wire == null) return;
                if (!Connected && (peerConnected || !string.IsNullOrEmpty(wire.Error)) && Phase != "result" && Phase != "disconnected")
                { Fail(string.IsNullOrEmpty(wire.Error) ? "对方已断开连接。" : wire.Error); return; }
                if (Phase == "lobby" && !hosting && !joining && seenRooms != wire.RoomsRevision)
                    UpdateRooms();
                if (Phase == "fighting" && IsHost)
                {
                    match.Tick(Time.unscaledDeltaTime);
                    if (Time.unscaledTime >= nextBroadcast)
                    {
                        nextBroadcast = Time.unscaledTime + .08f;
                        var local = match.View(0);
                        if (Send(new Packet { type = "frame", frame = match.View(1) })) PresentFrame(local);
                    }
                }
                if (Phase == "fighting") DrawActors();
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is FormatException || ex is OverflowException)
            { Fail("演武会话中断：" + ex.Message); }
        }
        private void PresentFrame(LanDuelFrame frame)
        {
            Frame = frame;
            if (frame.result != 0) { Phase = "result"; ShowResult(); }
        }
        private void Fail(string reason)
        {
            if (Phase == "disconnected" || Phase == "result") return;
            Phase = "disconnected"; notice = reason;
            wire?.Dispose(); wire = null; ShowDisconnect();
        }
        public void Leave()
        {
            if (wire?.Connected == true) wire.Send(JsonUtility.ToJson(new Packet { version = Protocol, type = "leave" }));
            wire?.Dispose(); wire = null; Phase = "closed";
            RestoreCampInputs();
            if (owner != null && owner.DuelScreen == this) owner.DuelScreen = null;
            gameObject.SetActive(false); Destroy(gameObject);
        }
        private void OnApplicationPause(bool paused)
        {
            if (paused && wire != null && (joining || hosting || peerConnected) && Phase != "result")
                Fail("演武已中止，营地进度已保留。");
        }
        private void OnDestroy()
        {
            wire?.Dispose(); wire = null;
            RestoreCampInputs();
            if (owner != null && owner.DuelScreen == this) owner.DuelScreen = null;
        }
        private void RestoreCampInputs()
        {
            if (campInputs == null) return;
            campInputs.interactable = campWasInteractive;
            campInputs.blocksRaycasts = campReceivedClicks;
            campInputs = null;
        }
    }
}
