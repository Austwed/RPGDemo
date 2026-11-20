using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using BaseConquest.RealtimePrototype;

namespace BaseConquest.Battle
{
    // 四种范围按可应用地格绘框；网格变动时才重建，每帧只更新状态颜色。
    public sealed class BattleRangeOverlay
    {
        public readonly GameObject Root;
        private readonly Mesh[] meshes = new Mesh[4];
        private readonly Renderer[] borders = new Renderer[4];
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private string geometryKey;
        public int HighlightedRange { get; private set; } = -1;
        public bool Visible => Root.activeSelf;
        public string Description {get;private set;}="";
        public BattleRangeOverlay(Transform parent, Material material)
        {
            Root = new GameObject("CommanderRadiusOverlay"); Root.transform.SetParent(parent, false);
            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject(new[] { "GuardGreen", "AlertOrange", "AttackRed", "LeadBlue" }[i], typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(Root.transform, false);
                meshes[i] = new Mesh { name = "RangeCellBorders" + i }; go.GetComponent<MeshFilter>().sharedMesh = meshes[i]; borders[i] = go.GetComponent<Renderer>(); borders[i].sharedMaterial = material;
            }
            Root.SetActive(false);
        }
        public void Refresh(BattleSimulation b, BattleSide observer, BattleSoldier commander)
        {
            bool show = commander?.Kind == SoldierKind.Hero && commander.IsActive && b.Vision.CanSee(observer, commander);
            Root.SetActive(show); if (!show) { HighlightedRange = -1; return; }
            var q = b.Squads[commander.SquadId]; var config = b.Scenario.Army(commander.Side).Commanders[commander.OriginCommanderSlot];
            var center = q.GuardAssigned ? q.GuardCenter : commander.Cell;
            int[] ranges = { q.GuardAssigned ? q.ActualGuardRange : config.GuardRange, q.GuardAssigned ? q.ActualAlertRange : config.AlertRange, q.GuardAssigned ? q.ActualAttackRange : config.AttackRange, b.LeadRadius(q) };
            bool attacking = q.Soldiers.Any(s => s.LastAction == RoundAction.Attack) || q.GuardTargets.Any(id => b.Soldier(id) is BattleSoldier t && GridPosition.Distance(t.Cell, center) > q.ActualAlertRange && b.Vision.CanSee(observer, t));
            HighlightedRange = q.GuardAssigned ? q.GuardAlert ? attacking ? 2 : 1 : 0 : q.IssuedCommand == SquadCommand.Move ? 3 : -1;
            string key = observer + "|" + commander.Id + "|" + commander.Cell + "|" + center + "|" + string.Join(",", ranges) + "|" + b.Vision.Revision + "|" + b.Scenario.ShowEnemiesThroughFog + "|" + HighlightedRange;
            if (geometryKey != key)
            {
                geometryKey = key;
                for (int i = 0; i < 4; i++)
                {
                    var vertices = new List<Vector3>(); var indices = new List<int>(); float r = BattleBoardView.CellSize * 0.5f;
                    var cells = b.AppliedRangeCells(i == 3 ? commander.Cell : center, ranges[i]).Where(p => b.Scenario.ShowEnemiesThroughFog || b.Vision.Visible(observer, p));
                    foreach (var edge in BattleSimulation.RangeBoundary(cells))
                    {
                        var origin = BattleBoardView.World(edge.cell, b.Board) + Vector3.up * (HighlightedRange == i ? 0.16f : 0.1f + i * 0.008f);
                        var corners = new[] { origin + new Vector3(r, 0, -r), origin + new Vector3(r, 0, r), origin + new Vector3(-r, 0, r), origin + new Vector3(-r, 0, -r) };
                        var a = corners[edge.side]; var bPoint = corners[(edge.side + 1) % 4]; var width = Vector3.Cross(Vector3.up, bPoint - a).normalized * (HighlightedRange == i ? 0.035f : 0.018f); int offset = vertices.Count;
                        vertices.AddRange(new[] { a - width, a + width, bPoint + width, bPoint - width }); indices.AddRange(new[] { offset, offset + 2, offset + 1, offset, offset + 3, offset + 2 });
                    }
                    meshes[i].Clear(); meshes[i].SetVertices(vertices); meshes[i].SetIndices(indices.ToArray(), MeshTopology.Triangles, 0); meshes[i].RecalculateBounds();
                }
            }
            var colors = new[] { new Color(0.2f, 1, 0.4f), new Color(1, 0.68f, 0.12f), new Color(1, 0.18f, 0.18f), new Color(0.25f, 0.65f, 1) };
            for (int i = 0; i < 4; i++) { var color = HighlightedRange == i ? colors[i] : colors[i] * 0.42f; properties.SetColor("_BaseColor", color); properties.SetColor("_Color", color); borders[i].SetPropertyBlock(properties); }
        }
        public static bool IncludesAttackCell(RealtimeCampaignBattle run, PrototypeUnit unit, PrototypePoint point)
        {
            var origin=unit.Position;
            double reach=unit.Reach;
            if(run.Battle.Scenario.HeightVision && unit.UsesRangedWeapon)
                reach+=BattleHeightSight.ArcherBonus(run.Battle.Board,origin.X,origin.Z,point.X,point.Z)/PrototypeArena.Subdivision;
            return (point-origin).Length<=reach+1e-9 && run.Model.ClearAttack(origin,point);
        }

        public void RefreshRealtime(RealtimeCampaignBattle run,PrototypeUnit unit)
        {
            bool show=unit!=null&&unit.Alive&&run.Battle.Phase==BattlePhase.Fighting&&run.Battle.Vision.CanSee(BattleSide.A,run.Battle.Soldier(unit.Id-1));
            Root.SetActive(show);Description="";if(!show)return;
            bool guard=unit.Role==PrototypeRole.Leader&&run.Model.CommanderDoctrine;
            HighlightedRange=2;
            Description="范围："+run.Battle.UnitName(run.Battle.Soldier(unit.Id-1))+"\n蓝：攻击距离 "+(unit.Reach*2).ToString("0")+"细格（高度／墙体遮挡处截断，高处最多＋6细格）";
            if(guard)Description+="\n绿：驻守 "+(unit.GuardRadius*2).ToString("0")+"；橙：威胁 "+(unit.ThreatRadius*2).ToString("0")+"；红：主动攻击 "+(unit.CommandRadius*2).ToString("0")+"细格\n士兵围绕队长，守护／驻守队长围绕驻守点。策略："+BattleSimulation.StrategyName((SquadStrategy)(int)unit.Strategy);
            string key="rt|"+unit.Id+"|"+unit.Position.X+"|"+unit.Position.Z+"|"+unit.Anchor.X+"|"+unit.Anchor.Z+"|"+guard+"|"+unit.Strategy+"|"+unit.Order+"|"+unit.Reach+"|"+unit.Cell+"|"+run.Battle.Vision.Revision+"|"+run.Battle.TerrainRevision+"|"+run.Tactics.Revision;
            if(geometryKey==key)return;geometryKey=key;
            var arena=run.Model.Arena;var board=run.Battle.Board;
            var reachable=guard?arena.Distances(unit.Cell,true):null;
            for(int range=0;range<4;range++)
            {
                meshes[range].Clear();if(range<3&&!guard)continue;
                var center=range<2&&(unit.Order==PrototypeOrder.Guard||unit.Strategy==PrototypeStrategy.Protect)?unit.Anchor:unit.Position;
                double radius=range==3?unit.Reach+(run.Battle.Scenario.HeightVision&&unit.UsesRangedWeapon?3:0):range==0?unit.GuardRadius:range==1?unit.ThreatRadius:unit.CommandRadius;
                var cells=new List<GridPosition>();
                int minX=System.Math.Max(0,(int)System.Math.Floor((center.X-radius)*2)),maxX=System.Math.Min(arena.FineColumns-1,(int)System.Math.Ceiling((center.X+radius)*2));
                int minZ=System.Math.Max(0,(int)System.Math.Floor((center.Z-radius)*2)),maxZ=System.Math.Min(arena.FineRows-1,(int)System.Math.Ceiling((center.Z+radius)*2));
                for(int z=minZ;z<=maxZ;z++)for(int x=minX;x<=maxX;x++)
                {
                    int cell=z*arena.FineColumns+x;var point=arena.Center(cell);
                    if(!run.Battle.Vision.Visible(BattleSide.A,new GridPosition(x/2,z/2)))continue;
                    if(range==3)
                    {
                        if(!IncludesAttackCell(run,unit,point))continue;
                    }
                    else if((point-center).Length>radius+1e-9||!arena.FineWalkable(cell)||double.IsPositiveInfinity(reachable[cell]))continue;
                    cells.Add(new GridPosition(x,z));
                }
                var vertices=new List<Vector3>();var indices=new List<int>();float r=BattleBoardView.CellSize*.25f;
                foreach(var edge in BattleSimulation.RangeBoundary(cells))
                {
                    var origin=new Vector3((edge.cell.X*.5f+.25f-board.Width*.5f)*BattleBoardView.CellSize,.1f+range*.02f,(edge.cell.Z*.5f+.25f-board.Height*.5f)*BattleBoardView.CellSize);
                    var corners=new[]{origin+new Vector3(r,0,-r),origin+new Vector3(r,0,r),origin+new Vector3(-r,0,r),origin+new Vector3(-r,0,-r)};
                    var a=corners[edge.side];var b=corners[(edge.side+1)%4];var width=Vector3.Cross(Vector3.up,b-a).normalized*.025f;int offset=vertices.Count;
                    vertices.AddRange(new[]{a-width,a+width,b+width,b-width});indices.AddRange(new[]{offset,offset+2,offset+1,offset,offset+3,offset+2});
                }
                meshes[range].SetVertices(vertices);meshes[range].SetIndices(indices.ToArray(),MeshTopology.Triangles,0);meshes[range].RecalculateBounds();
                var color=range==0?new Color(.2f,1,.4f):range==1?new Color(1,.68f,.12f):range==2?new Color(1,.18f,.18f):new Color(.25f,.65f,1);
                properties.SetColor("_BaseColor",color);properties.SetColor("_Color",color);borders[range].SetPropertyBlock(properties);
            }
        }
        public void Dispose() { Root.SetActive(false); foreach (var mesh in meshes) BattleBoardView.DestroyObject(mesh); }
    }
}
