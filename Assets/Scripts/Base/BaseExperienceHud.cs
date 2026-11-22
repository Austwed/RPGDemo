using System;
namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private void OpenHelp() => OpenHelp(OpenMenu);
        private void OpenTitleHelp() => OpenHelp(OpenMainMenu);
        private void OpenHelp(Action back)
        {
            BeginPanel("帮助", "help");
            RowButton("返回菜单",0,800,back);
            row+=60;
            foreach (var topic in BaseExperienceRules.Help)
            {
                RowButton(topic.Title,0,800,()=>
                {
                    BeginPanel("帮助 · "+topic.Title,"help:"+topic.Title);
                    RowButton("返回帮助",0,800,()=>OpenHelp(back));
                    row+=60;
                    Line(topic.Text,300,20);
                    EndPanel();
                });
                row+=60;
            }
            EndPanel();
        }
    }
}
