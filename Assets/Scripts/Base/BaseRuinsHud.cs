using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Story;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        public void OpenRuinsWarning(Action proceed)
        {
            var s=owner.State;var lines=new List<NpcLine>();
            lines.Add(new NpcLine(BaseScriptNames.Ruins+"警告","","当前出战队伍中有成员会受到侵蚀。侵蚀基准为每秒扣除最大生命的"+BaseRuinsRewards.Exposure(s).ToString("P0")+"。"));
            NpcDialogue(lines.ToArray(),new[]{new NpcReply("知晓危险，仍让当前队伍进入",proceed),new NpcReply("暂不进入，先调整随行",owner.CancelRuinsEntry)});
        }
        public void OpenRuinsConfrontation(Action proceed)
        {
            var retreat = new NpcReply("先退出去准备一下", () =>
            {
                if (owner.DeploymentWorldActive) owner.EndDeploymentWorld();
                ExitBattleDeployment();
            });
            bool known = owner.State.Demo.RuinsControllerHeard;
            if (!known && !owner.Change(s => s.Demo.RuinsControllerHeard = true, "")) return;
            string portrait = known ? "galan" : "unknown-speaker";
            string line = known ? "怎么又是你，没完了是吧" : "何人聒噪，吾乃古裔精锐迦兰是也，外来者速速退去，此地不是尔等所能沾染的。";
            NpcDialogue(new[] { new NpcLine(BaseScriptNames.Galan, portrait, line) }, new[] {
                new NpcReply("继续推进", () => {
                    if (known) { proceed(); return; }
                    NpcDialogue(new[] { new NpcLine(BaseScriptNames.Galan, portrait,
                        "敬酒不吃吃罚酒，哼，可惜不能继续增加人数了，你们两个跟我上。") },
                        Array.Empty<NpcReply>(), lockExitUntilFinished:true, onCompleted:proceed);
                }), retreat });
        }
        internal void CloseRuinsBriefing()
        {
            if(owner.DeploymentWorldActive)owner.ResumeDeploymentWorld();
            else ClosePanel();
        }
        public void OpenRuinsRescue(Action resume)
        {
            NpcDialogue(new[]{
                new NpcLine("获救百姓甲","villager","脑子里像蒙了层雾，手脚全不听使唤……多谢你。"),
                new NpcLine("获救百姓乙","villager","现在腿能动了。先扶我一把，出去再说。")
            },new[]{new NpcReply("去撤离点",resume)},lockExitUntilFinished:true);
        }
        public void OpenRescuedCivilian(string id,bool afterExtraction=false)
        {
            var state=owner.State;
            if(BaseSceneRules.Active(state)&&BaseSceneRules.Entrance(state)&&state.Demo.Rescue?.Stage==1)
            {
                NpcDialogue(new[]{
                    new NpcLine("获救百姓乙","villager","总算能喘口气了。多亏你手下留情，我们才出得来。"),
                    new NpcLine("获救百姓甲","villager","我们这就回灰岩镇。见了霍恩爵士，会把经过说清楚。")
                },new[]{new NpcReply("路上小心，我们城里见",()=>owner.ReturnRescuedVillagers(afterExtraction)),new NpcReply("稍后再谈",ClosePanel)});return;
            }
            bool first=id=="demo_rescued_1";
            bool inTown=state.Campaign.Location=="town"&&state.Demo.Rescue?.Stage==3;
            string speech;
            if(inTown)
            {
                speech=first?"回镇才算缓过神。要没你，我们还困在那座窟里。":
                    BaseDemoDefense.Delivered(state)?"爵士已经听过经过。多亏有你，我们才回得了家。":"营地的事去找爵士吧。古窟里的经过，我们替你作证。";
            }
            else speech=first?"刚才脑子里一片空白。你是来救我们的？":"你救了我们，见到霍恩爵士，我会把这话说清楚。";
            NpcDialogue(new[]{new NpcLine(first?"获救百姓甲":"获救百姓乙","villager",speech)},new[]{
                new NpcReply("查看"+BaseScriptNames.Ruins+"副本奖励",OpenRuinsReward,unlocked:!string.IsNullOrEmpty(state.Demo.RuinsRewards?.PendingBattleId)),
                new NpcReply(inTown?"回头见":"保重，尽快离开"+BaseScriptNames.Ruins,ClosePanel)
            });
        }
        internal static bool HasDungeonAftermath(BaseState state, string battle) =>
            state.Aftermath.Prisoners.Any(p => p.BattleId == battle && p.Status == "held") ||
            state.Aftermath.Loot.Any(item => item.BattleId == battle && (!item.Closed || item.HasReceipt));

        public void OpenRuinsReward() => OpenRuinsReward(BasePrepRoute.Ruins);
        internal void OpenRuinsReward(BasePrepRoute route)
        {
            var s=owner.State;
            var rules=s.Demo.RuinsRewards;
            string battle=BaseRuinsRewards.PendingBattle(s,route);
            string eventId=BaseRuinsRewards.EventId(route);
            if(battle=="")
            {
                var reports=s.Campaign.Reports.Where(report=>report.EventId==eventId);
                var available = reports.LastOrDefault(report => HasDungeonAftermath(s, report.BattleId));
                if (available != null) { OpenLoot(available.BattleId); return; }
            }
            BeginPanel(BaseRuinsRewards.DungeonName(route)+" · 奖励","ruins-reward");activeObject="ruins-reward";interactionObject="";
            if(battle=="")
            {
                Line(BaseRuinsRewards.ClaimProblem(s,route),75,21);
                RowButton("关闭",0,800,ClosePanel);row+=60;EndPanel();return;
            }
            int pendingCost=BaseRuinsRewards.PendingCost(s,route);
            Line("体力 "+BaseRuinsRewards.Energy(s)+" / "+BaseRuinsRewards.Maximum(s)+"。"+(pendingCost==0?"本次领奖免费。":"领奖消耗 "+pendingCost+"。"),65,23);
            Line(string.Format(StoryText.Get("story.ui.dungeon.reward_items", "副本奖励 · 钱币 {0}、个人装备 ×1、锻造石 ×{1}，有机会获得基础兵书。"),rules.Coins,rules.ItemCount),110,21);
            string problem=BaseRuinsRewards.ClaimProblem(s,route);Line(problem==""?"本场已胜利，可以领取一次。":problem,75,21);
            RowButton(pendingCost==0?"免费领取本次奖励":"消耗体力 · 领取本次奖励",0,800,()=>owner.ClaimRuinsReward(route),problem=="");row+=60;
            RowButton("放弃本次副本奖励",0,800,()=>owner.SkipRuinsReward(route));row+=60;
            if(HasDungeonAftermath(s, battle))
            {RowButton("查看战果与俘虏",0,800,()=>OpenLoot(battle));row+=60;}
            RowButton("关闭",0,800,ClosePanel);row+=60;EndPanel();
        }
        private void OpenRuinsChallenge()
        {
            var state = owner.State;
            if (!BaseDemoRuins.CanExplore(state)) { ClosePanel(); return; }
            if (!state.Demo.RuinsPlayerHeard)
            {
                NpcDialogue(new[] { new NpcLine("你", "", "这遗迹不对劲，我感觉身体很热", true) },
                    Array.Empty<NpcReply>(), lockExitUntilFinished:true, onCompleted:() => {
                        if (owner.Change(s => s.Demo.RuinsPlayerHeard = true, "")) OpenRuinsChallenge();
                    });
                return;
            }
            if (!state.Demo.RuinsAzhuHeard && BaseAdventure.Following(state, "partner_craft"))
            {
                NpcDialogue(new[] { new NpcLine("阿铸", "azhu", "我早听说这里面有古怪，还挺难受的，不过对我问题不大，要是村民真在里面那可糟了") },
                    Array.Empty<NpcReply>(), lockExitUntilFinished:true, onCompleted:() => {
                        if (owner.Change(s => s.Demo.RuinsAzhuHeard = true, "")) OpenRuinsChallenge();
                    });
                return;
            }
            if (!state.Demo.RuinsAheHeard && BaseAdventure.Following(state, "partner_farm"))
            {
                var hero = state.Leaders.Single(l => l.Id == state.PlayerLeaderId);
                NpcDialogue(new[] { new NpcLine("阿禾", "ahe", hero.Name + "，我感觉很不舒服，我感觉在里面呆久了会有危险，我可以在外面等着吗？") },
                    new[] { new NpcReply("在洞口等我。", () => AnswerAhe(true)), new NpcReply("和我一起进去。", () => AnswerAhe(false)) });
                return;
            }
            OpenBattlePreparation(BasePrepRoute.Ruins);

            void AnswerAhe(bool wait)
            {
                if (owner.Change(s => {
                    if (wait && !BaseAdventure.Pause(s, "partner_farm"))
                        throw new InvalidOperationException(BaseAdventure.PauseProblem(s,"partner_farm"));
                    s.Demo.RuinsAheHeard = true;
                }, "")) OpenRuinsChallenge();
            }
        }
        public void OpenRuinsSweep(string mode="full")
        {
            var s=owner.State;var p=BaseRuinsSweep.Preview(s,mode);BeginPanel(BaseScriptNames.Ruins+" · 扫荡确认","ruins-sweep");activeObject="ruins-sweep";interactionObject="";
            RowButton("尽量配足军需"+(mode=="full"?" · 已选":""),-205,390,()=>OpenRuinsSweep("full"));RowButton("不使用军需"+(mode=="none"?" · 已选":""),205,390,()=>OpenRuinsSweep("none"));row+=60;
            Line("我方战力 "+p.AllyPower.ToString("0")+" / 敌方 "+p.EnemyPower.ToString("0")+" ≈ "+(p.EnemyPower>1e-9?(p.AllyPower/p.EnemyPower).ToString("P0"):"—")+"（需 >150%）；体力 "+BaseRuinsRewards.Energy(s)+" / "+BaseRuinsRewards.Maximum(s)+"，胜利后领奖消耗"+BaseRuinsRewards.Cost(s)+"。",50,21);
            if(p.Problem!="")Line(p.Problem,85,21);
            else Line("确认后结算战损和军需消耗，停留在当天。",50,20);
            RowButton("确认扫荡",-205,390,()=>owner.SweepDemoRuins(mode),p.Problem=="");RowButton("取消",205,390,OpenRuinsChallenge);row+=60;
            EndPanel();
        }
        public void OpenSweepResult(BaseSweepPreview result, BasePrepRoute route = BasePrepRoute.Ruins)
        {
            BeginPanel(BaseRuinsRewards.DungeonName(route)+" · 扫荡完成","ruins-sweep-result");activeObject="ruins-sweep-result";interactionObject="";
            string summary=route==BasePrepRoute.Hideout
                ? string.Format(StoryText.Get("story.ui.hideout.sweep_result", "战损已结算 · 基础伤损 {0:P0} · 阵亡士兵 {1} 人"),result.Injury,result.Deaths)
                : "本场已胜利并保存战损；基础伤损 "+result.Injury.ToString("P0")+"，普通人额外侵蚀 "+result.Exposure.ToString("P0")+"（按最大生命）；普通兵阵亡 "+result.Deaths+"人。";
            Line(summary,75,23);SweepLossLines(result);
            RowButton("查看奖励",0,800,()=>OpenRuinsReward(route));row+=60;
            if(HasDungeonAftermath(owner.State, result.BattleId))
            {RowButton("查看战果与俘虏",0,800,()=>OpenLoot(result.BattleId));row+=60;}
            RowButton("关闭",0,800,ClosePanel);row+=60;EndPanel();
        }
        private void SweepLossLines(BaseSweepPreview result)
        {
            foreach(var loss in result.Losses.Where(l=>l.Leader))Line(loss.Name+"：生命 "+loss.Before.ToString("0")+" → "+loss.After.ToString("0"),30,21);
            var troops=result.Losses.Where(l=>!l.Leader&&!l.Dead).ToArray();
            if(troops.Length>0)Line("存活士兵 "+troops.Length+"人，合计生命 "+troops.Sum(l=>l.Before).ToString("0")+" → "+troops.Sum(l=>l.After).ToString("0"),36,20);
        }
    }
}
