using UnityEngine;
using UnityEngine.Rendering;
namespace BaseConquest.Battle
{
    // One fixed oval per category; acquisition particles are reused, not accumulated.
    public sealed class BattleStatusEffectsView
    {
        public readonly GameObject Root;
        private readonly LineRenderer[] auras = new LineRenderer[3];
        private readonly LineRenderer[][] particles = new LineRenderer[3][];
        private readonly Color[] colors = { new Color(1,0.82f,0.16f), new Color(0.2f,1,0.4f), new Color(0.76f,0.3f,1) };
        private readonly int[] serials = new int[3];
        private readonly float[] began = { -100,-100,-100 };
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private readonly BattleSoldier soldier;
        public int ActiveMask { get; private set; }
        public BattleStatusEffectsView(Transform parent, Material material, BattleSoldier unit)
        {
            soldier=unit; Root=new GameObject("StatusEffects");Root.transform.SetParent(parent,false);
            for(int type=0;type<3;type++)
            {
                var points=new Vector3[33]; for(int i=0;i<points.Length;i++){float a=i*Mathf.PI*2/32;points[i]=new Vector3(Mathf.Cos(a)*(0.42f+type*0.09f),0.12f+type*0.012f,Mathf.Sin(a)*(0.12f+type*0.035f));}
                auras[type]=Line(new[]{"BuffYellowAura","HealingGreenAura","DebuffPurpleAura"}[type],material,points,0.032f); Paint(auras[type],colors[type]);
                particles[type]=new LineRenderer[3];
                for(int i=0;i<3;i++)
                {
                    Vector3[] glyph = type==1 ? new[]{new Vector3(0,0,-0.08f),new Vector3(0,0,0.08f),Vector3.zero,new Vector3(-0.08f,0,0),new Vector3(0.08f,0,0)} : new[]{new Vector3(-0.09f,0,0.03f),new Vector3(0,0,0.15f),new Vector3(0.09f,0,0.03f),new Vector3(0,0,0.15f),new Vector3(0,0,-0.12f)};
                    if(type==2)for(int j=0;j<glyph.Length;j++)glyph[j].z=-glyph[j].z;
                    particles[type][i]=Line(new[]{"BuffUpArrow","HealingCross","DebuffDownArrow"}[type]+i,material,glyph,0.033f); particles[type][i].enabled=false;
                }
            }
        }
        private LineRenderer Line(string name,Material material,Vector3[] points,float width)
        { var go=new GameObject(name);go.transform.SetParent(Root.transform,false);var line=go.AddComponent<LineRenderer>();line.sharedMaterial=material;line.useWorldSpace=false;line.positionCount=points.Length;line.SetPositions(points);line.widthMultiplier=width;line.startColor=line.endColor=Color.white;line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;line.enabled=false;return line; }
        private void Paint(LineRenderer line,Color c){properties.SetColor("_BaseColor",c);properties.SetColor("_Color",c);line.SetPropertyBlock(properties);}
        public void Sync(BattleSimulation battle,float progress,float scale)
        {
            Root.SetActive(soldier.IsActive && soldier.CarriedById<0); Root.transform.localScale=Vector3.one*scale;
            int mask=battle.EffectVisualMask(soldier,progress<1?battle.RoundNumber:battle.RoundNumber+1);
            var gains=new[]{soldier.BuffGainSerial,soldier.HealGainSerial,soldier.DebuffGainSerial};
            for(int type=0;type<3;type++)
            {
                bool gained=gains[type]!=serials[type];
                if(gained && (type!=2 || progress>=0.9f)) { serials[type]=gains[type]; if((mask&(1<<type))!=0)began[type]=Time.unscaledTime; }
                bool delayed=type==2 && gained && progress<0.9f;
                auras[type].enabled=(mask&(1<<type))!=0 && !delayed;
                for(int i=0;i<3;i++)
                {
                    var line=particles[type][i];float t=(Time.unscaledTime-began[type]-i*0.055f)/0.7f;line.enabled=soldier.IsActive && t>=0 && t<1;
                    if(!line.enabled)continue;
                    line.transform.localPosition=new Vector3((i-1)*0.18f+(type==1?Mathf.Sin(t*Mathf.PI*2)*0.06f:0),0.5f,0.1f+(type==2?-1:1)*t*0.6f);
                    var c=colors[type];c.a=1-t;Paint(line,c);
                }
            }
            ActiveMask=(auras[0].enabled?1:0)|(auras[1].enabled?2:0)|(auras[2].enabled?4:0);
        }
    }
}
