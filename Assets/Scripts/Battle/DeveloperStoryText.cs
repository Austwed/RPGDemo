using System;
using System.Linq;
using BaseConquest.Presentation;
using BaseConquest.Story;
using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Battle
{
    public sealed partial class DeveloperPanel
    {
        private StoryTextDocument storyDraft;
        private string storySearch="",storyGroup="",storyKey="";
        private void StoryAction(Action action)
        {try{action();}catch(Exception e){message="文本操作失败："+e.Message;}Render();}
        private void RenderStoryText()
        {
            if(storyDraft==null){if(StoryText.Snapshot().entries.Count==0)StoryTextFiles.TryReload();storyDraft=StoryText.Snapshot();}
            targetText.text="保存位置："+StoryTextFiles.EditablePath;targetText.fontSize=14;
            StoryField("搜索键／正文／备注",storySearch,44,value=>{storySearch=value;},()=>Render());
            var groups=new[]{""}.Concat(storyDraft.entries.Select(e=>e.group).Distinct().OrderBy(v=>v)).ToArray();
            Add("分组："+(storyGroup==""?"全部":storyGroup)+"（点击切换）",()=>{int index=Array.IndexOf(groups,storyGroup);storyGroup=groups[(index+1)%groups.Length];Render();});
            Add("重载JSON（丢弃未保存改动）",()=>StoryAction(()=>{StoryTextFiles.Reload();storyDraft=StoryText.Snapshot();message="已重载JSON，后续对话立即使用新值";}));
            var selected=storyDraft.entries.FirstOrDefault(e=>e.key==storyKey);
            if(selected!=null)
            {
                Line(selected.key+" · "+selected.group,56);
                StoryField("正文（多行）",selected.text,180,value=>selected.text=value);
                StoryField("备注",selected.notes,70,value=>selected.notes=value);
                Add("预览当前草稿",()=>{message="草稿尚未保存";Render();});
                var preview=Text(body,selected.text,new Vector2(0,-y-60),new Vector2(510,120),18,new Vector2(.5f,1));preview.supportRichText=false;preview.horizontalOverflow=HorizontalWrapMode.Wrap;
                float height=Mathf.Max(80,preview.preferredHeight+16);preview.rectTransform.sizeDelta=new Vector2(510,height);preview.rectTransform.anchoredPosition=new Vector2(0,-y-height/2);y+=height+12;
            }
            Add("保存全部并立即生效",()=>StoryAction(()=>{StoryTextFiles.Save(storyDraft);message="已保存并应用："+StoryTextFiles.EditablePath;}));
            Add("导出已保存CSV",()=>StoryAction(()=>{StoryTextFiles.ExportCsv();message="已导出："+StoryTextFiles.CsvPath;}));
            Add("导入CSV草稿（保存后生效）",()=>StoryAction(()=>{storyDraft=StoryTextFiles.ReadCsv();storyKey=storyDraft.entries[0].key;storyGroup=storySearch="";message="已导入草稿 "+storyDraft.entries.Count+"条；请检查后保存";}));
            Add("导出已保存JSON（回工程用）",()=>StoryAction(()=>{StoryTextFiles.ExportJson();message="已导出："+StoryTextFiles.ExportPath;}));
            Line("CSV交换文件："+StoryTextFiles.CsvPath+"\n导出只含已保存目录；导入后先预览，再点击保存全部。",100);
            var matches=storyDraft.entries.Where(e=>(storyGroup==""||e.group==storyGroup)&&(string.IsNullOrWhiteSpace(storySearch)||(e.key+"\n"+e.text+"\n"+e.notes).IndexOf(storySearch,StringComparison.OrdinalIgnoreCase)>=0)).ToArray();
            Line("条目 "+matches.Length+"／"+storyDraft.entries.Count+"；选择条目编辑",44);
            foreach(var entry in matches){string key=entry.key;Add((key==storyKey?"● ":"")+key,()=>{storyKey=key;body.anchoredPosition=Vector2.zero;Render();});}
        }
        private void StoryField(string caption,string value,float height,Action<string> changed,Action ended=null)
        {
            Line(caption,32);
            var box=Box(body,"StoryInput_"+caption,new Vector2(.5f,1),new Vector2(0,-y-height/2),new Vector2(520,height),new Color(.08f,.12f,.14f));
            var label=Text(box,"",Vector2.zero,new Vector2(500,height-12),18);label.alignment=TextAnchor.UpperLeft;label.supportRichText=false;label.horizontalOverflow=HorizontalWrapMode.Wrap;
            var input=box.gameObject.GetOrAdd<InputField>();input.textComponent=label;input.lineType=height>60?InputField.LineType.MultiLineNewline:InputField.LineType.SingleLine;input.characterLimit=20000;input.SetTextWithoutNotify(value);input.onValueChanged.AddListener(v=>changed(v));if(ended!=null)input.onEndEdit.AddListener(v=>ended());y+=height+10;
        }
    }
}
