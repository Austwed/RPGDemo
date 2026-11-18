using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.Base
{
    [Serializable]
    public sealed class BaseVillageReportMember
    {
        public string Id;
        public double X, Y, TargetX, TargetY;

        [NonSerialized] private List<BasePoint> route;
        [NonSerialized] private int waypoint;

        public BasePoint Target => new BasePoint(TargetX, TargetY);
        public BaseVillageReportMember Copy() => (BaseVillageReportMember)MemberwiseClone();
        internal void SetRoute(List<BasePoint> path) => route = path;
        internal void Advance(BaseState state, double seconds)
        {
            var position = new BasePoint(X, Y);
            if (position.Distance(Target) < .08) return;
            route ??= BaseStageWalk.Route(state, position, Target);
            BaseStageWalk.Advance(ref X, ref Y, Target, seconds, route, ref waypoint);
        }
    }

    internal static class BaseVillageReport
    {
        public static bool Gathering(BaseState state) =>
            BaseDemoPrologue.Village(state) && state.Demo.Intro?.Step == 6 &&
            state.Demo.Intro.ReporterArrived && !state.Demo.Intro.ReporterLeaving;

        public static BasePoint Center(BaseState state) =>
            new BasePoint(state.Demo.Intro.ReporterX, state.Demo.Intro.ReporterY);

        public static BasePoint Target(BaseState state)
        {
            var marker = BaseSpatial.Layout(state)?.Objects.FirstOrDefault(item => item.Id == "prologue_report");
            return BaseSpatial.Nearest(state, new BasePoint((marker?.X ?? 9) + 1, (marker?.Y ?? 8.3) + 2.5));
        }

        public static BaseVillageReportMember Find(BaseState state, string id) =>
            state.Demo.Intro.ReportGroup?.FirstOrDefault(member => member.Id == id);

        public static BaseMapObject Position(BaseState state, BaseMapObject person)
        {
            var member = Gathering(state) ? Find(state, person.Id) : null;
            return member == null ? person : new BaseMapObject(person.Id, person.Name, member.X, member.Y,
                person.Width, person.Height, person.Blocks, person.SourceObjectId, person.AssetId, person.Kind);
        }

        public static void Prepare(BaseState state)
        {
            var intro = state.Demo.Intro;
            if (!Gathering(state) || intro.ReportGroup?.Count > 0) return;
            var center = Center(state);
            var offsets = new[]
            {
                new BasePoint(-1.5, 0), new BasePoint(-1.55, -.9), new BasePoint(-1.05, -1.65),
                new BasePoint(0, -1.8), new BasePoint(1.05, -1.65), new BasePoint(1.55, -.9),
                new BasePoint(1.5, 0), new BasePoint(1.05, .9)
            };
            var available = offsets.Select(offset => new BasePoint(center.X + offset.X, center.Y + offset.Y)).ToList();
            var people = BaseDemoIntro.Additional(state).Where(person => person.Kind == "npc")
                .Select(person => (person.Id, Origin: new BasePoint(person.X, person.Y)))
                .Concat(BaseDemoPrologue.Followers(state).Select(person => (person.Id, Origin: new BasePoint(person.MapX, person.MapY))));
            var occupied = new List<BaseMapObject> { new BaseMapObject("report-center", "", center.X, center.Y, .5, .5) };
            intro.ReportGroup = new List<BaseVillageReportMember>();
            foreach (var person in people)
            {
                var nearest = available.Count == 0 ? center : available.OrderBy(point => point.Distance(person.Origin)).First();
                available.Remove(nearest);
                var goal = BaseSpatial.Nearest(state, nearest, occupants: occupied);
                var route = BaseStageWalk.Route(state, person.Origin, goal);
                if (route.Count == 0 && person.Origin.Distance(goal) >= .08)
                    goal = person.Origin;
                occupied.Add(new BaseMapObject(person.Id, "", goal.X, goal.Y, .44, .44));
                var member = new BaseVillageReportMember
                {
                    Id = person.Id, X = person.Origin.X, Y = person.Origin.Y,
                    TargetX = goal.X, TargetY = goal.Y
                };
                member.SetRoute(route);
                intro.ReportGroup.Add(member);
            }
        }

        public static void Validate(BaseDemoIntroState intro, int width, int height)
        {
            if (intro.ReportGroup == null) return;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in intro.ReportGroup)
                if (member == null || string.IsNullOrWhiteSpace(member.Id) || !ids.Add(member.Id) ||
                    !InBounds(member.X, member.Y) || !InBounds(member.TargetX, member.TargetY))
                    throw new ArgumentException("村民集结位置非法");
            bool InBounds(double x, double y) => x >= 0 && y >= 0 && x < width && y < height;
        }
    }
}
