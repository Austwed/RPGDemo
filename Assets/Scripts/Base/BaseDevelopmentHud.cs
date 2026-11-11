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
            bool office = BaseFacilityLayout.OfficeBuilt(s);
            Line("当前策略：" + BaseDevelopment.PolicyName(s.Development.Policy) + (office ? "" : "（办公设施未建成，暂按手动）"), 48, 23);
            foreach (string policy in BaseDevelopment.Policies)
            {
                string id = policy;
                bool allowed = id == "manual" || office;
                var option=RowButton((s.Development.Policy==id ? "● " : "　 ")+BaseDevelopment.PolicyName(id), 0, 800, () => { owner.Change(next => BaseDevelopment.SetPolicy(next, id), "已按发展策略安排有效人员"); OpenGovernment(); }, allowed);
                option.GetComponentInChildren<UnityEngine.UI.Text>().color=Color.white;row += 56;
            }
            Line("办公设施建成前只能逐人手动安排；建成后才会自动补位。切换策略立即重排，手动锁定的人不受影响。" + (string.IsNullOrEmpty(s.Layout?.PausedPolicy) ? "" : "上次策略：" + BaseDevelopment.PolicyName(s.Layout.PausedPolicy) + "（办公设施收存后暂停，重新放置后请再确认）"), 76, 18);
            foreach (var facility in s.Muster.Facilities)
            {
                string id = facility.FacilityId;
                RowButton(BaseEconomy.FacilityName(id) + "生产优先：" + (facility.Priority == 2 ? "高" : facility.Priority == 1 ? "中" : "低"), 0, 800, () =>
                { owner.Change(next => { var f = next.Muster.Facilities.Single(x => x.FacilityId == id); f.Priority = (f.Priority + 1) % 3; next.Development.Policy = "manual"; }, "已改为手动并更新优先级"); OpenGovernment(); }); row += 58;
                Line("等级 " + facility.Level + "；今日生产人数 " + BaseEconomy.EligibleWorkers(s, id) + "/" + facility.Capacity + "；管理员 " + (BaseEconomy.Manager(s,id) is BaseLeader manager ? BasePersonnel.LeaderName(manager) : "空位"), 48);
            }
            // 基地建设（P40 临时入口）：正式拖放界面等原型定稿；这里只做放置／收存与费用结算，点一下自动找空地。
            Line("基地建设：点击放置会自动选第一处合法空地。", 76, 18);
            foreach (var def in BaseFacilityLayout.Catalog)
            {
                bool built = BaseFacilityLayout.Built(s, def.Id), stored = BaseFacilityLayout.Stored(s, def.Id);
                bool licensed = s.Layout?.OfficeGift == true || built || stored;
                string state = built ? "已放置" : stored ? "已收存" : def.Id == "office" || def.Id == "caravan" ? (licensed ? "待放置" : "需建设许可") : "未拥有";
                var definition = def;
                if (built && !def.Required)
                    RowButton(def.Name + " · " + state + " · 收存", 0, 800, () => { owner.Change(next => { if (!BaseFacilityLayout.Store(next, definition.Id, out string problem)) throw new InvalidOperationException(problem); }, def.Name + "已收存"); OpenGovernment(); });
                else if (!built && !def.Required)
                    RowButton(def.Name + " · " + state + (def.Materials > 0 ? " · 原料" + def.Materials : " · 免费") + " · 放置", 0, 800, () => { owner.Change(next => { if (!BaseFacilityLayout.AutoPlace(next, definition.Id, out string problem, out _, out _)) throw new InvalidOperationException(problem); }, def.Name + "已放置"); OpenGovernment(); }, licensed);
                else
                    Label(content, def.Name + " · " + state, new UnityEngine.Vector2(0, -row - 26), new UnityEngine.Vector2(800, 48), 18, UnityEngine.TextAnchor.MiddleCenter, new UnityEngine.Vector2(.5f, 1));
                row += 58;
            }
            Line("商队：" + BaseCaravan.Status(s), 60, 19);
            Line("随行队长不管理设施；队长按日自然回血，不影响任职资格，也不受兵力阈值限制。士兵生命低于 30% 既不能工作也不能主动出战；遇袭守家例外，还能行动就上。全员每天自动恢复，满血停止。", 98, 18);
            Line("管理员当前每设施一席，工人位有限；以后升级可扩容和解锁配方。手动调配会锁定意向，解锁后恢复自动安排。", 85, 18);
        }
        private void OpenCaravan()
        {
            var s = owner.State;
            activeObject = "caravan"; interactionObject = "caravan";
            BeginPanel("商队", "caravan");
            Line("人员 " + BaseCaravan.Crew(s) + "人 · 运力 " + BaseCaravan.Capacity(s) + " · 每日口粮 " + BaseCaravan.DailyFood(s) + "（双倍）", 56, 21);
            Line(BaseCaravan.Status(s), 52, 21);
            RowButton("领地内跑商 · 每日金币+" + BaseCaravan.Crew(s) * BaseCaravan.LocalCoinsPerCrew, -210, 400,
                () => owner.Change(next => { if (!BaseCaravan.Start(next, "local", out string problem)) throw new InvalidOperationException(problem); }, "商队已开始跑商"), BaseCaravan.Problem(s, "local") == "");
            RowButton("黑鸦寨运输 · 三天往返", 210, 400,
                () => owner.Change(next => { if (!BaseCaravan.Start(next, "crow", out string problem)) throw new InvalidOperationException(problem); }, "商队已出发前往黑鸦寨"), BaseCaravan.Problem(s, "crow") == "");
            row += 60;
            RowButton("召回／解散商队", 0, 800, () => { owner.Change(next => BaseCaravan.Stop(next, "玩家召回商队，人员回到基地空闲"), "商队已召回"); OpenCaravan(); }, s.Caravan.Route != "" || s.Caravan.Phase != ""); row += 60;
            RowButton("人员调配", 0, 800, OpenCaravanWorkers); row += 60;
            Line("满载以黑鸦寨当前库存为准。", 56, 18);
            foreach (var entry in s.Caravan.Log.Skip(Math.Max(0, s.Caravan.Log.Count - 5))) Line(entry, 44, 17);
            EndPanel();
        }
        private void OpenCaravanWorkers()
        {
            BeginPanel("商队 · 人员调配", "caravan-workers");
            RowButton("返回商队", 0, 800, () => { workerDraft = null; workerMoves.Clear(); OpenCaravan(); }); row += 60;
            Workers(new[] { "caravan" }, OpenCaravanWorkers);
            EndPanel();
        }
        private string PersonState(BaseLeader l) => BasePersonnel.State(owner.State,l);
        private void OpenBarracks()
        {
            if (activeObject != "barracks" || owner.State == null) return;
            if (UseFacilityPanel) { OpenBarracksFacility(); return; }
            BeginPanel("兵营 · 军队与伤情", "barracks:" + activePage); ManagementTabs(OpenBarracks);
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
                    var tally = BaseDevelopment.Tally(s, kind);
                    Line(BaseEconomy.TroopName(kind) + "：总数" + tally[0] + "／在岗" + working + " · 闲置可生产" + idle + "／可工作" + tally[1] + "／可主动出战" + tally[2] + "／重伤" + tally[3] + "／外出" + units.Where(w => w.Away).Sum(w => w.Count), 62, 19);
                }

                foreach (var l in s.Leaders) Line(BasePersonnel.LeaderName(l) + " · 生命 " + l.Health.ToString("0.#") + "/" + l.MaxHealth.ToString("0.#") + " · " + PersonState(l) + "\n粮田增益 " + (BaseDevelopment.Bonus(l, "farm") * 100).ToString("0.#") + "%／工坊 " + (BaseDevelopment.Bonus(l, "workshop") * 100).ToString("0.#") + "%", 70, 19);
                OutsideParties(OpenBarracks);
            }
            EndPanel();
        }
        private void Recovery()
        {
            var s = owner.State;
            Line("全员每天自然恢复最大生命的 " + (BaseDevelopment.RecoveryRatio(s) * 100).ToString("0") + "%（营帐 " + (s.Demo?.Rpg?.CampLevel ?? 1) + " 级），满血停止，口粮照常。队长不受血量阈值限制；士兵生命低于 30% 不能工作也不能主动出战，遇袭守家仍可迎战。", 98, 18);
            foreach (var person in s.Leaders)
            {
                string id = person.Id;
                Line(BasePersonnel.LeaderName(person) + " · " + person.Health.ToString("0.#") + "/" + person.MaxHealth.ToString("0.#") + " · " + PersonState(person), 44);
                Line(BaseAdventure.Alive(person)&&person.Health>0 ? "自然恢复，无需兵营操作" : "阵亡／失联者不自然恢复",44,18);
            }
            for (int i = 0; i < s.Workers.Count; i++)
            {
                int index = i; var worker = s.Workers[i]; if (worker.Away) continue;
                Line(BaseEconomy.TroopName(worker.Kind) + " " + worker.Count + "人 · " + worker.Health.ToString("0.#") + "/" + worker.MaxHealth.ToString("0.#") + "生命 · " + (BaseDevelopment.Effective(s, worker) ? "今日可生产" : "重伤强制空闲"), 52);
                string stamp = BaseMuster.Snapshot(s);
                RowButton("解锁岗位意向", 220, 400, () => { owner.Change(next => { if (BaseMuster.Snapshot(next) != stamp) throw new InvalidOperationException("人员状态变化"); next.Workers[index].PostLocked = false; }, "已交由策略安排"); OpenBarracks(); }); row += 62;
            }
        }
    }
}
