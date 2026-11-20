#if UNITY_EDITOR || LAN_DUEL_QA
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BaseConquest.Battle;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BaseConquest.Base
{
    // Included on device only in the separately identified interop-test APK.
    public sealed class LanDuelDeviceCheck : MonoBehaviour
    {
        private string role, address, mode, phase, saveBefore, stateBefore;
        private double started, pauseAt, finishAt;
        private float firstVoteSeconds, frozenSeconds;
        private bool sentVote, resumed, sawUnilateral, sawPaused, sawResume, ordered, finished, checkedScout;
        private bool sawRider;
        private bool sawMageCast, sawMageFlight, sawMageImpact, sawHealthChange;
        private int frozenTick, lastTarget = -1, nextReport;
        private BaseSceneController scene;
        private LanDuelScreen screen;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Launch()
        {
            if (FindAnyObjectByType<LanDuelDeviceCheck>() != null) return;
            string role, address, mode;
#if UNITY_EDITOR
            role = Environment.GetEnvironmentVariable("DEMO_DUEL_ROLE");
            address = Environment.GetEnvironmentVariable("DEMO_DUEL_ADDRESS");
            mode = Environment.GetEnvironmentVariable("DEMO_DUEL_CASE");
            if (!Application.isBatchMode || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BASE_CONQUEST_UI_CHECK_SAVES"))) return;
#elif UNITY_ANDROID
            if (Application.identifier != "com.baseconquest.lanqa") return;
            using var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = unity.GetStatic<AndroidJavaObject>("currentActivity");
            using var intent = activity.Call<AndroidJavaObject>("getIntent");
            role = intent.Call<string>("getStringExtra","lanDuelRole");
            address = intent.Call<string>("getStringExtra","lanDuelAddress");
            mode = intent.Call<string>("getStringExtra","lanDuelCase");
#else
            return;
#endif
#if UNITY_EDITOR || UNITY_ANDROID
            if (role != "host" && role != "guest") return;
            var go = new GameObject("DeviceDuelAcceptance");
            DontDestroyOnLoad(go);
            var check = go.AddComponent<LanDuelDeviceCheck>();
            check.role = role; check.address = address; check.mode = mode; check.phase = "fixture";
            check.started = Time.realtimeSinceStartupAsDouble;
            Debug.Log("[DuelQA] starting " + role);
#endif
        }

        private void Update()
        {
            if(finished || phase==null) return;
            try { Step(); } catch(Exception e){ Finish(false, phase+" "+e); }
        }

        private void Step()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if(now-started > (phase == "battle" ? 420 : 240)) throw new TimeoutException(phase + " scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name +
                " cover=" + (FindAnyObjectByType<SceneLoadingCover>()!=null) + " screen=" + screen?.Phase + " tick=" + screen?.Frame?.tick);
            if(phase=="fixture")
            {
                scene=FindAnyObjectByType<BaseSceneController>();
                if(scene?.Hud==null || scene.Busy || FindAnyObjectByType<SceneLoadingCover>()!=null) return;
                var fixture=JsonUtility.FromJson<BaseState>(Resources.Load<TextAsset>("DemoStages/defense-male").text);
                if (mode == "mage")
                {
                    var rules = fixture.Demo.Rpg;
                    var worker = fixture.Workers.First(w => w.Count > 0 && !w.Away && !w.VillageResident);
                    worker.Kind = BaseTroopKind.Mage;
                    var mage = new RpgCharacter { Profession = 3, TemplateId = rules.DefaultTemplateId("soldier", 3) };
                    worker.CombatStats = rules.Bare(mage, 1);
                    worker.Health = worker.MaxHealth = worker.CombatStats.Health;
                    worker.HasCombatProfile = true;
                }
                BaseEconomy.Validate(fixture);
                if (fixture.Campaign.Location == "perimeter")
                {
                    var gate = BaseSpatial.Objects(fixture).Single(o => o.Id == "demo_defense_return");
                    fixture.PlayerX = gate.X; fixture.PlayerY = gate.Y;
                    BaseAdventure.Arrive(fixture);
                    if (!BaseDemoRuins.Travel(fixture, "base"))
                        throw new InvalidOperationException(BaseDemoRuins.TravelProblem(fixture, "base"));
                }
                var door = BaseStageWalk.Door(fixture, "barracks");
                fixture.PlayerX = door.X;
                fixture.PlayerY = door.Y;
                // This asymmetric fixture makes an enemy visible to a single ordinary scout.
                if (mode == "scout" && role == "host")
                    foreach (var worker in fixture.Workers) worker.CombatStats.SightRadius = 18;
                BaseAdventure.Arrive(fixture);
                BaseBattleGateway.Catalog().SaveAuto(fixture,"演武验收");
                BaseBattleGateway.Session().Restore(fixture);
                BaseBattleGateway.MainMenu=false;
                phase="camp"; SceneLoadingCover.LoadExploration(); return;
            }
            if(phase=="camp")
            {
                scene=FindAnyObjectByType<BaseSceneController>();
                if(scene?.State==null || scene.Busy || FindAnyObjectByType<SceneLoadingCover>()!=null) return;
                if(scene.State.Campaign.Location=="perimeter") {scene.TravelDemo("base");return;}
                if(scene.State.Campaign.Location!="base") return;
                var barracks = BaseSpatial.Objects(scene.State).Single(o => o.Id == "barracks");
                BaseInteraction.Walk(new BasePoint(barracks.X - scene.State.PlayerX, barracks.Y - scene.State.PlayerY));
                scene.Hud.OpenObject("barracks");
                var entry = Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None)
                    .FirstOrDefault(b => b.IsInteractable() && b.GetComponentInChildren<UnityEngine.UI.Text>()?.text == "模拟对抗");
                if (entry == null) throw new InvalidOperationException("兵营中没有模拟对抗按钮");
                stateBefore=JsonUtility.ToJson(scene.State);
                saveBefore=DiskDigest();
                entry.onClick.Invoke();
                screen=scene.DuelScreen;
                if(screen==null) throw new InvalidOperationException("兵营入口未打开");
                if(role=="host") screen.HostRoom(); else if(address!="discover") screen.JoinRoom(address);
                phase=address=="discover" && role=="guest" ? "discover" : "connected"; Debug.Log("[DuelQA] room "+role); return;
            }
            if(phase=="discover")
            {
                var room=screen.GetComponentsInChildren<UnityEngine.UI.Button>().FirstOrDefault(b=>b.name=="JoinRoom" && b.IsInteractable());
                if(room==null) return;
                Debug.Log("[DuelQA] joined discovered room: "+room.GetComponentInChildren<UnityEngine.UI.Text>().text);
                room.onClick.Invoke(); phase="connected"; return;
            }
            if(screen.Phase=="disconnected")
            {
                if(mode=="drop" && role=="host" && sawResume){Complete("peer disconnect"); return;}
                throw new InvalidOperationException("unexpected disconnect at "+phase);
            }
            if(phase=="connected" && screen.Phase=="preparing")
            {
                var draft=screen.Draft;
                var soldier=draft.Units.First(u=>mode=="mage" ? u.Kind==SoldierKind.Mage : mode=="cavalry" ? u.Kind==SoldierKind.Cavalry : u.Kind!=SoldierKind.Hero);
                foreach(var unit in draft.Units) unit.Selected=unit.Id==draft.PlayerId || unit==soldier;
                soldier.TeamId=draft.PlayerId;draft.SupplyGrade=0;
                screen.Ready(); if(screen.Phase=="preparing") throw new InvalidOperationException("备战被拒"); started=now;phase="battle";Debug.Log("[DuelQA] ready");return;
            }
            if(phase!="battle")return;
            var f=screen.Frame;
            if(f==null)return;
            if (f.seconds >= nextReport)
            {
                nextReport += 10;
                Debug.Log("[DuelQA] battle seconds=" + f.seconds + " units=" + string.Join(",",f.units.Select(u=>u.name+"="+(int)u.hp)));
            }
            if(mode=="cavalry")
            {
                foreach(var rider in f.units.Where(u=>u.kind==(int)SoldierKind.Cavalry))
                {
                    if(rider.footprintCells!=1 || rider.attackRangeFine!=2)
                        throw new InvalidOperationException("骑兵同步占地或攻击距离错误");
                    if(rider.side!=screen.LocalSide) sawRider=true;
                }
            }
            if (mode == "mage")
            {
                sawMageCast |= f.units.Any(u => u.kind == (int)SoldierKind.Mage && u.appearance == "mage" && u.attackPose >= 0);
                sawMageFlight |= f.spells != null && f.spells.Any(s => !s.impact);
                sawMageImpact |= f.spells != null && f.spells.Any(s => s.impact);
                sawHealthChange |= f.units.Any(u => u.hp < u.maxHp);
            }
            if(f.result!=0 && (f.aDeployed!=2 || f.bDeployed!=2))throw new InvalidOperationException("出战单位数量错误");
            if(screen.Phase=="result")
            {
                if(mode=="cavalry"&&!sawRider)throw new InvalidOperationException("未观测到对方骑兵");
                if (mode == "mage" && !(sawMageCast && sawMageFlight && sawMageImpact && sawHealthChange))
                    throw new InvalidOperationException($"法师联机观测 cast={sawMageCast} flight={sawMageFlight} hit={sawMageImpact} hp={sawHealthChange}");
                if(!ordered||!sawPaused||!sawResume||!sawUnilateral) throw new InvalidOperationException("未覆盖暂停/指挥");
                if(finishAt==0){finishAt=now;Debug.Log("[DuelQA] result "+f.result+" tick "+f.tick+" survivors "+f.aStanding+"/"+f.bStanding);}
                if(now-finishAt>1.5)
                {
                    string observed=mode=="cavalry" ? " cavalry cells=1 rangeFine=2 seenOpponent="+sawRider :
                        mode=="mage" ? $" mage cast={sawMageCast} flight={sawMageFlight} impact={sawMageImpact} hpChanged={sawHealthChange}" : "";
                    Complete("result="+f.result+" tick="+f.tick+" survivors="+f.aStanding+"/"+f.bStanding+observed);
                }
                return;
            }
            if(screen.Phase!="fighting")return;
            var leader=f.units.FirstOrDefault(u=>u.side==screen.LocalSide&&u.kind==(int)SoldierKind.Hero && u.hp>0);
            if (mode == "scout" && !checkedScout)
            {
                var scout = f.units.Single(u=>u.side==screen.LocalSide && u.kind!=(int)SoldierKind.Hero);
                var foes = f.units.Where(u=>u.side!=screen.LocalSide).ToArray();
                if (leader == null || leader.sightRadius != 6 || scout.sightRadius != (role == "host" ? 18 : 6))
                    throw new InvalidOperationException("独立士兵视野未随编队传入战场");
                if (role == "host" && !foes.Any(e => Math.Abs(e.x-leader.x)>leader.sightRadius &&
                    f.visibleCells[(int)e.z*f.width+(int)e.x]) || role == "guest" && foes.Length != 0)
                    throw new InvalidOperationException("双方收到的敌情与各自士兵的侦察范围不符");
                checkedScout = true;
                Debug.Log($"[DuelQA] scout sight={scout.sightRadius} captain={leader.sightRadius} spotted={foes.Length}");
            }
            if(!ordered && leader!=null){screen.Order("move",leader.id,-1,f.width*.5f,f.height*.5f);ordered=true;}
            if(!sentVote&&role=="host"&&f.seconds>1f){screen.VotePause();sentVote=true;}
            if(f.voteA&&!f.voteB&&!f.paused)
            {
                if(firstVoteSeconds==0)firstVoteSeconds=f.seconds;
                if(f.seconds-firstVoteSeconds>.6f)sawUnilateral=true;
                if(sawUnilateral && role=="guest" && !sentVote && f.seconds-firstVoteSeconds>1.3f)
                {
                    if (mode == "touch") RequestTouch("pause"); else screen.VotePause();
                    sentVote=true;
                }
            }
            if(f.paused)
            {
                if(!sawPaused){sawPaused=true;pauseAt=now;frozenTick=f.tick;frozenSeconds=f.seconds;Debug.Log("[DuelQA] frozen "+frozenTick);}
                if(Math.Abs(f.seconds-frozenSeconds)>.001f)throw new InvalidOperationException("暂停时战斗仍在推进");
                if(role=="guest"&&!resumed&&now-pauseAt>1.4)
                {
                    if (mode == "touch") RequestTouch("resume"); else screen.Resume();
                    resumed=true;
                }
                return;
            }
            if(!sawPaused || f.seconds<=frozenSeconds) return;
            sawResume=true;
            if(mode=="drop" && role=="guest"){Complete("deliberate leave");return;}
            if(leader!=null)
            {
                var enemy=f.units.FirstOrDefault(u=>u.side!=screen.LocalSide&&u.hp>0);
                if(enemy!=null && enemy.id!=lastTarget){lastTarget=enemy.id;screen.Order("attack",leader.id,enemy.id,enemy.x,enemy.z);screen.Order("skill",leader.id,enemy.id,enemy.x,enemy.z);}
            }
        }

        private void RequestTouch(string action)
        {
            var button=screen.GetComponentsInChildren<UnityEngine.UI.Button>().Single(b=>b.name=="Pause");
            var rect=button.GetComponent<RectTransform>();
            var point=RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            Debug.Log(FormattableString.Invariant($"[DuelQA] tap {action} {point.x / Screen.width:F4} {1 - point.y / Screen.height:F4}"));
        }

        private static string DiskDigest()
        {
            var dir=BaseBattleGateway.SaveDirectory;
            using var hash=SHA256.Create();
            return string.Join("|",Directory.GetFiles(dir,"*.json*").OrderBy(f=>f,StringComparer.Ordinal).Select(f=>Path.GetFileName(f)+":"+Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(f)))));
        }
        private void Complete(string summary)
        {
            screen.Leave();
            if(stateBefore!=JsonUtility.ToJson(scene.State)||saveBefore!=DiskDigest())
                throw new InvalidOperationException("对局改写了本地营地或磁盘存档");
            Finish(true,summary+" campaign unchanged");
        }
        private void Finish(bool passed,string text)
        {
            finished=true; Debug.Log("[DuelQA] "+(passed?"PASS ":"FAIL ")+role+" "+text);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(passed?0:2);
#endif
        }
    }
}
#endif
