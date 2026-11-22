using System;

namespace BaseConquest.Base
{
    // A short, local walk never needs a search across the whole map.
    public sealed class BaseAmbientWalk
    {
        public const double Radius = 2.25;
        public readonly BasePoint Home;
        public BasePoint Position { get; private set; }
        private BasePoint goal;
        private double wait;
        private bool walking;
        private uint random = 2166136261;

        public BaseAmbientWalk(string id, BasePoint home, BasePoint position)
        {
            Home = home;
            Position = position;
            foreach (char letter in id) random = unchecked((random ^ letter) * 16777619);
            Rest();
        }

        private double Next()
        {
            random = unchecked(random * 1664525 + 1013904223);
            return (random >> 8) / 16777216d;
        }

        private void Rest()
        {
            walking = false;
            wait = 8 + Next() * 8;
        }

        public static bool IsAmbient(BaseMapObject person)
        {
            if (person?.Kind != "npc" || string.IsNullOrEmpty(person.Id) || person.Blocks ||
                person.Id.StartsWith("motion-check:", StringComparison.Ordinal) || BaseDemoNpcs.IsStoryShell(person))
                return false;
            if (person.Id == "town_guard" || person.Id == "town_girl") return true;
            if (BaseStoryScenePackage.NpcRole(person.Id)) return false;
            // Scripted visitors, soldiers and followers take their positions from their own progression.
            return !person.Id.StartsWith("demo_", StringComparison.Ordinal) && !person.Id.StartsWith("intro_", StringComparison.Ordinal) &&
                !person.Id.StartsWith("prologue_", StringComparison.Ordinal) && !person.Id.StartsWith("partner", StringComparison.Ordinal);
        }

        public bool Tick(double seconds, BasePoint player, Func<double,double,bool> walkable, int width, int height)
        {
            seconds = Math.Min(.05, Math.Max(0, seconds));
            if (seconds == 0 || Position.Distance(player) <= 2) return false;
            if (!walking)
            {
                wait -= seconds;
                if (wait > 0) return false;
                int first = (int)(Next() * 8);
                double length = 1.25 + Next() * .65;
                for (int i = 0; i < 8; i++)
                {
                    double angle = (first + 3 * i) * (Math.PI / 4);
                    var next = new BasePoint(Position.X + length * Math.Cos(angle), Position.Y + length * Math.Sin(angle));
                    // Old saves may have a villager already outside the new radius. Let them walk back into it.
                    if (next.Distance(Home) >= Math.Max(Radius, Position.Distance(Home))) continue;
                    if (!BaseMap.ClearSegment(Position, next, null, walkable, false, width, height)) continue;
                    goal = next;
                    walking = true;
                    break;
                }
                if (!walking) { Rest(); return false; }
            }
            double remaining = Position.Distance(goal);
            double step = Math.Min(remaining, seconds * 1.5);
            var point = new BasePoint(Position.X + (goal.X - Position.X) * step / remaining,
                Position.Y + (goal.Y - Position.Y) * step / remaining);
            if (!walkable(point.X, point.Y)) { Rest(); return false; }
            Position = point;
            if (step >= remaining) Rest();
            return true;
        }
    }
}
