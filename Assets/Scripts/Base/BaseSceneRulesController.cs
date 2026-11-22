using System;
using System.Collections.Generic;
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
        private BattleBoard explorationProjection;
        private (TerrainKind Terrain, int Elevation, RampAxis Ramp, bool HasSightHeight, double SightHeight)[] explorationTerrain;
        private BaseMapObject[] explorationBlockers;
        private bool? explorationFog;
        private bool[] explorationKnown;
        private readonly HashSet<GridPosition> sightObservers = new HashSet<GridPosition>();
        private readonly HashSet<GridPosition> previousSightObservers = new HashSet<GridPosition>();
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
            if(combatController.Realtime.Battle.Phase!=BattlePhase.Fighting){combatController.SetPracticeCue(0,"");return;}
            if(combatController.DetailsVisible)return;
            BaseDemoIntro.AdvanceTutorialDelay(State.Demo.Intro,Time.unscaledDeltaTime);
            int step=BaseDemoIntro.NextExercise(State.Demo.Intro);
            if(step==0){combatController.SetPracticeCue(0,"");return;}
            bool attackingDummy = step == BaseDemoIntro.AutoAttack && State.Demo.Intro.DummyPhase == 3;
            int cue = attackingDummy ? -step : step;
            if(practiceShown==cue)return;practiceShown=cue;
            string text = step switch
            {
                BaseDemoIntro.Select => StoryText.Get("story.scene.prologue.drill_select", "先点左下角的头像，也可以点你自己。"),
                BaseDemoIntro.Pause => StoryText.Get("story.scene.prologue.drill_pause", "点右下角的暂停／继续，或按空格。"),
                BaseDemoIntro.Move => StoryText.Get("story.scene.prologue.drill_move", "【手动】时，你得自己下令。单击前方空地，暂停时按空格就会迈步。"),
                BaseDemoIntro.DummyDemo => StoryText.Get("story.scene.prologue.drill_dummy", "看好了，每次挥砍都有起手空当，算准距离才能刀刀见肉！"),
                BaseDemoIntro.AutoAttack when attackingDummy => "让队伍继续攻击木桩。战斗暂停时点【继续】。",
                BaseDemoIntro.AutoAttack => StoryText.Get("story.scene.prologue.drill_auto_attack", "点右下角的【进攻】，让队伍连续攻击木桩。战斗暂停时先继续。"),
                _ => StoryText.Get("story.scene.prologue.drill_attack", "最后一式，单击我发起进攻。暂停时记得按空格继续！")
            };
            combatController.SetPracticeCue(step,text);
            if(step==BaseDemoIntro.DummyDemo)
            {
                BaseDemoIntro.StartDummyDemo(combatController.Realtime,State.Demo.Intro);
                combatController.ResumeAfterDialogue(true);
            }
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
        private bool TickRuinsRescueDialogue()
        {
            if(combatController?.IsDemoRuins!=true)return false;
            var run=combatController.Realtime;
            if(combatController.ExternalDialogue)
            {
                var keyboard=UnityEngine.InputSystem.Keyboard.current;
                if(keyboard!=null&&(keyboard.eKey.wasPressedThisFrame||keyboard.spaceKey.wasPressedThisFrame))Hud.ContinueDialogue();
                return true;
            }
            if(run?.AwaitingRescueDialogue==true)
            {
                bool running=combatController.PauseForDialogue();
                combatController.ExternalDialogue=true;
                Hud.SetVisible(true);
                Hud.OpenRuinsRescue(()=>{
                    Hud.ClosePanel();Hud.SetVisible(false);
                    run.ContinueRuinsExit();
                    combatController.ExternalDialogue=false;combatController.ResumeAfterDialogue(running);
                });
                return true;
            }
            return false;
        }
        public void BeginAzhuDuel()
        {
            BaseCampaignSession encounter;
            try{encounter=BaseLocalDuel.Create(State);}catch(Exception e){ReportMessage(e.Message);return;}
            beforeAzhuDuel=State.Copy();SaveStore.Save(State);
            State.SceneBattleId=BaseLocalDuel.Id;
            try{BeginSceneEncounter(encounter,run=>{run.OutcomeResolver=r=>r.Model.Player.Health<=r.Model.Player.MaxHealth*.5?BattleOutcome.SideBWin:r.Model.Units.Any(u=>u.Side==RealtimePrototype.PrototypeSide.Enemy&&u.Health<=u.MaxHealth*.5)?BattleOutcome.SideAWin:BattleOutcome.None;},null,FinishAzhuDuel,"阿铸 · 点到为止（半血结束）");}
            catch(Exception e){State=beforeAzhuDuel;beforeAzhuDuel=null;FinishLocalScene(false);ReportMessage("比武未开始："+e.Message);}
        }
        private bool FinishAzhuDuel(RealtimeCampaignBattle run,bool abandon,bool mainMenu)
        {
            if(beforeAzhuDuel==null)return false;var next=beforeAzhuDuel.Copy();
            var outcome = abandon ? BattleOutcome.None : run?.Battle.Report?.Outcome ?? BattleOutcome.None;
            bool completed = BaseLocalDuel.RecordResult(next, outcome);
            BaseEconomy.Validate(next); SaveStore.Save(next); State = next; beforeAzhuDuel = null;
            FinishLocalScene(mainMenu, completed ? () => Hud.OpenAzhuAfterDuel(outcome == BattleOutcome.SideAWin) : () => Hud.OpenDemoPartner("partner_craft"));
            return true;
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
            var objects=MapObjects;
            BattleVision sight=combatController?.Realtime?.Battle.Vision;
            var memory=BaseSceneRules.Memory(State);
            if(sight==null)
            {
                sight=RefreshExplorationSight(objects,memory);
                regionView.RefreshVision(sight,BattleSide.A,false);
            }
            else memory.Known=sight.KnownTerrain(BattleSide.A);
            // Actors can cross the current visibility boundary without moving the observers.
            var scenery=regionView.Root.transform.Find("RegionLandmarks");
            if(scenery!=null)foreach(Transform node in scenery)
            {
                var obj=objects.FirstOrDefault(o=>node.name==o.Id||node.name.StartsWith(o.Id+" ·"));
                if(obj==null)continue;
                bool known=sight.TerrainVisible(BattleSide.A,new GridPosition((int)obj.X,(int)obj.Y));
                if(node.gameObject.activeSelf!=known)node.gameObject.SetActive(known);
            }
            string opponent=combatController?.IsDemoPractice==true?"demo_captain":combatController?.IsAzhuDuel==true?"demo_azhu":"";
            foreach(var obj in objects)
            {
                if (!npcViews.TryGetValue(obj.Id, out var npc) || npc == null) continue;
                bool visible=obj.Id!=opponent&&sight.Visible(BattleSide.A,new GridPosition((int)obj.X,(int)obj.Y));
                if(npc.gameObject.activeSelf!=visible)npc.gameObject.SetActive(visible);
            }
            if(force)Hud?.RefreshMovement();
        }
        private BattleVision RefreshExplorationSight(BaseMapObject[] objects,BaseTerrainMemory memory)
        {
            bool fog=BaseSceneRules.FogEnabled(State),demo=BaseDemo.Active(State);
            var board=BaseBattlefield.Board(State);
            // A legacy camp UI substitutes its warehouse marker, which is not part of the sight projection.
            if(fog&&!demo&&State.Campaign.Location=="base")objects=BaseSpatial.Objects(State);
            if(explorationSight==null||explorationFog!=fog||fog&&!ExplorationGeometryMatches(board,objects))
            {
                explorationFog=null;
                explorationProjection=fog?ProjectExploration(board,objects,demo):board;
                explorationSight=new BattleVision(explorationProjection);
                explorationKnown=null;
            }
            bool memoryChanged=memory.Known!=explorationKnown&&(explorationKnown==null||!memory.Known.SequenceEqual(explorationKnown));
            if(memoryChanged)explorationSight.RememberTerrain(BattleSide.A,memory.Known);
            sightObservers.Clear();
            if(fog)
            {
                sightObservers.Add(new GridPosition((int)State.PlayerX,(int)State.PlayerY));
                foreach(var agent in Companions.Agents)
                    if(agent.Visible&&BaseAdventure.Following(State,agent.Id))
                        sightObservers.Add(new GridPosition((int)agent.Position.X,(int)agent.Position.Y));
            }
            bool sightChanged=explorationFog!=fog||!previousSightObservers.SetEquals(sightObservers);
            if(sightChanged)
            {
                explorationSight.RefreshExploration(sightObservers,fog,State.Campaign.CurrentMapId==BaseDemoFinale.Camp);
                previousSightObservers.Clear();
                previousSightObservers.UnionWith(sightObservers);
                explorationFog=fog;
            }
            if(memoryChanged||sightChanged)memory.Known=explorationSight.KnownTerrain(BattleSide.A);
            explorationKnown=memory.Known;
            return explorationSight;
        }
        private static (TerrainKind Terrain,int Elevation,RampAxis Ramp,bool HasSightHeight,double SightHeight) SightTerrain(BoardCell cell)
            =>(cell.Terrain,cell.Elevation,cell.Ramp,cell.HasSightHeight,cell.HasSightHeight?cell.SightHeight:0);
        private bool ExplorationGeometryMatches(BattleBoard board,BaseMapObject[] objects)
        {
            if(board.Width!=explorationProjection.Width||board.Height!=explorationProjection.Height)return false;
            for(int i=0;i<board.Cells.Length;i++)if(SightTerrain(board.Cells[i])!=explorationTerrain[i])return false;
            int index=0;
            foreach(var obj in objects)
            {
                if(!BaseSceneCombat.BlocksSight(obj))continue;
                if(index>=explorationBlockers.Length)return false;
                var previous=explorationBlockers[index++];
                if(obj.Id!=previous.Id||obj.X!=previous.X||obj.Y!=previous.Y||obj.Width!=previous.Width||obj.Height!=previous.Height
                    ||obj.Shape!=previous.Shape||obj.Rotation!=previous.Rotation||obj.AssetId!=previous.AssetId||obj.Kind!=previous.Kind)return false;
            }
            return index==explorationBlockers.Length;
        }
        private BattleBoard ProjectExploration(BattleBoard board,BaseMapObject[] objects,bool demo)
        {
            explorationTerrain=board.Cells.Select(SightTerrain).ToArray();
            explorationBlockers=objects.Where(BaseSceneCombat.BlocksSight).ToArray();
            return BaseSceneCombat.Project(board,explorationBlockers,demo);
        }
        private void TickRescueJourney(double seconds)
        {
            if(!BaseSceneRules.Active(State)||State.Demo.Rescue?.Stage!=2)return;
            bool arrived=BaseSceneRules.TickRescued(State,seconds);
            if(BaseSceneRules.Entrance(State))
            {
                if(npcViews.TryGetValue("demo_rescued_1",out var first))first.MoveTo(WorldPoint(State.Demo.Rescue.FirstX,State.Demo.Rescue.FirstY));
                if(npcViews.TryGetValue("demo_rescued_2",out var second))second.MoveTo(WorldPoint(State.Demo.Rescue.SecondX,State.Demo.Rescue.SecondY));
            }
            if(arrived){SaveStore.Save(State);RebuildRegion();RebuildHud();PlacePlayer();}
        }
        public void ReturnRescuedVillagers(bool afterExtraction=false)
        {
            if(Change(s=>{if(!BaseSceneRules.TalkRescued(s,afterExtraction))throw new InvalidOperationException("请在遗迹入口与获救村民交谈");},"两名村民动身返回城镇"))Hud.ClosePanel();
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
