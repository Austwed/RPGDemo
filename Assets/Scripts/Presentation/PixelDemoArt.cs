using UnityEngine;
using BaseConquest.Battle;

namespace BaseConquest.Presentation
{
    // The old resources remain available as fallbacks; only presentation reads this folder.
    public static class PixelDemoArt
    {
        private static readonly System.Collections.Generic.Dictionary<string,Texture2D> textures=new System.Collections.Generic.Dictionary<string,Texture2D>();
        // 士兵三职业的策划定名是 saber／archer／rider；图集文件名沿用旧批次（infantry／cavalry），
        // 这里统一归一，调用方一律用正式名。改文件名会牵动 .meta GUID 与所有 Resources 路径，没有收益。
        // 单位 id 规范（2026-11-02 定）：士兵单位是 `T-<职业>-<阵营>`（如 T-saber-royal），职业词不单独当单位名。
        // 这里把「职业词」和「T- 单位 id」都归一成图集文件名：saber→infantry、rider→cavalry、T-saber-*→infantry 等。
        public static string TroopId(string id)
        {
            if(string.IsNullOrEmpty(id))return id;
            if(BaseConquest.Battle.UnitNaming.TryParseTroop(id,out var classWord,out var _))id=classWord;
            return id=="saber"?"infantry":id=="rider"?"cavalry":id;
        }
        // 三职业角色：敌方队长也取这里的形象（galan=遗迹战、chief=匪寨战）。
        public static bool IsCast(string id)=>id=="player"||id=="female"||id=="ahe"||id=="azhu"||id=="mounted-azhu"||id=="galan"||id=="chief"||id=="batu"||id=="huoya";
        // 骑乘身份：静态图集本身就是「马＋骑手」的那几个（判据是图集本身——正面剪影窄、侧面宽，即为马）。
        // 体量、可用的动作图集、名牌高度都按它判断。chief（大当家）是骑马的匪首，别漏。
        public static bool Mounted(string id)=>id=="azhu"||TroopId(id)=="cavalry"||id=="mounted-azhu"||id=="mounted-cavalry"||id=="chief";
        // 各图集里角色实际画了多少像素高（alpha 剪影中位数，从 Assets/Resources/PixelDemo/Characters 实测生成）。
        // 美术把角色身高以及同一角色待机／行走／攻击的尺寸都画得不一致（cavalry 待机 48px、攻击 42px，chief 55px），
        // 只按格子渲染会让同一个人换姿势就忽大忽小。渲染时统一归一到该角色 -v1 的身高，再乘骑乘系数。
        private static readonly System.Collections.Generic.Dictionary<string,float> ArtHeight=new System.Collections.Generic.Dictionary<string,float>
        {
            { "ahe|v1", 56f }, { "ahe|walk", 56f }, { "ahe|attack", 56f },
            { "archer|v1", 51f }, { "archer|attack", 55f }, { "archer|walk", 55f },
            { "batu|v1", 57f }, { "batu|walk", 56f }, { "batu|attack", 56f },
            { "cavalry|v1", 48f }, { "cavalry|attack", 42f },
            { "mounted-cavalry|v1", 49f }, { "mounted-cavalry|idle", 57f }, { "mounted-cavalry|walk", 56f }, { "mounted-cavalry|attack", 53f },
            { "azhu|v1", 56f },
            { "mounted-azhu|v1", 56f }, { "mounted-azhu|walk", 57f }, { "mounted-azhu|attack", 56f },
            { "chief|v1", 55f }, { "chief|idle", 56f }, { "chief|walk", 56f },
            { "chenan|v1", 52f }, { "chenan|attack", 51f },
            { "baron|v1", 56f },
            { "chunshen|v1", 56f },
            { "enemy-captain|v1", 55f }, { "enemy-captain|attack", 51f }, { "enemy-captain|walk", 55f },
            { "female|v1", 56f }, { "female|walk", 56f }, { "female|attack", 56f },
            { "galan|v1", 56f }, { "galan|walk", 56f }, { "galan|attack", 55f },
            { "horn|v1", 56f },
            { "huoya|v1", 56f }, { "huoya|walk", 56f }, { "huoya|attack", 55f },
            { "infantry|v1", 53f }, { "infantry|attack", 49f }, { "infantry|walk", 51f },
            { "laozhou|v1", 56f }, { "merchant|v1", 56f }, { "merchant|walk", 56f },
            { "player|v1", 56f }, { "player|walk", 56f }, { "player|attack", 55f },
            { "villager|v1", 50f }, { "villager|attack", 55f },
        };
        public static float ArtHeightOf(string id,string kind)=>ArtHeight.TryGetValue(TroopId(id)+"|"+kind,out var px)&&px>0?px:56f;
        public static Texture2D Load(string category,string id)
        {
            if(string.IsNullOrEmpty(id))return null;
            id=TroopId(id);
            // 随机敌营已作废（2026-11-02），别名仅供旧数据读取。
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
        public static string AtlasId(string id)=>id=="wilderness"?"enemy-captain":id=="azhu"?"mounted-azhu":TroopId(id)=="cavalry"?"mounted-cavalry":TroopId(id);
        public static Texture2D Motion(string id,string motion)=>Resources.Load<Texture2D>("PixelDemo/Characters/"+AtlasId(id)+"-"+motion+"-v2");
        // 行为／表现解耦（2026-11-02 定）：能播什么动作只看这套贴图带没带对应动作图集，与单位类别无关。
        // 检查台按钮、动作能力标注一律用这两个查询，不再按「交互 NPC」类别硬关。
        public static bool HasMotion(string id,string motion)=>Motion(id,motion)!=null;
        public static bool HasAttack(string id)=>HasMotion(id,"attack");
        public static bool HasWalk(string id)=>HasMotion(id,"walk");
        // 图集规格说明（主角规格）：与图集同目录同名的 <id>-<motion>-v2.spec.json，由 杂项/工具/pack_hero_animation.py 生成。
        // 有它时格子取自图集、脚底与身高取自它；没有时按普通角色规则（格子仍取自图集尺寸，脚底按 FootPx 默认，身高查 ArtHeight）。
        // 主角图集身高 72px、格子更大，渲染时同样归一到 -v1 身高，所以屏幕上与其他角色一样高，只是像素更密。
        [System.Serializable] public sealed class AtlasSpec{public int cell,foot;public float body;}
        private static readonly System.Collections.Generic.Dictionary<string,AtlasSpec> specs=new System.Collections.Generic.Dictionary<string,AtlasSpec>();
        public static AtlasSpec Spec(string id,string motion)
        {
            if(string.IsNullOrEmpty(id)||string.IsNullOrEmpty(motion))return null;
            string path="PixelDemo/Characters/"+AtlasId(id)+"-"+motion+"-v2.spec";
            if(specs.TryGetValue(path,out var cached))return cached;
            var text=Resources.Load<TextAsset>(path);AtlasSpec spec=null;
            if(text!=null){try{spec=JsonUtility.FromJson<AtlasSpec>(text.text);}catch(System.ArgumentException){spec=null;}if(spec!=null&&spec.body<=0)spec=null;}
            specs[path]=spec;return spec;
        }
        // 地线（脚底下沿）在格子里的 y（自上而下）：待机／行走留 cell/16，攻击／倒地留 cell*3/32，即 64→60、96→90、128→116。
        public static float FootPx(string kind,float cell,AtlasSpec spec)=>spec!=null&&spec.foot>0?spec.foot:kind=="attack"||kind=="down"?cell-Mathf.Round(cell*3/32f):cell-Mathf.Floor(cell/16f);
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
        public PixelCharacterSheet(Transform parent,Material source,Texture2D texture,Texture2D mounted=null,string identity="",bool combatMotion=true,bool walkingMotion=true)
        {
            atlas=texture;mountedAtlas=mounted;
            // 阿铸日常也骑乘（规范 §2B 骑兵规则 1）：不能等调用方传 mounted，否则探索态回落到站立版。
            // 骑兵同理：cavalry 的静态图集已统一解析到 mounted-cavalry，不默认骑乘就会读到空的行走图集。
            alwaysMounted=identity=="azhu"||PixelDemoArt.TroopId(identity)=="cavalry";
            // 静态图集本身就是马的身份（骑兵、骑乘阿铸、大当家）：任何时候都不该按步兵体量渲染。
            mountedArt=PixelDemoArt.Mounted(identity);
            artId=identity;
            riderId=identity=="azhu"?"mounted-azhu":PixelDemoArt.TroopId(identity)=="cavalry"?"mounted-cavalry":identity;
            if(!string.IsNullOrEmpty(identity))
            {
                idleAtlas=PixelDemoArt.Motion(identity,"idle");if(walkingMotion)walkAtlas=PixelDemoArt.Motion(identity,"walk");if(combatMotion){attackAtlas=PixelDemoArt.Motion(identity,"attack");downAtlas=PixelDemoArt.Motion(identity,"down");}
                mountedIdleAtlas=PixelDemoArt.Motion(riderId,"idle");if(walkingMotion)mountedWalkAtlas=PixelDemoArt.Motion(riderId,"walk");if(combatMotion){mountedAttackAtlas=PixelDemoArt.Motion(riderId,"attack");mountedDownAtlas=PixelDemoArt.Motion(riderId,"down");}
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
            string drawId=riding?riderId:artId;
            string kind=motion==null?"v1":pose==HeroPose.Attack?"attack":pose==HeroPose.Down?"down":pose==HeroPose.Walk?"walk":"idle";
            var spec=motion==null?null:PixelDemoArt.Spec(drawId,kind);
            var uv=motion==null?PixelDemoArt.Frame(facing,pose,time):new Rect(column/(float)columns,1-((int)facing+1)/4f,1f/columns,.25f);
            Mesh.uv=new[]{new Vector2(uv.xMin,uv.yMin),new Vector2(uv.xMin,uv.yMax),new Vector2(uv.xMax,uv.yMax),new Vector2(uv.xMax,uv.yMin)};
            // 格子取自图集（四行方形格）：普通角色 64／128，主角按 spec；-v1 静态图集固定 64 格、地线 60。
            float size=motion==null?64:motion.height/4f,pivot=motion==null?60:PixelDemoArt.FootPx(kind,size,spec),scale=1.55f/56;
            Mesh.vertices=new[]{new Vector3(-size*.5f*scale,.28f,(pivot-size)*scale),new Vector3(-size*.5f*scale,.28f,pivot*scale),new Vector3(size*.5f*scale,.28f,pivot*scale),new Vector3(size*.5f*scale,.28f,(pivot-size)*scale)};Mesh.RecalculateBounds();
            ink.SetTexture("_BaseMap",motion??(riding?mountedAtlas:atlas));ink.SetColor("_BaseColor",hit?new Color(1,.35f,.35f):Color.white);Renderer.SetPropertyBlock(ink);
            Root.transform.localRotation=carried||pose==HeroPose.Down&&motion==null?Quaternion.Euler(0,90,0):Quaternion.identity;
            // 统一体量规则：
            // ① 每个姿势先归一到该角色 -v1 的身高——图集把同一角色画得忽大忽小（cavalry 待机 48px、攻击 42px），
            //    不归一就会「待机偏小、攻击正常」。倒地姿势是躺下的，不参与归一。
            // ② 骑乘类（马＋骑手）整体 ×1.45：美术把整匹马压进同一个 56px 格子，不放大就比站着的还矮。
            // 等比缩放，身高与横向面积一起长；网格自底边向上生长，放大后仍踩在同一接地点。
            // 骑乘的基准不能用 -v1 实测：-v1 是设定图批次（mounted-cavalry 只有 49px），动作图集是 ComfyUI 批次（56px），
            // 拿 49 当基准会把动作帧压小 12.5%，反而破坏「骑马要高过步兵」。统一按标准 56px 基准归一，再乘骑乘倍数。
            float refPx=riding||mountedArt?56f:PixelDemoArt.ArtHeightOf(drawId,"v1");
            // 主角规格图集的身高写在 spec 里（72px），倒地也按它归一（spec.body 是站姿身高）；普通倒地图集保持原样不归一。
            float drawPx=spec!=null?spec.body:PixelDemoArt.ArtHeightOf(drawId,kind);
            float norm=pose==HeroPose.Down&&motion!=null&&spec==null?1f:refPx/drawPx;
            float body=norm*(riding||mountedArt?1.45f:1f);
            Root.transform.localScale=Vector3.one*(carried?.55f:body);
        }
        public void Dispose(){BattleBoardView.DestroyObject(Mesh);BattleBoardView.DestroyObject(material);}
    }
}
