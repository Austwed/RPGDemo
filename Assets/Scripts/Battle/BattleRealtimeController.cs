using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Base;
using BaseConquest.RealtimePrototype;
using BaseConquest.Story;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BaseConquest.Battle
{
    public sealed partial class BattleSceneController
    {
        private RealtimeCampaignBattle realtime;
        private VillageOpeningAudio battleAudio;
        private bool realtimeSingleCommander;
        private int informationFineCell=-1,lastRealtimeTap=int.MinValue;
        private double lastRealtimeTapTime=-10;
        private bool lastRealtimeGround;
        private int practiceGroundCell = -1;
        internal int PracticeStep { get; private set; }
        internal string PracticeText { get; private set; } = "";

        internal void SetPracticeCue(int step, string instruction)
        {
            if (step == PracticeStep && PracticeText == instruction) return;
            if (step != PracticeStep) practiceGroundCell = -1;
            PracticeStep = step;
            PracticeText = instruction;
            hud?.Refresh();
        }

        internal bool TryPracticeMapTarget(int step, out Vector2 screen)
        {
            screen = default;
            var model = realtime?.Model;
            var actor = model?.Player;
            if (actor == null || boardView == null) return false;
            PrototypePoint point;
            float height = .22f;
            if (step == BaseDemoIntro.Attack)
            {
                var opponent = model.Units.FirstOrDefault(u => u.Alive && u.Side == PrototypeSide.Enemy);
                if (opponent == null) return false;
                point = opponent.BodyCenter;
                height = .65f;
            }
            else if (step == BaseDemoIntro.DummyDemo || step == BaseDemoIntro.AutoAttack)
            {
                var dummy = realtime.Tactics.Objects.FirstOrDefault(o => o.Id == BaseDemoIntro.DummyId && !o.Fallen);
                if (dummy == null) return false;
                point = new PrototypePoint(dummy.X + .5, dummy.Y + .5);
                height = .58f;
            }
            else if (step == BaseDemoIntro.Move)
            {
                var arena = model.Arena;
                if (practiceGroundCell < 0)
                {
                    var distances = arena.Distances(actor.Cell, true);
                    practiceGroundCell = Enumerable.Range(0, distances.Length)
                        .Where(i => distances[i] >= .75 && distances[i] <= 3 && arena.FineWalkable(i) &&
                            !model.Units.Any(u => u.Alive && u.Contains(arena.Center(i))) && realtime.Tactics.At(arena.Center(i)) == null)
                        .OrderBy(i => Math.Abs(distances[i] - 1.5)).ThenBy(i => i).DefaultIfEmpty(-1).First();
                }
                if (practiceGroundCell < 0) return false;
                point = arena.Center(practiceGroundCell);
            }
            else return false;
            screen = boardView.Camera.WorldToScreenPoint(RealtimeWorld(point) + Vector3.up * height);
            return true;
        }
        public string RealtimeFineInformation=>informationIsCell&&informationFineCell>=0&&realtime!=null?"细格 ("+(informationFineCell%realtime.Model.Arena.FineColumns)+", "+(informationFineCell/realtime.Model.Arena.FineColumns)+")":"";
        private readonly Dictionary<int,LineRenderer> realtimeArrows=new Dictionary<int,LineRenderer>();
        public bool IsRealtimeCampaign=>campaignSession?.Opening.RealtimeRules==true;
        public RealtimeCampaignBattle Realtime=>realtime;
        public string RealtimeRangeDescription=>rangeOverlay?.Description??"";
        public double RealtimeRange(BattleSoldier unit)=>RealtimeCampaignBattle.RangeCells(campaignSession,unit);
        public double RealtimeSpeed(BattleSoldier unit)
        {var actor=realtime?.Model.Units.FirstOrDefault(a=>a.Id==unit.Id+1);return actor==null?RealtimeCampaignBattle.SpeedCells(campaignSession,unit):realtime.Model.MovementSpeed(actor)*2;}
        public double RealtimeAttack(BattleSoldier unit)
        {var actor=realtime?.Model.Units.FirstOrDefault(a=>a.Id==unit.Id+1);return actor==null?unit.Stats.Damage*unit.DrinkMultiplier:realtime.Model.DamageFor(actor);}
        public string RealtimeStatus=>"作战 "+(realtime?.Model.Elapsed??0).ToString("0")+"秒 · "+(autoRun?"进行中":"暂停");
        public void PrepareCampaignPreview(BaseCampaignSession session)
        {
            campaignSession=session;campaignBattleId=session.Opening.Id;suppressInitialDeploy=true;Initialize();
            if(IsRealtimeCampaign&&battle.Phase!=BattlePhase.Deployment)AttachRealtime();hud?.Refresh();
        }
        private void AttachRealtime()
        {
            realtime=new RealtimeCampaignBattle(campaignSession);autoRun=false;animating=skipping=false;presentation=1;
            if(IsSceneEncounter)configureSceneEncounter?.Invoke(realtime);
            if (Application.isPlaying && (IsSceneEncounter || sharedMapSession != null) && (campaignSession.Opening.DemoCombat || campaignSession.Opening.EventId == BaseDemoPrologue.BattleId) && !DeploymentDraftActive)
            {
                battleAudio = VillageOpeningAudio.Shared();
                battleAudio.BindBattle(realtime);
            }
            if(realtime.Model.Player!=null)boardView.CenterOn(battle.Soldier(realtime.Model.Player.Id-1).Cell);
            feedback="即时战斗已暂停：点选细格查看；选队长后单击移动／攻击，双击空地驻守／敌人集火；空格继续。";
            SelectOpeningCommanders();
            SyncRealtimeViews();
        }

        private void SelectOpeningCommanders()
        {
            var chosen = realtime.Model.Units.Where(unit => !IsDemoPractice && unit.Alive && unit.Side == PrototypeSide.Ally &&
                unit.Role == PrototypeRole.Leader && unit.Strategy == PrototypeStrategy.Obey && battle.SelectableCommander(battle.Soldier(unit.Id - 1)))
                .OrderBy(unit => unit.Squad).ToArray();
            selection.Set(chosen.Select(unit => unit.Id - 1));
            selectedUnitId = selection.Primary;
            realtimeSingleCommander = true;
            deploymentSlotChosen = chosen.Length > 0;
            deploymentCommanderSlot = deploymentSlotChosen ? chosen[0].Squad : 0;
        }
        private bool RealtimeOrder(PrototypeOrder order,PrototypePoint point,int target=-1,bool squadFocus=false)
        {
            if(realtime==null)return false;
            if(order==PrototypeOrder.Auto)
            {var actor=realtime.Model.Units.FirstOrDefault(unit=>unit.Alive&&selection.Contains(unit.Id-1));if(actor==null)return false;point=actor.Position;}
            bool accepted=realtime.Issue(selection.Ids,order,point,target,true,squadFocus);
            bool groupPlacement = selection.Ids.Count > 1 && (order == PrototypeOrder.Move || order == PrototypeOrder.Guard);
            if (accepted)
            {
                string orderName = order switch
                {
                    PrototypeOrder.Move => "移动",
                    PrototypeOrder.Guard => "驻守",
                    PrototypeOrder.Focus => squadFocus ? "集火" : "攻击",
                    PrototypeOrder.Retreat => "撤退",
                    _ => "恢复自主"
                };
                feedback = "已下达" + orderName + "指令" + (autoRun ? "" : " · 暂停中");
            }
            else
            {
                feedback = groupPlacement
                    ? StoryText.Get("story.ui.battle.group_no_space", "队长落点不足，原指令保留。")
                    : "未下令：请选择可行动队长／小队，确认目标可见、落点可达";
            }
            SyncRealtimeViews();hud?.Refresh();return accepted;
        }
        public bool RealtimeMove(PrototypePoint point)=>RealtimeOrder(PrototypeOrder.Move,point);
        internal IEnumerable<PrototypeStrategy> SelectedRealtimeStrategies => realtime?.Model.Units
            .Where(u => u.Alive && u.Side == PrototypeSide.Ally && u.Role == PrototypeRole.Leader && selection.Contains(u.Id - 1))
            .Select(u => u.Strategy) ?? Enumerable.Empty<PrototypeStrategy>();

        internal void ChooseRealtimeStrategy(SquadStrategy strategy)
        {
            if (campaignMenu != null || DetailsVisible || ExternalDialogue || realtime?.ChooseStrategy(selection.Ids, strategy) != true) return;
            SyncRealtimeViews();
            hud?.Refresh();
        }
        public void RealtimeGuardSelection()
        {
            var actor=realtime?.Model.Units.FirstOrDefault(unit=>selection.Contains(unit.Id-1)&&unit.Alive);
            if(actor!=null)RealtimeOrder(PrototypeOrder.Guard,actor.Position);
        }
        public void RealtimeRetreat()
        {if(realtime==null)return;ManualPause();if(realtime.DefensePursuitReady)feedback=realtime.FinishDefensePursuit()?"已结束追击，存活敌军撤离":"无法结算";else feedback=realtime.RetreatAll()?(realtime.Session.Opening.DemoCombat?"已立即撤退；返回后结算真实战损与军需":"全军向真实撤离口移动；抵达后才算成功撤离"):"暂无可撤离单位";SyncRealtimeViews();hud?.Refresh();}
        public bool PauseForDialogue(){bool running=autoRun;autoRun=false;realtime?.Model.SetPaused(true);return running;}
        public void ResumeAfterDialogue(bool running){autoRun=running;realtime?.Model.SetPaused(!running || DetailsVisible || campaignMenu != null);}
        public void ToggleRealtimePause()
        {
            if(realtime==null||battle.Phase!=BattlePhase.Fighting||campaignMenu!=null||DetailsVisible||ExternalDialogue||hud?.ReportVisible==true)return;
            realtime.TutorialAction?.Invoke(BaseDemoIntro.Pause);autoRun=!autoRun;realtime.Model.SetPaused(!autoRun);feedback=autoRun?"即时战斗进行中":"已暂停，可以下令";hud?.Refresh();
        }
        private void SelectRealtimeCommander(int id,bool alone,bool toggle = false)
        {
            var commander=battle.Soldier(id);if(commander?.Kind!=SoldierKind.Hero||!commander.IsActive||commander.Side!=BattleSide.A)return;
            if (manualFieldEncounter && toggle)
            {
                lastRealtimeTap = int.MinValue;
                lastRealtimeGround = false;
                if (selection.Contains(id))
                {
                    selection.ToggleUnit(id, true);
                    deploymentSlotChosen = HasSelection;
                    if (!informationIsCell) informationUnitId = selection.Primary;
                    feedback = HasSelection ? "已取消该队长的选择。" : "点击格子查看战况。点击头像或按 1–3 选择指挥队长。";
                    SyncRealtimeViews(); hud?.Refresh(); return;
                }
            }
            realtime.TutorialAction?.Invoke(BaseDemoIntro.Select);selection.Set(new[]{id});selectedUnitId=informationUnitId=id;realtimeSingleCommander=alone;
            deploymentCommanderSlot=commander.OriginCommanderSlot;deploymentSlotChosen=true;informationIsCell=inspectionTracksUnit=false;informationCell=null;boardView.SelectCell(null);ShowInformationNotice();
            informationFineCell=-1;feedback="指挥队长：单击移动／攻击，双击驻守／队伍集火；士兵按策略护卫";SyncRealtimeViews();hud?.Refresh();
        }
        public void ToggleRealtimeAutoSkill(int slot)
        {if(realtime?.SetAutoSkill(slot,!realtime.AutoSkill(BattleSide.A,slot))==true){feedback="已更新自动技能";hud?.Refresh();}}
        private int realtimeCandidate=-1;
        public bool RealtimePointer(Vector2 screen,bool secondary=false,bool single=false,bool holdGuard=false)
        {
            if(realtime==null||battle.Phase!=BattlePhase.Fighting||ExternalDialogue||developerPanel?.BlocksPointer(screen)==true||campaignMenu!=null||DetailsVisible||hud?.ReportVisible==true||hud?.BlocksPointer(screen)==true||!boardView.Camera.pixelRect.Contains(screen))return false;
            if(!ResolvePointer(screen,out var cell))return false;
            var ray=boardView.Camera.ScreenPointToRay(screen);var plane=new Plane(Vector3.up,Vector3.zero);
            if(!plane.Raycast(ray,out float distance))return false;
            var world=ray.GetPoint(distance);var point=new PrototypePoint(world.x/BattleBoardView.CellSize+battle.Board.Width*.5,world.z/BattleBoardView.CellSize+battle.Board.Height*.5);
            int fine=realtime.Model.Arena.CellAt(point);if(fine<0)return false;
            if(realtime.OrderRuinsExit(point,selection.Ids))
            {
                feedback=realtime.EscortWithoutCaptain?"幸存士兵正前往撤离点":"队长正前往撤离点";
                hud?.Refresh();return true;
            }
            if (holdGuard)
            {
                if (!HasSelection || !realtime.Model.Arena.CanStand(point) || realtime.Tactics.At(point) != null || RealtimeFineUnits(fine).Any(u => u.IsActive)) return false;
                lastRealtimeTap = int.MinValue;
                lastRealtimeGround = false;
                InspectRealtimeFine(point, showNotice: false);
                return MarkRealtimeGuard(point);
            }
            if (manualFieldEncounter && !HasSelection)
            {
                InspectRealtimeFine(point);
                feedback = "查看格内可见单位与地形。";
                hud?.Refresh();
                return true;
            }
            var nearby=realtime.Model.Units.Where(a=>!a.Escaped&&(a.Alive||a.KnockedDown)).Select(a=>new {Actor=a,Unit=battle.Soldier(a.Id-1),Screen=(Vector2)boardView.Camera.WorldToScreenPoint(RealtimeWorld(a.BodyCenter)+boardView.Camera.transform.up*.55f)}).Where(a=>battle.CanInspectUnit(selectedSide,a.Unit)).Select(a=>new{a.Unit,Distance=Vector2.Distance(screen,a.Screen)}).Where(a=>a.Distance<=26).OrderBy(a=>a.Distance).ToArray();
            var picked=realtimeCandidate>=0?battle.Soldier(realtimeCandidate):nearby.FirstOrDefault()?.Unit??RealtimeFineUnits(fine).OrderBy(u=>u.IsActive?0:1).FirstOrDefault();
            if(realtimeCandidate<0&&!secondary&&nearby.Length>1&&nearby[1].Distance-nearby[0].Distance<8){hud.ShowRealtimeCandidates(screen,nearby.Select(a=>a.Unit).ToList(),unit=>{hud.CloseMenu();realtimeCandidate=unit.Id;RealtimePointer(screen,secondary,single);});return true;}
            realtimeCandidate=-1;
            int tapKey=picked?.Side==BattleSide.B? -picked.Id-2:fine;
            bool twice=!secondary&&(tapKey==lastRealtimeTap||lastRealtimeGround&&fine==lastRealtimeTap)&&Time.unscaledTimeAsDouble-lastRealtimeTapTime<=.32;
            bool guardDouble=twice&&lastRealtimeGround;
            lastRealtimeTap=twice?int.MinValue:tapKey;lastRealtimeTapTime=Time.unscaledTimeAsDouble;
            lastRealtimeGround=!twice&&picked==null&&realtime.Tactics.At(point)==null;
            if(!secondary&&picked?.Side==BattleSide.A&&!guardDouble)
            {
                var commander=picked.Kind==SoldierKind.Hero ? picked : battle.Commander(BattleSide.A,picked.OriginCommanderSlot);
                if(commander!=null)SelectRealtimeCommander(commander.Id,single&&picked.Kind==SoldierKind.Hero);
                InspectRealtimeFine(point);hud?.Refresh();return true;
            }
            // A ground command must not open a card under the pointer between the two presses.
            InspectRealtimeFine(point, showNotice: !guardDouble && (!HasSelection || picked != null || realtime.Tactics.At(point) != null));
            if(!HasSelection){feedback="查看格内全部可见单位与地形";hud?.Refresh();return true;}
            if(guardDouble){MarkRealtimeGuard(point);return true;}
            if(picked?.Side==BattleSide.B&&picked.IsActive){RealtimeOrder(PrototypeOrder.Focus,point,picked.Id,twice);return true;}
            if(picked?.Side==BattleSide.B){feedback="查看倒地单位；当前目标无法被攻击";hud?.Refresh();return true;}
            if(OrderTacticalObject(point))return true;
            if(twice)MarkRealtimeGuard(point);else RealtimeOrder(PrototypeOrder.Move,point);return true;
        }

        private bool MarkRealtimeGuard(PrototypePoint point)
        {
            if (!RealtimeOrder(PrototypeOrder.Guard, point)) return false;
            var first = realtime.Model.Units.Where(unit => unit.Alive && unit.Side == PrototypeSide.Ally &&
                unit.Role == PrototypeRole.Leader && selection.Contains(unit.Id - 1)).OrderBy(unit => unit.Squad).FirstOrDefault();
            var arena = realtime.Model.Arena;
            int fine = arena.CellAt(first?.Anchor ?? point);
            boardView.SelectFineCell(fine % arena.FineColumns, fine / arena.FineColumns, guard: true);
            return true;
        }
        private List<BattleSoldier> RealtimeFineUnits(int fine)
        {
            if(realtime==null)return new List<BattleSoldier>();var p=realtime.Model.Arena.Center(fine);
            return realtime.Model.Units.Where(a=>!a.Escaped&&(a.Alive||a.KnockedDown)&&
                (a.FootprintLength==2?a.Contains(p):realtime.Model.Arena.CellAt(a.Position)==fine))
                .Select(a=>battle.Soldier(a.Id-1)).Where(s=>battle.CanInspectUnit(selectedSide,s)).OrderBy(s=>s.Id).ToList();
        }
        public void InspectRealtimeFine(PrototypePoint point, bool showNotice = true)
        {
            if(realtime==null)return;int fine=realtime.Model.Arena.CellAt(point);if(fine<0)return;
            informationFineCell=fine;var cell=new GridPosition(fine%realtime.Model.Arena.FineColumns/2,fine/realtime.Model.Arena.FineColumns/2);
            boardView.SelectFineCell(fine%realtime.Model.Arena.FineColumns,fine/realtime.Model.Arena.FineColumns);informationCell=cell;informationIsCell=true;inspectionTracksUnit=false;
            informationUnitId=RealtimeFineUnits(fine).FirstOrDefault()?.Id??-1;
            if(showNotice)ShowInformationNotice();
        }
        private void InspectRealtimeCell(GridPosition cell)
        {
            boardView.SelectCell(cell);informationCell=cell;informationIsCell=true;inspectionTracksUnit=false;
            informationUnitId=VisibleCellUnits(cell).FirstOrDefault()?.Id??-1;ShowInformationNotice();
        }
        public void AdvanceRealtime(double seconds)
        {
            AdvanceRealtimeModel(seconds);SyncRealtimeViews();hud?.Refresh();
        }
        private void AdvanceRealtimeModel(double seconds)
        {
            if(realtime==null)return;
            if(developerPanel?.BlocksGameInput==true||campaignMenu!=null||hud?.ReportVisible==true)ManualPause();
            realtime.Model.SetPaused(!autoRun || DetailsVisible || ExternalDialogue);
            double before=realtime.Model.Elapsed;realtime.Tick(seconds);
            if(IsSceneEncounter&&realtime.Model.Elapsed>before)advanceSceneFollowers?.Invoke(realtime.Model.Elapsed-before);
        }
        private void UpdateRealtime()
        {
            boardView.UpdateCamera(Mathf.Min(.21f,132f/1280),.08f);
            if(developerPanel?.BlocksGameInput==true){MapPanUp();ManualPause();SyncRealtimeViews();PersistCampaignResult();hud?.Refresh();return;}
            var keyboard=Keyboard.current;
            if(campaignMenu!=null){MapPanUp();ManualPause();hud?.Refresh();return;}
            if(keyboard!=null)
            {
                if(keyboard.escapeKey.wasPressedThisFrame){if(DetailsVisible)CloseCommanderDetails();else if(hud.ReportVisible)hud.CloseReport();else OpenCampaignMenu();return;}
                if(DetailsVisible){AdvanceRealtimeModel(0);hud?.Refresh();return;}
                if(keyboard.spaceKey.wasPressedThisFrame)HandleSpaceInput();
                if(keyboard.digit1Key.wasPressedThisFrame)SelectCommanderSlot(0);
                if(keyboard.digit2Key.wasPressedThisFrame)SelectCommanderSlot(1);
                if(keyboard.digit3Key.wasPressedThisFrame)SelectCommanderSlot(2);
                if(keyboard.hKey.wasPressedThisFrame)RealtimeGuardSelection();
                if(keyboard.bKey.wasPressedThisFrame){ManualPause();OpenBattleBackpack();return;}
                if(keyboard.nKey.wasPressedThisFrame){ManualPause();OpenBattleQuests();return;}
            }
            bool cameraGestureConsumed=ReadMapCameraInput(Time.unscaledDeltaTime);
            if(!cameraGestureConsumed&&!DetailsVisible&&hud?.ReportVisible!=true&&!ReadTouchPointer())
            {
                var mouse=Mouse.current;
                if(mouse?.leftButton.wasPressedThisFrame==true)RealtimePointer(mouse.position.ReadValue(),false,keyboard?.leftShiftKey.isPressed==true);
                if(mouse?.rightButton.wasPressedThisFrame==true)RealtimePointer(mouse.position.ReadValue(),true,keyboard?.leftShiftKey.isPressed==true);
            }
            if (gestureActive && gestureId >= 0 && !gestureDragging && !gestureLong && Time.unscaledTime - gestureStartTime >= .5f)
                gestureLong = RealtimePointer(gestureCurrent, holdGuard: true);
            int iterations=fastMode?3:1;for(int index=0;index<iterations;index++)AdvanceRealtimeModel(Math.Min(.25,Time.unscaledDeltaTime));
            SyncRealtimeViews();
            UpdatePortraitHold();PersistCampaignResult();PersistCampaignCheckpoint();hud?.Refresh();
        }
        private Vector3 RealtimeWorld(PrototypePoint point)=>new Vector3(((float)point.X-battle.Board.Width*.5f)*BattleBoardView.CellSize,0,((float)point.Z-battle.Board.Height*.5f)*BattleBoardView.CellSize);
        private readonly List<MageSpellVisual> spellFrame = new();
        private readonly List<SpriteRenderer> spellSprites = new();

        private void SyncSpells()
        {
            MageSpellVisual.Collect(realtime, BattleSide.A, spellFrame);
            for (int i = 0; i < spellFrame.Count; i++)
            {
                if (i == spellSprites.Count)
                {
                    var node = new GameObject("MageSpell");
                    node.transform.SetParent(unitsRoot, false);
                    node.transform.rotation = Quaternion.Euler(90, 0, 0);
                    spellSprites.Add(node.AddComponent<SpriteRenderer>());
                }
                var fx = spellFrame[i];
                var sprite = spellSprites[i];
                sprite.enabled = true;
                sprite.sprite = MageSpellVisual.Art(fx.impact);
                sprite.color = new Color(1, 1, 1, fx.opacity);
                sprite.transform.position = RealtimeWorld(new PrototypePoint(fx.x, fx.z)) + new Vector3(0, .54f, .36f);
                sprite.transform.localScale = Vector3.one * (2 * fx.radius * BattleBoardView.CellSize);
            }
            for (int i = spellFrame.Count; i < spellSprites.Count; i++) spellSprites[i].enabled = false;
        }
        private BattleCombatTextView combatText;
        private void SyncRealtimeViews()
        {
            if(realtime==null||boardView==null)return;
            if (battle.Report != null) battleAudio?.PlayResult(realtime);
            if(combatText==null)combatText=new BattleCombatTextView(unitsRoot,font,battle);
            SyncTacticalObjects();
            selection.Prune(battle,BattleSide.A);selectedUnitId=selection.Primary;
            boardView.RefreshVision(battle.Vision,BattleSide.A,false,false);
            var routed=new List<PrototypeUnit>();
            foreach(var actor in realtime.Model.Units)
            {
                int id=actor.Id-1;if(!views.TryGetValue(id,out var view))continue;
                bool chosen=selection.Contains(id)||!realtimeSingleCommander&&battle.CommanderFor(view.Soldier) is BattleSoldier leader&&selection.Contains(leader.Id);
                view.SyncRealtime(battle,actor,realtime.Model.Elapsed,RealtimeWorld(actor.BodyCenter),chosen);
                if(chosen&&actor.Alive&&actor.Side==PrototypeSide.Ally)routed.Add(actor);
            }
            SyncSpells();
            combatText.Sync(battle,boardView.Camera,views,realtime.Model.Elapsed,realtime.RpgAttackHistory);
            orderOverlay.RefreshRealtime(realtime,routed.Count>0?routed[0]:null);
            while(extraOrders.Count<routed.Count-1)extraOrders.Add(new BattleOrderOverlay(transform,lineMaterial));
            for(int i=0;i<extraOrders.Count;i++)extraOrders[i].RefreshRealtime(realtime,i+1<routed.Count?routed[i+1]:null);
            var inspected=InformationUnits.FirstOrDefault(unit=>unit.IsActive);
            var rangeUnit=inspected==null?routed.FirstOrDefault(unit=>unit.Role==PrototypeRole.Leader):realtime.Model.Units.FirstOrDefault(unit=>unit.Id==inspected.Id+1);
            rangeOverlay.RefreshRealtime(realtime,rangeUnit);
            foreach(int id in realtimeArrows.Keys.Where(id=>!realtime.Model.Arrows.Any(arrow=>arrow.Id==id)).ToArray())
            {BattleBoardView.DestroyObject(realtimeArrows[id].gameObject);realtimeArrows.Remove(id);}
            foreach(var arrow in realtime.Model.Arrows)
            {
                if(!realtimeArrows.TryGetValue(arrow.Id,out var line))
                {
                    var host=new GameObject("RealtimeArrow_"+arrow.Id);host.transform.SetParent(unitsRoot,false);line=host.AddComponent<LineRenderer>();line.sharedMaterial=lineMaterial;
                    line.widthMultiplier=.045f;line.positionCount=2;line.startColor=line.endColor=new Color(1,.86f,.3f);realtimeArrows.Add(arrow.Id,line);
                }
                var cell=new GridPosition((int)arrow.Position.X,(int)arrow.Position.Z);line.gameObject.SetActive(battle.Vision.Visible(BattleSide.A,cell));
                line.SetPosition(0,RealtimeWorld(arrow.Position)+Vector3.up*.35f);line.SetPosition(1,RealtimeWorld(arrow.Position-arrow.Direction*.3)+Vector3.up*.35f);
            }
        }
        private void OnApplicationFocus(bool focused)
        {
            if (focused) return;
            CancelGesture();
            if (IsRealtimeCampaign) { ManualPause(); PersistCampaignCheckpoint(true); }
        }
    }
}
