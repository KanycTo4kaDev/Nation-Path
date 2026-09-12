using Godot;
using System.Collections.Generic;

public partial class GameManager : Node
{
    public static GameManager Instance { get; private set; }

    public int PlayerNationId = -1;
    public bool IsMultiplayerGame = false;
    public bool IsGodMode = false;
    public int TotalPlayers = 1;
    public int BotCount = 0;
    public int AIDifficulty = 0;
    public Dictionary<int, int> PeerNationMap = new();

    public Dictionary<int, int> Gold = new();
    public Dictionary<int, int> Mines = new();
    public Dictionary<int, float> BuildTimers = new();
    public Dictionary<int, int> Research = new();
    public Dictionary<int, int> Universities = new();
    public Dictionary<int, int> TechMask = new();
    public Dictionary<int, string> BuildOrders = new();
    public Dictionary<int, int> DayUpkeep = new();
    public int Day = 1;

    // Дипломатия: ключ пары min*10+max (01/02/03/12/13/23), значение — RelationState.
    public Dictionary<int, int> Relations = new();
    public Dictionary<int, float> PactTimers = new();
    public const float PactDuration = 300f;

    public static int RelationKey(int a, int b)
    {
        return System.Math.Min(a, b) * 10 + System.Math.Max(a, b);
    }

    public static RelationState GetRelation(int a, int b)
    {
        if (a == b) return RelationState.Neutral;
        return (RelationState)Instance.Relations.GetValueOrDefault(RelationKey(a, b), 0);
    }

    public static string RelationName(RelationState r)
    {
        return r switch
        {
            RelationState.War => "Война",
            RelationState.Pact => "Пакт",
            _ => "Нейтралитет",
        };
    }

    public const int StartingGold = 10;
    public const int MineCost = 10;
    public const int MineIncome = 5;
    public const int BaseIncome = 2;
    public const int MaxMines = 5;
    public const int MaxFortLevel = 3;
    public const float FortDamageReduction = 0.15f;
    public const float IncomeInterval = 5.0f;
    public const float BuildTime = 5.0f;

    // Наука: базовый тик + университеты (upkeep золотом за каждое очко).
    public const int BaseResearch = 1;
    public const int UniversityCost = 30;
    public const int MaxUniversities = 2;
    public const int UniversityPoints = 2;
    public const int UniversityUpkeepPerPoint = 5;

    // Техи: 0-2 война (Оружие I/II, Дисциплина), 3-5 экономика
    // (Торговля, Рекрутство, Логистика), 6-7 юниты (Пехота, Элита),
    // 8-10 ветки пехоты (Ветераны вверх, Тактика вниз, Резервы по стволу).
    public static int TechGoldCost(int techId)
    {
        return techId switch
        {
            0 => 20,
            1 => 50,
            2 => 40,
            3 => 20,
            4 => 40,
            5 => 60,
            6 => 30,
            7 => 80,
            8 => 50,
            9 => 50,
            10 => 60,
            _ => int.MaxValue,
        };
    }

    public static int TechResearchCost(int techId)
    {
        return techId switch
        {
            0 => 10,
            1 => 30,
            2 => 25,
            3 => 10,
            4 => 25,
            5 => 40,
            6 => 15,
            7 => 60,
            8 => 30,
            9 => 30,
            10 => 40,
            _ => int.MaxValue,
        };
    }

    // Требования: 1 требует 0; война цепочкой 0 → 2 → 6;
    // 8/9/10 требуют 6; 7 требует 10, 0 и 2;
    // экономика цепочкой 3 → 4 → 5.
    public static int[] TechRequires(int techId)
    {
        return techId switch
        {
            1 => new[] { 0 },
            2 => new[] { 0 },
            4 => new[] { 3 },
            5 => new[] { 4 },
            6 => new[] { 2 },
            7 => new[] { 10, 0, 2 },
            8 => new[] { 6 },
            9 => new[] { 6 },
            10 => new[] { 6 },
            _ => System.Array.Empty<int>(),
        };
    }

    // Полоса в ряду: 8 — вверх, 9 — вниз, остальные по стволу.
    public static int TechLane(int techId)
    {
        return techId switch
        {
            8 => -1,
            9 => 1,
            _ => 0,
        };
    }

    // Глубина в дереве (для раскладки по ярусам).
    public static int TechDepth(int techId)
    {
        int depth = 0;
        foreach (int req in TechRequires(techId))
            depth = System.Math.Max(depth, TechDepth(req) + 1);
        return depth;
    }

    public static bool TechIsMilitary(int techId)
    {
        return techId is 0 or 1 or 2 or 6 or 7 or 8 or 9 or 10;
    }

    public static string TechName(int techId)
    {
        return techId switch
        {
            0 => "Оружие I",
            1 => "Оружие II",
            2 => "Дисциплина",
            3 => "Торговля",
            4 => "Рекрутство",
            5 => "Логистика",
            6 => "Пехота",
            7 => "Элита",
            8 => "Ветераны",
            9 => "Тактика",
            10 => "Резервы",
            _ => "?",
        };
    }

    public static string TechDesc(int techId)
    {
        return techId switch
        {
            0 => "+10% к урону",
            1 => "+20% к урону",
            2 => "Реген 2→3/с",
            3 => "+2 к базовому доходу",
            4 => "−15% цены армий",
            5 => "Шаг −0.15с",
            6 => "Открывает пехоту",
            7 => "Открывает элиту",
            8 => "Меньшие потери",
            9 => "+15% вдвоём+",
            10 => "Лимит солдат 120",
            _ => "",
        };
    }

    public static bool CanRecruit(int nationId, UnitType type)
    {
        return type switch
        {
            UnitType.Infantry => HasTech(nationId, 6),
            UnitType.Elite => HasTech(nationId, 7),
            _ => true,
        };
    }

    public static int GetMineCost(int owned)
    {
        return MineCost * (owned + 1);
    }

    public static int GetFortCost(int nextLevel)
    {
        return nextLevel switch
        {
            1 => 15,
            2 => 30,
            3 => 50,
            _ => int.MaxValue,
        };
    }

    public const int ArmyBaseCost = 20;
    public const int ArmyCostPer10 = 20;
    public const int MinSoldiers = 10;
    public const int MaxSoldiers = 100;
    public const int MaxArmiesPerRegion = 2;
    public const int MaxSoldiersPerRegion = 200;

    public static int GetArmyCost(int soldiers)
    {
        if (soldiers < MinSoldiers) soldiers = MinSoldiers;
        return soldiers / 10 * ArmyCostPer10;
    }

    public static int GetArmyCost(int soldiers, UnitType type)
    {
        return (int)(GetArmyCost(soldiers) * UnitStats.CostMult(type));
    }

    public static int GetArmyCost(int soldiers, UnitType type, int armyCount)
    {
        return (int)(GetArmyCost(soldiers, type) * (1f + 0.25f * armyCount));
    }

    public static int GetArmyCost(int soldiers, UnitType type, int armyCount, int nationId)
    {
        return (int)(GetArmyCost(soldiers, type, armyCount) * RecruitMult(nationId));
    }

    public override void _Ready()
    {
        Instance = this;
    }

    public void InitEconomy(int nationCount)
    {
        Day = 1;
        for (int i = 0; i < nationCount; i++)
        {
            Gold[i] = StartingGold;
            Mines[i] = 0;
            BuildTimers[i] = 0f;
            Research[i] = 0;
            Universities[i] = 0;
            TechMask[i] = 0;
            BuildOrders[i] = "";
            DayUpkeep[i] = 0;
        }
        InitDiplomacy(nationCount);
    }

    public void InitDiplomacy(int nationCount)
    {
        Relations.Clear();
        PactTimers.Clear();
        for (int a = 0; a < nationCount; a++)
            for (int b = a + 1; b < nationCount; b++)
                Relations[RelationKey(a, b)] = (int)RelationState.Neutral;
    }

    public static bool HasTech(int nationId, int techId)
    {
        return (Instance.TechMask.GetValueOrDefault(nationId, 0) & (1 << techId)) != 0;
    }

    public static float DamageTechMult(int nationId)
    {
        if (HasTech(nationId, 1)) return 1.2f;
        if (HasTech(nationId, 0)) return 1.1f;
        return 1f;
    }

    public static float RegenRate(int nationId)
    {
        return HasTech(nationId, 2) ? 3f : 2f;
    }

    public static int BaseIncomeFor(int nationId)
    {
        return BaseIncome + (HasTech(nationId, 3) ? 2 : 0);
    }

    public static float RecruitMult(int nationId)
    {
        return HasTech(nationId, 4) ? 0.85f : 1f;
    }

    public static float MoveDelayBonus(int nationId)
    {
        return HasTech(nationId, 5) ? 0.15f : 0f;
    }

    public static float DeserterRate(int nationId)
    {
        return HasTech(nationId, 8) ? 0.15f : 0.25f;
    }

    public static int MaxSoldiersFor(int nationId)
    {
        return HasTech(nationId, 10) ? 120 : MaxSoldiers;
    }
}
