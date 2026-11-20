using System.Collections.Generic;
using BaseConquest.Base;
using BaseConquest.RealtimePrototype;
using UnityEngine;
using UnityEngine.Rendering;

namespace BaseConquest.Battle
{
    // 像素人物优先，保留旧图／几何回退；投射物不依赖物理模拟。
    public sealed class BattlePawn2D : MonoBehaviour
    {
        private BattleSoldier soldier;
        private BaseConquest.Presentation.PlayerHeroView playerHero;
        private BaseConquest.Presentation.CompanionSpriteView companion;
        private const float UprightSheetOffset = .28f;
        public bool UprightExploration {get;set;}
        internal Vector3 GroundContact => transform.position - (UprightExploration ? Vector3.forward * UprightSheetOffset : Vector3.zero);
        public Vector3 ExplorationHeadPoint=>playerHero!=null?playerHero.HeadPoint:transform.TransformPoint(new Vector3(0,.28f,1.38f));
        internal Vector3 ExplorationVisualTop => playerHero?.VisualTop ?? companion?.VisualTop ?? ExplorationHeadPoint;
        internal bool CanSitOnBench=>playerHero?.CanSitOnBench==true;
        internal void HoldSelectionPose()
        {
            playerHero?.HoldSelectionPose();
            companion?.HoldSelectionPose();
        }
        public bool EnableCompanion(string appearance,bool combatMotion=true,bool walkingMotion=true)
        {
            if(companion==null)
            {
                if(BaseConquest.Presentation.CommanderArt.For(appearance)==null&&BaseConquest.Presentation.PixelDemoArt.Load("Characters",appearance)==null)return false;
                companion=gameObject.AddComponent<BaseConquest.Presentation.CompanionSpriteView>();
                if(!companion.Initialize(appearance,material,combatMotion,walkingMotion)){BattleBoardView.DestroyObject(companion);companion=null;return false;}
            }
            shape.gameObject.SetActive(false);
            float height=BaseConquest.Presentation.PixelDemoArt.Mounted(appearance) ? 2.4f : 1.65f;
            healthRoot.localPosition=new Vector3(0,.3f,height);healthFill.localPosition=new Vector3(healthFill.localPosition.x,.31f,height);selectionArrow.transform.localPosition=new Vector3(0,.4f,height+.27f);
            if(appearance=="ahe")EnsureProjectile();return true;
        }
        public bool EnablePlayerHero(string appearance="player")
        {
            if(playerHero==null){playerHero=gameObject.AddComponent<BaseConquest.Presentation.PlayerHeroView>();if(!playerHero.Initialize(material,appearance)){BattleBoardView.DestroyObject(playerHero);playerHero=null;return false;}}
            shape.gameObject.SetActive(false);healthRoot.localPosition=new Vector3(0,.3f,1.47f);healthFill.localPosition=new Vector3(healthFill.localPosition.x,.31f,1.47f);selectionArrow.transform.localPosition=new Vector3(0,.4f,1.74f);return true;
        }
        private readonly List<Mesh> meshes = new List<Mesh>();
        private readonly Dictionary<Renderer, Color> damageShapes = new Dictionary<Renderer, Color>();
        private readonly Dictionary<LineRenderer, Color> damageLines = new Dictionary<LineRenderer, Color>();
        private readonly List<Renderer> colored = new List<Renderer>();
        private readonly List<LineRenderer> lines = new List<LineRenderer>();
        private Material material;
        private Transform shape, healthFill, healthRoot, projectile;
        private Renderer crown, squadStar;
        private LineRenderer arrow, starInk;
        private Renderer selectionArrow;
        private Renderer groundOccupation;
        private static readonly PrototypeUnit FootDraft = new PrototypeUnit();
        private static readonly PrototypeUnit MountedDraft = new PrototypeUnit { FootprintLength = 2, Facing = new PrototypePoint(1, 0) };
        private Renderer missingArtFlag; // 缺外观时头顶的品红「缺图」占位标记，防止被当成正式美术遗漏。
        private BattleStatusEffectsView statusEffects;
        private MaterialPropertyBlock properties;
        public static Material Transparent(Material source)
        {
            // WorldTransparent.mat keeps this runtime keyword variant in player builds.
            var m = new Material(source); m.SetFloat("_Surface", 1); m.SetFloat("_ZWrite", 0);
            m.SetFloat("_SrcBlend", (int)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (int)BlendMode.One); m.SetFloat("_DstBlendAlpha", (int)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_Cull", 0); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = (int)RenderQueue.Transparent; return m;
        }
        public void Initialize(BattleSoldier s, Material source)
        {
            soldier = s; properties = new MaterialPropertyBlock(); material = Transparent(source);
            shape = new GameObject("Pawn2D").transform; shape.SetParent(transform, false);
            if (s.Kind == SoldierKind.Cavalry)
            {
                Shape("HorseBody", new Vector3(0, 0.12f, -0.2f), 0.76f, 0.36f, false, new Color(0.37f, 0.22f, 0.12f));
                Shape("HorseNeck", new Vector3(0.31f, 0.13f, 0.01f), 0.18f, 0.38f, false, new Color(0.45f, 0.28f, 0.14f));
                Shape("HorseHead", new Vector3(0.4f, 0.14f, 0.19f), 0.25f, 0.18f, false, new Color(0.51f, 0.34f, 0.19f));
                for (int i = 0; i < 4; i++) Shape("HorseLeg" + i, new Vector3(-0.29f + i * 0.18f, 0.11f, -0.43f), 0.08f, 0.27f, false, new Color(0.24f, 0.15f, 0.08f));
                Stroke("HorseTail", new[] { new Vector2(-0.36f, -0.1f), new Vector2(-0.48f, -0.26f) }, 0.07f);
            }
            float rider = s.Kind == SoldierKind.Cavalry ? 0.12f : 0;
            Shape("BodyOutline", new Vector3(0, 0.15f, -0.03f + rider), 0.44f, 0.5f, false, Color.black);
            Shape("Body", new Vector3(0, 0.16f, -0.03f + rider), 0.36f, 0.44f, false, Color.white, true);
            Shape("HeadOutline", new Vector3(0, 0.17f, 0.37f + rider), 0.225f, 0, true, Color.black);
            Shape("Head", new Vector3(0, 0.18f, 0.37f + rider), 0.19f, 0, true, new Color(0.96f, 0.83f, 0.66f));
            if (s.Kind == SoldierKind.Hero)
            {
                crown = Polygon("CommanderCrown", new Vector3(0, 0.25f, 0.69f), new[] { new Vector2(-0.19f, -0.065f), new Vector2(0.19f, -0.065f), new Vector2(0.23f, 0.12f), new Vector2(0.1f, 0.04f), new Vector2(0, 0.17f), new Vector2(-0.1f, 0.04f), new Vector2(-0.23f, 0.12f) }, Color.yellow);
            }
            else if (s.Kind == SoldierKind.Melee)
            {
                Shape("LeftArm", new Vector3(-0.24f, 0.19f, -0.02f), 0.19f, 0.09f, false, new Color(0.96f, 0.83f, 0.66f));
                Shape("RightArm", new Vector3(0.25f, 0.19f, -0.02f), 0.2f, 0.09f, false, new Color(0.96f, 0.83f, 0.66f));
                Polygon("LeftShield", new Vector3(-0.32f, 0.24f, -0.04f), new[] { new Vector2(-0.14f, 0.16f), new Vector2(-0.13f, -0.08f), new Vector2(0, -0.2f), new Vector2(0.13f, -0.08f), new Vector2(0.14f, 0.16f) }, new Color(0.58f, 0.66f, 0.73f));
                Stroke("ShieldRim", new[] { new Vector2(-0.45f, 0.11f), new Vector2(-0.45f, -0.12f), new Vector2(-0.32f, -0.24f), new Vector2(-0.19f, -0.12f), new Vector2(-0.18f, 0.11f), new Vector2(-0.45f, 0.11f) }, 0.025f);
                var sword = Stroke("RightSword", new[] { new Vector2(0.27f, -0.08f), new Vector2(0.55f, 0.15f) }, 0.045f); sword.startColor = sword.endColor = new Color(0.9f, 0.94f, 1);
                Stroke("SwordGuard", new[] { new Vector2(0.28f, 0.04f), new Vector2(0.4f, -0.08f) }, 0.04f);
            }
            else if (s.Kind == SoldierKind.Ranged)
            {
                Shape("BowHand", new Vector3(0.24f, 0.2f, -0.02f), 0.18f, 0.08f, false, new Color(0.96f, 0.83f, 0.66f));
                var bow = Stroke("HeldBow", new[] { new Vector2(0.25f, -0.32f), new Vector2(0.4f, -0.16f), new Vector2(0.44f, 0.0f), new Vector2(0.4f, 0.16f), new Vector2(0.25f, 0.29f) }, 0.045f); bow.startColor = bow.endColor = new Color(0.65f, 0.36f, 0.15f);
                var bowstring = Stroke("BowString", new[] { new Vector2(0.25f, -0.32f), new Vector2(0.25f, 0.29f) }, 0.016f); bowstring.startColor = bowstring.endColor = Color.white;
                EnsureProjectile();
            }
            if (s.Kind != SoldierKind.Hero) squadStar = Polygon("SquadStar", new Vector3(0, 0.28f, -0.04f + rider), new[] { new Vector2(0, 0.15f), new Vector2(0.04f, 0.04f), new Vector2(0.14f, 0), new Vector2(0.04f, -0.04f), new Vector2(0, -0.15f), new Vector2(-0.04f, -0.04f), new Vector2(-0.14f, 0), new Vector2(-0.04f, 0.04f) }, Color.yellow);
            if (s.Kind != SoldierKind.Hero)
            {
                var star = new[] { new Vector2(0, 0.15f), new Vector2(0.05f, 0.04f), new Vector2(0.16f, 0), new Vector2(0.05f, -0.04f), new Vector2(0, -0.15f), new Vector2(-0.05f, -0.04f), new Vector2(-0.16f, 0), new Vector2(-0.05f, 0.04f), new Vector2(0, 0.15f) };
                var rim = Stroke("SquadStarRim", star, 0.075f); starInk = Stroke("SquadStarInk", star, 0.04f);
                rim.transform.localPosition = new Vector3(0, 0.08f, rider - 0.04f); starInk.transform.localPosition = new Vector3(0, 0.09f, rider - 0.04f);
                rim.startColor = rim.endColor = Color.black;
            }
            float healthHeight = s.Kind == SoldierKind.Hero ? 0.94f : 0.68f + rider;
            var track = Shape("OverheadHealthTrack", new Vector3(0, 0.3f, healthHeight), 0.62f, 0.095f, false, new Color(0.015f, 0.02f, 0.03f));
            track.transform.SetParent(transform, false); healthRoot = track.transform;
            var fill = Shape("OverheadHealthFill", new Vector3(0, 0.31f, healthHeight), 0.56f, 0.053f, false, Color.white, true);
            fill.transform.SetParent(transform, false); healthFill = fill.transform;
            selectionArrow = Polygon("CommanderSelectionArrow", new Vector3(0, 0.4f, healthHeight + 0.27f), new[] { new Vector2(-0.12f, 0.08f), new Vector2(0.12f, 0.08f), new Vector2(0, -0.12f) }, Color.yellow);
            selectionArrow.transform.SetParent(transform, false); selectionArrow.enabled = false;
            statusEffects = new BattleStatusEffectsView(transform, material, s);
            foreach (var line in lines) if (line != arrow && !line.name.StartsWith("SquadStar")) damageLines[line] = line.startColor;
            if(s.Kind!=SoldierKind.Hero)
            {
                // 士兵贴图优先读 AppearanceId（遗迹临时战士等会显式给 villager），
                // 没有显式外观才按职业回退到通用图（saber→infantry、archer、rider→cavalry）。
                // 职业与单位 id 是两层：这里按「行为职业」落通用图，具体部队的专属图走 AppearanceId 覆盖（行为／表现解耦，2026-11-02 定）。
                string id=string.IsNullOrEmpty(s.AppearanceId)?(s.Kind==SoldierKind.Mage?"mage":s.Kind==SoldierKind.Ranged?"archer":s.Kind==SoldierKind.Cavalry?"rider":"saber"):s.AppearanceId;
                if(BaseConquest.Presentation.PixelDemoArt.Load("Characters",id)!=null)
                {
                    // Keep the existing squad badge outside the hidden geometric body.
                    squadStar.transform.SetParent(transform,false);squadStar.transform.localPosition=new Vector3(0,.4f,-.2f);
                    foreach(var line in lines)if(line.name.StartsWith("SquadStar")){line.transform.SetParent(transform,false);line.transform.localPosition=new Vector3(0,.41f,-.2f);}
                    EnableCompanion(id);
                }
            }
        }
        public void SyncExploration(Vector3 feet, Color color,Vector3? observer=null,double now=0,bool autonomous=false,BaseConquest.Presentation.HeroFacing? facing=null,float? benchProgress=null,bool standingUp=false)
        {
            properties.Clear();
            // Art meshes sit .28 units above their original XZ sheet; standing the sheet rotates that offset toward the camera.
            transform.position=feet+(UprightExploration ? Vector3.forward*UprightSheetOffset : Vector3.zero);
            BaseConquest.Presentation.MapSpriteOrder.Apply(transform,feet.z);
            playerHero?.Exploration(feet,facing,benchProgress,standingUp);
            companion?.Exploration(feet,observer,now,autonomous);
            foreach(var renderer in colored) { properties.SetColor("_BaseColor",color); renderer.SetPropertyBlock(properties); }
            if(crown!=null) { properties.SetColor("_BaseColor",new Color(1,.76f,.16f)); crown.SetPropertyBlock(properties); }
            healthRoot.gameObject.SetActive(false);healthFill.gameObject.SetActive(false);
        }
        private void SortAt(float worldZ)
        {
            BaseConquest.Presentation.MapSpriteOrder.Apply(transform,worldZ);
            int order=BaseConquest.Presentation.MapSpriteOrder.At(worldZ);
            healthRoot.GetComponent<Renderer>().sortingOrder=order+8;
            healthFill.GetComponent<Renderer>().sortingOrder=order+9;
            selectionArrow.sortingOrder=order+10;
        }
        public void SyncRealtime(BattleSimulation battle,PrototypeUnit actor,double seconds,Vector3 feet)
        {
            properties.Clear();
            // The lower half-cell is under the front hooves facing south and the hind hooves facing north.
            float hoofOffset = actor.FootprintLength == 2 && actor.BodyAxis.Z != 0 ? BattleBoardView.CellSize * .25f : 0;
            transform.position = feet - Vector3.forward * hoofOffset;
            float scale=soldier.Kind==SoldierKind.Hero?1:.85f;shape.localScale=new Vector3(soldier.Side==BattleSide.A?scale:-scale,scale,scale);
            shape.localRotation=soldier.IsDowned?Quaternion.Euler(0,90,0):Quaternion.identity;
            playerHero?.Realtime(actor,seconds);companion?.Realtime(actor,seconds,soldier.CombatKind==SoldierKind.Cavalry);
            if(actor.FlashUntil>seconds)foreach(var pair in damageShapes){properties.SetColor("_BaseColor",new Color(1,.12f,.12f));pair.Key.SetPropertyBlock(properties);}
            if(projectile!=null)projectile.gameObject.SetActive(false);
            SortAt(transform.position.z);
            SyncGroundOccupation(actor, actor.Side, actor.Alive, hoofOffset);
        }

        private void SyncGroundOccupation(PrototypeUnit footprint, PrototypeSide side, bool visible, float hoofOffset = 0)
        {
            if (!visible)
            {
                if (groundOccupation != null) groundOccupation.enabled = false;
                return;
            }
            bool cavalry = footprint.FootprintLength == 2;
            if (groundOccupation == null)
            {
                Color tint = side == PrototypeSide.Ally
                    ? new Color(.13f, .48f, 1, .45f) : new Color(1, .18f, .15f, .45f);
                groundOccupation = Shape("GroundOccupation", Vector3.zero, cavalry ? 1 : .5f, 1, !cavalry, tint);
                damageShapes.Remove(groundOccupation);
                groundOccupation.transform.SetParent(transform, false);
            }
            groundOccupation.transform.localPosition = new Vector3(0, .035f, hoofOffset);
            var half = footprint.BodyHalf;
            float width = (float)(2 * half.X * BattleBoardView.CellSize);
            float depth = (float)(2 * half.Z * BattleBoardView.CellSize);
            groundOccupation.transform.localScale = new Vector3(width, 1, depth);
            groundOccupation.sortingOrder = -27500;
            groundOccupation.enabled = true;
        }
        private void EnsureProjectile()
        {
            if(projectile!=null)return;
            projectile=new GameObject("DirectionalArrow").transform;projectile.SetParent(transform,false);
            arrow=Stroke("ArrowShaftAndHead",new[]{new Vector2(0,-.25f),new Vector2(0,.25f),new Vector2(-.075f,.12f),new Vector2(0,.25f),new Vector2(.075f,.12f)},.032f);
            arrow.transform.SetParent(projectile,false);arrow.startColor=arrow.endColor=new Color(1,.88f,.5f);projectile.gameObject.SetActive(false);
        }
        // 缺外观时头顶的品红「缺图」标记（复用 AuthoredMapView「实体占位」先例）：让缺图单位一眼区别于正常士兵。
        private void EnsureMissingArtFlag()
        {
            if(missingArtFlag!=null){missingArtFlag.gameObject.SetActive(true);return;}
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name="缺图占位";BattleBoardView.DestroyObject(go.GetComponent<Collider>());
            go.transform.SetParent(transform,false);go.transform.localPosition=new Vector3(0,.3f,1.55f);go.transform.localScale=new Vector3(.5f,.5f,.5f);
            var mat=new Material(material){color=new Color(1,.08f,.58f)};go.GetComponent<Renderer>().sharedMaterial=mat;
            missingArtFlag=go.GetComponent<Renderer>();
        }
        private Renderer Polygon(string name, Vector3 position, Vector2[] points, Color color)
        {
            var vertices = new Vector3[points.Length + 1]; var triangles = new int[points.Length * 3];
            for (int i = 0; i < points.Length; i++) { vertices[i + 1] = new Vector3(points[i].x, 0, points[i].y); triangles[i * 3] = 0; triangles[i * 3 + 1] = (i + 1) % points.Length + 1; triangles[i * 3 + 2] = i + 1; }
            return MeshShape(name, position, vertices, triangles, color);
        }
        private Renderer Shape(string name, Vector3 position, float width, float height, bool circle, Color color, bool tint = false)
        {
            Renderer r;
            if (circle)
            {
                var points = new Vector2[24]; for (int i = 0; i < points.Length; i++) { float a = i * Mathf.PI * 2 / points.Length; points[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * width; }
                r = Polygon(name, position, points, color);
            }
            else r = MeshShape(name, position, new[] { new Vector3(-width / 2, 0, -height / 2), new Vector3(width / 2, 0, -height / 2), new Vector3(width / 2, 0, height / 2), new Vector3(-width / 2, 0, height / 2) }, new[] { 0, 2, 1, 0, 3, 2 }, color);
            if (tint) colored.Add(r); return r;
        }
        private Renderer MeshShape(string name, Vector3 position, Vector3[] vertices, int[] triangles, Color color)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(shape, false); go.transform.localPosition = position;
            var mesh = new Mesh { name = name, vertices = vertices, triangles = triangles }; meshes.Add(mesh); mesh.RecalculateBounds(); mesh.RecalculateNormals(); go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<Renderer>(); r.sharedMaterial = material; properties.SetColor("_BaseColor", color); r.SetPropertyBlock(properties); if (!name.StartsWith("Overhead") && name != "CommanderCrown" && name != "SquadStar" && name != "CommanderSelectionArrow") damageShapes[r] = color; return r;
        }
        private LineRenderer Stroke(string name, Vector2[] points, float width)
        {
            var go = new GameObject(name); go.transform.SetParent(shape, false); var line = go.AddComponent<LineRenderer>(); line.sharedMaterial = material; line.useWorldSpace = false; line.positionCount = points.Length; line.widthMultiplier = width;
            for (int i = 0; i < points.Length; i++) line.SetPosition(i, new Vector3(points[i].x, 0.26f, points[i].y));
            line.startColor = line.endColor = Color.black; lines.Add(line); return line;
        }
        public void Sync(BattleSimulation battle, bool isSelected, float progress,bool animate=true,bool draft=false)
        {
            // Visibility reads the character atlas into this block; geometric bars must not inherit that texture.
            properties.Clear();
            bool main=(battle.Scenario.CampaignRules||battle.Scenario.PlayerHeroAppearance)&&BaseConquest.Presentation.PlayerHeroFrames.IsPlayer(soldier.Kind==SoldierKind.Hero,soldier.Side==BattleSide.A,soldier.OriginCommanderSlot,battle.Scenario.PlayerLeaderSlot);
            if(main)EnablePlayerHero(string.IsNullOrEmpty(soldier.AppearanceId)?"player":soldier.AppearanceId);
            else if(soldier.Kind==SoldierKind.Hero)
            {
                string appearance=soldier.AppearanceId;
                bool shown=EnableCompanion(string.IsNullOrEmpty(appearance)?"":appearance);
                if(!shown&&string.IsNullOrEmpty(appearance))
                {
                    Debug.LogWarning("[占位符] 队长缺外观，使用几何体暂代（非正式美术）："+soldier.Id+" —— 请补外观或核对 AppearanceId。");
                    EnsureMissingArtFlag();
                }
            }
            var carrier = battle.Soldier(soldier.CarriedById >= 0 ? soldier.CarriedById : soldier.EscapeCarrierId); bool carried = carrier != null && (carrier.IsActive || carrier.Escaped && carrier.LastAction == RoundAction.Move && progress < 1);
            // 倒地（可救援）保持倒地贴图不离场；即死单位在倒地后直接消失，不再留尸体。
            transform.gameObject.SetActive((soldier.IsAlive || soldier.Kind == SoldierKind.Hero) && !soldier.InstantDead && (!soldier.Escaped || (soldier.LastAction == RoundAction.Move || carried) && progress < 1) && (soldier.CarriedById < 0 || carried));
            transform.position = carried ? PresentedPosition(battle, carrier, progress) + new Vector3(0, 0.5f, 0.04f) : PresentedPosition(battle, soldier, progress);
            var draftFootprint = soldier.CombatKind == SoldierKind.Cavalry ? MountedDraft : FootDraft;
            if (draft)
            {
                var anchor = BaseBattlePreparation.Anchor(soldier.Cell) + draftFootprint.BodyCenter;
                transform.position = new Vector3((float)(anchor.X - battle.Board.Width * .5) * BattleBoardView.CellSize,
                    0, (float)(anchor.Z - battle.Board.Height * .5) * BattleBoardView.CellSize);
            }
            if (soldier.LastAction == RoundAction.Attack && soldier.CombatKind != SoldierKind.Ranged && progress < 1)
            {
                Vector3 direction = BattleBoardView.World(soldier.LastTargetCell, battle.Board) - BattleBoardView.World(soldier.Cell, battle.Board);
                transform.position += direction.normalized * (Mathf.Sin(progress * Mathf.PI) * 0.2f);
            }
            shape.localRotation = carried ? Quaternion.Euler(0, 90, 0) : soldier.IsDowned ? Quaternion.Euler(0, 90 + Mathf.Sin(Time.unscaledTime * 5) * 6, 0) : Quaternion.identity;
            float bump = soldier.IsActive && soldier.LastAction == RoundAction.Attack && progress < 1 ? BattleSoldier.AttackBob(progress) : 0;
            shape.localPosition = new Vector3(0, 0, bump);
            float scale = carried ? 0.55f : battle.UnitsAt(soldier.Cell).Count > 1 ? 0.72f : 1;
            float facing = soldier.Side == BattleSide.A ? 1 : -1;
            shape.localScale = new Vector3(facing * scale, scale, scale);
            Color color = soldier.Side == BattleSide.A ? new Color(0.32f, 0.73f, 0.92f) : new Color(0.94f, 0.52f, 0.33f);
            if (!soldier.IsAlive) color = Color.Lerp(color, Color.gray, 0.55f);
            // 暴击由战斗日志提示；受击形象统一闪红，与阵营、兵种无关。
            foreach (var r in colored) { properties.SetColor("_BaseColor", color); r.SetPropertyBlock(properties); }
            bool damaged = BattleSoldier.DamageFlashVisible(soldier.LastDamageTaken, progress);
            var red = new Color(1, 0.12f, 0.12f);
            foreach (var pair in damageShapes)
            {
                properties.SetColor("_BaseColor", damaged ? red : colored.Contains(pair.Key) ? color : pair.Value);
                pair.Key.SetPropertyBlock(properties);
            }
            foreach (var pair in damageLines) pair.Key.startColor = pair.Key.endColor = damaged ? red : pair.Value;
            float health = Mathf.Clamp01(soldier.Health / Mathf.Max(1, soldier.Stats.Health)); healthFill.localScale = new Vector3(health, 1, 1); healthFill.localPosition = new Vector3((health - 1) * 0.28f, healthFill.localPosition.y, healthFill.localPosition.z);
            bool healthVisible = soldier.IsActive || soldier.IsAlive && soldier.Escaped && soldier.LastAction == RoundAction.Move && progress < 1;
            healthRoot.gameObject.SetActive(healthVisible); healthFill.gameObject.SetActive(healthVisible);
            if (battle.Scenario.Leaderless(soldier.Side) && squadStar != null)
            {
                squadStar.enabled = false;
                foreach (var line in lines)
                    if (line.name.StartsWith("SquadStar")) line.enabled = false;
            }
            if (starInk != null) starInk.startColor = starInk.endColor = CrownColor(battle, soldier.Side, battle.CommanderFor(soldier)?.OriginCommanderSlot ?? soldier.OriginCommanderSlot);
            if (squadStar != null) { var commander = battle.CommanderFor(soldier); properties.SetColor("_BaseColor", CrownColor(battle, soldier.Side, commander?.OriginCommanderSlot ?? soldier.OriginCommanderSlot)); squadStar.SetPropertyBlock(properties); }
            if (crown != null) { properties.SetColor("_BaseColor", CrownColor(battle, soldier.Side, soldier.OriginCommanderSlot)); crown.SetPropertyBlock(properties); }
            if (projectile != null)
            {
                bool firing = soldier.LastAction == RoundAction.Attack && progress > 0.05f && progress < 0.95f;
                projectile.gameObject.SetActive(firing);
                if (firing)
                {
                    var from = BattleBoardView.World(soldier.PreviousCell, battle.Board); var to = BattleBoardView.World(soldier.LastTargetCell, battle.Board); var direction = to - from;
                    projectile.position = Vector3.Lerp(from, to, Mathf.Clamp01((progress - 0.05f) / 0.9f)) + Vector3.up * 0.38f;
                    projectile.rotation = Quaternion.Euler(0, Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg, 0);
                }
            }
            selectionArrow.enabled = isSelected && soldier.IsActive;
            properties.SetColor("_BaseColor", CrownColor(battle, soldier.Side, battle.CommanderFor(soldier)?.OriginCommanderSlot ?? soldier.OriginCommanderSlot)); selectionArrow.SetPropertyBlock(properties);
            statusEffects.Sync(battle, progress, scale);
            if(animate)
            {
                if(main&&playerHero!=null)playerHero.Battle(battle,soldier,progress,carried);
                if(!main&&companion!=null)companion.Battle(battle,soldier,progress,carried);
                SortAt(transform.position.z);
            }
            if (draft) SyncGroundOccupation(draftFootprint, PrototypeSide.Ally, soldier.IsActive);
        }
        public static Vector3 PresentedPosition(BattleSimulation battle, BattleSoldier unit, float progress)
        {
            var position = BattleSoldierView.Position(battle, unit);
            if (unit.LastAction != RoundAction.Move || unit.LastMovePath.Count <= 1) return Vector3.Lerp(BattleBoardView.World(unit.PreviousCell, battle.Board), position, Mathf.SmoothStep(0, 1, progress));
            float segment = Mathf.SmoothStep(0, 1, progress) * (unit.LastMovePath.Count - 1); int i = Mathf.Min(Mathf.FloorToInt(segment), unit.LastMovePath.Count - 2);
            return Vector3.Lerp(BattleBoardView.World(unit.LastMovePath[i], battle.Board), BattleBoardView.World(unit.LastMovePath[i + 1], battle.Board), segment - i) + position - BattleBoardView.World(unit.Cell, battle.Board);
        }
        public void SetVisibility(BattleSimulation battle, BattleSide observer, float progress = 1)
        {
            var carrier = battle.Soldier(soldier.CarriedById >= 0 ? soldier.CarriedById : soldier.EscapeCarrierId); bool carried = carrier != null && (carrier.IsActive || carrier.Escaped && carrier.LastAction == RoundAction.Move && progress < 1);
            bool reveal = battle.PlayerCanSee(observer, carried ? carrier : soldier);
            gameObject.SetActive((soldier.IsAlive || soldier.Kind == SoldierKind.Hero) && !soldier.InstantDead && (!soldier.Escaped || (soldier.LastAction == RoundAction.Move || carried) && progress < 1) && (soldier.CarriedById < 0 || carried) && reveal); float alpha = 1; // 半透明遮挡统一由位于地形／单位上方的雾层提供。
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (r is LineRenderer || r == groundOccupation) continue;
                r.GetPropertyBlock(properties); var c = properties.GetColor("_BaseColor"); if (c == default) c = Color.white; c.a = alpha; properties.SetColor("_BaseColor", c); r.SetPropertyBlock(properties);
            }
            foreach (var l in lines)
            {
                var c = l.startColor; c.a = alpha; l.startColor = l.endColor = c;
                properties.Clear();
                properties.SetColor("_BaseColor", c); properties.SetColor("_Color", c); l.SetPropertyBlock(properties);
            }
        }
        public static Color CrownColor(BattleSimulation battle, BattleSide side, int slot) => slot == (battle.CaptainSlot(side) >= 0 ? battle.CaptainSlot(side) : battle.Scenario.Army(side).CaptainSlot) ? new Color(1, 0.76f, 0.16f) : slot == 2 ? new Color(0.78f, 0.46f, 0.24f) : new Color(0.77f, 0.85f, 0.94f);
        private void OnDestroy() { foreach (var mesh in meshes) BattleBoardView.DestroyObject(mesh); BattleBoardView.DestroyObject(material); }
    }
}
