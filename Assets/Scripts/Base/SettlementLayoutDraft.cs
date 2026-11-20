using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;

namespace BaseConquest.Base
{
    // A settlement can hold overlapping buildings while the player rearranges them.
    // Only the final layout ever enters a save or changes stock, staffing and collision.
    internal sealed class SettlementLayoutDraft
    {
        private readonly BaseMapLayout original;
        private readonly string mapKey;
        private readonly HashSet<string> changed = new();
        internal readonly bool Village;
        internal readonly BaseMapLayout Map;
        internal readonly BaseFacilityDef[] Projects;
        internal bool Changed => changed.Count > 0;
        internal int Revision { get; private set; }

        internal SettlementLayoutDraft(BaseState state, bool village)
        {
            Village = village;
            mapKey = village ? BaseDemoFinale.Home : "base";
            original = BaseSceneInstances.Layout(state, mapKey).Copy();
            Map = original.Copy();
            Projects = village
                ? new[] { BaseFacilityLayout.Find("farm"), BaseFacilityLayout.Find("workshop"), BaseFacilityLayout.Find("barracks") }
                : BaseFacilityLayout.CampProjects(state).Concat(BaseFacilityLayout.Catalog.Where(d => d.Required &&
                    (d.Id != "barracks" || BaseDemoDefense.Repaired(state, d.Id)))).ToArray();
        }

        internal BaseMapPlacement Placed(string id) => Map.Objects.Find(o => o.Id == id);
        internal bool CanStore(string id) => !Village && Placed(id) != null && BaseFacilityLayout.Find(id)?.Required == false;
        internal int Price(BaseState state, string id, bool money)
        {
            if (Village && id == "barracks" && !original.Objects.Any(o => o.Id == id))
                return money ? BaseDemoDefense.RepairCoinCost(state) : BaseDemoDefense.RepairMaterialCost(state);
            var def = BaseFacilityLayout.Find(id);
            if (def == null || original.Objects.Any(o => o.Id == id) || !Village && BaseFacilityLayout.Stored(state,id)) return 0;
            return money ? def.Coins : def.Materials;
        }

        internal int Cost(BaseState state, bool money) => changed.Where(id => Placed(id) != null).Sum(id => Price(state,id,money));

        internal void Move(string id, double x, double y)
        {
            var def = Projects.FirstOrDefault(d => d.Id == id);
            if (def == null) return;
            var p = Placed(id);
            if (p == null)
            {
                p = original.Objects.Find(o => o.Id == id)?.Copy() ?? new BaseMapPlacement
                {
                    Id = id, Name = def.Name, SourceObjectId = (Village ? "village-layout-" : "camp-layout-") + id,
                    AssetId = def.Asset, Kind = "facility", Blocks = true, Width = def.Width, Height = def.Height
                };
                Map.Objects.Add(p);
            }
            var obj = p.Object;
            if (string.IsNullOrEmpty(p.Shape))
            {
                x = Math.Round(x * 2) / 2; y = Math.Round(y * 2) / 2;
            }
            else
            {
                x = Math.Round(x - obj.BoundsWidth / 2) + obj.BoundsWidth / 2;
                y = Math.Round(y - obj.BoundsHeight / 2) + obj.BoundsHeight / 2;
            }
            if (p.X == x && p.Y == y && changed.Contains(id)) return;
            p.X = x; p.Y = y;
            RecordChange(id);
        }

        internal void Store(string id)
        {
            if (!CanStore(id)) return;
            Map.Objects.Remove(Placed(id));
            RecordChange(id);
        }

        internal void RestorePlacement(string id, BaseMapPlacement beforeDrag)
        {
            var after = Placed(id);
            if (after?.X == beforeDrag?.X && after?.Y == beforeDrag?.Y) return;
            if (after != null) Map.Objects.Remove(after);
            if (beforeDrag != null) Map.Objects.Add(beforeDrag);
            RecordChange(id);
        }

        private void RecordChange(string id)
        {
            var before = original.Objects.Find(o => o.Id == id);
            var after = Placed(id);
            if (before?.X == after?.X && before?.Y == after?.Y) changed.Remove(id);
            else changed.Add(id);
            Revision++;
        }

        internal string Problem(BaseState state, string id)
        {
            var p = Placed(id);
            if (p == null || !changed.Contains(id)) return "";
            var obj = p.Object;
            var cells = BaseObjectFootprint.Cells(obj).ToArray();
            var board = BaseBattlefield.Board(state,mapKey);
            if (cells.Length == 0 || cells.Any(pos => !board.Contains(pos))) return "超出场地";
            if (obj.Contains(Map.SpawnX,Map.SpawnY,.45) || obj.Contains(state.PlayerX,state.PlayerY,.45)) return "占用人物位置";
            if (Map.Objects.Where(o => o.Id != id && (o.Blocks || !o.VisualOnly)).Any(o =>
                o.Object.Parts.Any(b => obj.Parts.Any(a => Math.Abs(a.X-b.X) < (a.Width+b.Width)/2-1e-6 &&
                                                          Math.Abs(a.Y-b.Y) < (a.Height+b.Height)/2-1e-6)))) return "占地重叠";
            foreach (var pos in cells)
            {
                var cell = board.Cell(pos);
                bool vacated = cell.Terrain == TerrainKind.Building && changed.Contains(cell.BuildingId ?? "");
                if ((!cell.Walkable && !vacated) || cell.Ramp != RampAxis.None ||
                    cell.Elevation != board.Cell(cells[0]).Elevation) return "地基不平或被占用";
            }
            // Two fractional footprints can share a terrain cell even without intersecting geometrically.
            if (Map.Objects.Any(o => o.Id != id && changed.Contains(o.Id) && o.Blocks &&
                                     BaseObjectFootprint.Cells(o.Object).Intersect(cells).Any())) return "占地重叠";
            return "";
        }

        internal string LayoutProblem(BaseState state)
        {
            foreach (var id in changed)
            {
                var problem = Problem(state,id);
                if (problem.Length > 0) return BaseFacilityLayout.Find(id).Name + " · " + problem;
            }
            return Cost(state,false) > state.Inventory.Materials || Cost(state,true) > state.Inventory.Coins ? "资源不足" : "";
        }

        // Called inside the controller's copy-and-validate transaction.
        internal void Commit(BaseState state)
        {
            if (Village ? state.Campaign.CurrentMapId != BaseDemoFinale.Home || !state.Demo.Finale.VillageRebuilt :
                !BaseFacilityLayout.Editable(state) || !state.Demo.Flow.CampConstructionOrdered)
                throw new InvalidOperationException("当前无法建设");
            var problem = LayoutProblem(state);
            if (problem.Length > 0) throw new InvalidOperationException(problem);
            var actual = BaseSceneInstances.Layout(state,mapKey);
            int materials = Cost(state,false), money = Cost(state,true);
            foreach (var id in changed.Where(id => Placed(id) == null))
                if (!BaseFacilityLayout.Store(state,id,out problem)) throw new InvalidOperationException(problem);
            foreach (var id in changed)
                BaseFacilityLayout.RefreshFootprint(state,id,null,mapKey);
            foreach (var id in changed)
            {
                var p = Placed(id);
                if (p == null) continue;
                actual.Objects.RemoveAll(o => o.Id == id);
                actual.Objects.Add(p.Copy());
                BaseFacilityLayout.RefreshFootprint(state,id,p,mapKey);
                if (!Village)
                {
                    state.Layout.Stored.Remove(id);
                    if (id == "caravan") BaseMuster.EnsureCaravan(state);
                }
            }
            state.Inventory.Materials -= materials;
            state.Inventory.Coins -= money;
            state.Layout.Revision++;
            if(Village && changed.Contains("farm") && BaseDemoFinale.HomeVolunteers(state))
            {
                for(int i=0;i<2;i++)
                {
                    var person=actual.Objects.Single(o=>o.Id==(i==0?BaseDemoFinale.Builder:BaseDemoFinale.Helper));
                    var post=BaseDemoFinale.VolunteerPost(state,i);
                    person.X=post.X;person.Y=post.Y;
                }
            }
            if (!Village) state.Demo.Flow.CampConstructionOrdered = false;
            BaseDevelopment.Allocate(state);
        }
    }
}
