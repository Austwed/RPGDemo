using System;
using BaseConquest.Base;

namespace BaseConquest.Battle
{
    public sealed partial class BattleSceneController
    {
        private BaseCampaignSession sceneEncounter;
        private Action<RealtimeCampaignBattle> configureSceneEncounter;
        private Action<double> advanceSceneFollowers;
        private Func<RealtimeCampaignBattle,bool,bool,bool> finishSceneEncounter;
        public bool IsSceneEncounter=>sceneEncounter!=null;
        public string SceneEncounterObjective {get;private set;}
        public bool IsAzhuDuel=>sceneEncounter?.Opening.Id==BaseLocalDuel.Id;
        public bool IsDemoPractice=>sceneEncounter?.Opening.Id==BaseDemoIntro.BattleId;
        private bool IsDemoPursuit=>sceneEncounter?.Opening.Id==BaseDemoPrologue.BattleId;
        private bool finishWithdrawalThroughDialogue;
        public bool ContinuesThroughDialogue=>IsAzhuDuel||IsDemoPractice||IsDemoPursuit||finishWithdrawalThroughDialogue||
            IsRuinsRescue&&(battle?.Report?.Outcome==BattleOutcome.SideAWin||realtime?.RuinsHalfHealthRetreat==true);
        public bool IsDemoRuins=>BaseDemoRuins.Encounter(sceneEncounter);
        internal bool IsRuinsRescue => IsDemoRuins && sceneEncounter.Opening.RuinsFirstClear;
        public bool IsDemoDefense=>BaseDemoDefense.Encounter(sceneEncounter);
        public bool IsSecondDefense=>IsDemoDefense&&sceneEncounter.Opening.EventId==BaseDemoDefense.SecondEventId;
        public bool IsDemoAttack=>BaseDemoFinale.Encounter(sceneEncounter);
        public bool IsBanditHideout => sceneEncounter?.Opening.EventId == BaseBanditHideout.EventId;
        public bool IsStandaloneTest=>BaseStandaloneBattle.Is(sceneEncounter);
        public Action RestartStandaloneTest;
        public string SceneEncounterResult => BaseDemoRuins.Rematch(sceneEncounter)
            ? battle?.Report?.Outcome == BattleOutcome.SideAWin ? "敌军已歼灭" : "保留战损，返回遗迹入口"
            : IsBanditHideout
            ? battle?.Report?.Outcome == BattleOutcome.SideAWin ? BaseBanditHideout.VictoryText : BaseBanditHideout.DefeatText
            : IsDemoPursuit?"东口交战结束":IsStandaloneTest?"独立测试结束 · 可退出或重新选择预设":IsAzhuDuel?"比武结束 · 与阿铸继续交谈":IsDemoPractice?(battle?.Report?.Outcome==BattleOutcome.SideAWin?"演练完成 · 村口传来脚步声":"演练暂歇 · 可与搭档继续练习"):battle?.Report?.Outcome==BattleOutcome.SideAWin?(IsDemoAttack?"攻营成功 · 留在当地处理战果":IsDemoDefense?(sceneEncounter.Opening.EventId==BaseDemoDefense.SecondEventId?"第二波防守成功 · 返回处理战果":"首次防守成功 · 返回处理战果"): IsDemoRuins?"控制来源已击败 · 返回调查":"条件达成 · 继续重伤剧情"):(IsDemoAttack?"进攻败退 · 保留战损，留在敌营外":IsSecondDefense?"第二波防守失败 · 营地失守":IsDemoDefense?"守营败退 · 承担损失，残军次日再袭":IsDemoRuins?"救助未完成 · 保留战损，退回遗迹入口":"教学未完成 · 无损重试");
        public void ConfigureSceneEncounter(BaseCampaignSession encounter,Action<RealtimeCampaignBattle> configure,Action<double> advance,Func<RealtimeCampaignBattle,bool,bool,bool> finish,string objective)
        {
            sceneEncounter=encounter??throw new ArgumentNullException(nameof(encounter));campaignSession=encounter;campaignBattleId=encounter.Opening.Id;
            configureSceneEncounter=configure;advanceSceneFollowers=advance;finishSceneEncounter=finish;SceneEncounterObjective=objective;suppressInitialDeploy=true;
            if (IsBanditHideout) manualFieldEncounter = encounter.Opening.ManualDeployment;
        }
        private bool InitializeSceneEncounter()
        {
            if(!IsSceneEncounter)return false;
            campaignSession=sceneEncounter;ReplaceSimulation(sceneEncounter.Battle.Scenario,sceneEncounter.Battle);if(battle.Phase!=BattlePhase.Deployment)AttachRealtime();
            feedback=IsBanditHideout?BaseBanditHideout.Objective:IsSecondDefense?"这一波敌军不会撤走；当前暂停，选中队长下令，空格继续。全员失能即营地失守。":IsAzhuDuel?"与阿铸点到为止；当前暂停，选中主角下令，空格继续。":IsDemoPractice?"搭档带你分步练习；当前暂停，按提示操作，打中一次即可完成。":IsDemoDefense||IsDemoAttack?"实际队伍已落位；当前暂停，可以选择队长下令。保留真实战损，队长倒地不立即判负，可以撤退。":IsRuinsRescue?"击败控制者即可救出失踪者；己方有真实战损。已暂停，可选择队长／小队下令。":IsDemoRuins?"歼灭敌军。已暂停，可选择队长／小队下令。":"两名古裔追兵都已负伤，村民正往东山口撤离。选中主角后点击敌人即可下令。";return true;
        }
        public bool WithdrawSceneEncounter()
        {
            if (realtime?.HasBattlefieldExits != true || battle.Phase != BattlePhase.Fighting)
                return FinishSceneEncounter(true);
            if (!realtime.OrderBattlefieldExit(null)) return false;
            finishWithdrawalThroughDialogue = true;
            ResumeAfterDialogue(true);
            return true;
        }
        internal bool ContinueFinishedSceneDialogue()
        {
            if(!ContinuesThroughDialogue||finishSceneEncounter==null||ExternalDialogue||battle?.Phase!=BattlePhase.Finished||!CanReturnCampaign)return false;
            if(UnityEngine.Time.unscaledTime<campaignRetryAt)return false;
            campaignRetryAt=UnityEngine.Time.unscaledTime+1;
            return FinishSceneEncounter();
        }
        private bool FinishSceneEncounter(bool abandon=false,bool mainMenu=false)
        {
            if(!IsSceneEncounter||finishSceneEncounter==null)return false;
            if(IsSecondDefense&&abandon&&!mainMenu&&battle.Phase==BattlePhase.Fighting)return false;
            try
            {
                // Persist the live board before the exploration owner restores its peaceful copy.
                if(!finishSceneEncounter(realtime,abandon,mainMenu)){feedback="剧情条件或本场身份已变化，进度未提交";hud?.Refresh();return false;}
                finishSceneEncounter=null;
                return true;
            }
            catch(Exception error){feedback="暂时无法返回，请再试一次。";UnityEngine.Debug.LogException(error);hud?.Refresh();return false;}
        }
    }
}
