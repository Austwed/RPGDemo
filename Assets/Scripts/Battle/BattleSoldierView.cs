using UnityEngine;

namespace BaseConquest.Battle
{
    public sealed class BattleSoldierView
    {
        public readonly BattleSoldier Soldier;
        public readonly GameObject Root;
        private readonly BattlePawn2D pawn;
        private readonly Transform body;
        private readonly Renderer renderer;
        private readonly Transform rider;
        private readonly Renderer riderRenderer;
        private readonly LineRenderer selection, attack, squadStripe;
        private readonly Color color;
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();

        public BattleSoldierView(BattleSoldier soldier, Transform parent, Material material, Material lineMaterial, bool flat = false)
        {
            Soldier = soldier; Root = new GameObject(BattleSimulation.SideName(soldier.Side) + "_" + soldier.Kind + "_" + soldier.Id);
            Root.transform.SetParent(parent, false);
            if (flat) { pawn = Root.AddComponent<BattlePawn2D>(); pawn.Initialize(soldier, material); return; }
            PrimitiveType shape = soldier.Kind == SoldierKind.Melee || soldier.Kind == SoldierKind.Cavalry ? PrimitiveType.Cube : soldier.Kind == SoldierKind.Ranged ? PrimitiveType.Cylinder : PrimitiveType.Capsule;
            GameObject visual = GameObject.CreatePrimitive(shape); visual.name = "Body"; visual.transform.SetParent(Root.transform, false);
            BattleBoardView.DestroyObject(visual.GetComponent<Collider>()); body = visual.transform;
            renderer = visual.GetComponent<Renderer>(); renderer.sharedMaterial = material;
            color = soldier.Side == BattleSide.A ? new Color(0.22f, 0.71f, 1) : new Color(1, 0.47f, 0.25f);
            if (soldier.Kind == SoldierKind.Hero) color = Color.Lerp(color, Color.yellow, 0.5f);
            if (soldier.Kind == SoldierKind.Cavalry)
            {
                GameObject mounted = GameObject.CreatePrimitive(PrimitiveType.Capsule); mounted.name = "Rider"; mounted.transform.SetParent(Root.transform, false);
                BattleBoardView.DestroyObject(mounted.GetComponent<Collider>()); rider = mounted.transform; riderRenderer = mounted.GetComponent<Renderer>(); riderRenderer.sharedMaterial = material;
            }
            selection = MakeLine("Selection", lineMaterial, 0.055f); selection.loop = true; selection.useWorldSpace = false; selection.positionCount = 4;
            selection.SetPositions(new[] { new Vector3(-0.48f, 0.06f, -0.48f), new Vector3(-0.48f, 0.06f, 0.48f), new Vector3(0.48f, 0.06f, 0.48f), new Vector3(0.48f, 0.06f, -0.48f) });
            attack = MakeLine("ActionLine", lineMaterial, 0.045f); attack.useWorldSpace = true; attack.positionCount = 2;
            squadStripe = MakeLine("SquadStripe", lineMaterial, 0.08f); squadStripe.useWorldSpace = false; squadStripe.positionCount = 2;
            squadStripe.SetPositions(new[] { new Vector3(-0.26f, 0.07f, -0.38f), new Vector3(0.26f, 0.07f, -0.38f) });
        }
        private LineRenderer MakeLine(string name, Material material, float width)
        {
            var child = new GameObject(name); child.transform.SetParent(Root.transform, false);
            LineRenderer line = child.AddComponent<LineRenderer>(); line.sharedMaterial = material; line.widthMultiplier = width;
            line.startColor = line.endColor = new Color(1, 0.9f, 0.35f); return line;
        }
        public static Vector3 Position(BattleSimulation battle, BattleSoldier s) => BattleBoardView.World(s.Cell, battle.Board) + (battle.UnitsAt(s.Cell).Count > 1 ? Vector3.right * (s.IsTransit ? 0.32f : -0.32f) : Vector3.zero);
        public void Sync(BattleSimulation battle, int selectedSquad, float presentation, int selectedUnit = -1, bool? selected = null, bool draft = false)
        {
            if (pawn != null) { pawn.Sync(battle, selected ?? selectedUnit == Soldier.Id, presentation, draft: draft); return; }
            bool showingAction = presentation < 1 && Soldier.LastAction != RoundAction.Wait;
            Root.SetActive(!Soldier.Escaped || showingAction);
            Vector3 previous = BattleBoardView.World(Soldier.PreviousCell, battle.Board) + (Soldier.PreviousShared ? Vector3.right * (Soldier.PreviousTransit ? 0.32f : -0.32f) : Vector3.zero);
            float progress = Mathf.SmoothStep(0, 1, presentation);
            if (Soldier.LastAction == RoundAction.Move && Soldier.LastMovePath.Count > 1)
            {
                float segment = progress * (Soldier.LastMovePath.Count - 1); int index = Mathf.Min(Mathf.FloorToInt(segment), Soldier.LastMovePath.Count - 2);
                Vector3 point = Vector3.Lerp(BattleBoardView.World(Soldier.LastMovePath[index], battle.Board), BattleBoardView.World(Soldier.LastMovePath[index + 1], battle.Board), segment - index);
                Root.transform.position = point + Vector3.Lerp(previous - BattleBoardView.World(Soldier.PreviousCell, battle.Board), Position(battle, Soldier) - BattleBoardView.World(Soldier.Cell, battle.Board), progress);
            }
            else Root.transform.position = Vector3.Lerp(previous, Position(battle, Soldier), progress);
            bool upright = Soldier.IsAlive || showingAction;
            body.localPosition = new Vector3(0, upright ? (Soldier.Kind == SoldierKind.Hero ? 0.6f : 0.45f) : 0.08f, 0);
            body.localScale = new Vector3(0.6f, upright ? (Soldier.Kind == SoldierKind.Melee ? 0.85f : Soldier.Kind == SoldierKind.Hero ? 0.55f : 0.4f) : 0.1f, 0.6f);
            if (Soldier.Kind == SoldierKind.Cavalry) { body.localPosition = new Vector3(0, upright ? 0.35f : 0.08f, 0); body.localScale = new Vector3(0.46f, upright ? 0.5f : 0.1f, 0.88f); }
            bool shared = battle.UnitsAt(Soldier.Cell).Count > 1;
            squadStripe.enabled = battle.Scenario.CommanderControl && Soldier.IsActive && !battle.Scenario.Leaderless(Soldier.Side);
            if (squadStripe.enabled)
            {
                int slot = battle.Squads[Soldier.SquadId].CommanderSlot;
                squadStripe.startColor = squadStripe.endColor = slot == 0 ? new Color(0.25f, 1, 0.65f) : slot == 1 ? new Color(0.8f, 0.55f, 1) : new Color(1, 0.85f, 0.3f);
            }
            if (shared) body.localScale = Vector3.Scale(body.localScale, new Vector3(0.72f, 1, 0.72f));
            Color tint = upright ? color : new Color(0.11f, 0.14f, 0.18f);
            if (Soldier.IsTransit && upright) tint = Color.Lerp(tint, Color.white, 0.5f);
            if (showingAction && (Soldier.LastAction == RoundAction.Attack || Soldier.LastAction == RoundAction.Skill)) tint = Color.Lerp(tint, Color.white, 0.45f);
            if (BattleSoldier.DamageFlashVisible(Soldier.LastDamageTaken, presentation)) tint = new Color(1, 0.12f, 0.12f);
            properties.SetColor("_BaseColor", tint); renderer.SetPropertyBlock(properties);
            if (rider != null) { rider.gameObject.SetActive(upright); rider.localPosition = new Vector3(0, 0.92f, 0); rider.localScale = new Vector3(0.3f, 0.3f, 0.3f) * (shared ? 0.72f : 1); riderRenderer.SetPropertyBlock(properties); }
            selection.transform.localScale = shared ? Vector3.one * 0.58f : Vector3.one;
            selection.enabled = Soldier.IsActive && (selected ?? (selectedUnit >= 0 ? Soldier.Id == selectedUnit : Soldier.SquadId == selectedSquad));
            attack.enabled = showingAction && (Soldier.LastAction == RoundAction.Attack || Soldier.LastAction == RoundAction.Skill);
            if (attack.enabled) { attack.SetPosition(0, Root.transform.position + Vector3.up * 0.9f); attack.SetPosition(1, BattleBoardView.World(Soldier.LastTargetCell, battle.Board) + Vector3.up * 0.9f); }
        }
        public void ApplyVisibility(BattleSimulation battle, BattleSide observer, float presentation)
        {
            if (pawn != null) { pawn.SetVisibility(battle, observer, presentation); return; }
            // 动画不显示跨出当前视野的敌军，也不把隐藏攻击落点通过动作线泄露。
            bool seen = battle.Vision.CanSee(observer, Soldier);
            if (Soldier.Side != observer && presentation < 1) seen &= battle.Vision.Visible(observer, Soldier.PreviousCell);
            Root.SetActive(Root.activeSelf && seen);
            if (attack.enabled && !battle.Vision.Visible(observer, Soldier.LastTargetCell)) attack.enabled = false;
        }
        public void SyncRealtime(BattleSimulation battle,BaseConquest.RealtimePrototype.PrototypeUnit actor,double seconds,Vector3 feet,bool selected)
        {
            if(!battle.PlayerCanSee(BattleSide.A,Soldier)){Root.SetActive(false);return;}
            if(pawn!=null){pawn.Sync(battle,selected,1,false);pawn.SyncRealtime(battle,actor,seconds,feet);}
            else {Sync(battle,-1,1,-1,selected);Root.transform.position=feet;}
        }
    }
}
