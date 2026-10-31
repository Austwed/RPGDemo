using System.Collections.Generic;
using UnityEngine;
namespace BaseConquest.Battle
{
    // Flat map ink, not physical terrain height: combat coordinates and the camera remain 2D.
    public sealed class BattleElevationView
    {
        public readonly GameObject Root;
        private readonly BattleBoard board;
        private readonly Mesh[] meshes=new Mesh[2];
        public BattleElevationView(BattleBoard board,Transform parent,Material material)
        {
            this.board=board;Root=new GameObject("高度标记 · 崖边与台阶");Root.transform.SetParent(parent,false);
            for(int i=0;i<2;i++)
            {
                var obj=new GameObject(i==0 ? "崖壁暗边" : "台阶与高地边沿",typeof(MeshFilter),typeof(MeshRenderer));obj.transform.SetParent(Root.transform,false);
                meshes[i]=new Mesh{name=obj.name};obj.GetComponent<MeshFilter>().sharedMesh=meshes[i];obj.GetComponent<MeshRenderer>().sharedMaterial=material;
                var ink=new MaterialPropertyBlock();ink.SetColor("_BaseColor",i==0 ? new Color(.16f,.12f,.075f) : new Color(.78f,.68f,.43f));obj.GetComponent<Renderer>().SetPropertyBlock(ink);
            }
        }
        public void Rebuild(bool[] known=null)
        {
            var verts=new[]{new List<Vector3>(),new List<Vector3>()};var indices=new[]{new List<int>(),new List<int>()};
            bool Known(GridPosition p)=>board.Contains(p)&&(known==null||known[p.Z*board.Width+p.X]);
            void Line(int layer,Vector3 a,Vector3 b,float width)
            {
                var side=Vector3.Cross((b-a).normalized,Vector3.up)*width*.5f;int n=verts[layer].Count;
                verts[layer].Add(a-side);verts[layer].Add(a+side);verts[layer].Add(b+side);verts[layer].Add(b-side);
                indices[layer].AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
            }
            foreach(var p in board.Positions())
            {
                if(!Known(p))continue;var cell=board.Cell(p);var center=BattleBoardView.World(p,board)+Vector3.up*.145f;float r=BattleBoardView.CellSize*.5f;
                foreach(var d in new[]{new GridPosition(1,0),new GridPosition(-1,0),new GridPosition(0,1),new GridPosition(0,-1)})
                {
                    var next=p+d;if(!Known(next)||cell.Elevation<=board.Cell(next).Elevation||board.HeightConnection(p,next))continue;
                    var edge=center+new Vector3(d.X,0,d.Z)*r;var tangent=new Vector3(-d.Z,0,d.X)*r;
                    Line(0,edge-tangent,edge+tangent,.15f);var upper=edge-new Vector3(d.X,0,d.Z)*.095f+Vector3.up*.003f;Line(1,upper-tangent,upper+tangent,.035f);
                }
                if(cell.Ramp==RampAxis.None||!cell.Walkable)continue;
                bool horizontal=cell.Ramp==RampAxis.EastWest;
                for(int step=0;step<5;step++)
                {
                    float offset=-.42f+step*.21f;var c=center+(horizontal ? Vector3.right : Vector3.forward)*offset;
                    var tangent=(horizontal ? Vector3.forward : Vector3.right)*.43f;Line(0,c-tangent,c+tangent,.065f);Line(1,c-tangent+Vector3.up*.004f,c+tangent+Vector3.up*.004f,.026f);
                }
            }
            for(int i=0;i<2;i++){meshes[i].Clear();meshes[i].SetVertices(verts[i]);meshes[i].SetTriangles(indices[i],0);meshes[i].RecalculateBounds();}
        }
        public void Dispose(){foreach(var mesh in meshes)BattleBoardView.DestroyObject(mesh);BattleBoardView.DestroyObject(Root);}
    }
}
