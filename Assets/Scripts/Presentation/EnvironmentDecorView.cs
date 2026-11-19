using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Base;
using BaseConquest.Battle;
using UnityEngine;
namespace BaseConquest.Presentation
{
    public sealed class EnvironmentDecorView : MonoBehaviour
    {
        private BaseSceneController owner;private BaseMapObject placement;private EnvironmentDefinition definition;private readonly List<GameObject> sprites=new List<GameObject>();private readonly List<Material> materials=new List<Material>();private EnvironmentAnimal[] animals;
        private Quaternion lastCameraRotation;private float lastGroundHeight;
        public static void Attach(GameObject root,BaseMapObject placement,Material source)
        {
            var view=root.AddComponent<EnvironmentDecorView>();view.owner=root.GetComponentInParent<BaseSceneController>(true);view.placement=placement;view.definition=BaseEnvironment.Find(placement.AssetId);
            if(view.definition==null)throw new ArgumentException("未知环境素材："+placement.AssetId);
            var d=view.definition;
            void Sprite(string id,float width,float height)
            {
                var go=GameObject.CreatePrimitive(PrimitiveType.Quad);go.name=id;go.transform.SetParent(root.transform,false);BattleBoardView.DestroyObject(go.GetComponent<Collider>());go.transform.localScale=new Vector3(width*BattleBoardView.CellSize,height*BattleBoardView.CellSize,1);
                var material=BattlePawn2D.Transparent(source);material.SetTexture("_BaseMap",Resources.Load<Texture2D>("Environment/"+id));material.SetColor("_BaseColor",Color.white);
                var renderer=go.GetComponent<Renderer>();renderer.sharedMaterial=material;view.materials.Add(material);view.sprites.Add(go);
                if(d.Animal==""&&id!="rabbit-hole"&&height>=1.15f)SceneryOcclusion.Attach(renderer,view.owner.WorldCamera);
            }
            if(SpriteMetrics.Building(d.Id)||d.Id.StartsWith("tree-"))
            {SpriteFacade.Create(root.transform,Resources.Load<Texture2D>("Environment/"+d.Id),view.owner.WorldPoint(placement.X,d.Id.StartsWith("tree-")?placement.Y:placement.Y-placement.Height/2),(float)d.ArtWidth*BattleBoardView.CellSize,source,view.owner.WorldCamera);return;}
            if(d.Animal=="")Sprite(d.Id,(float)d.ArtWidth,(float)d.ArtHeight);
            else
            {
                int count=d.Animal=="bird"?3:1;view.animals=view.owner.EnvironmentAnimals(placement,d);
                if(d.Animal=="rabbit")Sprite("rabbit-hole",.6f,.4f);
                for(int n=0;n<count;n++){Sprite(d.Animal+"-sheet",(float)d.ArtWidth,(float)d.ArtHeight);}
            }
            view.Refresh(0);
        }
        private void Update()
        {
            if(owner==null||sprites.Count==0||owner.WorldCamera==null)return;
            if(animals!=null)Refresh(Time.deltaTime);
            else if(owner.WorldCamera.transform.rotation!=lastCameraRotation||owner.WorldPoint(placement.X,placement.Y-placement.Height/2).y!=lastGroundHeight)Refresh(0);
        }
        private void Refresh(double seconds)
        {
            if(owner==null)return;var d=definition;var camera=owner.WorldCamera;if(camera==null)return;
            lastCameraRotation=camera.transform.rotation;lastGroundHeight=owner.WorldPoint(placement.X,placement.Y-placement.Height/2).y;
            bool visible=d.Animal==""?owner.SceneCellKnown(new GridPosition((int)placement.X,(int)placement.Y)):owner.SceneObjectVisible(placement);
            for(int n=0;n<sprites.Count;n++)
            {
                var go=sprites[n];BasePoint point=new BasePoint(placement.X,placement.Y-placement.Height/2);string pose="idle";double time=0;int direction=1;
                if(animals!=null&&(d.Animal!="rabbit"||n>0))
                {
                    var animal=animals[d.Animal=="rabbit"?n-1:n];animal.Tick(seconds,owner.EnvironmentPlayer,owner.EnvironmentPaused);point=animal.Position;pose=animal.Pose;time=animal.StateTime;direction=animal.Direction;
                    var material=materials[n];int action=d.Animal=="bird"?(pose=="fly"?1:0):pose=="walk"?1:pose=="burrow"?2:pose=="emerge"?3:0;
                    int columns=d.Animal=="bird"?16:32,frame=pose=="burrow"||pose=="emerge"?Math.Min(7,(int)(time/.65*8)):(int)(animal.Clock*10)%8;
                    material.SetTextureScale("_BaseMap",new Vector2(1f/columns,.5f));material.SetTextureOffset("_BaseMap",new Vector2((action*8+frame)/(float)columns,direction<0?.5f:0));
                    if(d.Animal=="bird"&&animal.State>0)point=new BasePoint(point.X,point.Y+Math.Min(1,animal.StateTime));
                }
                bool hole=d.Id=="rabbit-hole"||d.Animal=="rabbit"&&n==0;
                if(hole)
                {
                    point=animals!=null?animals[0].Home:BaseEnvironment.FineCenter(new BasePoint(placement.X,placement.Y));
                    var metric=SpriteMetrics.For(Resources.Load<Texture2D>("Environment/rabbit-hole"));materials[n].SetTextureScale("_BaseMap",new Vector2(metric.Width,metric.Height));materials[n].SetTextureOffset("_BaseMap",new Vector2(metric.U,metric.V));
                    go.transform.localScale=new Vector3(.6f*BattleBoardView.CellSize,.6f*BattleBoardView.CellSize/metric.Aspect,1);
                }
                go.SetActive(visible&&pose!="hidden");if(!go.activeSelf)continue;
                var feet=owner.WorldPoint(point.X,point.Y);float height=go.transform.localScale.y;
                go.transform.position=hole?feet+Vector3.up*.045f:feet+camera.transform.up*height*(animals!=null?.375f:.5f);go.transform.rotation=hole?Quaternion.Euler(90,0,0):camera.transform.rotation;MapSpriteOrder.Apply(go.transform,feet.z);if(hole)go.GetComponent<Renderer>().sortingOrder=-29500;
            }
        }
        private void OnDestroy(){foreach(var material in materials)BattleBoardView.DestroyObject(material);}
    }
    public sealed class EnvironmentRoadEdges : MonoBehaviour
    {
        private Mesh mesh;private Material[] materials;
        private Vector3[] raisedVertices,flatVertices;
        private int[][] allTriangles,triangleCells;
        private readonly Texture2D[] edgeMasks = new Texture2D[2];
        private ushort[,] slotsByCell;
        private int width;
        // 路面边缘只有两种：石板(cobble)与泥径(mud)。统一 Cainos 后两种都换：cobble→Stone()，mud→path-64-v1。
        private static readonly string[] FallbackBase={"cobble","mud"};
        public static EnvironmentRoadEdges Build(BaseState state,BattleBoard b,Transform parent,bool raised)
        {
            if(state==null||!BaseEnvironment.Active(state)&&!b.Cells.Any(c=>c.Surface==GroundSurface.Cobble||c.Surface==GroundSurface.DirtPath))return null;var root=new GameObject("EnvironmentRoadEdges",typeof(MeshFilter),typeof(MeshRenderer),typeof(EnvironmentRoadEdges));root.transform.SetParent(parent,false);var view=root.GetComponent<EnvironmentRoadEdges>();
            view.materials=new Material[2];var on=HomeTerrainTrial.Enabled&&HomeTerrainTrial.Eligible(state);var trial=on?HomeTerrainTrial.Texture(GroundSurface.DirtPath):null;var stone=on?HomeTerrainTrial.Stone():null;
            view.width = b.Width;
            view.slotsByCell = new ushort[b.Width * b.Height, 2];
            var shader = Resources.Load<Shader>("Presentation/RoadTransition");
            for (int i = 0; i < view.materials.Length; i++)
            {
                var material = view.materials[i] = new Material(shader);
                var surface = i == 0 ? stone : trial;
                material.SetTexture("_BaseMap", surface != null ? surface : Resources.Load<Texture2D>("Environment/" + FallbackBase[i]));
                view.edgeMasks[i] = Resources.Load<Texture2D>("Environment/" + (i == 0 ? "cobble-edge" : "mud-edge"));
                material.SetTexture("_MaskMap", view.edgeMasks[i]);
                material.SetVector("_Grid", new Vector4(b.Width, b.Height, BattleBoardView.CellSize, 0));
            }
            var verts=new List<Vector3>();var uv=new List<Vector2>();var groups=new[]{new List<int>(),new List<int>()};var cells=new[]{new List<int>(),new List<int>()};var dirs=new[]{new GridPosition(0,1),new GridPosition(1,0),new GridPosition(0,-1),new GridPosition(-1,0)};
            foreach(var p in b.Positions())
            {
                if(b.Cell(p).Surface!=GroundSurface.Grass||!b.Walkable(p))continue;
                for(int kind=0;kind<2;kind++)
                {
                    var surface=kind==0?GroundSurface.Cobble:GroundSurface.DirtPath;
                    bool Road(GridPosition q)=>b.Cell(q)!=null&&b.Cell(q).Elevation==b.Cell(p).Elevation&&b.Cell(q).Ramp==RampAxis.None&&b.Cell(q).Surface==surface;
                    var sides=dirs.Select(d=>Road(p+d)).ToArray();var corners=new[]{new GridPosition(-1,1),new GridPosition(1,1),new GridPosition(1,-1),new GridPosition(-1,-1)}.Select(d=>Road(p+d)).ToArray();
                    foreach(int slot in BaseEnvironment.RoadEdgeSlots(sides,corners))
                    {
                        view.slotsByCell[p.Z*b.Width+p.X,kind] |= (ushort)(1 << slot);
                    int start=verts.Count;float x0=slot%4/4f,x1=x0+.25f,y1=1-slot/4/4f,y0=y1-.25f;var center=BattleBoardView.World(p,b);float r=BattleBoardView.CellSize*.5f;
                    verts.AddRange(new[]{center+new Vector3(-r,BaseTerrainView.Height(b,p,0,0)+.035f,-r),center+new Vector3(-r,BaseTerrainView.Height(b,p,0,1)+.035f,r),center+new Vector3(r,BaseTerrainView.Height(b,p,1,1)+.035f,r),center+new Vector3(r,BaseTerrainView.Height(b,p,1,0)+.035f,-r)});uv.AddRange(new[]{new Vector2(x0,y0),new Vector2(x0,y1),new Vector2(x1,y1),new Vector2(x1,y0)});groups[kind].AddRange(new[]{start,start+1,start+2,start,start+2,start+3});cells[kind].Add(p.Z*b.Width+p.X);
                    }
                }
            }
            view.raisedVertices=verts.ToArray();view.flatVertices=verts.Select(v=>new Vector3(v.x,.035f,v.z)).ToArray();view.allTriangles=groups.Select(g=>g.ToArray()).ToArray();view.triangleCells=cells.Select(c=>c.ToArray()).ToArray();
            view.mesh=new Mesh{name="EnvironmentPathEdges"};view.mesh.SetVertices(verts);view.mesh.SetUVs(0,uv);view.mesh.subMeshCount=2;for(int i=0;i<2;i++)view.mesh.SetTriangles(groups[i],i);root.GetComponent<MeshFilter>().sharedMesh=view.mesh;root.GetComponent<Renderer>().sharedMaterials=view.materials;root.GetComponent<Renderer>().sortingOrder=-30000;view.SetRaised(raised);return view;
        }
        // The visible road extends onto neighbouring grass. Read the same atlas
        // texel as the point-filtered road shader, with the top submesh first.
        internal GroundSurface SurfaceAt(GridPosition cell, float localX, float localZ)
        {
            int index = cell.Z * width + cell.X;
            for (int kind = 1; kind >= 0; kind--)
            {
                int slots = slotsByCell[index, kind];
                if (slots == 0) continue;
                var mask = edgeMasks[kind];
                int w = mask.width / 4, h = mask.height / 4;
                int x = Mathf.Clamp(Mathf.FloorToInt(localX * w), 0, w - 1);
                int z = Mathf.Clamp(Mathf.FloorToInt(localZ * h), 0, h - 1);
                for (int slot = 0; slots != 0; slot++, slots >>= 1)
                    if ((slots & 1) != 0 && mask.GetPixel(slot % 4 * w + x, (3 - slot / 4) * h + z).a >= .5f)
                        return kind == 0 ? GroundSurface.Cobble : GroundSurface.DirtPath;
            }
            return GroundSurface.Grass;
        }
        public void SetRaised(bool raised){if(mesh==null)return;mesh.vertices=raised?raisedVertices:flatVertices;mesh.RecalculateNormals();mesh.RecalculateBounds();}
        public void SetKnown(bool[] known)
        {
            if(mesh==null)return;
            for(int kind=0;kind<allTriangles.Length;kind++)
            {
                if(known==null){mesh.SetTriangles(allTriangles[kind],kind);continue;}
                var visible=new List<int>();for(int q=0;q<triangleCells[kind].Length;q++)if(known[triangleCells[kind][q]])for(int n=0;n<6;n++)visible.Add(allTriangles[kind][q*6+n]);
                mesh.SetTriangles(visible,kind);
            }
        }
        // 开发者开关会在运行中切换试点，边缘 노드不会重建，这里单独换贴图；mask 与拓扑一律不动。
        private void ApplyTrial(BaseState state)
        {
            if(materials==null||materials.Length<2||materials[1]==null)return;
            bool on=BaseConquest.Presentation.HomeTerrainTrial.Enabled&&BaseConquest.Presentation.HomeTerrainTrial.Eligible(state);
            var trial=on?BaseConquest.Presentation.HomeTerrainTrial.Texture(GroundSurface.DirtPath):null;
            var stone=on?BaseConquest.Presentation.HomeTerrainTrial.Stone():null;
            materials[0].SetTexture("_BaseMap",stone!=null?stone:Resources.Load<Texture2D>("Environment/cobble"));
            materials[1].SetTexture("_BaseMap",trial!=null?trial:Resources.Load<Texture2D>("Environment/mud"));
        }
        public static void ApplyTrialToScene(BaseState state)
        {
            foreach(var edge in UnityEngine.Object.FindObjectsByType<EnvironmentRoadEdges>())edge.ApplyTrial(state);
        }
        private void OnDestroy(){if(materials!=null)foreach(var material in materials)BattleBoardView.DestroyObject(material);BattleBoardView.DestroyObject(mesh);}
    }
}
