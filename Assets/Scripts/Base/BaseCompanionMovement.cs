using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.Base
{
    public sealed class BaseCompanionAgent
    {
        public string Id, Name, FacilityId = "";
        public BasePoint Position, Destination;
        public bool Visible, Arrived;
        public bool Resting;
        public bool Following;
        public bool TemporarilyDetached;
        public string MapId;
        public bool SceneOnly;
        public string Region = "base";
        public bool ReturningToGuard {get;internal set;}
        public double PauseRemaining {get;internal set;}
        public double AttentionRemaining;
        internal int PatrolStep;
        internal BasePoint GuardCenter;
        internal BaseAmbientWalk VillageWalk;
        internal double SightWait;
        internal bool GuardVisible = true;
        internal BasePoint LastObserver, LastGuard;
        internal List<BasePoint> Route = new List<BasePoint>();
        internal int Waypoint;
        internal double BlockedSeconds;
        internal double RepathSeconds;
        internal BaseMotionAssist Motion=new BaseMotionAssist();
        public BaseMapObject Footprint => new BaseMapObject(Id, SceneOnly?Name:"伙伴 · " + Name, Position.X, Position.Y, 0.45, 0.45);
    }
    // 表现任职与随行，日期／库存／资格仍由唯一经营状态决定。
    public sealed class BaseCompanionMovement
    {
        public readonly List<BaseCompanionAgent> Agents = new List<BaseCompanionAgent>();
        // Wide circles leave room to patrol; different radii/phases keep companions from sharing a target.
        public const double GuardRadiusFirst=6,GuardRadiusSecond=6.5,PauseMinimum=8,PauseRange=8;
        public static double GuardRadius(BaseState state,string id)=>state.Adventure.Members.IndexOf(id)<=1 ? GuardRadiusFirst : GuardRadiusSecond;
        public BaseCompanionMovement()
        {
            foreach (var obj in BaseMap.Objects.Where(o => o.Id.StartsWith("partner")))
                Agents.Add(new BaseCompanionAgent { Id = obj.Id, Position = new BasePoint(obj.X, obj.Y) });
        }
        public IEnumerable<BaseMapObject> Footprints => Agents.Where(a => a.Visible).Select(a => a.Footprint);
        // A follower can overlap another body when detached. Let existing overlaps separate; restore blocking once clear.
        public IEnumerable<BaseMapObject> PlayerObstacles(BaseState state) => Agents.Where(a => a.Visible && !a.Following).Select(a => a.Footprint)
            .Where(o=>!o.Contains(state.PlayerX,state.PlayerY,BaseMap.Radius));
        private static IEnumerable<BaseLeader> People(BaseState state)=>state.Leaders.Concat(BaseDemoPrologue.Followers(state));
        private bool Route(BaseState state, BaseCompanionAgent agent, BasePoint goal, BaseMapObject[] blockers = null, BaseMapObject[] geometry = null, bool detour = true)
        {
            agent.Destination = goal; agent.Arrived = agent.Position.Distance(goal) < .08;
            var objects=geometry ?? BaseSpatial.Objects(state,string.IsNullOrEmpty(agent.MapId)?agent.Region:agent.MapId);var board=BaseBattlefield.Board(state,string.IsNullOrEmpty(agent.MapId)?agent.Region:agent.MapId);
            Func<double,double,bool> terrain=(x,y)=>BaseSpatial.Geometry(objects,x,y,board.Width,board.Height)&&BaseSpatial.TerrainWalkable(board,x,y,objects);
            // Moving followers reserve during stepping, rather than closing the only corridor in the static route.
            var routing=agent.Following?blockers?.Where(o=>!Agents.Any(a=>a.Following&&a.Id==o.Id)).ToArray():blockers;
            var direct=BaseMap.EightLink(agent.Position,goal,routing,terrain,false,board.Width,board.Height);
            agent.Route=direct??(detour?BaseMap.FindRoute(agent.Position,goal,routing,terrain,false,board.Width,board.Height):new List<BasePoint>());
            if(agent.Following)
            {
                // Skip visible grid centres on a new route so replanning never makes a follower step back.
                int visible=-1;
                for(int i=agent.Route.Count-1;i>=0;i--)if(BaseMap.EightAligned(agent.Position,agent.Route[i])&&BaseMap.ClearSegment(agent.Position,agent.Route[i],routing,terrain,false,board.Width,board.Height)){visible=i;break;}
                if(visible>0)agent.Route.RemoveRange(0,visible);
            }
            agent.Waypoint = 0; agent.BlockedSeconds = 0; agent.RepathSeconds = 0;
            return agent.Arrived||agent.Route.Count>0;
        }
        public void Synchronize(BaseState state, bool reset = false)
        {
            if(reset && BaseAdventure.Members(state).Any(l=>l.Id!=state.PlayerLeaderId&&BaseAdventure.Region(l)!=state.Campaign.Location)) BaseAdventure.Arrive(state);
            foreach(var leader in People(state).Where(l=>l.Id!=state.PlayerLeaderId&&!Agents.Any(a=>a.Id==l.Id)))
                Agents.Add(new BaseCompanionAgent {Id=leader.Id,Position=leader.HasMapPosition ? new BasePoint(leader.MapX,leader.MapY) : leader.HasHomePosition ? new BasePoint(leader.HomeX,leader.HomeY) : new BasePoint(7+(Agents.Count%12)*1.1,7.9+(Agents.Count/12)*.04)});
            foreach (var agent in Agents)
            {
                if(reset){agent.Motion=new BaseMotionAssist();agent.SightWait=0;agent.GuardVisible=true;agent.LastObserver=new BasePoint(double.NegativeInfinity,0);}
                var person = People(state).FirstOrDefault(p => p.Id == agent.Id);agent.SceneOnly=BaseDemoPrologue.Followers(state).Any(follower=>follower.Id==agent.Id);
                var home = BaseSpatial.Objects(state,"base").FirstOrDefault(o => o.Id == agent.Id)??new BaseMapObject(agent.Id,"伙伴 · "+person?.Name,7+(Agents.IndexOf(agent)%12)*1.1,7.9+(Agents.IndexOf(agent)/12)*.04,.45,.45);
                if(person?.HasHomePosition==true)home=new BaseMapObject(agent.Id,"伙伴 · "+person.Name,person.HomeX,person.HomeY,.45,.45);
                var pause=person==null?null:BaseAdventure.PauseFor(state,person.Id);
                bool following = person != null && (BaseAdventure.Following(state,person.Id)||agent.SceneOnly&&BaseDemoPrologue.FollowingPlayer(state));
                string region = following ? state.Campaign.Location : pause?.Region??"base";
                string mapId=BaseSceneInstances.Active(state)?following?state.Campaign.CurrentMapId:pause?.MapId??BaseSpatial.Layout(state,"base").SourceMapId:null;
                if (reset || agent.Region != region || agent.MapId!=mapId) agent.Position = person?.HasMapPosition == true && BaseAdventure.Region(person)==region && BaseSpatial.Walkable(state,person.MapX,person.MapY,mapId??region)
                    ? new BasePoint(person.MapX, person.MapY) : new BasePoint(home.X, home.Y);
                agent.Region = region;agent.MapId=mapId;
                if(reset&&person!=null)agent.Position=BaseSpatial.Nearest(state,agent.Position,mapId??region,Agents.Where(a=>a!=agent&&a.Visible).Select(a=>a.Footprint));
                agent.Visible = person != null && BaseAdventure.Alive(person) && person.Health > 0 && region==state.Campaign.Location && (!BaseSceneInstances.Active(state)||mapId==state.Campaign.CurrentMapId) && (following || pause!=null || !person.Away);
                if(agent.Id=="partner_craft"&&BaseDemoFlow.SmithVisit(state))agent.Visible=false;
                agent.Name = person==null ? "" : agent.SceneOnly?person.Name:BasePersonnel.LeaderName(person);
                bool changed = agent.Following != following||agent.TemporarilyDetached!=(pause!=null); agent.Following = following;agent.TemporarilyDetached=pause!=null;if(following)agent.AttentionRemaining=0;
                agent.Resting = false;
                string job = following ? "adventure" : pause!=null?"paused":person?.FacilityId ?? "";
                if(pause!=null){agent.FacilityId=job;agent.GuardCenter=new BasePoint(pause.X,pause.Y);if(changed||reset){agent.Route.Clear();agent.Arrived=true;agent.ReturningToGuard=false;agent.PatrolStep=0;agent.PauseRemaining=PauseMinimum+PauseRange*Sample(agent,53);}continue;}
                if(agent.SceneOnly&&!following)
                {
                    if (agent.VillageWalk == null || reset || changed)
                    {
                        var origin = person.HasHomePosition ? new BasePoint(person.HomeX, person.HomeY) : agent.Position;
                        agent.VillageWalk = new BaseAmbientWalk(agent.Id, origin, agent.Position);
                        agent.Route.Clear();
                        agent.Arrived = true;
                    }
                    agent.FacilityId="village-idle";
                    continue;
                }
                agent.VillageWalk = null;
                if (following) { agent.FacilityId = job; if(changed || reset) {agent.Route.Clear();agent.Arrived=true;agent.ReturningToGuard=false;agent.PatrolStep=0;agent.PauseRemaining=PauseMinimum+PauseRange*Sample(agent,53);agent.RepathSeconds=.3;} continue; }
                BasePoint goal = job == "" ? new BasePoint(home.X, home.Y) : BaseSpatial.WorkPost(state,job);
                if (reset || changed || agent.FacilityId != job || agent.Destination.Distance(goal) > 0.01)
                {
                    agent.FacilityId = job; Route(state,agent,goal);
                }
            }
        }
        public void Capture(BaseState state)
        {
            foreach (var agent in Agents)
            {
                var person = People(state).FirstOrDefault(p => p.Id == agent.Id);
                if (person == null) continue;
                person.HasMapPosition = true; person.MapX = agent.Position.X; person.MapY = agent.Position.Y; person.MapRegion=agent.Region;
                person.MapId=agent.MapId;
            }
        }
        private BaseMapObject[] Obstacles(BaseCompanionAgent agent, BaseState state) => Agents.Where(a => a.Visible && a != agent).Select(a => a.Footprint)
            .Concat(agent.Following||agent.Region!=state.Campaign.Location ? new BaseMapObject[0] : new[] { new BaseMapObject("player", "主角", state.PlayerX, state.PlayerY, 0.45, 0.45) })
            .Where(o=>!o.Contains(agent.Position.X,agent.Position.Y,BaseMap.Radius)).ToArray();
        private static double Sample(BaseCompanionAgent agent,int salt)
        {
            uint value=2166136261;foreach(char c in agent.Id)value=(value^c)*16777619;
            value+=(uint)(agent.PatrolStep*747796405+salt*289133645);value^=value<<13;value^=value>>17;value^=value<<5;
            return (value&0xffffff)/16777215d;
        }
        private bool GuardRoute(BaseState state,BaseCompanionAgent agent,BasePoint player,BaseMapObject[] blockers,bool returning,BaseMapObject[] objects)
        {
            double radius=GuardRadius(state,agent.Id);int member=Math.Max(1,state.Adventure.Members.IndexOf(agent.Id));
            var board=BaseBattlefield.Board(state,string.IsNullOrEmpty(agent.MapId)?agent.Region:agent.MapId);
            bool Terrain(double x,double y)=>BaseSpatial.Geometry(objects,x,y,board.Width,board.Height)&&BaseSpatial.TerrainWalkable(board,x,y,objects);
            agent.PatrolStep++;
            for(int n=0;n<12;n++)
            {
                double angle=member*2.399963+Sample(agent,n+1)*Math.PI*2;
                double distance=returning ? radius*.45 : radius*(.3+.5*Sample(agent,n+17));
                var goal=new BasePoint(player.X+Math.Cos(angle)*distance,player.Y+Math.Sin(angle)*distance);
                if(!Terrain(goal.X,goal.Y)||!BaseMap.Walkable(goal.X,goal.Y,blockers,false,board.Width,board.Height)||!returning&&goal.Distance(agent.Position)<.8)continue;
                if(Agents.Any(a=>a!=agent&&a.Visible&&a.Following&&goal.Distance(a.Destination)<.7))continue;
                if(!BaseMap.ClearSegment(player,goal,null,Terrain,false,board.Width,board.Height))continue;
                if(Route(state,agent,goal,blockers,objects,detour:false)){agent.GuardCenter=player;return true;}
            }
            if(returning&&Route(state,agent,player,blockers,objects)){agent.GuardCenter=player;return true;}
            agent.Route.Clear();agent.Arrived=true;agent.RepathSeconds=0;agent.PauseRemaining=PauseMinimum;return false;
        }
        private void Guard(BaseState state,BaseCompanionAgent agent,BasePoint player,BaseMapObject[] blockers,double seconds,BaseMapObject[] objects)
        {
            double radius=GuardRadius(state,agent.Id);
            if (agent.SightWait <= 0 && (player.Distance(agent.LastObserver) > .12 || agent.Position.Distance(agent.LastGuard) > .12))
            {
                var board=BaseBattlefield.Board(state,string.IsNullOrEmpty(agent.MapId)?agent.Region:agent.MapId);
                agent.GuardVisible=BaseMap.ClearSegment(agent.Position,player,null,(x,y)=>BaseSpatial.Geometry(objects,x,y,board.Width,board.Height)&&BaseSpatial.TerrainWalkable(board,x,y,objects),false,board.Width,board.Height);
                agent.LastObserver=player;agent.LastGuard=agent.Position;agent.SightWait=.4;
            }
            bool outside=agent.Position.Distance(player)>radius||!agent.GuardVisible;
            if(outside)
            {
                bool start=!agent.ReturningToGuard;agent.ReturningToGuard=true;agent.PauseRemaining=0;
                if(start||agent.RepathSeconds>=.65&&(agent.GuardCenter.Distance(player)>.6||agent.Route.Count==0||agent.Arrived||agent.BlockedSeconds>=.65))GuardRoute(state,agent,player,blockers,true,objects);
                return;
            }
            if(agent.ReturningToGuard)
            {
                if(agent.Position.Distance(player)>radius-.75&&!agent.Arrived)return;
                agent.ReturningToGuard=false;agent.Route.Clear();agent.Arrived=true;agent.PauseRemaining=0;
            }
            if(agent.Arrived||agent.Route.Count==0)
            {
                agent.PauseRemaining=Math.Max(0,agent.PauseRemaining-seconds);
                if(agent.PauseRemaining<=0)GuardRoute(state,agent,player,blockers,false,objects);
            }
            else if(agent.Destination.Distance(player)>radius||agent.BlockedSeconds>=1.2&&agent.RepathSeconds>=1.2)GuardRoute(state,agent,player,blockers,false,objects);
        }
        public void Update(BaseState state, double seconds,bool allowFollowing=true,bool sceneActorsOnly=false,Func<BaseMapObject[]> readGeometry=null)
        {
            double attentionSeconds=Math.Max(0,seconds);
            seconds = Math.Max(0, Math.Min(seconds, 0.05));
            var player = new BasePoint(state.PlayerX,state.PlayerY);
            bool reportGathering = BaseVillageReport.Gathering(state);
            BaseMapObject[] sceneGeometry = null;
            BaseMapObject[] Geometry() => sceneGeometry ??= readGeometry == null ? BaseSpatial.Objects(state) : readGeometry();
            foreach (var agent in Agents)
            {
                if(sceneActorsOnly&&!agent.SceneOnly)continue;
                if(agent.Following&&!allowFollowing)continue;
                agent.RepathSeconds += seconds;
                if(!agent.Visible)continue;
                agent.SightWait -= seconds;
                bool scripted=agent.SceneOnly&&BaseDemoPrologue.Village(state)&&(!BaseDemoIntro.Daily(state)||state.Demo.Intro.ReporterArrived);
                if(!scripted&&!agent.Following)
                {
                    agent.AttentionRemaining=agent.Position.Distance(player)<=2?3:Math.Max(0,agent.AttentionRemaining-attentionSeconds);
                    if(agent.AttentionRemaining>0)continue;
                }
                var reportMember = reportGathering && agent.SceneOnly ? BaseVillageReport.Find(state, agent.Id) : null;
                if (agent.VillageWalk != null && reportMember == null && !scripted)
                {
                    if (sceneActorsOnly) continue;
                    var board = BaseBattlefield.Board(state);
                    BaseMapObject[] neighbors = null;
                    bool CanWalk(double x,double y)
                    {
                        Geometry();
                        neighbors ??= Obstacles(agent, state);
                        foreach (var occupant in sceneGeometry)
                            if (occupant.Kind == "npc" && !BaseDemoNpcs.IsStoryShell(occupant) && occupant.Contains(x,y,BaseMap.Radius)) return false;
                        return BaseMap.Walkable(x,y,neighbors,false,board.Width,board.Height) &&
                            BaseSpatial.Geometry(sceneGeometry,x,y,board.Width,board.Height) && BaseSpatial.TerrainWalkable(board,x,y,sceneGeometry);
                    }
                    if (agent.VillageWalk.Tick(seconds,player,CanWalk,board.Width,board.Height))
                        agent.Position=agent.VillageWalk.Position;
                    continue;
                }
                bool pursuit = agent.SceneOnly && BaseDemoPrologue.Pursuit(state);
                if (reportMember != null && agent.Arrived && agent.Position.Distance(reportMember.Target) < .08) continue;
                if (!pursuit && reportMember == null && !agent.Following && !agent.TemporarilyDetached && agent.Arrived) continue;
                var center = agent.Following ? (agent.SceneOnly ? BaseDemoPrologue.GatherCenter(state) : player) : agent.GuardCenter;
                if (!pursuit && reportMember == null && agent.Arrived && agent.PauseRemaining > 0 &&
                    !agent.ReturningToGuard && agent.GuardVisible && center.Distance(agent.LastObserver) < .12 &&
                    agent.Position.Distance(center) <= GuardRadius(state,agent.Id))
                { agent.PauseRemaining = Math.Max(0, agent.PauseRemaining - seconds); continue; }
                var blockers = Obstacles(agent, state);
                var objects=Geometry();
                if (reportMember != null)
                {
                    bool needsRoute = agent.Route.Count == 0 && agent.Position.Distance(reportMember.Target) >= .08;
                    if (agent.Destination.Distance(reportMember.Target) > .01 ||
                        !agent.Arrived && agent.RepathSeconds >= .5 && (needsRoute || agent.BlockedSeconds >= .5))
                        Route(state, agent, reportMember.Target, blockers, objects);
                }
                else if(pursuit)
                {
                    var exit=BaseDemoPrologue.Exit(state,agent.Id);
                    if(agent.Destination.Distance(exit)>.01||agent.BlockedSeconds>=.8)Route(state,agent,exit,blockers,objects);
                }
                else if(agent.Following || agent.TemporarilyDetached)Guard(state,agent,center,blockers,seconds,objects);
                if (!agent.Visible || agent.Arrived) continue;
                double budget = seconds * (agent.Following ? 4.8 : 2.8); bool blocked = false;
                while (budget > 0 && agent.Waypoint < agent.Route.Count)
                {
                    var target = agent.Route[agent.Waypoint]; double distance = agent.Position.Distance(target);
                    if (distance < 0.001) { agent.Waypoint++; continue; }
                    double step = Math.Min(budget, Math.Min(distance, 0.08));
                    double dx=(target.X-agent.Position.X)/distance*step,dy=(target.Y-agent.Position.Y)/distance*step;
                    double x=agent.Position.X,y=agent.Position.Y;
                    if(agent.TemporarilyDetached&&new BasePoint(x+dx,y+dy).Distance(agent.GuardCenter)>GuardRadius(state,agent.Id)){blocked=true;break;}
                    BaseSpatial.Move(state,ref x,ref y,dx,dy,blockers,agent.Motion,objects);
                    var next=new BasePoint(x,y);
                    if(next.Distance(agent.Position)<.000001){blocked=true;break;}
                    agent.Position=next;budget-=step;
                }
                bool justArrived=!agent.Arrived&&agent.Position.Distance(agent.Destination)<.03;
                agent.Arrived=agent.Position.Distance(agent.Destination)<.03;
                if((agent.Following||agent.TemporarilyDetached)&&justArrived&&!agent.ReturningToGuard)agent.PauseRemaining=PauseMinimum+PauseRange*Sample(agent,53);
                if (blocked || agent.Route.Count == 0) agent.BlockedSeconds += seconds; else agent.BlockedSeconds = 0;
                if (!agent.Following&&!agent.TemporarilyDetached&&agent.BlockedSeconds >= 0.8) Route(state,agent,agent.Destination,blockers,objects);
            }
        }
    }
}
