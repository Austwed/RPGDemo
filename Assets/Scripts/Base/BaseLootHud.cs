using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using BaseConquest.Battle;
using BaseConquest.Presentation;
using BaseConquest.Story;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private string lootTab="items",lootSelected="",lootConfirmation="",lootBattle;
        private void LootChange(Func<BaseState,bool> action,string message)
        {owner.Change(s=>{if(!action(s))throw new InvalidOperationException("操作未完成：请检查背包空格、人数上限或当前条目状态。");},message);OpenLoot();}
        public void OpenLoot() => OpenLoot(activeObject=="loot" ? lootBattle : null);
        internal void OpenLoot(string battle)
        {
            var s=owner.State;if(s==null||s.Aftermath.GameOver)return;
            if(BaseDemo.Active(s)){OpenDemoReward(battle);return;}
            var m=s.Aftermath.Loot.FirstOrDefault(x=>!x.Closed&&(battle==null||x.BattleId==battle));
            if(m==null){ClosePanel();return;}
            lootBattle=m.BattleId;
            activeObject="loot";BeginPanel("奖励 · "+m.Title,"loot:"+m.BattleId+":"+lootTab);
            var report=s.Campaign.Reports.FirstOrDefault(r=>r.BattleId==m.BattleId);if(!string.IsNullOrEmpty(report?.RecoveryNote))Line(report.RecoveryNote,70,20);
            if(report?.EventId==BaseDemoDefense.EventId)Line("本场Demo敌方队长固定击倒，可尝试招降。队长的技能与特性各不相同，营帐满员时可暂留俘虏。",76,19);
            else if(report!=null&&s.Demo.Finale!=null&&report.EventId==BaseThreats.Site(s,BaseDemoFinale.Camp)?.AttackEventId)Line("本场Demo敌方队长固定战死，无法招降。一般战斗中队长可能直接死亡；收编前请查看实际技能与特性。",76,19);
            panel.GetComponent<UnityEngine.UI.Image>().color=new Color(.07f,.13f,.15f,.92f);
            RowButton("处理完毕 · 继续",-290,270,()=>{if(owner.Change(next=>{if(!BaseBattleLoot.Finish(next,m.BattleId))throw new InvalidOperationException("请先处理全部道具和俘虏");},"战利品已处理"))ClosePanel();},BaseBattleLoot.CanFinish(s,m));
            RowButton("整理小队背包 · B",0,270,OpenBackpack);
            RowButton("规则 i",290,270,()=>RuleInfo("战利品处理规则",(BaseRpg.Active(s)?"处理完全部道具后可保留俘虏继续，俘虏不会丢失；营帐满员可回营升级后再招降。\n":"")+"必须处理全部道具后才能离开。可打开B背包拖放、换装、整理或确认销毁；关闭背包回到本清单。领取失败不会丢失奖励。\n\n普通道具进入随行队员背包，不远程送仓库；放弃不可恢复。战略物资已一次计入库存，只在此展示。\n\n招降加入己方，姓名为棕色，规则与其他单位一致。测试招降100%；非测试可能拒绝，拒绝也结束本次处置。处死永久移除；释放离开名单但不记为死亡，特殊剧情以后设计。",OpenLoot));row+=62;
            int held=s.Aftermath.Prisoners.Count(p=>p.BattleId==m.BattleId&&p.Status=="held");
            RowButton("奖励 · 待领取 "+m.Items.Count(i=>i.Status=="pending"),-210,400,()=>{lootTab="items";lootConfirmation="";OpenLoot();});
            RowButton("俘虏 · 待处理 "+held,210,400,()=>{lootTab="prisoners";lootConfirmation="";OpenLoot();});row+=62;
            RowButton("一键领取",-210,400,()=>LootChange(next=>BaseBattleLoot.CollectAll(next,m.BattleId)>0,"已按清单与随行顺序领取；装不下的奖励仍保留"),m.Items.Any(i=>i.Status=="pending"));
            RowButton("一键招募",210,400,()=>RecruitLootAll(m.BattleId),held>0);row+=62;
            if(lootTab=="items")
            {
                Line("战略物资已入账：粮 "+m.Food.ToString("+0;-0;0")+"／原料 "+m.Materials.ToString("+0;-0;0")+"／钱币 "+m.Coins.ToString("+0;-0;0"),52,18);
                foreach(var line in m.Items)
                {var item=line;RowButton(BaseBattleLoot.Name(s,item)+" · "+(item.Status=="taken"?"已领取":item.Status=="discarded"?"已放弃":"待领取"),0,820,()=>{lootSelected=item.Id;lootConfirmation="";OpenLoot();});row+=56;}
                var chosen=m.Items.FirstOrDefault(i=>i.Id==lootSelected&&i.Status=="pending")??m.Items.FirstOrDefault(i=>i.Status=="pending");
                if(chosen!=null)
                {
                    Line("选择："+BaseBattleLoot.Name(s,chosen),48,21);
                    if(chosen.Reward.Template!="")GearItemLines(new BasePersonalItem {Template=chosen.Reward.Template,Quality=chosen.Reward.Quality});
                    foreach(var person in BaseAdventure.Members(s).Where(l=>BasePartyInventory.AccessibleLeader(s,l)))
                    {string holder=person.Id;int used=BasePartyInventory.Entries(s).Count(e=>e.Holder==holder&&!e.Worn);RowButton("交给 "+person.Name+" · 背包 "+used+"/"+BasePartyInventory.Capacity,0,820,()=>LootChange(next=>BaseBattleLoot.Resolve(next,m.BattleId,chosen.Id,holder),"已领取道具"));row+=56;}
                    RowButton("放弃此物品",0,820,()=>{lootConfirmation=chosen.Id;OpenLoot();});row+=56;
                    if(lootConfirmation==chosen.Id){Line("确认永久放弃此物品？",48,20);RowButton("确认放弃",-210,400,()=>{lootConfirmation="";LootChange(next=>BaseBattleLoot.Resolve(next,m.BattleId,chosen.Id,"",true),"已放弃物品");});RowButton("取消",210,400,()=>{lootConfirmation="";OpenLoot();});row+=56;}
                }
                else Line("道具已处理完毕。请处理俘虏，再点击上方继续。",65,20);
            }
            else
            {
                foreach(var p in s.Aftermath.Prisoners.Where(p=>p.BattleId==m.BattleId&&p.Status=="held").ToArray())
                {
                    string id=p.Id;Line(UnitDisplayNames.Mark(p.Name,true)+" · "+(p.Kind==BaseConquest.Battle.SoldierKind.Hero?"指挥者":"士兵")+" · 生命 "+p.Health.ToString("0.#")+"/"+p.Stats.Health.ToString("0.#"),50,20);
                    RowButton("招降",-290,270,()=>RecruitPrisoner(id,OpenLoot));
                    RowButton("释放",0,270,()=>{lootConfirmation="release:"+id;OpenLoot();});
                    RowButton("处死",290,270,()=>{lootConfirmation="execute:"+id;OpenLoot();});row+=56;
                    foreach(string action in new[]{"release","execute"})if(lootConfirmation==action+":"+id)
                    {string decision=action;Line(action=="execute"?"确认处死？此单位将永久移除。":"确认释放？此单位将离开，不能再招降。",52,19);RowButton("确认",-210,400,()=>{lootConfirmation="";LootChange(next=>BaseAftermath.Decide(next,id,decision),"已处置俘虏");});RowButton("取消",210,400,()=>{lootConfirmation="";OpenLoot();});row+=56;}
                }
                if(held==0)Line("本场俘虏已全部处理。",60);
            }
            EndPanel();
        }
        private void OpenDemoReward(string battle)
        {
            var state=owner.State;
            var manifest=state.Aftermath.Loot.FirstOrDefault(m=>(!m.Closed||m.HasReceipt)&&(battle==null||m.BattleId==battle));
            if(manifest==null)
            {
                if(battle!=null&&state.Aftermath.Prisoners.Any(p=>p.BattleId==battle&&p.Status=="held"))OpenRewardPrisoners(battle);
                else ClosePanel();
                return;
            }
            battle=manifest.BattleId;
            interactionObject="";
            BaseLootManifest receipt=null;
            if(!owner.Change(next=>receipt=BaseBattleLoot.ConsumeReceipt(next,battle),""))return;
            bool held=owner.State.Aftermath.Prisoners.Any(p=>p.BattleId==battle&&p.Status=="held");
            if(!receipt.HasReceipt){if(held)OpenRewardPrisoners(battle);else ClosePanel();return;}
            var lines=receipt.Items.Where(i=>!string.IsNullOrEmpty(i.ReceiptDestination)).Select(i=>(
                Item: BaseBattleLoot.Name(owner.State,i),
                Destination: i.ReceiptDestination==BaseBattleLoot.WarehouseReceipt?StoryText.Get("ui.reward.warehouse","仓库"):
                    (owner.State.Leaders.FirstOrDefault(l=>l.Id==i.ReceiptDestination)?.Name??"")+StoryText.Get("ui.reward.backpack","的背包"))).ToList();
            if(receipt.ReceiptFood>0)lines.Insert(0,(StoryText.Get("ui.reward.food","粮食")+" +"+receipt.ReceiptFood,""));
            if(receipt.ReceiptMaterials>0)lines.Insert(0,(StoryText.Get("ui.reward.materials","原料")+" +"+receipt.ReceiptMaterials,""));
            if(receipt.ReceiptCoins>0)lines.Insert(0,(StoryText.Get("ui.reward.coins","钱币")+" +"+receipt.ReceiptCoins,""));
            OpenRewardReceipt(receipt.Title,lines.ToArray(),held?()=>OpenRewardPrisoners(battle):(Action)null);
        }
        private void OpenRewardReceipt(string title,(string Item,string Destination)[] lines,Action prisoners)
        {
            activeObject="reward-receipt";
            BeginPanel(StoryText.Get("ui.reward.received","获得奖励")+" · "+title,"reward-receipt");
            var view=panel;
            var viewport=panelScroll.viewport;
            const float itemWidth = 314f;
            float destinationWidth = Mathf.Max(156f, lines.Select(line => line.Destination.Length * 18f).DefaultIfEmpty(0).Max());
            float width = itemWidth + destinationWidth + 66;
            float listHeight=Mathf.Clamp(lines.Length*34+12,56,240),height=listHeight+(prisoners!=null?110:66);
            view.sizeDelta=new Vector2(width,height);
            var header=view.Find("Header") as RectTransform;
            if(header!=null)Pin(header,new Vector2(0,1),new Vector2(1,1),new Vector2(0,-45),Vector2.zero);
            var caption=(view.Find("Title")??view.GetComponentsInChildren<Text>(true).First().transform) as RectTransform;
            Pin(caption,new Vector2(0,1),new Vector2(1,1),new Vector2(16,-44),new Vector2(-90,0));
            var close=view.Find("Close") as RectTransform;
            if(close==null)close=view.GetComponentsInChildren<Button>(true).First().GetComponent<RectTransform>();
            close.GetComponent<Button>().interactable=true;
            Pin(close,Vector2.one,Vector2.one,new Vector2(-81,-40),new Vector2(-8,-5));
            Pin(viewport,new Vector2(0,1),new Vector2(1,1),new Vector2(14,-52-listHeight),new Vector2(-14,-52));
            content.sizeDelta=new Vector2(width-28,Mathf.Max(listHeight,lines.Length*34+8));
            panelScroll.vertical=lines.Length*34+8>listHeight;
            for(int i=0;i<lines.Length;i++)
            {
                var y = -21 - i * 34;
                var line = lines[i];
                Label(content,line.Item,new Vector2(27-width*.5f+itemWidth*.5f,y),new Vector2(itemWidth,34),18,TextAnchor.MiddleLeft,new Vector2(.5f,1));
                if (!string.IsNullOrEmpty(line.Destination))
                    Label(content,line.Destination,new Vector2(width*.5f-27-destinationWidth*.5f,y),new Vector2(destinationWidth,34),18,TextAnchor.MiddleRight,new Vector2(.5f,1));
            }
            if(prisoners!=null)MakeButton(view,StoryText.Get("ui.reward.prisoners","处理俘虏"),new Vector2(0,29),new Vector2(160,38),prisoners,new Vector2(.5f,0));
            RefreshSummary();
            void Pin(RectTransform node,Vector2 min,Vector2 max,Vector2 low,Vector2 high)
            {node.anchorMin=min;node.anchorMax=max;node.offsetMin=low;node.offsetMax=high;}
        }
        private void OpenRewardPrisoners(string battle)
        {
            activeObject="reward-prisoners";
            interactionObject="";
            var state=owner.State;
            var held=state.Aftermath.Prisoners.Where(p=>p.BattleId==battle&&p.Status=="held").ToArray();
            if(held.Length==0){ClosePanel();return;}
            BeginPanel(StoryText.Get("ui.reward.prisoners","处理俘虏"),"reward-prisoners:"+battle);
            Line("释放后无法再招降，处死将永久移除。",42,18);
            RowButton("一键招募",0,800,()=>
            {
                owner.Change(next=>BaseBattleLoot.RecruitAll(next,battle),"");OpenRewardPrisoners(battle);
            });row+=54;
            foreach(var p in held)
            {
                string id=p.Id;
                Line(UnitDisplayNames.Mark(p.Name,true)+" · 生命 "+p.Health.ToString("0.#")+" / "+p.Stats.Health.ToString("0.#"),46,19);
                RowButton("招降",-290,270,()=>RecruitPrisoner(id,()=>OpenRewardPrisoners(battle)),p.Kind!=BaseConquest.Battle.SoldierKind.Hero||BaseAftermath.CanRecruitLeader(state));
                RowButton("释放",0,270,()=>DisposePrisoner(id,"release"));
                RowButton("处死",290,270,()=>DisposePrisoner(id,"execute"));row+=54;
            }
            EndPanel();
            void DisposePrisoner(string id, string action)
            {
                owner.Change(next=>{if(!BaseAftermath.Decide(next,id,action))throw new InvalidOperationException("俘虏状态已变化");},"");
                OpenRewardPrisoners(battle);
            }
        }
        private void RenderTradeSale(RectTransform details)
        {
            var s=owner.State;
            var item=BasePartyInventory.Find(s,tradeSale);
            if(item==null||item.Holder!=tradePerson)
            {
                TradeLine(details,"请选择背包物品");
                return;
            }
            string id=item.Id,holder=item.Holder;
            int price=BaseBattleLoot.SellPrice(s,item);
            TradeLine(details,item.Name+(item.Count>1?" ×"+item.Count:""));
            if(item.Worn)TradeLine(details,"已穿戴，请先卸下");
            else if(price==0)TradeLine(details,"此物品不可出售");
            else TradeLine(details,"出售 1 件 · 获得 "+price+"币");
            TradeButton(details,"确认出售",()=>TradeCommit(next=>{
                var entry=BasePartyInventory.Find(next,id);
                return entry?.Holder==holder&&BaseBattleLoot.SellPrice(next,entry)==price&&BaseBattleLoot.Sell(next,id);
            },"已出售 "+item.Name),BaseBattleLoot.CanSell(s,id));
            TradeButton(details,"返回货架",()=>{tradeSale=tradeFeedback="";RenderTownTrade();});
            TradeLine(details,item.Description);
        }

    }
}
