using System;
using System.Linq;
using UnityEngine;
using BaseConquest.Base;
using BaseConquest.Battle;
namespace BaseConquest.Presentation
{
    [Serializable] public sealed class SpriteMetric
    {
        public string Name,Mask;public float U,V,Width=1,Height=1,Aspect=1,RootX=.5f,RootY=0,ShadowHeight=1;
        [NonSerialized]private byte[] alpha;
        public Rect UV=>new Rect(U,V,Width,Height);
        public float Sample(float x,float y)
        {if(x<0||y<0||x>=1||y>=1)return 0;alpha??=Convert.FromBase64String(Mask);return alpha[(31-Mathf.Clamp((int)(y*32),0,31))*32+Mathf.Clamp((int)(x*32),0,31)]/255f;}
    }
    public static class SpriteMetrics
    {
        [Serializable]private sealed class Document{public SpriteMetric[] Entries=Array.Empty<SpriteMetric>();}
        private static Document document;
        public static SpriteMetric For(Texture2D texture)
        {if(texture==null)return null;document??=JsonUtility.FromJson<Document>(Resources.Load<TextAsset>("Environment/SpriteMetrics").text);return document.Entries.FirstOrDefault(v=>v.Name==texture.name);}
        public static bool Building(string id)=>id!=null&&(id.StartsWith("house-")||id.StartsWith("shop-")||id=="lord-manor");
    }
    // The ground-contact anchor is independent of the visible roof and of walking collision.
    public sealed class SpriteFacade:MonoBehaviour
    {
        public Renderer Surface{get;private set;}public Vector3 Feet{get;private set;}public float VisibleWidth{get;private set;}public float VisibleHeight{get;private set;}
        private Camera camera;private Material material;private SpriteMetric metric;private float raisedFeetY;
        public static SpriteFacade Create(Transform parent,Texture2D texture,Vector3 feet,float width,Material source,Camera camera)
        {
            var root=new GameObject("UprightFacade",typeof(SpriteFacade));root.transform.SetParent(parent,false);var view=root.GetComponent<SpriteFacade>();view.Feet=feet;view.raisedFeetY=feet.y;view.camera=camera;view.VisibleWidth=width;
            var metric=SpriteMetrics.For(texture);view.metric=metric;view.VisibleHeight=width/(metric?.Aspect??(texture.width/(float)texture.height));
            var face=GameObject.CreatePrimitive(PrimitiveType.Quad);face.transform.SetParent(root.transform,false);BattleBoardView.DestroyObject(face.GetComponent<Collider>());
            face.transform.localScale=new Vector3(width,view.VisibleHeight,1);view.material=BattlePawn2D.Transparent(source);view.material.SetTexture("_BaseMap",texture);view.material.SetColor("_BaseColor",Color.white);
            if(metric!=null){view.material.SetTextureScale("_BaseMap",new Vector2(metric.Width,metric.Height));view.material.SetTextureOffset("_BaseMap",new Vector2(metric.U,metric.V));}
            view.Surface=face.GetComponent<Renderer>();view.Surface.sharedMaterial=view.material;view.Refresh();return view;
        }
        public void Refresh()
        {if(camera==null)return;Surface.transform.position=Feet+camera.transform.right*(VisibleWidth*(.5f-(metric?.RootX??.5f)))+camera.transform.up*(VisibleHeight*(.5f-(metric?.RootY??0)));Surface.transform.rotation=camera.transform.rotation;MapSpriteOrder.Apply(transform,Feet.z);}
        public void SetRaised(bool raised){Feet=new Vector3(Feet.x,raised?raisedFeetY:0,Feet.z);Refresh();}
        private void LateUpdate()=>Refresh();
        private void OnDestroy()=>BattleBoardView.DestroyObject(material);
    }
}
