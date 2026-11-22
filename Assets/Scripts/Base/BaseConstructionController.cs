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
        private SettlementLayoutDraft campLayout;
        private string campProject, campPlacementMessage;
        private float campViewBefore;
        private Vector2 campPinchCenter;
        private readonly Dictionary<string, GameObject> hiddenBuildings = new();
        private readonly Dictionary<string, (GameObject Root, Vector3 Anchor, LineRenderer Frame)> buildingPreviews = new();
        private string draggedFacility;
        private Vector2 campStart, campLast, campGrabOffset;
        private bool campPressed, campDragged, paletteDragging;
        private BaseMapPlacement placementBeforeDrag;
        public bool CampPlacementMode => campLayout != null;
        internal SettlementLayoutDraft CampLayoutDraft => campLayout;
        internal string CampProject => campProject;
        internal string CampPlacementMessage => campPlacementMessage;
        internal bool CampLayoutChanged => campLayout?.Changed == true;
        public bool ConstructionMode => constructionOpen;
        public ConstructionProject SelectedConstructionProject => constructionBrush.Project;
        public string ConstructionHoverText {get;private set;}="点击地图连续施工；右键／Esc退出";
        public void StopConstruction()
        {
            if (CampPlacementMode)
            {
                foreach (var view in hiddenBuildings.Values) if (view != null) view.SetActive(true);
                foreach (var preview in buildingPreviews.Values)
                    if (preview.Root != null) { preview.Root.SetActive(false); Destroy(preview.Root); }
                hiddenBuildings.Clear();
                buildingPreviews.Clear();
                campLayout = null;
                campProject = draggedFacility = null;
                paletteDragging = campPressed = campDragged = false;
                placementBeforeDrag = null;
                regionView.SetViewSize(campViewBefore);
                regionView.Follow(PlayerCameraTarget);
            }
            constructionOpen=false;constructionBrush.Cancel();constructionTouch=-1;mouseTap=null;touches.Clear();
            regionView?.SelectCell(null);regionView?.TintSelection(new Color(1,.89f,.28f));Hud?.RefreshConstructionPalette();
        }
        internal void GrantCampConstructionPermit()
        {
            if (!BaseFacilityLayout.CanOrder(State) || !BaseDemoDefense.Won(State) ||
                !BaseInteraction.At(State, "town", "town_quest")) return;
            if (Change(next => next.Layout.OfficeGift = true, "")) Hud.ClosePanel();
        }

        internal void ArmCampPlacement()
        {
            if (Busy || ConstructionMode || !BaseFacilityLayout.CanEnterCampConstruction(State) ||
                !BaseInteraction.At(State, "base", "demo_rescuer")) return;
            if (!State.Demo.Flow.CampConstructionOrdered &&
                !Change(next => next.Demo.Flow.CampConstructionOrdered = true, "")) return;
            Hud.ClosePanel();
            StartSettlementPlacement(false);
        }

        internal void ArmVillagePlacement()
        {
            if (Busy || ConstructionMode || State.Campaign.CurrentMapId != BaseDemoFinale.Home || !State.Demo.Finale.VillageRebuilt) return;
            Hud.ClosePanel();
            StartSettlementPlacement(true);
        }

        private void StartSettlementPlacement(bool village)
        {
            campLayout = new SettlementLayoutDraft(State,village);
            constructionOpen = true;
            campViewBefore = WorldCamera.orthographicSize;
            ClearSelection();
            regionView.UpdateCamera(0,0);
            var landmarks = regionView.Root.transform.Find("RegionLandmarks");
            foreach (var def in campLayout.Projects)
            {
                if (landmarks == null) break;
                foreach (Transform item in landmarks)
                    if (item.name.StartsWith(def.Id+" · ",StringComparison.Ordinal))
                    {
                        hiddenBuildings[def.Id] = item.gameObject;
                        item.gameObject.SetActive(false);
                        break;
                    }
            }
            SelectCampProject(village ? "barracks" : campLayout.Projects.FirstOrDefault()?.Id);
            Hud.RefreshSummary();
        }

        internal void SelectCampProject(string id)
        {
            if (campLayout == null || !campLayout.Projects.Any(d=>d.Id==id)) return;
            campProject = id;
            RefreshLayoutPreview();
        }

        private void RefreshLayoutPreview()
        {
            campPlacementMessage = campLayout.LayoutProblem(State);
            foreach (var def in campLayout.Projects)
            {
                var obj = campLayout.Placed(def.Id);
                if (obj == null)
                {
                    if (buildingPreviews.TryGetValue(def.Id,out var old)) old.Root.SetActive(false);
                    continue;
                }
                Vector3 anchor = WorldPoint(obj.X,obj.Y);
                if (!buildingPreviews.TryGetValue(def.Id,out var preview))
                {
                    var view = BaseConquest.Presentation.AuthoredMapView.Create(regionView.Root.transform,obj.Object,anchor,mapMaterial,regionView.ObliqueExploration);
                    var edge = new GameObject("DraftFootprint").AddComponent<LineRenderer>();
                    edge.transform.SetParent(view.transform,false);
                    edge.sharedMaterial = regionView.Root.transform.Find("ClickedCellSelection").GetComponent<LineRenderer>().sharedMaterial;
                    edge.positionCount=4; edge.loop=true; edge.widthMultiplier=.045f;
                    preview=(view.gameObject,anchor,edge);
                }
                else
                {
                    preview.Root.SetActive(true);
                    var delta=anchor-preview.Anchor;
                    if(delta.sqrMagnitude>0.000001f)
                    {
                        preview.Root.transform.position+=delta;
                        foreach(var facade in preview.Root.GetComponentsInChildren<BaseConquest.Presentation.SpriteFacade>())
                            facade.TranslateFeet(delta);
                    }
                }
                buildingPreviews[def.Id]=(preview.Root,anchor,preview.Frame);
                bool invalid=campLayout.Problem(State,def.Id).Length>0;
                preview.Frame.enabled=invalid||def.Id==campProject;
                if(!preview.Frame.enabled)continue;
                Vector3 w=Vector3.right*(float)obj.Object.BoundsWidth*BattleBoardView.CellSize*.5f;
                Vector3 h=Vector3.forward*(float)obj.Object.BoundsHeight*BattleBoardView.CellSize*.5f;
                var c=anchor+Vector3.up*.07f;
                preview.Frame.SetPositions(new[]{c-w-h,c-w+h,c+w+h,c+w-h});
                preview.Frame.startColor=preview.Frame.endColor=invalid?new Color(1,.23f,.17f):Color.white;
            }
            Hud.RefreshConstructionPalette();
        }

        private bool TryBuildPoint(Vector2 point,out Vector2 grid)
        {
            grid=default;
            if (!CampPlacementMode || IsUiPoint(point) || !regionView.Pick(point,out var cell)) return false;
            var ray=WorldCamera.ScreenPointToRay(point);
            var plane=new Plane(Vector3.up,WorldPoint(cell.X+.5,cell.Z+.5));
            if (!plane.Raycast(ray,out var distance)) return false;
            var hit=ray.GetPoint(distance);
            grid=new Vector2(hit.x/BattleBoardView.CellSize+regionView.Board.Width*.5f,
                             hit.z/BattleBoardView.CellSize+regionView.Board.Height*.5f);
            return true;
        }

        internal void DragPaletteProject(string id,Vector2 point,bool finished)
        {
            if (!CampPlacementMode) return;
            if (!paletteDragging)
            {
                SelectCampProject(id);
                placementBeforeDrag=campLayout.Placed(id)?.Copy();
            }
            if (TryBuildPoint(point,out var grid)) MoveCampProject(id,grid);
            else if (finished)
            {
                campLayout.RestorePlacement(id,placementBeforeDrag);
                RefreshLayoutPreview();
            }
            paletteDragging=!finished;
            if(finished)placementBeforeDrag=null;
        }

        private void MoveCampProject(string id,Vector2 grid)
        {
            int before=campLayout.Revision;
            campLayout.Move(id,grid.x,grid.y);
            if(campLayout.Revision!=before)RefreshLayoutPreview();
        }

        private void PlaceCampProject(GridPosition cell) => MoveCampProject(campProject,new Vector2(cell.X+.5f,cell.Z+.5f));

        internal void StoreCampProject()
        {
            if (campLayout==null || !campLayout.CanStore(campProject)) return;
            campLayout.Store(campProject);
            RefreshLayoutPreview();
        }

        internal bool ConfirmCampLayout()
        {
            if (!CampLayoutChanged || Hud.PanelOpen || campPlacementMessage.Length>0) return false;
            bool village=campLayout.Village;
            if (!Change(campLayout.Commit,"已保存布局")) {campPlacementMessage=Message;Hud.RefreshConstructionPalette();return false;}
            StopConstruction();
            RefreshDemoRegion();
            if(village)Hud.FinishVillageConstructionVisit();
            return true;
        }

        internal void RequestExitCampPlacement()
        {
            bool village=campLayout?.Village==true;
            StopConstruction();
            Hud.RefreshSummary();
            if(village)Hud.FinishVillageConstructionVisit();
        }

        private void ReadSettlementPointer()
        {
            if (paletteDragging) return;
            var screen = Touchscreen.current;
            if (screen != null)
            {
                var pair = screen.touches.Where(t=>t.press.isPressed).Take(2).ToArray();
                if (pair.Length==2)
                {
                    var a=pair[0].position.ReadValue();var b=pair[1].position.ReadValue();
                    var center=(a+b)*.5f;var span=Vector2.Distance(a,b);
                    if (!IsUiPoint(a) && !IsUiPoint(b) && explorationPinchSpan>0)
                    {
                        ZoomExplorationFactor(center,explorationPinchSpan/Mathf.Max(1,span));
                        regionView.Pan(center-campPinchCenter);
                    }
                    explorationPinchSpan=span;campPinchCenter=center;campPressed=false;
                    return;
                }
                explorationPinchSpan=0;
                foreach(var t in screen.touches)
                    if (t.press.isPressed || t.press.wasReleasedThisFrame)
                    {
                        if (t.phase.ReadValue()==UnityEngine.InputSystem.TouchPhase.Canceled) {campPressed=false;return;}
                        HandleSettlementPointer(t.position.ReadValue(),t.press.wasPressedThisFrame,t.press.wasReleasedThisFrame);
                        return;
                    }
            }
            var mouse=Mouse.current;
            if(mouse==null)return;
            var point=mouse.position.ReadValue();
            float scroll=mouse.scroll.ReadValue().y;
            if(Mathf.Abs(scroll)>.01f)ZoomExploration(point,scroll);
            HandleSettlementPointer(point,mouse.leftButton.wasPressedThisFrame,mouse.leftButton.wasReleasedThisFrame);
        }

        private void HandleSettlementPointer(Vector2 point,bool down,bool up)
        {
            if(down)
            {
                campPressed=TryBuildPoint(point,out var start);
                if(!campPressed)return;
                campStart=campLast=point;campDragged=false;
                var hit=campLayout.Projects.Select(d=>campLayout.Placed(d.Id)).Where(o=>o!=null && o.Object.Contains(start.x,start.y))
                    .OrderByDescending(o=>o.Id==campProject).FirstOrDefault();
                draggedFacility=hit?.Id;
                if(hit!=null)
                {
                    campGrabOffset=new Vector2((float)hit.X,(float)hit.Y)-start;
                    SelectCampProject(hit.Id);
                }
            }
            if(!campPressed)return;
            campDragged |= Vector2.Distance(point,campStart)>Mathf.Max(6,Screen.height*.009f);
            if(campDragged && point!=campLast)
            {
                if(draggedFacility==null)regionView.Pan(point-campLast);
                else if(TryBuildPoint(point,out var grid))MoveCampProject(draggedFacility,grid+campGrabOffset);
            }
            campLast=point;
            if(!up)return;
            if(!campDragged && draggedFacility==null && TryBuildPoint(point,out var tap)) MoveCampProject(campProject,tap);
            campPressed=false;
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
                ReadSettlementPointer();
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
            constructionBrush.BeginStroke();PaintConstructionCells(constructionBrush.Sample(cell));constructionBrush.EndStroke();Hud.RefreshConstructionPalette();
        }
    }
}
