using UnityEngine;
using BaseConquest.Battle;

namespace BaseConquest.Presentation
{
    // Only the base exploration view uses physical height; battle keeps its existing flat ink.
    public static class BaseTerrainView
    {
        public const float LevelHeight=1.2f;
        // Ground draws before transparent upright art; physical depth still occludes art behind a foreground ledge.
        public static int GroundOrder(float worldZ)=>-32000+Mathf.Clamp(MapSpriteOrder.At(worldZ)/4,-500,500);
        public static Material AtlasMaterial(Material source,Texture2D atlas,Vector2 quadrant,string name,bool transparent=false)
        {
            if(atlas==null)return null;
            var material=transparent ? BattlePawn2D.Transparent(source) : new Material(source);material.name=name;
            if(transparent)material.SetFloat("_Cull",2);
            material.SetTexture("_BaseMap",atlas);material.mainTexture=atlas;
            material.SetTextureScale("_BaseMap",new Vector2(.499f,.499f));
            material.SetTextureOffset("_BaseMap",quadrant+Vector2.one*.0005f);
            return material;
        }
        public static float Height(BattleBoard board, GridPosition p, float x, float z)
        {
            return (float)board.SurfaceLevel(p,x,z)*LevelHeight;
        }
        public static Mesh Ground(BattleBoard board,GridPosition p)
        {
            float r=BattleBoardView.CellSize*.5f;
            var mesh=new Mesh {name="BaseTerrace_"+p};
            var vertices=new System.Collections.Generic.List<Vector3>{
                new Vector3(-r,Height(board,p,0,0)-.01f,-r),new Vector3(-r,Height(board,p,0,1)-.01f,r),
                new Vector3(r,Height(board,p,1,1)-.01f,r),new Vector3(r,Height(board,p,1,0)-.01f,-r),
                new Vector3(-r,-.19f,-r),new Vector3(-r,-.19f,r),new Vector3(r,-.19f,r),new Vector3(r,-.19f,-r)};
            var uv=new System.Collections.Generic.List<Vector2>{Vector2.zero,Vector2.up,Vector2.one,Vector2.right,Vector2.zero,Vector2.up,Vector2.one,Vector2.right};
            if(board.Cell(p).Ramp==RampAxis.EastWest)for(int i=0;i<4;i++)uv[i]=new Vector2(uv[i].y,uv[i].x);
            var sides=new System.Collections.Generic.List<int>();
            for(int side=0;side<4;side++)
            {
                int next=(side+1)%4;
                var direction=side==0 ? new GridPosition(-1,0) : side==1 ? new GridPosition(0,1) : side==2 ? new GridPosition(1,0) : new GridPosition(0,-1);
                var neighbor=p+direction;
                float Adjacent(Vector3 vertex)=>Height(board,neighbor,(vertex.x+r)/BattleBoardView.CellSize-direction.X,(vertex.z+r)/BattleBoardView.CellSize-direction.Z)-.01f;
                var a=vertices[side];var b=vertices[next];
                var bottomA=a;bottomA.y=Mathf.Min(a.y,Adjacent(a));var bottomB=b;bottomB.y=Mathf.Min(b.y,Adjacent(b));
                // Internal walls must not be drawn through adjacent transparent terrace tops.
                if(a.y-bottomA.y<.001f&&b.y-bottomB.y<.001f)continue;
                int start=vertices.Count;
                vertices.AddRange(new[]{a,bottomA,bottomB,b});
                uv.AddRange(new[]{Vector2.up,Vector2.zero,Vector2.right,Vector2.one});
                sides.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
            }
            mesh.SetVertices(vertices);mesh.SetUVs(0,uv);
            mesh.subMeshCount=2;mesh.SetTriangles(new[]{0,1,2,0,2,3},0);
            mesh.SetTriangles(sides,1);
            mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        public static bool Raycast(MeshFilter filter,Ray ray,out float distance)
        {
            distance=float.MaxValue;var mesh=filter.sharedMesh;var vertices=mesh.vertices;var triangles=mesh.triangles;
            for(int i=0;i<triangles.Length;i+=3)
            {
                var a=filter.transform.TransformPoint(vertices[triangles[i]]);
                var edge1=filter.transform.TransformPoint(vertices[triangles[i+1]])-a;
                var edge2=filter.transform.TransformPoint(vertices[triangles[i+2]])-a;
                var cross=Vector3.Cross(ray.direction,edge2);float determinant=Vector3.Dot(edge1,cross);
                if(Mathf.Abs(determinant)<.000001f)continue;
                float inverse=1/determinant;var offset=ray.origin-a;float u=Vector3.Dot(offset,cross)*inverse;
                if(u<0||u>1)continue;
                var q=Vector3.Cross(offset,edge1);float v=Vector3.Dot(ray.direction,q)*inverse;
                if(v<0||u+v>1)continue;
                float hit=Vector3.Dot(edge2,q)*inverse;if(hit>=0&&hit<distance)distance=hit;
            }
            return distance<float.MaxValue;
        }
    }
}
