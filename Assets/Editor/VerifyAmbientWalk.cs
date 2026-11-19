using System;
using System.IO;
using System.Linq;
using BaseConquest.Base;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static class VerifyAmbientWalk
    {
        private static void Need(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        public static void Run()
        {
            var home = new BasePoint(5.5, 5.5);
            var far = new BasePoint(14, 14);
            var walk = new BaseAmbientWalk("town_girl", home, home);
            int probes = 0;
            bool Pass(double x, double y) { probes++; return x < 6.2; }
            for (int t = 0; t < 150; t++) walk.Tick(.05, far, Pass, 16, 16);
            Need(probes == 0, "人物站定时仍在检查地形");
            int trips = 0;
            bool moving = false;
            for (int t = 0; t < 1200; t++)
            {
                bool moved = walk.Tick(.05, far, Pass, 16, 16);
                if (moved && !moving) trips++;
                moving = moved;
                Need(walk.Position.Distance(home) <= BaseAmbientWalk.Radius + .000001 && walk.Position.X < 6.2, "闲逛越过范围或碰撞");
            }
            Need(trips >= 2 && trips <= 8, "一分钟闲逛次数不合理：" + trips);
            int priorProbes = probes;
            var stopped = walk.Position;
            for (int t = 0; t < 450; t++) walk.Tick(.05, stopped, Pass, 16, 16);
            Need(walk.Position.Distance(stopped) == 0 && probes == priorProbes, "玩家接近时人物仍在移动或探路");
            var trapped = new BaseAmbientWalk("blocked", home, home);
            int blockedProbes = 0;
            for (int t = 0; t < 1200; t++) trapped.Tick(.05, far, (x,y) => { blockedProbes++; return false; }, 16, 16);
            Need(blockedProbes > 0 && blockedProbes <= 64 && trapped.Position.Distance(home) == 0, "不可达时持续重试或穿过了障碍");

            BaseMapObject Person(string id) => new BaseMapObject(id,id,3,3,.5,.5,false,"editor:"+id,"villager","npc");
            foreach (var id in new[]{"town_girl","town_guard","custom-civilian"})
                Need(BaseAmbientWalk.IsAmbient(Person(id)), "普通人物没有固定范围闲逛：" + id);
            foreach (var id in new[]{"town_quest","town_blacksmith","town_peddler","intro_laozhou","intro_chunshen",
                "demo_rescued_1","demo_survivor","prologue_report","demo_azhu","partner_farm","demo_galan_waiting","story-shell:test","motion-check:test"})
                Need(!BaseAmbientWalk.IsAmbient(Person(id)), "功能或剧情人物错误启用了闲逛：" + id);

            var raw = File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"BaseScenarios",BaseDemo.BalanceTemplate));
            var state = BaseDemo.CreateOpening(JsonUtility.FromJson<BaseScenarioDocument>(raw),raw,true,true,true,DemoBalanceRuntime.Load().Rules).CreateState();
            Need(BaseDemoIntro.Choose(state,"male","平原"),"未能开场");
            var guard = new BaseCompanionMovement();
            var escort = new BaseCompanionAgent { Id="escort-test",Region="base",Visible=true,Following=true,Arrived=true,
                Position=BaseSpatial.Nearest(state,new BasePoint(state.PlayerX+.6,state.PlayerY)) };
            guard.Agents.Add(escort);
            int guardReads = 0;
            BaseMapObject[] ReadGuard() { guardReads++; return BaseSpatial.Objects(state); }
            int guardTrips = 0;
            bool previouslyMoving = false;
            for(int t=0;t<1200;t++)
            {
                var before=escort.Position;
                guard.Update(state,.05,readGeometry:ReadGuard);
                bool moved=before.Distance(escort.Position)>.000001;
                if(moved && !previouslyMoving) guardTrips++;
                previouslyMoving=moved;
            }
            Need(guardTrips>0 && guardTrips<=9,"随行队长驻足警戒频率异常："+guardTrips);
            for(int t=0;t<500 && !(escort.Arrived && escort.PauseRemaining>=7.5);t++)
                guard.Update(state,.05,readGeometry:ReadGuard);
            Need(escort.Arrived && escort.PauseRemaining>=7.5,"随行队长未停留");
            guard.Update(state,.05,readGeometry:ReadGuard);
            guardReads=0;
            for(int t=0;t<140;t++) guard.Update(state,.05,readGeometry:ReadGuard);
            Need(guardReads==0 && escort.Arrived,"随行队长站定时仍在重算地图");
            var crowd = new BaseCompanionMovement();
            crowd.Synchronize(state,true);
            var residents = crowd.Agents.Where(a => a.SceneOnly && a.Visible).ToArray();
            Need(residents.Length == 2,"序章两位普通村民缺席");
            var starts = residents.Select(a => a.Position).ToArray();
            int geometryReads = 0;
            BaseMapObject[] Read() { geometryReads++; return BaseSpatial.Objects(state); }
            for (int t = 0; t < 140; t++) crowd.Update(state,.05,readGeometry:Read);
            Need(geometryReads == 0,"报信前闲置的村民仍读取全图");
            int villageTrips = 0;
            var movedOnLastTick = new bool[residents.Length];
            for (int t = 0; t < 1200; t++)
            {
                var before = residents.Select(a => a.Position).ToArray();
                crowd.Update(state,.05,readGeometry:Read);
                for (int n=0; n<residents.Length; n++)
                {
                    bool moved = residents[n].Position.Distance(before[n]) > .00001;
                    if (moved && !movedOnLastTick[n]) villageTrips++;
                    movedOnLastTick[n] = moved;
                    Need(residents[n].Position.Distance(starts[n]) <= BaseAmbientWalk.Radius + .01,"序章村民远离出生点");
                }
            }
            Need(villageTrips > 0 && villageTrips <= 16,"序章闲逛次数不合理：" + villageTrips);
            crowd.Capture(state);
            state.Demo.Intro.Step = 6;
            for (int t=0;t<1200 && !state.Demo.Intro.ReporterArrived;t++) BaseDemoIntro.AdvanceReporter(state,.05);
            Need(state.Demo.Intro.ReporterArrived && state.Demo.Intro.ReportGroup.Count>0,"报信人没有集合村民");
            crowd.Synchronize(state);
            var gathered = crowd.Agents.Where(a=>a.Visible && a.SceneOnly).ToArray();
            BasePoint Assigned(BaseCompanionAgent actor) => state.Demo.Intro.ReportGroup.Single(member=>member.Id==actor.Id).Target;
            for (int t=0;t<1600 && gathered.Any(a=>a.Position.Distance(Assigned(a))>=.08);t++)
                crowd.Update(state,.05,readGeometry:Read);
            Need(gathered.All(a=>a.Position.Distance(Assigned(a))<.08),"村民未到指定会合点");
            for(int t=0;t<4;t++) crowd.Update(state,.05,readGeometry:Read);
            geometryReads=0;
            for (int t=0;t<180;t++) crowd.Update(state,.05,readGeometry:Read);
            Need(geometryReads==0 && gathered.All(a=>a.Position.Distance(Assigned(a))<.08),"报信后已站定的人仍在移动或重算路线");
            state.Demo.Intro.Step=7;
            for (int t=0;t<1600 && !BaseDemoPrologue.Evacuating(state);t++) BaseDemoIntro.AdvanceDeparture(state,.05);
            Need(BaseDemoPrologue.Evacuating(state),"民兵离场未推进撤离");
            Debug.Log($"PASS AmbientWalk：镇民每分钟走动 {trips} 次；受阻一分钟探测 {blockedProbes} 次；原地警戒每分钟走动 {guardTrips} 次；序章两人每分钟共走动 {villageTrips} 次；报信前 7 秒、集结后 9 秒站定期间地形读取 0 次；四名民兵完成离场。");
        }
    }
}
