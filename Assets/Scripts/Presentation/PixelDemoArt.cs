using UnityEngine;
using BaseConquest.Battle;

namespace BaseConquest.Presentation
{
    // The old resources remain available as fallbacks; only presentation reads this folder.
    public static class PixelDemoArt
    {
        private static readonly System.Collections.Generic.Dictionary<string,Texture2D> textures=new System.Collections.Generic.Dictionary<string,Texture2D>();
        // 骑乘身份：静态图集本身就是「马＋骑手」的那几个。体量、可用的动作图集、名牌高度都按它判断。
        public static bool Mounted(string id)=>id=="azhu"||id=="cavalry"||id=="mounted-azhu"||id=="mounted-cavalry";
        public static Texture2D Load(string category,string id)
        {
            if(string.IsNullOrEmpty(id))return null;
            if(id=="wilderness")id="enemy-captain";
            else if(id=="portrait-wilderness")id="portrait-enemy-captain";
            // 阿铸只保留骑乘一套图集：所有调用方（含头像兜底、BattlePawn2D 的可用性判断）统一解析到 mounted-azhu，不再有站立版。
            else if(category=="Characters"&&id=="azhu")id="mounted-azhu";
            // 骑兵同理：cavalry-v1 画的是持矛步战，探索态与检查台只读 -v1，会把这个兵种显示成步兵。
            else if(category=="Characters"&&id=="cavalry")id="mounted-cavalry";
            string path="PixelDemo/"+category+"/"+id+"-v1";
            if(textures.TryGetValue(path,out var cached)&&cached!=null)return cached;
            var texture=Resources.Load<Texture2D>(path);if(texture!=null)textures[path]=texture;return texture;
        }
        public static Texture2D Building(string id)
        {
            string asset=id=="home"?"house":id=="government"?"camp-tent":id=="town_market"?"market-stall":id=="town_recruit"?"barracks":id=="town_quest"?"lord-house":id;
            return Load("Buildings",asset);
        }
        public static Texture2D Motion(string id,string motion)=>Resources.Load<Texture2D>("PixelDemo/Characters/"+(id=="wilderness"?"enemy-captain":id)+"-"+motion+"-v2");
        public static Rect Frame(HeroFacing facing,HeroPose pose,double time)
        {
            int column=pose==HeroPose.Walk?1+(int)(System.Math.Max(0,time)*8)%4:0;
            return new Rect(column/5f,1-((int)facing+1)/4f,1/5f,1/4f);
        }
        public static GameObject Prop(Transform parent,string id,Vector3 feet,Material source,float width,float height)
        {
            var texture=Load("Props",id);if(texture==null)return null;
            var go=GameObject.CreatePrimitive(PrimitiveType.Quad);go.name="PixelDemo_"+id;go.transform.SetParent(parent,false);BattleBoardView.DestroyObject(go.GetComponent<Collider>());
            go.transform.localPosition=feet+new Vector3(0,.28f,height*.5f-.06f);go.transform.localRotation=Quaternion.Euler(90,0,0);go.transform.localScale=new Vector3(width,height,1);
            var material=BattlePawn2D.Transparent(source);material.SetTexture("_BaseMap",texture);material.SetColor("_BaseColor",Color.white);go.GetComponent<Renderer>().sharedMaterial=material;
            go.AddComponent<MapNoticeResource>().OwnedMaterial=material;MapSpriteOrder.Apply(go.transform,go.transform.position.z-height*.5f);return go;
        }
    }

    // One sheet, four independently drawn directions. No weapon-hand mirroring.
    public sealed class PixelCharacterSheet : System.IDisposable
    {
        public readonly GameObject Root;
        public readonly Mesh Mesh;
        public readonly Renderer Renderer;
        private readonly Material material;
        private readonly Texture2D atlas,mountedAtlas,idleAtlas,walkAtlas,attackAtlas,downAtlas,mountedIdleAtlas,mountedWalkAtlas,mountedAttackAtlas,mountedDownAtlas;
        private readonly bool bowAttack,alwaysMounted,mountedArt;
        private readonly MaterialPropertyBlock ink=new MaterialPropertyBlock();
        public PixelCharacterSheet(Transform parent,Material source,Texture2D texture,Texture2D mounted=null,string identity="")
        {
            atlas=texture;mountedAtlas=mounted;
            // 阿铸日常也骑乘（规范 §2B 骑兵规则 1）：不能等调用方传 mounted，否则探索态回落到站立版。
            // 骑兵同理：cavalry 的静态图集已统一解析到 mounted-cavalry，不默认骑乘就会读到空的行走图集。
            alwaysMounted=identity=="azhu"||identity=="cavalry";
            // 静态图集本身就是马的身份（骑兵、骑乘阿铸）：任何时候都不该按步兵体量渲染。
            mountedArt=PixelDemoArt.Mounted(identity);
            if(!string.IsNullOrEmpty(identity))
            {
                idleAtlas=PixelDemoArt.Motion(identity,"idle");walkAtlas=PixelDemoArt.Motion(identity,"walk");attackAtlas=PixelDemoArt.Motion(identity,"attack");downAtlas=PixelDemoArt.Motion(identity,"down");
                string rider=identity=="azhu"?"mounted-azhu":identity=="cavalry"?"mounted-cavalry":identity;
                mountedIdleAtlas=PixelDemoArt.Motion(rider,"idle");mountedWalkAtlas=PixelDemoArt.Motion(rider,"walk");mountedAttackAtlas=PixelDemoArt.Motion(rider,"attack");mountedDownAtlas=PixelDemoArt.Motion(rider,"down");
                bowAttack=identity=="ahe";
            }
            Root=new GameObject("PixelDemoCharacter",typeof(MeshFilter),typeof(MeshRenderer));Root.transform.SetParent(parent,false);
            // 占位网格，真正每帧由 Set 按帧尺寸重建（体量差异在 Set 里按是否骑马处理）。
            const float unit=1.55f/56;
            Mesh=new Mesh{name="PixelDemo64Frame",vertices=new[]{new Vector3(-32*unit,.28f,-4*unit),new Vector3(-32*unit,.28f,60*unit),new Vector3(32*unit,.28f,60*unit),new Vector3(32*unit,.28f,-4*unit)},triangles=new[]{0,1,2,0,2,3}};
            Mesh.RecalculateNormals();Mesh.RecalculateBounds();Root.GetComponent<MeshFilter>().sharedMesh=Mesh;
            material=BattlePawn2D.Transparent(source);material.SetColor("_BaseColor",Color.white);Renderer=Root.GetComponent<Renderer>();Renderer.sharedMaterial=material;
            Set(HeroFacing.South,HeroPose.Idle,0,false,false,false);
        }
        public void Set(HeroFacing facing,HeroPose pose,double time,bool hit,bool carried,bool mounted,bool referenceIdle=false)
        {
            bool riding=(mounted||alwaysMounted)&&mountedAtlas!=null&&!carried&&(pose!=HeroPose.Down||mountedDownAtlas!=null);
            var idle=riding?mountedIdleAtlas:idleAtlas;
            var walk=riding?mountedWalkAtlas:walkAtlas;
            var motion=pose==HeroPose.Attack?(riding?mountedAttackAtlas:attackAtlas):pose==HeroPose.Down?(riding?mountedDownAtlas:downAtlas):pose==HeroPose.Walk?walk:idle;
            if(referenceIdle&&pose==HeroPose.Idle)motion=null;
            // 待机／受击直接走 -v1 参考帧（第 0 列）。不再回退到行走图集——行走帧是迈步中的姿势，脚会一直抬着。
            int columns=motion==null?4:motion.width/(motion.height/4);
            double phase=System.Math.Max(0,time);
            int column=0;
            if(pose==HeroPose.Attack)
            {
                if(columns==8)
                {
                    // Match the source strike/release to the existing combat hit, without delaying damage.
                    double impact=bowAttack?.24/(.24+.16):.22/(.22+.16);int contact=bowAttack?5:4;
                    column=phase+1e-6<impact?(int)(phase/impact*contact):contact+(int)((System.Math.Max(impact,System.Math.Min(1,phase))-impact)/(1-impact)*(columns-contact));
                }
                else column=(int)(phase*4);
                column=System.Math.Min(columns-1,column);
            }
            else if(pose==HeroPose.Down)column=carried?columns-1:System.Math.Min(columns-1,(int)(phase*8));
            else if(pose==HeroPose.Walk||pose==HeroPose.Idle&&idle!=null)column=(int)(phase*8)%columns;
            var uv=motion==null?PixelDemoArt.Frame(facing,pose,time):new Rect(column/(float)columns,1-((int)facing+1)/4f,1f/columns,.25f);
            Mesh.uv=new[]{new Vector2(uv.xMin,uv.yMin),new Vector2(uv.xMin,uv.yMax),new Vector2(uv.xMax,uv.yMax),new Vector2(uv.xMax,uv.yMin)};
            bool expanded=motion!=null&&(pose==HeroPose.Attack||pose==HeroPose.Down);float size=expanded?128:64,pivot=expanded?116:60,scale=1.55f/56;
            Mesh.vertices=new[]{new Vector3(-size*.5f*scale,.28f,(pivot-size)*scale),new Vector3(-size*.5f*scale,.28f,pivot*scale),new Vector3(size*.5f*scale,.28f,pivot*scale),new Vector3(size*.5f*scale,.28f,(pivot-size)*scale)};Mesh.RecalculateBounds();
            ink.SetTexture("_BaseMap",motion??(riding?mountedAtlas:atlas));ink.SetColor("_BaseColor",hit?new Color(1,.35f,.35f):Color.white);Renderer.SetPropertyBlock(ink);
            Root.transform.localRotation=carried||pose==HeroPose.Down&&motion==null?Quaternion.Euler(0,90,0):Quaternion.identity;
            // 骑乘图集要在同一个 64×64 格里塞下整匹马，实测四向剪影高度只有 44~56px（步兵 56px），
            // 按原尺寸渲染骑马反而比站着的矮。这里按「是否骑马」等比放大到 1.45 倍：身高与横向面积一起长，
            // 马不会被压扁；网格自底边向上生长，放大后仍踩在同一接地点。
            float body=riding||mountedArt?1.45f:1f;
            Root.transform.localScale=Vector3.one*(carried?.55f:body);
        }
        public void Dispose(){BattleBoardView.DestroyObject(Mesh);BattleBoardView.DestroyObject(material);}
    }
}
