using UnityEngine;
using BaseConquest.Battle;
namespace BaseConquest.Presentation
{
    public sealed class PlayerHeroView : MonoBehaviour
    {
        private Mesh mesh;
        private Material material;
        private Renderer surface;
        private MaterialPropertyBlock ink;
        private Texture2D atlas;
        private PlayerHeroWalkRig walkRig;
        private PixelCharacterSheet pixel;
        private string appearanceId;
        private Texture2D benchAtlas;
        internal bool CanSitOnBench => pixel != null &&
            (benchAtlas ??= PixelDemoArt.Motion(appearanceId, "sit")) != null;
        public float LeftStride=>walkRig?.LeftStride??0;
        public float RightStride=>walkRig?.RightStride??0;
        public void SortRig(int order)=>walkRig?.Sort(order);
        private double walkDistance;
        private Vector3 previous;
        private bool hasPrevious,wasDown;
        private float downProgress;
        private double downShownAt=double.NaN;
        private double realtimeDownAt=double.NaN;
        private bool realtimeObserved;
        public HeroFacing Facing {get;private set;}=HeroFacing.South;
        public HeroPose Pose {get;private set;}
        public int AtlasFrame {get;private set;}
        public Vector3 HeadPoint=>pixel!=null?pixel.Root.transform.TransformPoint(new Vector3(0,.28f,50f*1.55f/56)):transform.TransformPoint(new Vector3(0,.28f,1.38f));
        internal Vector3 VisualTop => pixel?.VisualTop ?? HeadPoint;
        public bool Initialize(Material source,string appearance="player")
        {
            if(pixel!=null)return true;
            appearanceId=appearance;
            var recommended=PixelDemoArt.Load("Characters",appearance);
            if(recommended!=null){pixel=new PixelCharacterSheet(transform,source,recommended,null,appearance);Set(HeroPose.Idle,0,false,false);return true;}
            if(mesh!=null)return true;
            atlas=Resources.Load<Texture2D>("Characters/PlayerHero-v1");if(atlas==null)return false;
            var parts=Resources.Load<Texture2D>("Characters/PlayerHeroParts-v3");if(parts!=null)walkRig=new PlayerHeroWalkRig(transform,source,parts);
            // Native Unity objects must be created by initialization on the main thread,
            // never by a MonoBehaviour constructor or instance field initializer.
            ink=new MaterialPropertyBlock();
            material=BattlePawn2D.Transparent(source);material.name="主角 · 四向手绘图集";material.SetTexture("_BaseMap",atlas);material.mainTexture=atlas;material.SetColor("_BaseColor",Color.white);
            var obj=new GameObject("主角形象 · 待机／行走／攻击／受击／倒地",typeof(MeshFilter),typeof(MeshRenderer));obj.transform.SetParent(transform,false);
            mesh=new Mesh{name="PlayerHeroAtlasQuad"};mesh.vertices=new[]{new Vector3(-.725f,.27f,-.09f),new Vector3(-.725f,.27f,1.36f),new Vector3(.725f,.27f,1.36f),new Vector3(.725f,.27f,-.09f)};mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateNormals();mesh.RecalculateBounds();
            obj.GetComponent<MeshFilter>().sharedMesh=mesh;surface=obj.GetComponent<MeshRenderer>();surface.sharedMaterial=material;Set(HeroPose.Idle,0,false,false);return true;
        }
        public void Exploration(Vector3 feet,HeroFacing? facing=null,float? benchProgress=null,bool standingUp=false)
        {
            var delta=hasPrevious ? feet-previous : Vector3.zero;hasPrevious=true;previous=feet;
            if(benchProgress.HasValue&&CanSitOnBench)
            {
                Facing=HeroFacing.South;
                Pose=standingUp?HeroPose.StandingUp:benchProgress.Value>=1?HeroPose.Seated:HeroPose.Sitting;
                AtlasFrame=Mathf.RoundToInt(Mathf.Clamp01(benchProgress.Value)*3);
                pixel.BenchFrame(benchAtlas,AtlasFrame);
                return;
            }
            if(Pose==HeroPose.Sitting||Pose==HeroPose.Seated||Pose==HeroPose.StandingUp)delta=Vector3.zero;
            var groundDelta=new Vector2(delta.x,delta.z);
            bool moving=groundDelta.sqrMagnitude>.000001f;
            if(moving)walkDistance+=groundDelta.magnitude;
            Facing=facing??PlayerHeroFrames.FaceWalking(delta.x,delta.z,Facing);
            Set(moving ? HeroPose.Walk : HeroPose.Idle,moving ? walkDistance/(BattleBoardView.CellSize*4) : Time.unscaledTime,false,false);
        }
        internal void HoldSelectionPose()
        {
            Facing = HeroFacing.South;
            Set(HeroPose.Idle, 0, false, false, referenceIdle: true);
        }
        public void Battle(BattleSimulation battle,BattleSoldier unit,float progress,bool carried)
        {
            var heading=unit.LastAction==RoundAction.Attack||unit.LastAction==RoundAction.Skill ? unit.LastTargetCell : unit.Cell;
            bool walking=unit.LastAction==RoundAction.Move&&unit.CombatKind!=SoldierKind.Cavalry;
            Facing=walking ? PlayerHeroFrames.FaceWalking(heading.X-unit.PreviousCell.X,heading.Z-unit.PreviousCell.Z,Facing)
                : PlayerHeroFrames.Face(heading.X-unit.PreviousCell.X,heading.Z-unit.PreviousCell.Z,Facing);
            if(unit.LastAction==RoundAction.Move&&unit.LastMovePath.Count>1)
            {
                int step=Mathf.Min(Mathf.FloorToInt(Mathf.SmoothStep(0,1,progress)*(unit.LastMovePath.Count-1)),unit.LastMovePath.Count-2);
                var a=unit.LastMovePath[step];var b=unit.LastMovePath[step+1];
                Facing=walking ? PlayerHeroFrames.FaceWalking(b.X-a.X,b.Z-a.Z,Facing) : PlayerHeroFrames.Face(b.X-a.X,b.Z-a.Z,Facing);
            }
            bool down=unit.IsDowned||!unit.IsAlive;if(down&&!wasDown)downProgress=0;if(down)downProgress=Mathf.Max(downProgress,progress);wasDown=down;
            bool hit=BattleSoldier.DamageFlashVisible(unit.LastDamageTaken,progress);
            var pose=down ? HeroPose.Down : unit.LastDamageTaken>0&&progress>=.85f&&progress<1 ? HeroPose.Hurt : unit.LastAction==RoundAction.Move&&progress<1 ? HeroPose.Walk : (unit.LastAction==RoundAction.Attack||unit.LastAction==RoundAction.Skill)&&progress<1 ? HeroPose.Attack : HeroPose.Idle;
            // 倒地贴图按真实时间播完一遍倒地动画后定格在最后一列（完全躺平），不随回合进度回退；
            // 原 downProgress 映射上限 0.5，8 列图集只播到第 5 帧，人还悬在半倒姿势。
            if(pose==HeroPose.Down){if(double.IsNaN(downShownAt))downShownAt=Time.unscaledTimeAsDouble;}
            else downShownAt=double.NaN;
            double downTime=double.IsNaN(downShownAt)?0:System.Math.Min(1,(Time.unscaledTimeAsDouble-downShownAt)/.8);
            double time=down ? carried ? 1 : downTime : pose==HeroPose.Hurt ? Mathf.Clamp01((progress-.85f)/.15f)*.249 : pose==HeroPose.Attack ? progress : pose==HeroPose.Walk ? progress*Mathf.Max(1,unit.LastMovePath.Count-1)*.5 : Time.unscaledTime;
            Set(pose,time,hit,carried);
        }
        public void Realtime(BaseConquest.RealtimePrototype.PrototypeUnit actor,double seconds)
        {
            bool attacking=actor.SwingUntil>seconds;var heading=attacking?actor.AttackFacing:actor.Facing;
            var visual=actor.FootprintLength==2?actor.BodyAxis:heading;
            var pose=actor.Health<=0||actor.KnockedDown?HeroPose.Down:attacking?HeroPose.Attack:actor.Moving&&!actor.SteeringStopped?HeroPose.Walk:HeroPose.Idle;
            Facing=pose==HeroPose.Walk&&actor.FootprintLength!=2&&actor.JumpDuration<=0
                ? PlayerHeroFrames.FaceWalking(visual.X,visual.Z,Facing) : PlayerHeroFrames.Face(visual.X,visual.Z,Facing);
            double windup=actor.Ranged?.24:.22;
            double time=pose==HeroPose.Down?1:pose==HeroPose.Attack?(actor.RpgWindup?actor.AttackPoseTime(seconds,windup/(windup+.16)):Mathf.Clamp01((float)((seconds-actor.HitAt+windup)/(windup+.16)))):pose==HeroPose.Walk?actor.DistanceTravelled/4:seconds;
            if(pose==HeroPose.Down)
            {
                if(double.IsNaN(realtimeDownAt))realtimeDownAt=realtimeObserved?seconds:seconds-1;
                time=System.Math.Max(0,seconds-realtimeDownAt);
            }
            else realtimeDownAt=double.NaN;
            realtimeObserved=true;
            Set(pose,time,actor.FlashUntil>seconds,false);
        }
        private void Set(HeroPose pose,double time,bool hit,bool carried,bool referenceIdle=false)
        {
            Pose=pose;AtlasFrame=PlayerHeroFrames.Frame(pose,Facing,time);
            if(pixel!=null){pixel.Set(Facing,pose,time,hit,carried,false,referenceIdle);return;}
            bool rigged=(pose==HeroPose.Walk||pose==HeroPose.Idle)&&walkRig!=null;if(walkRig!=null){walkRig.Root.SetActive(rigged);if(rigged)walkRig.Set(pose==HeroPose.Walk ? time : 0,Facing,hit);}surface.enabled=!rigged;
            int row=AtlasFrame/8,col=AtlasFrame%8;float left=col/8f,right=(col+1)/8f,bottom=1-(row+1)/8f,top=1-row/8f;
            mesh.uv=new[]{new Vector2(left,bottom),new Vector2(left,top),new Vector2(right,top),new Vector2(right,bottom)};
            ink.SetTexture("_BaseMap",atlas);ink.SetColor("_BaseColor",hit ? new Color(1,.35f,.35f) : Color.white);surface.SetPropertyBlock(ink);surface.transform.localScale=Vector3.one*(carried ? .55f : 1);
        }
        private void OnDestroy(){pixel?.Dispose();walkRig?.Dispose();BattleBoardView.DestroyObject(mesh);BattleBoardView.DestroyObject(material);}
    }
}
