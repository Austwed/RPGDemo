using System;
using System.Linq;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private void SupplyLines(BaseEquipmentQuota quota)
        {
            foreach (string product in BaseEconomy.ProductsFor(owner.State))
            {
                int low = quota.Equipment.Single(e => e.Product == product && e.Level == 1).Count, high = quota.Equipment.Single(e => e.Product == product && e.Level == 2).Count;
                double bonus = product == "supply"?quota.SwordBonus:product == "sword" ? quota.SwordBonus : product == "bow" ? quota.BowBonus : quota.ArmorBonus;
                Line(BaseEconomy.ProductName(product) + "：需求 " + quota.Demand(product) + "；一级 " + low + "／二级 " + high + "；缺 " + (quota.Demand(product) - low - high) + "\n对应普通兵均摊" + (product == "armor" ? "防御" : "攻击") + " +" + bonus.ToString("0")+(product=="supply"?"；防御 +"+quota.ArmorBonus.ToString("0"):""), 76, 20);
            }
            Line("口粮攻击系数 " + (quota.AttackFactor * 100).ToString("0") + "%", 42, 18);
        }
        private void PartySupply(string id, Action back)
        {
            var q = owner.State.Supply.Quotas.FirstOrDefault(x => x.PartyId == id);
            if (q == null)
            { RowButton("预览本队军需", 0, 800, () => OpenSupply(id, "full", back)); row += 60; return; }
            Line(q.Phase == "prepared" ? "军需已准备，装备分配已锁定" : "本场供给比例已锁定", 48, 22); SupplyLines(q);
            RowButton("取消军需准备并释放装备分配", 0, 800, () => { owner.Change(next => { if (!BaseSupply.Cancel(next, q.BattleId)) throw new InvalidOperationException("战斗已开始，不能取消装备分配"); }, "已释放军需装备分配，总库存未重复增加"); back(); }, q.Phase == "prepared"); row += 60;
        }
        private void OpenSupply(string id, string mode, Action back)
        {
            var party = owner.State.Muster.Parties.FirstOrDefault(p => p.Id == id); if (party == null) { back(); return; }
            BaseSupplyPlan plan;
            try { plan = BaseSupply.Preview(owner.State, id, mode); }
            catch (Exception e) { BeginPanel("军需准备不可用"); Line(e.Message, 100); EndPanel(); return; }
            BeginPanel("外出队伍 · 军需准备", "supply:" + id);
            RowButton("确认军需准备", -220, 400, () => { if (owner.Change(next => { if (!BaseSupply.Reserve(next, plan)) throw new InvalidOperationException("库存或编制变化，请重新预览"); }, "军需已准备并保存装备分配")) back(); });
            RowButton("取消预览", 220, 400, back); row += 64;
            foreach (string choice in BaseSupply.Modes)
            {
                string option = choice;
                RowButton((mode == option ? "● " : "") + BaseSupply.ModeName(option), option == "full" ? -290 : option == "half" ? 0 : 290, 265, () => OpenSupply(id, option, back));
            }
            row += 64;
            foreach (string product in BaseEconomy.ProductsFor(owner.State))
            {
                string key = product;
                string priority = key == "sword" ? party.Preset.SwordPriority : key == "bow" ? party.Preset.BowPriority : party.Preset.ArmorPriority;
                RowButton(BaseEconomy.ProductName(key) + " · " + (priority == "high" ? "优先高级" : "优先低级") + "（点击切换）", 0, 800, () =>
                {
                    owner.Change(next => { var p = next.Muster.Parties.Single(x => x.Id == id).Preset; string value = priority == "high" ? "low" : "high"; if (key == "sword") p.SwordPriority = value; else if (key == "bow") p.BowPriority = value; else p.ArmorPriority = value; }, "已更新本队军需优先级"); OpenSupply(id, mode, back);
                }); row += 56;
            }
            SupplyLines(plan.Quota);
            EndPanel();
        }
    }
}
