using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.Battle
{
    public sealed partial class BattleSimulation
    {
        private bool CanReachPatient(GridPosition from,GridPosition patient)=>from==patient||Board.CanStep(from,patient);
        private readonly Dictionary<BattleSide, SortedDictionary<int, Dictionary<GridPosition, List<BattleSoldier>>>> rescueIndex = new Dictionary<BattleSide, SortedDictionary<int, Dictionary<GridPosition, List<BattleSoldier>>>>();
        public int RescueCandidatesExamined { get; private set; }
        public int RescueDistanceFieldsBuilt { get; private set; }
        private void BuildRescueIndex()
        {
            rescueIndex.Clear();
            foreach (var s in Soldiers)
            {
                if (!s.IsActive || s.CarryingId >= 0) continue;
                if (!rescueIndex.TryGetValue(s.Side, out var speeds)) rescueIndex[s.Side] = speeds = new SortedDictionary<int, Dictionary<GridPosition, List<BattleSoldier>>>();
                int speed = Math.Max(1, MovementSpeed(s));
                if (!speeds.TryGetValue(speed, out var cells)) speeds[speed] = cells = new Dictionary<GridPosition, List<BattleSoldier>>();
                if (!cells.TryGetValue(s.Cell, out var list)) cells[s.Cell] = list = new List<BattleSoldier>();
                list.Add(s);
            }
        }
        private Dictionary<GridPosition, int> RescueDistances(BattleSide side, IEnumerable<GridPosition> seeds)
        {
            RescueDistanceFieldsBuilt++; var distances = new Dictionary<GridPosition, int>(); var queue = new Queue<GridPosition>();
            // 只阻挡该阵营已知敌军；不能用透雾测试显示给单位传递隐藏敌军坐标。
            var blocked = new HashSet<GridPosition>(Soldiers.Where(s => s.IsActive && s.Side != side && Vision.CanSee(side, s)).Select(s => s.Cell));
            foreach (var p in seeds) if (Board.Walkable(p) && !blocked.Contains(p) && !distances.ContainsKey(p)) { distances[p] = 0; queue.Enqueue(p); }
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                foreach (var d in Directions)
                {
                    var next = p + d;
                    if (!Board.CanStep(p,next) || blocked.Contains(next) || distances.ContainsKey(next)) continue;
                    distances[next] = distances[p] + 1; queue.Enqueue(next);
                }
            }
            return distances;
        }
        private BattleSoldier FastestRescuer(BattleSoldier patient, bool allUnits)
        {
            if (!patient.IsDowned || patient.CarriedById >= 0 || !rescueIndex.TryGetValue(patient.Side, out var speeds)) return null;
            var adjacent = Directions.Select(d => patient.Cell + d).Concat(new[] { patient.Cell }).Where(p=>CanReachPatient(p,patient.Cell));
            var approach = RescueDistances(patient.Side, adjacent);
            var exit = RescueDistances(patient.Side, Board.Positions().Where(p => IsExit(patient.Side, p)));
            int carry = adjacent.Where(exit.ContainsKey).Select(p => exit[p]).DefaultIfEmpty(int.MaxValue).Min();
            if (carry == int.MaxValue) return null;
            var ordered = approach.OrderBy(p => p.Value).ThenBy(p => p.Key.Z * Board.Width + p.Key.X).ToList();
            BattleSoldier best = null; int bestEta = int.MaxValue;
            foreach (var bucket in speeds.Reverse())
            {
                int speed = bucket.Key;
                if (2 + (int)Math.Ceiling(carry / (double)speed) >= bestEta) continue;
                foreach (var pair in ordered)
                {
                    int eta = 2 + (int)Math.Ceiling(pair.Value / (double)speed) + (int)Math.Ceiling(carry / (double)speed);
                    if (eta >= bestEta) break;
                    if (!bucket.Value.TryGetValue(pair.Key, out var candidates)) continue;
                    var candidate = candidates.OrderBy(s => s.Id).FirstOrDefault(s => s.RescueTargetId < 0 && (allUnits || s.Command == SquadCommand.Retreat));
                    if (candidate == null) continue;
                    RescueCandidatesExamined++; best = candidate; bestEta = eta; break;
                }
            }
            return best;
        }
        public bool RequestRescue(BattleSide side, int patientId)
        {
            var patient = Soldier(patientId);
            if (!ModernRules || Phase != BattlePhase.Fighting || patient == null || patient.Side != side || !patient.IsDowned || patient.CarriedById >= 0) return false;
            patient.RescueRequested = patient.RescueAnyUnit = true; patient.ManualRescuerId = -1;
            var previous = Soldiers.FirstOrDefault(s => s.RescueTargetId == patientId);
            if (previous != null) previous.RescueTargetId = -1;
            BuildRescueIndex(); var rescuer = FastestRescuer(patient, true);
            if (rescuer == null) { Notice(UnitName(patient) + "暂时没有可达救援者，后续回合重试", side, patientId); return false; }
            rescuer.RescueTargetId = patientId;
            Notice(UnitName(rescuer) + "接到紧急救援：" + UnitName(patient), side, patientId, rescuer.Id); return true;
        }
        private void PrepareRescues()
        {
            if (!ModernRules) return;
            foreach (var s in Soldiers)
            {
                if (s.RescueTargetId >= 0 && (!s.IsActive || Soldier(s.RescueTargetId)?.IsDowned != true || Soldier(s.RescueTargetId).CarriedById >= 0)) s.RescueTargetId = -1;
                if (s.CarryingId >= 0 && !s.IsActive)
                {
                    var patient = Soldier(s.CarryingId);
                    if (patient != null) { patient.Cell = s.Cell; patient.CarriedById = -1; patient.RescueRequested = true; Notice(UnitName(s) + "失去行动能力，重新寻找救援者", s.Side, patient.Id, s.Id); }
                    s.CarryingId = -1;
                }
            }
            BuildRescueIndex();
            foreach (var patient in Soldiers.Where(s => s.IsDowned && s.CarriedById < 0).OrderBy(s => s.OriginCommanderSlot))
            {
                if (Soldiers.Any(s => s.IsActive && s.RescueTargetId == patient.Id)) continue;
                if (patient.ManualRescuerId >= 0)
                {
                    var chosen = Soldier(patient.ManualRescuerId);
                    if (chosen?.IsActive == true && chosen.CarryingId < 0 && chosen.RescueTargetId < 0) { chosen.RescueTargetId = patient.Id; continue; }
                    if (chosen?.IsActive == true) continue;
                    patient.ManualRescuerId = -1; // 指定者失能后恢复自动尝试。
                }
                if (!patient.RescueRequested && !rescueIndex.ContainsKey(patient.Side)) continue;
                var rescuer = FastestRescuer(patient, patient.RescueAnyUnit);
                if (rescuer == null) continue;
                patient.RescueRequested = true; rescuer.RescueTargetId = patient.Id;
                Notice(UnitName(rescuer) + "前往救援" + UnitName(patient), patient.Side, patient.Id, rescuer.Id);
            }
        }
        private Intent PlanRescue(BattleSoldier s, Dictionary<GridPosition, List<BattleSoldier>> occupied)
        {
            if (!ModernRules) return null;
            var intent = new Intent { Actor = s, Action = RoundAction.Wait, Destination = s.Cell };
            if (s.CarryingId >= 0)
            {
                if (IsExit(s.Side, s.Cell)) intent.Action = RoundAction.Escape;
                else SetMove(intent, FindPath(s, p => IsExit(s.Side, p), occupied));
                return intent;
            }
            var patient = Soldier(s.RescueTargetId); if (patient?.IsDowned != true || patient.CarriedById >= 0) return null;
            if (CanReachPatient(s.Cell,patient.Cell)) { intent.Action = RoundAction.Rescue; intent.Target = patient; }
            else SetMove(intent, FindPath(s, p => CanReachPatient(p,patient.Cell), occupied));
            return intent;
        }
        private bool ResolveRescueActions(List<Intent> intents)
        {
            if (!ModernRules) return false;
            bool changed = false;
            foreach (var intent in intents)
            {
                var s = intent.Actor;
                if (s.CarryingId >= 0 && s.IsActive) Soldier(s.CarryingId).Cell = s.Cell;
                if (intent.Action == RoundAction.Rescue && s.IsActive && intent.Target.IsDowned && intent.Target.CarriedById < 0 && CanReachPatient(s.Cell,intent.Target.Cell))
                {
                    s.CarryingId = intent.Target.Id; s.RescueTargetId = -1; s.LastAction = RoundAction.Rescue;
                    intent.Target.CarriedById = s.Id; intent.Target.Cell = s.Cell; changed = true;
                    Notice(UnitName(s) + "带起" + UnitName(intent.Target) + "，前往撤离入口", s.Side, intent.Target.Id, s.Id);
                }
                if (s.Escaped && s.CarryingId >= 0)
                {
                    var patient = Soldier(s.CarryingId); patient.Rescued = patient.Escaped = true; patient.EscapeCarrierId = s.Id; patient.CarriedById = -1; s.CarryingId = -1; changed = true;
                    Notice(UnitName(patient) + "已被救出战场", s.Side, patient.Id);
                }
            }
            PrepareRescues(); return changed;
        }
        public bool AssignRescuer(BattleSide side, int patientId, int rescuerId)
        {
            var patient = Soldier(patientId); var rescuer = Soldier(rescuerId);
            if (!ModernRules || Phase != BattlePhase.Fighting || patient?.IsDowned != true || patient.Side != side || rescuer?.IsActive != true || rescuer.Side != side || rescuer.CarryingId >= 0 || patient.CarriedById >= 0) return false;
            var occupied = KnownOccupancy(side, Occupancy());
            if (FindPath(rescuer, p => CanReachPatient(p,patient.Cell), occupied).Count == 0 || FindPath(rescuer, p => IsExit(side, p), occupied).Count == 0) return false;
            foreach (var s in Soldiers.Where(s => s.RescueTargetId == patientId)) s.RescueTargetId = -1;
            if (rescuer.RescueTargetId >= 0 && Soldier(rescuer.RescueTargetId) is BattleSoldier previous) previous.ManualRescuerId = -1;
            patient.RescueRequested = patient.RescueAnyUnit = true; patient.ManualRescuerId = rescuerId; rescuer.RescueTargetId = patientId;
            Notice(UnitName(rescuer) + "被指定救援" + UnitName(patient), side, patientId, rescuer.Id); return true;
        }
    }
}
