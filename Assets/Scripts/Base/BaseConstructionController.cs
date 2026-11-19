using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
using UnityEngine;
using UnityEngine.InputSystem;
namespace BaseConquest.Base
{
    public sealed partial class BaseSceneController
    {
        private readonly ConstructionBrush constructionBrush=new ConstructionBrush();
        private bool constructionOpen;
        private int constructionTouch=-1;
        private string constructionResult="";
        private GridPosition? constructionHoverCell;
        private float constructionHoverAt;
        private string constructionHoverProblem="";
        private BaseState campLayoutDraft;
        private string campProject, campPlacementMessage;
        private float campViewBefore;
        private Vector2 campPinchCenter;
        private readonly List<(string Id, GridPosition? Position)> campChanges = new();
        public bool CampPlacementMode => campLayoutDraft != null;
        internal bool CampConstructionAvailable => State?.Demo?.Flow?.CampConstructionOrdered == true && !Busy && !ConstructionMode &&
            BaseFacilityLayout.Editable(State) && BaseFacilityLayout.CanOrder(State);
        internal bool CampConstructionReturnReminder => State?.Demo?.Flow?.CampConstructionOrdered == true && State.Campaign.Location == "town";
        internal BaseState CampLayoutDraft => campLayoutDraft;
        internal string CampProject => campProject;
        internal string CampPlacementMessage => campPlacementMessage;
        internal bool CampLayoutChanged => campChanges.Count > 0;
        public bool ConstructionMode => constructionOpen;
        public ConstructionProject SelectedConstructionProject => constructionBrush.Project;
        public string ConstructionHoverText {get;private set;}="点击地图连续施工；右键／Esc退出";
        public void StopConstruction()
        {
            bool returningFromHome=RebuildPlacementMode;
            if (CampPlacementMode)
            {
                campLayoutDraft = null;
                campChanges.Clear();
                campProject = null;
                regionView.SetViewSize(campViewBefore);
                regionView.Follow(PlayerCameraTarget);
            }
            ResetHomeRebuildOverview();RebuildPlacementMode=false;rebuildProject=-1;rebuildCell=null;
            constructionOpen=false;constructionBrush.Cancel();constructionTouch=-1;mouseTap=null;touches.Clear();
            regionView?.SelectCell(null);regionView?.TintSelection(new Color(1,.89f,.28f));Hud?.RefreshConstructionPalette();
            if(returningFromHome)Hud?.RefreshSummary();
        }
        internal void OrderCampConstruction(bool caravanPermit = false)
        {
            if (!BaseFacilityLayout.CanOrder(State) || !BaseInteraction.At(State, "town", "town_quest")) return;
            if (Change(next =>
            {
                if (caravanPermit && BaseDemoDefense.Won(next)) next.Layout.OfficeGift = true;
                next.Demo.Flow.CampConstructionOrdered = true;
            }, "")) Hud.ClosePanel();
        }

        internal void ArmCampPlacement()
        {
            if (!CampConstructionAvailable || Hud.PanelOpen) return;
            campLayoutDraft = State.Copy();
            campChanges.Clear();
            constructionOpen = true;
            campViewBefore = WorldCamera.orthographicSize;
            ClearSelection();
            regionView.UpdateCamera(0, 0);
            regionView.SetViewSize(regionView.FullViewSize);
            regionView.Follow(Vector3.zero);
            SelectCampProject(BaseFacilityLayout.CampProjects(campLayoutDraft).FirstOrDefault()?.Id);
            Hud.RefreshSummary();
        }

        internal void SelectCampProject(string id)
        {
            if (!CampPlacementMode) return;
            campProject = id;
            campPlacementMessage = "在地图上选择空地";
            regionView.SelectCell(null);
            var current = BaseFacilityLayout.BaseLayout(campLayoutDraft).Objects.FirstOrDefault(obj => obj.Id == id);
            if (current != null && campChanges.Any(change => change.Id == id))
                ShowCampPlacementFootprint(current.X, current.Y, current.Object.BoundsWidth, current.Object.BoundsHeight, true);
            Hud.RefreshConstructionPalette();
        }

        internal void PlaceCampProjectFromPointer(Vector2 point)
        {
            if (CampPlacementMode && !IsUiPoint(point) && regionView.Pick(point, out var cell)) PlaceCampProject(cell);
        }

        private void PlaceCampProject(GridPosition cell)
        {
            var definition = BaseFacilityLayout.Find(campProject);
            if (definition == null) return;
            double x = cell.X + .5, y = cell.Z + .5;
            var current = BaseFacilityLayout.BaseLayout(campLayoutDraft).Objects.FirstOrDefault(obj => obj.Id == campProject);
            var footprint = current?.Object;
            double width = footprint?.BoundsWidth ?? definition.Width, height = footprint?.BoundsHeight ?? definition.Height;
            if (!string.IsNullOrEmpty(current?.Shape))
            {
                x = cell.X + width / 2 % 1;
                y = cell.Z + height / 2 % 1;
            }
            if (current != null && current.X == x && current.Y == y) return;
            bool applied = BaseFacilityLayout.Place(campLayoutDraft, campProject, x, y, out campPlacementMessage);
            if (applied)
                campChanges.Add((campProject, new GridPosition((int)(x * 2), (int)(y * 2))));
            ShowCampPlacementFootprint(x, y, width, height, applied);
            Hud.RefreshConstructionPalette();
        }

        private void ShowCampPlacementFootprint(double x, double y, double width, double height, bool legal)
        {
            var center = WorldPoint(x, y) + Vector3.up * .05f;
            regionView.ShowFacilitySelection(center, (float)width, (float)height, legal ? Color.white : new Color(1, .32f, .25f));
        }

        internal void StoreCampProject()
        {
            if (!CampPlacementMode || string.IsNullOrEmpty(campProject)) return;
            if (BaseFacilityLayout.Store(campLayoutDraft, campProject, out campPlacementMessage))
            {
                campChanges.Add((campProject, null));
                regionView.SelectCell(null);
            }
            Hud.RefreshConstructionPalette();
        }

        internal bool ConfirmCampLayout()
        {
            if (!CampPlacementMode || !CampLayoutChanged || Hud.PanelOpen) return false;
            if (!Change(state =>
            {
                foreach (var change in campChanges)
                {
                    string problem;
                    bool applied = change.Position is GridPosition halfCell
                        ? BaseFacilityLayout.Place(state, change.Id, halfCell.X * .5, halfCell.Z * .5, out problem)
                        : BaseFacilityLayout.Store(state, change.Id, out problem);
                    if (!applied) throw new InvalidOperationException(problem);
                }
                state.Demo.Flow.CampConstructionOrdered = false;
            }, "营地布局已保存"))
            {
                campPlacementMessage = Message;
                Hud.RefreshConstructionPalette();
                return false;
            }
            StopConstruction();
            RefreshDemoRegion();
            return true;
        }

        internal void RequestExitCampPlacement()
        {
            StopConstruction();
            Hud.RefreshSummary();
        }

        public void ArmConstruction()
        {
            if(State==null||Busy)return;string problem=BaseConstruction.ModeProblem(State);
            if(problem!=""){Message=problem;Hud.RefreshSummary();return;}
            Hud.ClosePanel();ClearSelection();constructionResult="";constructionOpen=true;constructionBrush.Select(ConstructionProject.None);
            ConstructionHoverText="选择陷阱、沙袋或拆除工具，再点击格子";
            Message="从下方选择工事，点击或拖划格子建造。";Hud.RefreshSummary();
        }
        public void SelectConstructionProject(ConstructionProject project)
        {
            if(project==ConstructionProject.None){StopConstruction();return;}
            if(State==null||Busy||!ConstructionMode)return;
            constructionBrush.Select(project);constructionTouch=-1;mouseTap=null;touches.Clear();constructionResult="";
            ConstructionHoverText=project==ConstructionProject.Demolish?"将整组拆除该处工事，所耗原料不予返还":project==ConstructionProject.Trap?"埋设陷阱：原料4，每战场最多10个":"堆筑沙袋：向右占3格，原料2";
            Hud.RefreshConstructionPalette();
        }
        private void PaintConstructionCells(List<GridPosition> cells)
        {
            if(cells.Count==0)return;
            bool any=false;foreach(var p in cells)if(constructionBrush.Problem(State,p)==""){any=true;break;}
            if(!any){constructionResult=constructionBrush.Problem(State,cells[cells.Count-1]);return;}
            ConstructionPaintResult result=null;
            if(Change(s=>{result=constructionBrush.Apply(s,cells);if(result.Applied==0)throw new InvalidOperationException(result.Problem);},"已保存连续营造"))
            {
                constructionResult=(SelectedConstructionProject==ConstructionProject.Demolish ? "已拆除 " : "已建造 ")+result.Applied+"处"+(result.MaterialsSpent>0 ? " · 原料 −"+result.MaterialsSpent : " · 无材料返还")+(result.Skipped>0 ? " · 跳过 "+result.Skipped+"格" : "");RefreshConstructionMap();
            }
            else constructionResult=Message;
        }
        private void UpdateConstructionPointer()
        {
            if (CampPlacementMode)
            {
                if (Mouse.current?.rightButton.wasPressedThisFrame == true) { RequestExitCampPlacement(); return; }
                ReadPointer();
                if (Mouse.current != null && mouseTap != null && mouseTap.Travel > Mathf.Max(10, Screen.height / 720f * 16))
                    regionView.Pan(Mouse.current.delta.ReadValue());
                return;
            }
            if(RebuildPlacementMode)
            {
                if(Mouse.current?.rightButton.wasPressedThisFrame==true)StopConstruction();
                else ReadPointer();
                return;
            }
            // Construction consumes primary pointer only; wheel and two-finger zoom remain available.
            var wheel=Mouse.current?.scroll.ReadValue().y??0;if(Math.Abs(wheel)>.01f)ZoomExploration(Mouse.current.position.ReadValue(),wheel);
            if(Touchscreen.current!=null){var pair=System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Take(System.Linq.Enumerable.Where(Touchscreen.current.touches,t=>t.press.isPressed),2));if(pair.Length==2){var a=pair[0].position.ReadValue();var b=pair[1].position.ReadValue();float span=Vector2.Distance(a,b);if(explorationPinchSpan>0)ZoomExplorationFactor((a+b)*.5f,explorationPinchSpan/Mathf.Max(1,span));explorationPinchSpan=span;constructionBrush.EndStroke();constructionTouch=-1;return;}}
            explorationPinchSpan=0;
            if(State.Campaign.Location!="perimeter"||!BaseDemoFlow.Active(State)){StopConstruction();return;}
            if(Mouse.current?.rightButton.wasPressedThisFrame==true){StopConstruction();Message="已退出建造模式";Hud.RefreshSummary();return;}
            bool touchActive=false;
            if(Touchscreen.current!=null)foreach(var touch in Touchscreen.current.touches)
            {
                int id=touch.touchId.ReadValue();Vector2 point=touch.position.ReadValue();
                if(constructionTouch<0&&touch.press.wasPressedThisFrame&&!IsUiPoint(point)&&WorldCamera.pixelRect.Contains(point)){constructionTouch=id;constructionBrush.BeginStroke();}
                if(id!=constructionTouch)continue;
                touchActive=true;
                if(touch.phase.ReadValue()==UnityEngine.InputSystem.TouchPhase.Canceled){constructionBrush.EndStroke();constructionTouch=-1;break;}
                SampleConstructionPoint(point,touch.press.isPressed||touch.press.wasReleasedThisFrame);
                if(touch.press.wasReleasedThisFrame){constructionBrush.EndStroke();constructionTouch=-1;}
                break;
            }
            if(!touchActive&&constructionTouch<0&&Mouse.current!=null)
            {
                Vector2 point=Mouse.current.position.ReadValue();
                if(Mouse.current.leftButton.wasPressedThisFrame&&!IsUiPoint(point)&&WorldCamera.pixelRect.Contains(point))constructionBrush.BeginStroke();
                SampleConstructionPoint(point,Mouse.current.leftButton.isPressed||Mouse.current.leftButton.wasReleasedThisFrame);
                if(Mouse.current.leftButton.wasReleasedThisFrame)constructionBrush.EndStroke();
            }
            Hud.RefreshConstructionPalette();
        }
        private void SampleConstructionPoint(Vector2 point,bool pressed)
        {
            if(IsUiPoint(point)||!WorldCamera.pixelRect.Contains(point)||!regionView.Pick(point,out var cell))
            {constructionBrush.BreakLine();constructionHoverCell=null;regionView.SelectCell(null);return;}
            if(pressed&&constructionBrush.Drawing)PaintConstructionCells(constructionBrush.Sample(cell));
            if(!constructionHoverCell.HasValue||!constructionHoverCell.Value.Equals(cell)||pressed||Time.unscaledTime>=constructionHoverAt)
            {constructionHoverProblem=constructionBrush.Problem(State,cell);constructionHoverCell=cell;constructionHoverAt=Time.unscaledTime+.25f;}
            string problem=constructionHoverProblem;
            ConstructionHoverText="格 "+cell+" · "+(problem=="" ? SelectedConstructionProject==ConstructionProject.Demolish ? "可拆除（无返还）" : "可建造" : problem)+(constructionResult=="" ? "" : "\n"+constructionResult);
            regionView.SelectCell(cell);regionView.TintSelection(problem=="" ? new Color(.35f,1,.55f) : new Color(1,.32f,.25f));
        }
        public void PaintConstructionCell(GridPosition cell)
        {
            if(!ConstructionMode)return;
            if (CampPlacementMode) { PlaceCampProject(cell); return; }
            if(RebuildPlacementMode){SelectRebuildCell(cell);return;}
            constructionBrush.BeginStroke();PaintConstructionCells(constructionBrush.Sample(cell));constructionBrush.EndStroke();Hud.RefreshConstructionPalette();
        }
    }
}
