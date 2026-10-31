using System;
using System.Collections.Generic;
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
        public bool ConstructionMode => constructionOpen;
        public ConstructionProject SelectedConstructionProject => constructionBrush.Project;
        public string ConstructionHoverText {get;private set;}="点击地图连续施工；右键／Esc退出";
        public void StopConstruction()
        {
            constructionOpen=false;constructionBrush.Cancel();constructionTouch=-1;mouseTap=null;touches.Clear();
            regionView?.SelectCell(null);regionView?.TintSelection(new Color(1,.89f,.28f));Hud?.RefreshConstructionPalette();
        }
        public void ArmConstruction()
        {
            if(State==null||Busy)return;string problem=BaseConstruction.ModeProblem(State);
            if(problem!=""){Message=problem;Hud.RefreshSummary();return;}
            Hud.ClosePanel();ClearSelection();constructionResult="";constructionOpen=true;constructionBrush.Select(ConstructionProject.None);
            ConstructionHoverText="先在左侧选择拒马或拆除工具，再点击／拖划格子";
            Message="建造模式：左侧选项目，点击／拖划连续建造；右键／Esc退出";Hud.RefreshSummary();
        }
        public void SelectConstructionProject(ConstructionProject project)
        {
            if(project==ConstructionProject.None){StopConstruction();return;}
            if(State==null||Busy||!ConstructionMode)return;
            constructionBrush.Select(project);constructionTouch=-1;mouseTap=null;touches.Clear();constructionResult="";
            ConstructionHoverText=project==ConstructionProject.Demolish ? "拆除：连续点击／拖划普通路障；固定设施不可拆" : "拒马：成品优先，每格原料1、生命100；点击／拖划连续建造";
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
                constructionResult=(SelectedConstructionProject==ConstructionProject.Demolish ? "已拆除 " : "已建造 ")+result.Applied+"处"+(result.MaterialsSpent>0 ? " · 原料 −"+result.MaterialsSpent : SelectedConstructionProject==ConstructionProject.Barricade&&BaseDemoFlow.Active(State)?" · 使用成品拒马":" · 无材料返还")+(result.Skipped>0 ? " · 跳过 "+result.Skipped+"格" : "");RefreshConstructionMap();
            }
            else constructionResult=Message;
        }
        private void UpdateConstructionPointer()
        {
            // Construction consumes primary pointer only; wheel and two-finger zoom remain available.
            var wheel=Mouse.current?.scroll.ReadValue().y??0;if(Math.Abs(wheel)>.01f)ZoomExploration(Mouse.current.position.ReadValue(),wheel);
            if(Touchscreen.current!=null){var pair=System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Take(System.Linq.Enumerable.Where(Touchscreen.current.touches,t=>t.press.isPressed),2));if(pair.Length==2){var a=pair[0].position.ReadValue();var b=pair[1].position.ReadValue();float span=Vector2.Distance(a,b);if(explorationPinchSpan>0)ZoomExplorationFactor((a+b)*.5f,explorationPinchSpan/Mathf.Max(1,span));explorationPinchSpan=span;constructionBrush.EndStroke();constructionTouch=-1;return;}}
            explorationPinchSpan=0;
            if(State.Campaign.Location!="perimeter"&&State.Campaign.Location!="base"||!BaseDemoFlow.Active(State)&&!State.Construction.FieldworksUnlocked){StopConstruction();return;}
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
            ConstructionHoverText="格 "+cell+" · "+(problem=="" ? SelectedConstructionProject==ConstructionProject.Demolish ? "可拆除（无返还）" : "可建造（成品优先，原料1）" : problem)+(constructionResult=="" ? "" : "\n"+constructionResult);
            regionView.SelectCell(cell);regionView.TintSelection(problem=="" ? new Color(.35f,1,.55f) : new Color(1,.32f,.25f));
        }
        public void PaintConstructionCell(GridPosition cell)
        {if(!ConstructionMode)return;constructionBrush.BeginStroke();PaintConstructionCells(constructionBrush.Sample(cell));constructionBrush.EndStroke();Hud.RefreshConstructionPalette();}
    }
}
