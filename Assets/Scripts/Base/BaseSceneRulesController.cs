using System;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.Story;
using UnityEngine;

namespace BaseConquest.Base
{
    public sealed partial class BaseSceneController
    {
        private BattleVision explorationSight;
        private double nextSightAt;
        private BaseState beforeAzhuDuel;
        private int practiceShown=-1;
        private void TickPracticeGuide()
        {
            if(combatController?.IsDemoPractice!=true||combatController.Realtime==null)return;
            if(combatController.ExternalDialogue)
            {
                var keyboard=UnityEngine.InputSystem.Keyboard.current;
                if(keyboard!=null&&(keyboard.eKey.wasPressedThisFrame||keyboard.spaceKey.wasPressedThisFrame))Hud.ContinueDialogue();
                return;
            }
            if(combatController.Realtime.Battle.Phase!=BattlePhase.Fighting||combatController.DetailsVisible)return;
            BaseDemoIntro.AdvanceTutorialDelay(State.Demo.Intro,Time.unscaledDeltaTime);
            int step=BaseDemoIntro.NextExercise(State.Demo.Intro);if(step==0||practiceShown==step)return;practiceShown=step;
            string text = step switch
            {
                BaseDemoIntro.Select => StoryText.Get("story.scene.prologue.drill_select", "先点左下角的头像，也可以点你自己。"),
                BaseDemoIntro.Pause => StoryText.Get("story.scene.prologue.drill_pause", "点右上角的【{toggle}】，也可以按空格，练习暂停和继续。")
                    .Replace("{toggle}", combatController.Automatic ? "暂停" : "继续"),
                BaseDemoIntro.Move => StoryText.Get("story.scene.prologue.drill_move", "【手动】时，你得自己下令。单击前方空地，暂停时按空格就会迈步。"),
                BaseDemoIntro.Guard => StoryText.Get("story.scene.prologue.drill_guard", "双击身旁的空地，下令【驻守】。"),
                BaseDemoIntro.Skill => StoryText.Get("story.scene.prologue.drill_skill", "点左下角头像下方的【{skill}】，把气势提起来！")
                    .Replace("{skill}", combatController.Realtime.SkillName(BattleSide.A, 0)),
                BaseDemoIntro.Inspect => StoryText.Get("story.scene.prologue.drill_inspect", "长按左下角的头像，在【状态效果】里看技能能维持多久。"),
                BaseDemoIntro.DummyDemo => StoryText.Get("story.scene.prologue.drill_dummy", "看好了，每次挥砍都有起手空当，算准距离才能刀刀见肉！"),
                BaseDemoIntro.AutoAttack => StoryText.Get("story.scene.prologue.drill_auto_attack", "长按左下角的头像，在详情里选【进攻】。关闭详情后按空格，连劈木桩三剑！"),
                _ => StoryText.Get("story.scene.prologue.drill_attack", "最后一式，单击我发起进攻。暂停时记得按空格继续！")
            };
            bool running=combatController.PauseForDialogue();combatController.ExternalDialogue=true;Hud.SetVisible(true);
            Hud.OpenPracticeInstruction(text,()=>{Hud.ClosePanel();Hud.SetVisible(false);if(combatController!=null){combatController.ExternalDialogue=false;if(step==BaseDemoIntro.DummyDemo)BaseDemoIntro.StartDummyDemo(combatController.Realtime,State.Demo.Intro);combatController.ResumeAfterDialogue(step==BaseDemoIntro.DummyDemo||step==BaseDemoIntro.AutoAttack?true:step==BaseDemoIntro.Inspect?false:running);}});
        }
        private void TickAzhuInvitation()
        {
            if(Hud.PanelOpen||ConstructionMode||BaseBattleGateway.MainMenu||developerPanel?.BlocksGameInput==true)return;
            if(!BaseDemoNpcs.CanInviteAzhu(State))return;
            if(Change(s=>{if(!BaseDemoNpcs.InviteAzhu(s))throw new InvalidOperationException("阿铸邀约条件已变化");},""))Hud.OpenAzhuInvitation();
        }
        private void TickDefenseIntel()
        {
            if(combatController?.Realtime?.Session.Opening.EventId!=BaseDemoDefense.EventId)return;
            if(combatController.ExternalDialogue){var keyboard=UnityEngine.InputSystem.Keyboard.current;if(keyboard!=null&&(keyboard.eKey.wasPressedThisFrame||keyboard.spaceKey.wasPressedThisFrame))Hud.ContinueDialogue();return;}
            var run=combatController.Realtime;if(Hud.PanelOpen||run.Battle.Phase!=BattlePhase.Fighting||State.Demo.Defense.BattleIntelSeen)return;
            var survivors=run.Model.Units.Where(unit=>unit.Side==BaseConquest.RealtimePrototype.PrototypeSide.Enemy&&unit.Alive).ToArray();
            if(survivors.Length!=1||survivors[0].Role==BaseConquest.RealtimePrototype.PrototypeRole.Leader)return;
            bool running=combatController.PauseForDialogue();combatController.ExternalDialogue=true;Hud.SetVisible(true);
            Hud.OpenDefenseIntel(()=>{State.Demo.Defense.BattleIntelSeen=true;run.SaveCheckpoint(State);SaveStore.Save(State);Hud.ClosePanel();Hud.SetVisible(false);if(combatController!=null){combatController.ExternalDialogue=false;combatController.ResumeAfterDialogue(running);}});
        }
        private string ruinsRetreatGuideId;
        private bool TickRuinsRetreatGuide()
        {
            if(combatController?.IsDemoRuins!=true || combatController.ExternalDialogue)return false;
            var run=combatController.Realtime;
            if(Hud.PanelOpen||run==null||ruinsRetreatGuideId==run.Session.Opening.Id||!BaseDemoRuins.SoloRetreatGuide(run))return false;
            ruinsRetreatGuideId=run.Session.Opening.Id;
            combatController.PauseForDialogue();
            return combatController.WithdrawSceneEncounter();
        }
        public void BeginAzhuDuel()=>BeginAzhuDuelAt(false);
        public void BeginAzhuFieldDuel()=>BeginAzhuDuelAt(true);
        private void BeginAzhuDuelAt(bool battlefield)
        {
            BaseCampaignSession encounter;
            try{encounter=BaseLocalDuel.Create(State,battlefield);}catch(Exception e){ReportMessage(e.Message);return;}
            beforeAzhuDuel=State.Copy();SaveStore.Save(State);
            if(battlefield)
            {
                var field=BaseSpatial.Layout(State,"perimeter");State.Campaign.Location="perimeter";if(BaseSceneInstances.Active(State))State.Campaign.CurrentMapId=field.SourceMapId;
                var start=encounter.Battle.Commander(BattleSide.A,0).Cell;State.PlayerX=start.X+.25;State.PlayerY=start.Z+.25;BaseAdventure.Arrive(State);RefreshDemoRegion();
            }
            State.SceneBattleId=BaseLocalDuel.Id;
            try{BeginSceneEncounter(encounter,run=>{run.OutcomeResolver=r=>r.Model.Player.Health<=r.Model.Player.MaxHealth*.5?BattleOutcome.SideBWin:r.Model.Units.Any(u=>u.Side==RealtimePrototype.PrototypeSide.Enemy&&u.Health<=u.MaxHealth*.5)?BattleOutcome.SideAWin:BattleOutcome.None;},null,FinishAzhuDuel,"阿铸 · 点到为止（半血结束）");}
            catch(Exception e){State=beforeAzhuDuel;beforeAzhuDuel=null;FinishLocalScene(false);ReportMessage("比武未开始："+e.Message);}
        }
        private bool FinishAzhuDuel(RealtimeCampaignBattle run,bool abandon,bool mainMenu)
        {
            if(beforeAzhuDuel==null)return false;var next=beforeAzhuDuel.Copy();
            bool won=!abandon&&run?.Battle.Report?.Outcome==BattleOutcome.SideAWin;
            if(won){next.Demo.Flow.AzhuDuelWon=true;if(!next.Demo.MetCompanions.Contains("partner_craft"))BaseDemoRuins.Meet(next,"partner_craft");}
            if(!abandon&&run?.Battle.Phase==BattlePhase.Finished)BaseSceneRules.RememberStrategy(next,run.Session);BaseEconomy.Validate(next);SaveStore.Save(next);State=next;beforeAzhuDuel=null;
            FinishLocalScene(mainMenu,()=>Hud.OpenAzhuAfterDuel(won));return true;
        }
        public bool SceneCellKnown(GridPosition p)=>(combatController?.Realtime?.Battle.Vision??explorationSight)?.TerrainVisible(BattleSide.A,p)??true;
        public bool SceneObjectVisible(BaseMapObject o)
        {
            if(o==null)return false;
            var cell=new GridPosition((int)o.X,(int)o.Y);
            if(o.Id==BaseBanditHideout.EntryId)return SceneCellKnown(cell);
            var sight=combatController?.Realtime?.Battle.Vision??explorationSight;
            return sight==null||sight.Visible(BattleSide.A,cell);
        }
        private void RefreshSceneSight(bool force=false)
        {
            if(force)InvalidateObjects();
            if(!BaseSceneRules.Active(State)||BaseWorldJourney.OnWorld(State))return;
            if(!force&&Time.unscaledTimeAsDouble<nextSightAt)return;nextSightAt=Time.unscaledTimeAsDouble+.1;
            BattleVision sight=combatController?.Realtime?.Battle.Vision;
            if(sight==null)
            {
                if(explorationSight==null){explorationSight=new BattleVision(BaseSceneCombat.Project(State));explorationSight.RememberTerrain(BattleSide.A,BaseSceneRules.Memory(State).Known);}
                var points=new[]{new GridPosition((int)State.PlayerX,(int)State.PlayerY)}.Concat(Companions.Agents.Where(a=>a.Visible&&BaseAdventure.Following(State,a.Id)).Select(a=>new GridPosition((int)a.Position.X,(int)a.Position.Y)));
                explorationSight.RefreshExploration(points,BaseSceneRules.FogEnabled(State));sight=explorationSight;regionView.RefreshVision(sight,BattleSide.A,false);
            }
            BaseSceneRules.Memory(State).Known=sight.KnownTerrain(BattleSide.A);
            var objects=MapObjects;var scenery=regionView.Root.transform.Find("RegionLandmarks");
            if(scenery!=null)foreach(Transform node in scenery)
            {
                var obj=objects.FirstOrDefault(o=>node.name==o.Id||node.name.StartsWith(o.Id+" ·"));
                if(obj!=null)node.gameObject.SetActive(sight.TerrainVisible(BattleSide.A,new GridPosition((int)obj.X,(int)obj.Y)));
            }
            string opponent=combatController?.IsDemoPractice==true?"demo_captain":combatController?.IsAzhuDuel==true?"demo_azhu":"";
            foreach(var npc in npcViews)
            {
                var obj=objects.FirstOrDefault(o=>npc!=null&&npc.name.StartsWith(o.Id+" ·"));if(obj==null)continue;
                bool visible=obj.Id!=opponent&&sight.Visible(BattleSide.A,new GridPosition((int)obj.X,(int)obj.Y));npc.gameObject.SetActive(visible);
            }
            if(force)Hud?.RefreshMovement();
        }
        private void TickRescueJourney(double seconds)
        {
            if(!BaseSceneRules.Active(State)||State.Demo.Rescue?.Stage!=2)return;
            bool arrived=BaseSceneRules.TickRescued(State,seconds);
            if(BaseSceneRules.Entrance(State))foreach(var npc in npcViews)
            {
                if(npc.name.StartsWith("demo_rescued_1 ·"))npc.MoveTo(WorldPoint(State.Demo.Rescue.FirstX,State.Demo.Rescue.FirstY));
                if(npc.name.StartsWith("demo_rescued_2 ·"))npc.MoveTo(WorldPoint(State.Demo.Rescue.SecondX,State.Demo.Rescue.SecondY));
            }
            if(arrived){SaveStore.Save(State);RebuildRegion();RebuildHud();PlacePlayer();}
        }
        public void ReturnRescuedVillagers()
        {
            if(Change(s=>{if(!BaseSceneRules.TalkRescued(s))throw new InvalidOperationException("请在遗迹入口与获救村民交谈");},"两名村民动身返回城镇"))Hud.ClosePanel();
        }
        private void FinishLocalScene(bool mainMenu,Action after=null)
        {
            SetRegionReady(false);BaseBattleGateway.MainMenu=mainMenu;BaseBattleGateway.RequestedId=null;
            var previous=combatController;combatController=null;
            if(previous!=null){previous.gameObject.SetActive(false);BattleBoardView.DestroyObject(previous.gameObject);}
            MapSession.LeaveBattle();RefreshDemoRegion();SetRegionReady(true);if(mainMenu)Hud.OpenMenu();else after?.Invoke();
        }
        private void HideSceneParticipants(BaseCampaignSession session)
        {
            if(!BaseSceneRules.Active(State)){explorationActors.gameObject.SetActive(false);return;}
            player.gameObject.SetActive(false);
            foreach(var pair in companionViews)if(session.Opening.Leaders.Any(l=>l.Id==pair.Key))pair.Value.gameObject.SetActive(false);
        }
    }
}
