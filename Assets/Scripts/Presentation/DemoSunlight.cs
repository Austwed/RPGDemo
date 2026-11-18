using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using BaseConquest.Base;
using BaseConquest.Battle;
namespace BaseConquest.Presentation
{
    [Serializable]public sealed class DemoSunSettings
    {
        public bool Enabled=true;public float DirectionX=1,DirectionZ=-1,Projection=.65f,Ambient=1,ShadowOpacity=.25f,ShadeBrightness=.8f;
        public Color SunColor=new Color(1,.98f,.94f,1),ShadowColor=new Color(.13f,.12f,.1f,1);
        // Feet shadows are a contact-shadow convention, not lighting: they stay on when Enabled is false.
        public bool FeetShadow=true;public float FeetShadowRadius=.3f,FeetShadowOpacity=.2f;
    }
    public sealed class DemoSunlight:MonoBehaviour
    {
        public DemoSunSettings Settings;public BaseSceneController Owner{get;private set;}public bool Outdoors{get;private set;}private int boardWidth,boardHeight,children;
        private Texture2D shadowTexture;private Material shadowMaterial;private Mesh shadowMesh;private byte[] shade;private int width,height;private string tacticalKey;private Transform field;private bool shadeBuilt;
        private const int Density=16;private float nextScan;
        public static DemoSunlight Create(BaseSceneController owner,Transform parent)
        {
            var root=new GameObject("DemoSunlight",typeof(DemoSunlight));root.transform.SetParent(parent,false);var light=root.GetComponent<DemoSunlight>();light.Owner=owner;
            light.Settings=JsonUtility.FromJson<DemoSunSettings>(Resources.Load<TextAsset>("Environment/Sunlight").text);light.Refresh();return light;
        }
        public void Refresh()
        {
            if(Owner.State==null)return;var b=BaseBattlefield.Board(Owner.State);boardWidth=b.Width;boardHeight=b.Height;Outdoors=BaseSpatial.Layout(Owner.State)?.SurfaceStyle!="dungeon";width=b.Width*Density;height=b.Height*Density;shade=new byte[width*height];
            Owner.InvalidateObjects();var objects=Owner.MapObjects;var sources=new List<(BaseMapObject Obj,Texture2D Texture,float Width,float Height)>();
            var catalog=Resources.Load<BuildingAppearanceCatalog>("BuildingAppearances");
            foreach(var o in objects)
            {
                if(o.Kind=="wildlife"||o.Kind=="tactical-trunk"||!o.Blocks)continue;
                var d=BaseEnvironment.Find(o.AssetId);bool tree=o.AssetId?.StartsWith("tree-")==true;bool building=SpriteMetrics.Building(o.AssetId)||!BaseEnvironment.Decorative(o)&&o.Kind!="npc"&&o.AssetId?.StartsWith("farm")!=true;
                Texture2D t=tree||SpriteMetrics.Building(o.AssetId)?Resources.Load<Texture2D>("Environment/"+o.AssetId):o.Kind=="tactical-tree"?PixelDemoArt.Load("Props","dead-tree"):building?catalog?.Find(string.IsNullOrEmpty(o.AssetId)?o.Id:o.AssetId)?.Texture:null;
                var metric=SpriteMetrics.For(t);if(metric==null)continue;float w=(float)(d?.ArtWidth??o.Width)*BattleBoardView.CellSize;float h=building?w/metric.Aspect:(float)(d?.ArtHeight??2.5)*BattleBoardView.CellSize;
                sources.Add((o,t,w,h));
            }
            if(Outdoors&&Settings.Enabled)foreach(var source in sources)
            {
                var o=source.Obj;float x=(float)o.X,z=(float)(o.Y-o.Height/2);var metric=SpriteMetrics.For(source.Texture);float w=source.Width/BattleBoardView.CellSize,h=source.Height/BattleBoardView.CellSize*Settings.Projection*metric.ShadowHeight;
                var dir=new Vector2(Settings.DirectionX,Settings.DirectionZ).normalized*h;if(Mathf.Abs(dir.y)<.01f)continue;
                int x0=Mathf.Clamp(Mathf.FloorToInt((x-w*metric.RootX+Mathf.Min(0,dir.x))*Density),0,width-1),x1=Mathf.Clamp(Mathf.CeilToInt((x+w*(1-metric.RootX)+Mathf.Max(0,dir.x))*Density),0,width-1);
                int z0=Mathf.Clamp(Mathf.FloorToInt((z+Mathf.Min(0,dir.y))*Density),0,height-1),z1=Mathf.Clamp(Mathf.CeilToInt((z+Mathf.Max(0,dir.y))*Density),0,height-1);
                for(int iz=z0;iz<=z1;iz++)for(int ix=x0;ix<=x1;ix++)
                {float v=((iz+.5f)/Density-z)/dir.y,u=((ix+.5f)/Density-x-dir.x*v)/w+metric.RootX;float a=metric.Sample(u,v);int index=iz*width+ix;shade[index]=(byte)Mathf.Max(shade[index],a*255);}
            }
            shadeBuilt=Outdoors&&Settings.Enabled;
            if(field!=null)BattleBoardView.DestroyObject(field.gameObject);BattleBoardView.DestroyObject(shadowTexture);BattleBoardView.DestroyObject(shadowMaterial);BattleBoardView.DestroyObject(shadowMesh);
            field=null;shadowTexture=null;shadowMaterial=null;shadowMesh=null;
            // Lighting is off by default (2026-11-01). Skip the projection entirely instead of building it and hiding it.
            if(shadeBuilt)
            {
            field=new GameObject("GroundSunProjection",typeof(MeshFilter),typeof(MeshRenderer)).transform;field.SetParent(transform,false);shadowTexture=new Texture2D(width,height,TextureFormat.RGBA32,false){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            var pixels=new Color32[shade.Length];var tint=Settings.ShadowColor;for(int i=0;i<pixels.Length;i++)pixels[i]=new Color(tint.r,tint.g,tint.b,shade[i]/255f*Settings.ShadowOpacity);shadowTexture.SetPixels32(pixels);shadowTexture.Apply(false,true);
            shadowMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit"));shadowMaterial=MakeTransparent(shadowMaterial);shadowMaterial.SetTexture("_BaseMap",shadowTexture);shadowMaterial.SetColor("_BaseColor",Color.white);
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            foreach(var p in b.Positions())
            {
                if(!b.Cell(p).Walkable)continue;int start=vertices.Count;
                foreach(var d in new[]{new Vector2(0,0),new Vector2(0,1),new Vector2(1,1),new Vector2(1,0)})
                {var local=new Vector3((p.X+d.x-b.Width*.5f)*BattleBoardView.CellSize,BaseTerrainView.Height(b,p,d.x,d.y)+.055f,(p.Z+d.y-b.Height*.5f)*BattleBoardView.CellSize);vertices.Add(local);uv.Add(new Vector2((p.X+d.x)/b.Width,(p.Z+d.y)/b.Height));}
                triangles.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
            }
            shadowMesh=new Mesh{name="SunGroundProjection"};shadowMesh.SetVertices(vertices);shadowMesh.SetUVs(0,uv);shadowMesh.SetTriangles(triangles,0);shadowMesh.RecalculateBounds();field.GetComponent<MeshFilter>().sharedMesh=shadowMesh;field.GetComponent<Renderer>().sharedMaterial=shadowMaterial;field.GetComponent<Renderer>().sortingOrder=-28500;field.gameObject.SetActive(true);
            }
            tacticalKey=Key();BindArt();
        }
        private string Key()=>string.Join("|",(BaseSpatial.Layout(Owner.State)?.TacticalObjects??new List<BattleTacticalObject>()).Select(o=>o.Id+":"+o.Fallen+":"+o.X+":"+o.Y+":"+string.Join(",",o.Trunk)));
        public void Tick(){if(Owner.State==null)return;if(!Owner.DeploymentWorldActive&&tacticalKey!=Key())Refresh();if(children!=Owner.transform.childCount)BindArt();}
        private void LateUpdate(){if(Time.unscaledTime<nextScan)return;nextScan=Time.unscaledTime+.25f;Tick();}
        public void BindArt()
        {
            children=Owner.transform.childCount;
            // Static scenery only needs a SunInfluence while lighting is on; characters always need one for the feet shadow.
            if(Settings.Enabled)
            {
                foreach(var facade in Owner.GetComponentsInChildren<SpriteFacade>(true))SunInfluence.Attach(facade.Surface,this,facade.transform,false,facade.Feet);
                foreach(var decor in Owner.GetComponentsInChildren<EnvironmentDecorView>(true))foreach(var renderer in decor.GetComponentsInChildren<Renderer>(true))if(renderer.GetComponentInParent<SpriteFacade>()==null)SunInfluence.Attach(renderer,this,decor.transform,false,decor.transform.position);
            }
            foreach(var pawn in Owner.GetComponentsInChildren<BattlePawn2D>(true))foreach(var renderer in pawn.GetComponentsInChildren<MeshRenderer>(true))if(renderer.gameObject.name=="PixelDemoCharacter"||renderer.gameObject.name.StartsWith("主角形象"))SunInfluence.Attach(renderer,this,pawn.transform,true,Vector3.zero);
        }
        public float Brightness(Vector3 point)
        {if(!Outdoors||!Settings.Enabled)return 1;int x=Mathf.FloorToInt((point.x/BattleBoardView.CellSize+boardWidth*.5f)*Density),z=Mathf.FloorToInt((point.z/BattleBoardView.CellSize+boardHeight*.5f)*Density);return shade!=null&&x>=0&&z>=0&&x<width&&z<height&&shade[z*width+x]>127?Settings.Ambient*Settings.ShadeBrightness:Settings.Ambient;}
        public Color DayColor=>Outdoors&&Settings.Enabled?Settings.SunColor:Color.white;
        internal static Material MakeTransparent(Material original){var result=BattlePawn2D.Transparent(original);BattleBoardView.DestroyObject(original);return result;}
        private void OnDestroy(){BattleBoardView.DestroyObject(shadowTexture);BattleBoardView.DestroyObject(shadowMaterial);BattleBoardView.DestroyObject(shadowMesh);}
    }
    public sealed class SunInfluence:MonoBehaviour
    {
        private Renderer surface;private DemoSunlight sun;private Transform anchor;private Vector3 fixedPoint;private bool moving;private Material blobMaterial;private GameObject blob;private Mesh blobMesh;private MaterialPropertyBlock block;
        public static void Attach(Renderer surface,DemoSunlight sun,Transform anchor,bool moving,Vector3 fixedPoint)
        {
            var existing=surface.GetComponent<SunInfluence>();if(existing!=null){existing.Refresh();return;}var component=surface.gameObject.AddComponent<SunInfluence>();component.surface=surface;component.sun=sun;component.anchor=anchor;component.moving=moving;component.fixedPoint=fixedPoint;
            // With lighting off there is nothing to shade, so leave the sprite on its plain unlit material.
            if(sun.Settings.Enabled)surface.sharedMaterial.shader=Resources.Load<Shader>("Presentation/SunSprite");
            if(moving)component.CreateBlob();component.Refresh();
        }
        private void CreateBlob()
        {
            blob=new GameObject("FeetShadow",typeof(MeshFilter),typeof(MeshRenderer));blob.transform.SetParent(anchor,false);blobMesh=new Mesh();var verts=new List<Vector3>{Vector3.zero};var tris=new List<int>();
            float r=Mathf.Max(.05f,sun.Settings.FeetShadowRadius);
            for (int n = 0; n <= 32; n++)
                verts.Add(new Vector3(Mathf.Cos(n * Mathf.PI / 16) * r, 0, Mathf.Sin(n * Mathf.PI / 16) * r * .58f));
            for (int n = 1; n <= 32; n++) tris.AddRange(new[] { 0, n, n + 1 });
            blobMesh.SetVertices(verts);blobMesh.SetTriangles(tris,0);blobMesh.RecalculateBounds();blob.GetComponent<MeshFilter>().sharedMesh=blobMesh;
            blobMaterial=DemoSunlight.MakeTransparent(new Material(Shader.Find("Universal Render Pipeline/Unlit")));blobMaterial.SetColor("_BaseColor",new Color(.13f,.12f,.1f,sun.Settings.FeetShadowOpacity));blob.GetComponent<Renderer>().sharedMaterial=blobMaterial;
        }
        public void Refresh()
        {
            if(sun==null||surface==null)return;block??=new MaterialPropertyBlock();var point=moving?anchor.position:fixedPoint;
            if(sun.Settings.Enabled){surface.GetPropertyBlock(block);block.SetFloat("_SunFactor",sun.Brightness(point));block.SetColor("_SunTint",sun.DayColor);surface.SetPropertyBlock(block);}
            // Contact shadow is a readability convention, not lighting: it stays on when the sun is off.
            if(blob!=null){blob.SetActive(sun.Outdoors&&sun.Settings.FeetShadow&&surface.enabled&&surface.gameObject.activeInHierarchy);blob.transform.position=anchor.position+new Vector3(0,.025f,-.13f);blob.transform.rotation=Quaternion.identity;blob.transform.localScale=Vector3.one;blob.GetComponent<Renderer>().sortingOrder=-28000;}
        }
        private void LateUpdate(){if(moving)Refresh();}
        private void OnDestroy(){BattleBoardView.DestroyObject(blobMaterial);BattleBoardView.DestroyObject(blobMesh);if(blob!=null)BattleBoardView.DestroyObject(blob);}
    }
}
