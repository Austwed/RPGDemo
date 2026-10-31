using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace BaseConquest.Presentation
{
    public sealed class PartyInventoryCell:MonoBehaviour,IBeginDragHandler,IDragHandler,IEndDragHandler,IDropHandler
    {
        public string ItemId,Holder,Slot;
        public int Cell;
        [NonSerialized] public Action<string,string,int,string> Move;
        [NonSerialized] public RectTransform Scope;
        [NonSerialized] public Action BeginDrag;
        private static string dragging;
        private static RectTransform ghost,scope;
        public void OnBeginDrag(PointerEventData e)
        {
            if(string.IsNullOrEmpty(ItemId)||Scope==null)return;BeginDrag?.Invoke();Clear();dragging=ItemId;scope=Scope;
            var obj=new GameObject("拖动物品",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image),typeof(CanvasGroup));obj.transform.SetParent(scope,false);
            ghost=obj.GetComponent<RectTransform>();ghost.sizeDelta=new Vector2(128,54);obj.GetComponent<Image>().color=new Color(.18f,.34f,.32f,.94f);obj.GetComponent<CanvasGroup>().blocksRaycasts=false;
            var textObject=new GameObject("名称",typeof(RectTransform),typeof(Text));textObject.transform.SetParent(ghost,false);var r=textObject.GetComponent<RectTransform>();r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;
            var t=textObject.GetComponent<Text>();var source=GetComponentInChildren<Text>();t.font=source?.font;t.fontSize=16;t.text=source?.text??"物品";t.alignment=TextAnchor.MiddleCenter;t.color=Color.white;t.raycastTarget=false;OnDrag(e);
        }
        public void OnDrag(PointerEventData e){if(ghost!=null&&scope!=null&&RectTransformUtility.ScreenPointToLocalPointInRectangle(scope,e.position,e.pressEventCamera,out var p))ghost.anchoredPosition=p;}
        public void OnDrop(PointerEventData e){if(!string.IsNullOrEmpty(dragging)){string id=dragging;Clear();Move?.Invoke(id,Holder,Cell,Slot??"");}}
        public void OnEndDrag(PointerEventData e)=>Clear();
        private static void Clear(){dragging=null;if(ghost!=null)UiPrefabBinding.DestroyView(ghost.gameObject);ghost=null;scope=null;}
        private void OnDisable(){if(Scope==scope)Clear();}
    }
}
