using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.Battle
{
    // 点选、框选、兵种按钮都修改同一份单位 ID 列表；选中不等于覆盖命令。
    public sealed class BattleSelection
    {
        private readonly List<int> ids = new List<int>();
        public IReadOnlyList<int> Ids => ids;
        public int Primary => ids.Count > 0 ? ids[0] : -1;
        public bool Contains(int id) => ids.Contains(id);
        public void Clear() => ids.Clear();
        public void Set(IEnumerable<int> members) { ids.Clear(); ids.AddRange(members.Distinct()); }
        public void Prune(BattleSimulation battle, BattleSide side) => ids.RemoveAll(id => !(battle.Soldier(id)?.IsActive ?? false) || battle.Soldier(id).Side != side || !battle.SelectableCommander(battle.Soldier(id)));
        public void ToggleUnit(int id, bool additive = false)
        {
            if (additive) { if (!ids.Remove(id)) ids.Add(id); }
            else { bool sole = ids.Count == 1 && ids[0] == id; ids.Clear(); if (!sole) ids.Add(id); }
        }
        public void ToggleCell(BattleSimulation battle, GridPosition cell, BattleSide side, bool additive = false)
        {
            var members = battle.UnitsAt(cell).Where(s => s.Side == side).Take(2).ToList();
            if (battle.Scenario.CommanderControl)
            {
                var leader = members.Select(battle.CommanderFor).FirstOrDefault(battle.SelectableCommander);
                if (leader != null) ToggleUnit(leader.Id, additive);
                return;
            }
            if (members.Count == 0) return;
            int first = members[0].Id; bool a = Contains(first);
            if (members.Count == 1) { ToggleUnit(first, additive); return; }
            int second = members[1].Id; bool b = Contains(second);
            if (!additive) ids.Clear();
            ids.Remove(first); ids.Remove(second);
            if (!a && !b) ids.Add(first);
            else if (a && !b) ids.Add(second);
            else if (!a && b) { ids.Add(first); ids.Add(second); }
        }
    }
}
