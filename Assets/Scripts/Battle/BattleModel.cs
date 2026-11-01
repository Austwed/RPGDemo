using System;
using System.Collections.Generic;

namespace BaseConquest.Battle
{
    public enum BattleSide { A, B }
    public enum SoldierKind { Melee = 0, Ranged = 1, Hero = 2, Cavalry = 3 }
    public enum SquadCommand { Auto, Move, Focus, Retreat, Hold }
    public enum SquadStrategy { Attack, Protect, Flee, Obey }
    public enum BattlePhase { Deployment, Fighting, Finished }
    public enum BattleOutcome { None, SideAWin, SideBWin, Draw, Retreat, Stalemate, RoundLimit }
    public enum TerrainKind { Ground = 0, Water = 1, Wall = 2, Swamp = 3, Building = 4 }
    public enum RampAxis { None = 0, EastWest = 1, NorthSouth = 2 }
    public enum GroundSurface { Default = 0, Grass = 1, Sand = 2, Road = 3, Cobble = 4, DirtPath = 5 }
    public enum DeploymentZone { None, A, B }
    public enum RoundAction { Wait, Move, Attack, Skill, Escape, Rescue }
    public enum BoardBrush { ZoneA = 0, ZoneB = 1, ClearZone = 2, Ground = 3, Water = 4, Wall = 5, Swamp = 6, Building = 7, Height0 = 8, Height1 = 9, Height2 = 10, Height3 = 11, RampEastWest = 12, RampNorthSouth = 13, ClearRamp = 14 }

    [Serializable]
    public struct GridPosition : IEquatable<GridPosition>
    {
        public int X, Z;
        public GridPosition(int x, int z) { X = x; Z = z; }
        public bool Equals(GridPosition other) => X == other.X && Z == other.Z;
        public override bool Equals(object obj) => obj is GridPosition p && Equals(p);
        public override int GetHashCode() => X * 397 ^ Z;
        public static bool operator ==(GridPosition a, GridPosition b) => a.Equals(b);
        public static bool operator !=(GridPosition a, GridPosition b) => !a.Equals(b);
        public static GridPosition operator +(GridPosition a, GridPosition b) => new GridPosition(a.X + b.X, a.Z + b.Z);
        public static int Distance(GridPosition a, GridPosition b) => Math.Abs(a.X - b.X) + Math.Abs(a.Z - b.Z);
        public override string ToString() => "(" + X + ", " + Z + ")";
    }

    [Serializable]
    public sealed class BoardCell
    {
        public bool HasSightHeight;
        public double SightHeight;
        public TerrainKind Terrain;
        public int Elevation;
        public GroundSurface Surface;
        public RampAxis Ramp;
        public string BuildingId = "";
        public bool HasDeveloperTerrain;
        public TerrainKind OriginalTerrain;
        public DeploymentZone OriginalZone;
        public string OriginalBuildingId = "";
        public float ObstacleHealth;
        public BattleSide ObstacleOwner;
        public bool FixedCover;
        // Zero means no sandbag; positive values identify all three cells in one group.
        public int SandbagGroup;
        public bool HiddenTrap;
        public bool BlocksShots => ObstacleHealth>0 || Terrain == TerrainKind.Wall || Terrain == TerrainKind.Building;
        public bool MetadataValid => (!HasSightHeight||!double.IsNaN(SightHeight)&&!double.IsInfinity(SightHeight)&&SightHeight>=0&&SightHeight<=3)&&Enum.IsDefined(typeof(GroundSurface),Surface)&&Elevation>=0&&Elevation<=3&&Enum.IsDefined(typeof(RampAxis),Ramp)&&!float.IsNaN(ObstacleHealth)&&!float.IsInfinity(ObstacleHealth)&&ObstacleHealth>=0&&ObstacleHealth<=60&&Enum.IsDefined(typeof(BattleSide),ObstacleOwner)&&(ObstacleHealth==0||Zone==DeploymentZone.None&&(Terrain==TerrainKind.Ground||HasDeveloperTerrain))&&(BuildingId==null||BuildingId.Length<=64)&&(OriginalBuildingId==null||OriginalBuildingId.Length<=64)&&(!HasDeveloperTerrain||Enum.IsDefined(typeof(TerrainKind),OriginalTerrain)&&Enum.IsDefined(typeof(DeploymentZone),OriginalZone)&&((OriginalTerrain==TerrainKind.Ground||OriginalTerrain==TerrainKind.Swamp)||OriginalZone==DeploymentZone.None));
        public DeploymentZone Zone;
        public bool Walkable => ObstacleHealth<=0 && (Terrain == TerrainKind.Ground || Terrain == TerrainKind.Swamp);
        public int FootworkCost => Terrain == TerrainKind.Swamp || SandbagGroup>0 ? 2 : 1;
        public BoardCell Clone() => (BoardCell)MemberwiseClone();
    }

    [Serializable]
    public sealed class SoldierStats
    {
        public int Level = 1;
        public float Health = 100;
        public float Damage = 12;
        public float Defense;
        public float Penetration, Resistance;
        public bool HasCombatKind;
        public SoldierKind CombatKind;
        public bool HasProfessionKind;
        public SoldierKind ProfessionKind;
        public bool EquipmentStatsValid => Level>=1&&Level<=9&&!float.IsNaN(Penetration)&&!float.IsInfinity(Penetration)&&Penetration>=0&&Penetration<=100000&&!float.IsNaN(Resistance)&&!float.IsInfinity(Resistance)&&Resistance>=0&&Resistance<=100000&&(!HasCombatKind||CombatKind==SoldierKind.Melee||CombatKind==SoldierKind.Ranged||CombatKind==SoldierKind.Cavalry)&&(!HasProfessionKind||ProfessionKind==SoldierKind.Melee||ProfessionKind==SoldierKind.Ranged||ProfessionKind==SoldierKind.Cavalry);
        public int Range = 1;
        public int MoveDistance = 1;
        public float CritChance = 0.1f;
        public float CritMultiplier = 1.5f;
        public float BaseBattlePower => CombatNumbers.Power(this);
        public float ExpectedDamage => Damage * (1 + CritChance * (CritMultiplier - 1));
        public bool PersonalProfileValid => EquipmentStatsValid&&Finite(Health)&&Health>=1&&Health<=100000&&Finite(Damage)&&Damage>=0&&Finite(Defense)&&Defense>=0&&Range>=1&&Range<=12&&MoveDistance>=1&&MoveDistance<=12&&Finite(CritChance)&&CritChance>=0&&CritChance<=1&&Finite(CritMultiplier)&&CritMultiplier>=1&&CritMultiplier<=10;
        private static bool Finite(float n)=>!float.IsNaN(n)&&!float.IsInfinity(n);
        // 2026-11-01 用户裁决：删除破甲／抵抗收益。字段保留仅为存档兼容，不再参与任何结算。
        public float EffectiveDefense(SoldierStats target) => Math.Max(0,target.Defense);
        public float ExpectedDamageAgainst(SoldierStats target) => CombatNumbers.Hit(this,target,ExpectedDamage);
        public SoldierStats Clone() => new SoldierStats
        {
            Health = Math.Max(1, Health), Damage = Math.Max(0, Damage), Defense = Math.Max(0, Defense), Range = Math.Max(1, Math.Min(12, Range)),
            MoveDistance = Math.Max(1, Math.Min(12, MoveDistance)), CritChance = Math.Max(0, Math.Min(1, CritChance)), CritMultiplier = Math.Max(1, Math.Min(10, CritMultiplier)),
            Level=Level,Penetration=Penetration,Resistance=Resistance,HasCombatKind=HasCombatKind,CombatKind=CombatKind,HasProfessionKind=HasProfessionKind,ProfessionKind=ProfessionKind
        };
    }

    public static class CombatNumbers
    {
        public const float DefenseK=40, MaximumMitigation=.8f;
        public static float ArmorFactor(SoldierStats attacker,SoldierStats defender)
        {
            if(attacker==null||defender==null||!attacker.EquipmentStatsValid||!defender.EquipmentStatsValid||float.IsNaN(defender.Defense)||float.IsInfinity(defender.Defense)||defender.Defense<0)throw new ArgumentException("伤害等级／防御属性非法");
            float defense=attacker.EffectiveDefense(defender);
            return 1-MaximumMitigation*defense/(DefenseK*attacker.Level+defense);
        }
        public static float Hit(SoldierStats attacker,SoldierStats defender,float attack,bool critical=false)
        {
            if(float.IsNaN(attack)||float.IsInfinity(attack)||attack<0)throw new ArgumentException("攻击数值非法");
            return attack*ArmorFactor(attacker,defender)*(critical?attacker.CritMultiplier:1);
        }
        public static float Power(SoldierStats stats)
        {
            var reference=new SoldierStats {Health=1000,Damage=100,Defense=20,CritChance=.05f,CritMultiplier=1.5f};
            return (float)Math.Sqrt(stats.Health/ArmorFactor(reference,stats)*stats.ExpectedDamageAgainst(reference)/(1000*reference.ExpectedDamage));
        }
    }

    [Serializable]
    public sealed class ArmyDefinition
    {
        public int MeleeCount = 6;
        public int RangedCount = 4;
        public int HeroCount = 3;
        public int CavalryCount = 2;
        public int CaptainSlot;
        public int Capacity = 6;
        public SoldierStats Melee = new SoldierStats();
        public SoldierStats Ranged = new SoldierStats { Health = 65, Damage = 10, Range = 5 };
        public SoldierStats Hero = new SoldierStats { Health = 200, Damage = 20, Range = 1 };
        public SoldierStats Cavalry = new SoldierStats { Health = 120, Damage = 14, Range = 1, MoveDistance = 3 };
        public List<CommanderLoadout> Commanders = new List<CommanderLoadout>();
        public List<string> CommanderNames = new List<string>();
        public List<string> CommanderAppearances = new List<string>();
        public List<bool> CommanderRecruited = new List<bool>();
        public List<SoldierStats> CommanderStats = new List<SoldierStats>();
        public SoldierStats CommanderProfile(int slot)=>CommanderStats!=null&&slot>=0&&slot<CommanderStats.Count ? CommanderStats[slot] : Hero;
        public int Count(SoldierKind kind) => kind == SoldierKind.Melee ? MeleeCount : kind == SoldierKind.Ranged ? RangedCount : kind == SoldierKind.Cavalry ? CavalryCount : HeroCount;
        public void SetCount(SoldierKind kind, int count) { if (kind == SoldierKind.Melee) MeleeCount = count; else if (kind == SoldierKind.Ranged) RangedCount = count; else if (kind == SoldierKind.Cavalry) CavalryCount = count; else HeroCount = count; }
        public SoldierStats Stats(SoldierKind kind) => kind == SoldierKind.Melee ? Melee : kind == SoldierKind.Ranged ? Ranged : kind == SoldierKind.Cavalry ? Cavalry : Hero;
        public ArmyDefinition Clone() => new ArmyDefinition
        {
            MeleeCount = Math.Max(0, Math.Min(30, MeleeCount)), RangedCount = Math.Max(0, Math.Min(30, RangedCount)), HeroCount = Math.Max(0, Math.Min(3, HeroCount)),
            CavalryCount = Math.Max(0, Math.Min(30, CavalryCount)),
            Melee = (Melee ?? new SoldierStats()).Clone(), Ranged = (Ranged ?? new SoldierStats()).Clone(), Hero = (Hero ?? new SoldierStats()).Clone(),
            Cavalry = (Cavalry ?? new SoldierStats { Health = 120, Damage = 14, MoveDistance = 3 }).Clone(),
            Commanders = Commanders == null ? new List<CommanderLoadout>() : Commanders.ConvertAll(c => (c ?? new CommanderLoadout()).Clone()),
            CommanderNames = new List<string>(CommanderNames??new List<string>()),
            CommanderAppearances = new List<string>(CommanderAppearances??new List<string>()),
            CommanderRecruited = new List<bool>(CommanderRecruited??new List<bool>()),
            CommanderStats = CommanderStats==null ? new List<SoldierStats>() : CommanderStats.ConvertAll(p=>p?.Clone()),
            CaptainSlot = Math.Max(0, Math.Min(Math.Max(0, HeroCount - 1), CaptainSlot)), Capacity = Math.Max(1, Math.Min(90, Capacity))
        };
    }

    [Serializable]
    public sealed class UnitPlacement
    {
        public BattleSide Side;
        public SoldierKind Kind;
        public GridPosition Cell;
        public int CommanderSlot = -1;
        public UnitPlacement Clone() => new UnitPlacement { Side = Side, Kind = Kind, Cell = Cell, CommanderSlot = CommanderSlot };
    }

    // 已实现歼灭与坚守；未知类型必须显式拒载。
    public static class ObjectiveKinds
    {
        public const string Annihilation = "annihilation";
        public const string Hold = "hold";
        public static bool Implemented(string kind) => kind == Annihilation || kind == Hold;
    }

    [Serializable]
    public sealed class ObjectiveConfig
    {
        // 目标类型：默认歼灭，保持旧JSON缺省兼容。
        public string Kind = ObjectiveKinds.Annihilation;
        // 可选短标题，供顶部目标面板显示；留空则用默认文案。
        public string Title = "";
        public int HoldRounds = 30;
        public ObjectiveConfig Clone() => new ObjectiveConfig { Kind = Kind ?? ObjectiveKinds.Annihilation, Title = Title ?? "", HoldRounds = HoldRounds };
    }

    [Serializable]
    public sealed class BattleScenario
    {
        public string SupplyBattleId = "";
        public BattleEffectRules Effects = new BattleEffectRules();
        public BattleBoard Board = BattleBoard.CreateDefault();
        public ArmyDefinition A = new ArmyDefinition();
        public ArmyDefinition B = new ArmyDefinition(); // 具体测试默认值以Stage1BattleSettings为准。
        public List<UnitPlacement> Placements = new List<UnitPlacement>();
        // 可选目标配置；旧JSON缺省时视为歼灭，见 ObjectiveConfig。
        public ObjectiveConfig Objective = new ObjectiveConfig();
        public int MaxRounds = 200;
        public int StalemateRounds = 20;
        public int SkillRange = 5;
        public int SkillRadius = 2;
        public float SkillDamage = 35;
        public int SkillCooldown = 3;
        public int RandomSeed = 12345;
        public bool FogEnabled = true;
        public int DefenderSide = -1;
        public bool DefenderVisionEnabled, CampaignRules;
        public int PlayerLeaderSlot = -1;
        public bool PlayerHeroAppearance;
        public bool CustomExits, InstantDeathEnabled;
        public GridPosition ExitA, ExitB;
        public double LeaderInstantDeath = 0.05, TroopInstantDeath = 0.65;
        public int VisionRadius = 6;
        public bool HeightVision;
        // false仅供旧规则夹具回归；实际场景默认只开放小队长指挥。
        public bool CommanderControl = true;
        public bool MobileBattleRules = true;
        public bool ShowEnemiesThroughFog = true;
        public bool TestAutoDeployment = true;
        public ArmyDefinition Army(BattleSide side) => side == BattleSide.A ? A : B;
        public BattleScenario Clone()
        {
            var copy = new BattleScenario { Effects = (Effects ?? new BattleEffectRules()).Clone(), Board = (Board ?? BattleBoard.CreateDefault()).Clone(), A = (A ?? new ArmyDefinition()).Clone(), B = (B ?? new ArmyDefinition()).Clone(), MaxRounds = Math.Max(1, MaxRounds), StalemateRounds = Math.Max(1, StalemateRounds), SkillRange = Math.Max(1, SkillRange), SkillRadius = Math.Max(0, SkillRadius), SkillDamage = Math.Max(0, SkillDamage), SkillCooldown = Math.Max(1, SkillCooldown), RandomSeed = RandomSeed, HeightVision=HeightVision, FogEnabled = FogEnabled, VisionRadius = Math.Max(1, Math.Min(32, VisionRadius)) };
            copy.CommanderControl = CommanderControl;
            copy.DefenderSide = DefenderSide; copy.CustomExits = CustomExits; copy.ExitA = ExitA; copy.ExitB = ExitB;
            copy.PlayerLeaderSlot = PlayerLeaderSlot;
            copy.PlayerHeroAppearance = PlayerHeroAppearance;
            copy.DefenderVisionEnabled = DefenderVisionEnabled; copy.CampaignRules = CampaignRules;
            copy.InstantDeathEnabled = InstantDeathEnabled; copy.LeaderInstantDeath = LeaderInstantDeath; copy.TroopInstantDeath = TroopInstantDeath;
            copy.SupplyBattleId = SupplyBattleId ?? "";
            copy.MobileBattleRules = MobileBattleRules; copy.ShowEnemiesThroughFog = ShowEnemiesThroughFog; copy.TestAutoDeployment = TestAutoDeployment;
            copy.Objective = (Objective ?? new ObjectiveConfig()).Clone();
            if (Placements != null) foreach (UnitPlacement entry in Placements) copy.Placements.Add(entry.Clone());
            return copy;
        }
    }

    public sealed class ArmyStatus
    {
        public bool Exact;
        public int InField, Defeated, Stock, SeenTotal;
    }

    [Serializable] public sealed class BattleSoldier
    {
        public int Id, SquadId;
        public BaseConquest.Base.RpgCharacter Rpg;
        public int OriginCommanderSlot = -1;
        public int RescueTargetId = -1, CarryingId = -1, CarriedById = -1;
        public bool RescueRequested, RescueAnyUnit, Rescued;
        public int LastHitSourceId=-1;
        public bool DeathResolved, InstantDead, ProtectFromInstantDeath, KnockedDown;
        public int ManualRescuerId = -1;
        public int EscapeCarrierId = -1; // 仅供最后一段撤离动画保持怀抱，不参与行动。
        public BattleSide Side;
        public SoldierKind Kind;
        public GridPosition Cell, PreviousCell, MoveGoal, LastTargetCell;
        public string AppearanceId = "";
        public SoldierKind CombatKind => Kind==SoldierKind.Hero ? Stats.HasCombatKind ? Stats.CombatKind : AppearanceId=="ahe" ? SoldierKind.Ranged : AppearanceId=="azhu" ? SoldierKind.Cavalry : SoldierKind.Melee : Kind;
        public SoldierKind ProfessionKind => Kind==SoldierKind.Hero&&Stats.HasProfessionKind ? Stats.ProfessionKind : CombatKind;
        public SoldierStats Stats;
        public float Health, DamageDealt, HealingDone;
        public bool Escaped, IsTransit, PreviousTransit, PreviousShared;
        public float BattlePower;
        public int TargetId = -1;
        public SquadCommand Command;
        public int FocusTargetId = -1;
        public bool ResumeFollowing;
        public int RetainedCombatTargetId = -1;
        public int WeakTerrainUntil, WeakCrowdUntil, InspiredUntil, MarchUntil, RegenUntil;
        public int RegenSourceId = -1;
        public int BuffGainSerial, HealGainSerial, DebuffGainSerial;
        public bool CloseSuppressed;
        public RoundAction LastAction;
        public string LastDecision = "";
        public bool LastAttackCritical;
        public bool MovedThisRound;
        public bool DeveloperInvulnerable, DeveloperImmobile, DeveloperSpawned;
        public bool Recruited, HasProvisionEffects;
        public float ProvisionAttackFactor=1,ProvisionWeaponBonus,ProvisionArmorBonus;
        public float DrinkMultiplier=1;
        public string DrinkLabel="";
        public float LastDamageTaken;
        public int LastFootwork;
        public static float AttackBob(float progress) => progress <= 0 || progress >= 1 ? 0 : (float)Math.Sin(progress * Math.PI * 2) * 0.075f;
        public static bool DamageFlashVisible(float damage, float progress) => damage > 0 && progress >= 0.94f && progress < 1;
        public List<GridPosition> LastMovePath = new List<GridPosition>();
        public BattleSoldier Copy() { var x = (BattleSoldier)MemberwiseClone(); x.Stats = Stats.Clone();x.Rpg=Rpg?.Copy(); x.LastMovePath = new List<GridPosition>(LastMovePath); return x; }
        public bool IsAlive => Health > 0 && !KnockedDown;
        public bool IsActive => IsAlive && !Escaped;
        public bool IsDowned => (KnockedDown || Kind == SoldierKind.Hero && !IsAlive) && !InstantDead && !Rescued && !Escaped;
    }

    [Serializable] public sealed class BattleSquad
    {
        public int Id;
        public string Name;
        public BattleSide Side;
        public SoldierKind Kind;
        public int CommanderSlot = -1;
        public int CommanderId = -1;
        public int SkillReadyRound, SkillChargeRounds;
        public int LastSkillCastRound = -1;
        public bool SkillQueued;
        public int MergedInto = -1;
        public bool GuardAssigned, GuardArrived, GuardAlert;
        public GridPosition GuardCenter;
        public GridPosition GuardRequestedCell;
        public int ActualGuardRange, ActualAlertRange, ActualAttackRange;
        public readonly HashSet<int> StrategyThreats = new HashSet<int>();
        public readonly HashSet<int> GuardTargets = new HashSet<int>();
        public readonly HashSet<int> SupplySlots = new HashSet<int>();
        public SquadCommand IssuedCommand;
        public readonly HashSet<GridPosition> GuardCells = new HashSet<GridPosition>();
        public readonly List<BattleSoldier> Soldiers = new List<BattleSoldier>();
        public int ActiveCount { get { int count = 0; foreach (BattleSoldier s in Soldiers) if (s.IsActive) count++; return count; } }
        public SquadCommand Command
        {
            get
            {
                foreach (BattleSoldier s in Soldiers) if (s.IsActive) return s.Command;
                return SquadCommand.Auto;
            }
        }
        public bool MixedCommands { get { foreach (BattleSoldier s in Soldiers) if (s.IsActive && s.Command != Command) return true; return false; } }
    }

    public sealed class UnitOrderPreview
    {
        public int SoldierId;
        public SquadCommand Command;
        public int LockedTargetId = -1;
        public int ImmediateTargetId = -1;
        public double DetourRounds = double.PositiveInfinity, ClearRounds = double.PositiveInfinity;
        public GridPosition? TargetCell;
        public readonly List<GridPosition> Path = new List<GridPosition>();
        public bool NextStepBlocked;
        public string Status = "";
    }

    public sealed class SquadReport
    {
        public string Name;
        public BattleSide Side;
        public int InitialCount, Standing, Defeated, Escaped;
        public float DamageDealt;
    }

    public sealed class BattleReport
    {
        public BattleOutcome Outcome;
        public int Rounds, CommandsIssued, AbilitiesUsed;
        public readonly List<SquadReport> Squads = new List<SquadReport>();
        public readonly List<ArmyBattleReport> Armies = new List<ArmyBattleReport>();
    }
}
