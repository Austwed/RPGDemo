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
        public BaseLeader Captain;public int DeparturePhase,MilitiaSpawned;public double DepartureWait,MilitiaDelay;
        public List<BaseLeader> Militia=new List<BaseLeader>();
        public string Hero="", PracticeId="", OriginalWeaponId="";
        public double Walked, CaptainX=10, CaptainY=8.6;
        public bool Zoomed, BackpackOpened, MoveOrdered;
        public double DrillMoveOrigin;
        public double ReporterX=.6,ReporterY=7.2;
        public bool ReporterArrived,ReporterLeaving,ReporterGone;
        public double TutorialDelay;
        // 村民闲聊过的 NPC id（PRO_02 首次／再次两套台词；旧档读进来为 null，按空处理）。
        public List<string> Talked=new List<string>();
        public BaseDemoIntroState Copy(){var i=(BaseDemoIntroState)MemberwiseClone();i.Captain=Captain?.Copy();i.Militia=Militia.Select(v=>v.Copy()).ToList();i.Talked=Talked==null?new List<string>():Talked.ToList();return i;}
    }

    public static class BaseDemoIntro
    {
        public const string BattleId="demo:prologue:practice";
        public const int Select=1, Pause=2, Move=4, Attack=8, Guard=16, Skill=32, Inspect=64, All=127;
        public const int DummyDemo=128, AutoAttack=256;
        public const string DummyId="intro:training-dummy";
        public static bool Active(BaseState s)=>BaseDemo.Active(s)&&s.Demo.Intro!=null;
        public static bool Daily(BaseState s)=>Active(s)&&BaseDemoPrologue.Village(s)&&!new[]{6,7,8}.Contains(s.Demo.Intro.Step);
        public static string PlayerArt(BaseState s)=>Active(s)&&s.Demo.Intro.Hero=="female"?"female":"player";
        public static string CaptainArt(BaseState s)=>PlayerArt(s)=="female"?"player":"female";
        public static string Address(BaseState s)=>PlayerArt(s)=="female"?"姑娘":"少侠";
        // 搭档名：优先取队长的实际名字；旧档把名字写死为「搭档」时按所选性别回填本名，不回写存档字段。
        public static string PartnerName(BaseState s)
        {
            var captain=s.Leaders.FirstOrDefault(l=>l.Id=="demo_captain");
            if(captain==null||string.IsNullOrEmpty(captain.Name)||captain.Name==BaseScriptNames.LegacyPartner)
                return BaseScriptNames.PartnerName(s);
            return captain.Name;
        }
        private static string PracticeObjective(BaseDemoIntroState i)
        {
            int ex=NextExercise(i);
            return ex==Select?"演练：选中指挥角色":ex==Pause?"演练：使用战术暂停":ex==Move?"演练：下达移动指令":ex==Guard?"演练：下达驻守指令":ex==Skill?"演练：施放战吼鼓舞":ex==Inspect?"演练：查看当前增益":ex==DummyDemo?"演练：观察攻击前摇":ex==AutoAttack?"演练：切换【进攻】策略":"演练：主动点击对手交锋";
        }
        public static string Objective(BaseState s)
        {
            var i=s.Demo.Intro;
            switch(i.Step)
            {
                case 0:return "选择你的主角，开启"+BaseScriptNames.Village+"的清晨";
                case 1:return "走近搭档，按 E 交谈";
                case 2:return "前往麦田问候阿禾";
                case 9:return (i.FarmWorkers==4?"■ ":"□ ")+"为粮田调配 4 名劳力（"+i.FarmWorkers+"/4）";
                case 10:return "找"+PartnerName(s)+"领取佩剑";
                case 3:return "按 B 打开背包穿戴【旧制短剑】，再找"+PartnerName(s)+"演练";
                case 4:return PracticeObjective(i);
                case 5:return "演练已经完成，等待村口的消息";
                case 6:return "村口传来急促脚步声，去听取村民报告";
                case 7:return "护送阿禾与村民向东撤离";
                default:return "死战断后，掩护村民出村";
            }
        }
        public static void Enable(BaseState s,bool preserveWeaponScale=false)
        {
            s.Demo.Intro=new BaseDemoIntroState();if(BaseDemoPrologue.Village(s)){s.Demo.Intro.CaptainX=10.2;s.Demo.Intro.CaptainY=9.1;s.PlayerX=s.Campaign.BaseX=7.9;s.PlayerY=s.Campaign.BaseY=9.1;}if(BaseDemoPrologue.Village(s)){s.Campaign.Layouts.RemoveAll(m=>m.Region=="base");var village=BaseDemoPrologue.CreateVillage(true);village.ForbidSoldiers=true;s.Campaign.Layouts.Add(village);}if(!preserveWeaponScale)s.Demo.Rpg.WeaponAttackScale=.75;s.Demo.Rpg.SceneRulesVersion=1;BaseSceneRules.Install(s);if(s.Demo.Stage==BaseDemoStage.PrologueBriefing)BaseDemoPrologue.SeedVillagers(s);foreach(var leader in s.Leaders.Concat(s.Demo.Candidates))leader.PreferredStrategy=leader.Id==s.PlayerLeaderId?SquadStrategy.Obey:SquadStrategy.Attack;
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
        public static bool Choose(BaseState s,string hero)
        {
            if(!Daily(s)||s.Demo.Intro.Step!=0||!new[]{"male","female"}.Contains(hero))return false;
            s.Demo.Intro.Hero=hero;s.Demo.Intro.Step=1;
            var left=new BasePoint(s.PlayerX,s.PlayerY);var right=new BasePoint(s.Demo.Intro.CaptainX,s.Demo.Intro.CaptainY);
            var opening=BaseSpatial.Nearest(s,hero=="male"?left:right);var partner=BaseSpatial.Nearest(s,hero=="male"?right:left);
            s.PlayerX=s.Campaign.BaseX=opening.X;s.PlayerY=s.Campaign.BaseY=opening.Y;s.Demo.Intro.CaptainX=partner.X;s.Demo.Intro.CaptainY=partner.Y;
            s.Leaders.Single(l=>l.Id==s.PlayerLeaderId).AppearanceId=PlayerArt(s);return true;
        }
        public static void Walk(BaseState s,double distance)
        {if(Daily(s)&&s.Demo.Intro.Step==2&&distance>1e-6&&distance<2)s.Demo.Intro.Walked+=distance;}
        public static bool Meet(BaseState s)
        {if(!Daily(s)||s.Demo.Intro.Step!=1||!At(s,"demo_captain"))return false;JoinCaptain(s);s.Demo.Intro.Step=2;return true;}
        public static void JoinCaptain(BaseState s)
        {
            if(s.Leaders.Any(l=>l.Id=="demo_captain"))return;
            var captain=s.Leaders.Single(l=>l.Id==s.PlayerLeaderId).Copy();captain.Id="demo_captain";captain.Name=BaseScriptNames.PartnerName(s);captain.AppearanceId=CaptainArt(s);captain.AcquisitionSource=BaseScriptNames.Village+" · 临时同行";captain.AcquisitionDay=s.Day;
            captain.PreferredStrategy=SquadStrategy.Attack;
            captain.HasMapPosition=true;captain.MapX=s.Demo.Intro.CaptainX;captain.MapY=s.Demo.Intro.CaptainY;captain.MapRegion="base";
            if(captain.Rpg!=null)captain.Rpg.Id=captain.Id;
            foreach(var g in captain.PersonalEquipment.Items){g.Id="personal:"+s.PersonalEquipment.NextId++;if(g.BoundLeader!="")g.BoundLeader=captain.Id;}
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
            int cell=BasePartyInventory.FreeCell(s,player.Id);if(cell<0)throw new InvalidOperationException("请先腾出一格背包");
            var captain=s.Leaders.Single(l=>l.Id=="demo_captain");i.Captain=captain.Copy();i.CaptainX=captain.MapX;i.CaptainY=captain.MapY;
            s.Leaders.Remove(captain);s.Adventure.Members.Remove(captain.Id);
            // The temporary companion takes their own possessions when leaving the party.
            s.PersonalEquipment.Warehouse.RemoveAll(g=>g.Holder==captain.Id);s.Backpack.Items.RemoveAll(g=>g.Holder==captain.Id);
            i.OriginalWeaponId=player.PersonalEquipment.Items.FirstOrDefault(g=>g.Slot=="weapon")?.Id??"";
            var item=new BasePersonalItem{Id="personal:"+s.PersonalEquipment.NextId++,Template="novice_sword",Holder=player.Id,Cell=cell};s.PersonalEquipment.Warehouse.Add(item);i.PracticeId=item.Id;i.Step=3;return true;
        }
        public static void CheckEquipment(BaseState s)
        {
            if(Daily(s)&&s.Demo.Intro.Step==3&&s.Demo.Intro.BackpackOpened&&s.Leaders.Single(l=>l.Id==s.PlayerLeaderId).PersonalEquipment.Items.Any(g=>g.Id==s.Demo.Intro.PracticeId&&g.Slot=="weapon"))s.Demo.Intro.Step=4;
        }
        public static bool At(BaseState s,string id)=>BaseSpatial.Objects(s).Concat(s.Leaders.Where(l=>l.Id=="demo_captain").Select(l=>new BaseMapObject(l.Id,l.Name,l.MapX,l.MapY,.45,.45))).Any(o=>o.Id==id&&BaseInteraction.CanReach(s,o));
        public static BaseMapObject[] Additional(BaseState s)
        {
            if(!Active(s)||!BaseDemoPrologue.Village(s)||s.Demo.Stage!=BaseDemoStage.PrologueBriefing)return Array.Empty<BaseMapObject>();
            var i=s.Demo.Intro;var result=new List<BaseMapObject>();
            if(i.Step!=8&&!s.Leaders.Any(l=>l.Id=="demo_captain"))result.Add(new BaseMapObject("demo_captain",PartnerName(s)+" · 交谈",i.CaptainX,i.CaptainY,.5,.5,false,"demo-npc:captain",CaptainArt(s),"npc"));
            if(Daily(s)||!i.ReporterArrived)
            {
                result.Add(new BaseMapObject("intro_villager","阿禾 · 交谈",12,10,.5,.5,false,"demo-npc:intro-villager","ahe","npc"));

                // PRO_02_MILITIA_IDLE：护村剑士在序章日常阶段可点击搭话；演练与撤离阶段不出现，免得挡路。
                if(!BaseSceneRules.Active(s)||Daily(s)&&i.Step>=2)
                {
                    result.Add(new BaseMapObject("intro_militia_1","东队民兵",18,10,.5,.5,false,"demo-npc:militia-1","infantry","npc"));
                    result.Add(new BaseMapObject("intro_militia_2","西队民兵",3,8,.5,.5,false,"demo-npc:militia-2","infantry","npc"));
                }
            }
            return result.ToArray();
        }
        public static bool AssignFarm(BaseState s,int workers)
        {
            if(!Daily(s)||!(At(s,"intro_farm")||At(s,"intro_smith"))||workers<2||workers>4)return false;
            // PRO_02_LOCK_FARM／LOCK_SMITH：还没听阿禾说完人手安排，先别动粮田与铁匠铺的岗位。
            if(s.Demo.Intro.Step<9)throw new InvalidOperationException(At(s,"intro_farm")?"阿禾正在田埂边清点麦种，先过去听听他的盘算吧。":"炉火正旺，先去麦田问问阿禾今天的人手安排。");
            s.Demo.Intro.FarmWorkers=workers;return true;
        }
        public static int FarmOutput(BaseState s)=>(int)Math.Floor(s.Demo.Intro.FarmWorkers*s.Rules.FarmPerWorker);
        public static int VillageFood(BaseState s)=>4*s.Rules.SoldierFood+2*(BaseRpg.Active(s)?s.Demo.Rpg.LeaderFood:s.Rules.LeaderFood);
        public static double SmithWork(BaseState s)=>(4-s.Demo.Intro.FarmWorkers)*s.Rules.WorkshopPerWorker;
        public static bool FinishFarm(BaseState s)
        {if(!Daily(s)||s.Demo.Intro.Step!=9||s.Demo.Intro.FarmWorkers!=4||!(At(s,"intro_farm")||At(s,"intro_smith")))return false;s.Demo.Intro.Step=10;return true;}
        public static bool AdvanceReporter(BaseState s,double seconds)
        {
            if(!Active(s)||s.Demo.Intro.Step!=6||s.Demo.Intro.ReporterArrived)return false;var i=s.Demo.Intro;
            var target=BaseSpatial.Nearest(s,new BasePoint(i.CaptainX-1.1,i.CaptainY));
            if(!BaseStageWalk.Advance(s,ref i.ReporterX,ref i.ReporterY,target,Math.Max(0,seconds)*3.2/2.5))return false;i.ReporterArrived=true;BaseDemoPrologue.GatherVillagers(s);return true;
        }
        public static bool Depart(BaseState s)
        {
            if(!Active(s)||s.Demo.Intro.Step!=6||!s.Demo.Intro.ReporterArrived||!BaseInteraction.At(s,"base","prologue_report")&&!At(s,"demo_captain"))return false;
            s.Demo.Intro.Step=7;s.Demo.Intro.ReporterLeaving=true;return true;
        }
        public static bool AdvanceReporterExit(BaseState s,double seconds)
        {
            var i=s.Demo.Intro;if(!i.ReporterLeaving||i.ReporterGone)return false;
            if(!BaseStageWalk.Advance(s,ref i.ReporterX,ref i.ReporterY,new BasePoint(23.4,7.2),Math.Max(0,seconds)*3.2/2.5))return false;i.ReporterGone=true;return true;
        }
        public static void AdvanceTutorialDelay(BaseDemoIntroState i,double seconds)=>i.TutorialDelay=Math.Max(0,i.TutorialDelay-Math.Max(0,seconds));
        public static bool AdvanceDeparture(BaseState s,double seconds)
        {
            if(!Active(s)||s.Demo.Intro.Step!=7)return false;
            var i=s.Demo.Intro;seconds=Math.Max(0,seconds);
            // Small deterministic steps keep the cutscene resumable with any caller tick size.
            while(seconds>1e-8&&i.Step==7){double dt=Math.Min(.05,seconds);seconds-=dt;var door=BaseStageWalk.Door(s,"intro_farm");
                if(i.DeparturePhase==0){if(BaseStageWalk.Advance(s,ref i.CaptainX,ref i.CaptainY,door,dt))i.DeparturePhase=1;}
                else if(i.DeparturePhase==1){i.DepartureWait+=dt;if(i.DepartureWait>=1)i.DeparturePhase=2;}
                else if(i.DeparturePhase==2){i.MilitiaDelay-=dt;if(i.MilitiaDelay<=0&&i.MilitiaSpawned<4){int n=i.MilitiaSpawned++;i.Militia.Add(new BaseLeader{Id="intro_militia_"+n,Name="村庄剑士",AppearanceId="infantry",Health=100,MaxHealth=100,MapX=door.X,MapY=door.Y,HasMapPosition=true});i.MilitiaDelay=.4;}if(i.MilitiaSpawned==4)i.DeparturePhase=3;}
                else if(i.DeparturePhase==3){if(BaseStageWalk.Advance(s,ref i.CaptainX,ref i.CaptainY,new BasePoint(.6,7.2),dt))i.DeparturePhase=4;}
                foreach(var person in i.Militia){int n=i.Militia.IndexOf(person);var target=i.DeparturePhase>=4?new BasePoint(.6,7.2):BaseSpatial.Nearest(s,new BasePoint(i.CaptainX+1+(n%2)*.6,i.CaptainY+(n/2)*.6));BaseStageWalk.Advance(s,ref person.MapX,ref person.MapY,target,dt);}
                if(i.DeparturePhase==4&&i.Militia.Count==4&&i.Militia.All(p=>new BasePoint(p.MapX,p.MapY).Distance(new BasePoint(.6,7.2))<.12)){i.Step=8;BaseDemoPrologue.BeginEscape(s);return true;}
            }return false;
        }
        public static void RemovePractice(BaseState s)
        {
            if(!Active(s)||string.IsNullOrEmpty(s.Demo.Intro.PracticeId))return;
            var i=s.Demo.Intro;var player=s.Leaders.Single(l=>l.Id==s.PlayerLeaderId);
            bool wore=player.PersonalEquipment.Items.RemoveAll(g=>g.Id==i.PracticeId)>0;
            s.PersonalEquipment.Warehouse.RemoveAll(g=>g.Id==i.PracticeId);
            if(wore&&!string.IsNullOrEmpty(i.OriginalWeaponId))
            {var original=s.PersonalEquipment.Warehouse.SingleOrDefault(g=>g.Id==i.OriginalWeaponId);if(original!=null){s.PersonalEquipment.Warehouse.Remove(original);original.Slot="weapon";original.Holder="";original.Cell=-1;player.PersonalEquipment.Items.Add(original);}}
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
                Objective=new ObjectiveConfig {Title="村庄演练 · 暂停、选择、移动、攻击、驻守、鼓舞"},Placements=new List<UnitPlacement>{new UnitPlacement {Side=BattleSide.A,Kind=SoldierKind.Hero,CommanderSlot=0,Cell=a},new UnitPlacement {Side=BattleSide.B,Kind=SoldierKind.Hero,CommanderSlot=0,Cell=b}}};
            BaseSceneRules.Configure(s,scenario,new[]{player});
            var enemies=BaseDemoCombat.CreateEnemies(BattleId,scenario.B);BaseRpg.PrepareEnemies(s,enemies,BattleId);var partner=enemies.Single();partner.Rpg=opponent.Rpg.Copy();partner.Rpg.Id=partner.Id;partner.Rpg.AutoSkill=false;partner.Equipment=opponent.PersonalEquipment.Copy();partner.Stats=target.Clone();partner.Health=(float)opponent.MaxHealth;partner.IsAwakened=opponent.IsAwakened;
            var fixture=BaseSpatial.Objects(s).FirstOrDefault(o=>o.Id==DummyId);var dummy=fixture==null?board.Positions().Where(p=>board.Walkable(p)&&p!=a&&p!=b).OrderBy(p=>Math.Abs(p.X-13)+Math.Abs(p.Z-8)).First():new GridPosition((int)fixture.X,(int)fixture.Y);
            s.Demo.Intro.DummyPhase=s.Demo.Intro.DummyHits=0;
            var session=new BaseCampaignSession(new BaseCampaignBattle {Rpg=s.Demo.Rpg?.Copy(),Enemies=enemies,Id=BattleId,EventId=BattleId,DemoCombat=true,MapRegion="base",StartedDay=s.Day,Scenario=scenario,RealtimeRules=true,RealtimeRulesVersion=1,Leaders=new List<BaseLeader>{player},TacticalBaseDamage=stats.Damage,TacticalRules=new BattleTacticalRules(),TacticalObjects=new List<BattleTacticalObject>{new BattleTacticalObject{Id=DummyId,Kind="dummy",X=dummy.X,Y=dummy.Z,Health=500}}});
            session.Bind();if(!session.Battle.Start(out string problem))throw new InvalidOperationException(problem);return session;
        }
        public static string DrillHint(int flags)=>"演练："+string.Join(" · ",new[]{Tuple.Create(Select,"选主角"),Tuple.Create(Pause,"暂停／继续"),Tuple.Create(Move,"移动"),Tuple.Create(Attack,"攻击"),Tuple.Create(Guard,"驻守"),Tuple.Create(Skill,"手动鼓舞"),Tuple.Create(Inspect,"查看增益")}.Select(x=>((flags&x.Item1)!=0?"■":"□")+x.Item2));
        public static int NextExercise(BaseDemoIntroState intro)
        {
            if(intro.TutorialDelay>1e-8)return 0;
            int preparation=new[]{Select,Pause,Move,Guard,Skill,Inspect}.FirstOrDefault(flag=>(intro.DrillFlags&flag)==0);
            return preparation!=0?preparation:intro.DummyPhase<2?DummyDemo:intro.DummyPhase<4?AutoAttack:(intro.DrillFlags&Attack)==0?Attack:0;
        }
        public static void StartDummyDemo(RealtimeCampaignBattle run,BaseDemoIntroState intro)
        {if(intro.DummyPhase!=0)return;intro.DummyPhase=1;foreach(var u in run.Model.Units.Where(u=>u.Side==PrototypeSide.Enemy))run.Model.SetStrategy(u,PrototypeStrategy.Attack);}
        public static void Configure(RealtimeCampaignBattle run,BaseDemoIntroState intro)
        {
            int Next()=>NextExercise(intro);
            run.TutorialAction=flag=>{int next=Next();if((flag&next)==0)return;if(next==AutoAttack){intro.DummyPhase=3;run.Issue(new[]{run.Model.Player.Id-1},PrototypeOrder.Auto,run.Model.Player.Position);return;}if(next==Move){intro.MoveOrdered=true;intro.DrillMoveOrigin=run.Model.Player.DistanceTravelled;}else {intro.DrillFlags|=next;if(next!=Attack)intro.TutorialDelay=1;}};
            run.TutorialProgress=()=>intro.DummyPhase>=2&&intro.DummyPhase<4?"进攻策略：自动攻击木桩 "+intro.DummyHits+"/3":DrillHint(intro.DrillFlags);
            run.Model.SetStrategy(run.Model.Player,PrototypeStrategy.Obey);run.Model.DefaultDestination=_=>-1;
            foreach(var u in run.Model.Units.Where(u=>u.Side==PrototypeSide.Enemy))run.Model.SetStrategy(u,PrototypeStrategy.Obey);
            run.Tactics.ObjectHit=(actor,obj,power)=>{
                if(obj.Id!=DummyId||power<=0)return;
                if(intro.DummyPhase==1&&actor.Side==PrototypeSide.Enemy){intro.DummyPhase=2;actor.ObjectOrder=false;actor.ObjectTargetId="";actor.ObjectWindupAt=-1;run.Model.SetStrategy(actor,PrototypeStrategy.Obey);}
                else if(intro.DummyPhase==3&&actor.Id==run.Model.Player.Id&&!actor.ManualOrder){intro.DummyHits++;if(intro.DummyHits>=3){intro.DummyPhase=4;run.Tactics.Cancel(new[]{actor.Squad});run.Model.SetStrategy(actor,PrototypeStrategy.Obey);intro.TutorialDelay=1;}}
            };
            run.PrepareTutorialSkills();run.TutorialSkillGate=()=>Next()==Skill;
            var targeting=run.Model.CanTarget;run.Model.CanTarget=(source,target)=>source.Side==PrototypeSide.Ally&&(intro.DrillFlags&Attack)!=0&&(targeting?.Invoke(source,target)??true);
            run.Model.CanMove=actor=>actor.Side==PrototypeSide.Ally||intro.DummyPhase==1;
            run.OutcomeResolver=current=>{
                if(intro.MoveOrdered&&current.Model.Player.DistanceTravelled-intro.DrillMoveOrigin>=.49){intro.DrillFlags|=Move;intro.MoveOrdered=false;intro.TutorialDelay=1;}
                return intro.DrillFlags==All&&current.Model.Hits>0?BattleOutcome.SideAWin:BattleOutcome.None;};
        }
        public static bool FinishPractice(BaseState s,bool completed)
        {if(!Active(s)||s.Demo.Intro.Step!=4||s.SceneBattleId!=BattleId)return false;s.SceneBattleId="";if(completed&&s.Demo.Intro.DrillFlags==All)s.Demo.Intro.Step=6;return true;}
        public static void Validate(BaseState s)
        {
            if(!Active(s))return;var i=s.Demo.Intro;
            if(i.DummyPhase<0||i.DummyPhase>4||i.DummyHits<0||i.DummyHits>3)throw new ArgumentException("木桩教学进度非法");
            if(i.Step<0||i.Step>10||i.Step>0&&!new[]{"male","female"}.Contains(i.Hero)||i.Step==0&&i.Hero!=""||i.DrillFlags<0||i.DrillFlags>All||i.FarmWorkers<2||i.FarmWorkers>4||double.IsNaN(i.Walked)||double.IsInfinity(i.Walked)||i.Walked<0||i.CaptainX<0||i.CaptainX>=24||i.CaptainY<0||i.CaptainY>=14)throw new ArgumentException("序章教学进度非法");
            if(new[]{i.ReporterX,i.ReporterY,i.DrillMoveOrigin,i.TutorialDelay,i.CaptainX,i.CaptainY,i.DepartureWait,i.MilitiaDelay}.Any(v=>double.IsNaN(v)||double.IsInfinity(v))||i.ReporterX<0||i.ReporterX>=24||i.ReporterY<0||i.ReporterY>=14||i.DrillMoveOrigin<0||i.TutorialDelay<0||i.TutorialDelay>1)throw new ArgumentException("报信人与演练位置非法");
            if(i.Militia==null||i.MilitiaSpawned<0||i.MilitiaSpawned>4||i.Militia.Count!=i.MilitiaSpawned||i.DepartureWait<0||i.DepartureWait>1.05||i.MilitiaDelay<-.06||i.MilitiaDelay>.4||i.Militia.Any(v=>v==null||new[]{v.MapX,v.MapY}.Any(n=>double.IsNaN(n)||double.IsInfinity(n))||v.MapX<0||v.MapX>=24||v.MapY<0||v.MapY>=14)||i.DeparturePhase<0||i.DeparturePhase>4||i.Militia.Select(v=>v.Id).Distinct().Count()!=i.Militia.Count)throw new ArgumentException("序章民兵离场记录非法");
            if(i.Step>0&&s.Leaders.Single(l=>l.Id==s.PlayerLeaderId).AppearanceId!=PlayerArt(s))throw new ArgumentException("主角选择与形象不一致");
        }
    }
}
