using System;
using System.Collections.Generic;
using UnityEngine;
using BaseConquest.Battle;
namespace BaseConquest.Presentation
{
    // Cutout gait shares the protagonist's timing and opposing leg motion, never mirrors a weapon.
    public sealed class CompanionSpriteView : MonoBehaviour
    {
        private readonly List<Mesh> meshes=new List<Mesh>();
        private readonly List<Renderer> surfaces=new List<Renderer>();
        private Transform rig;
        private Material humanMaterial,horseMaterial;
        private MaterialPropertyBlock ink;
        private CommanderArt art;
        private PixelCharacterSheet pixel;
        private Texture2D atlas,horse;
        private Vector3 previous;
        private bool hasPrevious;
        private bool isMounted;
        private bool horseLoaded;
        private double distance;
        private double realtimeDownAt=double.NaN;
        private bool realtimeObserved;
        private double battleDownAt=double.NaN;
        private HeroFacing facing=HeroFacing.South;
        private HeroFacing defaultFacing=HeroFacing.South;
        private double lookUntil;
        private bool looking,wasNear;
        public HeroFacing Facing=>facing;
        private HeroFacing previewFacing=HeroFacing.South;
        public void Preview(HeroPose pose,double time,HeroFacing? face=null){if(face.HasValue)previewFacing=face.Value;if(pixel!=null)pixel.Set(previewFacing,pose,pose==HeroPose.Attack?time%1:time,false,false,false,true);}
        /// <summary>
        /// 只把朝向转向观察者（主角），不动正在播放的动作：美术检查台既要看动作循环，也要看四向朝向。
        /// </summary>
        public HeroFacing LookTowards(Vector3 feet,Vector3 observer)
        {
            var d=new Vector2(observer.x-feet.x,observer.z-feet.z);
            if(d.sqrMagnitude<=4*BattleBoardView.CellSize*BattleBoardView.CellSize+.0001f)previewFacing=PlayerHeroFrames.FaceTowards(d.x,d.y,previewFacing);
            return previewFacing;
        }
        private float stride;
        public string AppearanceId {get;private set;}
        public bool Initialize(string id,Material source)
        {
            var recommended=PixelDemoArt.Load("Characters",id);
            if(recommended!=null){AppearanceId=id;string mountId=id=="azhu"?"mounted-azhu":PixelDemoArt.TroopId(id)=="cavalry"?"mounted-cavalry":"";pixel=new PixelCharacterSheet(transform,source,recommended,PixelDemoArt.Load("Characters",mountId),id);return true;}
            art=CommanderArt.For(id);if(art==null)return false;
            atlas=Resources.Load<Texture2D>("Characters/"+art.TextureName);if(atlas==null)return false;
            AppearanceId=id;ink=new MaterialPropertyBlock();
            humanMaterial=BattlePawn2D.Transparent(source);humanMaterial.SetTexture("_BaseMap",atlas);humanMaterial.mainTexture=atlas;humanMaterial.SetColor("_BaseColor",Color.white);
            rig=new GameObject("四向人物 · 独立步态").transform;rig.SetParent(transform,false);
            for(int i=0;i<5;i++)AddPart(i==0 ? "固定上身与武器" : i==1 ? "左腿" : i==2 ? "右腿" : i==3 ? "武器下缘" : "倒地整身",humanMaterial);
            if(id=="azhu")EnsureHorse();
            Set(HeroPose.Idle,0,false,false,false);return true;
        }
        private void EnsureHorse()
        {
            if(horseLoaded)return;horseLoaded=true;
                horse=Resources.Load<Texture2D>("Characters/RidingHorse-v1");
                if(horse!=null)
                {
                    horseMaterial=BattlePawn2D.Transparent(humanMaterial);horseMaterial.SetTexture("_BaseMap",horse);horseMaterial.mainTexture=horse;horseMaterial.SetColor("_BaseColor",Color.white);
                    for(int i=0;i<5;i++)AddPart(i==0 ? "战马躯干" : "独立马腿"+i,horseMaterial);
                }
        }
        private void AddPart(string name,Material material)
        {
            var obj=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));obj.transform.SetParent(rig,false);
            var mesh=new Mesh{name="CommanderPart"+meshes.Count};obj.GetComponent<MeshFilter>().sharedMesh=mesh;meshes.Add(mesh);
            var renderer=obj.GetComponent<Renderer>();renderer.sharedMaterial=material;surfaces.Add(renderer);
        }
        public void Exploration(Vector3 feet,Vector3? observer=null,double now=0,bool autonomous=false)
        {
            var delta=hasPrevious ? feet-previous : Vector3.zero;previous=feet;hasPrevious=true;
            bool moving=delta.sqrMagnitude>.000001f;if(moving)distance+=new Vector2(delta.x,delta.z).magnitude;
            if(moving)defaultFacing=PlayerHeroFrames.Face(delta.x,delta.z,defaultFacing);
            bool near=observer.HasValue&&new Vector2(observer.Value.x-feet.x,observer.Value.z-feet.z).sqrMagnitude<=4*BattleBoardView.CellSize*BattleBoardView.CellSize+.0001f;
            if(moving||autonomous){facing=defaultFacing;looking=wasNear=false;lookUntil=0;}
            else if(near){facing=PlayerHeroFrames.FaceTowards(observer.Value.x-feet.x,observer.Value.z-feet.z,facing);looking=true;}
            else
            {
                if(looking&&wasNear)lookUntil=now+3;
                if(!looking||now>=lookUntil){looking=false;facing=defaultFacing;}
            }
            wasNear=near;
            Set(moving||autonomous ? HeroPose.Walk : HeroPose.Idle,moving||autonomous?distance/(BattleBoardView.CellSize*4):Time.unscaledTime,false,false,false);
            SortRig(MapSpriteOrder.At(feet.z));
        }
        public void Battle(BattleSimulation battle,BattleSoldier unit,float progress,bool carried)
        {
            var heading=unit.LastAction==RoundAction.Attack||unit.LastAction==RoundAction.Skill ? unit.LastTargetCell : unit.Cell;
            facing=PlayerHeroFrames.Face(heading.X-unit.PreviousCell.X,heading.Z-unit.PreviousCell.Z,facing);
            if(unit.LastAction==RoundAction.Move&&unit.LastMovePath.Count>1)
            {
                int step=Mathf.Min(Mathf.FloorToInt(Mathf.SmoothStep(0,1,progress)*(unit.LastMovePath.Count-1)),unit.LastMovePath.Count-2);
                var a=unit.LastMovePath[step];var b=unit.LastMovePath[step+1];facing=PlayerHeroFrames.Face(b.X-a.X,b.Z-a.Z,facing);
            }
            var pose=unit.IsDowned||!unit.IsAlive ? HeroPose.Down : unit.LastAction==RoundAction.Move&&progress<1 ? HeroPose.Walk : (unit.LastAction==RoundAction.Attack||unit.LastAction==RoundAction.Skill)&&progress<1 ? HeroPose.Attack : HeroPose.Idle;
            // 倒地贴图按真实时间播完一遍倒地动画后定格在最后一列（完全躺平），不随回合进度回退。
            if(pose==HeroPose.Down){if(double.IsNaN(battleDownAt))battleDownAt=Time.unscaledTimeAsDouble;}
            else battleDownAt=double.NaN;
            var time=pose==HeroPose.Down?double.IsNaN(battleDownAt)?0:System.Math.Min(1,(Time.unscaledTimeAsDouble-battleDownAt)/.8):progress*Mathf.Max(1,unit.LastMovePath.Count-1)*.5;
            Set(pose,time,BattleSoldier.DamageFlashVisible(unit.LastDamageTaken,progress),carried,unit.CombatKind==SoldierKind.Cavalry);
        }
        public void Realtime(BaseConquest.RealtimePrototype.PrototypeUnit actor,double seconds,bool mounted)
        {
            bool attacking=actor.SwingUntil>seconds;var heading=attacking?actor.AttackFacing:actor.Facing;
            facing=PlayerHeroFrames.Face(heading.X,heading.Z,facing);
            var pose=actor.Health<=0||actor.KnockedDown?HeroPose.Down:attacking?HeroPose.Attack:actor.Moving&&!actor.SteeringStopped?HeroPose.Walk:HeroPose.Idle;
            double windup=actor.Ranged?.24:.22;
            double time=pose==HeroPose.Attack?(actor.RpgWindup?actor.AttackPoseTime(seconds,windup/(windup+.16)):Mathf.Clamp01((float)((seconds-actor.HitAt+windup)/(windup+.16)))):pose==HeroPose.Walk?actor.DistanceTravelled/4:seconds;
            if(pose==HeroPose.Down)
            {
                if(double.IsNaN(realtimeDownAt))realtimeDownAt=realtimeObserved?seconds:seconds-1;
                time=Math.Max(0,seconds-realtimeDownAt);
            }
            else realtimeDownAt=double.NaN;
            realtimeObserved=true;
            Set(pose,time,actor.FlashUntil>seconds,false,mounted);
        }
        private void Quad(int index,RectInt clip,Texture texture,Vector2 pivot,float scale,float z,float angle=0,float stretch=1)
        {
            var mesh=meshes[index];float x0=(clip.x-pivot.x)*scale,x1=(clip.xMax-pivot.x)*scale,bottom=(pivot.y-clip.yMax)*scale,top=(pivot.y-clip.y)*scale;
            mesh.vertices=new[]{new Vector3(x0,.28f,bottom),new Vector3(x0,.28f,top),new Vector3(x1,.28f,top),new Vector3(x1,.28f,bottom)};
            mesh.triangles=new[]{0,1,2,0,2,3};var uv=CommanderArt.UV(clip,texture);
            mesh.uv=new[]{new Vector2(uv.xMin,uv.yMin),new Vector2(uv.xMin,uv.yMax),new Vector2(uv.xMax,uv.yMax),new Vector2(uv.xMax,uv.yMin)};mesh.RecalculateBounds();
            var t=surfaces[index].transform;t.localPosition=new Vector3(0,0,z);t.localRotation=Quaternion.Euler(0,angle,0);t.localScale=new Vector3(1,1,stretch);surfaces[index].enabled=clip.width>0&&clip.height>0;surfaces[index].SetPropertyBlock(ink);
        }
        private void Set(HeroPose pose,double time,bool hit,bool carried,bool mounted)
        {
            if(pixel!=null){pixel.Set(facing,pose,time,hit,carried,mounted);return;}
            if(mounted)EnsureHorse();
            int f=(int)facing;stride=pose==HeroPose.Walk ? PlayerHeroWalkRig.Stride(time) : 0;mounted&=horse!=null&&pose!=HeroPose.Down&&!carried;
            isMounted=mounted;
            foreach(var r in surfaces)r.enabled=false;
            ink.SetColor("_BaseColor",hit ? new Color(1,.35f,.35f) : Color.white);
            rig.localScale=Vector3.one*(carried ? .55f : 1);rig.localRotation=Quaternion.identity;
            if(pose==HeroPose.Down||carried)
            {
                var full=art.Clips[f];Quad(4,full,atlas,new Vector2(full.x+full.width*.5f,full.yMax),1.55f/full.height,0);
                rig.localRotation=Quaternion.Euler(0,90,0);rig.localScale=new Vector3(carried ? .55f : 1,1,carried ? .55f : .6f);return;
            }
            var torso=art.Clips[4+f];torso.height=art.TorsoBottom[f]-torso.y;
            float bodyScale=art.PixelScale*(mounted ? .75f : 1),hips=mounted ? 1.08f : .62f;
            float lean=pose==HeroPose.Attack ? Mathf.Sin((float)time*Mathf.PI*2)*6 : 0;
            Quad(0,torso,atlas,art.Waist[f],bodyScale,hips,lean);
            if(art.WeaponEnds!=null&&art.WeaponEnds[f].width>0)Quad(3,art.WeaponEnds[f],atlas,art.Waist[f],bodyScale,hips,lean);
            for(int i=1;i<=2;i++)
            {
                var leg=art.Clips[(i+1)*4+f];float length=mounted ? .44f : .62f;
                Quad(i,leg,atlas,new Vector2(leg.x+leg.width*.5f,leg.y),length/leg.height,hips,mounted ? 0 : PlayerHeroWalkRig.LegAngle(i,facing,stride),mounted ? 1 : PlayerHeroWalkRig.LegLength(i,facing,stride));
                surfaces[i].transform.localPosition+=new Vector3(PlayerHeroWalkRig.HipX(i,facing),0,0);
            }
            if(!mounted)return;
            bool side=facing==HeroFacing.West||facing==HeroFacing.East;
            var trunk=CommanderArt.HorseClips[f];Quad(5,trunk,horse,new Vector2(trunk.x+trunk.width*.5f,trunk.yMax-18),.0042f,.55f);
            for(int i=0;i<4;i++)
            {
                var leg=CommanderArt.HorseClips[(i+1)*4+f];float phase=i==0||i==3 ? stride : -stride;
                Quad(6+i,leg,horse,CommanderArt.HorseLegPivots[i*4+f],.58f/leg.height,.64f,side ? phase*24 : phase*4,side ? 1 : 1+phase*.15f);
                float x=side ? (i<2 ? -.22f : .46f)*(facing==HeroFacing.West ? 1 : -1) : i%2==0 ? -.13f : .13f;
                surfaces[6+i].transform.localPosition+=new Vector3(x,0,i<2 ? 0 : .03f);
            }
        }
        public void SortRig(int order)
        {
            if(pixel!=null){pixel.Renderer.sortingOrder=order;return;}
            if(surfaces.Count==0)return;
            for(int i=0;i<surfaces.Count;i++)surfaces[i].sortingOrder=order+(i<5 ? i==1||i==2 ? (i==1)==(stride>=0) ? 2 : 1 : 3 : i==5 ? 1 : 0);
            if(isMounted){for(int i=0;i<4;i++)surfaces[i].sortingOrder=order+3;}
        }
        private void OnDestroy()
        {pixel?.Dispose();foreach(var mesh in meshes)BattleBoardView.DestroyObject(mesh);BattleBoardView.DestroyObject(humanMaterial);BattleBoardView.DestroyObject(horseMaterial);}
    }
}
