using System;
using System.Linq;
using BaseConquest.Battle;
namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private int trainingPage;
        private void OpenTraining()
        {
            var s=owner.State;BeginPanel("军营 · 等级训练","level-training");
            RowButton("返回兵营",0,820,()=>{activeObject="barracks";OpenBarracks();});row+=62;
            Line("9级成长，升入3／6／9级用高阶兵书。训练停工、不可出征；开始扣书，取消不退，完成不额外回血。按经营日推进。",90,19);
            Line("普通士兵没有训练与升级；战力由兵种基础、军需品质和数量及临时增益决定。",65,18);
            {
                var people=s.Leaders.Where(l=>BaseAdventure.Alive(l)).ToArray();int pages=Math.Max(1,(people.Length+7)/8);trainingPage=Math.Min(trainingPage,pages-1);
                TrainingPages(pages);
                foreach(var l in people.Skip(trainingPage*8).Take(8))
                {
                    string id=l.Id;Line(l.Name+" · "+BasePersonalEquipment.ClassName(l)+" · Lv"+l.Training.Level+" · 生命 "+l.Health.ToString("0.#")+"/"+l.MaxHealth.ToString("0.#"),52,20);
                    if(BaseTraining.Busy(l.Training)){Line("正在训练至Lv"+l.Training.Target+"，第"+l.Training.EndDay+"天完成",48);RowButton("取消训练（不退兵书）",0,820,()=>TrainingCancel(()=>GearChange(next=>BaseTraining.CancelLeader(next,id),"训练已取消，兵书不退",OpenTraining)));row+=58;continue;}
                    int target=l.Training.Level+1;if(target>9){Line("已达9级",44);continue;}string book=BaseTraining.Book(BasePersonalEquipment.Stats(l).ProfessionKind,target);int need=BaseTraining.Books(l,target),rev=s.Backpack.Revision;
                    Line("下一级Lv"+target+" · "+s.Backpack.Catalog.Single(d=>d.Id==book).Name+" ×"+need+"（可用"+BasePartyInventory.AvailableMaterial(s,book)+"） · "+BaseTraining.Days(l,target)+"天",58,19);
                    string problem=BaseTraining.LeaderProblem(s,l);if(problem!="")Line(problem,42,18);
                    RowButton("训练 "+l.Name+" 至Lv"+target,0,820,()=>GearChange(next=>BaseTraining.StartLeader(next,id,rev),"已开始等级训练",OpenTraining),problem==""&&BasePartyInventory.AvailableMaterial(s,book)>=need);row+=58;
                }
            }
            EndPanel();
        }
        private void TrainingPages(int pages){RowButton("上一页",-210,400,()=>{trainingPage--;OpenTraining();},trainingPage>0);RowButton((trainingPage+1)+"/"+pages+" 下一页",210,400,()=>{trainingPage++;OpenTraining();},trainingPage<pages-1);row+=58;}
        private void TrainingCancel(Action confirm){BeginPanel("确认取消训练");Line("已消耗的兵书不退还，本次训练进度清除；等级保留。",90);RowButton("确认取消训练",-210,400,confirm);RowButton("继续训练",210,400,OpenTraining);row+=60;EndPanel();}
        private void RenderSuppliesTrade(UnityEngine.RectTransform offers,UnityEngine.RectTransform details)
        {
            var s=owner.State;
            var selected=s.PersonalEquipment.Supplies.FirstOrDefault(o=>o.Item==tradeSelection)??s.PersonalEquipment.Supplies.FirstOrDefault();
            if(selected==null)return;
            tradeSelection=selected.Item;
            foreach(var offer in s.PersonalEquipment.Supplies)
            {
                var item=s.Backpack.Catalog.Single(d=>d.Id==offer.Item);
                TradeOffer(offers,item.Id,item.Name+" · "+BaseProgressSupplies.Price(s,item.Id)+"币 · 余 "+offer.Remaining);
            }
            if(details==null)return;
            var chosen=s.Backpack.Catalog.Single(d=>d.Id==selected.Item);
            int price=BaseProgressSupplies.Price(s,chosen.Id),revision=s.Backpack.Revision;
            TradeLine(details,chosen.Name+" · 单价 "+price+"币 · 今日剩余 "+selected.Remaining);
            TradeQuantity(details,BaseProgressSupplies.MaxBuy(s,chosen.Id));
            int count=tradeQuantity;
            TradeButton(details,"确认买入 · "+count*price+"币",()=>TradeCommit(next=>
                BaseProgressSupplies.Price(next,chosen.Id)==price&&BaseProgressSupplies.Buy(next,chosen.Id,count,revision),"已购入补给"),count>0);
        }

        private void OpenWine(string selected="")
        {
            var s=owner.State;BeginPanel("小队 · 酒类补给","wine");RowButton("返回背包",0,820,OpenBackpack);row+=60;
            Line("烈酒：所选队长及其小兵当日攻击＋10%。清酒：下一场攻击＋5%，未开战可跨日保存；不叠加，战斗中不能使用。",86,19);
            var items=s.Backpack.Items.Where(i=>new[]{BaseWine.Strong,BaseWine.Sake}.Contains(i.ItemId)&&BasePartyInventory.CanUseMaterial(s,i)&&(selected==""||i.Id==selected)).ToArray();
            foreach(var l in BaseAdventure.Members(s))Line(l.Name+"："+(l.Wine.Kind==""?"无酒类效果":l.Wine.Kind==BaseWine.Strong?"烈酒，本日结束清除":"清酒，下一场结束清除"),45,19);
            foreach(var item in items)
            {
                string stack=item.Id;Line(s.Backpack.Catalog.Single(d=>d.Id==item.ItemId).Name+" ×"+item.Count,50,21);
                if(selected!=""&&item.Count>1){int split=Math.Min(inventoryQuantity,item.Count-1);RowButton("拆分 "+split+"份",0,820,()=>InventoryChange(next=>BasePartyInventory.Move(next,stack,item.Holder,item.Holder==""?-1:BasePartyInventory.FreeCell(next,item.Holder),"",split),"已拆分酒类补给"));row+=58;}
                foreach(var l in BaseAdventure.Members(s))
                {string person=l.Id;int rev=s.Backpack.Revision;bool replace=item.ItemId==BaseWine.Strong&&l.Wine.Kind==BaseWine.Sake;RowButton((replace?"覆盖清酒，给":"使用给")+l.Name,0,820,()=>{if(replace)WineReplace(stack,person,rev);else GearChange(next=>BaseWine.Use(next,stack,person,rev),"已使用小队补给",()=>OpenWine());},BaseWine.CanUse(s,stack,person,replace));row+=58;}
            }
            if(items.Length==0)Line("没有当前可用的酒类补给，可在城镇交易摊位购买。",70,20);
            EndPanel();
        }
        private void WineReplace(string item,string leader,int revision)
        {BeginPanel("确认覆盖清酒");Line("该队长已有清酒效果，饮用烈酒将直接覆盖原效果。确定继续吗？",90);RowButton("确认覆盖并使用烈酒",-210,400,()=>GearChange(next=>BaseWine.Use(next,item,leader,revision,true),"已使用烈酒，清酒效果作废",()=>OpenWine()));RowButton("取消",210,400,()=>OpenWine());row+=60;EndPanel();}
        private void OpenLostItems()
        {
            var s=owner.State;BeginPanel("遗失物与反击追回","lost-items");RowButton("返回菜单",0,820,OpenMenu);row+=60;
            foreach(var loss in s.Aftermath.LostItems.AsEnumerable().Reverse())
            {Line(loss.Title+" · "+(loss.Returned?"已夺回入库":"待反击夺回"),60,21);if(loss.Returned)continue;Line("装备"+loss.Gear.Count+"件／背包堆叠"+loss.Stacks.Count+"项 · 目标："+(loss.Temporary?"世界地图临时敌军残部":BaseThreats.Site(s,loss.SourceSiteId)?.Name),65,19);foreach(var item in loss.Gear)Line(BasePersonalEquipment.Name(item),40,18);foreach(var stack in loss.Stacks)Line(s.Backpack.Catalog.Single(d=>d.Id==stack.ItemId).Name+" ×"+stack.Count,40,18);string id=loss.EventId;RowButton("预览反击夺回",0,820,()=>OpenCampaignPreview(id,"full"),!s.Campaign.HasActiveBattle&&!BaseBattleLoot.Pending(s));row+=60;}
            if(s.Aftermath.LostItems.Count==0)Line("暂无遗失物。胜利阵亡遗物与俘虏缴械直接入库，普通战利品仍需背包取舍。",90);
            EndPanel();
        }
    }
}
