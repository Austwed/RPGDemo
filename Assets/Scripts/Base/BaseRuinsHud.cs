using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        public void OpenRuinsWarning(Action proceed, BaseBattlePreparation prepared = null)
        {
            var s=owner.State;var lines=new List<NpcLine>();
            if(BaseAdventure.Following(s,"partner_farm"))lines.Add(new NpcLine("阿禾","ahe","［打了个寒颤］洞口吹出来的黑风好邪门，吸一口胸口就像塞了团火炭……"));
            if(BaseAdventure.Following(s,"partner_craft"))lines.Add(new NpcLine("阿铸","azhu","里头瘴气太毒！咱们燃脉者扛得住，普通弟兄进去怕是撑不了几息！"));
            bool danger=prepared == null ? BaseRuinsRewards.VulnerableParty(s) : prepared.Units.Any(unit =>
                prepared.Positions.ContainsKey(unit.Id) && (unit.Leader == null || !BaseRuinsRewards.Immune(s, unit.Leader)));
            lines.Add(new NpcLine(BaseScriptNames.Ruins+"警告","",(danger?"严重警告：当前随行队伍有普通人。":BaseScriptNames.Ruins+"副本存在侵蚀。")+"仅副本内，非异族、非异能者每秒损失最大生命的"+(BaseRuinsRewards.Exposure(s)*100).ToString("0.##")+"%；异族和异能者免受侵蚀伤害；"+BaseScriptNames.Ruins+"中受迦兰魂丝保护的村民暂免侵蚀。入口野外没有侵蚀，暂离成员不会进入副本。战斗中存在真实战损，请谨慎决定。"));
            NpcDialogue(lines.ToArray(),new[]{new NpcReply(danger?"知晓危险，仍让当前队伍进入":"继续进入",proceed),new NpcReply("暂不进入，先调整随行",owner.CancelRuinsEntry)});
        }
        public void OpenRuinsConfrontation(Action proceed)
        {
            NpcDialogue(new[]{new NpcLine("迦兰","galan","别伤他们！我用魂丝护住了他们，才让他们撑到现在。"),
                new NpcLine("迦兰","galan","我若松开魂丝，瘴气立刻会要他们的命。"),
                new NpcLine("你","","先想办法制住他。不要伤到村民。",true)},
                new[]{new NpcReply("准备交战",proceed),new NpcReply("暂不交战",ClosePanel)});
        }
        public void OpenRuinsVictory()
        {
            var s=owner.State;
            bool first=s.Campaign.Reports.Count(r=>r.EventId==BaseDemoRuins.EventId&&r.Outcome=="SideAWin")==1;
            if(!first){OpenRuinsReward();return;}
            NpcDialogue(new[]{
                new NpcLine("获救百姓甲","villager","刚才脑子里像蒙着雾，身体也不听使唤……现在清醒了。谢谢你救了我们！"),
                new NpcLine("获救百姓乙","villager","我们两个都恢复神智了。你尽管来和我们说话；我们一定去领主那里替你说好话。"),
                new NpcLine("战果提示","","控制者已被击倒，可以在战果中招降；每名队长的技能和特性各不相同。本次奖励已加入清单，不消耗体力。"),
                new NpcLine("你","","先离开危险的地方。"+BaseScriptNames.Ruins+"的调查证据我会带回去；即使之前没接委托，也可以向领主说明经过。",true)
            },new[]{new NpcReply("查看奖励",OpenLoot),new NpcReply("先与两位百姓交流",ClosePanel)});
        }
        public void OpenRescuedCivilian(string id)
        {
            if(BaseSceneRules.Active(owner.State)&&BaseSceneRules.Entrance(owner.State))
            {
                NpcDialogue(new[]{new NpcLine("获救百姓乙","villager","总算呼吸到外头的活气了！要不是你剑下留情，我们哥俩就交代在里头了。"),new NpcLine("获救百姓甲","villager","我们这就回灰岩镇面见霍恩爵士，一定在老爷面前替你好好美言几句！")},new[]{new NpcReply("路上小心，我们城里见",owner.ReturnRescuedVillagers),new NpcReply("稍后再谈",ClosePanel)});return;
            }
            bool first=id=="demo_rescued_1";
            NpcDialogue(new[]{new NpcLine(first?"获救百姓甲":"获救百姓乙","villager",first?"啊……我这是在哪儿？刚才脑子里一片空白，多谢壮士救命！":"你若想重建营地，就去找领主吧。我们会替你说好话；这份恩情不会忘。")},new[]{new NpcReply("查看"+BaseScriptNames.Ruins+"副本奖励",OpenRuinsReward,!string.IsNullOrEmpty(owner.State.Demo.RuinsRewards?.PendingBattleId)),new NpcReply("保重，尽快离开"+BaseScriptNames.Ruins,ClosePanel)});
        }
        public void OpenRuinsReward()
        {
            var s=owner.State;if(BaseBattleLoot.Pending(s)&&string.IsNullOrEmpty(s.Demo.RuinsRewards?.PendingBattleId)){OpenLoot();return;}BeginPanel(BaseScriptNames.Ruins+" · 奖励","ruins-reward");activeObject="ruins-reward";interactionObject="";
            if(string.IsNullOrEmpty(s.Demo.RuinsRewards?.PendingBattleId))
            {
                var reward=s.Demo.RuinsRewards;
                Line("首次通关奖励包含钱币"+reward.FirstCoins+"、粮食"+reward.FirstFood+"、原料"+reward.FirstMaterials+"及道具，不耗体力。物品从奖励清单领取到背包；领主的委托在交付时结算。再次挑战后消耗体力领取奖励。",150,21);
                RowButton("查看战果 · 道具与可招募队长",0,800,OpenLoot,BaseBattleLoot.Pending(s));row+=60;RowButton("关闭",0,800,ClosePanel);row+=60;EndPanel();return;
            }
            int pendingCost=BaseRuinsRewards.PendingCost(s);
            Line("体力 "+BaseRuinsRewards.Energy(s)+" / "+BaseRuinsRewards.Maximum(s)+"；每日补满。"+(pendingCost==0?"本次领奖免费。":"领奖消耗 "+pendingCost+"。"),65,23);
            Line("副本奖励：钱币 "+(s.Demo.RuinsRewards?.Coins??40)+"、个人装备 ×1、锻造石 ×"+(s.Demo.RuinsRewards?.ItemCount??1)+"。与领主任务首奖分开；先完成本场战斗，再选择是否消耗体力。",110,21);
            string problem=BaseRuinsRewards.ClaimProblem(s);Line(problem==""?"本场已胜利，可以领取一次。":problem,75,21);
            RowButton(pendingCost==0?"免费领取本次奖励":"消耗体力 · 领取本次奖励",0,800,owner.ClaimRuinsReward,problem=="");row+=60;
            RowButton("放弃本次副本奖励",0,800,owner.SkipRuinsReward,!string.IsNullOrEmpty(s.Demo.RuinsRewards?.PendingBattleId));row+=60;
            RowButton("关闭",0,800,ClosePanel);row+=60;EndPanel();
        }
        private void OpenRuinsChallenge()
        {
            var state = owner.State;
            if (BaseDemoRuins.CanExplore(state)) { OpenBattlePreparation(BasePrepRoute.Ruins); return; }
            activeObject = "demo-ruins";
            interactionObject = "";
            BeginPanel(BaseScriptNames.Ruins + " · 调查与整备");
            Line(BaseDemo.Objective(state), 95, 22);
            RowButton("关闭", 0, 800, ClosePanel);
            row += 60;
            EndPanel();
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
        public void OpenSweepResult(BaseSweepPreview result)
        {
            BeginPanel(BaseScriptNames.Ruins+" · 扫荡完成","ruins-sweep-result");activeObject="ruins-sweep-result";interactionObject="";
            Line("本场已胜利并保存战损；基础伤损 "+result.Injury.ToString("P1")+"，普通人额外侵蚀 "+result.Exposure.ToString("P0")+"（按最大生命）；普通兵阵亡 "+result.Deaths+"人。",75,23);SweepLossLines(result);
            RowButton("查看奖励",0,800,OpenRuinsReward);row+=60;RowButton("查看战果与俘虏",0,800,OpenLoot);row+=60;RowButton("关闭",0,800,ClosePanel);row+=60;EndPanel();
        }
        private void SweepLossLines(BaseSweepPreview result)
        {
            foreach(var loss in result.Losses.Where(l=>l.Leader))Line(loss.Name+"：生命 "+loss.Before.ToString("0.#")+" → "+loss.After.ToString("0.#"),30,21);
            var troops=result.Losses.Where(l=>!l.Leader&&!l.Dead).ToArray();
            if(troops.Length>0)Line("存活士兵 "+troops.Length+"人，合计生命 "+troops.Sum(l=>l.Before).ToString("0.#")+" → "+troops.Sum(l=>l.After).ToString("0.#"),36,20);
        }
    }
}
