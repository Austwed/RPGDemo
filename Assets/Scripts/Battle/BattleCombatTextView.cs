using System.Collections.Generic;
using UnityEngine;

namespace BaseConquest.Battle
{
    // Presentation only: consume settled battle events once, using the paused simulation clock.
    public sealed class BattleCombatTextView
    {
        private sealed class Entry
        {
            public Transform Root;
            public TextMesh Label,Shadow;
            public Vector3 Origin;
            public Color Color;
            public double Began;
        }
        private readonly Transform parent;
        private readonly Font font;
        private readonly List<Entry> entries=new List<Entry>();
        private BattleEvent last;
        private int lastObjectAttack;
        public BattleCombatTextView(Transform parent,Font font,BattleSimulation battle)
        {this.parent=parent;this.font=font;last=battle.Events.Count>0?battle.Events[battle.Events.Count-1]:null;}
        public void Sync(BattleSimulation battle,Camera camera,IReadOnlyDictionary<int,BattleSoldierView> views,double seconds,IReadOnlyList<RpgAttackPacket> attacks=null)
        {
            if(attacks!=null)foreach(var packet in attacks)
            {
                if(packet.Id<=lastObjectAttack||string.IsNullOrEmpty(packet.ObjectId)||!packet.Settled)continue;
                var p=packet.Destination;
                if(!battle.Vision.Visible(BattleSide.A,new GridPosition((int)p.X,(int)p.Z)))continue;
                var origin=new Vector3(((float)p.X-battle.Board.Width*.5f)*BattleBoardView.CellSize,0,((float)p.Z-battle.Board.Height*.5f)*BattleBoardView.CellSize);
                var host=new GameObject("物件伤害浮字");host.transform.SetParent(parent,false);
                var entry=new Entry {Root=host.transform,Origin=origin,Color=new Color(1,.9f,.65f),Began=seconds};
                string value=packet.Attack.ToString("0");entry.Shadow=Label(host.transform,"Outline",value,32760,new Vector3(.014f,-.014f,.01f));entry.Label=Label(host.transform,"Text",value,32761,Vector3.zero);entries.Add(entry);
            }
            if(attacks!=null&&attacks.Count>0)lastObjectAttack=attacks[attacks.Count-1].Id;
            int start=last==null?0:battle.Events.IndexOf(last)+1;
            for(int i=start;i<battle.Events.Count;i++)
            {
                var e=battle.Events[i];
                if((!e.Critical&&!e.Missed)||!views.TryGetValue(e.TargetId,out var view)||!battle.PlayerCanSee(BattleSide.A,view.Soldier))continue;
                if(battle.Scenario.FogEnabled&&!battle.Scenario.ShowEnemiesThroughFog&&!e.SeenByA)continue;
                if(entries.Count>=32){BattleBoardView.DestroyObject(entries[0].Root.gameObject);entries.RemoveAt(0);}
                var host=new GameObject(e.Missed?"闪避浮字":"暴击浮字");host.transform.SetParent(parent,false);
                var color=e.Missed?new Color(.65f,.9f,1):new Color(1,.8f,.2f);
                var entry=new Entry {Root=host.transform,Origin=view.Root.transform.position,Color=color,Began=seconds};
                entry.Shadow=Label(host.transform,"Outline",e.Missed?"闪避":"暴击",32760,new Vector3(.014f,-.014f,.01f));
                entry.Label=Label(host.transform,"Text",e.Missed?"闪避":"暴击",32761,Vector3.zero);entries.Add(entry);
            }
            if(battle.Events.Count>0)last=battle.Events[battle.Events.Count-1];
            for(int i=entries.Count-1;i>=0;i--)
            {
                var entry=entries[i];float age=(float)(seconds-entry.Began);
                if(age>=.9f){BattleBoardView.DestroyObject(entry.Root.gameObject);entries.RemoveAt(i);continue;}
                entry.Root.rotation=camera.transform.rotation;
                entry.Root.position=entry.Origin+camera.transform.up*(1.9f+age*.65f);
                var color=entry.Color;color.a=Mathf.Clamp01((.9f-age)/.3f);entry.Label.color=color;entry.Shadow.color=new Color(0,0,0,color.a);
            }
        }
        private TextMesh Label(Transform root,string name,string value,int order,Vector3 offset)
        {
            var host=new GameObject(name);host.transform.SetParent(root,false);host.transform.localPosition=offset;
            var label=host.AddComponent<TextMesh>();label.font=font;label.fontSize=40;label.characterSize=.045f;label.anchor=TextAnchor.MiddleCenter;label.alignment=TextAlignment.Center;label.text=value;
            var renderer=host.GetComponent<MeshRenderer>();renderer.sharedMaterial=font.material;renderer.sortingOrder=order;return label;
        }
    }
}
