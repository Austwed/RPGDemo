using System;
using System.Linq;
using BaseConquest.Presentation;
using UnityEngine;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private string armyPurpose = "defense", armyPage = "preset";
        private BaseArmyPreset ArmyPreset(BaseState state) => state.Muster.Presets.Single(p => p.Purpose == armyPurpose);
        private void ArmyEdit(Action<BaseState> action)
        { owner.Change(action, "已保存用兵设置，尚未派出人员"); OpenGovernment(); }
        private int counterStep=1;
        private void ArmyCounter(string label, int value, int minimum, int maximum, Action<int> change,bool singleStep=false)
        {
            Label(content, label + "：" + value, new Vector2(-190, -row - 26), new Vector2(480, 48), 20, TextAnchor.MiddleLeft, new Vector2(0.5f, 1));
            if(singleStep||BaseDemo.Active(owner.State))
            {RowButton("−1",190,105,()=>change(value-1),value>minimum);RowButton("+1",330,105,()=>change(value+1),value<maximum);row+=56;return;}
            var step=Box("CounterStep",content,new Vector2(.5f,1),new Vector2(55,-row-26),new Vector2(90,46),buttonColor);
            var number=Label(step,counterStep.ToString(),Vector2.zero,new Vector2(80,40),20,TextAnchor.MiddleCenter);number.raycastTarget=true;
            var input=step.gameObject.GetOrAdd<UnityEngine.UI.InputField>();input.targetGraphic=step.GetComponent<UnityEngine.UI.Image>();input.textComponent=number;input.contentType=UnityEngine.UI.InputField.ContentType.IntegerNumber;input.text=counterStep.ToString();
            input.onEndEdit.AddListener(text=>{counterStep=int.TryParse(text,out int n)?Math.Max(1,Math.Min(100000,n)):1;input.SetTextWithoutNotify(counterStep.ToString());});
            RowButton("−n",190,105,()=>change((int)Math.Max(minimum,(long)value-counterStep)),value>minimum);RowButton("+n",330,105,()=>change((int)Math.Min(maximum,(long)value+counterStep)),value<maximum);row+=56;
        }
        private void Army()
        {
            foreach (string purpose in BaseMuster.Purposes)
            {
                string id = purpose;
                RowButton((armyPurpose == id ? "● " : "") + BaseMuster.PurposeName(id) + "预设", id == "defense" ? -220 : 220, 400, () => { armyPurpose = id; OpenGovernment(); });
            }
            row += 64;
            foreach (string page in new[] { "preset", "protection", "parties" })
            {
                string id = page;
                RowButton((armyPage == id ? "● " : "") + (id == "preset" ? "编制" : id == "protection" ? "留守与生产保留" : "外出队伍"), id == "preset" ? -290 : id == "protection" ? 0 : 290, 265, () => { armyPage = id; OpenGovernment(); });
            }
            row += 64;
            if (armyPage == "protection") { ArmyProtection(); return; }
            if (armyPage == "parties") { OutsideParties(OpenGovernment); return; }
            var s = owner.State; var preset = ArmyPreset(s);
            Line("预设仅保存希望编制；确认抽调前不会占用人物、兵力或产能。", 56, 18);
            Line("队长沿用常驻冒险小队。可让伙伴原地等候或归队，回营后可调整随行人选。",56,18);
            Line("默认带兵组成已移至兵营·队长编组；这里保留军需与留守设置。",58,18);
            foreach(var leader in BaseAdventure.Members(s))Line(BasePersonnel.LeaderName(leader)+" · "+TroopsText(leader.DefaultTroops),48,19);
            Line("当前冒险小队 "+s.Adventure.Members.Count+"/3 · 暂离占位 "+s.Adventure.Pauses.Count,48,20);
            foreach (var leader in BaseAdventure.Members(s))
            {
                Line(BasePersonnel.LeaderName(leader)+" · 生命 "+leader.Health.ToString("0.#")+"/"+leader.MaxHealth.ToString("0.#")+(leader.Away ? " · 外出" : " · 随行"),50,20);
            }
            Line("军需取用优先级（保存规则；不会提前扣出装备）", 56, 18);
            foreach (string product in BaseEconomy.ProductsFor(s))
            {
                string id = product; string priority = id == "sword" ? preset.SwordPriority : id == "bow" ? preset.BowPriority : preset.ArmorPriority;
                Label(content, BaseEconomy.ProductName(id), new Vector2(-285, -row - 26), new Vector2(200, 48), 20, TextAnchor.MiddleLeft, new Vector2(0.5f, 1));
                RowButton(priority == "high" ? "优先高级，低级补足" : "优先低级，节约高级", 110, 540, () => ArmyEdit(next => { var p = ArmyPreset(next); string value = priority == "high" ? "low" : "high"; if (id == "sword") p.SwordPriority = value; else if (id == "bow") p.BowPriority = value; else p.ArmorPriority = value; })); row += 56;
            }
            RowButton("预览本次队伍", 0, 800, () => OpenMusterPreview(false), !s.Muster.Parties.Any(p => p.Purpose == armyPurpose)); row += 65;
            if (s.Muster.Parties.Any(p => p.Purpose == armyPurpose)) Line("该用途已有外出队伍，可在外出队伍页查看或返营。", 56, 18);
        }
        private void ArmyProtection()
        {
            var s = owner.State;
            Line("先取闲置，再低优先设施；同级优先少减产。按兵种保留留守人数，按设施保留生产人数。", 75, 18);
            foreach (BaseTroopKind kind in Enum.GetValues(typeof(BaseTroopKind)))
            { var troop = kind; ArmyCounter("基地最低留守 · " + BaseEconomy.TroopName(troop), s.Muster.Keep(troop), 0, 10000, n => ArmyEdit(next => next.Muster.SetKeep(troop, n))); }
            foreach (var facility in s.Muster.Facilities)
            {
                string id = facility.FacilityId;
                Line(BaseEconomy.FacilityName(id), 48, 22);
                RowButton("经营优先级：" + (facility.Priority == 0 ? "低" : facility.Priority == 1 ? "中" : "高"), 0, 800, () => ArmyEdit(next => { var f = next.Muster.Facilities.Single(x => x.FacilityId == id); f.Priority = (f.Priority + 1) % 3; next.Development.Policy = "manual"; })); row += 58;
                ArmyCounter("至少保留生产人数", facility.MinimumWorkers, 0, facility.Capacity, n => ArmyEdit(next => next.Muster.Facilities.Single(f => f.FacilityId == id).MinimumWorkers = n));
            }
            Line("人手不足时少带兵；需要增兵，可在出发前允许抽调留守和生产人员。", 58, 18);
            Line("生产优先以这里的最低人数保障开工；即使设为0，每座生产设施仍至少需1名有效工人。原料紧张时工坊按最低人数节约生产，返营后恢复常规安排。",98,18);
        }
        private void OpenMusterPreview(bool bypass)
        {
            OpenDeparture(armyPurpose=="defense" ? "perimeter" : "world",null,"full",bypass);
        }

        private void OutsideParties(Action back)
        {
            Line("外出队伍", 50, 22);
            if (owner.State.Muster.Parties.Count == 0) { Line("当前没有外出队伍。", 42, 18); return; }
            foreach (var party in owner.State.Muster.Parties)
            {
                string id = party.Id; int count = owner.State.Workers.Where(w => w.Away && w.PartyId == id).Sum(w => w.Count);
                RowButton(BaseMuster.PurposeName(party.Purpose) + " · " + party.Leaders.Count + "队长／" + count + "兵 · 查看", 0, 800, () => OpenParty(id, back)); row += 60;
            }
        }
        private void OpenParty(string id, Action back)
        {
            var s=owner.State;var party=s.Muster.Parties.FirstOrDefault(p=>p.Id==id);if(party==null){back();return;}
            activeObject="party";BeginPanel(BaseMuster.PurposeName(party.Purpose)+" · 外出队伍","party:"+id);
            var q=s.Supply.Quotas.FirstOrDefault(x=>x.PartyId==id);bool available=!party.InCombat&&!s.Campaign.HasActiveBattle;
            RowButton("前往本队区域",-290,270,()=>owner.Travel(party.Purpose=="defense"?"perimeter":"world"),available);RowButton("军需预览",0,270,()=>OpenSupply(id,"full",()=>OpenParty(id,back)),q==null&&available);RowButton("返回菜单",290,270,OpenMenu);row+=64;
            RowButton("追加基地兵力",-290,270,()=>OpenAppend(id,back,false),available&&party.Purpose=="expedition"&&q==null);
            RowButton("返营",0,270,()=>{if(owner.Change(next=>{if(!BaseMuster.Return(next,id))throw new InvalidOperationException("先结束战斗或取消军需");},"本队返营，今日不生产"))OpenMenu();},available&&q==null);
            RowButton("规则 i",290,270,()=>RuleInfo("队伍规则","确认出征与追加后实际外出，按日仍消耗唯一仓库口粮。准备装备分配需先取消才能返营或追加；真实战损、伤势、来源及岗位意向持续保存。返营者今日不生产，不挤走今日可生产的替补。进入世界必须由主角带队。",()=>OpenParty(id,back)));row+=64;
            if(q!=null){RowButton("取消军需准备",0,800,()=>{owner.Change(next=>{if(!BaseSupply.Cancel(next,q.BattleId))throw new InvalidOperationException("战斗装备分配不能取消");},"已释放准备装备分配");OpenParty(id,back);},q.Phase=="prepared");row+=60;}
            Line("队长："+string.Join("、",party.Leaders.Select(x=>BasePersonnel.LeaderName(s.Leaders.Single(l=>l.Id==x.LeaderId)))),64);
            foreach(BaseTroopKind k in Enum.GetValues(typeof(BaseTroopKind)))Line(BaseEconomy.TroopName(k)+"："+s.Workers.Where(w=>w.Away&&w.PartyId==id&&w.Kind==k).Sum(w=>w.Count),42);
            var groups=BaseMuster.CurrentFormation(s,party);if(groups!=null)foreach(var group in groups)Line(BasePersonnel.LeaderName(s.Leaders.Single(l=>l.Id==group.LeaderId))+" · "+TroopsText(group.Actual),50,18);
            if(!string.IsNullOrEmpty(party.PriorityMode))Line("本次抽调："+BaseMuster.ModeName(party.PriorityMode)+(party.ConserveMaterials ? "；工坊按最低开工人数节约原料，返营解除" : ""),72,18);
            Line(q==null?"尚未准备军需":q.Phase=="prepared"?"军需已准备；追加前需取消":"军需已锁定于本场战斗",60,18);EndPanel();
        }
    }
}
