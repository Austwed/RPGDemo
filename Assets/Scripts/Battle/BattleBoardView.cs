using UnityEngine;
using System.Linq;
using System.Collections.Generic;

namespace BaseConquest.Battle
{
    public sealed class BattleBoardView
    {
        public const float CellSize = 1.2f;
        public readonly GameObject Root;
        public readonly Camera Camera;
        private readonly GameObject background;
        private Mesh gridMesh;
        private BattleElevationView elevationView;
        private BaseConquest.Presentation.BaseGroundEdgeView groundEdges;
        private BaseConquest.Presentation.FieldworkArtView fieldworkArt;
        private GameObject gridRoot;
        private bool[] gridKnown;
        public bool GridOutlinesVisible { get; private set; }
        public void SetGridOutlines(bool visible)
        {
            if(gridRoot==null)
            {
                gridRoot=new GameObject("MapGridOutlines",typeof(MeshFilter),typeof(MeshRenderer));gridRoot.transform.SetParent(Root.transform,false);
                gridMesh=new Mesh {name="KnownMapCellEdges"};gridRoot.GetComponent<MeshFilter>().sharedMesh=gridMesh;gridRoot.GetComponent<MeshRenderer>().sharedMaterial=groundMaterial;
                var ink=new MaterialPropertyBlock();ink.SetColor("_BaseColor",new Color(.07f,.11f,.10f));gridRoot.GetComponent<MeshRenderer>().SetPropertyBlock(ink);
            }
            GridOutlinesVisible=visible;gridRoot.SetActive(visible);if(visible)RebuildGridEdges();
        }
        private void RebuildGridEdges()
        {
            if(gridMesh==null||!GridOutlinesVisible)return;
            var vertices=new System.Collections.Generic.List<Vector3>();var indices=new System.Collections.Generic.List<int>();
            foreach(var p in board.Positions())
            {
                if(gridKnown!=null&&!gridKnown[p.Z*board.Width+p.X])continue;
                var center=World(p,board)+Vector3.up*(DisplayCell(p).Terrain==TerrainKind.Wall ? .9f : .035f);float r=CellSize*.5f;int start=vertices.Count;
                vertices.Add(center+new Vector3(-r,0,-r));vertices.Add(center+new Vector3(r,0,-r));vertices.Add(center+new Vector3(r,0,r));vertices.Add(center+new Vector3(-r,0,r));
                if(ObliqueExploration)
                    for(int corner=0;corner<4;corner++)
                    {var v=vertices[start+corner];v.y+=BaseConquest.Presentation.BaseTerrainView.Height(board,p,corner==1||corner==2 ? 1 : 0,corner>=2 ? 1 : 0);vertices[start+corner]=v;}
                for(int side=0;side<4;side++){indices.Add(start+side);indices.Add(start+(side+1)%4);}
            }
            gridMesh.Clear();gridMesh.SetVertices(vertices);gridMesh.SetIndices(indices.ToArray(),MeshTopology.Lines,0);
        }
        private readonly Renderer[] tiles;
        private readonly Mesh[] buildingMeshes;
        private readonly Mesh[] raisedMeshes;
        private Mesh cubeMesh;
        private readonly GameObject[] wallCaps;
        private readonly Renderer[] fogTiles;
        private readonly Color[] tileColors;
        private int visionRevision = -1;
        private BattleSide visionSide;
        private bool editingMap;
        private readonly Material waterMaterial, wallMaterial, swampMaterial, groundMaterial;
        private readonly Texture2D waterTexture, wallTexture, swampTexture;
        private readonly Texture2D buildingTexture;
        private readonly Material buildingMaterial;
        private readonly Material baseGrassMaterial,baseRaisedGrassMaterial,baseRaisedRoadMaterial,baseCliffMaterial,baseStairMaterial;
        private readonly BuildingAppearanceCatalog buildings;
        private readonly System.Collections.Generic.Dictionary<string,Material> buildingMaterials=new System.Collections.Generic.Dictionary<string,Material>();
        private readonly System.Collections.Generic.Dictionary<GroundSurface,Material> surfaceMaterials=new System.Collections.Generic.Dictionary<GroundSurface,Material>();
        private readonly System.Collections.Generic.Dictionary<string,Material> dungeonMaterials=new System.Collections.Generic.Dictionary<string,Material>();
        private string surfaceRegion="battle";
        private bool homeTerrainTrial;
        private bool campScenery;
        private readonly Dictionary<Material,(Texture Texture,Vector2 Scale,Vector2 Offset)> originalTrialMaterials=new Dictionary<Material,(Texture,Vector2,Vector2)>();
        public bool SetHomeTerrainTrial(bool enabled)
        {
            if(enabled&&(BaseConquest.Presentation.HomeTerrainTrial.Texture(GroundSurface.Grass)==null||BaseConquest.Presentation.HomeTerrainTrial.Texture(GroundSurface.Road)==null||BaseConquest.Presentation.HomeTerrainTrial.Stone()==null||BaseConquest.Presentation.HomeTerrainTrial.Cliff()==null||BaseConquest.Presentation.HomeTerrainTrial.Stair()==null))return false;
            if(homeTerrainTrial==enabled)return true;
            homeTerrainTrial=enabled;
            void Apply(Material skin,GroundSurface surface)
            {
                if(skin==null)return;
                if(enabled)
                {
                    if(!originalTrialMaterials.ContainsKey(skin))originalTrialMaterials[skin]=(skin.GetTexture("_BaseMap"),skin.GetTextureScale("_BaseMap"),skin.GetTextureOffset("_BaseMap"));
                    var texture=BaseConquest.Presentation.HomeTerrainTrial.Texture(surface);if(texture==null)return;
                    skin.SetTexture("_BaseMap",texture);skin.SetTextureScale("_BaseMap",Vector2.one);skin.SetTextureOffset("_BaseMap",Vector2.zero);
                }
                else if(originalTrialMaterials.TryGetValue(skin,out var original))
                {skin.SetTexture("_BaseMap",original.Texture);skin.SetTextureScale("_BaseMap",original.Scale);skin.SetTextureOffset("_BaseMap",original.Offset);}
                else skin.SetTexture("_BaseMap",BaseConquest.Presentation.MapSurfaceView.Texture(surface));
            }
            void ApplyCliff(Material skin)
            {
                if(skin==null)return;var cliff=BaseConquest.Presentation.HomeTerrainTrial.Cliff();if(enabled&&(cliff==null))return;
                if(enabled)
                {
                    if(!originalTrialMaterials.ContainsKey(skin))originalTrialMaterials[skin]=(skin.GetTexture("_BaseMap"),skin.GetTextureScale("_BaseMap"),skin.GetTextureOffset("_BaseMap"));
                    skin.SetTexture("_BaseMap",cliff);skin.SetTextureScale("_BaseMap",Vector2.one);skin.SetTextureOffset("_BaseMap",Vector2.zero);
                }
                else if(originalTrialMaterials.TryGetValue(skin,out var original))
                {skin.SetTexture("_BaseMap",original.Texture);skin.SetTextureScale("_BaseMap",original.Scale);skin.SetTextureOffset("_BaseMap",original.Offset);}
            }
            void ApplyStair(Material skin)
            {
                if(skin==null)return;var stair=BaseConquest.Presentation.HomeTerrainTrial.Stair();if(enabled&&(stair==null))return;
                if(enabled)
                {
                    if(!originalTrialMaterials.ContainsKey(skin))originalTrialMaterials[skin]=(skin.GetTexture("_BaseMap"),skin.GetTextureScale("_BaseMap"),skin.GetTextureOffset("_BaseMap"));
                    skin.SetTexture("_BaseMap",stair);skin.SetTextureScale("_BaseMap",Vector2.one);skin.SetTextureOffset("_BaseMap",Vector2.zero);
                }
                else if(originalTrialMaterials.TryGetValue(skin,out var original))
                {skin.SetTexture("_BaseMap",original.Texture);skin.SetTextureScale("_BaseMap",original.Scale);skin.SetTextureOffset("_BaseMap",original.Offset);}
            }
            Apply(baseGrassMaterial,GroundSurface.Grass);Apply(baseRaisedGrassMaterial,GroundSurface.Grass);Apply(baseRaisedRoadMaterial,GroundSurface.Road);ApplyCliff(baseCliffMaterial);ApplyStair(baseStairMaterial);
            foreach(var entry in surfaceMaterials)if(entry.Key==GroundSurface.Grass||entry.Key==GroundSurface.Road||entry.Key==GroundSurface.DirtPath||entry.Key==GroundSurface.Cobble)Apply(entry.Value,entry.Key);
            if(!enabled)originalTrialMaterials.Clear();
            Refresh();return true;
        }
        public void SetSurfaceRegion(string region){surfaceRegion=region;Refresh();}
        public Material WaterMaterial => waterMaterial;
        public Material WallMaterial => wallMaterial;
        private readonly BattleBoard board;
        private BattleBoard draftDeploymentZones;
        private bool showDraftDeploymentZones;
        internal void SetDraftDeploymentZones(BattleBoard draft, bool show = false)
        {
            if (draftDeploymentZones == draft && showDraftDeploymentZones == show) return;
            draftDeploymentZones = draft;
            showDraftDeploymentZones = show;
            foreach (Transform child in Root.transform)
                if (child.name.StartsWith("ExitMarker_")) child.gameObject.SetActive(draft == null && CombatMode);
            Refresh();
        }
        private readonly Dictionary<int,BoardCell> appearanceCells=new Dictionary<int,BoardCell>();
        public BoardCell DisplayCell(GridPosition point)=>appearanceCells.TryGetValue(point.Z*board.Width+point.X,out var cell)?cell:board.Cell(point);
        private GroundSurface DisplaySurface(GridPosition cell, BoardCell data) => GroundSurfaceRules.Resolve(data.Surface, surfaceRegion, cell.X, cell.Z);
        internal GroundSurface FootstepSurfaceAt(double x, double z)
        {
            var cell = new GridPosition((int)System.Math.Floor(x), (int)System.Math.Floor(z));
            var surface = DisplaySurface(cell, DisplayCell(cell));
            // The current trail texture paints stone pavers on cells authored as DirtPath.
            return surface == GroundSurface.Road || homeTerrainTrial && surface == GroundSurface.DirtPath ||
                ObliqueExploration && board.RampLowEdge(cell) != 0 ? GroundSurface.Cobble : surface;
        }
        public void SetCombatAppearance(BattleBoard original)
        {
            appearanceCells.Clear();
            if(original!=null&&original.Width==board.Width&&original.Height==board.Height)
                foreach(var p in board.Positions())if(original.Cell(p).Terrain!=board.Cell(p).Terrain)appearanceCells[p.Z*board.Width+p.X]=original.Cell(p).Clone();
            Refresh();
        }
        public BattleBoard Board => board;
        public bool CombatMode { get; private set; } = true;
        private bool showDeploymentZones = true;
        public void SetCombatMode(bool combat,bool preserveView=false,bool deploymentZones=true)
        {
            if(combat&&!CombatMode&&!preserveView){zoomFactor=1;panOffset=Vector3.zero;}
            if (!combat) { draftDeploymentZones = null; showDraftDeploymentZones = false; }
            fieldworkArt?.SetKnown(null);
            CombatMode=combat; showDeploymentZones=deploymentZones; SelectCell(null); Refresh();
            foreach(var edge in Root.GetComponentsInChildren<BaseConquest.Presentation.EnvironmentRoadEdges>(true)){edge.SetRaised(ObliqueExploration);edge.SetKnown(null);}
            foreach(var facade in Root.GetComponentsInChildren<BaseConquest.Presentation.SpriteFacade>(true))facade.SetRaised(ObliqueExploration);
            foreach(var fog in fogTiles) fog.gameObject.SetActive(false);
            foreach(var tile in tiles) tile.enabled=true;
            gridKnown=null;SetGridOutlines(combat ? MapDisplayOptions.BattleGridOutlines : MapDisplayOptions.ShowGridOutlines);
            elevationView?.Rebuild();
            foreach(Transform child in Root.transform) if(child.name.StartsWith("ExitMarker_")) child.gameObject.SetActive(combat);
        }
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private readonly LineRenderer outline;
        private readonly LineRenderer cellSelection;
        public GridPosition? SelectedCell { get; private set; }
        private float zoomFactor = 1;
        private Vector3 panOffset;
        public bool FlatView;
        public bool ObliqueBaseView;
        public bool ObliqueExploration => ObliqueBaseView && !CombatMode;
        public static readonly Quaternion BaseArtTilt = Quaternion.Euler(-30, 0, 0);
        public static readonly Quaternion BasePawnTilt = Quaternion.Euler(-90, 0, 0);
        private readonly System.Collections.Generic.Dictionary<int, GameObject> buildingGround = new System.Collections.Generic.Dictionary<int, GameObject>();
        private readonly Material translucentFog;
        private bool visionReveal;

        public BattleBoardView(BattleBoard board, Transform parent, Material material, Material lineMaterial)
        {
            this.board = board;
            translucentFog = BattlePawn2D.Transparent(material);
            groundMaterial = material; swampTexture = MakeSwampTexture(); swampMaterial = MakeMaterial(material, swampTexture, "SwampMudAndReeds"); waterTexture = MakeTexture(true); wallTexture = MakeTexture(false);
            waterMaterial = MakeMaterial(material, waterTexture, "WaterWaves"); wallMaterial = MakeMaterial(material, wallTexture, "StoneWallBricks");
            var cainosBrick=BaseConquest.Presentation.HomeTerrainTrial.Cliff();if(cainosBrick!=null)wallMaterial.SetTexture("_BaseMap",cainosBrick);// 全高墙统一 Cainos 砖墙
            var pixelWater=BaseConquest.Presentation.PixelDemoArt.Load("Terrain","shallow-water");if(pixelWater!=null)waterMaterial.SetTexture("_BaseMap",pixelWater);
            var pixelSwamp=BaseConquest.Presentation.PixelDemoArt.Load("Terrain","swamp");if(pixelSwamp!=null)swampMaterial.SetTexture("_BaseMap",pixelSwamp);
            buildings=Resources.Load<BuildingAppearanceCatalog>("BuildingAppearances");
            buildingTexture=new Texture2D(32,32) {name="BuildingRoof",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Repeat};
            for(int y=0;y<32;y++)for(int x=0;x<32;x++)buildingTexture.SetPixel(x,y,y%8==0||x%8==((y/8)%2)*4 ? new Color(.29f,.18f,.14f) : new Color(.65f,.4f,.28f));buildingTexture.Apply();
            buildingMaterial=BattlePawn2D.Transparent(material);buildingMaterial.SetTexture("_BaseMap",buildingTexture);buildingMaterial.name="BuildingRoofMaterial";
            var terrace=Resources.Load<Texture2D>("MapSurfaces/BaseTerrace-v1");
            baseGrassMaterial=BaseConquest.Presentation.BaseTerrainView.AtlasMaterial(material,terrace,new Vector2(0,.5f),"BaseTerraceGrass");
            baseRaisedGrassMaterial=BaseConquest.Presentation.BaseTerrainView.AtlasMaterial(material,terrace,new Vector2(0,.5f),"BaseTerraceRaisedGrass");
            baseRaisedRoadMaterial=BaseConquest.Presentation.BaseTerrainView.AtlasMaterial(material,terrace,new Vector2(.5f,0),"BaseTerraceRaisedRoad");
            baseCliffMaterial=BaseConquest.Presentation.BaseTerrainView.AtlasMaterial(material,terrace,new Vector2(.5f,.5f),"BaseTerraceCliff");
            baseStairMaterial=BaseConquest.Presentation.BaseTerrainView.AtlasMaterial(material,terrace,Vector2.zero,"BaseTerraceSteps");
            void PixelGround(Material target,string id)
            {
                var texture=BaseConquest.Presentation.PixelDemoArt.Load("Terrain",id);if(texture==null||target==null)return;
                target.SetTexture("_BaseMap",texture);target.SetTextureScale("_BaseMap",Vector2.one);target.SetTextureOffset("_BaseMap",Vector2.zero);
            }
            PixelGround(baseGrassMaterial,"grass");PixelGround(baseRaisedGrassMaterial,"grass");PixelGround(baseRaisedRoadMaterial,"dirt-road");
            Root = new GameObject("EditableChessboard"); Root.transform.SetParent(parent, false);
            tiles = new Renderer[board.Width * board.Height];
            buildingMeshes = new Mesh[tiles.Length];
            raisedMeshes = new Mesh[tiles.Length];
            wallCaps = new GameObject[tiles.Length];
            fogTiles = new Renderer[tiles.Length]; tileColors = new Color[tiles.Length];
            foreach (GridPosition p in board.Positions())
            {
                GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tile.name = "Cell_" + p.X + "_" + p.Z; tile.transform.SetParent(Root.transform, false);
                DestroyObject(tile.GetComponent<Collider>());
                Renderer renderer = tile.GetComponent<Renderer>(); renderer.sharedMaterial = material; tiles[p.Z * board.Width + p.X] = renderer;
                cubeMesh = tile.GetComponent<MeshFilter>().sharedMesh;
                GameObject fog = GameObject.CreatePrimitive(PrimitiveType.Cube); fog.name = "Unexplored_" + p.X + "_" + p.Z; fog.transform.SetParent(Root.transform, false); DestroyObject(fog.GetComponent<Collider>());
                fog.transform.position = World(p, board) + Vector3.down * 0.1f; fog.transform.localScale = new Vector3(CellSize - 0.04f, 0.18f, CellSize - 0.04f);
                fogTiles[p.Z * board.Width + p.X] = fog.GetComponent<Renderer>(); fogTiles[p.Z * board.Width + p.X].sharedMaterial = material;fogTiles[p.Z * board.Width + p.X].sortingOrder=32700; fog.SetActive(false);
            }
            var cameraObject = new GameObject("BoardCamera"); cameraObject.transform.SetParent(Root.transform, false);
            // 棋盘相机保留UI边栏，先清全屏，避免边栏保留上一帧地图／IMGUI像素。
            var backdrop = new GameObject("BattleBackgroundCamera"); backdrop.transform.SetParent(Root.transform, false);
            background=backdrop;
            var backgroundCamera = backdrop.AddComponent<Camera>(); backgroundCamera.depth = -10; backgroundCamera.cullingMask = 0;
            backgroundCamera.clearFlags = CameraClearFlags.SolidColor; backgroundCamera.backgroundColor = new Color(0.045f, 0.065f, 0.095f);
            Camera = cameraObject.AddComponent<Camera>(); Camera.orthographic = true;
            Camera.clearFlags = CameraClearFlags.SolidColor; Camera.backgroundColor = new Color(0.045f, 0.065f, 0.095f);
            Camera.nearClipPlane = 0.1f; Camera.farClipPlane = 100;
            cameraObject.transform.position = new Vector3(0, 30, -12); cameraObject.transform.LookAt(Vector3.zero);
            var cursor = new GameObject("BrushRectangle"); cursor.transform.SetParent(Root.transform, false);
            outline = cursor.AddComponent<LineRenderer>(); outline.sharedMaterial = lineMaterial;
            outline.widthMultiplier = 0.065f; outline.loop = true; outline.positionCount = 4;
            outline.startColor = outline.endColor = new Color(0.97f, 0.95f, 0.45f); outline.enabled = false;
            var selectedCell = new GameObject("ClickedCellSelection"); selectedCell.transform.SetParent(Root.transform, false);
            cellSelection = selectedCell.AddComponent<LineRenderer>(); cellSelection.sharedMaterial = lineMaterial;
            cellSelection.widthMultiplier = 0.045f; cellSelection.loop = true; cellSelection.positionCount = 4;
            cellSelection.startColor = cellSelection.endColor = new Color(1, 0.89f, 0.28f); cellSelection.enabled = false;
            Refresh();
        }
        public static Vector3 World(GridPosition p, BattleBoard board) => new Vector3((p.X - (board.Width - 1) * 0.5f) * CellSize, 0, (p.Z - (board.Height - 1) * 0.5f) * CellSize);
        internal void EnableCampScenery()
        {
            BaseConquest.Presentation.CampScenery.Create(Root.transform, board, Camera, baseGrassMaterial, groundMaterial);
            campScenery = true;
        }
        public void UpdateCamera(float leftFraction, float bottomFraction)
        {
            bool showGrid=CombatMode ? MapDisplayOptions.BattleGridOutlines : MapDisplayOptions.ShowGridOutlines;
            if(GridOutlinesVisible!=showGrid)SetGridOutlines(showGrid);
            background.SetActive(true);
            Camera.rect = FlatView ? new Rect(0, 0, 1, 1) : new Rect(leftFraction, bottomFraction, Mathf.Max(0.35f, 1 - leftFraction), 1 - bottomFraction);
            if(FlatView && !CombatMode)
            {
                // The default scene fills the game area; translucent HUD panels overlay the map.
                float left=Mathf.Clamp(leftFraction,0,.35f);Camera.rect=new Rect(left,0,1-left,1);
            }
            Camera.rect=BaseConquest.Presentation.GameFrameCanvas.Compose(Camera.rect);
            Camera.orthographicSize = FullViewSize * zoomFactor;
            if (campScenery) panOffset = ClampCampPan(panOffset);
            Camera.transform.position = new Vector3(0, 30, FlatView ? 0 : -12) + panOffset;
            if (FlatView) Camera.transform.rotation = Quaternion.Euler(90, 0, 0);
            if (ObliqueExploration)
            {
                Camera.transform.position = new Vector3(0, 30, -30 / Mathf.Sqrt(3)) + panOffset;
                Camera.transform.rotation = Quaternion.Euler(60, 0, 0);
            }
        }
        // 草地随机变体（2026-11-01 韦德：地面太复制粘贴）：0=基准不换材质；1=第二草块 2=稀疏草 3/4=带白花（花格占比约 6%）。
        // 变体贴图均为 Cainos 草地图集的无缝裁块（花是原版白花像素叠加到无缝基块上）。
        private readonly Dictionary<Material,Material[]> grassVariantCache=new Dictionary<Material,Material[]>();
        private static Texture2D GrassVariantTexture(int i)=>Resources.Load<Texture2D>("HomeTerrainTrial/"+(i==1?"grass-64-v2-b":i==2?"grass-64-v2-sparse":i==3?"grass-64-v2-flower-a":"grass-64-v2-flower-b"));
        Material GrassVariant(Material baseMat,int x,int z)
        {
            if(baseMat==null)return null;
            int h;unchecked{h=(x*73856093)^(z*19349663);h=(int)((h*(long)0x45d9f3b)&0x7fffffff);h^=h>>16;}
            int v=(h&0x7fffffff)%100,index=v<4?3:v<6?4:v<16?2:v<40?1:0;
            if(index==0)return baseMat;
            if(!grassVariantCache.TryGetValue(baseMat,out var set))
            {
                set=new Material[5];
                for(int i=1;i<set.Length;i++){var tex=GrassVariantTexture(i);set[i]=tex==null?baseMat:MakeMaterial(baseMat,tex,"GrassVariant"+i);}
                grassVariantCache[baseMat]=set;
            }
            return set[index]??baseMat;
        }
        // 缩放边界（2026-11-01 韦德定，第二版）：探索／作战／即时战共用同一套最大最小缩放。
        // 基准取城镇探索视图——zoomFactor=1 即棋盘刚好完整入画，最小缩到 0.3 倍。
        // 原先作战态额外把下限抬到 .44（比探索更近不了），这条特例已取消。
        public const float MinZoomFactor=.3f;
        public void Zoom(float ratio,float minimum=MinZoomFactor) { zoomFactor = Mathf.Clamp(zoomFactor * ratio, minimum, 1); }
        // zoomFactor=1 的默认视图即棋盘刚好完整入画；探索视图的绝对尺寸边界由此导出。
        public float FullViewSize => Mathf.Max(
            board.Height * CellSize * .5f + (campScenery ? .9f : FlatView ? .15f : 2f),
            (board.Width * CellSize * .5f + (campScenery ? 3.2f : FlatView ? .15f : 1.5f)) / Camera.aspect);
        public float MinViewSize=>FullViewSize*MinZoomFactor;
        // 统一入口：无论探索还是作战，都夹在同一组绝对边界里，并把 zoomFactor 同步成相对比例。
        public void SetViewSize(float size)
        {
            size=Mathf.Clamp(size,MinViewSize,FullViewSize);
            zoomFactor=Mathf.Clamp(size/FullViewSize,MinZoomFactor,1f);
            Camera.orthographicSize=size;
            if (campScenery) ApplyPanOffset(panOffset);
        }
        public void Follow(Vector3 feet)=>ApplyPanOffset(feet);
        public void ZoomAt(float ratio,Vector2 screen)
        {
            var ground=new Plane(Vector3.up,Vector3.zero);var before=Camera.ScreenPointToRay(screen);
            if(!ground.Raycast(before,out float distance))return;
            Vector3 anchor=before.GetPoint(distance);float previous=zoomFactor;Zoom(ratio);
            Camera.orthographicSize*=zoomFactor/previous;
            var after=Camera.ScreenPointToRay(screen);
            if(!ground.Raycast(after,out distance))return;
            var shift=anchor-after.GetPoint(distance);
            // Keep the ground under the pointer stationary while respecting the existing pan limits.
            ApplyPanOffset(panOffset+new Vector3(shift.x,0,shift.z));
        }
        public void Pan(Vector2 delta)
        {
            Vector3 right = Camera.transform.right; Vector3 up = Camera.transform.up; right.y = up.y = 0;
            ApplyPanOffset(panOffset-(right.normalized * delta.x + up.normalized * delta.y) * (Camera.orthographicSize * 2 / Mathf.Max(1, Camera.pixelHeight)));
        }
        private void ApplyPanOffset(Vector3 offset)
        {
            Vector3 previous = panOffset;
            panOffset = campScenery ? ClampCampPan(offset) : new Vector3(
                Mathf.Clamp(offset.x, -board.Width * CellSize * .6f, board.Width * CellSize * .6f), offset.y,
                Mathf.Clamp(offset.z, -board.Height * CellSize * .6f, board.Height * CellSize * .6f));
            Camera.transform.position+=panOffset-previous;
        }
        private Vector3 ClampCampPan(Vector3 offset) => BaseConquest.Presentation.CampScenery.ClampPan(
            offset, board, Camera.orthographicSize, Camera.aspect, ObliqueExploration, zoomFactor);
        public bool Pick(Vector2 screen, out GridPosition cell)
        {
            cell = default;
            if (!Camera.pixelRect.Contains(screen)) return false;
            Ray ray = Camera.ScreenPointToRay(screen);
            float nearest = float.MaxValue; bool found = false;
            foreach (GridPosition p in board.Positions())
            {
                float distance;
                bool hit;
                var tile = tiles[p.Z * board.Width + p.X].transform;
                if (ObliqueExploration)
                {
                    distance=float.MaxValue;
                    hit=tiles[p.Z*board.Width+p.X].bounds.IntersectRay(ray,out _) && BaseConquest.Presentation.BaseTerrainView.Raycast(tile.GetComponent<MeshFilter>(),ray,out distance);
                    if(buildingGround.TryGetValue(p.Z*board.Width+p.X,out var floor)&&floor.activeSelf&&floor.GetComponent<Renderer>().bounds.IntersectRay(ray,out _))
                    {
                        if(BaseConquest.Presentation.BaseTerrainView.Raycast(floor.GetComponent<MeshFilter>(),ray,out var floorDistance)&&floorDistance<distance){distance=floorDistance;hit=true;}
                    }
                }
                else hit = TileBounds(p).IntersectRay(ray, out distance);
                if (hit && distance < nearest)
                { nearest = distance; cell = p; found = true; }
            }
            return found;
        }
        private readonly HashSet<string> authoredBuildings=new HashSet<string>();
        public void SetAuthoredBuildings(IEnumerable<string> ids)
        {authoredBuildings.Clear();foreach(var id in ids)authoredBuildings.Add(id);Refresh();}
        public void Refresh()
        {
            exitMarkers=false;
            visionRevision = -1;
            Material DungeonSkin(string id)
            {
                if(!dungeonMaterials.TryGetValue(id,out var skin)){var texture=BaseConquest.Presentation.PixelDemoArt.Load("Terrain",id);if(texture!=null)dungeonMaterials[id]=skin=MakeMaterial(groundMaterial,texture,id);}
                return skin;
            }
            var footprints=BuildingFootprint.Read(board);
            foreach (GridPosition p in board.Positions())
            {
                BoardCell cell = DisplayCell(p); int index = p.Z * board.Width + p.X; Renderer tile = tiles[index];
                if(cell.Terrain==TerrainKind.Building&&authoredBuildings.Contains(cell.BuildingId)){cell=cell.Clone();cell.Terrain=TerrainKind.Ground;}
                Vector3 position = World(p, board); float height = 0.18f;
                Color color = (p.X + p.Z) % 2 == 0 ? new Color(0.19f, 0.24f, 0.3f) : new Color(0.23f, 0.29f, 0.35f);
                color = new Color(.34f,.43f,.30f);
                var zone = draftDeploymentZones == null ? showDeploymentZones ? cell.Zone : DeploymentZone.None : showDraftDeploymentZones && draftDeploymentZones.Cell(p)?.Zone == DeploymentZone.A
                    ? DeploymentZone.A : DeploymentZone.None;
                bool draftAlly = draftDeploymentZones != null && zone == DeploymentZone.A && draftDeploymentZones.Walkable(p);
                if (CombatMode && zone == DeploymentZone.A && !draftAlly) color = new Color(.17f,.4f,.59f);
                if (CombatMode && zone == DeploymentZone.B) color = new Color(.58f,.32f,.17f);
                position.y = -0.1f;
                tile.sharedMaterials = new[] {groundMaterial};
                tile.sortingOrder=0;
                if(cell.Terrain==TerrainKind.Ground)
                {
                    var surface=DisplaySurface(p,cell);
                    if(surface!=GroundSurface.Default)
                    {
                        if(!surfaceMaterials.TryGetValue(surface,out var skin)){var texture=(homeTerrainTrial?BaseConquest.Presentation.HomeTerrainTrial.Texture(surface):null)??BaseConquest.Presentation.MapSurfaceView.Texture(surface);if(texture!=null)surfaceMaterials[surface]=skin=MakeMaterial(groundMaterial,texture,"Ground_"+surface);}
                        if(skin!=null){var groundSkin=ObliqueExploration&&surface==GroundSurface.Grass&&baseGrassMaterial!=null ? baseGrassMaterial : skin;tile.sharedMaterial=surface==GroundSurface.Grass?GrassVariant(groundSkin,p.X,p.Z):groundSkin;if(!CombatMode||zone==DeploymentZone.None)color=Color.white;}
                        if(homeTerrainTrial&&(!CombatMode||zone==DeploymentZone.None)&&(surface==GroundSurface.Grass||surface==GroundSurface.Road||surface==GroundSurface.DirtPath))color*=Mathf.Lerp(.94f,1.02f,Mathf.PerlinNoise(p.X*.12f,p.Z*.12f));
                    }
                }
                tile.GetComponent<MeshFilter>().sharedMesh=cubeMesh;
                properties.Clear();
                if (cell.Terrain == TerrainKind.Water) { height = 0.16f; position.y = -0.16f; color = Color.white; tile.sharedMaterial = waterMaterial; }
                if (cell.Terrain == TerrainKind.Swamp) { color = Color.white; tile.sharedMaterial = swampMaterial; }
                if (cell.Terrain == TerrainKind.Wall)
                {
                    height = 0.86f; position.y = 0.43f; color = Color.white; tile.sharedMaterial = wallMaterial;
                    if (wallCaps[index] == null) wallCaps[index] = MakeWallCaps(p);
                }
                if(cell.Terrain==TerrainKind.Building)
                {
                    height=.36f;position.y=.18f;color=Color.white;string id=cell.BuildingId??"";var style=buildings?.Find(id);
                    tile.sortingOrder=BaseConquest.Presentation.MapSpriteOrder.At((p.Z-board.Height*.5f)*CellSize);
                    if(style?.Texture!=null)
                    {
                        if(!buildingMaterials.TryGetValue(id,out var skin)){skin=BattlePawn2D.Transparent(groundMaterial);skin.name="Building_"+id;skin.SetTexture("_BaseMap",style.Texture);buildingMaterials[id]=skin;}tile.sharedMaterial=skin;color=style.Tint;
                        // 显式指定屋面的西南→东北UV，避免Unity默认立方体每个面的UV方向不同。
                        var mesh=buildingMeshes[index];
                        if(mesh==null)buildingMeshes[index]=mesh=MakeBuildingMesh();
                        var area=footprints[id];
                        float frontZ=(area.MinZ-board.Height*.5f)*CellSize;
                        tile.sortingOrder=BaseConquest.Presentation.MapSpriteOrder.At(frontZ);
                        var sw=new Vector2(area.U(p.X),area.V(p.Z));var nw=new Vector2(area.U(p.X),area.V(p.Z+1));
                        var ne=new Vector2(area.U(p.X+1),area.V(p.Z+1));var se=new Vector2(area.U(p.X+1),area.V(p.Z));
                        mesh.uv=new[] {sw,nw,ne,se,sw,nw,ne,se};tile.GetComponent<MeshFilter>().sharedMesh=mesh;
                    }
                    else {tile.sharedMaterial=buildingMaterial;color=style?.Tint??Color.white;}
                }
                if(cell.FixedCover){height=.30f;position.y=.05f;color=new Color(.56f,.55f,.42f);}
                if(cell.ObstacleHealth>0){height=.68f;position.y=.24f;tile.sharedMaterial=groundMaterial;color=new Color(.48f,.29f,.13f);}
                if(cell.Walkable&&cell.Elevation>0)color=Color.Lerp(color,new Color(.60f,.63f,.42f),cell.Elevation*.12f);
                if (wallCaps[index] != null) wallCaps[index].SetActive(cell.Terrain == TerrainKind.Wall);
                if(wallCaps[index]!=null)wallCaps[index].transform.position=World(p,board)+Vector3.up*(1+(ObliqueExploration ? cell.Elevation*BaseConquest.Presentation.BaseTerrainView.LevelHeight : 0));
                tile.transform.position = position; tile.transform.localScale = new Vector3(CellSize, height, CellSize);
                tile.transform.localRotation = Quaternion.identity;
                if(ObliqueExploration)tile.transform.position+=Vector3.up*cell.Elevation*BaseConquest.Presentation.BaseTerrainView.LevelHeight;
                if(raisedMeshes[index]!=null){DestroyObject(raisedMeshes[index]);raisedMeshes[index]=null;}
                bool liftArt = ObliqueExploration && cell.Terrain == TerrainKind.Building && buildings?.Find(cell.BuildingId ?? "")?.Texture != null;
                if (liftArt)
                {
                    // Rotate all texture fragments around the same front edge so the whole building stays joined.
                    var area = footprints[cell.BuildingId];
                    float frontZ = (area.MinZ - board.Height * .5f) * CellSize;
                    float depth = position.z - frontZ;
                    tile.transform.position = new Vector3(position.x, cell.Elevation*BaseConquest.Presentation.BaseTerrainView.LevelHeight + .02f + depth * .5f, frontZ + depth * Mathf.Sqrt(3) * .5f);
                    tile.transform.localRotation = BaseArtTilt;
                    tile.transform.localScale = new Vector3(CellSize, .02f, CellSize);
                    if (!buildingGround.TryGetValue(index, out var floor))
                    {
                        floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "BuildingGround_" + index;
                        floor.transform.SetParent(Root.transform, false); DestroyObject(floor.GetComponent<Collider>());
                        floor.GetComponent<Renderer>().sharedMaterial = baseGrassMaterial??groundMaterial;
                        var ink = new MaterialPropertyBlock(); ink.SetColor("_BaseColor", baseGrassMaterial!=null ? Color.white : new Color(.34f, .43f, .30f)); floor.GetComponent<Renderer>().SetPropertyBlock(ink);
                        buildingGround[index] = floor;
                    }
                    raisedMeshes[index]=BaseConquest.Presentation.BaseTerrainView.Ground(board,p);
                    floor.GetComponent<MeshFilter>().sharedMesh=raisedMeshes[index];
                    floor.GetComponent<Renderer>().sharedMaterials=new[]{baseRaisedGrassMaterial??groundMaterial,baseCliffMaterial??wallMaterial};
                    floor.GetComponent<Renderer>().sortingOrder=BaseConquest.Presentation.BaseTerrainView.GroundOrder(frontZ);
                    floor.transform.position = World(p, board);
                    floor.transform.localScale = Vector3.one;
                }
                if (buildingGround.TryGetValue(index, out var ground)) ground.SetActive(liftArt);
                if(ObliqueExploration&&cell.Terrain==TerrainKind.Ground&&(cell.Elevation>0||cell.Ramp!=RampAxis.None))
                {
                    var surface=DisplaySurface(p,cell);
                    bool steps=board.RampLowEdge(p)!=0;
                    var topMaterial=steps&&homeTerrainTrial&&baseStairMaterial!=null ? baseStairMaterial : (steps||surface==GroundSurface.Road)&&baseRaisedRoadMaterial!=null ? baseRaisedRoadMaterial : (surface==GroundSurface.Grass||surface==GroundSurface.Default)&&baseRaisedGrassMaterial!=null ? GrassVariant(baseRaisedGrassMaterial,p.X,p.Z) : tile.sharedMaterial;
                    raisedMeshes[index]=BaseConquest.Presentation.BaseTerrainView.Ground(board,p,steps);
                    tile.GetComponent<MeshFilter>().sharedMesh=raisedMeshes[index];
                    tile.sharedMaterials=new[]{topMaterial,baseCliffMaterial??wallMaterial};
                    tile.transform.position=World(p,board);tile.transform.localScale=Vector3.one;
                    tile.sortingOrder=BaseConquest.Presentation.BaseTerrainView.GroundOrder((p.Z-board.Height*.5f)*CellSize);
                }
                if(surfaceRegion=="dungeon")
                {
                    string id=cell.Terrain==TerrainKind.Wall?"dungeon-wall":!ObliqueExploration&&cell.Ramp!=RampAxis.None?"dungeon-stair":p.Z>=12&&(p.X+p.Z)%7<3?"dungeon-damp":"dungeon-floor";
                    var skin=DungeonSkin(id);
                    if(skin!=null)
                    {
                        if(cell.Terrain==TerrainKind.Wall)
                        {
                            if(!dungeonMaterials.TryGetValue("wall-sorted",out var sorted)){sorted=BattlePawn2D.Transparent(skin);dungeonMaterials["wall-sorted"]=sorted;}skin=sorted;
                            tile.sortingOrder=BaseConquest.Presentation.MapSpriteOrder.At(World(p,board).z-CellSize*.5f);
                        }
                        tile.sharedMaterials=raisedMeshes[index]!=null&&cell.Terrain==TerrainKind.Ground ? new[]{skin,DungeonSkin("dungeon-wall")??skin} : Enumerable.Repeat(skin,tile.sharedMaterials.Length).ToArray();if(ground!=null)ground.GetComponent<Renderer>().sharedMaterials=new[]{skin,skin};color=cell.Terrain==TerrainKind.Wall?new Color(.55f,.52f,.45f):Color.white;
                        if(wallCaps[index]!=null)foreach(var cap in wallCaps[index].GetComponentsInChildren<Renderer>()){cap.sharedMaterial=skin;cap.sortingOrder=tile.sortingOrder+1;var tint=new MaterialPropertyBlock();tint.SetColor("_BaseColor",new Color(1,.94f,.77f));cap.SetPropertyBlock(tint);}
                    }
                }
                if (CombatMode && draftAlly) color = Color.Lerp(color, new Color(.24f, .84f, 1f), .72f);
                properties.SetColor("_BaseColor", color); tile.SetPropertyBlock(properties);
                tileColors[index] = color;
            }
            elevationView??=new BattleElevationView(board,Root.transform,groundMaterial);elevationView.Rebuild(gridKnown);
            fieldworkArt??=new BaseConquest.Presentation.FieldworkArtView(board,Root.transform,groundMaterial);fieldworkArt.Rebuild(CombatMode,ObliqueExploration);
            elevationView.Root.SetActive(!ObliqueExploration);
            groundEdges??=new BaseConquest.Presentation.BaseGroundEdgeView(board,Root.transform,groundMaterial);groundEdges.Rebuild(ObliqueExploration);
            var landmarks = Root.transform.Find("RegionLandmarks");
            if (landmarks != null) foreach (Transform landmark in landmarks)
            {
                var authored=landmark.GetComponent<BaseConquest.Presentation.AuthoredMapView>();
                if(authored!=null)authored.SetExploration(ObliqueExploration);
                else landmark.localRotation = ObliqueExploration ? BaseArtTilt : Quaternion.identity;
            }
            if(GridOutlinesVisible)RebuildGridEdges();
        }
        public float ExplorationHeight(double x,double z)
        {
            if(!ObliqueExploration)return 0;
            int cellX=Mathf.FloorToInt((float)x),cellZ=Mathf.FloorToInt((float)z);
            return BaseConquest.Presentation.BaseTerrainView.Height(board,new GridPosition(cellX,cellZ),(float)x-cellX,(float)z-cellZ);
        }
        private static Mesh MakeBuildingMesh()
        {
            var mesh=new Mesh {name="WholeBuildingTextureCell"};
            mesh.vertices=new[] {new Vector3(-.5f,.5f,-.5f),new Vector3(-.5f,.5f,.5f),new Vector3(.5f,.5f,.5f),new Vector3(.5f,.5f,-.5f),
                new Vector3(-.5f,-.5f,-.5f),new Vector3(-.5f,-.5f,.5f),new Vector3(.5f,-.5f,.5f),new Vector3(.5f,-.5f,-.5f)};
            mesh.triangles=new[] {0,1,2,0,2,3,4,6,5,4,7,6,0,4,5,0,5,1,1,5,6,1,6,2,2,6,7,2,7,3,3,7,4,3,4,0};
            mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        private bool exitMarkers;
        private GridPosition markedExitA,markedExitB;
        public void MarkExits(GridPosition a,GridPosition b)
        {
            if(exitMarkers&&a==markedExitA&&b==markedExitB)return;exitMarkers=true;markedExitA=a;markedExitB=b;
            foreach(var p in new[] {a,b}) if(board.Contains(p)){int i=p.Z*board.Width+p.X;tileColors[i]=new Color(.1f,.55f,.32f);tiles[i].GetPropertyBlock(properties);properties.SetColor("_BaseColor",tileColors[i]);tiles[i].SetPropertyBlock(properties);}visionRevision=-1;
        }
        private BattleVision renderedVision;
        private Material heightFogMaterial;private Mesh heightFogMesh;private readonly Dictionary<int,Mesh> rampFogMeshes=new Dictionary<int,Mesh>();
        public void RefreshVision(BattleVision vision, BattleSide side, bool showMapEditor, bool reveal = false)
        {
            if (renderedVision==vision&&visionRevision == vision.Revision && visionSide == side && editingMap == showMapEditor && visionReveal == reveal) return;
            renderedVision=vision;visionRevision = vision.Revision; visionSide = side; editingMap = showMapEditor; visionReveal = reveal;
            gridKnown=new bool[tiles.Length];
            foreach (GridPosition p in board.Positions())
            {
                int index = p.Z * board.Width + p.X; bool explored = showMapEditor || vision.Explored(side, p), visible = showMapEditor || vision.Visible(side, p);
                if(vision.HeightMode)
                {
                    bool known=showMapEditor||vision.TerrainVisible(side,p);gridKnown[index]=known;
                    tiles[index].enabled=known;fogTiles[index].gameObject.SetActive(!visible);
                    if(heightFogMaterial==null){heightFogMaterial=new Material(Resources.Load<Shader>("Presentation/GreyFog"));heightFogMesh=new Mesh {vertices=new[]{new Vector3(-.5f,0,-.5f),new Vector3(-.5f,0,.5f),new Vector3(.5f,0,.5f),new Vector3(.5f,0,-.5f)},triangles=new[]{0,1,2,0,2,3}};heightFogMesh.RecalculateBounds();}
                    bool sloped=ObliqueExploration&&board.Cell(p).Ramp!=RampAxis.None;
                    var fogMesh=heightFogMesh;
                    if(sloped)
                    {
                        if(!rampFogMeshes.TryGetValue(index,out fogMesh)){fogMesh=new Mesh{name="RampFog_"+index};fogMesh.vertices=heightFogMesh.vertices;fogMesh.triangles=heightFogMesh.triangles;rampFogMeshes[index]=fogMesh;}
                        var vertices=fogMesh.vertices;
                        for(int corner=0;corner<vertices.Length;corner++)vertices[corner].y=BaseConquest.Presentation.BaseTerrainView.Height(board,p,vertices[corner].x+.5f,vertices[corner].z+.5f);
                        fogMesh.vertices=vertices;fogMesh.RecalculateBounds();
                    }
                    fogTiles[index].GetComponent<MeshFilter>().sharedMesh=fogMesh;fogTiles[index].sharedMaterial=heightFogMaterial;
                    fogTiles[index].transform.position=World(p,board)+Vector3.up*(.03f+(ObliqueExploration&&!sloped?ExplorationHeight(p.X+.5,p.Z+.5):0));fogTiles[index].transform.localScale=new Vector3(CellSize,1,CellSize);
                    properties.Clear();properties.SetColor("_BaseColor",new Color(.4f,.42f,.44f,known?.3f:1));fogTiles[index].SetPropertyBlock(properties);
                    properties.Clear();properties.SetColor("_BaseColor",tileColors[index]);tiles[index].SetPropertyBlock(properties);
                    if(wallCaps[index]!=null)wallCaps[index].SetActive(known&&DisplayCell(p).Terrain==TerrainKind.Wall);
                    continue;
                }
                bool deploy = vision.DeploymentKnown(side, p);
                fogTiles[index].GetComponent<MeshFilter>().sharedMesh=cubeMesh;
                gridKnown[index]=visible||deploy||showMapEditor;
                tiles[index].enabled = visible || deploy || reveal || showMapEditor; fogTiles[index].gameObject.SetActive(!visible && !deploy);
                if (reveal) { fogTiles[index].transform.position = World(p, board) + Vector3.up * 1.3f; fogTiles[index].transform.localScale = new Vector3(CellSize - 0.04f, 0.025f, CellSize - 0.04f); }
                else { fogTiles[index].transform.position = World(p, board) + Vector3.down * 0.1f; fogTiles[index].transform.localScale = new Vector3(CellSize - 0.04f, 0.18f, CellSize - 0.04f); }
                fogTiles[index].sharedMaterial = reveal ? translucentFog : groundMaterial;properties.Clear();
                properties.SetColor("_BaseColor", new Color(0.035f, 0.045f, 0.06f, reveal ? 0.72f : 1)); fogTiles[index].SetPropertyBlock(properties);
                tiles[index].GetPropertyBlock(properties);
                properties.SetColor("_BaseColor", tileColors[index] * (visible || deploy || reveal ? 1 : 0.32f)); tiles[index].SetPropertyBlock(properties);
                if (wallCaps[index] != null)
                {
                    wallCaps[index].SetActive((visible || reveal || showMapEditor) && DisplayCell(p).Terrain == TerrainKind.Wall);
                    properties.SetColor("_BaseColor", visible || reveal ? Color.white : new Color(0.32f, 0.32f, 0.32f));
                    foreach (Renderer cap in wallCaps[index].GetComponentsInChildren<Renderer>()) cap.SetPropertyBlock(properties);
                }
            }
            RebuildGridEdges();
            elevationView?.Rebuild(reveal&&!vision.HeightMode ? null : gridKnown);
            fieldworkArt?.SetKnown(reveal&&!vision.HeightMode ? null : gridKnown);
            foreach(var edge in Root.GetComponentsInChildren<BaseConquest.Presentation.EnvironmentRoadEdges>(true))edge.SetKnown(reveal&&!vision.HeightMode ? null : gridKnown);
        }
        private Bounds TileBounds(GridPosition point)
        {
            int index = point.Z * board.Width + point.X; Bounds bounds = tiles[index].bounds;
            if (wallCaps[index] != null && wallCaps[index].activeSelf) foreach (var renderer in wallCaps[index].GetComponentsInChildren<Renderer>()) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }
        private GameObject MakeWallCaps(GridPosition point)
        {
            var root = new GameObject("WallBattlements_" + point); root.transform.SetParent(Root.transform, false); root.transform.position = World(point, board) + Vector3.up;
            for (int i = 0; i < 3; i++)
            {
                var cap = GameObject.CreatePrimitive(PrimitiveType.Cube); cap.transform.SetParent(root.transform, false); cap.transform.localPosition = Vector3.right * (i - 1) * 0.36f;
                cap.transform.localScale = new Vector3(0.28f, 0.26f, CellSize - 0.04f); DestroyObject(cap.GetComponent<Collider>()); cap.GetComponent<Renderer>().sharedMaterial = wallMaterial;
            }
            return root;
        }
        private static Material MakeMaterial(Material source, Texture2D texture, string name)
        {
            var material = new Material(source) { name = name };
            material.SetTexture(material.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex", texture); material.SetColor("_BaseColor", Color.white); return material;
        }
        private static Texture2D MakeSwampTexture()
        {
            var texture = new Texture2D(32, 32) { name = "SwampMudAndReeds", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            {
                bool reed = x % 9 == 2 && y % 12 < 7; bool puddle = (x * 3 + y * 7) % 23 < 7;
                texture.SetPixel(x, y, reed ? new Color(0.52f, 0.6f, 0.23f) : puddle ? new Color(0.16f, 0.32f, 0.22f) : new Color(0.32f, 0.29f, 0.16f));
            }
            texture.Apply(); return texture;
        }
        private static Texture2D MakeTexture(bool water)
        {
            var texture = new Texture2D(96, 96, TextureFormat.RGBA32, false) { name = water ? "WaterWavePattern" : "WallBrickPattern", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var colors = new Color[96 * 96];
            for (int y = 0; y < 96; y++) for (int x = 0; x < 96; x++)
            {
                if (water)
                {
                    float band = Mathf.Repeat(y + Mathf.Sin(x * Mathf.PI * 4 / 96) * 3, 24);
                    colors[y * 96 + x] = band < 2.5f ? new Color(0.62f, 0.94f, 1) : Color.Lerp(new Color(0.015f, 0.3f, 0.6f), new Color(0.08f, 0.6f, 0.85f), band / 24);
                }
                else
                {
                    int row = y / 24, shifted = (x + (row % 2) * 16) % 32;
                    bool mortar = y % 24 < 3 || shifted < 3; float variation = ((row * 7 + (x + (row % 2) * 16) / 32 * 13) % 9) * 0.012f;
                    colors[y * 96 + x] = mortar ? new Color(0.22f, 0.25f, 0.28f) : new Color(0.56f + variation, 0.6f + variation, 0.64f + variation);
                }
            }
            texture.SetPixels(colors); texture.Apply(); return texture;
        }
        public void Dispose() {DestroyObject(heightFogMaterial);DestroyObject(heightFogMesh);foreach(var mesh in rampFogMeshes.Values)DestroyObject(mesh);foreach(var m in dungeonMaterials.Values)DestroyObject(m);elevationView?.Dispose();groundEdges?.Dispose();fieldworkArt?.Dispose();DestroyObject(baseGrassMaterial);DestroyObject(baseRaisedGrassMaterial);DestroyObject(baseRaisedRoadMaterial);DestroyObject(baseCliffMaterial);DestroyObject(baseStairMaterial);foreach(var mesh in buildingMeshes)DestroyObject(mesh);foreach(var mesh in raisedMeshes)DestroyObject(mesh);foreach(var skin in buildingMaterials.Values)DestroyObject(skin);foreach(var skin in surfaceMaterials.Values)DestroyObject(skin);DestroyObject(buildingMaterial);DestroyObject(buildingTexture);DestroyObject(gridMesh); DestroyObject(swampMaterial); DestroyObject(swampTexture); DestroyObject(translucentFog); DestroyObject(waterMaterial); DestroyObject(wallMaterial); DestroyObject(waterTexture); DestroyObject(wallTexture); }
        public void ShowOutline(GridPosition from, GridPosition to, bool visible)
        {
            outline.enabled = visible; if (!visible) return;
            Vector3 a = World(new GridPosition(Mathf.Min(from.X, to.X), Mathf.Min(from.Z, to.Z)), board) - new Vector3(CellSize / 2, 0, CellSize / 2);
            Vector3 b = World(new GridPosition(Mathf.Max(from.X, to.X), Mathf.Max(from.Z, to.Z)), board) + new Vector3(CellSize / 2, 0, CellSize / 2);
            outline.SetPosition(0, new Vector3(a.x, 0.85f, a.z)); outline.SetPosition(1, new Vector3(a.x, 0.85f, b.z));
            outline.SetPosition(2, new Vector3(b.x, 0.85f, b.z)); outline.SetPosition(3, new Vector3(b.x, 0.85f, a.z));
        }
        public void TintSelection(Color color){cellSelection.startColor=cellSelection.endColor=color;properties.Clear();properties.SetColor("_BaseColor",color);properties.SetColor("_Color",color);cellSelection.SetPropertyBlock(properties);}
        public void SelectCell(GridPosition? cell)
        {
            SelectedCell = cell; cellSelection.enabled = cell.HasValue; if (!cell.HasValue) return;
            TintSelection(Color.white);
            Vector3 p = World(cell.Value, board) + Vector3.up * 1.1f; float r = CellSize * 0.48f;
            var corners=new[] { p + new Vector3(-r, 0, -r), p + new Vector3(-r, 0, r), p + new Vector3(r, 0, r), p + new Vector3(r, 0, -r) };
            if(ObliqueExploration)
                for(int i=0;i<corners.Length;i++)corners[i].y=BaseConquest.Presentation.BaseTerrainView.Height(board,cell.Value,(corners[i].x-p.x)/CellSize+.5f,(corners[i].z-p.z)/CellSize+.5f)+.025f;
            cellSelection.SetPositions(corners);
        }
        public void SelectFineCell(int x,int z, bool guard = false)
        {
            TintSelection(guard ? new Color(1f, .81f, .22f) : Color.white);
            SelectedCell=new GridPosition(x/2,z/2);cellSelection.enabled=true;
            var p=new Vector3((x*.5f+.25f-board.Width*.5f)*CellSize,1.1f,(z*.5f+.25f-board.Height*.5f)*CellSize);float r=CellSize*.24f;
            cellSelection.SetPositions(new[]{p+new Vector3(-r,0,-r),p+new Vector3(-r,0,r),p+new Vector3(r,0,r),p+new Vector3(r,0,-r)});
        }
        public void CenterOn(GridPosition cell) { var p = World(cell, board); panOffset = new Vector3(p.x, 0, p.z); UpdateCamera(0, 0); }
        public void FollowSelection(GridPosition cell, Vector3 position)
        {
            SelectedCell = cell; cellSelection.enabled = true; TintSelection(Color.white); position.y = 1.4f; float r = CellSize * 0.48f;
            cellSelection.SetPositions(new[] { position + new Vector3(-r, 0, -r), position + new Vector3(-r, 0, r), position + new Vector3(r, 0, r), position + new Vector3(r, 0, -r) });
        }
        public void MarkExits(Font font, GridPosition? exitA=null, GridPosition? exitB=null)
        {
            for (int i = 0; i < 2; i++)
            {
                var cell = i == 0 ? exitA??new GridPosition(0, board.Height - 1) : exitB??new GridPosition(board.Width - 1, 0);
                string name = "ExitMarker_" + (i == 0 ? "A" : "B");
                var existing = Root.transform.Find(name);
                var marker = existing != null ? existing.gameObject : new GameObject(name);
                marker.transform.SetParent(Root.transform, false); marker.transform.position = World(cell, board) + new Vector3(0, 0.5f, -0.34f); marker.transform.rotation = Quaternion.Euler(90, 0, 0);
                var text = marker.GetComponent<TextMesh>();
                if (text == null) text = marker.AddComponent<TextMesh>();
                text.text = i == 0 ? "我方撤离" : "敌方撤离"; text.fontSize = 36; text.characterSize = 0.08f; text.anchor = TextAnchor.MiddleCenter; text.color = new Color(0.65f, 1, 0.75f);
                if (font != null) { text.font = font; marker.GetComponent<Renderer>().sharedMaterial = font.material; }
            }
        }
        public static void DestroyObject(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Object.Destroy(obj); else Object.DestroyImmediate(obj);
        }
    }
}
