using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Base;
using BaseConquest.RealtimePrototype;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BaseConquest.Battle
{
    public sealed partial class BattleSceneController
    {
        private RealtimeCampaignBattle realtime;
        private bool realtimeSingleCommander;
        private int informationFineCell=-1,lastRealtimeTap=int.MinValue;
        private double lastRealtimeTapTime=-10;
        private bool lastRealtimeGround;
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
        public string RealtimeStatus=>realtime?.TutorialProgress!=null?realtime.TutorialProgress():"即时格战 · "+(realtime?.Model.Elapsed??0).ToString("0.0")+"秒 · "+(autoRun?"进行中":"暂停")+" · "+(campaignSession?.Opening.RealtimeRulesVersion==0?"旧实验数值·本场保留":"细格数值")+(campaignSession?.Opening.DemoCombat==true?" · Demo独立存档":" · 独立实验存档");
        public const string RealtimePending="首版：1轮参数暂按1秒；脚力冲锋／救援／胜后追击未接入";
        public void PrepareCampaignPreview(BaseCampaignSession session)
        {
            campaignSession=session;campaignBattleId=session.Opening.Id;suppressInitialDeploy=true;Initialize();
            if(IsRealtimeCampaign&&battle.Phase!=BattlePhase.Deployment)AttachRealtime();hud?.Refresh();
        }
        private void AttachRealtime()
        {
            realtime=new RealtimeCampaignBattle(campaignSession);autoRun=false;animating=skipping=false;presentation=1;
            if(IsSceneEncounter)configureSceneEncounter?.Invoke(realtime);
            if(realtime.Model.Player!=null)boardView.CenterOn(battle.Soldier(realtime.Model.Player.Id-1).Cell);
            feedback="即时战斗已暂停：点选细格查看；选队长后单击移动／攻击，双击空地驻守／敌人集火；空格继续。";
            if(realtime.Tactics.Objects.Count>0)feedback+=" 点击枯树可攻击倒障；点击巨石可沿攻击方向推动，双方均可能受伤。";
            var chosen=realtime.Model.Units.Where(u=>u.Alive&&u.Side==PrototypeSide.Ally&&u.Role==PrototypeRole.Leader&&u.Strategy==PrototypeStrategy.Obey).OrderByDescending(u=>u.Player).ThenBy(u=>u.Squad).FirstOrDefault();
            if(chosen!=null){selection.Set(new[]{chosen.Id-1});selectedUnitId=chosen.Id-1;deploymentCommanderSlot=chosen.Squad;deploymentSlotChosen=true;}
            SyncRealtimeViews();
        }
        public bool CanSteerRealtimeCommander=>realtime!=null&&autoRun&&!ExternalDialogue&&battle.Phase==BattlePhase.Fighting&&campaignMenu==null&&!DetailsVisible&&hud?.ReportVisible!=true&&developerPanel?.BlocksGameInput!=true&&selection.Ids.Count==1&&realtime.Model.Units.Any(u=>u.Id==selection.Primary+1&&u.Alive&&u.Side==PrototypeSide.Ally&&u.Role==PrototypeRole.Leader);
        private int joystickDriven=-1;
        private void ReleaseRealtimeSteering(){if(joystickDriven>=0)realtime?.Model.ReleaseSteering(joystickDriven+1);joystickDriven=-1;}
        public bool SteerRealtimeCommander(Vector2 direction)
        {
            if(!CanSteerRealtimeCommander||direction.magnitude<.15f){ReleaseRealtimeSteering();return false;}
            joystickDriven=selection.Primary;
            var camera=boardView.Camera.transform;var right=Vector3.ProjectOnPlane(camera.right,Vector3.up).normalized;var up=Vector3.ProjectOnPlane(camera.up,Vector3.up).normalized;
            var world=right*direction.x+up*direction.y;
            return realtime.SteerCommander(selection.Primary,new PrototypePoint(world.x,world.z));
        }
        private bool RealtimeOrder(PrototypeOrder order,PrototypePoint point,int target=-1,bool squadFocus=false)
        {
            if(realtime==null)return false;
            ReleaseRealtimeSteering();hud?.Joystick?.ResetInput();
            if(order==PrototypeOrder.Auto)
            {var actor=realtime.Model.Units.FirstOrDefault(unit=>unit.Alive&&selection.Contains(unit.Id-1));if(actor==null)return false;point=actor.Position;}
            bool accepted=realtime.Issue(selection.Ids,order,point,target,true,squadFocus);
            feedback=accepted?(autoRun?"指令已执行":"指令已写入，按空格或继续按钮运行"):"未下令：请选择可行动队长／小队，确认目标可见、落点可达";
            SyncRealtimeViews();hud?.Refresh();return accepted;
        }
        public bool RealtimeMove(PrototypePoint point)=>RealtimeOrder(PrototypeOrder.Move,point);
        public void RealtimeGuardSelection()
        {
            var actor=realtime?.Model.Units.FirstOrDefault(unit=>selection.Contains(unit.Id-1)&&unit.Alive);
            if(actor!=null)RealtimeOrder(PrototypeOrder.Guard,actor.Position);
        }
        public void RealtimeRetreat()
        {if(realtime==null)return;ManualPause();if(realtime.DefensePursuitReady)feedback=realtime.FinishDefensePursuit()?"已结束追击，存活敌军撤离":"无法结算";else feedback=realtime.RetreatAll()?(realtime.Session.Opening.DemoCombat?"已立即撤退；返回后结算真实战损与军需":"全军向真实撤离口移动；抵达后才算成功撤离"):"暂无可撤离单位";SyncRealtimeViews();hud?.Refresh();}
        public bool PauseForDialogue(){ReleaseRealtimeSteering();hud?.Joystick?.ResetInput();bool running=autoRun;autoRun=false;realtime?.Model.SetPaused(true);return running;}
        public void ResumeAfterDialogue(bool running){autoRun=running;realtime?.Model.SetPaused(!running);}
        public void ToggleRealtimePause()
        {
            if(realtime==null||battle.Phase!=BattlePhase.Fighting)return;
            ReleaseRealtimeSteering();hud?.Joystick?.ResetInput();realtime.TutorialAction?.Invoke(BaseDemoIntro.Pause);autoRun=!autoRun;realtime.Model.SetPaused(!autoRun);feedback=autoRun?"即时战斗进行中":"已暂停，可以下令";hud?.Refresh();
        }
        private void SelectRealtimeCommander(int id,bool alone)
        {
            var commander=battle.Soldier(id);if(commander?.Kind!=SoldierKind.Hero||!commander.IsActive||commander.Side!=BattleSide.A)return;
            ReleaseRealtimeSteering();hud?.Joystick?.ResetInput();realtime.TutorialAction?.Invoke(BaseDemoIntro.Select);selection.Set(new[]{id});selectedUnitId=informationUnitId=id;realtimeSingleCommander=alone;
            deploymentCommanderSlot=commander.OriginCommanderSlot;deploymentSlotChosen=true;informationIsCell=inspectionTracksUnit=false;informationCell=null;boardView.SelectCell(null);ShowInformationNotice();
            informationFineCell=-1;feedback="指挥队长：单击移动／攻击，双击驻守／队伍集火；士兵按策略护卫";SyncRealtimeViews();hud?.Refresh();
        }
        public void ToggleRealtimeAutoSkill(int slot)
        {if(realtime?.SetAutoSkill(slot,!realtime.AutoSkill(BattleSide.A,slot))==true){feedback="已更新自动技能";hud?.Refresh();}}
        private int realtimeCandidate=-1;
        public bool RealtimePointer(Vector2 screen,bool secondary=false,bool single=false)
        {
            if(realtime==null||battle.Phase!=BattlePhase.Fighting||developerPanel?.BlocksPointer(screen)==true||campaignMenu!=null||DetailsVisible||hud?.ReportVisible==true||hud?.BlocksPointer(screen)==true||!boardView.Camera.pixelRect.Contains(screen))return false;
            if(!ResolvePointer(screen,out var cell))return false;
            var ray=boardView.Camera.ScreenPointToRay(screen);var plane=new Plane(Vector3.up,Vector3.zero);
            if(!plane.Raycast(ray,out float distance))return false;
            var world=ray.GetPoint(distance);var point=new PrototypePoint(world.x/BattleBoardView.CellSize+battle.Board.Width*.5,world.z/BattleBoardView.CellSize+battle.Board.Height*.5);
            int fine=realtime.Model.Arena.CellAt(point);if(fine<0)return false;
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
            InspectRealtimeFine(point);
            if(!HasSelection){feedback="查看格内全部可见单位与地形";hud?.Refresh();return true;}
            if(guardDouble){RealtimeOrder(PrototypeOrder.Guard,point);return true;}
            if(picked?.Side==BattleSide.B&&picked.IsActive){RealtimeOrder(PrototypeOrder.Focus,point,picked.Id,twice);return true;}
            if(picked?.Side==BattleSide.B){feedback="查看倒地单位；当前目标无法被攻击";hud?.Refresh();return true;}
            if(OrderTacticalObject(point))return true;
            RealtimeOrder(twice?PrototypeOrder.Guard:PrototypeOrder.Move,point);return true;
        }
        private List<BattleSoldier> RealtimeFineUnits(int fine)
        {
            if(realtime==null)return new List<BattleSoldier>();var p=realtime.Model.Arena.Center(fine);
            return realtime.Model.Units.Where(a=>!a.Escaped&&(a.Alive||a.KnockedDown)&&
                (a.FootprintLength==2?a.Contains(p):realtime.Model.Arena.CellAt(a.Position)==fine))
                .Select(a=>battle.Soldier(a.Id-1)).Where(s=>battle.CanInspectUnit(selectedSide,s)).OrderBy(s=>s.Id).ToList();
        }
        public void InspectRealtimeFine(PrototypePoint point)
        {
            if(realtime==null)return;int fine=realtime.Model.Arena.CellAt(point);if(fine<0)return;
            informationFineCell=fine;var cell=new GridPosition(fine%realtime.Model.Arena.FineColumns/2,fine/realtime.Model.Arena.FineColumns/2);
            boardView.SelectFineCell(fine%realtime.Model.Arena.FineColumns,fine/realtime.Model.Arena.FineColumns);informationCell=cell;informationIsCell=true;inspectionTracksUnit=false;
            informationUnitId=RealtimeFineUnits(fine).FirstOrDefault()?.Id??-1;ShowInformationNotice();
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
            if(developerPanel?.BlocksGameInput==true||campaignMenu!=null||DetailsVisible||hud?.ReportVisible==true)ManualPause();
            realtime.Model.SetPaused(!autoRun);
            double before=realtime.Model.Elapsed;realtime.Tick(seconds);
            if(IsSceneEncounter&&realtime.Model.Elapsed>before)advanceSceneFollowers?.Invoke(realtime.Model.Elapsed-before);
        }
        private void UpdateRealtime()
        {
            boardView.UpdateCamera(Mathf.Min(.21f,132f/1280),.08f);
            if(developerPanel?.BlocksGameInput==true){MapPanUp();ManualPause();SyncRealtimeViews();PersistCampaignResult();hud?.Refresh();return;}
            var keyboard=Keyboard.current;
            if(campaignMenu!=null){MapPanUp();ManualPause();PersistCampaignCheckpoint();PersistCampaignResult();hud?.Refresh();return;}
            if(keyboard!=null)
            {
                if(keyboard.escapeKey.wasPressedThisFrame){ManualPause();if(DetailsVisible)CloseCommanderDetails();else if(hud.ReportVisible)hud.CloseReport();else OpenCampaignMenu();return;}
                if(keyboard.spaceKey.wasPressedThisFrame)HandleSpaceInput();
                if(keyboard.digit1Key.wasPressedThisFrame)SelectCommanderSlot(0);
                if(keyboard.digit2Key.wasPressedThisFrame)SelectCommanderSlot(1);
                if(keyboard.digit3Key.wasPressedThisFrame)SelectCommanderSlot(2);
                if(keyboard.hKey.wasPressedThisFrame)RealtimeGuardSelection();
                if(keyboard.bKey.wasPressedThisFrame){ManualPause();OpenBattleBackpack();return;}
                if(keyboard.nKey.wasPressedThisFrame){ManualPause();OpenBattleQuests();return;}
            }
            bool cameraGestureConsumed=ReadMapCameraInput(Time.unscaledDeltaTime);
            if(!cameraGestureConsumed&&!DetailsVisible&&hud?.ReportVisible!=true)
            {
                var mouse=Mouse.current;
                if(mouse?.leftButton.wasPressedThisFrame==true)RealtimePointer(mouse.position.ReadValue(),false,keyboard?.leftShiftKey.isPressed==true);
                if(mouse?.rightButton.wasPressedThisFrame==true)RealtimePointer(mouse.position.ReadValue(),true,keyboard?.leftShiftKey.isPressed==true);
                if(Touchscreen.current!=null)foreach(var touch in Touchscreen.current.touches)if(touch.press.wasPressedThisFrame)RealtimePointer(touch.position.ReadValue());
            }
            int iterations=fastMode?3:1;for(int index=0;index<iterations;index++){SteerRealtimeCommander(hud.Joystick?.Direction??Vector2.zero);AdvanceRealtimeModel(Math.Min(.25,Time.unscaledDeltaTime));}
            SyncRealtimeViews();
            UpdatePortraitHold();PersistCampaignResult();PersistCampaignCheckpoint();hud?.Refresh();
        }
        private Vector3 RealtimeWorld(PrototypePoint point)=>new Vector3(((float)point.X-battle.Board.Width*.5f)*BattleBoardView.CellSize,0,((float)point.Z-battle.Board.Height*.5f)*BattleBoardView.CellSize);
        private BattleCombatTextView combatText;
        private void SyncRealtimeViews()
        {
            if(realtime==null||boardView==null)return;
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
        {if(!focused&&IsRealtimeCampaign){ReleaseRealtimeSteering();hud?.Joystick?.ResetInput();MapPanUp();ManualPause();realtime?.Model.SetPaused(true);PersistCampaignCheckpoint(true);}}
    }
}
