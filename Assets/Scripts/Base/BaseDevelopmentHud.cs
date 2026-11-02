using System;
using System.Linq;
using UnityEngine;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private void LeaderLock(string id, Action refresh)
        {
            var leader = owner.State.Leaders.Single(l => l.Id == id);
            RowButton(leader.PostLocked ? "已锁定岗位意向 · 点击解锁" : "自动岗位 · 点击锁定当前安排", 0, 800, () =>
            {
                owner.Change(next => { var l = next.Leaders.Single(x => x.Id == id); l.PostLocked = !l.PostLocked; if (l.PostLocked) l.DesiredFacility = l.FacilityId; }, "已更新管理员岗位锁定"); refresh();
            },!BaseAdventure.Contains(owner.State,id)); row += 56;
        }
        private void Development()
        {
            var s = owner.State;
            Line("当前策略：" + BaseDevelopment.PolicyName(s.Development.Policy), 48, 23);
            RowButton("自动安排：" + (s.Development.Automatic ? "开" : "关"), 0, 800, () => { owner.Change(next => BaseDevelopment.SetAutomatic(next, !next.Development.Automatic), "已更新自动安排"); OpenGovernment(); }); row += 58;
            foreach (string policy in BaseDevelopment.Policies.Where(p => p != "custom"))
            {
                string id = policy;
                var option=RowButton((s.Development.Policy==id ? "● " : "　 ")+BaseDevelopment.PolicyName(id), 0, 800, () => { owner.Change(next => BaseDevelopment.SetPolicy(next, id), "已按发展策略安排有效人员"); OpenGovernment(); });option.GetComponentInChildren<UnityEngine.UI.Text>().color=Color.white;row += 56;
            }
            if(s.Development.Policy=="custom")Line("● 自定优先（当前）",48,20);
            Line("生产与抽调共用下列优先级。先保留今日可生产的锁定岗位，再优先满足高位设施；同等效率保持原安排。", 76, 18);
            foreach (var facility in s.Muster.Facilities)
            {
                string id = facility.FacilityId;
                RowButton(BaseEconomy.FacilityName(id) + "生产优先：" + (facility.Priority == 2 ? "高" : facility.Priority == 1 ? "中" : "低"), 0, 800, () =>
                { owner.Change(next => { var f = next.Muster.Facilities.Single(x => x.FacilityId == id); f.Priority = (f.Priority + 1) % 3; next.Development.Policy = "custom"; }, "已更新统一优先级"); OpenGovernment(); }); row += 58;
                Line("等级 " + facility.Level + "；今日生产人数 " + BaseEconomy.EligibleWorkers(s, id) + "/" + facility.Capacity + "；管理员 " + (BaseEconomy.Manager(s,id) is BaseLeader manager ? BasePersonnel.LeaderName(manager) : "空位"), 48);
            }
            Line("随行队长不管理设施；队长按日自然回血，不影响任职资格。出战、出征、返营或选择休养的士兵当天不工作；有效替补日末前补位仍正常生产。", 98, 18);
            Line("管理员当前每设施一席，工人位有限；以后升级可扩容和解锁配方。手动调配会锁定意向，解锁后恢复自动安排。", 85, 18);
        }
        private string PersonState(BaseLeader l) => BasePersonnel.State(owner.State,l);
        private void OpenBarracks()
        {
            if (activeObject != "barracks" || owner.State == null) return;
            BeginPanel("兵营 · 军队与休养", "barracks:" + activePage); ManagementTabs(OpenBarracks);
            RowButton("规则 i",0,800,()=>FacilityRules(OpenBarracks));row+=60;
            BaseConquest.Presentation.DemoUiIcons.Attach(RowButton("角色等级训练",0,820,OpenTraining),"training");row+=60;
            var s = owner.State;
            Line("全军 " + s.Workers.Sum(w => w.Count) + "兵／" + s.Leaders.Count + "队长。基地与外出共用实际人数和粮草。", 58, 22);
            if(activePage=="composition")CommanderPresets();else if(activePage=="roster")PersonnelRoster();else if (activePage == "recovery") Recovery();
            else
            {
                foreach (BaseTroopKind kind in Enum.GetValues(typeof(BaseTroopKind)))
                {
                    var units = s.Workers.Where(w => w.Kind == kind).ToArray();
                    int working = units.Where(w => BaseDevelopment.Effective(s, w) && w.FacilityId != "").Sum(w => w.Count);
                    int idle = units.Where(w => BaseDevelopment.Effective(s, w) && w.FacilityId == "").Sum(w => w.Count);
                    int invalid = units.Where(w => !w.Away && !w.Resting && !BaseDevelopment.Effective(s, w)).Sum(w => w.Count);
                    Line(BaseEconomy.TroopName(kind) + "：在岗" + working + "／闲置 · 今日可生产" + idle + "／闲置 · 今日不生产" + invalid + "／休养" + units.Where(w => w.Resting).Sum(w => w.Count) + "／外出" + units.Where(w => w.Away).Sum(w => w.Count), 65, 19);
                }

                foreach (var l in s.Leaders) Line(BasePersonnel.LeaderName(l) + " · 生命 " + l.Health.ToString("0.#") + "/" + l.MaxHealth.ToString("0.#") + " · " + PersonState(l) + "\n粮田增益 " + (BaseDevelopment.Bonus(l, "farm") * 100).ToString("0.#") + "%／工坊 " + (BaseDevelopment.Bonus(l, "workshop") * 100).ToString("0.#") + "%", 70, 19);
                OutsideParties(OpenBarracks);
            }
            EndPanel();
        }
        private void Recovery()
        {
            var s = owner.State;
            Line("队长每天自然恢复 " + s.Development.RecoveryPerDay.ToString("0.#") + "生命，可任职或随行；士兵需选择休养。满血停止，口粮照常。", 85, 18);
            foreach (var person in s.Leaders)
            {
                string id = person.Id;
                Line(BasePersonnel.LeaderName(person) + " · " + person.Health.ToString("0.#") + "/" + person.MaxHealth.ToString("0.#") + " · " + PersonState(person), 44);
                Line(BaseAdventure.Alive(person)&&person.Health>0 ? "自然恢复，无需兵营操作" : "阵亡／失联者不自然恢复",44,18);
            }
            for (int i = 0; i < s.Workers.Count; i++)
            {
                int index = i; var worker = s.Workers[i]; if (worker.Away) continue;
                Line(BaseEconomy.TroopName(worker.Kind) + " " + worker.Count + "人 · " + worker.Health.ToString("0.#") + "/" + worker.MaxHealth.ToString("0.#") + "生命 · " + (worker.Resting ? "休养" : BaseDevelopment.Effective(s, worker) ? "今日可生产" : "今日不生产"), 52);
                string stamp = BaseMuster.Snapshot(s);
                RowButton(worker.Resting ? "本批停止休养" : "本批选择休养", -220, 400, () => { owner.Change(next => { if (BaseMuster.Snapshot(next) != stamp || !BaseDevelopment.RestWorkers(next, index, worker.Count, !worker.Resting)) throw new InvalidOperationException("人员状态变化，请重新打开兵营"); }, "已更新本批休养"); OpenBarracks(); }, worker.Health > 0 && (worker.Resting || worker.Health < worker.MaxHealth));
                RowButton("解锁岗位意向", 220, 400, () => { owner.Change(next => { if (BaseMuster.Snapshot(next) != stamp) throw new InvalidOperationException("人员状态变化"); next.Workers[index].PostLocked = false; }, "已交由策略安排"); OpenBarracks(); }); row += 62;
            }
        }
    }
}
