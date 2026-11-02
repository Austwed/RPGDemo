using UnityEngine;
using BaseConquest.Battle;

namespace BaseConquest.Presentation
{
    // The old resources remain available as fallbacks; only presentation reads this folder.
    public static class PixelDemoArt
    {
        private static readonly System.Collections.Generic.Dictionary<string,Texture2D> textures=new System.Collections.Generic.Dictionary<string,Texture2D>();
        // 骑乘身份：静态图集本身就是「马＋骑手」的那几个（判据是图集本身——正面剪影窄、侧面宽，即为马）。
        // 体量、可用的动作图集、名牌高度都按它判断。chief（大当家）是骑马的匪首，别漏。
        public static bool Mounted(string id)=>id=="azhu"||id=="cavalry"||id=="mounted-azhu"||id=="mounted-cavalry"||id=="chief";
        // 各图集里角色实际画了多少像素高（alpha 剪影中位数，从 Assets/Resources/PixelDemo/Characters 实测生成）。
        // 美术把角色身高以及同一角色待机／行走／攻击的尺寸都画得不一致（cavalry 待机 48px、攻击 42px，chief 55px），
        // 只按格子渲染会让同一个人换姿势就忽大忽小。渲染时统一归一到该角色 -v1 的身高，再乘骑乘系数。
        private static readonly System.Collections.Generic.Dictionary<string,float> ArtHeight=new System.Collections.Generic.Dictionary<string,float>
        {
            { "ahe|v1", 56f }, { "ahe|walk", 56f }, { "ahe|attack", 56f },
            { "archer|v1", 51f }, { "archer|attack", 55f },
            { "batu|v1", 57f }, { "batu|walk", 56f }, { "batu|attack", 56f },
            { "cavalry|v1", 48f }, { "cavalry|attack", 42f },
            { "mounted-cavalry|v1", 49f }, { "mounted-cavalry|idle", 57f }, { "mounted-cavalry|walk", 56f }, { "mounted-cavalry|attack", 53f },
            { "azhu|v1", 56f },
            { "mounted-azhu|v1", 56f }, { "mounted-azhu|walk", 57f }, { "mounted-azhu|attack", 56f },
            { "chief|v1", 55f },
            { "chenan|v1", 52f }, { "chenan|attack", 51f },
            { "chunshen|v1", 56f },
            { "controller|v1", 50f }, { "controller|attack", 53f },
            { "enemy-captain|v1", 55f }, { "enemy-captain|attack", 51f },
            { "female|v1", 56f }, { "female|idle", 56f }, { "female|walk", 56f }, { "female|attack", 56f },
            { "galan|v1", 56f }, { "galan|walk", 56f }, { "galan|attack", 55f },
            { "horn|v1", 56f },
            { "huoya|v1", 56f }, { "huoya|walk", 56f }, { "huoya|attack", 55f },
            { "infantry|v1", 53f }, { "infantry|attack", 49f },
            { "laozhou|v1", 56f }, { "merchant|v1", 56f }, { "merchant|walk", 56f },
            { "player|v1", 56f }, { "player|walk", 56f }, { "player|attack", 55f },
            { "villager|v1", 50f }, { "villager|attack", 55f },
        };
        public static float ArtHeightOf(string id,string kind)=>ArtHeight.TryGetValue(id+"|"+kind,out var px)&&px>0?px:56f;
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
        // 动作图集查找必须和 Load 用同一套别名：cavalry／azhu 只有骑乘版动作集，
        // 不归一就会查不到 walk，检查台把骑兵误判成「只能攻击」，也会在别处误判可用动作。
        public static string AtlasId(string id)=>id=="wilderness"?"enemy-captain":id=="azhu"?"mounted-azhu":id=="cavalry"?"mounted-cavalry":id;
        public static Texture2D Motion(string id,string motion)=>Resources.Load<Texture2D>("PixelDemo/Characters/"+AtlasId(id)+"-"+motion+"-v2");
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
        private readonly string artId,riderId;
        private readonly MaterialPropertyBlock ink=new MaterialPropertyBlock();
        public PixelCharacterSheet(Transform parent,Material source,Texture2D texture,Texture2D mounted=null,string identity="")
        {
            atlas=texture;mountedAtlas=mounted;
            // 阿铸日常也骑乘（规范 §2B 骑兵规则 1）：不能等调用方传 mounted，否则探索态回落到站立版。
            // 骑兵同理：cavalry 的静态图集已统一解析到 mounted-cavalry，不默认骑乘就会读到空的行走图集。
            alwaysMounted=identity=="azhu"||identity=="cavalry";
            // 静态图集本身就是马的身份（骑兵、骑乘阿铸、大当家）：任何时候都不该按步兵体量渲染。
            mountedArt=PixelDemoArt.Mounted(identity);
            artId=identity;
            riderId=identity=="azhu"?"mounted-azhu":identity=="cavalry"?"mounted-cavalry":identity;
            if(!string.IsNullOrEmpty(identity))
            {
                idleAtlas=PixelDemoArt.Motion(identity,"idle");walkAtlas=PixelDemoArt.Motion(identity,"walk");attackAtlas=PixelDemoArt.Motion(identity,"attack");downAtlas=PixelDemoArt.Motion(identity,"down");
                mountedIdleAtlas=PixelDemoArt.Motion(riderId,"idle");mountedWalkAtlas=PixelDemoArt.Motion(riderId,"walk");mountedAttackAtlas=PixelDemoArt.Motion(riderId,"attack");mountedDownAtlas=PixelDemoArt.Motion(riderId,"down");
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
            // 统一体量规则：
            // ① 每个姿势先归一到该角色 -v1 的身高——图集把同一角色画得忽大忽小（cavalry 待机 48px、攻击 42px），
            //    不归一就会「待机偏小、攻击正常」。倒地姿势是躺下的，不参与归一。
            // ② 骑乘类（马＋骑手）整体 ×1.45：美术把整匹马压进同一个 56px 格子，不放大就比站着的还矮。
            // 等比缩放，身高与横向面积一起长；网格自底边向上生长，放大后仍踩在同一接地点。
            string drawId=riding?riderId:artId;
            string kind=motion==null?"v1":pose==HeroPose.Attack?"attack":pose==HeroPose.Down?"down":pose==HeroPose.Walk?"walk":"idle";
            // 骑乘的基准不能用 -v1 实测：-v1 是设定图批次（mounted-cavalry 只有 49px），动作图集是 ComfyUI 批次（56px），
            // 拿 49 当基准会把动作帧压小 12.5%，反而破坏「骑马要高过步兵」。统一按标准 56px 基准归一，再乘骑乘倍数。
            float refPx=riding||mountedArt?56f:PixelDemoArt.ArtHeightOf(drawId,"v1");
            float drawPx=PixelDemoArt.ArtHeightOf(drawId,kind);
            float norm=pose==HeroPose.Down&&motion!=null?1f:refPx/drawPx;
            float body=norm*(riding||mountedArt?1.45f:1f);
            Root.transform.localScale=Vector3.one*(carried?.55f:body);
        }
        public void Dispose(){BattleBoardView.DestroyObject(Mesh);BattleBoardView.DestroyObject(material);}
    }
}
