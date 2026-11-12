using System;
using System.IO;
using System.Linq;
using BaseConquest.Base;
using BaseConquest.Battle;
using BaseConquest.Presentation;
using UnityEditor;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static class VerifyFieldworkArt
    {
        private static void Need(bool yes,string message){if(!yes)throw new InvalidOperationException(message);}
        public static void RunFieldworkStage()
        {
            var asset=Resources.Load<TextAsset>("DemoStages/defense-male");Need(asset!=null,"缺少首防阶段档");
            var state=JsonUtility.FromJson<BaseState>(asset.text);var board=BaseBattlefield.Board(state);int materials=state.Inventory.Materials;
            var traps=board.Positions().Where(p=>BaseConstruction.DemoFieldworkProblem(state,p,ConstructionProject.Trap)=="").ToArray();Need(traps.Length>0,"阶段档中找不到可建陷阱的位置");
            var trap=traps[0];Need(BaseConstruction.DemoFieldwork(state,trap,ConstructionProject.Trap),"陷阱建造失败");
            var bags=board.Positions().Where(p=>BaseConstruction.DemoFieldworkProblem(state,p,ConstructionProject.Sandbag)=="").ToArray();Need(bags.Length>0,"阶段档中找不到可建沙袋的位置");
            var bag=bags[0];Need(BaseConstruction.DemoFieldwork(state,bag,ConstructionProject.Sandbag)&&state.Inventory.Materials==materials-6,"工事建造或费用错误");
            int group=board.Cell(bag).SandbagGroup;var host=new GameObject("FieldworkStageVerification");var source=new Material(Shader.Find("Universal Render Pipeline/Unlit"));BattleBoardView view=null;
            try
            {
                view=new BattleBoardView(board,host.transform,source,source){FlatView=true,ObliqueBaseView=true};view.SetCombatMode(false);
                Transform Find(GridPosition p)=>view.Root.transform.Find("FieldworkArt/Fieldwork_"+p.X+"_"+p.Z);
                Need(Find(trap)?.gameObject.activeSelf==true&&Find(bag)?.gameObject.activeSelf==true,"战前地图未显示工事");
                var campaign=BaseDemoDefense.Begin(state,"none");BattleScenePlacement.Start(state,campaign);
                var session=new BaseMapSession(board);session.PrepareCombat(state,campaign.Battle.Board);session.EnterBattle(campaign.Battle);view.SetCombatMode(true);
                Need(!Find(trap).gameObject.activeSelf&&Find(bag).gameObject.activeSelf,"战斗投影中的工事显示错误");
                var run=new RealtimeCampaignBattle(campaign);BattleScenePlacement.Snap(state,run);BaseDemoDefense.Configure(run);
                session.Board.Cell(trap).HiddenTrap=false;Need(BaseDemoCombat.Fail(state,run),"首防败退结算失败");session.LeaveBattle();view.SetCombatMode(false);
                Need(!Find(trap).gameObject.activeSelf&&Find(bag).gameObject.activeSelf,"战后消耗没有同步到地图显示");
                var store=new BaseSaveStore(Path.GetFullPath("Logs/fieldwork-stage-save.json"),d=>JsonUtility.ToJson(d),json=>JsonUtility.FromJson<BaseSaveDocument>(json),true);
                store.Save(state);var loaded=store.Load();var saved=BaseBattlefield.Board(loaded,"perimeter");
                Need(!saved.Cell(trap).HiddenTrap&&Enumerable.Range(0,3).All(x=>saved.Cell(new GridPosition(bag.X+x,bag.Z)).SandbagGroup==group),"存读后工事状态错误");
                Directory.CreateDirectory("Logs");File.WriteAllText("Logs/fieldwork-stage-verification.txt","PASS: defense stage construction, cost, projected battle visibility, consumed trap, retained sandbags and save/load.\n");Debug.Log("[VerifyFieldworkStage] PASS");
            }
            finally{view?.Dispose();UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(host);}
        }
        public static void RunFacade()
        {
            var board=BattleBoard.CreateDefault(8,8);var host=new GameObject("FacadeVerification");var source=new Material(Shader.Find("Universal Render Pipeline/Unlit"));BattleBoardView boardView=null;
            try
            {
                boardView=new BattleBoardView(board,host.transform,source,source){FlatView=true,ObliqueBaseView=true};boardView.SetCombatMode(false);boardView.UpdateCamera(0,0);
                var texture=PixelDemoArt.Building("barracks");Need(texture!=null,"缺少兵营素材");
                var facade=SpriteFacade.Create(boardView.Root.transform,texture,new Vector3(1,BaseTerrainView.LevelHeight,2),1,source,boardView.Camera);
                Need(Mathf.Abs(facade.Feet.y-BaseTerrainView.LevelHeight)<.001f,"探索视图建筑脚点高度错误");
                boardView.SetCombatMode(true);boardView.UpdateCamera(0,0);facade.Refresh();
                Need(Mathf.Abs(facade.Feet.y)<.001f&&Quaternion.Angle(facade.Surface.transform.rotation,boardView.Camera.transform.rotation)<.001f,"战斗视图建筑脚点或朝向未同步");
                boardView.SetCombatMode(false);boardView.UpdateCamera(0,0);facade.Refresh();
                Need(Mathf.Abs(facade.Feet.y-BaseTerrainView.LevelHeight)<.001f&&Quaternion.Angle(facade.Surface.transform.rotation,boardView.Camera.transform.rotation)<.001f,"探索视图建筑脚点或朝向未恢复");
                Directory.CreateDirectory("Logs");File.WriteAllText("Logs/facade-verification.txt","PASS: raised facade anchor and camera alignment across view switches.\n");Debug.Log("[VerifyFacade] PASS");
            }
            finally{boardView?.Dispose();UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(host);}
        }
        public static void RunRoadEdges()
        {
            foreach(var axis in new[]{RampAxis.EastWest,RampAxis.NorthSouth})
            {
                var board=BattleBoard.CreateDefault(8,8);var slope=new GridPosition(4,4);var cell=board.Cell(slope);
                cell.Surface=GroundSurface.Grass;cell.Elevation=1;cell.Ramp=axis;
                var road=board.Cell(axis==RampAxis.EastWest?new GridPosition(4,5):new GridPosition(5,4));road.Surface=GroundSurface.DirtPath;road.Elevation=1;
                string before=JsonUtility.ToJson(board);var host=new GameObject("RoadEdgeVerification");var source=new Material(Shader.Find("Universal Render Pipeline/Unlit"));BattleBoardView boardView=null;
                try
                {
                    boardView=new BattleBoardView(board,host.transform,source,source){FlatView=true,ObliqueBaseView=true};boardView.SetCombatMode(false);
                    var edge=EnvironmentRoadEdges.Build(new BaseState(),board,boardView.Root.transform,true);Need(edge!=null,"道路过渡层未生成");
                    var mesh=edge.GetComponent<MeshFilter>().sharedMesh;Need(mesh.vertexCount==4,"道路过渡夹具应只生成一片");
                    int high=axis==RampAxis.EastWest?3:1;
                    Need(Mathf.Abs(mesh.vertices[0].y-.035f)<.001f&&Mathf.Abs(mesh.vertices[high].y-(BaseTerrainView.LevelHeight+.035f))<.001f,"道路过渡层没有按坡道方向贴合两端");
                    var fog=boardView.Root.transform.Find("Unexplored_4_4");var cube=fog.GetComponent<MeshFilter>().sharedMesh;
                    var vision=new BattleVision(board);boardView.RefreshVision(vision,BattleSide.A,false);Need(mesh.GetTriangles(1).Length==0,"未知地形的道路过渡层不应透出迷雾");
                    var fogMesh=fog.GetComponent<MeshFilter>().sharedMesh;Need(Mathf.Abs(fogMesh.vertices[0].y)<.001f&&Mathf.Abs(fogMesh.vertices[high].y-BaseTerrainView.LevelHeight)<.001f,"坡道迷雾没有贴合高低端");
                    var known=new bool[board.Width*board.Height];known[slope.Z*board.Width+slope.X]=true;vision.RememberTerrain(BattleSide.A,known);
                    boardView.RefreshVision(vision,BattleSide.A,false);Need(mesh.GetTriangles(1).Length==6,"已知地形的道路过渡层未恢复");
                    boardView.SetCombatMode(true);Need(mesh.vertices.All(v=>Mathf.Abs(v.y-.035f)<.001f),"战斗平面视图的道路过渡层未降回地面");
                    var legacy=new BattleSimulation(new BattleScenario{Board=board,FogEnabled=true,HeightVision=false});
                    boardView.RefreshVision(legacy.Vision,BattleSide.A,false);Need(mesh.GetTriangles(1).Length==0,"普通迷雾未隐藏未知道路过渡层");
                    Need(fog.GetComponent<MeshFilter>().sharedMesh==cube,"从高度迷雾切换后普通迷雾未恢复立方体网格");
                    boardView.RefreshVision(legacy.Vision,BattleSide.A,false,true);Need(mesh.GetTriangles(1).Length==6,"显示全图未恢复道路过渡层");
                    boardView.SetCombatMode(false);Need(Mathf.Abs(mesh.vertices[high].y-(BaseTerrainView.LevelHeight+.035f))<.001f,"恢复探索后道路过渡层高度错误");
                    Need(JsonUtility.ToJson(board)==before,"道路过渡层改变了棋盘数据");
                }
                finally{boardView?.Dispose();UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(host);}
            }
            Directory.CreateDirectory("Logs");File.WriteAllText("Logs/road-edge-verification.txt","PASS: east-west and north-south ramp and fog heights, legacy fog mesh restore, edge visibility, view switching, board unchanged.\n");Debug.Log("[VerifyRoadEdges] PASS");
        }
        public static void RunInn()
        {
            var asset=Resources.Load<TextAsset>("DemoStages/camp-male");Need(asset!=null,"缺少营地阶段档");
            var state=JsonUtility.FromJson<BaseState>(asset.text);
            Need(BaseSpatial.Objects(state,"town").All(o=>o.Id!="town_inn"),"灰岩镇仍有住宿入口");
            var leader=state.Leaders.FirstOrDefault(l=>l.Id!=state.PlayerLeaderId);
            if(leader==null){leader=new BaseLeader{Id="legacy-guest",Name="旧档队长"};state.Leaders.Add(leader);}
            leader.Away=true;leader.PartyId="";state.Adventure.Members.Remove(leader.Id);
            state.Inn.Guests.Add(new BaseInnGuest{LeaderId=leader.Id,Debt=4});
            int coins=state.Inventory.Coins;bool rejected=false;
            try{BaseInn.ValidateRetired(state);}catch(ArgumentException){rejected=true;}
            Need(rejected&&state.Inn.Guests.Count==1&&leader.Away&&state.Inventory.Coins==coins,"已退役旅店存档仍被迁移");
            Debug.Log("[VerifyRetiredInn] PASS");
        }
        public static void RunCampPolish()
        {
            RunInn();
            var asset=Resources.Load<TextAsset>("DemoStages/camp-male");
            var state=JsonUtility.FromJson<BaseState>(asset.text);
            BaseEconomy.Validate(state);
            var camp=state.Campaign.Layouts.Single(m=>m.Region=="base");
            var keep=camp.Objects.Where(o=>!o.VisualOnly||o.Kind=="npc").Select(o=>o.Id).ToArray();
            camp.Objects.Add(new BaseMapPlacement{Id="test-decor",Y=2,Kind="decoration",VisualOnly=true});
            BaseEnvironment.ClearCampApproach(state);BaseEnvironment.ClearCampApproach(state);
            Need(camp.Objects.All(o=>o.Id!="test-decor")&&keep.All(id=>camp.Objects.Any(o=>o.Id==id)),"清理营地装饰影响设施或人物");

            var gift=new BaseLootReward{Template="novice_sword",Quality=2};
            const string giftId="verify:reward:bag";
            state.Campaign.SettledIds.Add(giftId);
            BaseBattleLoot.Create(state,new BaseCampaignReport{BattleId=giftId,Title="测试奖励"},new[]{gift});
            var bagLine=state.Aftermath.Loot.Single(m=>m.BattleId==giftId).Items.Single();
            Need(bagLine.Status=="taken"&&bagLine.ReceiptDestination==state.PlayerLeaderId,"空背包未直接收到奖励");
            int gearCount=state.PersonalEquipment.Warehouse.Count;
            BaseBattleLoot.Create(state,new BaseCampaignReport{BattleId=giftId,Title="测试奖励"},new[]{gift});
            Need(state.PersonalEquipment.Warehouse.Count==gearCount,"重复结算造成重复奖励");
            foreach(var holder in BaseAdventure.Members(state).Where(l=>BasePartyInventory.AccessibleLeader(state,l)))
                for(int cell=BasePartyInventory.FreeCell(state,holder.Id);cell>=0;cell=BasePartyInventory.FreeCell(state,holder.Id))
                    state.PersonalEquipment.Warehouse.Add(new BasePersonalItem{Id="personal:"+state.PersonalEquipment.NextId++,Template="novice_sword",Holder=holder.Id,Cell=cell});
            const string overflowId="verify:reward:warehouse";
            state.Campaign.SettledIds.Add(overflowId);
            BaseBattleLoot.Create(state,new BaseCampaignReport{BattleId=overflowId,Title="测试入库"},new[]{gift,new BaseLootReward{ItemId="forging_stone",Count=1}});
            var restored=JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(state));
            var receipt=BaseBattleLoot.ConsumeReceipt(restored,overflowId);
            Need(receipt.Items.All(i=>i.Status=="taken"&&i.ReceiptDestination==BaseBattleLoot.WarehouseReceipt)&&
                restored.PersonalEquipment.Warehouse.Any(i=>i.Holder==""&&i.Cell==-1&&i.Template=="novice_sword")&&
                restored.Backpack.Items.Any(i=>i.Holder==""&&i.Cell==-1&&i.ItemId=="forging_stone")&&
                !BaseBattleLoot.ConsumeReceipt(restored,overflowId).HasReceipt,"满包入库、存档收据或单次展示失败");
            Debug.Log("[VerifyCampPolish] PASS camp approach, reward destination, once-only delivery and receipt persistence");
            RunStairGeometry();
        }
        public static void RunStairGeometry()
        {
            foreach(var axis in new[]{RampAxis.EastWest,RampAxis.NorthSouth})foreach(int direction in new[]{-1,1})
            {
                var board=BattleBoard.CreateDefault(8,8);var point=new GridPosition(3,3);
                bool eastWest=axis==RampAxis.EastWest;
                var along=eastWest?new GridPosition(direction,0):new GridPosition(0,direction);
                var across=eastWest?new GridPosition(0,1):new GridPosition(1,0);
                var high=point+new GridPosition(-along.X,-along.Z);
                foreach(var p in new[]{point,point+across}){board.Cell(p).Elevation=1;board.Cell(p).Ramp=axis;}
                foreach(var p in new[]{high,high+across})board.Cell(p).Elevation=1;
                Need(board.RampLowEdge(point)==direction&&board.CanStep(point+along,point)&&board.CanStep(point,high),"台阶坡向或通行不一致");
                var mesh=BaseTerrainView.Ground(board,point,true);
                try
                {
                    var v=mesh.vertices;float Lateral(Vector3 x)=>eastWest?x.z:x.x;
                    float Progress(Vector3 x)=>(eastWest?x.x:x.z)*-direction;
                    var tops=mesh.GetTriangles(0).Select(i=>v[i]).ToArray();
                    Need(tops.Select(x=>x.y).Distinct().Count()>2&&Mathf.Abs(tops.OrderBy(Progress).First().y+.01f)<.001f&&
                        Mathf.Abs(tops.OrderByDescending(Progress).First().y-(BaseTerrainView.LevelHeight-.01f))<.001f,"台阶踏面未从正确低端接到高端");
                    var sides=mesh.GetTriangles(1);for(int i=0;i<sides.Length;i+=3)
                        Need(!Enumerable.Range(i,3).All(j=>Lateral(v[sides[j]])>BattleBoardView.CellSize*.49f),"并排台阶之间出现挡板");
                }
                finally{UnityEngine.Object.DestroyImmediate(mesh);}
            }
            Debug.Log("[VerifyStairGeometry] PASS both ramp axes, both directions, walkability and joined stair lanes");
        }
        public static void RunFarm()
        {
            var mature=PixelDemoArt.Building("farm-mature");var immature=PixelDemoArt.Building("farm-immature");
            Need(mature!=null&&immature!=null&&mature!=immature,"粮田 A/B 资源缺失或重复");
            var host=new GameObject("FarmArtVerification");var source=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            try
            {
                var placement=new BaseMapObject("farm","farm",3,3,3,2,true,"farm","farm","facility");
                var view=AuthoredMapView.Create(host.transform,placement,Vector3.up*BaseTerrainView.LevelHeight,source,true);
                var renderer=view.GetComponentInChildren<Renderer>();
                Need(renderer!=null&&renderer.sharedMaterial.GetTexture("_BaseMap")==mature,"粮田未使用成熟状态贴图");
                Need(renderer.sortingOrder< -30000,"粮田未处于直立角色和物件下方");
                view.SetExploration(false);Need(Mathf.Abs(view.transform.localPosition.y)<.001f,"战斗视图的高台粮田未降回平面");
                view.SetExploration(true);Need(Mathf.Abs(view.transform.localPosition.y-BaseTerrainView.LevelHeight)<.001f,"探索视图的高台粮田高度未恢复");
                Need(renderer.sortingOrder< -30000,"视图切换改变了粮田排序");
                var prop=new BaseMapObject("rock","rock",3,3,1,1,true,"rock","boulder","tactical-rock");
                var propView=AuthoredMapView.Create(host.transform,prop,Vector3.up*BaseTerrainView.LevelHeight,source,true);
                propView.SetExploration(false);Need(Mathf.Abs(propView.transform.localPosition.y)<.001f,"战斗视图的高台道具未降回平面");
                propView.SetExploration(true);Need(Mathf.Abs(propView.transform.localPosition.y-BaseTerrainView.LevelHeight)<.001f,"探索视图的高台道具高度未恢复");
                var stage=Resources.Load<TextAsset>("DemoStages/camp-male");Need(stage!=null,"缺少营地阶段档");
                var state=JsonUtility.FromJson<BaseState>(stage.text);
                state.Workers.Add(new BaseWorkerBucket{Kind=BaseTroopKind.Melee,Count=1,Health=100,MaxHealth=100,FacilityId="farm",DesiredFacility="farm",PostLocked=true});
                foreach(var item in new[]{(rate:1d,sign:-1,texture:immature),(rate:3d,sign:0,texture:mature),(rate:4d,sign:1,texture:mature),(rate:1d,sign:-1,texture:immature)})
                {
                    state.Rules.FarmPerWorker=item.rate;string before=JsonUtility.ToJson(state);
                    Need(Math.Sign(BaseDailyDelta.Read(state).Food)==item.sign,"粮田夹具未形成目标净增");
                    view.RefreshFarmAppearance(state);Need(renderer.sharedMaterial.GetTexture("_BaseMap")==item.texture,"粮食净增跨零后粮田贴图未正确切换");
                    Need(JsonUtility.ToJson(state)==before,"粮田贴图刷新改变了经营状态");
                }
                Directory.CreateDirectory("Logs");File.WriteAllText("Logs/farm-art-verification.txt","PASS: A/B textures, negative/zero/positive refresh, farm and prop view heights, ground sorting, state unchanged.\n");
                Debug.Log("[VerifyFarmArt] PASS");
            }
            finally{UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(source);}
        }
        public static void Run()
        {
            foreach(string id in new[]{"trap-bear","sandbag-row-3"})
            {
                string path="Assets/Resources/PixelDemo/Buildings/"+id+"-v1.png";
                var importer=AssetImporter.GetAtPath(path) as TextureImporter;Need(importer!=null,"缺少工事贴图："+id);
                Need(importer.filterMode==FilterMode.Point&&!importer.mipmapEnabled&&importer.textureCompression==TextureImporterCompression.Uncompressed&&importer.npotScale==TextureImporterNPOTScale.None,"工事贴图导入设置不符合规格："+id);
            }
            var board=BattleBoard.CreateDefault(12,10);board.Cell(new GridPosition(2,2)).HiddenTrap=true;
            for(int x=4;x<=6;x++)board.Cell(new GridPosition(x,4)).SandbagGroup=1;
            string before=JsonUtility.ToJson(board);var host=new GameObject("FieldworkArtVerification");var source=new Material(Shader.Find("Universal Render Pipeline/Unlit"));BattleBoardView view=null;
            try
            {
                view=new BattleBoardView(board,host.transform,source,source){FlatView=true,ObliqueBaseView=true};view.SetCombatMode(false);
                var trap=view.Root.transform.Find("FieldworkArt/Fieldwork_2_2");Need(trap!=null&&trap.gameObject.activeSelf,"和平状态陷阱未显示");
                for(int x=4;x<=6;x++){var part=view.Root.transform.Find("FieldworkArt/Fieldwork_"+x+"_4");Need(part!=null&&part.gameObject.activeSelf,"沙袋分片缺失");var uv=part.GetComponent<MeshFilter>().sharedMesh.uv;Need(Mathf.Abs(uv[0].x-(x-4)/3f)<.0001f&&Mathf.Abs(uv[2].x-(x-3)/3f)<.0001f,"沙袋纹理分片错误");}
                view.SetCombatMode(true);Need(!trap.gameObject.activeSelf,"战斗中陷阱不应显示");Need(view.Root.transform.Find("FieldworkArt/Fieldwork_4_4").gameObject.activeSelf,"战斗中沙袋应显示");
                var bag=view.Root.transform.Find("FieldworkArt/Fieldwork_4_4").GetComponent<Renderer>();
                var legacy=new BattleSimulation(new BattleScenario{Board=board,FogEnabled=true,HeightVision=false});
                view.RefreshVision(legacy.Vision,BattleSide.A,false);Need(!bag.enabled,"未发现区域的沙袋不应透出迷雾");
                view.RefreshVision(legacy.Vision,BattleSide.A,false,true);Need(bag.enabled,"显示全部地图时沙袋应显示");
                var heightVision=new BattleVision(board);view.RefreshVision(heightVision,BattleSide.A,false);Need(!bag.enabled,"高度迷雾下未知沙袋不应显示");
                var known=new bool[board.Width*board.Height];known[4*board.Width+4]=true;heightVision.RememberTerrain(BattleSide.A,known);
                view.RefreshVision(heightVision,BattleSide.A,false);Need(bag.enabled,"已知地形的沙袋应受迷雾正常覆盖");
                board.Cell(new GridPosition(2,2)).HiddenTrap=false;view.SetCombatMode(false);Need(!trap.gameObject.activeSelf,"消耗后陷阱仍显示");board.Cell(new GridPosition(2,2)).HiddenTrap=true;
                Need(bag.enabled,"退出战斗后工事显示没有恢复");
                for(int x=4;x<=6;x++)board.Cell(new GridPosition(x,4)).SandbagGroup=0;view.Refresh();Need(!view.Root.transform.Find("FieldworkArt/Fieldwork_4_4").gameObject.activeSelf,"拆除后沙袋仍显示");
                for(int x=4;x<=6;x++)board.Cell(new GridPosition(x,4)).SandbagGroup=1;Need(JsonUtility.ToJson(board)==before,"表现改变了棋盘数据");
                Directory.CreateDirectory("Logs");File.WriteAllText("Logs/fieldwork-art-verification.txt","PASS: point/no mip, trap peace/combat/consumed, three-segment sandbag, fog visibility, combat, demolition, board unchanged.\n");Debug.Log("[VerifyFieldworkArt] PASS");
            }
            finally{view?.Dispose();UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(host);}
        }
    }
}
