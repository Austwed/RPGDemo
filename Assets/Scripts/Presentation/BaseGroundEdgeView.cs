using System.Collections.Generic;
using UnityEngine;
using BaseConquest.Battle;

namespace BaseConquest.Presentation
{
    public sealed class BaseGroundEdgeView
    {
        public readonly GameObject Root;
        private readonly BattleBoard board;
        private readonly Mesh mesh;
        private readonly Material[] materials;
        private static readonly GridPosition[] Directions={new GridPosition(0,-1),new GridPosition(1,0),new GridPosition(0,1),new GridPosition(-1,0)};
        public BaseGroundEdgeView(BattleBoard board,Transform parent,Material source)
        {
            this.board=board;
            Root=new GameObject("BaseGroundEdges",typeof(MeshFilter),typeof(MeshRenderer));Root.transform.SetParent(parent,false);
            mesh=new Mesh {name="GrassRoadAndCliffEdges"};Root.GetComponent<MeshFilter>().sharedMesh=mesh;
            var atlas=Resources.Load<Texture2D>("MapSurfaces/BaseGroundEdges-v1");
            materials=new[]{
                BaseTerrainView.AtlasMaterial(source,atlas,new Vector2(0,.5f),"GrassPathFringe",true),
                BaseTerrainView.AtlasMaterial(source,atlas,new Vector2(.5f,.5f),"GrassOuterCorner",true),
                BaseTerrainView.AtlasMaterial(source,atlas,Vector2.zero,"GrassInnerCorner",true),
                BaseTerrainView.AtlasMaterial(source,atlas,new Vector2(.5f,0),"GrassCliffLip",true)};
            // Inset each atlas sample beyond the generated separator and its mip-filter footprint.
            foreach(var material in materials)if(material!=null)
            {material.SetTextureScale("_BaseMap",new Vector2(.475f,.475f));material.SetTextureOffset("_BaseMap",material.GetTextureOffset("_BaseMap")+Vector2.one*.012f);}
            var renderer=Root.GetComponent<Renderer>();renderer.sharedMaterials=materials;
            renderer.sortingOrder=-31000; // Above all ground meshes, below every upright sprite.
        }
        private bool Grass(GridPosition p)=>board.Contains(p)&&board.Cell(p).Terrain==TerrainKind.Ground&&board.Cell(p).Ramp==RampAxis.None&&GroundSurfaceRules.Resolve(board.Cell(p).Surface,"base",p.X,p.Z)==GroundSurface.Grass;
        private bool Edge(GridPosition p,GridPosition d)
        {
            var other=board.Cell(p+d);
            if(other==null)return true;
            if(other.Terrain!=TerrainKind.Ground)return false;
            return !Grass(p+d)||board.Cell(p).Elevation!=other.Elevation;
        }
        public void Rebuild(bool visible)
        {
            // The old painted fringe blends poorly with the crisp pixel tiles.
            Root.SetActive(visible&&materials[0]!=null&&PixelDemoArt.Load("Terrain","grass")==null);if(!Root.activeSelf)return;
            var vertices=new List<Vector3>();var uv=new List<Vector2>();
            var triangles=new[]{new List<int>(),new List<int>(),new List<int>(),new List<int>()};
            void Quad(GridPosition p,Vector3 center,Vector3 across,Vector3 inward,int kind)
            {
                int start=vertices.Count;var cellCenter=BattleBoardView.World(p,board);
                foreach(var v in new[]{center-across-inward,center-across+inward,center+across+inward,center+across-inward})
                {
                    var point=v;float x=Mathf.Clamp01((v.x-cellCenter.x)/BattleBoardView.CellSize+.5f),z=Mathf.Clamp01((v.z-cellCenter.z)/BattleBoardView.CellSize+.5f);
                    point.y=BaseTerrainView.Height(board,p,x,z)+.025f;vertices.Add(point);
                }
                uv.AddRange(new[]{Vector2.zero,Vector2.up,Vector2.one,Vector2.right});
                triangles[kind].AddRange(Vector3.Cross(inward,across).y>0 ? new[]{start,start+1,start+2,start,start+2,start+3} : new[]{start,start+2,start+1,start,start+3,start+2});
            }
            foreach(var p in board.Positions())
            {
                var center=BattleBoardView.World(p,board);float r=BattleBoardView.CellSize*.5f;
                if(Grass(p))
                {
                    for(int side=0;side<4;side++)
                    {
                        var d=Directions[side];if(!Edge(p,d))continue;
                        var outward=new Vector3(d.X,0,d.Z);var inward=-outward;
                        bool cliff=board.Cell(p+d)==null||board.Cell(p+d).Elevation<board.Cell(p).Elevation;
                        Quad(p,center+outward*r,Vector3.Cross(Vector3.up,inward)*r,inward*.18f,cliff ? 3 : 0);
                        var next=Directions[(side+1)%4];
                        if(Edge(p,next))
                        {
                            var corner=d+next;
                            Quad(p,center+new Vector3(corner.X,0,corner.Z)*r,Vector3.right*corner.X*.18f,Vector3.forward*-corner.Z*.18f,1);
                        }
                    }
                }
                else if(board.Cell(p).Terrain==TerrainKind.Ground&&board.Cell(p).Ramp==RampAxis.None)
                {
                    for(int side=0;side<4;side++)
                    {
                        var a=Directions[side];var b=Directions[(side+1)%4];var corner=a+b;
                        if(Grass(p+a)&&Grass(p+b)&&Grass(p+corner)&&board.Cell(p+a).Elevation==board.Cell(p).Elevation&&board.Cell(p+b).Elevation==board.Cell(p).Elevation)
                            Quad(p,center+new Vector3(corner.X,0,corner.Z)*r,Vector3.right*-corner.X*.18f,Vector3.forward*corner.Z*.18f,2);
                    }
                }
            }
            mesh.Clear();mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.subMeshCount=4;
            for(int i=0;i<4;i++)mesh.SetTriangles(triangles[i],i);
            mesh.RecalculateNormals();mesh.RecalculateBounds();
        }
        public void Dispose()
        {foreach(var material in materials)BattleBoardView.DestroyObject(material);BattleBoardView.DestroyObject(mesh);BattleBoardView.DestroyObject(Root);}
    }
}
