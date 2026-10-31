using System.Linq;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private void OpenDemoQuestList()
        {
            var s=owner.State;activeObject="quests";interactionObject="";BeginPanel("任务列表 · 主线与支线","quests");
            void Quest(string name,string status,string goal,string reward)
            {Line(name+"  【"+status+"】",48,23);Line(goal+"\n奖励："+reward,88,20);}
            Line("主线任务",50,25);
            Quest(BaseDemoPrologue.Village(s)?"守护"+BaseScriptNames.Village:"追查失踪的队长",s.Demo.Stage==BaseDemoStage.Finished?"获得新线索":"进行中",BaseDemo.Objective(s),BaseDemoPrologue.Village(s)?"巡村准备与撤离":"推进队长与断剑线索");
            if(BaseDemoIntro.Daily(s))Quest(BaseScriptNames.Village+"的清晨","进行中",BaseDemoIntro.Objective(s),"与搭档巡村，完成日常准备");
            else if(BaseDemoPrologue.Village(s))Quest("掩护百姓撤离",s.Demo.Stage==BaseDemoStage.PrologueBriefing?"待听取报告":"进行中","带村民撤离，阻挡追兵。","序章剧情进展");
            else
            {
                string status=s.Demo.DeliveredDay>0?"已领取任务奖赏":s.Demo.RuinsDay>0?"已完成，可向领主交差":s.Demo.AcceptedDay>0?"已接取":"可向领主求助，也可先探索";
                var reward=s.Demo.RuinsReward;
                Quest(BaseScriptNames.Ruins+"调查与失踪者",status,"击败控制来源，救助两名百姓；未接任务也可先完成调查。",reward==null?"重建许可":("重建许可、粮草 "+reward.Food+"、原料 "+reward.Materials+"、钱币 "+reward.Coins+"（首奖一次）"));
                if(BaseDemoDefense.Delivered(s))Quest("招募建设人手",BaseDemoFlow.Recruited(s)?"已完成":"进行中",BaseDemoFlow.RecruitProgress(s),"开放经营教学，了解"+BaseScriptNames.Ruins+"反复挑战");
                if(BaseDemoDefense.Delivered(s))Quest("重建营地",s.Demo.Defense.WorkshopRepaired&&s.Demo.Defense.BarracksRepaired?"已修复":"进行中","修复工坊和兵营，安排生产；用奖赏补兵，再以经营盈余添置个人装备。","设施与供军能力");
                if(BaseDemoDefense.Later(s))Quest("守住新营地",BaseDemoDefense.Won(s)?"已完成":"进行中",BaseDemoDefense.Objective(s),"攻营与返乡道路线索");
                if(BaseDemoDefense.Won(s))Quest("攻营与返乡",s.Demo.Stage==BaseDemoStage.Finished?"已完成":"进行中",BaseDemoFinale.Objective(s),"断剑与幸存者证词");
            }
            Line("支线任务",50,25);
            foreach(var id in BaseDemoPrologue.Village(s)?Enumerable.Empty<string>():new[]{"partner_farm","partner_craft"})
            {
                string name=id=="partner_farm"?"阿禾的邀请":"阿铸的承诺";
                string status=BaseAdventure.Contains(s,id)?"正在同行":s.Leaders.Any(l=>l.Id==id)?"已结识，留在营地":s.Demo.MetCompanions.Contains(id)?"已交谈，可邀请":"尚未交谈";
                Quest(name,status,id=="partner_farm"?"在营地认识阿禾。他是普通人，"+BaseScriptNames.Ruins+"侵蚀会伤害他。":"在"+BaseScriptNames.Town+"与阿铸交谈、切磋后邀请。他是异能者，可协助"+BaseScriptNames.Ruins+"调查。","可选伙伴，缺席不阻断主线");
            }
            if(s.Demo.RuinsDay>0)Quest(BaseScriptNames.Ruins+"试炼", "可重复挑战","体力 "+BaseRuinsRewards.Energy(s)+" / "+BaseRuinsRewards.Maximum(s)+"，每日补满；无体力也能挑战，但不能领取副本奖励。","每次消耗 "+BaseRuinsRewards.Cost(s)+" 体力，获得个人装备、锻造石与金币；保留真实战损");
            RowButton("关闭任务列表",0,800,ClosePanel);row+=60;EndPanel();
        }
    }
}
