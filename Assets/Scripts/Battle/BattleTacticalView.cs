using System.Linq;
using BaseConquest.RealtimePrototype;
using UnityEngine;
namespace BaseConquest.Battle
{
    public sealed partial class BattleSceneController
    {
        private GameObject tacticalRoot;
        private int tacticalRevision=-1;
        private void SyncTacticalObjects()
        {
            if(realtime?.Tactics==null||realtime.Tactics.Objects.Count==0)return;
            if(tacticalRoot!=null&&tacticalRevision==realtime.Tactics.Revision)return;
            if(tacticalRoot!=null)BattleBoardView.DestroyObject(tacticalRoot);tacticalRoot=new GameObject("TacticalObjects");tacticalRoot.transform.SetParent(unitsRoot,false);tacticalRevision=realtime.Tactics.Revision;
            foreach(var o in realtime.Tactics.Objects)
            {
                var cells=o.Fallen?o.Trunk:new System.Collections.Generic.List<GridPosition>{new GridPosition(o.X,o.Y)};
                foreach(var cell in cells)
                {
                    var feet=RealtimeWorld(new PrototypePoint(cell.X+.5,cell.Z+.5));
                    // Each blocked trunk cell keeps its own segment, including truncated trees.
                    var pixel=BaseConquest.Presentation.PixelDemoArt.Prop(tacticalRoot.transform,o.Fallen?"fallen-log":o.Kind=="dummy"?"stump":o.Kind=="rock"?"boulder":"dead-tree",feet,material,o.Fallen?BattleBoardView.CellSize:o.Kind=="rock"?1.4f:1.25f,o.Fallen?.55f:o.Kind=="dummy"?1.3f:o.Kind=="rock"?1.4f:2.5f);
                    if(pixel!=null){pixel.name=o.Id;if(o.Fallen&&o.DirectionY!=0)pixel.transform.localRotation=Quaternion.Euler(0,90,0)*pixel.transform.localRotation;continue;}
                    var go=GameObject.CreatePrimitive(o.Kind=="rock"?PrimitiveType.Sphere:PrimitiveType.Cube);go.name=o.Id;go.transform.SetParent(tacticalRoot.transform,false);
                    go.transform.position=RealtimeWorld(new PrototypePoint(cell.X+.5,cell.Z+.5))+Vector3.up*(o.Fallen?.18f:.55f);go.transform.localScale=o.Fallen?new Vector3(1.15f,.35f,1.15f):o.Kind=="rock"?Vector3.one*.95f:new Vector3(.45f,1.7f,.45f);
                    var renderer=go.GetComponent<Renderer>();renderer.sharedMaterial=lineMaterial;var ink=new MaterialPropertyBlock();ink.SetColor("_BaseColor",o.Kind=="rock"?new Color(.55f,.58f,.6f):new Color(.4f,.24f,.12f));ink.SetColor("_Color",o.Kind=="rock"?Color.gray:new Color(.4f,.24f,.12f));renderer.SetPropertyBlock(ink);
                }
            }
        }
        private bool OrderTacticalObject(PrototypePoint point)
        {
            var target=realtime.Tactics.At(point);if(target==null)return false;bool ok=realtime.Tactics.Issue(selection.Ids,target.Id,true);
            feedback=ok?(target.Kind=="tree"?"攻击枯树：倒下形成障碍":"推动巨石：沿攻击方向滚动，双方均可能受伤")+(autoRun?"":"；按空格继续"):"请选择可行动队长／小队，再点击物件";hud?.Refresh();return true;
        }
    }
}
