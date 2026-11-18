using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BaseConquest.Base;
using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;
using UnityEditor;
using UnityEngine;
namespace BaseConquest.Editor
{
    public static class DemoStageSaveTools
    {
        public static BaseState Camp(string raw,string gender,RpgRules rules=null)
        {
            var doc=BaseDemo.CreateOpening(JsonUtility.FromJson<BaseScenarioDocument>(raw),raw,false,true,false,rules);
            // 必须在 CreateState 之前设：角色在 CreateState 里就按种子生成完了，之后再改只是改一个没人重读的字段。
            if(doc.Opening?.Demo?.Rpg!=null)doc.Opening.Demo.Rpg.GenerationSeed=StageGenerationSeed;
            var s=doc.CreateState();
            BaseDemoIntro.Enable(s,rules!=null);s.Demo.Intro.Hero=gender;s.Demo.Intro.Step=8;s.Leaders.Single(l=>l.Id==s.PlayerLeaderId).AppearanceId=BaseDemoIntro.PlayerArt(s);return s;
        }
        private static void At(BaseState s,string id)
        {var obj=BaseSpatial.Objects(s).Single(o=>o.Id==id);var p=obj.Blocks?BaseStageWalk.Door(s,id):new BasePoint(obj.X,obj.Y);s.PlayerX=p.X;s.PlayerY=p.Y;BaseAdventure.Arrive(s);}
        private static void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
        // P40：办公设施在首防之后才由领主赠送，此前只能逐人手动；测试档把可工作的人先排到粮田。
        private static void AssignFarm(BaseState s){foreach(var w in s.Workers.Where(w=>!w.Away))w.DesiredFacility="farm";BaseDevelopment.SetPolicy(s,"manual");}
        private static void Loot(BaseState s)
        {
            foreach(var p in s.Aftermath.Prisoners.Where(p=>p.Status=="held").ToArray())Need(BaseAftermath.Decide(s,p.Id,"release"),"释放俘虏");
            foreach(var manifest in s.Aftermath.Loot.Where(l=>!l.Closed).ToArray()){
                foreach(var item in manifest.Items.Where(i=>i.Status=="pending").ToArray())Need(BaseBattleLoot.Resolve(s,manifest.BattleId,item.Id,s.PlayerLeaderId,true),"领取奖励");
                Need(BaseBattleLoot.Finish(s,manifest.BattleId),"完成战果");}
        }
        public static BaseState Defense(string raw,string gender,RpgRules rules=null)
        {
            var s=Camp(raw,gender,rules);At(s,"home");Need(BaseDemo.AcknowledgeWaking(s),"醒来");
            At(s,"demo_ahe");Need(BaseDemoRuins.Meet(s,"partner_farm")&&BaseDemoRuins.Join(s,"partner_farm"),"阿禾加入");
            At(s,"exit_east");Need(BaseCampaign.Travel(s,"town"),"进入城镇");At(s,"demo_azhu");Need(BaseDemoRuins.Meet(s,"partner_craft")&&BaseDemoRuins.Join(s,"partner_craft"),"阿铸加入");s.Demo.Flow.AzhuDuelWon=true;Need(BaseDemoStories.Gift(s),"父亲赠礼");BaseBattleLoot.CollectAll(s,"demo:smith:parting-gift");Need(BaseBattleLoot.Finish(s,"demo:smith:parting-gift"),"领取父亲赠礼");
            At(s,"town_quest");Need(BaseDemo.AcceptLordQuest(s),"领主任务");At(s,"demo_ruins_exit");Need(BaseCampaign.Travel(s,"outpost"),"前往遗迹入口");
            var entrance=BaseSpatial.Objects(s).First(o=>o.Id=="demo_ruins_investigate");s.PlayerX=entrance.X;s.PlayerY=entrance.Y;
            var session=BaseDemoRuins.Begin(s,"none");var run=new RealtimeCampaignBattle(session);BaseDemoRuins.Configure(run);run.Model.SetPaused(false);
            for(int t=0;t<5400&&run.Battle.Phase==BattlePhase.Fighting;t++){
                if(t%30==0){var enemy=run.Battle.Commander(BattleSide.B,0);var target=run.Model.Units.First(u=>u.Id==enemy.Id+1);
                    foreach(var captain in run.Model.Units.Where(u=>u.Alive&&u.Side==PrototypeSide.Ally&&u.Role==PrototypeRole.Leader)){
                        bool visible=run.Battle.PlayerCanSee(BattleSide.A,enemy);
                        if(visible){if(captain.TargetId!=target.Id||!captain.ManualOrder)run.Issue(new[]{captain.Id-1},PrototypeOrder.Focus,target.Position,target.Id-1,true);}
                        else run.Issue(new[]{captain.Id-1},PrototypeOrder.Move,target.Position,-1,true);
                    }}run.Tick(1d/60);}
            Need(BaseDemoRuins.Complete(s,run),"阶段档遗迹实际战斗："+run.Battle.Report?.Outcome);Loot(s);
            // Fixed test starts are healthy; resources and progress use the actual transaction results.
            foreach(var l in s.Leaders){l.Health=l.MaxHealth;l.Fate="active";}
            BaseSceneRules.LeaveDungeon(s);At(s,"demo_ruins_return");Need(BaseCampaign.Travel(s,"town"),"返回城镇");At(s,"town_quest");Need(BaseDemoRuins.Deliver(s),"交付遗迹");
            At(s,"town_recruit");var kinds=Enum.GetValues(typeof(BaseTroopKind)).Cast<BaseTroopKind>().ToArray();
            // 2026-11-03 起第1日固定每兵种 2 人、次日起按基准浮动，11 人必须跨日招满；阶段档必须走玩家真能走到的路径。
            s.Demo.Flow.RestLessonDone=true;s.Demo.Flow.FarmOfferShown=true;s.Demo.Flow.NoviceShop=true;
            for(int round=0;round<4&&s.Workers.Sum(w=>w.Count)<11&&!BaseDemoDefense.Due(s);round++)
            {
                for(int pass=0;pass<4;pass++)
                {
                    bool bought=false;
                    foreach(var kind in kinds)
                    {
                        if(s.Workers.Sum(w=>w.Count)>=11)break;
                        if(BaseTown.MaxHire(s,kind)>0){Need(BaseTown.Confirm(s,BaseTown.QuoteHire(s,kind,1)),"实际募兵 "+kind);bought=true;}
                    }
                    if(!bought)break;
                }
                if(s.Workers.Sum(w=>w.Count)>=11||BaseDemoDefense.Due(s))break;
                At(s,"travel_base");Need(BaseCampaign.Travel(s,"base"),"返回基地");AssignFarm(s);
                At(s,"home");Need(BaseEconomy.Rest(s,s.Day),"真实休息日结失败");
                At(s,"exit_east");Need(BaseCampaign.Travel(s,"town"),"返回城镇");At(s,"town_recruit");
            }
            Need(s.Workers.Sum(w=>w.Count)==11,"阶段档11名普通兵");s.Demo.Flow.RecruitmentNotice=true;
            At(s,"travel_base");Need(BaseCampaign.Travel(s,"base"),"返回基地");At(s,"demo_fix_workshop");Need(BaseDemoDefense.Repair(s,"workshop"),"修复工坊");At(s,"demo_fix_barracks");Need(BaseDemoDefense.Repair(s,"barracks"),"修复兵营");AssignFarm(s);
            At(s,"home");s.Demo.Flow.RestLessonDone=true;for(int n=0;n<20&&!BaseDemoDefense.Due(s);n++)Need(BaseEconomy.Rest(s,s.Day),"真实休息日结失败");
            At(s,"home");
            Need(BaseDemoDefense.Due(s),"首防到达");s.Demo.Flow.RestLessonDone=true;s.Demo.Flow.FarmOfferShown=true;s.Demo.Flow.NoviceShop=true;
            At(s,"demo_defense_exit");Need(BaseCampaign.Travel(s,"perimeter"),"进入迎敌战场");At(s,"demo_defense_start");
            foreach(var l in s.Leaders){l.Health=l.MaxHealth;l.Fate="active";}BaseEconomy.Validate(s);return s;
        }
        // 固定阶段档是回归基线，必须每次生成同一批随机队长，否则跨批次的战斗结果没法比。
        // 只在这里写死种子：新局仍由 BaseState.CreateState 现取随机种子，保证每局敌人不同、反复刷也不固定。
        public const int StageGenerationSeed = 1615048403;
        public static Dictionary<string,string> Generate(string raw,RpgRules rules=null,string configHash=null)
        {
            var output=new Dictionary<string,string>();
            foreach(string hero in new[]{"male","female"}){
                foreach(var pair in new[]{(Id:"camp",State:Camp(raw,hero,rules)),(Id:"defense",State:Defense(raw,hero,rules))})
                {pair.State.Demo.ConfigHash=configHash;BaseEconomy.Validate(pair.State);output[pair.Id+"-"+hero]=JsonUtility.ToJson(pair.State,true);}
            }
            return output;
        }
    }
}
