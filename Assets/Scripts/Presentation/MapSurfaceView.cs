using UnityEngine;
using BaseConquest.Battle;
namespace BaseConquest.Presentation
{
    public static class MapSurfaceView
    {
        public static Texture2D Texture(GroundSurface kind)=>kind==GroundSurface.Cobble||kind==GroundSurface.DirtPath ? Resources.Load<Texture2D>("Environment/"+(kind==GroundSurface.Cobble?"cobble":"mud")) : PixelDemoArt.Load("Terrain",kind==GroundSurface.Grass?"grass":kind==GroundSurface.Sand?"sand":kind==GroundSurface.Road?"dirt-road":"")??Resources.Load<Texture2D>("MapSurfaces/"+(kind==GroundSurface.Grass ? "Grass-v1" : kind==GroundSurface.Sand ? "Sand-v1" : kind==GroundSurface.Road ? "Road-v1" : ""));
        public static Vector3 SignPosition(double x,double z,int width=24,int height=14)=>new Vector3(((float)x-width*.5f)*BattleBoardView.CellSize,0,((float)z-height*.5f)*BattleBoardView.CellSize);
        public static GameObject Notice(Transform parent,string name,Vector3 feet,Material source)
        {
            var texture=PixelDemoArt.Load("Props","noticeboard")??Resources.Load<Texture2D>("MapSurfaces/Notice-v1");
            var root=new GameObject(name);root.transform.SetParent(parent,false);root.transform.localPosition=feet;
            var face=GameObject.CreatePrimitive(PrimitiveType.Quad);face.name="单格告示牌 · 交互占位";face.transform.SetParent(root.transform,false);BattleBoardView.DestroyObject(face.GetComponent<Collider>());
            face.transform.localRotation=Quaternion.Euler(90,0,0);face.transform.localPosition=new Vector3(0,.32f,.48f);face.transform.localScale=Vector3.one*BattleBoardView.CellSize;
            var material=BattlePawn2D.Transparent(source);material.name="Noticeboard";material.SetTexture("_BaseMap",texture);material.SetColor("_BaseColor",Color.white);
            face.GetComponent<Renderer>().sharedMaterial=material;MapSpriteOrder.Apply(root.transform,root.transform.position.z);
            root.AddComponent<MapNoticeResource>().OwnedMaterial=material;return root;
        }
    }
    public sealed class MapNoticeResource : MonoBehaviour
    {
        [System.NonSerialized]public Material OwnedMaterial;
        private void OnDestroy()=>BattleBoardView.DestroyObject(OwnedMaterial);
    }
}
