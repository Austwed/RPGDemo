using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.RealtimePrototype;

namespace BaseConquest.Battle
{
    // 预览来自模拟器的同一个规划器；显示路径不会发出命令或消耗技能。
    public sealed class BattleOrderOverlay
    {
        public readonly GameObject Root;
        private readonly LineRenderer path, aim, target, obstacle, destination;
        private readonly List<PrototypePoint> realtimePath = new List<PrototypePoint>();
        private readonly MaterialPropertyBlock tint = new MaterialPropertyBlock();
        private int cachedId = -2, cachedRound = -1, cachedCommands = -1;
        private BattlePhase cachedPhase = (BattlePhase)(-1);
        private bool cachedAQueued, cachedBQueued;
        private int cachedVision = -1;
        public UnitOrderPreview Current { get; private set; }
        public int PathPointCount => path.enabled ? path.positionCount : 0;
        public bool TargetVisible => target.enabled;

        public BattleOrderOverlay(Transform parent, Material material)
        {
            Root = new GameObject("SelectedOrderOverlay"); Root.transform.SetParent(parent, false);
            path = MakeLine("CurrentMovePath", material, 0.085f);
            aim = MakeLine("LockedTargetLine", material, 0.04f);
            target = MakeLine("LockedTargetMarker", material, 0.065f);
            obstacle = MakeLine("BlockingEnemyLine", material, 0.06f); obstacle.positionCount = 2;
            obstacle.startColor = obstacle.endColor = new Color(1, 0.8f, 0.15f);
            aim.positionCount = 2; target.positionCount = 4; target.loop = true;
            destination = MakeLine("OrderDestination", material, 0.075f);
            destination.positionCount = 4;
            destination.loop = true;
        }
        private LineRenderer MakeLine(string name, Material material, float width)
        {
            var obj = new GameObject(name); obj.transform.SetParent(Root.transform, false);
            LineRenderer line = obj.AddComponent<LineRenderer>(); line.sharedMaterial = material;
            line.useWorldSpace = true; line.widthMultiplier = width; line.enabled = false;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; return line;
        }
        public void Invalidate() => cachedId = -2;
        private void Tint(LineRenderer line,Color color)
        {
            line.startColor=line.endColor=color;tint.SetColor("_BaseColor",color);tint.SetColor("_Color",color);line.SetPropertyBlock(tint);
        }
        private static Vector3 RealtimePosition(PrototypePoint point,BattleBoard board)=>new Vector3(((float)point.X-board.Width*.5f)*BattleBoardView.CellSize,.16f,((float)point.Z-board.Height*.5f)*BattleBoardView.CellSize);
        public void RefreshRealtime(RealtimeCampaignBattle run, PrototypeUnit unit)
        {
            Root.SetActive(true);Current=null;Invalidate();
            path.enabled=aim.enabled=target.enabled=obstacle.enabled=destination.enabled=false;
            if(unit==null||!unit.Alive||unit.Side!=PrototypeSide.Ally||run.Battle.Phase!=BattlePhase.Fighting||!run.Battle.Vision.CanSee(BattleSide.A,run.Battle.Soldier(unit.Id-1)))return;
            bool showLines = MapDisplayOptions.BattleLines;
            if (showLines)
            {
                run.Model.PreviewPath(unit,realtimePath);
                int count=0;
                foreach(var point in realtimePath)
                {
                    if(!run.Battle.Vision.Explored(BattleSide.A,new GridPosition((int)point.X,(int)point.Z)))break;
                    count++;
                }
                if(count>=2)
                {
                    path.enabled=true;path.positionCount=count;
                    Tint(path,unit.BlockedFor>.35?new Color(1,.68f,.2f):new Color(.3f,1,.48f));
                    for(int i=0;i<count;i++)path.SetPosition(i,RealtimePosition(realtimePath[i],run.Battle.Board));
                }
            }
            if (unit.Role == PrototypeRole.Leader && unit.ManualOrder && !unit.Steering && !unit.ObjectOrder &&
                (unit.Order == PrototypeOrder.Move || unit.Order == PrototypeOrder.Guard))
            {
                bool guarding = unit.Order == PrototypeOrder.Guard;
                var arena = run.Model.Arena;
                var goal = guarding ? unit.Anchor : unit.Goal;
                int cell = arena.CellAt(goal);
                if (cell >= 0 && run.Battle.Vision.Explored(BattleSide.A, new GridPosition((int)goal.X, (int)goal.Z)))
                {
                    destination.enabled = true;
                    Tint(destination, guarding ? new Color(1, .78f, .22f) : new Color(.3f, 1, .48f));
                    var center = RealtimePosition(arena.Center(cell), run.Battle.Board) + Vector3.up * .015f;
                    float halfWidth = BattleBoardView.CellSize * .23f;
                    destination.SetPosition(0, center + new Vector3(-halfWidth, 0, -halfWidth));
                    destination.SetPosition(1, center + new Vector3(-halfWidth, 0, halfWidth));
                    destination.SetPosition(2, center + new Vector3(halfWidth, 0, halfWidth));
                    destination.SetPosition(3, center + new Vector3(halfWidth, 0, -halfWidth));
                }
            }
            var locked=run.Model.Units.FirstOrDefault(actor=>actor.Id==unit.TargetId&&actor.Alive&&actor.Side!=unit.Side);
            if(locked==null||!run.Battle.Vision.CanSee(BattleSide.A,run.Battle.Soldier(locked.Id-1)))return;
            aim.enabled=showLines;target.enabled=true;var color=unit.Order==PrototypeOrder.Focus?new Color(1,.25f,.3f):new Color(1,.83f,.25f);Tint(aim,color);Tint(target,color);
            var position=RealtimePosition(locked.Position,run.Battle.Board);aim.SetPosition(0,RealtimePosition(unit.Position,run.Battle.Board));aim.SetPosition(1,position);
            float radius=BattleBoardView.CellSize*.24f;
            target.SetPositions(new[]{position+new Vector3(-radius,0,-radius),position+new Vector3(-radius,0,radius),position+new Vector3(radius,0,radius),position+new Vector3(radius,0,-radius)});
        }
        public void Refresh(BattleSimulation battle, int id, float presentation, GridPosition? commandPreview = null, BattleSide? observer = null)
        {
            bool aQueued = battle.SkillQueued(BattleSide.A), bQueued = battle.SkillQueued(BattleSide.B);
            if (commandPreview.HasValue) { Current = battle.PreviewCommand(id, commandPreview.Value); cachedId = -2; }
            else if (cachedId != id || cachedRound != battle.RoundNumber || cachedCommands != battle.CommandsIssued || cachedPhase != battle.Phase || cachedAQueued != aQueued || cachedBQueued != bQueued || cachedVision != battle.Vision.Revision)
            {
                Current = id >= 0 ? battle.PreviewOrder(id) : null;
                cachedId = id; cachedRound = battle.RoundNumber; cachedCommands = battle.CommandsIssued; cachedAQueued = aQueued; cachedBQueued = bQueued;
                cachedPhase = battle.Phase;
                cachedVision = battle.Vision.Revision;
            }
            path.enabled = aim.enabled = target.enabled = obstacle.enabled = destination.enabled = false;
            BattleSoldier soldier = battle.Soldier(id);
            if (Current == null || soldier == null || !soldier.IsActive || battle.Phase != BattlePhase.Fighting || !battle.Vision.CanSee(observer ?? soldier.Side, soldier)) return;
            var shownPath = presentation < 1 && soldier.LastAction == RoundAction.Move ? soldier.LastMovePath : Current.Path;
            bool showLines = MapDisplayOptions.BattleLines;
            if (showLines && shownPath.Count > 1)
            {
                int count = 1;
                while (count < shownPath.Count && battle.Vision.Explored(observer ?? soldier.Side, shownPath[count])) count++;
                path.enabled = count > 1; path.positionCount = count;
                path.startColor = path.endColor = Current.NextStepBlocked ? new Color(1, 0.68f, 0.2f) : new Color(0.3f, 1, 0.48f);
                for (int i = 0; i < count; i++) path.SetPosition(i, BattleBoardView.World(shownPath[i], battle.Board) + Vector3.up * 0.14f);
            }
            if (showLines && presentation >= 1 && Current.ImmediateTargetId >= 0 && Current.ImmediateTargetId != Current.LockedTargetId && battle.Soldier(Current.ImmediateTargetId) is BattleSoldier blocker)
            {
                obstacle.enabled = true; obstacle.SetPosition(0, BattleSoldierView.Position(battle, soldier) + Vector3.up * 0.95f);
                obstacle.SetPosition(1, BattleSoldierView.Position(battle, blocker) + Vector3.up * 0.95f);
            }
            GridPosition? shownTarget = presentation < 1 && (soldier.LastAction == RoundAction.Attack || soldier.LastAction == RoundAction.Skill) ? soldier.LastTargetCell : Current.TargetCell;
            if (shownTarget.HasValue && battle.Vision.Explored(observer ?? soldier.Side, shownTarget.Value))
            {
                aim.enabled = showLines;
                target.enabled = true;
                Color tint = Current.LockedTargetId >= 0 ? new Color(1, 0.25f, 0.3f) : new Color(1, 0.83f, 0.25f);
                aim.startColor = aim.endColor = target.startColor = target.endColor = tint;
                BattleSoldier locked = battle.Soldier(Current.LockedTargetId);
                Vector3 point = (locked != null && presentation >= 1 ? BattleSoldierView.Position(battle, locked) : BattleBoardView.World(shownTarget.Value, battle.Board)) + Vector3.up * 0.95f;
                aim.SetPosition(0, BattleSoldierView.Position(battle, soldier) + Vector3.up * 0.95f); aim.SetPosition(1, point);
                target.SetPositions(new[] { point + new Vector3(-0.48f, 0, -0.48f), point + new Vector3(-0.48f, 0, 0.48f), point + new Vector3(0.48f, 0, 0.48f), point + new Vector3(0.48f, 0, -0.48f) });
            }
        }
    }
}
