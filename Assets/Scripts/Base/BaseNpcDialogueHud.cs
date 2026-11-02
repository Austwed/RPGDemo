using System;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private sealed class NpcLine
        {
            public string Name,Appearance,Text;
            public bool Player;
            public NpcLine(string name,string appearance,string text,bool player=false){Name=name;Appearance=player?appearance:BaseDemoNpcs.DialogueArt(name,appearance);Text=text;Player=player;}
        }
        private sealed class NpcReply
        {
            public string Text;public Action Action;public bool Enabled;
            public NpcReply(string text,Action action,bool enabled=true){Text=text;Action=action;Enabled=enabled;}
        }
        private Action dialogueContinue;
        public bool DialogueOpen=>PanelOpen&&panelViewKey=="npc-dialogue";
        public bool ContinueDialogue(){if(!DialogueOpen||dialogueContinue==null)return false;dialogueContinue();return true;}
        private void NpcDialogue(NpcLine[] lines,NpcReply[] replies,int index=0)
        {
            if(panel!=null){ClearPanelSelection();panel.gameObject.SetActive(false);UiPrefabBinding.DestroyView(panel.gameObject);}
            panelScroll=null;panelFeedback=null;panelViewKey="npc-dialogue";interactionObject="";dialogueContinue=null;root.GetComponent<Canvas>().sortingOrder=300;
            modal.gameObject.SetActive(true);modal.GetComponent<Image>().color=new Color(.02f,.06f,.07f,.12f);Joystick.ResetInput();
            panel=UiPrefabBinding.Create("NpcDialogue","NPC对白",modal,false,owner.PageTemplate("NpcDialogue")).GetComponent<RectTransform>();
            panel.anchorMin=panel.anchorMax=panel.pivot=new Vector2(.5f,0);panel.anchoredPosition=new Vector2(0,22);panel.sizeDelta=new Vector2(1160,282);
            panel.gameObject.GetOrAdd<Image>().color=new Color(.055f,.09f,.12f,.97f);
            var line=lines[Mathf.Clamp(index,0,lines.Length-1)];
            bool portrait=!string.IsNullOrEmpty(line.Appearance)||line.Player;
            var border=Box("PortraitFrame",panel,new Vector2(0,.5f),new Vector2(114,0),new Vector2(194,246),new Color(.14f,.23f,.25f,1));
            var avatar=Rect("SpeakerPortrait",border,Vector2.one*.5f,new Vector2(0,2),new Vector2(168,228)).gameObject.GetOrAdd<BattleAvatarGraphic>();
            avatar.PlayerHero=line.Player;avatar.AppearanceId=line.Player?BaseDemoIntro.PlayerArt(owner.State):line.Appearance;avatar.raycastTarget=false;border.gameObject.SetActive(portrait);
            var namePlate=Box("SpeakerNamePlate",panel,new Vector2(0,1),new Vector2(405,-33),new Vector2(330,42),new Color(.25f,.35f,.35f,1));
            var nameText=Label(namePlate,line.Name,Vector2.zero,new Vector2(304,38),24);nameText.name="SpeakerName";
            var speech=Label(panel,line.Text.Replace("{称呼}",BaseDemoIntro.Address(owner.State)).Replace("{搭档代词}",BaseDemoIntro.CaptainArt(owner.State)=="female"?"她":"他").Replace("{Player}",BaseDemoIntro.Address(owner.State)).Replace("{Partner}",BaseDemoIntro.PartnerName(owner.State)),new Vector2(117,0),new Vector2(858,164),24,TextAnchor.UpperLeft);speech.name="DialogueText";speech.supportRichText=false;
            if(!portrait){speech.rectTransform.anchoredPosition=new Vector2(0,0);speech.rectTransform.sizeDelta=new Vector2(1090,164);namePlate.anchoredPosition=new Vector2(192,-33);}
            speech.resizeTextForBestFit=true;speech.resizeTextMinSize=20;speech.resizeTextMaxSize=24;
            panelFeedback=Label(panel,portrait?"交谈中 · 选择答复后才接取或交付":"提示 · 确认后继续",new Vector2(0,-119),new Vector2(1090,24),16);
            if(index<lines.Length-1)
            {
                dialogueContinue=()=>NpcDialogue(lines,replies,index+1);
                MakeButton(panel,"继续",new Vector2(-87,31),new Vector2(132,42),()=>ContinueDialogue(),new Vector2(1,0));
                Label(panel,"E／空格 继续",new Vector2(-108,79),new Vector2(170,25),16,TextAnchor.MiddleRight,new Vector2(1,0));
            }
            else
            {
                var choices=Rect("Responses",panel,new Vector2(1,1),new Vector2(-310,8),new Vector2(620,replies.Length*52));choices.pivot=new Vector2(.5f,0);
                choices.gameObject.GetOrAdd<UiDynamicContent>();
                for(int n=0;n<replies.Length;n++)
                {var reply=replies[n];var button=MakeButton(choices,reply.Text,new Vector2(0,26+(replies.Length-1-n)*52),new Vector2(620,46),reply.Action,new Vector2(.5f,0));button.interactable=reply.Enabled;}
            }
            UiPrefabBinding.Complete(panel.gameObject);DemoUiTheme.Current.Frame(panel,true);RefreshSummary();Canvas.ForceUpdateCanvases();
        }
    }
}
