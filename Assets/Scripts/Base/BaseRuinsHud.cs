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
            if(BaseAdventure.Following(s,"partner_farm"))lines.Add(new NpcLine("阿禾","ahe","［咳了两声］这风一吸进去，胸口烧得慌。"));
            if(BaseAdventure.Following(s,"partner_craft"))lines.Add(new NpcLine("阿铸","azhu","这股黑风我扛得住。寻常人进去，可得当心。"));
            lines.Add(new NpcLine(BaseScriptNames.Ruins+"警告","","严重警告：当前出战队伍有普通人。仅副本内，非异族、非异能者每秒损失最大生命的"+(BaseRuinsRewards.Exposure(s)*100).ToString("0.##")+"%；异族和异能者免受侵蚀伤害；"+BaseScriptNames.Ruins+"中受迦兰魂丝保护的村民暂免侵蚀。入口野外没有侵蚀，暂离成员不会进入副本。战斗中存在真实战损，请谨慎决定。"));
            NpcDialogue(lines.ToArray(),new[]{new NpcReply("知晓危险，仍让当前队伍进入",proceed),new NpcReply("暂不进入，先调整随行",owner.CancelRuinsEntry)});
        }
        public void OpenRuinsConfrontation(Action proceed)
        {
            var fight=new NpcReply("准备交战",proceed);
            var defer=new NpcReply("暂不交战",CloseRuinsBriefing);
            NpcDialogue(new[]{
                new NpcLine("迦兰","galan","站住。他们吸了瘴气，全靠我护着。"),
                new NpcLine("迦兰","galan","我这点织魂……这点手段一松，他们就撑不住了。"),
                new NpcLine("你","","他们连一步都挪不了。先制住他，别伤镇民。",true)
            },new[]{
                fight,
                new NpcReply("追问他们为何不能动",()=>NpcDialogue(new[]{
                    new NpcLine("你","","你说在救他们。那他们怎么连手脚都动不了？",true),
                    new NpcLine("迦兰","galan","［杖尖点地，两人挪上前］再走一步，他们就得拦你。")
                },new[]{fight,defer})),
                defer
            });
        }
        internal void CloseRuinsBriefing()
        {
            if(owner.DeploymentWorldActive)owner.ResumeDeploymentWorld();
            else ClosePanel();
        }
        public void OpenRuinsVictory()
        {
            var s=owner.State;
            bool first=s.Campaign.Reports.Count(r=>r.EventId==BaseDemoRuins.EventId&&r.Outcome=="SideAWin")==1;
            if(!first){OpenRuinsReward();return;}
            NpcDialogue(new[]{
                new NpcLine("获救百姓甲","villager","脑子里像蒙了层雾，手脚全不听使唤……多谢你。"),
                new NpcLine("获救百姓乙","villager","现在腿能动了。先扶我一把，出去再说。"),
                new NpcLine("你","","先到洞外透气。古窟的事，我回镇上向霍恩交代。",true)
            },new[]{new NpcReply("继续",ClosePanel)});
        }
        public void OpenRescuedCivilian(string id)
        {
            var state=owner.State;
            if(BaseSceneRules.Active(state)&&BaseSceneRules.Entrance(state)&&state.Demo.Rescue?.Stage==1)
            {
                NpcDialogue(new[]{
                    new NpcLine("获救百姓乙","villager","总算能喘口气了。多亏你手下留情，我们才出得来。"),
                    new NpcLine("获救百姓甲","villager","我们这就回灰岩镇。见了霍恩爵士，会把经过说清楚。")
                },new[]{new NpcReply("路上小心，我们城里见",owner.ReturnRescuedVillagers),new NpcReply("稍后再谈",ClosePanel)});return;
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
                var loot=s.Aftermath.Loot.FirstOrDefault(item=>!item.Closed&&reports.Any(report=>report.BattleId==item.BattleId));
                if(loot!=null){OpenLoot(loot.BattleId);return;}
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
            if(route==BasePrepRoute.Hideout&&s.Aftermath.Loot.Any(item=>item.BattleId==battle&&!item.Closed))
            {RowButton("查看战果与俘虏",0,800,()=>OpenLoot(battle));row+=60;}
            RowButton("关闭",0,800,ClosePanel);row+=60;EndPanel();
        }
        private void OpenRuinsChallenge()
        {
            var state = owner.State;
            if (BaseDemoRuins.CanExplore(state)) { OpenBattlePreparation(BasePrepRoute.Ruins); return; }
            ClosePanel();
        }
        public void OpenRuinsSweep(string mode="full")
        {
            var s=owner.State;var p=BaseRuinsSweep.Preview(s,mode);BeginPanel(BaseScriptNames.Ruins+" · 扫荡确认","ruins-sweep");activeObject="ruins-sweep";interactionObject="";
            Line("已首通，且我方战力严格超过敌方 1.5 倍才可扫荡（正好 1.5 倍不放）。确认前只给战力比，伤损在确认后结算，队长最低保留1生命。",48,21);
            RowButton("尽量配足军需"+(mode=="full"?" · 已选":""),-205,390,()=>OpenRuinsSweep("full"));RowButton("不使用军需"+(mode=="none"?" · 已选":""),205,390,()=>OpenRuinsSweep("none"));row+=60;
            Line("我方战力 "+p.AllyPower.ToString("0.00")+" / 敌方 "+p.EnemyPower.ToString("0.00")+" ≈ "+(p.EnemyPower>1e-9?(p.AllyPower/p.EnemyPower).ToString("0.00"):"—")+" 倍（需 >1.5）；体力 "+BaseRuinsRewards.Energy(s)+" / "+BaseRuinsRewards.Maximum(s)+"，胜利后领奖消耗"+BaseRuinsRewards.Cost(s)+"。",50,21);
            if(p.Problem!="")Line(p.Problem,85,21);
            else Line("确认后按该战力比结算伤损，不预告具体损失；不耗游戏日，军需照常磨损。",58,21);
            RowButton("确认扫荡",-205,390,()=>owner.SweepDemoRuins(mode),p.Problem=="");RowButton("取消",205,390,OpenRuinsChallenge);row+=60;
            Line("不耗游戏日，军需照常磨损；体力不足不能领奖。扫荡不解锁“破咒不伤人”。",60,19);EndPanel();
        }
        public void OpenSweepResult(BaseSweepPreview result, BasePrepRoute route = BasePrepRoute.Ruins)
        {
            BeginPanel(BaseRuinsRewards.DungeonName(route)+" · 扫荡完成","ruins-sweep-result");activeObject="ruins-sweep-result";interactionObject="";
            string summary=route==BasePrepRoute.Hideout
                ? string.Format(StoryText.Get("story.ui.hideout.sweep_result", "战损已结算 · 基础伤损 {0:P1} · 阵亡士兵 {1} 人"),result.Injury,result.Deaths)
                : "本场已胜利并保存战损；基础伤损 "+result.Injury.ToString("P1")+"，普通人额外侵蚀 "+result.Exposure.ToString("P0")+"（按最大生命）；普通兵阵亡 "+result.Deaths+"人。";
            Line(summary,75,23);SweepLossLines(result);
            RowButton("查看奖励",0,800,()=>OpenRuinsReward(route));row+=60;
            if(owner.State.Aftermath.Loot.Any(item=>item.BattleId==result.BattleId&&!item.Closed))
            {RowButton("查看战果与俘虏",0,800,()=>OpenLoot(result.BattleId));row+=60;}
            RowButton("关闭",0,800,ClosePanel);row+=60;EndPanel();
        }
        private void SweepLossLines(BaseSweepPreview result)
        {
            foreach(var loss in result.Losses.Where(l=>l.Leader))Line(loss.Name+"：生命 "+loss.Before.ToString("0.#")+" → "+loss.After.ToString("0.#"),30,21);
            var troops=result.Losses.Where(l=>!l.Leader&&!l.Dead).ToArray();
            if(troops.Length>0)Line("存活士兵 "+troops.Length+"人，合计生命 "+troops.Sum(l=>l.Before).ToString("0.#")+" → "+troops.Sum(l=>l.After).ToString("0.#"),36,20);
        }
    }
}
