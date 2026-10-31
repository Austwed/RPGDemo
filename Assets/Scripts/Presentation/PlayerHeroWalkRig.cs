using UnityEngine;
using BaseConquest.Battle;
namespace BaseConquest.Presentation
{
    // Cutout animation: sword/arms/torso never mirror or swap between gait phases.
    public sealed class PlayerHeroWalkRig
    {
        public readonly GameObject Root;
        private readonly Mesh[] meshes=new Mesh[3];
        private readonly Renderer[] renderers=new Renderer[3];
        private readonly Transform[] parts=new Transform[3];
        private readonly Material material;
        private readonly MaterialPropertyBlock ink=new MaterialPropertyBlock();
        private readonly Texture2D atlas;
        public float LeftStride {get;private set;}
        public float RightStride=>-LeftStride;
        public static float Stride(double time)=>Mathf.Sin((float)(time*4*System.Math.PI));
        // Source rectangles were inspected from the generated part atlas, no bitmap mirroring.
        private static readonly RectInt[] clips={
            new RectInt(26,46,332,372),new RectInt(380,75,327,330),new RectInt(741,87,340,308),new RectInt(1098,49,330,362),
            new RectInt(174,517,72,212),new RectInt(484,522,101,207),new RectInt(864,521,101,209),new RectInt(1202,519,74,209),
            new RectInt(147,810,80,217),new RectInt(491,810,91,215),new RectInt(865,810,93,215),new RectInt(1221,808,77,217)};
        public static Rect UV(int part,HeroFacing facing,Texture texture)
        {var r=clips[part*4+(int)facing];return new Rect(r.x/(float)texture.width,1-r.yMax/(float)texture.height,r.width/(float)texture.width,r.height/(float)texture.height);}
        public static Vector2 Size(int part,HeroFacing facing)
        {var r=clips[part*4+(int)facing];float height=part==0 ? 1.14f : .62f;return new Vector2(height*r.width/r.height,height);}
        public static float HipX(int part,HeroFacing facing)
        {bool side=facing==HeroFacing.West||facing==HeroFacing.East;return part==1 ? side ? -.025f : .105f : side ? .025f : -.105f;}
        public static float LegAngle(int part,HeroFacing facing,float stride)
        {float phase=part==1 ? stride : -stride;return (facing==HeroFacing.West||facing==HeroFacing.East) ? phase*30 : phase*5;}
        public static float LegLength(int part,HeroFacing facing,float stride)
        {float phase=part==1 ? stride : -stride;return facing==HeroFacing.South||facing==HeroFacing.North ? 1+phase*.18f : 1-Mathf.Max(0,phase)*.12f;}
        public PlayerHeroWalkRig(Transform parent,Material source,Texture2D texture)
        {
            atlas=texture;Root=new GameObject("主角步态 · 固定持剑侧／独立左右腿");Root.transform.SetParent(parent,false);
            material=BattlePawn2D.Transparent(source);material.SetTexture("_BaseMap",texture);material.SetColor("_BaseColor",Color.white);
            for(int i=0;i<3;i++)
            {
                var obj=new GameObject(i==0 ? "固定上身与右手剑" : i==1 ? "独立左腿" : "独立右腿",typeof(MeshFilter),typeof(MeshRenderer));obj.transform.SetParent(Root.transform,false);parts[i]=obj.transform;
                meshes[i]=new Mesh{name="HeroRigPart_"+i};obj.GetComponent<MeshFilter>().sharedMesh=meshes[i];renderers[i]=obj.GetComponent<Renderer>();renderers[i].sharedMaterial=material;
            }
            Set(0,HeroFacing.South,false);
        }
        public void Set(double time,HeroFacing facing,bool hit)
        {
            LeftStride=Stride(time);ink.SetColor("_BaseColor",hit ? new Color(1,.35f,.35f) : Color.white);
            for(int i=0;i<3;i++)
            {
                var size=Size(i,facing);float bottom=i==0 ? .34f : -.62f;var rect=UV(i,facing,atlas);float width=size.x*.5f;
                meshes[i].vertices=new[]{new Vector3(-width,.28f,bottom),new Vector3(-width,.28f,bottom+size.y),new Vector3(width,.28f,bottom+size.y),new Vector3(width,.28f,bottom)};meshes[i].triangles=new[]{0,1,2,0,2,3};
                meshes[i].uv=new[]{new Vector2(rect.xMin,rect.yMin),new Vector2(rect.xMin,rect.yMax),new Vector2(rect.xMax,rect.yMax),new Vector2(rect.xMax,rect.yMin)};meshes[i].RecalculateBounds();
                parts[i].localPosition=i==0 ? Vector3.zero : new Vector3(HipX(i,facing),0,.62f);parts[i].localRotation=Quaternion.Euler(0,i==0 ? 0 : LegAngle(i,facing,LeftStride),0);parts[i].localScale=new Vector3(1,1,i==0 ? 1 : LegLength(i,facing,LeftStride));renderers[i].SetPropertyBlock(ink);
            }
        }
        public void Sort(int order){renderers[0].sortingOrder=order+3;renderers[1].sortingOrder=order+(LeftStride>=0 ? 2:1);renderers[2].sortingOrder=order+(LeftStride>=0 ? 1:2);}
        public void Dispose(){foreach(var mesh in meshes)BattleBoardView.DestroyObject(mesh);BattleBoardView.DestroyObject(material);BattleBoardView.DestroyObject(Root);}
    }
}
