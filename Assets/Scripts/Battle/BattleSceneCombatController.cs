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
        public bool IsDemoRuins=>BaseDemoRuins.Encounter(sceneEncounter);
        public bool IsDemoDefense=>BaseDemoDefense.Encounter(sceneEncounter);
        public bool IsSecondDefense=>IsDemoDefense&&sceneEncounter.Opening.EventId==BaseDemoDefense.SecondEventId;
        public bool IsDemoAttack=>BaseDemoFinale.Encounter(sceneEncounter);
        public bool IsBanditHideout => sceneEncounter?.Opening.EventId == BaseBanditHideout.EventId;
        public bool IsStandaloneTest=>BaseStandaloneBattle.Is(sceneEncounter);
        public Action RestartStandaloneTest;
        public string SceneEncounterResult => IsBanditHideout
            ? battle?.Report?.Outcome == BattleOutcome.SideAWin ? BaseBanditHideout.VictoryText : BaseBanditHideout.DefeatText
            : IsStandaloneTest?"独立测试结束 · 可退出或重新选择预设":IsAzhuDuel?"比武结束 · 与阿铸继续交谈":IsDemoPractice?(battle?.Report?.Outcome==BattleOutcome.SideAWin?"演练完成 · 村口传来脚步声":"演练暂歇 · 可与搭档继续练习"):battle?.Report?.Outcome==BattleOutcome.SideAWin?(IsDemoAttack?"攻营成功 · 留在当地处理战果":IsDemoDefense?(sceneEncounter.Opening.EventId==BaseDemoDefense.SecondEventId?"第二波防守成功 · 返回处理战果":"首次防守成功 · 返回处理战果"): IsDemoRuins?"控制来源已击败 · 返回调查":"条件达成 · 继续重伤剧情"):(IsDemoAttack?"进攻败退 · 保留战损，留在敌营外":IsSecondDefense?"第二波防守失败 · 营地失守":IsDemoDefense?"守营败退 · 承担损失，残军次日再袭":IsDemoRuins?"救助未完成 · 保留战损，退回遗迹入口":"教学未完成 · 无损重试");
        public string SceneEncounterReport=>IsBanditHideout?BaseBanditHideout.ReportText:IsSecondDefense?"第二波防守没有时限：击败全部来敌才算守住，我方全员失能即营地失守、行动中止。队长1血倒地后不能行动；保留实际血量与已消耗军需。":IsAzhuDuel?"双方点到为止；结束恢复比武前状态，可以继续交谈邀请阿铸。":IsDemoPractice?"演练结束后恢复原有生命、兵力与资源。完成暂停、选择、移动、指定攻击、驻守和手动鼓舞后，回到村庄听取来讯。":IsDemoAttack||IsDemoDefense||IsDemoRuins?"队长1血倒地后不能行动，全队失能才判败退；可立即撤退。保留实际血量、死亡及已消耗军需，倒地者战后1血恢复行动。胜利才处理俘虏与战果；败退不回战前，不返还已走行程。敌方残军过日恢复：半血以下回半血，半血及以上回满血；死者不恢复。"+(IsDemoDefense?"首次防守败退另扣本次入侵的经济损失，残军撤走后次日再袭，胜利才推进。":""):"此段使用临时教学数据，不结算正式战损、军需、俘虏或奖励。条件达成后继续重伤剧情；提前倒下则返回追兵出现前重试。";
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
            feedback=IsBanditHideout?BaseBanditHideout.Objective:IsSecondDefense?"这一波敌军不会撤走；当前暂停，选中队长下令，空格继续。全员失能即营地失守。":IsAzhuDuel?"与阿铸点到为止；当前暂停，选中主角下令，空格继续。":IsDemoPractice?"搭档带你分步练习；当前暂停，按提示操作，打中一次即可完成。":IsDemoDefense||IsDemoAttack?"实际队伍已落位；当前暂停，可以选择队长下令。保留真实战损，队长倒地不立即判负，可以撤退。":IsDemoRuins?"击败控制者即可救出失踪者；己方有真实战损。已暂停，可选择队长／小队下令。":"追兵已被队长打伤，均为半血；村民自行撤向地图边缘。选择主角、点敌人集火，空格继续／暂停。";return true;
        }
        public bool WithdrawSceneEncounter()=>FinishSceneEncounter(true);
        private bool FinishSceneEncounter(bool abandon=false,bool mainMenu=false)
        {
            if(!IsSceneEncounter||finishSceneEncounter==null)return false;
            if(IsSecondDefense&&abandon&&!mainMenu&&battle.Phase==BattlePhase.Fighting)return false;
            try
            {
                // Persist the live board before the exploration owner restores its peaceful copy.
                if(!finishSceneEncounter(realtime,abandon,mainMenu)){feedback="剧情条件或本场身份已变化，进度未提交";hud?.Refresh();return false;}
                return true;
            }
            catch(Exception error){feedback="未提交本场遭遇："+error.Message;UnityEngine.Debug.LogError(feedback+"\n"+error);hud?.Refresh();return false;}
        }
    }
}
