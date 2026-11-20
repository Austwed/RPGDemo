using System;
using System.Linq;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private string appendPartyId;
        private int appendMelee, appendRanged, appendCavalry, appendMage;
        private bool appendDetails;
        private void OpenAppend(string id, Action back, bool bypass)
        {
            var party = owner.State.Muster.Parties.FirstOrDefault(p => p.Id == id);
            if (party == null) { back(); return; }
            if (appendPartyId != id) { appendPartyId = id; appendMelee = appendRanged = appendCavalry = appendMage = 0; }
            BaseMusterPlan plan;
            try { plan = BaseMuster.PreviewAppend(owner.State, id, appendMelee, appendRanged, appendCavalry, bypass, appendMage); }
            catch (Exception e)
            {
                BeginPanel("追加不可用"); Line(e.Message, 90); RowButton("返回本队", 0, 800, () => OpenParty(id, back)); row += 60; EndPanel(); return;
            }
            BeginPanel("出征队伍 · 追加预览", "append:" + id);
            RowButton("按实际可用确认追加", -220, 400, () =>
            {
                if (owner.Change(next => { if (!BaseMuster.ConfirmAppend(next, plan)) throw new InvalidOperationException("人员或设置变化，请重新预览"); }, "已追加真实基地兵力；重新准备本队军需"))
                { appendMelee = appendRanged = appendCavalry = appendMage = 0; OpenParty(id, back); }
                else OpenAppend(id, back, bypass);
            }, plan.Total > 0);
            RowButton("取消，不追加", 220, 400, () => OpenParty(id, back)); row += 65;
            RowButton(appendDetails ? "收起来源明细" : "查看来源明细",-210,400,()=>{appendDetails=!appendDetails;OpenAppend(id,back,bypass);}); RowButton("规则 i",210,400,()=>RuleInfo("追加规则","追加真实基地兵，保留伤势、岗位意向与锁定。每兵种本队合计最多30人；已有军需先取消后重算，战中禁止。保留人员不足时少带兵，允许抽调后才会使用这部分人员；重伤者保持强制空闲。",()=>OpenAppend(id,back,bypass)));row+=60;
            Line(bypass ? "允许抽调留守和生产人员，可能减产" : "保留留守和生产人员", 54, 22);
            Line("追加实际基地兵，队长与原编制保留；每兵种本队合计最多30人。", 65, 18);
            foreach (BaseTroopKind kind in BaseTown.TroopKinds(owner.State))
            {
                var troop = kind;
                int existing = owner.State.Workers.Where(w => w.Away && w.PartyId == id && w.Kind == kind).Sum(w => w.Count);
                int count = kind == BaseTroopKind.Melee ? appendMelee : kind == BaseTroopKind.Ranged ? appendRanged : kind == BaseTroopKind.Mage ? appendMage : appendCavalry;
                ArmyCounter(BaseEconomy.TroopName(kind) + "（本队已有" + existing + "）追加", count, 0, Math.Min(Math.Max(0,30-existing),owner.State.Workers.Where(w=>w.Kind==kind&&BaseSettlements.Local(owner.State,w)&&BaseDevelopment.CanFight(w)).Sum(w=>w.Count)), n =>
                {
                    if (troop == BaseTroopKind.Melee) appendMelee = n; else if (troop == BaseTroopKind.Ranged) appendRanged = n; else if (troop == BaseTroopKind.Mage) appendMage = n; else appendCavalry = n;
                    OpenAppend(id, back, bypass);
                });
            }
            if(appendDetails) foreach (var take in plan.Takes)
            {
                var w = owner.State.Workers[take.BucketIndex];
                Line(BaseEconomy.FacilityName(w.FacilityId) + " → " + BaseEconomy.TroopName(w.Kind) + " " + take.Count + "人 · 每人生命 " + w.Health.ToString("0") + "/" + w.MaxHealth.ToString("0"), 52, 18);
            }
            Line("实际追加 " + plan.Total + "兵；缺近战 " + plan.MissingMelee + "／弓兵 " + plan.MissingRanged + "／骑兵 " + plan.MissingCavalry + "／法师 " + plan.MissingMage, 58);
            Line("基地余兵 " + plan.RemainingBase + "；补位后产出变化：粮 " + (-plan.FoodLoss).ToString("+0;-0;0") + "／产品 " + (-plan.ProductLoss).ToString("+0;-0;0"), 58);

            RowButton(bypass ? "保留留守和生产人员" : "允许抽调留守和生产人员", 0, 800, () => OpenAppend(id, back, !bypass)); row += 60;
            EndPanel();
        }
    }
}
