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
        public static Mesh Ground(BattleBoard board,GridPosition p,bool steps=false)
        {
            if (board.IsDoubleLowRamp(p)) return Crest(board, p);
            int low=board.RampLowEdge(p);
            if(steps&&low!=0)return Stairs(board,p,low);
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
                void WallSection(Vector3 a, Vector3 b)
                {
                    var bottomA=a;bottomA.y=Mathf.Min(a.y,Adjacent(a));var bottomB=b;bottomB.y=Mathf.Min(b.y,Adjacent(b));
                    // Internal walls must not be drawn through adjacent transparent terrace tops.
                    if(a.y-bottomA.y<.001f&&b.y-bottomB.y<.001f)return;
                    int start=vertices.Count;
                    vertices.AddRange(new[]{a,bottomA,bottomB,b});
                    uv.AddRange(new[]{Vector2.up,Vector2.zero,Vector2.right,Vector2.one});
                    sides.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
                }
                var first = vertices[side]; var last = vertices[next];
                if (board.IsDoubleLowRamp(neighbor) && board.Cell(neighbor).Ramp ==
                    (direction.X == 0 ? RampAxis.EastWest : RampAxis.NorthSouth))
                {
                    var midpoint = (first + last) * .5f;
                    WallSection(first, midpoint);
                    WallSection(midpoint, last);
                }
                else WallSection(first, last);
            }
            mesh.SetVertices(vertices);mesh.SetUVs(0,uv);
            mesh.subMeshCount=2;mesh.SetTriangles(new[]{0,1,2,0,2,3},0);
            mesh.SetTriangles(sides,1);
            mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        private static Mesh Crest(BattleBoard board, GridPosition p)
        {
            const int halves = 2;
            float size = BattleBoardView.CellSize, radius = size * .5f;
            bool eastWest = board.Cell(p).Ramp == RampAxis.EastWest;
            var vertices = new System.Collections.Generic.List<Vector3>();
            var uv = new System.Collections.Generic.List<Vector2>();
            var top = new System.Collections.Generic.List<int>();
            var walls = new System.Collections.Generic.List<int>();

            Vector3 Point(float along, float across)
            {
                float x = eastWest ? along : across, z = eastWest ? across : along;
                return new Vector3(x * size - radius, Height(board, p, x, z) - .01f, z * size - radius);
            }

            void Face(System.Collections.Generic.List<int> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
            {
                int start = vertices.Count;
                vertices.AddRange(new[] { a, b, c, d });
                if (triangles == top)
                {
                    foreach (var v in new[] { a, b, c, d })
                        uv.Add(eastWest ? new Vector2((v.z + radius) / size, (v.x + radius) / size) :
                            new Vector2((v.x + radius) / size, (v.z + radius) / size));
                }
                else uv.AddRange(new[] { Vector2.up, Vector2.zero, Vector2.right, Vector2.one });
                var faceNormal = Vector3.Cross(b - a, c - a) + Vector3.Cross(c - a, d - a);
                if (Vector3.Dot(faceNormal, normal) > 0)
                    triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
                else
                    triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
            }

            void Side(Vector3 a, Vector3 b, GridPosition offset)
            {
                var neighbor = p + offset;
                float Adjacent(Vector3 vertex) => Height(board, neighbor, (vertex.x + radius) / size - offset.X,
                    (vertex.z + radius) / size - offset.Z) - .01f;
                float adjacentA = Adjacent(a), adjacentB = Adjacent(b);
                float gapA = a.y - adjacentA, gapB = b.y - adjacentB;
                if (gapA <= .001f && gapB <= .001f) return;
                if (gapA < 0 || gapB < 0)
                {
                    var crossing = Vector3.Lerp(a, b, gapA / (gapA - gapB));
                    if (gapA < 0) { a = crossing; adjacentA = a.y; }
                    else { b = crossing; adjacentB = b.y; }
                }
                var lowerA = a; lowerA.y = adjacentA;
                var lowerB = b; lowerB.y = adjacentB;
                Face(walls, a, lowerA, lowerB, b, new Vector3(offset.X, 0, offset.Z));
            }

            for (int i = 0; i < halves; i++)
            {
                float start = (float)i / halves, end = (float)(i + 1) / halves;
                var nearStart = Point(start, 0);
                var nearEnd = Point(end, 0);
                var farStart = Point(start, 1);
                var farEnd = Point(end, 1);
                Face(top, nearStart, farStart, farEnd, nearEnd, Vector3.up);
                Side(nearStart, nearEnd, eastWest ? new GridPosition(0, -1) : new GridPosition(-1, 0));
                Side(farEnd, farStart, eastWest ? new GridPosition(0, 1) : new GridPosition(1, 0));
            }
            Side(Point(0, 1), Point(0, 0), eastWest ? new GridPosition(-1, 0) : new GridPosition(0, -1));
            Side(Point(1, 0), Point(1, 1), eastWest ? new GridPosition(1, 0) : new GridPosition(0, 1));
            var mesh = new Mesh { name = "BaseCrest_" + p };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(top, 0);
            mesh.SetTriangles(walls, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh Stairs(BattleBoard board,GridPosition p,int low)
        {
            const int treadCount=7;
            var cell=board.Cell(p);
            bool eastWest=cell.Ramp==RampAxis.EastWest;
            float size=BattleBoardView.CellSize,r=size*.5f;
            var vertices=new System.Collections.Generic.List<Vector3>();
            var uv=new System.Collections.Generic.List<Vector2>();
            var top=new System.Collections.Generic.List<int>();
            var walls=new System.Collections.Generic.List<int>();
            Vector3 Point(float side,float along,float height)
            {
                float x=low<0?along:1-along;
                return new Vector3((eastWest?x:side)*size-r,height,(eastWest?side:x)*size-r);
            }
            Vector2 TopUv(Vector3 v)=>new Vector2((v.x+r)/size,(v.z+r)/size);
            void Face(System.Collections.Generic.List<int> tris,Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 normal)
            {
                int i=vertices.Count;
                vertices.AddRange(new[]{a,b,c,d});
                if(tris==top)uv.AddRange(new[]{TopUv(a),TopUv(b),TopUv(c),TopUv(d)});
                else uv.AddRange(new[]{new Vector2(0,a.y/LevelHeight),new Vector2(1,b.y/LevelHeight),new Vector2(1,c.y/LevelHeight),new Vector2(0,d.y/LevelHeight)});
                if(Vector3.Dot(Vector3.Cross(b-a,c-a),normal)>0)tris.AddRange(new[]{i,i+1,i+2,i,i+2,i+3});
                else tris.AddRange(new[]{i,i+2,i+1,i,i+3,i+2});
            }
            float bottom=(cell.Elevation-1)*LevelHeight-.01f,previous=bottom;
            for(int i=0;i<treadCount;i++)
            {
                float a=(float)i/treadCount,b=(float)(i+1)/treadCount,h=bottom+i*LevelHeight/(treadCount-1);
                var left=Point(0,a,h);var right=Point(1,a,h);
                Face(top,left,right,Point(1,b,h),Point(0,b,h),Vector3.up);
                if(i>0)Face(walls,left,right,Point(1,a,previous),Point(0,a,previous),eastWest?Vector3.right*low:Vector3.forward*low);
                previous=h;
                for(int side=0;side<=1;side++)
                {
                    int sign=side==0?-1:1;
                    var offset=eastWest?new GridPosition(0,sign):new GridPosition(sign,0);
                    var neighbor=p+offset;
                    if(board.Cell(neighbor)?.Ramp==cell.Ramp&&board.Cell(neighbor).Elevation==cell.Elevation&&board.RampLowEdge(neighbor)==low)continue;
                    var start=Point(side,a,h);var end=Point(side,b,h);
                    float Adjacent(Vector3 v)=>Mathf.Min(h,Height(board,neighbor,(v.x+r)/size-offset.X,(v.z+r)/size-offset.Z)-.01f);
                    var lowerStart=start;lowerStart.y=Adjacent(start);var lowerEnd=end;lowerEnd.y=Adjacent(end);
                    if(h-lowerStart.y>.001f||h-lowerEnd.y>.001f)
                        Face(walls,start,end,lowerEnd,lowerStart,(eastWest?Vector3.forward:Vector3.right)*sign);
                }
            }
            var mesh=new Mesh{name="BaseSteps_"+p};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);
            mesh.subMeshCount=2;mesh.SetTriangles(top,0);mesh.SetTriangles(walls,1);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
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
