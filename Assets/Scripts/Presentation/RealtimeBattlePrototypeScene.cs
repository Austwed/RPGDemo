using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BaseConquest.Presentation
{
    public sealed class RealtimeBattlePrototypeScene : MonoBehaviour
    {
        [SerializeField] private PrototypeLayout layout;
        [SerializeField] private int totalUnits=40;
        private readonly Dictionary<int,BattlePawn2D> pawns=new Dictionary<int,BattlePawn2D>();
        private readonly Dictionary<int,LineRenderer> swings=new Dictionary<int,LineRenderer>();
        private readonly Dictionary<int,LineRenderer> arrows=new Dictionary<int,LineRenderer>();
        private Transform generated;
        private Material material;
        private LineRenderer commandMarker;
        private GameObject fineGridRoot;
        private Mesh fineGridMesh;
        private Font font;
        private int selectedSquad=-1,selectedCompanion=-1;
        private PrototypeOrder selectedOrder=PrototypeOrder.Move;
        private string message="内存隔离实验。选择小队后右键下令；空格开始。";
        private Vector2 scroll;
        private double renderSeconds;
        private int renderFrames;
        private float displayedFps;
        public RealtimeBattlePrototype Battle { get; private set; }
        public BattleBoardView View { get; private set; }
        public string Message=>message;
        public bool FineGridVisible=>fineGridRoot!=null&&fineGridRoot.activeSelf;
        private float GuiScale=>Mathf.Max(.2f,Mathf.Min(Screen.width/1280f,Screen.height/720f));
        private void Start()=>Initialize();
        public void Initialize()
        {
            if(Battle!=null)return;
            var shader=Shader.Find("Universal Render Pipeline/Unlit");
            if(shader==null)throw new InvalidOperationException("即时战斗实验需要现有URP Unlit着色器");
            material=new Material(shader);material.SetColor("_BaseColor",Color.white);
            font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","Noto Sans CJK SC","Arial"},16);
            ResetBattle(layout,totalUnits);
        }
        public void ResetBattle(PrototypeLayout arena,int count)
        {
            var next=RealtimeBattlePrototype.Create(arena,count);
            View?.Dispose();
            BattleBoardView.DestroyObject(fineGridMesh);
            if(generated!=null)BattleBoardView.DestroyObject(generated.gameObject);
            pawns.Clear();swings.Clear();arrows.Clear();
            layout=arena;totalUnits=count;Battle=next;selectedCompanion=-1;selectedSquad=-1;
            generated=new GameObject("RealtimePrototype_Runtime").transform;generated.SetParent(transform,false);generated.gameObject.hideFlags=HideFlags.DontSave;
            var board=BattleBoard.CreateDefault(PrototypeArena.Width,PrototypeArena.Height);
            foreach(var cell in board.Positions())
            {board.Cell(cell).Zone=DeploymentZone.None;board.Cell(cell).Terrain=Battle.Arena.Wall(cell.X,cell.Z) ? TerrainKind.Wall : TerrainKind.Ground;board.Cell(cell).Surface=GroundSurface.Grass;}
            View=new BattleBoardView(board,generated,material,material){FlatView=true,ObliqueBaseView=true};
            View.SetSurfaceRegion("base");View.SetCombatMode(false);
            foreach(var unit in Battle.Units)
            {
                var root=new GameObject((unit.Side==PrototypeSide.Ally ? "Ally_" : "Enemy_")+unit.Id);root.transform.SetParent(generated,false);
                var pawn=root.AddComponent<BattlePawn2D>();
                pawn.Initialize(new BattleSoldier {Id=unit.Id,Kind=unit.Role==PrototypeRole.Leader ? SoldierKind.Hero : unit.Role==PrototypeRole.Archer ? SoldierKind.Ranged : SoldierKind.Melee,
                    Stats=new SoldierStats {Health=(float)unit.MaxHealth},Health=(float)unit.Health},material);
                if(unit.Player)pawn.EnablePlayerHero();
                pawn.UprightExploration=true;root.transform.localRotation=BattleBoardView.BasePawnTilt;root.transform.localScale=unit.Player ? new Vector3(1,1,2) : new Vector3(.85f,.85f,1.7f);
                pawns.Add(unit.Id,pawn);swings.Add(unit.Id,Line("AttackArc_"+unit.Id,.045f));
            }
            commandMarker=Line("SelectedOrderDestination",.055f);
            BuildFineGrid();
            message="已重开 "+count+" 人／"+LayoutName(layout)+"，48×28内部细格；全员AI，空格开始。";SyncView();
        }
        public void SelectSquad(int squad){selectedSquad=squad;selectedCompanion=-1;}
        public void SelectCompanion(int identity){selectedCompanion=identity;}
        public void SelectOrder(PrototypeOrder order){selectedOrder=order;}
        public bool CommandAt(PrototypePoint point)
        {
            var target=Battle.Units.Where(unit=>unit.Side==PrototypeSide.Enemy&&unit.Alive&&(unit.Position-point).Length<.7)
                .OrderBy(unit=>(unit.Position-point).Length).FirstOrDefault();
            var order=selectedOrder;
            if(order==PrototypeOrder.Focus&&target==null){message="集火请右键敌方单位。";return false;}
            bool accepted=Battle.IssueOrder(selectedSquad,selectedCompanion,order,point,target?.Id??-1);
            message=accepted ? "指令已下达："+OrderName(order)+(Battle.Paused ? "；解除暂停后执行。" : "。") : "未下令：目标不可站立、单位已倒下，或附近无足够可达落位。";
            return accepted;
        }
        private PrototypeUnit PickUnit(Vector2 screen,PrototypeSide side)
        {
            if(!View.Camera.pixelRect.Contains(screen)||screen.y>Screen.height-70*GuiScale)return null;
            float radius=Mathf.Max(10*GuiScale,View.Camera.pixelHeight/View.Camera.orthographicSize*.28f);
            return Battle.Units.Where(unit=>unit.Alive&&unit.Side==side)
                .Select(unit=>new {Unit=unit,Screen=(Vector2)View.Camera.WorldToScreenPoint(WorldPoint(unit.Position,.7f))})
                .Where(candidate=>Vector2.Distance(screen,candidate.Screen)<=radius).OrderBy(candidate=>Vector2.Distance(screen,candidate.Screen)).Select(candidate=>candidate.Unit).FirstOrDefault();
        }
        public bool SelectAtScreen(Vector2 screen,bool commanderOnly)
        {
            var unit=PickUnit(screen,PrototypeSide.Ally);if(unit==null)return false;
            if(commanderOnly&&unit.Role==PrototypeRole.Leader)SelectCompanion(unit.Id);else SelectSquad(unit.Squad);return true;
        }
        public bool CommandAtScreen(Vector2 screen)
        {
            if(!Pick(screen,out var point))return false;
            if(selectedOrder==PrototypeOrder.Focus)
            {
                var target=PickUnit(screen,PrototypeSide.Enemy);if(target==null){message="集火请右键敌方单位。";return false;}
                point=target.Position;
            }
            return CommandAt(point);
        }
        private void Update()
        {
            if(Battle==null)return;
            var keyboard=Keyboard.current;var mouse=Mouse.current;
            if(keyboard?.spaceKey.wasPressedThisFrame==true)Battle.SetPaused(!Battle.Paused);
            if(keyboard?.escapeKey.wasPressedThisFrame==true)Battle.SetPaused(true);
            if(keyboard?.digit1Key.wasPressedThisFrame==true)SelectSquad(0);
            if(keyboard?.digit2Key.wasPressedThisFrame==true)SelectSquad(1);
            if(keyboard?.digit3Key.wasPressedThisFrame==true)SelectSquad(2);
            if(keyboard?.gKey.wasPressedThisFrame==true)SetFineGrid(!FineGridVisible);
            if(mouse!=null&&mouse.leftButton.wasPressedThisFrame)SelectAtScreen(mouse.position.ReadValue(),keyboard?.leftShiftKey.isPressed==true);
            if(mouse!=null&&mouse.rightButton.wasPressedThisFrame)CommandAtScreen(mouse.position.ReadValue());
            Advance(Math.Min(Time.unscaledDeltaTime,.1f));
            renderSeconds+=Time.unscaledDeltaTime;renderFrames++;
            if(renderSeconds>=.5){displayedFps=(float)(renderFrames/renderSeconds);renderSeconds=0;renderFrames=0;}
        }
        public void Advance(double seconds){Battle.Tick(seconds);SyncView();}
        public void SetFineGrid(bool visible){if(fineGridRoot!=null)fineGridRoot.SetActive(visible);}
        private void BuildFineGrid()
        {
            fineGridRoot=new GameObject("OptionalFineBattleGrid",typeof(MeshFilter),typeof(MeshRenderer));fineGridRoot.transform.SetParent(generated,false);
            var vertices=new List<Vector3>();
            for(int column=0;column<=PrototypeArena.FineWidth;column++)
            {vertices.Add(WorldPoint(new PrototypePoint(column*.5,0),.065f));vertices.Add(WorldPoint(new PrototypePoint(column*.5,PrototypeArena.Height),.065f));}
            for(int row=0;row<=PrototypeArena.FineHeight;row++)
            {vertices.Add(WorldPoint(new PrototypePoint(0,row*.5),.065f));vertices.Add(WorldPoint(new PrototypePoint(PrototypeArena.Width,row*.5),.065f));}
            fineGridMesh=new Mesh {name="48x28_InternalBattleGrid"};fineGridMesh.SetVertices(vertices);fineGridMesh.SetIndices(Enumerable.Range(0,vertices.Count).ToArray(),MeshTopology.Lines,0);
            fineGridRoot.GetComponent<MeshFilter>().sharedMesh=fineGridMesh;var renderer=fineGridRoot.GetComponent<MeshRenderer>();renderer.sharedMaterial=material;
            var color=new MaterialPropertyBlock();color.SetColor("_BaseColor",new Color(.15f,.6f,.6f));renderer.SetPropertyBlock(color);fineGridRoot.SetActive(false);
        }
        private bool Pick(Vector2 screen,out PrototypePoint point)
        {
            point=new PrototypePoint();if(!View.Camera.pixelRect.Contains(screen)||screen.y>Screen.height-70*GuiScale)return false;
            var ray=View.Camera.ScreenPointToRay(screen);
            if(!new Plane(Vector3.up,Vector3.zero).Raycast(ray,out float distance))return false;
            var world=ray.GetPoint(distance);point=new PrototypePoint(world.x/BattleBoardView.CellSize+PrototypeArena.Width*.5,world.z/BattleBoardView.CellSize+PrototypeArena.Height*.5);
            return point.X>=0&&point.Z>=0&&point.X<PrototypeArena.Width&&point.Z<PrototypeArena.Height;
        }
        public Vector3 WorldPoint(PrototypePoint point,float height=.04f)=>new Vector3((float)(point.X-PrototypeArena.Width*.5)*BattleBoardView.CellSize,height,(float)(point.Z-PrototypeArena.Height*.5)*BattleBoardView.CellSize);
        public void SyncView()
        {
            if(View==null)return;
            View.UpdateCamera(312*GuiScale/Mathf.Max(1,Screen.width),0);
            float left=Mathf.Clamp(312*GuiScale/Mathf.Max(1,Screen.width),0,.4f);
            View.Camera.rect=new Rect(left,0,1-left,1);View.Camera.aspect=Mathf.Max(.1f,Screen.width*(1-left)/Mathf.Max(1,Screen.height));
            View.Camera.orthographicSize=Mathf.Max(PrototypeArena.Height*.6f+.4f,(PrototypeArena.Width*.6f+.4f)/View.Camera.aspect);
            foreach(var unit in Battle.Units)
            {
                var pawn=pawns[unit.Id];pawn.gameObject.SetActive(unit.Alive);
                if(unit.Alive)pawn.SyncExploration(WorldPoint(unit.Position),unit.FlashUntil>Battle.Elapsed ? Color.white : unit.Player ? new Color(.3f,1,.9f) : unit.Side==PrototypeSide.Ally ? new Color(.3f,.7f,1) : new Color(1,.35f,.25f));
                var arc=swings[unit.Id];bool active=unit.Alive&&unit.SwingUntil>Battle.Elapsed&&unit.HitAt>=0&&unit.Role==PrototypeRole.Leader&&unit.Side==PrototypeSide.Ally&&
                    (selectedCompanion>=0 ? unit.Id==selectedCompanion : selectedSquad<0||unit.Squad==selectedSquad);
                arc.gameObject.SetActive(active);
                if(active)
                {
                    double facing=Math.Atan2(unit.AttackFacing.Z,unit.AttackFacing.X);
                    var points=new List<Vector3>{WorldPoint(unit.Position,.09f)};
                    for(int index=0;index<=16;index++)
                    {double angle=facing-Math.PI/3+index*Math.PI/24;points.Add(WorldPoint(unit.Position+new PrototypePoint(Math.Cos(angle),Math.Sin(angle))*unit.Reach,.09f));}
                    points.Add(WorldPoint(unit.Position,.09f));arc.positionCount=points.Count;arc.SetPositions(points.ToArray());
                    arc.startColor=arc.endColor=unit.HitResolved ? Color.white : Color.yellow;
                }
            }
            foreach(int identity in arrows.Keys.Where(identity=>!Battle.Arrows.Any(arrow=>arrow.Id==identity)).ToArray())
            {BattleBoardView.DestroyObject(arrows[identity].gameObject);arrows.Remove(identity);}
            foreach(var arrow in Battle.Arrows)
            {
                if(!arrows.TryGetValue(arrow.Id,out var line)){line=Line("Arrow_"+arrow.Id,.06f);arrows.Add(arrow.Id,line);}
                var tip=arrow.Position;var back=tip-arrow.Direction*.34;var normal=new PrototypePoint(-arrow.Direction.Z,arrow.Direction.X);
                line.positionCount=5;line.SetPositions(new[]{WorldPoint(back,.45f),WorldPoint(tip,.45f),WorldPoint(tip-arrow.Direction*.12+normal*.09,.45f),WorldPoint(tip,.45f),WorldPoint(tip-arrow.Direction*.12-normal*.09,.45f)});
                line.startColor=line.endColor=arrow.Side==PrototypeSide.Ally ? Color.cyan : new Color(1,.6f,.15f);
            }
            var selected=Battle.Units.FirstOrDefault(unit=>unit.Alive&&unit.Side==PrototypeSide.Ally&&
                (selectedCompanion>=0 ? unit.Id==selectedCompanion : selectedSquad<0||unit.Squad==selectedSquad));
            commandMarker.gameObject.SetActive(selected!=null&&selected.Order!=PrototypeOrder.Auto);
            if(selected!=null&&selected.Order!=PrototypeOrder.Auto)
            {
                var goal=selected.Order==PrototypeOrder.Focus ? Battle.Units.FirstOrDefault(unit=>unit.Id==selected.TargetId&&unit.Alive)?.Position??selected.Anchor : selected.Anchor;
                double radius=selected.Order==PrototypeOrder.Guard ? 3 : .4;commandMarker.positionCount=33;
                for(int index=0;index<=32;index++){double angle=index*Math.PI/16;commandMarker.SetPosition(index,WorldPoint(goal+new PrototypePoint(Math.Cos(angle),Math.Sin(angle))*radius,.07f));}
                commandMarker.startColor=commandMarker.endColor=selected.Order==PrototypeOrder.Focus ? Color.red : Color.cyan;
            }
        }
        private LineRenderer Line(string title,float width)
        {
            var root=new GameObject(title);root.transform.SetParent(generated,false);var line=root.AddComponent<LineRenderer>();line.sharedMaterial=material;
            line.useWorldSpace=true;line.startWidth=line.endWidth=width;line.positionCount=0;return line;
        }
        private void OnApplicationFocus(bool focused){if(!focused&&Battle!=null)Battle.SetPaused(true);}
        private static string LayoutName(PrototypeLayout arena)=>arena==PrototypeLayout.Open ? "平地" : arena==PrototypeLayout.Choke ? "双格窄口" : "障碍绕行";
        private static string OrderName(PrototypeOrder order)=>order==PrototypeOrder.Auto ? "自主作战" : order==PrototypeOrder.Move ? "移动" : order==PrototypeOrder.Guard ? "驻守" : "集火";
        private void OnGUI()
        {
            if(Battle==null)return;var previousMatrix=GUI.matrix;var previousFont=GUI.skin.font;
            GUI.matrix=Matrix4x4.Scale(new Vector3(GuiScale,GuiScale,1));GUI.skin.font=font;
            try
            {
                var label=new GUIStyle(GUI.skin.label){fontSize=15,wordWrap=true};var button=new GUIStyle(GUI.skin.button){fontSize=14,wordWrap=true};
                GUILayout.BeginArea(new Rect(10,10,292,Screen.height/GuiScale-20),GUI.skin.box);scroll=GUILayout.BeginScrollView(scroll);
                GUILayout.Label("即时格战 · 队长指挥",new GUIStyle(label){fontSize=20});
                GUILayout.Label("只在内存运行，不读写存档\n不推进经营日，不替换旧回合战",label);
                GUILayout.Space(6);
                if(GUILayout.Button(Battle.Paused ? "继续战斗（空格）" : "暂停并指挥（空格）",button,GUILayout.Height(34)))Battle.SetPaused(!Battle.Paused);
                GUILayout.Label("左键选队，Shift＋左键单选队长\n右键下令，1／2／3选小队\n全员AI，无WASD手操；Esc暂停",label);
                if(GUILayout.Button(FineGridVisible ? "隐藏48×28细格（G）" : "显示内部细格（G）",button))SetFineGrid(!FineGridVisible);
                GUILayout.Space(6);GUILayout.Label("选择指挥对象（包含主角）",label);
                if(GUILayout.Button((selectedCompanion<0&&selectedSquad<0 ? "✓ " : "")+"全体友军",button))SelectSquad(-1);
                GUILayout.BeginHorizontal();
                for(int squad=0;squad<3;squad++)if(GUILayout.Button((selectedCompanion<0&&selectedSquad==squad ? "✓ " : "")+"小队"+(squad+1),button,GUILayout.Height(28)))SelectSquad(squad);
                GUILayout.EndHorizontal();
                foreach(var unit in Battle.Units.Where(unit=>unit.Side==PrototypeSide.Ally&&unit.Role==PrototypeRole.Leader))
                {
                    GUI.enabled=unit.Alive;
                    if(GUILayout.Button((selectedCompanion==unit.Id ? "✓ " : "")+(unit.Player ? "主角队长" : "伙伴"+unit.Squad)+" · 单独指挥",button))SelectCompanion(unit.Id);
                    GUI.enabled=true;
                }
                int order=GUILayout.SelectionGrid((int)selectedOrder,new[]{"自主作战","移动","驻守","集火"},2,button,GUILayout.Height(56));selectedOrder=(PrototypeOrder)order;
                GUILayout.Label(message,label);
                GUILayout.Space(6);GUILayout.Label("重开实验（仅清本场）",label);
                int count=GUILayout.SelectionGrid((totalUnits-30)/10,new[]{"30人","40人","50人"},3,button,GUILayout.Height(28));
                if(30+count*10!=totalUnits)ResetBattle(layout,30+count*10);
                int arena=GUILayout.SelectionGrid((int)layout,new[]{"平地","窄口","障碍"},3,button,GUILayout.Height(28));
                if(arena!=(int)layout)ResetBattle((PrototypeLayout)arena,totalUnits);
                if(GUILayout.Button("按当前设置重开",button,GUILayout.Height(28)))ResetBattle(layout,totalUnits);
                GUILayout.Label("时间 "+Battle.Elapsed.ToString("F1")+"秒 · FPS "+displayedFps.ToString("F0")+"\n命中 "+Battle.Hits+" · 发射 "+Battle.Shots+"\n走格 "+Battle.CompletedSteps+" · 让路 "+Battle.YieldMoves+"\n主角移速 "+Battle.Player.MoveSpeed.ToString("F2")+" 旧格/秒",label);
                GUILayout.Label("八向按实际距离计时；前后格预约，不重叠穿人。箭矢、步进、前摇与冷却都随暂停冻结。平地／无友伤／无骑兵；数值仅供实验。",label);
                GUILayout.EndScrollView();GUILayout.EndArea();
                int ally=Battle.Units.Count(unit=>unit.Side==PrototypeSide.Ally&&unit.Alive),enemy=Battle.Units.Count(unit=>unit.Side==PrototypeSide.Enemy&&unit.Alive);
                var banner=new GUIStyle(label){fontSize=19,alignment=TextAnchor.MiddleCenter};banner.normal.textColor=Color.white;
                GUI.Box(new Rect(320,10,Screen.width/GuiScale-330,54),"");
                string result=Battle.Outcome==PrototypeOutcome.Victory ? "蓝方胜利 · 可重开" : Battle.Outcome==PrototypeOutcome.Defeat ? "主角倒下／蓝方战败 · 可重开" : Battle.Paused ? "暂停中 · 修改队长指令后继续" : "即时格战 · 全员自动作战";
                GUI.Label(new Rect(325,12,Screen.width/GuiScale-340,50),result+"\n蓝方 "+ally+" ／ 红方 "+enemy+"　主角 "+Math.Ceiling(Battle.Player.Health)+" / "+Battle.Player.MaxHealth,banner);
                foreach(var unit in Battle.Units.Where(unit=>unit.Alive))
                {
                    var screen=View.Camera.WorldToScreenPoint(WorldPoint(unit.Position,1.5f));if(!View.Camera.pixelRect.Contains(screen))continue;
                    var rect=new Rect(screen.x/GuiScale-14,(Screen.height-screen.y)/GuiScale,28,4);var previousColor=GUI.color;
                    GUI.color=Color.black;GUI.DrawTexture(rect,Texture2D.whiteTexture);rect.width*=Mathf.Clamp01((float)(unit.Health/unit.MaxHealth));
                    GUI.color=unit.Side==PrototypeSide.Ally ? Color.green : Color.red;GUI.DrawTexture(rect,Texture2D.whiteTexture);GUI.color=previousColor;
                    if(unit.Player||unit.Id==selectedCompanion||unit.Side==PrototypeSide.Ally&&selectedCompanion<0&&(selectedSquad<0||unit.Squad==selectedSquad))
                        GUI.Label(new Rect(screen.x/GuiScale-16,(Screen.height-screen.y)/GuiScale-24,40,24),unit.Player ? "主角" : "▼",label);
                    bool selected=unit.Side==PrototypeSide.Ally&&(selectedCompanion>=0 ? unit.Id==selectedCompanion : selectedSquad<0||unit.Squad==selectedSquad);
                    if(selected&&(unit.Order==PrototypeOrder.Move||unit.Order==PrototypeOrder.Guard))
                    {
                        var goal=View.Camera.WorldToScreenPoint(WorldPoint(unit.Goal));GUI.color=Color.cyan;
                        GUI.DrawTexture(new Rect(goal.x/GuiScale-2,(Screen.height-goal.y)/GuiScale-2,4,4),Texture2D.whiteTexture);GUI.color=previousColor;
                    }
                }
            }
            finally{GUI.matrix=previousMatrix;GUI.skin.font=previousFont;}
        }
        private void OnDestroy()
        {
            View?.Dispose();BattleBoardView.DestroyObject(fineGridMesh);BattleBoardView.DestroyObject(material);BattleBoardView.DestroyObject(font);
        }
    }
}
