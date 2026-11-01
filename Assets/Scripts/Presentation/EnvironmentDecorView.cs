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
        public static void Attach(GameObject root,BaseMapObject placement,Material source)
        {
            var view=root.AddComponent<EnvironmentDecorView>();view.owner=root.GetComponentInParent<BaseSceneController>(true);view.placement=placement;view.definition=BaseEnvironment.Find(placement.AssetId);
            if(view.definition==null)throw new ArgumentException("未知环境素材："+placement.AssetId);
            var d=view.definition;
            void Sprite(string id,float width,float height)
            {
                var go=GameObject.CreatePrimitive(PrimitiveType.Quad);go.name=id;go.transform.SetParent(root.transform,false);BattleBoardView.DestroyObject(go.GetComponent<Collider>());go.transform.localScale=new Vector3(width*BattleBoardView.CellSize,height*BattleBoardView.CellSize,1);
                var material=BattlePawn2D.Transparent(source);material.SetTexture("_BaseMap",Resources.Load<Texture2D>("Environment/"+id));material.SetColor("_BaseColor",Color.white);go.GetComponent<Renderer>().sharedMaterial=material;view.materials.Add(material);view.sprites.Add(go);
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
        private void Update(){if(owner!=null&&animals!=null)Refresh(Time.deltaTime);}
        private void Refresh(double seconds)
        {
            if(owner==null)return;var d=definition;var camera=owner.WorldCamera;if(camera==null)return;
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
        // 路面边缘只有两种：石板(cobble)与泥径(mud)。统一 Cainos 后两种都换：cobble→Stone()，mud→path-64-v1。
        private static readonly string[] FallbackBase={"cobble","mud"};
        public static void Build(BaseSceneController owner,Transform parent,Material source)
        {
            if(owner.State==null)return;var b=BaseBattlefield.Board(owner.State);if(!BaseEnvironment.Active(owner.State)&&!b.Cells.Any(c=>c.Surface==GroundSurface.Cobble||c.Surface==GroundSurface.DirtPath))return;var root=new GameObject("EnvironmentRoadEdges",typeof(MeshFilter),typeof(MeshRenderer),typeof(EnvironmentRoadEdges));root.transform.SetParent(parent,false);var view=root.GetComponent<EnvironmentRoadEdges>();
            view.materials=new Material[2];var on=BaseConquest.Presentation.HomeTerrainTrial.Enabled&&BaseConquest.Presentation.HomeTerrainTrial.Eligible(owner.State);var trial=on?BaseConquest.Presentation.HomeTerrainTrial.Texture(GroundSurface.DirtPath):null;var stone=on?BaseConquest.Presentation.HomeTerrainTrial.Stone():null;
            for(int i=0;i<2;i++){view.materials[i]=new Material(Resources.Load<Shader>("Presentation/RoadTransition"));view.materials[i].SetTexture("_BaseMap",i==1&&trial!=null?trial:i==0&&stone!=null?stone:Resources.Load<Texture2D>("Environment/"+FallbackBase[i]));view.materials[i].SetTexture("_MaskMap",Resources.Load<Texture2D>("Environment/"+(i==0?"cobble-edge":"mud-edge")));view.materials[i].SetVector("_Grid",new Vector4(b.Width,b.Height,BattleBoardView.CellSize,0));}
            var verts=new List<Vector3>();var uv=new List<Vector2>();var groups=new[]{new List<int>(),new List<int>()};var dirs=new[]{new GridPosition(0,1),new GridPosition(1,0),new GridPosition(0,-1),new GridPosition(-1,0)};
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
                    int start=verts.Count;float x0=slot%4/4f,x1=x0+.25f,y1=1-slot/4/4f,y0=y1-.25f;var center=owner.WorldPoint(p.X+.5,p.Z+.5)+Vector3.up*.035f;float r=BattleBoardView.CellSize*.5f;
                    verts.AddRange(new[]{center+new Vector3(-r,0,-r),center+new Vector3(-r,0,r),center+new Vector3(r,0,r),center+new Vector3(r,0,-r)});uv.AddRange(new[]{new Vector2(x0,y0),new Vector2(x0,y1),new Vector2(x1,y1),new Vector2(x1,y0)});groups[kind].AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
                    }
                }
            }
            view.mesh=new Mesh{name="EnvironmentPathEdges"};view.mesh.SetVertices(verts);view.mesh.SetUVs(0,uv);view.mesh.subMeshCount=2;for(int i=0;i<2;i++)view.mesh.SetTriangles(groups[i],i);view.mesh.RecalculateNormals();view.mesh.RecalculateBounds();root.GetComponent<MeshFilter>().sharedMesh=view.mesh;root.GetComponent<Renderer>().sharedMaterials=view.materials;root.GetComponent<Renderer>().sortingOrder=-30000;
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
            foreach(var edge in UnityEngine.Object.FindObjectsByType<EnvironmentRoadEdges>(FindObjectsSortMode.None))edge.ApplyTrial(state);
        }
        private void OnDestroy(){if(materials!=null)foreach(var material in materials)BattleBoardView.DestroyObject(material);BattleBoardView.DestroyObject(mesh);}
    }
}
