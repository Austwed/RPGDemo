using System.Collections.Generic;
using BaseConquest.Battle;
using UnityEngine;

namespace BaseConquest.Presentation
{
    // Presentation only: one conforming quad per occupied coarse cell.
    public sealed class FieldworkArtView
    {
        private readonly BattleBoard board;
        private readonly Transform root;
        private readonly Material trap,sandbag;
        private readonly Dictionary<int,(GameObject Object,Mesh Mesh)> items=new Dictionary<int,(GameObject,Mesh)>();
        public FieldworkArtView(BattleBoard board,Transform parent,Material source)
        {
            this.board=board;root=new GameObject("FieldworkArt").transform;root.SetParent(parent,false);
            Material Create(string id)
            {
                var texture=PixelDemoArt.Load("Buildings",id);if(texture==null)return null;
                var material=BattlePawn2D.Transparent(source);material.name="Fieldwork_"+id;material.SetTexture("_BaseMap",texture);material.SetColor("_BaseColor",Color.white);return material;
            }
            trap=Create("trap-bear");sandbag=Create("sandbag-row-3");
        }
        public void Rebuild(bool combat,bool raised)
        {
            foreach(var item in items.Values)item.Object.SetActive(false);
            foreach(var p in board.Positions())
            {
                var cell=board.Cell(p);bool isTrap=cell.HiddenTrap&&!combat;bool isSandbag=cell.SandbagGroup>0;
                var material=isTrap?trap:isSandbag?sandbag:null;if(material==null)continue;
                int index=p.Z*board.Width+p.X;
                if(!items.TryGetValue(index,out var item))
                {
                    var go=new GameObject("Fieldwork_"+p.X+"_"+p.Z,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(root,false);
                    var mesh=new Mesh{name=go.name};go.GetComponent<MeshFilter>().sharedMesh=mesh;item=(go,mesh);items.Add(index,item);
                }
                float r=BattleBoardView.CellSize*.5f;
                float H(float x,float z)=>raised?BaseTerrainView.Height(board,p,x,z)+.045f:.045f;
                item.Mesh.Clear();item.Mesh.vertices=new[]{new Vector3(-r,H(0,0),-r),new Vector3(-r,H(0,1),r),new Vector3(r,H(1,1),r),new Vector3(r,H(1,0),-r)};
                int segment=0;if(isSandbag)for(int x=p.X-1;x>=0&&board.Cell(new GridPosition(x,p.Z)).SandbagGroup==cell.SandbagGroup;x--)segment++;
                float u0=isSandbag?Mathf.Clamp(segment,0,2)/3f:0,u1=isSandbag?(Mathf.Clamp(segment,0,2)+1)/3f:1;
                item.Mesh.uv=new[]{new Vector2(u0,0),new Vector2(u0,1),new Vector2(u1,1),new Vector2(u1,0)};
                item.Mesh.triangles=new[]{0,1,2,0,2,3};item.Mesh.RecalculateNormals();item.Mesh.RecalculateBounds();
                item.Object.transform.localPosition=BattleBoardView.World(p,board);var renderer=item.Object.GetComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.sortingOrder=-31000;item.Object.SetActive(true);
            }
        }
        public void Dispose(){foreach(var item in items.Values)BattleBoardView.DestroyObject(item.Mesh);BattleBoardView.DestroyObject(trap);BattleBoardView.DestroyObject(sandbag);BattleBoardView.DestroyObject(root.gameObject);}
    }
}
