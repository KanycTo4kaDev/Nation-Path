using Godot;
using System.Collections.Generic;

// ИИ-противник: ходит тем же API, что игрок (Server*-методы, QueueArmyMove).
// В одиночке работает локально, в сети — только на сервере (синк штатный).
public partial class AIPlayer : Node
{
    public int NationId = -1;
    public int Difficulty = 0; // 0 — Легко, 1 — Норма

    private MapGenerator _map;
    private float _timer = 2f;

    private float ThinkInterval => Difficulty == 1 ? 4f : 8f;
    private float Aggro => Difficulty == 1 ? 1.0f : 0.7f;
    private int MaxArmies => Difficulty == 1 ? 5 : 3;

    public void Setup(MapGenerator map, int nationId, int difficulty)
    {
        _map = map;
        NationId = nationId;
        Difficulty = difficulty;
    }

    public override void _Process(double delta)
    {
        if (_map == null || !IsInstanceValid(_map)) return;
        if (NationId < 0) return;

        // Столица потеряна — ИИ мёртв.
        int capId = _map.GetCapitalRegion(NationId);
        var cap = capId >= 0 ? _map.GetRegionById(capId) : null;
        if (cap == null || cap.OwnerId != NationId)
        {
            QueueFree();
            return;
        }

        _timer -= (float)delta;
        if (_timer > 0f) return;
        _timer = ThinkInterval;

        Think();
    }

    private void Think()
    {
        AcceptPactOffers();
        BuildPhase();
        MovePhase();
    }

    // Боты принимают входящие предложения пакта (обе сложности).
    private void AcceptPactOffers()
    {
        foreach (var kvp in new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<int, int>>(GameManager.Instance.PactOffers))
        {
            int a = kvp.Key / 10;
            int b = kvp.Key % 10;
            if (a != NationId && b != NationId) continue;
            int from = a == NationId ? b : a;
            if (kvp.Value != from) continue;
            _map.ServerAnswerPact(NationId, from, true);
        }
    }

    private void BuildPhase()
    {
        int gold = GameManager.Instance.Gold.GetValueOrDefault(NationId, 0);
        int capId = _map.GetCapitalRegion(NationId);
        if (capId < 0) return;

        var armies = _map.GetArmiesOfNation(NationId);
        if (armies.Count < MaxArmies)
        {
            bool canElite = GameManager.CanRecruit(NationId, UnitType.Elite);
            bool canInf = GameManager.CanRecruit(NationId, UnitType.Infantry);
            if (Difficulty == 1 && canElite && gold >= GameManager.GetArmyCost(60, UnitType.Elite, armies.Count, NationId))
                _map.ServerCreateArmy(capId, 60, NationId, UnitType.Elite);
            else if (canInf && gold >= GameManager.GetArmyCost(50, UnitType.Infantry, armies.Count, NationId))
                _map.ServerCreateArmy(capId, 50, NationId, UnitType.Infantry);
            else if (gold >= GameManager.GetArmyCost(30, UnitType.Militia, armies.Count, NationId))
                _map.ServerCreateArmy(capId, 30, NationId, UnitType.Militia);
            return;
        }

        float buildTimer = GameManager.Instance.BuildTimers.GetValueOrDefault(NationId, 0f);
        if (buildTimer > 0f) return;

        int mines = GameManager.Instance.Mines.GetValueOrDefault(NationId, 0);
        if (mines < GameManager.MaxMines && gold >= GameManager.GetMineCost(mines))
        {
            _map.ServerBuildMine(NationId);
            return;
        }

        if (Difficulty == 1)
        {
            int unis = GameManager.Instance.Universities.GetValueOrDefault(NationId, 0);
            if (unis < GameManager.MaxUniversities && gold >= GameManager.UniversityCost)
            {
                _map.ServerBuildUniversity(NationId);
                return;
            }

            if (GameManager.Instance.ResearchQueue.GetValueOrDefault(NationId, -1) >= 0)
                return;

            for (int tech = 0; tech < 11; tech++)
            {
                if (GameManager.HasTech(NationId, tech)) continue;
                bool reqOk = true;
                foreach (int req in GameManager.TechRequires(tech))
                {
                    if (!GameManager.HasTech(NationId, req)) { reqOk = false; break; }
                }
                if (!reqOk) continue;
                if (gold < GameManager.TechGoldCost(tech)) continue;
                int pts = GameManager.Instance.Research.GetValueOrDefault(NationId, 0);
                if (pts < GameManager.TechResearchCost(tech)) continue;
                _map.ServerResearchTech(NationId, tech);
                return;
            }

            var cap = _map.GetRegionById(capId);
            if (cap != null && cap.FortLevel < GameManager.MaxFortLevel
                && gold >= GameManager.GetFortCost(cap.FortLevel + 1))
                _map.ServerBuildFort(NationId);
        }
    }

    private void MovePhase()
    {
        var armies = _map.GetArmiesOfNation(NationId);
        var regions = _map.GetAllRegions();
        int capId = _map.GetCapitalRegion(NationId);
        var cap = capId >= 0 ? _map.GetRegionById(capId) : null;

        foreach (var army in armies)
        {
            if (!IsInstanceValid(army)) continue;
            if (_map.IsArmyBusy(army.Id)) continue;
            if (army.HP <= 10) continue;

            // 1. Оборона: враг в столице или рядом — бить его.
            int guardTarget = FindGuardTarget(army, regions, cap);
            if (guardTarget >= 0)
            {
                EnsureWarForCell(guardTarget);
                // Через серверный метод: ходы ботов синкаются клиентам.
                _map.ServerMoveArmy(army.Id, guardTarget);
                continue;
            }

            // 2. Экспансия: ближайшая чужая клетка, которую потянем.
            float myPower = army.Soldiers * UnitStats.DamageMult(army.Type);
            int bestId = -1;
            int bestLen = int.MaxValue;
            foreach (var region in regions)
            {
                if (region.OwnerId == NationId) continue;
                if (region.Id == army.RegionId) continue;
                if (!Beatable(region, myPower)) continue;

                var path = _map.FindPathTo(army.RegionId, region.Id);
                if (path == null || path.Count == 0) continue;
                if (path.Count < bestLen)
                {
                    bestLen = path.Count;
                    bestId = region.Id;
                }
            }

            if (bestId >= 0)
            {
                EnsureWarForCell(bestId);
                _map.ServerMoveArmy(army.Id, bestId);
            }
        }
    }

    // Перед атакой чужой owned-клетки — объявить войну (пакты чтим: их пропускаем).
    private void EnsureWarForCell(int regionId)
    {
        var region = _map.GetRegionById(regionId);
        if (region == null) return;
        if (region.OwnerId < 0 || region.OwnerId == NationId) return;
        if (GameManager.GetRelation(NationId, region.OwnerId) == RelationState.War) return;
        if (GameManager.GetRelation(NationId, region.OwnerId) == RelationState.Pact) return;
        _map.ServerSetRelation(NationId, region.OwnerId, RelationState.War);
    }

    private int FindGuardTarget(Army army, List<Region> regions, Region cap)
    {
        if (cap == null) return -1;
        var danger = new HashSet<int> { cap.Id };
        foreach (int nId in cap.Neighbors)
            danger.Add(nId);

        int bestId = -1;
        int bestLen = int.MaxValue;
        foreach (var region in regions)
        {
            if (!danger.Contains(region.Id)) continue;
            if (region.OwnerId == NationId && !HasEnemy(region)) continue;

            var path = _map.FindPathTo(army.RegionId, region.Id);
            if (path == null || path.Count == 0) continue;
            if (path.Count < bestLen)
            {
                bestLen = path.Count;
                bestId = region.Id;
            }
        }
        return bestId;
    }

    private bool HasEnemy(Region region)
    {
        foreach (var army in _map.GetArmiesInRegion(region.Id))
        {
            if (army.PlayerId != NationId) return true;
        }
        return false;
    }

    private bool Beatable(Region region, float myPower)
    {
        // Партнёров по пакту не трогаем.
        if (region.OwnerId >= 0 && region.OwnerId != NationId
            && GameManager.GetRelation(NationId, region.OwnerId) == RelationState.Pact)
            return false;
        float cellPower = 0f;
        foreach (var army in _map.GetArmiesInRegion(region.Id))
        {
            if (army.PlayerId == NationId) continue;
            cellPower += army.Soldiers * UnitStats.DamageMult(army.Type);
        }
        return cellPower <= myPower * Aggro;
    }
}
