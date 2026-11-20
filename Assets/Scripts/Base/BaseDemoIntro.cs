using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;

namespace BaseConquest.Base
{
    [Serializable] public sealed class BaseDemoIntroState
    {
        public int Step, DrillFlags, FarmWorkers=2, DummyPhase, DummyHits;
        // 四名村庄剑士按固定编号保存岗位；早期存档只记录粮田人数。
        public string[] VillageWorkerPosts;
        public BaseLeader Captain;public int DeparturePhase,MilitiaSpawned;public double DepartureWait,MilitiaDelay;
        public List<BaseLeader> Militia=new List<BaseLeader>();
        public string Hero="", PlayerName="", PracticeId="";
        public double Walked, CaptainX=10, CaptainY=8.6;
        public bool Zoomed, BackpackOpened, MoveOrdered;
        public double DrillMoveOrigin;
        public double ReporterX=.6,ReporterY=7.2;
        public bool ReporterArrived,ReporterLeaving,ReporterGone;
        [NonSerialized] internal List<BasePoint> ReporterRoute;
        [NonSerialized] internal BasePoint ReporterTarget;
        [NonSerialized] internal int ReporterWaypoint;
        [NonSerialized] internal BaseDepartureWalk[] DepartureRoutes;
        [NonSerialized] internal BasePoint? FarmDoor;
        public List<BaseVillageReportMember> ReportGroup = new List<BaseVillageReportMember>();
        public double TutorialDelay;
        // 村民闲聊过的 NPC id（PRO_02 首次／再次两套台词；旧档读进来为 null，按空处理）。
        public List<string> Talked=new List<string>();
        public BaseDemoIntroState Copy(){var i=(BaseDemoIntroState)MemberwiseClone();i.DepartureRoutes=null;i.Captain=Captain?.Copy();i.Militia=Militia.Select(v=>v.Copy()).ToList();i.Talked=Talked==null?new List<string>():Talked.ToList();i.ReportGroup=ReportGroup?.Select(member=>member.Copy()).ToList()??new List<BaseVillageReportMember>();i.VillageWorkerPosts=VillageWorkerPosts?.ToArray();return i;}
    }

    internal sealed class BaseDepartureWalk
    {
        private List<BasePoint> path;
        private BasePoint requested, goal;
        private int waypoint;
        private bool followedLastTarget;
        private double remaining;

        internal bool Advance(BaseState state,ref double x,ref double y,BasePoint target,double seconds,bool follow=false)
        {
            remaining -= seconds;
            if (path == null || followedLastTarget != follow || (!follow && target.Distance(requested) > .01) ||
                (path.Count == 0 && remaining <= 0 && new BasePoint(x, y).Distance(goal) >= .08) ||
                (follow && remaining <= 0 && target.Distance(requested) > .45))
            {
                requested = target;
                followedLastTarget = follow;
                goal = follow ? BaseSpatial.Nearest(state, target) : target;
                path = BaseStageWalk.Route(state, new BasePoint(x,y), goal);
                waypoint = 0;
                remaining = .65;
            }
            return BaseStageWalk.Advance(ref x, ref y, goal, seconds, path, ref waypoint);
        }
    }

    public static class BaseDemoIntro
    {
        public const string BattleId="demo:prologue:practice";
        public const int Select=1, Pause=2, Move=4, Attack=8, All=Select|Pause|Move|Attack;
        private const int RetiredDrillFlags = 16 | 32 | 64; // 驻守、技能、查看详情的旧存档位。
        public const int DummyDemo=128, AutoAttack=256;
        public const string DummyId="intro:training-dummy";
        private static readonly int[] DrillControls={Select,Pause,Move};
        public static bool Active(BaseState s)=>BaseDemo.Active(s)&&s.Demo.Intro!=null;
        public static bool Daily(BaseState s)=>Active(s)&&BaseDemoPrologue.Village(s)&&s.Demo.Intro.Step is not (6 or 7 or 8);
        public static string PlayerArt(BaseState s)=>Active(s)&&s.Demo.Intro.Hero=="female"?"female":"player";
        public static string CaptainArt(BaseState s)=>PlayerArt(s)=="female"?"player":"female";
        public static string Address(BaseState s)=>PlayerArt(s)=="female"?"姑娘":"少侠";
        public static string PartnerName(BaseState s)=>BaseScriptNames.Partner;
        public const int PlayerNameLimit=12;
        public static bool TryPlayerName(string input,out string name)
        {
            name=(input??"").Trim();
            return name.Length>0&&name.Length<=PlayerNameLimit
                &&name.All(c=>!char.IsControl(c)&&!char.IsSurrogate(c)&&"<>:\"/\\|?*{}.".IndexOf(c)<0);
        }
        public static string Objective(BaseState s)
        {
            var i=s.Demo.Intro;
            switch(i.Step)
            {
                case 0:return "选择你的主角，开启"+BaseScriptNames.Village+"的清晨";
                case 1:return "走近搭档交谈";
                case 2:return "前往麦田问候阿禾";
                case 9:return "为粮田调配 4 名工人，确认方案";
                case 10:return "找"+PartnerName(s)+"领取佩剑";
                case 3:return "打开背包并装备旧制短剑";
                case 4:return "和搭档交谈，开始民兵演练";
                case 5:return "演练已经完成，等待村口的消息";
                case 6:return "村口传来急促脚步声，去听取村民报告";
                case 7:return "护送阿禾与村民向东撤离";
                default:return "死战断后，掩护村民出村";
            }
        }
        public static void Enable(BaseState s,bool preserveWeaponScale=false)
        {
            s.Demo.Intro=new BaseDemoIntroState();if(BaseDemoPrologue.Village(s)){if(s.Demo.StoryScene?.Valid==true)s.Demo.StoryScene.InstallVillage(s);else{s.Demo.Intro.CaptainX=10.2;s.Demo.Intro.CaptainY=9.1;s.PlayerX=s.Campaign.BaseX=7.9;s.PlayerY=s.Campaign.BaseY=9.1;s.Campaign.Layouts.RemoveAll(m=>m.Region=="base");var village=BaseDemoPrologue.CreateVillage(true);village.ForbidSoldiers=true;s.Campaign.Layouts.Add(village);}}if(!preserveWeaponScale)s.Demo.Rpg.WeaponAttackScale=1;s.Demo.Rpg.SceneRulesVersion=1;BaseSceneRules.Install(s);if(s.Demo.Stage==BaseDemoStage.PrologueBriefing)BaseDemoPrologue.SeedVillagers(s);foreach(var leader in s.Leaders.Concat(s.Demo.Candidates))leader.PreferredStrategy=leader.Id==s.PlayerLeaderId?SquadStrategy.Obey:SquadStrategy.Attack;
            if(!BaseDemoRelease.Enabled(s)||s.Demo.Encounters?.Version>0)return;
            BaseEnemyPreset Preset(SoldierKind[] captains,GridPosition[] positions,int melee,int ranged,int cavalry)
            {
                var p=new BaseEnemyPreset {Version=2,Melee=melee,Ranged=ranged,Cavalry=cavalry,Commanders=captains.Select(kind=>new BaseEnemyCommanderPreset{Profession=kind,Level=1}).ToList()};int index=0;
                foreach(var kind in new[]{SoldierKind.Hero,SoldierKind.Melee,SoldierKind.Ranged,SoldierKind.Cavalry})
                {
                    int count=kind==SoldierKind.Hero?captains.Length:kind==SoldierKind.Melee?melee:kind==SoldierKind.Ranged?ranged:cavalry;
                    for(int n=0;n<count;n++)p.Units.Add(new BaseEnemyDeployment {Id=(kind==SoldierKind.Hero?"commander":kind.ToString().ToLowerInvariant())+"-"+(n+1),Kind=kind,CommanderSlot=kind==SoldierKind.Hero?n:kind==SoldierKind.Ranged&&captains.Length>1?1:0,Cell=positions[index++]});
                }
                p.Validate();return p;
            }
            s.Demo.Encounters=new BaseDemoEncounterConfig {Version=2,
                Ruins=Preset(new[]{SoldierKind.Melee},new[]{new GridPosition(27,12),new GridPosition(25,8),new GridPosition(25,16)},2,0,0),
                Defense=Preset(new[]{SoldierKind.Ranged},new[]{new GridPosition(3,12),new GridPosition(5,8),new GridPosition(5,9),new GridPosition(5,16),new GridPosition(5,17),new GridPosition(2,9),new GridPosition(2,16)},4,2,0),
                Camp=Preset(new[]{SoldierKind.Cavalry,SoldierKind.Ranged,SoldierKind.Melee},new[]{new GridPosition(27,9),new GridPosition(28,12),new GridPosition(28,16),new GridPosition(25,8),new GridPosition(25,9),new GridPosition(25,16),new GridPosition(25,17),new GridPosition(27,8),new GridPosition(27,16),new GridPosition(27,17),new GridPosition(26,12)},4,3,1)};
            // 巴图不持有普通兵槽；他缺席时其余两队的人数保持不变。
            foreach(var unit in s.Demo.Encounters.Camp.Units.Where(unit=>unit.Kind!=SoldierKind.Hero&&unit.CommanderSlot==1))unit.CommanderSlot=2;
            s.Demo.Encounters.Camp.Commanders[2].SkillId="inspire";
            // 剧情队长写死数值、技能与特性（2026-11-02 用户裁决：这类关卡的重要剧情角色用固定搭配）。
            // 只有反复刷的遗迹控制者保持随机——PrepareEnemies 里把 slot 0 的 authored 置空即是此意。
            // 模板必须一起写死：Generate 会先按 AwakenedChance 随机决定是否异能者，光固定特性数值仍会变。
            RpgTrait StoryTrait(string id,int value)=>new RpgTrait{Id=id,Value=value};
            void StoryFix(BaseEnemyCommanderPreset c,string template,string skill,RpgTrait[] traits)
            {c.TemplateId=template;c.SkillId=skill;c.FixedTraits=true;c.Traits=traits.ToList();}
            var batu=new[]{StoryTrait("tough",10),StoryTrait("frenzy",8)};                       // 巴图：老兵弓手，耐打、出手略快
            StoryFix(s.Demo.Encounters.Defense.Commanders[0],"captain_normal_archer","rapid_fire",batu);
            StoryFix(s.Demo.Encounters.Camp.Commanders[0],"captain_normal_knight","charge",new[]{StoryTrait("frenzy",15),StoryTrait("cruel",6)}); // 大当家：骑冲、凶悍
            StoryFix(s.Demo.Encounters.Camp.Commanders[1],"captain_normal_archer","rapid_fire",batu);
            StoryFix(s.Demo.Encounters.Camp.Commanders[2],"captain_normal_warrior","inspire",new[]{StoryTrait("tough",12),StoryTrait("taunt",2)}); // 豁牙：近战肉盾拉仇恨
            s.Demo.Encounters.Validate();
        }
        public static bool Choose(BaseState s,string hero,string playerName)
        {
            if(!Daily(s)||s.Demo.Intro.Step!=0||!new[]{"male","female"}.Contains(hero)||!TryPlayerName(playerName,out var name))return false;
            s.Demo.Intro.Hero=hero;s.Demo.Intro.PlayerName=name;s.Demo.Intro.Step=1;
            var left=new BasePoint(s.PlayerX,s.PlayerY);var right=new BasePoint(s.Demo.Intro.CaptainX,s.Demo.Intro.CaptainY);
            var opening=BaseSpatial.Nearest(s,hero=="male"?left:right);var partner=BaseSpatial.Nearest(s,hero=="male"?right:left);
            s.PlayerX=s.Campaign.BaseX=opening.X;s.PlayerY=s.Campaign.BaseY=opening.Y;s.Demo.Intro.CaptainX=partner.X;s.Demo.Intro.CaptainY=partner.Y;
            var player=s.Leaders.Single(l=>l.Id==s.PlayerLeaderId);player.AppearanceId=PlayerArt(s);player.Name=name;return true;
        }
        public static void Walk(BaseState s,double distance)
        {if(Daily(s)&&s.Demo.Intro.Step==2&&distance>1e-6&&distance<2)s.Demo.Intro.Walked+=distance;}
        public static bool Meet(BaseState s)
        {if(!Daily(s)||s.Demo.Intro.Step!=1||!At(s,"demo_captain"))return false;JoinCaptain(s);s.Demo.Intro.Step=2;return true;}
        public static void JoinCaptain(BaseState s)
        {
            if(s.Leaders.Any(l=>l.Id=="demo_captain"))return;
            var captain=s.Leaders.Single(l=>l.Id==s.PlayerLeaderId).Copy();captain.Id="demo_captain";captain.Name=BaseScriptNames.Partner;captain.AppearanceId=CaptainArt(s);captain.AcquisitionSource=BaseScriptNames.Village+" · 临时同行";captain.AcquisitionDay=s.Day;
            captain.PreferredStrategy=SquadStrategy.Attack;
            captain.HasMapPosition=true;captain.MapX=s.Demo.Intro.CaptainX;captain.MapY=s.Demo.Intro.CaptainY;captain.MapRegion="base";
            if(captain.Rpg!=null)captain.Rpg.Id=captain.Id;
            foreach(var g in captain.PersonalEquipment.Items){g.Id="personal:"+s.PersonalEquipment.NextId++;if(g.BoundLeader!="")g.BoundLeader=captain.Id;}
            if (!captain.PersonalEquipment.Items.Any(gear => gear.Slot == "weapon"))
                captain.PersonalEquipment.Items.Add(new BasePersonalItem
                {
                    Id = "personal:" + s.PersonalEquipment.NextId++, Template = "basic_sword", Slot = "weapon"
                });
            s.Leaders.Add(captain);s.Adventure.Members.Add(captain.Id);
        }
        public static bool Patrol(BaseState s)
        {
            if(!Daily(s)||s.Demo.Intro.Step!=2||!At(s,"intro_villager"))return false;
            s.Demo.Intro.Step=9;return true;
        }
        public static bool ReceiveSword(BaseState s)
        {
            if(!Daily(s)||s.Demo.Intro.Step!=10||!At(s,"demo_captain"))return false;
            var player=s.Leaders.Single(l=>l.Id==s.PlayerLeaderId);var i=s.Demo.Intro;
            int cell=BaseBackpack.RequireGearCell(s,player.Id);
            var captain=s.Leaders.Single(l=>l.Id=="demo_captain");i.Captain=captain.Copy();i.CaptainX=captain.MapX;i.CaptainY=captain.MapY;
            s.Leaders.Remove(captain);s.Adventure.Members.Remove(captain.Id);
            // The temporary companion takes their own possessions when leaving the party.
            s.PersonalEquipment.Warehouse.RemoveAll(g=>g.Holder==captain.Id);s.Backpack.Items.RemoveAll(g=>g.Holder==captain.Id);
            var item=new BasePersonalItem{Id="personal:"+s.PersonalEquipment.NextId++,Template="novice_sword",Holder=player.Id,Cell=cell};s.PersonalEquipment.Warehouse.Add(item);i.PracticeId=item.Id;i.Step=3;return true;
        }
        public static void CheckEquipment(BaseState s)
        {
            if(Daily(s)&&s.Demo.Intro.Step==3&&s.Demo.Intro.BackpackOpened&&s.Leaders.Single(l=>l.Id==s.PlayerLeaderId).PersonalEquipment.Items.Any(g=>g.Id==s.Demo.Intro.PracticeId&&g.Slot=="weapon"))s.Demo.Intro.Step=4;
        }
        public static bool At(BaseState s,string id)=>BaseSpatial.Objects(s).Concat(s.Leaders.Where(l=>l.Id=="demo_captain").Select(l=>new BaseMapObject(l.Id,l.Name,l.MapX,l.MapY,.45,.45))).Any(o=>o.Id==id&&BaseInteraction.CanReach(s,o));
        public static (string Id, string Name, string Art) VillageManager(string facility) => facility switch
        {
            "intro_farm" => ("intro_chunshen", BaseScriptNames.ChunShen, "chunshen"),
            "intro_smith" => ("intro_laozhou", BaseScriptNames.LaoZhou, "laozhou"),
            _ => default
        };

        private static void AddVillageManager(BaseState state, string facility, List<BaseMapObject> people)
        {
            var site = BaseSpatial.Layout(state)?.Objects.FirstOrDefault(obj => obj.Id == facility);
            if (site == null) return;
            var manager = VillageManager(facility);
            double x = facility == "intro_farm" ? site.X + Math.Min(site.Width * .5 + .65, 2) : site.X + .85;
            double y = site.Y - site.Height * .5 - .65;
            people.Add(new BaseMapObject(manager.Id, manager.Name + " · 交谈", x, y, .5, .5, false,
                "demo-npc:" + facility + ":manager", manager.Art, "npc"));
        }

        public static BaseMapObject[] Additional(BaseState s)
        {
            if(!Active(s)||!BaseDemoPrologue.Village(s)||s.Demo.Stage!=BaseDemoStage.PrologueBriefing)return Array.Empty<BaseMapObject>();
            var i=s.Demo.Intro;var result=new List<BaseMapObject>();
            if(i.Step!=8&&!s.Leaders.Any(l=>l.Id=="demo_captain"))result.Add(new BaseMapObject("demo_captain",PartnerName(s)+" · 交谈",i.CaptainX,i.CaptainY,.5,.5,false,"demo-npc:captain",CaptainArt(s),"npc"));
            if(Daily(s)||!i.ReporterArrived)
            {
                var authored=s.Demo.StoryScene?.Valid==true?s.Demo.StoryScene.Map("village").Layout.Objects.Single(obj=>obj.Id=="intro_villager"):null;
                result.Add(new BaseMapObject("intro_villager","阿禾 · 交谈",authored?.X??12,authored?.Y??10,.5,.5,false,"demo-npc:intro-villager","ahe","npc"));

            }
            if (!BaseSceneRules.Active(s) || Daily(s) && i.Step >= 2 || i.Step == 6)
            {
                result.Add(new BaseMapObject("intro_militia_1","东队民兵",18,10,.5,.5,false,"demo-npc:militia-1","saber","npc"));
                result.Add(new BaseMapObject("intro_militia_2","西队民兵",3,8,.5,.5,false,"demo-npc:militia-2","saber","npc"));
            }
            if (Daily(s) || i.Step == 6)
            {
                AddVillageManager(s, "intro_farm", result);
                AddVillageManager(s, "intro_smith", result);
            }
            return result.Select(person => BaseVillageReport.Position(s, person)).ToArray();
        }
        public static bool AssignFarm(BaseState s,int workers)
        {
            if (!Active(s) || workers < 2 || workers > 4) return false;
            var posts = VillagePosts(s.Demo.Intro);
            int delta = workers - posts.Count(p => p == "intro_farm");
            for (int n = 0; n < posts.Length && delta != 0; n++)
            {
                if (delta > 0 && posts[n] != "intro_farm") { posts[n] = "intro_farm"; delta--; }
                else if (delta < 0 && posts[n] == "intro_farm") { posts[n] = "intro_smith"; delta++; }
            }
            return AssignVillagePosts(s, posts);
        }
        public static string[] VillagePosts(BaseDemoIntroState intro) =>
            intro.VillageWorkerPosts?.Length == 4 ? intro.VillageWorkerPosts.ToArray() :
            Enumerable.Range(0, 4).Select(n => n < intro.FarmWorkers ? "intro_farm" : "intro_smith").ToArray();

        private static bool ValidVillagePosts(string[] posts) =>
            posts?.Length == 4 && posts.All(p => p == "" || p == "intro_farm" || p == "intro_smith");

        public static bool AssignVillagePosts(BaseState s, string[] posts)
        {
            if (!Daily(s) || !(At(s,"intro_farm") || At(s,"intro_smith")) || !ValidVillagePosts(posts)) return false;
            // PRO_02_LOCK_FARM／LOCK_SMITH：还没听阿禾说完人手安排，先别动粮田与铁匠铺的岗位。
            if(s.Demo.Intro.Step<9)throw new InvalidOperationException(At(s,"intro_farm")?"阿禾正在田埂边清点麦种，先过去听听她怎么安排。":"炉火正旺，先去麦田问问阿禾今天的人手安排。");
            if (VillagePosts(s.Demo.Intro).SequenceEqual(posts)) return false;
            s.Demo.Intro.VillageWorkerPosts = posts.ToArray();
            s.Demo.Intro.FarmWorkers = posts.Count(p => p == "intro_farm");
            CompleteFarmAssignment(s);
            return true;
        }
        public static int FarmOutput(BaseState s)=>(int)Math.Floor(s.Demo.Intro.FarmWorkers*s.Rules.FarmPerWorker);
        public static int VillageFood(BaseState s)=>4*s.Rules.SoldierFood+2*(BaseRpg.Active(s)?s.Demo.Rpg.LeaderFood:s.Rules.LeaderFood);
        public static double SmithWork(BaseState s)=>(s.Demo.Intro.VillageWorkerPosts?.Length == 4 ?
            s.Demo.Intro.VillageWorkerPosts.Count(p => p == "intro_smith") : 4-s.Demo.Intro.FarmWorkers)*s.Rules.WorkshopPerWorker;
        internal static void CompleteFarmAssignment(BaseState s)
        {
            if(Daily(s)&&s.Demo.Intro.Step==9&&s.Demo.Intro.FarmWorkers==4)s.Demo.Intro.Step=10;
        }
        public static bool AdvanceReporter(BaseState s,double seconds)
        {
            if(!Active(s)||s.Demo.Intro.Step!=6||s.Demo.Intro.ReporterArrived)return false;var i=s.Demo.Intro;
            if (i.ReporterRoute == null)
            {
                i.ReporterTarget = BaseVillageReport.Target(s);
                i.ReporterRoute = BaseStageWalk.Route(s, new BasePoint(i.ReporterX, i.ReporterY), i.ReporterTarget);
            }
            if(!BaseStageWalk.Advance(ref i.ReporterX,ref i.ReporterY,i.ReporterTarget,seconds*1.28,i.ReporterRoute,ref i.ReporterWaypoint))return false;
            i.ReporterRoute = null;
            i.ReporterWaypoint = 0;
            i.ReporterArrived=true;BaseDemoPrologue.GatherVillagers(s);BaseVillageReport.Prepare(s);return true;
        }
        public static bool Depart(BaseState s)
        {
            if(!Active(s)||s.Demo.Intro.Step!=6||!s.Demo.Intro.ReporterArrived||!BaseInteraction.At(s,"base","prologue_report")&&!At(s,"demo_captain"))return false;
            s.Demo.Intro.Step=7;s.Demo.Intro.ReporterLeaving=true;s.Demo.Intro.ReportGroup?.Clear();return true;
        }
        public static bool AdvanceReporterExit(BaseState s,double seconds)
        {
            var i=s.Demo.Intro;if(!i.ReporterLeaving||i.ReporterGone)return false;
            var target = new BasePoint(23.4, 7.2);
            i.ReporterRoute ??= BaseStageWalk.Route(s, new BasePoint(i.ReporterX, i.ReporterY), target);
            if(!BaseStageWalk.Advance(ref i.ReporterX,ref i.ReporterY,target,seconds*1.28,i.ReporterRoute,ref i.ReporterWaypoint))return false;
            i.ReporterRoute = null;
            i.ReporterWaypoint = 0;
            i.ReporterGone = true;
            return true;
        }
        public static void AdvanceTutorialDelay(BaseDemoIntroState i,double seconds)=>i.TutorialDelay=Math.Max(0,i.TutorialDelay-Math.Max(0,seconds));
        public static bool AdvanceDeparture(BaseState s,double seconds)
        {
            if(!Active(s)||s.Demo.Intro.Step!=7)return false;
            var i=s.Demo.Intro;seconds=Math.Max(0,seconds);
            i.FarmDoor ??= BaseStageWalk.Door(s,"intro_farm");
            var door = i.FarmDoor.Value;
            var exit = new BasePoint(.6,7.2);
            i.DepartureRoutes ??= new BaseDepartureWalk[5];
            var captain = i.DepartureRoutes[0] ??= new BaseDepartureWalk();
            // Small deterministic steps keep the cutscene resumable with any caller tick size.
            while(seconds>1e-8&&i.Step==7){double dt=Math.Min(.05,seconds);seconds-=dt;
                if(i.DeparturePhase==0){if(captain.Advance(s,ref i.CaptainX,ref i.CaptainY,door,dt))i.DeparturePhase=1;}
                else if(i.DeparturePhase==1){i.DepartureWait+=dt;if(i.DepartureWait>=1)i.DeparturePhase=2;}
                else if(i.DeparturePhase==2){i.MilitiaDelay-=dt;if(i.MilitiaDelay<=0&&i.MilitiaSpawned<4){int n=i.MilitiaSpawned++;i.Militia.Add(new BaseLeader{Id="intro_militia_"+n,Name="村庄剑士",AppearanceId="saber",Health=100,MaxHealth=100,MapX=door.X,MapY=door.Y,HasMapPosition=true});i.MilitiaDelay=.4;}if(i.MilitiaSpawned==4)i.DeparturePhase=3;}
                else if(i.DeparturePhase==3){if(captain.Advance(s,ref i.CaptainX,ref i.CaptainY,exit,dt))i.DeparturePhase=4;}
                for (int n = 0; n < i.Militia.Count; n++)
                {
                    var person = i.Militia[n];
                    var walk = i.DepartureRoutes[n + 1] ??= new BaseDepartureWalk();
                    bool follow = i.DeparturePhase < 4;
                    var target = follow ? new BasePoint(i.CaptainX+1+(n%2)*.6,i.CaptainY+(n/2)*.6) : exit;
                    walk.Advance(s,ref person.MapX,ref person.MapY,target,dt,follow);
                }
                if(i.DeparturePhase==4&&i.Militia.Count==4&&i.Militia.All(p=>new BasePoint(p.MapX,p.MapY).Distance(new BasePoint(.6,7.2))<.12)){i.Step=8;BaseDemoPrologue.BeginEscape(s);return true;}
            }return false;
        }
        public static BaseCampaignSession Practice(BaseState s)
        {
            if(!Daily(s)||s.Demo.Intro.Step!=4||!(At(s,"demo_captain")||At(s,DummyId)))throw new InvalidOperationException(PartnerName(s)+"：［挑眉］赤手空拳就敢过招？快打开【背包】把【旧制短剑】装备上。");
            var player=s.Leaders.Single(l=>l.Id==s.PlayerLeaderId).Copy();if(player.Rpg!=null)player.Rpg.AutoSkill=false;
            var stats=BaseCommanderIdentity.Stats(player,new ArmyDefinition().Hero);player.Health=player.MaxHealth;var opponent=s.Demo.Intro.Captain?.Copy()??player.Copy();var target=BaseCommanderIdentity.Stats(opponent,new ArmyDefinition().Hero);
            var board=BaseSceneCombat.Project(s);foreach(var c in board.Cells)c.Zone=DeploymentZone.None;
            GridPosition Nearest(double x,double y,GridPosition? avoid=null)=>board.Positions().Where(p=>board.Walkable(p)&&(!avoid.HasValue||p!=avoid.Value)).OrderBy(p=>(p.X+.5-x)*(p.X+.5-x)+(p.Z+.5-y)*(p.Z+.5-y)).First();
            var a=Nearest(s.PlayerX,s.PlayerY);var b=Nearest(s.Demo.Intro.CaptainX,s.Demo.Intro.CaptainY,a);board.Cell(a).Zone=DeploymentZone.A;board.Cell(b).Zone=DeploymentZone.B;
            var scenario=new BattleScenario {Board=board,CampaignRules=true,FogEnabled=false,PlayerHeroAppearance=true,PlayerLeaderSlot=0,MaxRounds=360,InstantDeathEnabled=false,
                A=new ArmyDefinition {HeroCount=1,MeleeCount=0,RangedCount=0,CavalryCount=0,Hero=stats},B=new ArmyDefinition {HeroCount=1,MeleeCount=0,RangedCount=0,CavalryCount=0,Hero=target},
                Objective=new ObjectiveConfig {Title="村庄演练 · 选人、暂停、移动、木桩、攻击"},Placements=new List<UnitPlacement>{new UnitPlacement {Side=BattleSide.A,Kind=SoldierKind.Hero,CommanderSlot=0,Cell=a},new UnitPlacement {Side=BattleSide.B,Kind=SoldierKind.Hero,CommanderSlot=0,Cell=b}}};
            BaseSceneRules.Configure(s,scenario,new[]{player});
            var enemies=BaseDemoCombat.CreateEnemies(BattleId,scenario.B);BaseRpg.PrepareEnemies(s,enemies,BattleId);var partner=enemies.Single();partner.Rpg=opponent.Rpg.Copy();partner.Rpg.Id=partner.Id;partner.Rpg.AutoSkill=false;partner.Equipment=opponent.PersonalEquipment.Copy();partner.Stats=target.Clone();partner.Health=(float)opponent.MaxHealth;partner.IsAwakened=opponent.IsAwakened;
            var fixture=BaseSpatial.Objects(s).FirstOrDefault(o=>o.Id==DummyId);var dummy=fixture==null?board.Positions().Where(p=>board.Walkable(p)&&p!=a&&p!=b).OrderBy(p=>Math.Abs(p.X-13)+Math.Abs(p.Z-8)).First():new GridPosition((int)fixture.X,(int)fixture.Y);
            s.Demo.Intro.DummyPhase=s.Demo.Intro.DummyHits=0;
            var session=new BaseCampaignSession(new BaseCampaignBattle {Rpg=s.Demo.Rpg?.Copy(),Enemies=enemies,Id=BattleId,EventId=BattleId,DemoCombat=true,MapRegion="base",StartedDay=s.Day,Scenario=scenario,RealtimeRules=true,RealtimeRulesVersion=1,Leaders=new List<BaseLeader>{player},TacticalBaseDamage=stats.Damage,TacticalRules=new BattleTacticalRules(),TacticalObjects=new List<BattleTacticalObject>{new BattleTacticalObject{Id=DummyId,Kind="dummy",X=dummy.X,Y=dummy.Z,Health=500}}});
            session.Bind();if(!session.Battle.Start(out string problem))throw new InvalidOperationException(problem);return session;
        }
        public static int NextExercise(BaseDemoIntroState intro)
        {
            if(intro.TutorialDelay>1e-8)return 0;
            foreach(int control in DrillControls)
                if((intro.DrillFlags&control)==0)return control;
            return intro.DummyPhase<2?DummyDemo:intro.DummyPhase<4?AutoAttack:(intro.DrillFlags&Attack)==0?Attack:0;
        }
        public static void StartDummyDemo(RealtimeCampaignBattle run,BaseDemoIntroState intro)
        {if(intro.DummyPhase!=0)return;intro.DummyPhase=1;foreach(var u in run.Model.Units.Where(u=>u.Side==PrototypeSide.Enemy))run.Model.SetStrategy(u,PrototypeStrategy.Attack);}
        public static void Configure(RealtimeCampaignBattle run,BaseDemoIntroState intro)
        {
            int Next()=>NextExercise(intro);
            var player = run.Model.Player;
            var partner = run.Model.Units.Single(u => u.Side == PrototypeSide.Enemy);
            run.TutorialAction = flag =>
            {
                int next = Next();
                if ((flag & next) == 0) return;
                if (next == AutoAttack)
                {
                    if (intro.DummyPhase != 2) return;
                    intro.DummyPhase = 3;
                    run.Issue(new[] { player.Id - 1 }, PrototypeOrder.Auto, player.Position);
                }
                else if (next == Move)
                {
                    intro.MoveOrdered = true;
                    intro.DrillMoveOrigin = player.DistanceTravelled;
                }
                else
                {
                    intro.DrillFlags |= next;
                    if (next != Attack) intro.TutorialDelay = 1;
                }
            };
            run.Model.SetStrategy(player, PrototypeStrategy.Obey);
            run.Model.SetStrategy(partner, PrototypeStrategy.Obey);
            run.Model.DefaultDestination = _ => -1;
            run.Tactics.ObjectHit = (actor, obj, power) =>
            {
                if (obj.Id != DummyId || power <= 0 || intro.DummyPhase >= 4) return;
                if (intro.DummyPhase == 3 && actor.Id == player.Id && !actor.ManualOrder) intro.DummyHits++;
                if (obj.Health <= 0 || intro.DummyHits >= 3)
                {
                    intro.DummyPhase = 4;
                    run.Tactics.Cancel(new[] { player.Squad });
                    run.Model.SetStrategy(player, PrototypeStrategy.Obey);
                    run.Model.SetStrategy(partner, PrototypeStrategy.Obey);
                    intro.TutorialDelay = 1;
                }
                else if (intro.DummyPhase == 1 && actor.Id == partner.Id)
                {
                    intro.DummyPhase = 2;
                    actor.ObjectOrder = false;
                    actor.ObjectTargetId = "";
                    actor.ObjectWindupAt = -1;
                    run.Model.SetStrategy(actor, PrototypeStrategy.Obey);
                }
            };
            var targeting=run.Model.CanTarget;run.Model.CanTarget=(source,target)=>source.Side==PrototypeSide.Ally&&(intro.DrillFlags&Attack)!=0&&(targeting?.Invoke(source,target)??true);
            run.Model.CanMove=actor=>actor.Side==PrototypeSide.Ally||intro.DummyPhase==1;
            run.OutcomeResolver=current=>{
                if(intro.MoveOrdered&&current.Model.Player.DistanceTravelled-intro.DrillMoveOrigin>=.49){intro.DrillFlags|=Move;intro.MoveOrdered=false;intro.TutorialDelay=1;}
                return (intro.DrillFlags & All) == All && current.Model.Hits > 0 ? BattleOutcome.SideAWin : BattleOutcome.None;};
        }
        public static bool FinishPractice(BaseState s,bool completed)
        {if(!Active(s)||s.Demo.Intro.Step!=4||s.SceneBattleId!=BattleId)return false;s.SceneBattleId="";if(completed&&(s.Demo.Intro.DrillFlags&All)==All)s.Demo.Intro.Step=6;return true;}
        public static void Validate(BaseState s)
        {
            if(!Active(s))return;var i=s.Demo.Intro;var village=s.Demo.StoryScene?.Valid==true?s.Demo.StoryScene.Map("village").Board:null;int width=village?.Width??24,height=village?.Height??14;
            BaseVillageReport.Validate(i, width, height);
            if(i.DummyPhase<0||i.DummyPhase>4||i.DummyHits<0||i.DummyHits>3)throw new ArgumentException("木桩教学进度非法");
            bool namedPosts = i.VillageWorkerPosts != null && i.VillageWorkerPosts.Length != 0;
            if (namedPosts && (!ValidVillagePosts(i.VillageWorkerPosts) || i.VillageWorkerPosts.Count(p => p == "intro_farm") != i.FarmWorkers))
                throw new ArgumentException("村庄士兵岗位记录非法");
            if(i.Step<0||i.Step>10||i.Step>0&&!new[]{"male","female"}.Contains(i.Hero)||i.Step==0&&i.Hero!=""||(i.DrillFlags&~(All|RetiredDrillFlags))!=0||i.FarmWorkers<(namedPosts?0:2)||i.FarmWorkers>4||double.IsNaN(i.Walked)||double.IsInfinity(i.Walked)||i.Walked<0||i.CaptainX<0||i.CaptainX>=width||i.CaptainY<0||i.CaptainY>=height)throw new ArgumentException("序章教学进度非法");
            if(new[]{i.ReporterX,i.ReporterY,i.DrillMoveOrigin,i.TutorialDelay,i.CaptainX,i.CaptainY,i.DepartureWait,i.MilitiaDelay}.Any(v=>double.IsNaN(v)||double.IsInfinity(v))||i.ReporterX<0||i.ReporterX>=width||i.ReporterY<0||i.ReporterY>=height||i.DrillMoveOrigin<0||i.TutorialDelay<0||i.TutorialDelay>1)throw new ArgumentException("报信人与演练位置非法");
            if(i.Militia==null||i.MilitiaSpawned<0||i.MilitiaSpawned>4||i.Militia.Count!=i.MilitiaSpawned||i.DepartureWait<0||i.DepartureWait>1.05||i.MilitiaDelay<-.06||i.MilitiaDelay>.4||i.Militia.Any(v=>v==null||new[]{v.MapX,v.MapY}.Any(n=>double.IsNaN(n)||double.IsInfinity(n))||v.MapX<0||v.MapX>=width||v.MapY<0||v.MapY>=height)||i.DeparturePhase<0||i.DeparturePhase>4||i.Militia.Select(v=>v.Id).Distinct().Count()!=i.Militia.Count)throw new ArgumentException("序章民兵离场记录非法");
            if(i.Step>0&&s.Leaders.Single(l=>l.Id==s.PlayerLeaderId).AppearanceId!=PlayerArt(s))throw new ArgumentException("主角选择与形象不一致");
        }
    }
}
