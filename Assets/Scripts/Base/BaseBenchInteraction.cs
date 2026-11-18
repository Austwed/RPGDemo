using System.Collections.Generic;
using BaseConquest.Battle;
using UnityEngine;

namespace BaseConquest.Base
{
    public sealed partial class BaseSceneController
    {
        internal const string BenchSeatKind = "bench-seat";
        private static readonly BaseMapObject StandBenchTarget =
            new BaseMapObject("stand-from-bench", "起身", 0, 0, .1, .1, false, kind: BenchSeatKind);

        private BaseMapObject benchSeat;
        private readonly List<(BaseMapObject Bench, BaseMapObject Target)> benchTargets =
            new List<(BaseMapObject, BaseMapObject)>();
        private Vector3 benchSeatAnchor;
        private float benchSitProgress;
        private bool leavingBench;

        private void BuildBenchTargets()
        {
            benchTargets.Clear();
            if (!BaseDemo.Active(State)) return;
            foreach (var bench in MapObjects)
            {
                if (bench.AssetId != "bench" || bench.Rotation != 0) continue;
                double front = bench.Y - bench.Height / 2;
                for (int side = -1; side <= 1; side += 2)
                {
                    double x = bench.X + side * bench.Width * .29;
                    double y = front - BaseMap.Radius - .06;
                    if (!BaseSpatial.Walkable(State, x, y)) continue;
                    var seat = new BaseMapObject(bench.Id + (side < 0 ? ":seat:left" : ":seat:right"),
                        side < 0 ? "坐下 · 长椅左侧" : "坐下 · 长椅右侧", x, y, .1, .1, false, kind: BenchSeatKind);
                    benchTargets.Add((bench, seat));
                }
            }
        }

        private BaseMapObject NearbyBenchSeat(BaseMapObject other)
        {
            if (benchSeat != null) return StandBenchTarget;
            if (benchTargets.Count == 0 || BaseDemoPrologue.Evacuating(State) || BaseSceneCombat.Active(State) ||
                State.Aftermath.GameOver || ConstructionMode || RebuildPlacementMode) return other;

            double nearest = System.Math.Min(.65, other?.Distance(State.PlayerX, State.PlayerY) ?? double.PositiveInfinity);
            var result = other;
            foreach (var (bench, seat) in benchTargets)
            {
                if (State.PlayerY > bench.Y - bench.Height / 2 + .05) continue;
                double distance = System.Math.Sqrt((State.PlayerX - seat.X) * (State.PlayerX - seat.X) +
                    (State.PlayerY - seat.Y) * (State.PlayerY - seat.Y));
                if (distance >= nearest || !SceneObjectVisible(bench) || !BaseInteraction.CanReach(State, seat)) continue;
                nearest = distance;
                result = seat;
            }
            return result;
        }

        private void UseBenchSeat(BaseMapObject target)
        {
            if (benchSeat != null)
            {
                leavingBench = true;
                return;
            }

            var pawn = player.GetComponent<BattlePawn2D>();
            if (!pawn.CanSitOnBench) return;
            benchSeat = target;
            // The visual moves to the bench. The walkable position stays at the original approach point.
            benchSeatAnchor = WorldPoint(target.X, target.Y + BaseMap.Radius - .03);
            BaseInteraction.Facing = new BasePoint(0, -1);
            ClearSelection();
            PlacePlayer();
            Hud.RefreshMovement();
        }

        private bool UpdateBenchSeat(Vector2 input, double seconds)
        {
            if (benchSeat == null) return false;
            if (input.sqrMagnitude > .0001f) leavingBench = true;
            benchSitProgress = Mathf.MoveTowards(benchSitProgress, leavingBench ? 0 : 1, (float)(seconds / .36));
            if (leavingBench && benchSitProgress == 0) ClearBenchSeat();
            PlacePlayer();
            return true;
        }

        private void ClearBenchSeat()
        {
            benchSeat = null;
            leavingBench = false;
            benchSitProgress = 0;
        }
    }
}
