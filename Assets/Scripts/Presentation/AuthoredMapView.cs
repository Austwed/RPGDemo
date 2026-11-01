using BaseConquest.Base;
using BaseConquest.Battle;
using UnityEngine;

namespace BaseConquest.Presentation
{
    public sealed class AuthoredMapView : MonoBehaviour
    {
        private bool person;
        private bool shaped;
        private Vector3 feet;
        private Material ownedMaterial;
        private bool farmVisual;
        private string farmAppearance;
        private HeroPose? previewPose;
        // 最近一次已知的主角位置，供循环动作的对象也朝向他。
        public static Vector3? AttentionSource;
        public void Preview(HeroPose pose){previewPose=pose;}
        // 美术检查台的循环动作不看 RefreshAttention（那会走战斗姿态），这里单独让它们朝向主角。
        private void Update()
        {
            if(!previewPose.HasValue)return;
            var sprite=GetComponent<CompanionSpriteView>();if(sprite==null)return;
            var now=Time.unscaledTimeAsDouble;
            HeroFacing? face=person&&AttentionSource.HasValue?sprite.LookTowards(feet,AttentionSource.Value):(HeroFacing?)null;
            sprite.Preview(previewPose.Value,now,face);
        }
        public static string FarmAppearance(BaseState state) => state != null && BaseDailyDelta.Read(state).Food < 0 ? "farm-immature" : "farm-mature";
        public static AuthoredMapView Create(Transform parent,BaseMapObject placement,Vector3 feet,Material source,bool exploration)
        {
            var root=new GameObject(placement.Id+" · "+placement.Name);root.transform.SetParent(parent,false);root.transform.localPosition=feet;
            var view=root.AddComponent<AuthoredMapView>();view.person=placement.Kind=="npc";view.feet=feet;
            view.shaped=!string.IsNullOrEmpty(placement.Shape);
            if(BaseEnvironment.Decorative(placement)){view.shaped=true;EnvironmentDecorView.Attach(root,placement,source);return view;}
            string prop=placement.AssetId=="dungeon-entrance"?"dungeon-entrance":placement.Kind=="pixel-prop"?placement.AssetId:placement.Kind=="tactical-rock"?"boulder":placement.Kind=="tactical-tree"?"dead-tree":placement.Kind=="tactical-trunk"?"fallen-log":placement.AssetId=="demo-broken-sword"?"broken-sword":placement.AssetId=="demo-long-sword"?"longsword":placement.AssetId=="demo-debris"?"ruined-wall":null;
            var pixelProp=prop==null?null:PixelDemoArt.Prop(root.transform,prop,Vector3.zero,source,prop=="dungeon-entrance"?3:prop=="dead-tree"?1.25f:1.5f,prop=="dungeon-entrance"?3:prop=="dead-tree"?2.5f:prop=="ruined-wall"?.75f:1.5f);
            if(pixelProp!=null){view.shaped=false;}
            else if(placement.Kind?.StartsWith("tactical-")==true||placement.Kind=="demo-prop")
            {
                view.shaped=true;view.ownedMaterial=new Material(source);view.ownedMaterial.SetTexture("_BaseMap",Texture2D.whiteTexture);view.ownedMaterial.SetColor("_BaseColor",Color.white);
                void Part(string name,PrimitiveType type,Vector3 position,Vector3 scale,Color color)
                {var piece=GameObject.CreatePrimitive(type);piece.name=name;piece.transform.SetParent(root.transform,false);piece.transform.localPosition=position;piece.transform.localScale=scale;BattleBoardView.DestroyObject(piece.GetComponent<Collider>());var renderer=piece.GetComponent<Renderer>();renderer.sharedMaterial=view.ownedMaterial;var ink=new MaterialPropertyBlock();ink.SetColor("_BaseColor",color);renderer.SetPropertyBlock(ink);}
                if(placement.Kind=="tactical-rock")Part("巨石",PrimitiveType.Sphere,Vector3.up*.5f,Vector3.one,new Color(.48f,.5f,.52f));
                else if(placement.Kind=="tactical-tree")Part("枯树",PrimitiveType.Cube,Vector3.up*.8f,new Vector3(.4f,1.6f,.4f),new Color(.39f,.24f,.12f));
                else if(placement.Kind=="tactical-trunk")Part("倒木",PrimitiveType.Cube,Vector3.up*.18f,new Vector3(1.1f,.35f,1.1f),new Color(.32f,.2f,.11f));
                else if(placement.AssetId=="demo-debris")
                {for(int n=0;n<4;n++)Part("破损残片",PrimitiveType.Cube,new Vector3((n-1.5f)*.33f,.14f,n%2*.2f),new Vector3(.4f,.28f,.3f),new Color(.17f,.14f,.12f));}
                else
                {
                    bool broken=placement.AssetId=="demo-broken-sword";
                    Part("剑柄",PrimitiveType.Cube,new Vector3(0,.14f,-.5f),new Vector3(.12f,.12f,.4f),new Color(.25f,.13f,.07f));Part("王室式护手",PrimitiveType.Cube,new Vector3(0,.14f,-.22f),new Vector3(.58f,.12f,.12f),new Color(.65f,.5f,.2f));
                    Part("剑身",PrimitiveType.Cube,new Vector3(0,.14f,broken?-.02f:.4f),new Vector3(.18f,.08f,broken?.3f:1.1f),new Color(.76f,.79f,.77f));
                    if(broken)Part("断裂剑尖",PrimitiveType.Cube,new Vector3(.25f,.13f,.48f),new Vector3(.16f,.08f,.5f),new Color(.56f,.58f,.55f));
                }
            }
            else if(view.shaped)
            {
                view.ownedMaterial=BattlePawn2D.Transparent(source);view.ownedMaterial.name="Footprint_"+placement.Id;
                view.ownedMaterial.SetTexture("_BaseMap",Texture2D.whiteTexture);view.ownedMaterial.SetColor("_BaseColor",new Color(.65f,.54f,.32f));
                foreach(var cell in BaseObjectFootprint.Cells(placement))
                {
                    var tile=GameObject.CreatePrimitive(PrimitiveType.Quad);tile.name="占地示意_"+cell.X+"_"+cell.Z;tile.transform.SetParent(root.transform,false);BattleBoardView.DestroyObject(tile.GetComponent<Collider>());
                    tile.transform.localRotation=Quaternion.Euler(90,0,0);tile.transform.localPosition=new Vector3((float)(cell.X+.5-placement.X)*BattleBoardView.CellSize,.04f,(float)(cell.Z+.5-placement.Y)*BattleBoardView.CellSize);
                    tile.transform.localScale=new Vector3(.96f*BattleBoardView.CellSize,.96f*BattleBoardView.CellSize,1);tile.GetComponent<Renderer>().sharedMaterial=view.ownedMaterial;
                }
            }
            else if(view.person)
            {
                var pawn=root.AddComponent<BattlePawn2D>();
                pawn.Initialize(new BattleSoldier {Kind=SoldierKind.Hero,Stats=new SoldierStats {Health=100},Health=100},source);
                pawn.EnableCompanion(placement.AssetId);pawn.SyncExploration(feet,Color.white);
            }
            else
            {
                var catalog=Resources.Load<BuildingAppearanceCatalog>("BuildingAppearances");
                string asset=string.IsNullOrEmpty(placement.AssetId)?placement.Id:placement.AssetId;
                var owner=root.GetComponentInParent<BaseSceneController>(true);
                view.farmVisual=asset=="farm"||asset=="farm-small";
                if(view.farmVisual)asset=FarmAppearance(owner?.State);
                view.farmAppearance=asset;
                var texture=asset=="notice" ? PixelDemoArt.Load("Props","noticeboard")??Resources.Load<Texture2D>("MapSurfaces/Notice-v1") : catalog?.Find(asset)?.Texture;
                if(texture==null&&placement.Blocks)
                {
                    view.shaped=true;var block=GameObject.CreatePrimitive(PrimitiveType.Cube);block.name=placement.Name+" · 实体占位";block.transform.SetParent(root.transform,false);block.transform.localPosition=Vector3.up*.55f;block.transform.localScale=new Vector3((float)placement.Width*BattleBoardView.CellSize,1.1f,(float)placement.Height*BattleBoardView.CellSize);BattleBoardView.DestroyObject(block.GetComponent<Collider>());
                    view.ownedMaterial=new Material(source);view.ownedMaterial.SetTexture("_BaseMap",Texture2D.whiteTexture);view.ownedMaterial.SetColor("_BaseColor",new Color(.64f,.50f,.32f));block.GetComponent<Renderer>().sharedMaterial=view.ownedMaterial;return view;
                }
                if(texture!=null&&asset!="notice"&&!asset.StartsWith("farm"))
                {
                    view.shaped=true;
                    SpriteFacade.Create(root.transform,texture,feet-Vector3.forward*(float)placement.Height*BattleBoardView.CellSize*.5f,(float)placement.Width*BattleBoardView.CellSize,source,owner.WorldCamera);
                    return view;
                }
                if(asset.StartsWith("farm"))view.shaped=true;
                var face=GameObject.CreatePrimitive(PrimitiveType.Quad);face.name=placement.Name;face.transform.SetParent(root.transform,false);BattleBoardView.DestroyObject(face.GetComponent<Collider>());
                float halfDepth=(float)placement.Height*BattleBoardView.CellSize*.5f;
                root.transform.localPosition=feet-Vector3.forward*halfDepth;
                face.transform.localRotation=Quaternion.Euler(90,0,0);
                face.transform.localPosition=new Vector3(0,.04f,halfDepth);
                face.transform.localScale=new Vector3((float)placement.Width*BattleBoardView.CellSize,(float)placement.Height*BattleBoardView.CellSize,1);
                view.ownedMaterial=BattlePawn2D.Transparent(source);view.ownedMaterial.name="Authored_"+placement.Id;
                if(texture!=null)view.ownedMaterial.SetTexture("_BaseMap",texture);
                view.ownedMaterial.SetColor("_BaseColor",texture!=null ? Color.white : placement.VisualOnly ? new Color(.55f,.43f,.31f) : new Color(.7f,.65f,.4f));
                face.GetComponent<Renderer>().sharedMaterial=view.ownedMaterial;
            }
            view.SetExploration(exploration);MapSpriteOrder.Apply(root.transform,feet.z-(float)placement.BoundsHeight*BattleBoardView.CellSize*.5f);
            if(view.farmVisual)foreach(var renderer in root.GetComponentsInChildren<Renderer>())renderer.sortingOrder=-30500;
            return view;
        }
        public void RefreshFarmAppearance(BaseState state)
        {
            if(!farmVisual||ownedMaterial==null)return;
            string next=FarmAppearance(state);if(next==farmAppearance)return;
            var catalog=Resources.Load<BuildingAppearanceCatalog>("BuildingAppearances");
            var texture=catalog?.Find(next)?.Texture;if(texture==null)return;
            ownedMaterial.SetTexture("_BaseMap",texture);farmAppearance=next;
        }
        public void SetExploration(bool exploration)
        {
            var position=transform.localPosition;position.y=exploration?feet.y:0;transform.localPosition=position;
            transform.localRotation=exploration&&!shaped ? person ? BattleBoardView.BasePawnTilt : BattleBoardView.BaseArtTilt : Quaternion.identity;
            transform.localScale=person&&exploration ? new Vector3(1,1,2) : Vector3.one;
            var pawn=GetComponent<BattlePawn2D>();if(pawn!=null){pawn.UprightExploration=exploration;pawn.SyncExploration(feet,Color.white);}
        }
        private double autonomousUntil;
        public bool AutonomousMoving=>Time.unscaledTimeAsDouble<autonomousUntil;
        public void MoveTo(Vector3 position)
        {bool moving=(position-feet).sqrMagnitude>1e-7f;if(moving)autonomousUntil=Time.unscaledTimeAsDouble+.12;feet=position;var pawn=GetComponent<BattlePawn2D>();if(pawn!=null)pawn.SyncExploration(feet,Color.white,null,Time.unscaledTimeAsDouble,moving);}
        public void RefreshAttention(Vector3 observer,double now)
        {
            AttentionSource=observer;
            if(!person||!gameObject.activeInHierarchy||AutonomousMoving)return;
            var sprite=GetComponent<CompanionSpriteView>();
            if(previewPose.HasValue){if(sprite!=null)sprite.Preview(previewPose.Value,now,sprite.LookTowards(feet,observer));return;}
            GetComponent<BattlePawn2D>()?.SyncExploration(feet,Color.white,observer,now);
        }
        private void OnDestroy()=>BattleBoardView.DestroyObject(ownedMaterial);
    }
}
