using System;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace BaseConquest.Base
{
    public static class DemoBalanceRuntime
    {
        public const string ResourcePath="DemoBalance/DemoBalance";
        public static DemoBalancePackage Load()
        {
            var asset=Resources.Load<TextAsset>(ResourcePath);
            if(asset==null)throw new InvalidOperationException("缺少Demo数值快照，请先在Unity中校验并导入Demo数值表");
            var package=JsonUtility.FromJson<DemoBalancePackage>(asset.text);if(package==null)throw new InvalidOperationException("Demo数值快照为空");
            package.Validate();return package;
        }
    }
    public sealed partial class BaseSceneController
    {
        public void LoadDemoStage(string id,string gender)
        {
            try
            {
                if((id!="camp"&&id!="defense")||(gender!="male"&&gender!="female"))throw new InvalidOperationException("无效的阶段档选择");
                var asset=Resources.Load<TextAsset>("DemoStages/"+id+"-"+gender);if(asset==null)throw new InvalidOperationException("阶段档尚未生成");
                var next=JsonUtility.FromJson<BaseState>(asset.text);BaseEconomy.Validate(next);
                if(next.Demo.ConfigHash!=DemoBalanceRuntime.Load().WorkbookSha256)throw new InvalidOperationException("阶段档与当前数值快照不一致，请重新生成阶段档");
                BaseBattleGateway.Catalog().SaveAuto(next,"阶段测试 · "+id);BaseBattleGateway.Session().Restore(next);BaseBattleGateway.MainMenu=false;BaseBattleGateway.RequestedId=null;
                if(Application.isPlaying)SceneManager.LoadScene(BaseBattleGateway.ExplorationScene);else{SaveStore.RestoreSession(next);LoadSaved();}
            }
            catch(Exception e){ReportMessage("阶段档读取失败："+e.Message);}
        }
    }
    public sealed partial class BaseHud
    {
        private string stageGender="male";
        public void OpenDemoStages()
        {
            BeginPanel("阶段测试档","demo-stages");
            RowButton("返回",0,820,()=>OpenSlots(false));row+=52;
            foreach(string choice in new[]{"male","female"}){string hero=choice;RowButton((stageGender==hero?"● ":"")+(hero=="male"?"男主角":"女主角"),hero=="male"?-210:210,400,()=>{stageGender=hero;OpenDemoStages();});}row+=52;
            string[] ids={"camp","defense"},names={"新营地 · 醒来与伙伴","巴图袭击当天 · 迎战"},descriptions={"零普通兵，从结识伙伴、拜访领主和遗迹开始。","袭击已到达，三人队与11名普通兵已整备，可在迎敌战场布置工事。"};
            for(int n=0;n<ids.Length;n++){string id=ids[n];Line(descriptions[n],42,20);RowButton("读取 · "+names[n],0,820,()=>Confirm("读取固定阶段档","切换到该阶段起点。当前进度可先存入手动槽。",()=>owner.LoadDemoStage(id,stageGender)));row+=52;}
            Line("阶段起点固定，反复读取可重新测试；测试进展使用普通手动存档保存。",40,18);EndPanel();
        }
    }
}
