using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class MapGenerator : Node2D
{
    [Export] public int HexBoardRadius = 5;
    [Export] public float HexCellRadius = 35f;

    [Signal] public delegate void RegionSelectedEventHandler(string name, int gold, string ownerName, int regionId, bool isCapital);
    [Signal] public delegate void RegionDeselectedEventHandler();

    private List<Nation> _nations = new();
    private Region _selectedRegion = null;
    private Color _selectedOriginalColor;

    public List<Army> _armies = new();
    private int _nextArmyId = 0;
    private Army _selectedArmy = null;
    private Army _secondArmy = null;

    private Dictionary<int, int> _capitalRegions = new();
    private Dictionary<int, Node2D> _capitalMarkers = new();

    public void RefreshCapitalMarkers()
    {
        foreach (var m in _capitalMarkers.Values)
            if (IsInstanceValid(m)) m.QueueRedraw();
    }

    private Dictionary<int, Queue<int>> _armyPaths = new();
    private Dictionary<int, float> _armyMoveTimers = new();
    private Dictionary<int, float> _retreatTimers = new();
    private Dictionary<int, int> _retreatTargets = new();
    private Dictionary<int, float> _captureFlashes = new();
    private const float CaptureFlashDuration = 0.6f;
    private List<int> _highlightedPath = new();
    private HashSet<int> _armiesInBattle = new();
    private const float MoveDelay = 1.5f;
    private const float RetreatWaitDelay = 0.2f;

    private float GetMoveDelay(int armyId)
    {
        var army = GetArmyById(armyId);
        if (army == null) return MoveDelay;
        return Mathf.Max(0.5f, UnitStats.MoveDelay(army.Type) - GameManager.MoveDelayBonus(army.PlayerId));
    }
    private const float BattleRoundDelay = 0.7f;
    private const float BattleBaseHit = 8f;
    private const float BattleRatioMin = 0.25f;
    private const float BattleRatioMax = 4f;
    private const int BattleMaxHitPerRound = 8;

    private class BattleState
    {
        public Army Defender;
        public List<Army> Attackers = new();
        public int DefenderStartHp = 100;
    }

    private Dictionary<int, BattleState> _activeBattles = new();

    private float _incomeTimer = 0f;
    private Label _goldValueLabel;
    private Label _scienceValueLabel;
    private Label _dayLabel;
    private PanelContainer _topBack;
    private PanelContainer _ecoTip;
    private Label _ecoTipBody;
    private float _ecoTipTimer = 0f;
    private Label _observerLabel;
    private RegionPanel _regionPanel;
    private SideTabs _sideTabs;
    private CameraController _camera;
    private MoveOverlay _overlay;
    private bool _wasOverlayActive = false;

    private bool _gameOver = false;
    private bool _wasObserver = false;
    private float _gameTime = 0f;
    private int _statBattles = 0;
    private Dictionary<int, int> _statLosses = new();
    private Dictionary<int, int> _statCaptures = new();

    public override void _Ready()
    {
        GD.Randomize();

        _overlay = new MoveOverlay();
        _overlay.Setup(this);
        AddChild(_overlay);

        InitNations();
        GenerateMap();
        GenerateNeighbors();
        PlaceCapitals();
        ShowPlayerHUD();
        GameManager.Instance.InitEconomy(_nations.Count);
        SpawnAI();

        var panel = new RegionPanel();
        _regionPanel = panel;
        AddChild(panel);
        RegionSelected += panel.OnRegionSelected;
        RegionDeselected += panel.OnRegionDeselected;

        var sideTabs = new SideTabs();
        _sideTabs = sideTabs;
        AddChild(sideTabs);

        _camera = GetNodeOrNull<CameraController>("Camera2D");
    }

    public bool IsTabPanelOpen()
    {
        return _sideTabs != null && _sideTabs.IsAnyTabOpen();
    }

    public override void _Process(double delta)
    {
        // Тултип экономики: ховер по плашке → справа-снизу от мышки, следует.
        // Текст — по таймеру 0.5с, позиция — каждый кадр.
        if (_ecoTip != null && IsInstanceValid(_ecoTip) && _topBack != null && IsInstanceValid(_topBack))
        {
            Vector2 mouse = GetViewport().GetMousePosition();
            bool hover = _topBack.GetGlobalRect().Grow(8f).HasPoint(mouse);
            if (hover && !_ecoTip.Visible)
            {
                _ecoTip.Visible = true;
                RefreshEcoTip();
                _ecoTipTimer = 0f;
            }
            else if (!hover)
            {
                _ecoTip.Visible = false;
            }

            if (_ecoTip.Visible)
            {
                _ecoTipTimer += (float)delta;
                if (_ecoTipTimer >= 0.5f)
                {
                    _ecoTipTimer = 0f;
                    RefreshEcoTip();
                }
                Vector2 size = _ecoTip.GetCombinedMinimumSize();
                Vector2 vp = GetViewportRect().Size;
                _ecoTip.Position = new Vector2(
                    Mathf.Min(mouse.X + 18f, vp.X - size.X - 8f),
                    Mathf.Min(mouse.Y + 24f, vp.Y - size.Y - 8f));
            }
        }

        // Открыто любое меню — камера стоит, игра идёт дальше.
        // (Пауза в одиночке останавливает и камеру, и игру сама.)
        if (_camera != null)
        {
            bool menuOpen = (_sideTabs != null && _sideTabs.IsAnyTabOpen())
                || (_pauseLayer != null && IsInstanceValid(_pauseLayer));
            _camera.InputLocked = menuOpen;
        }

        foreach (var army in _armies)
        {
            if (army.IsWounded && IsInstanceValid(army) && !_armiesInBattle.Contains(army.Id))
                army.Regenerate((float)delta);
        }

        // Пара на совмещение: развалилась сама (смерть/бой/отход) — снять.
        if (IsPairBroken())
            ClearSecondArmy();

        // Отступление с задержкой: по истечении таймера — шаг в целевой регион.
        // Итерация по снимку ключей — словарь правится по ходу тика.
        var finishedRetreats = new List<int>();
        foreach (int armyId in new List<int>(_retreatTimers.Keys))
        {
            if (!_retreatTimers.TryGetValue(armyId, out float timer)) continue;
            timer -= (float)delta;
            _retreatTimers[armyId] = timer;
            if (timer > 0f) continue;

            finishedRetreats.Add(armyId);
            var army = GetArmyById(armyId);
            if (army == null || !IsInstanceValid(army)) continue;
            if (!_retreatTargets.TryGetValue(armyId, out int targetId)) continue;
            var target = GetRegionById(targetId);
            if (target == null) continue;

            int prevRegionId = army.RegionId;
            army.RegionId = targetId;
            army.Position = target.Center;
            army.QueueRedraw();

            UpdateArmyPositions(prevRegionId);
            UpdateArmyPositions(targetId);

            // Клетка освобождена: у ждущих появляется стрелка и начинается
            // полный замах. Сброс раньше тика путей — шаг в том же кадре невозможен.
            foreach (var kvp in _armyPaths)
            {
                var other = GetArmyById(kvp.Key);
                if (other == null || !IsInstanceValid(other)) continue;
                if (kvp.Value == null || kvp.Value.Count == 0 || kvp.Value.Peek() != prevRegionId) continue;
                _armyMoveTimers[kvp.Key] = GetMoveDelay(kvp.Key);
            }

            GD.Print($"Армия #{army.Id} отступила в {target.RegionName} (HP={army.HP}, ранена)");
        }
        foreach (int id in finishedRetreats)
        {
            _retreatTimers.Remove(id);
            _retreatTargets.Remove(id);
        }

        // Вспышки захвата: затухание.
        var finishedFlashes = new List<int>();
        foreach (int regionId in new List<int>(_captureFlashes.Keys))
        {
            if (!_captureFlashes.TryGetValue(regionId, out float t)) continue;
            t -= (float)delta;
            if (t <= 0f) finishedFlashes.Add(regionId);
            else _captureFlashes[regionId] = t;
        }
        foreach (int id in finishedFlashes)
            _captureFlashes.Remove(id);

        var finishedIds = new List<int>();

        foreach (var kvp in _armyPaths)
        {
            int armyId = kvp.Key;
            Queue<int> path = kvp.Value;

            if (path.Count == 0)
            {
                finishedIds.Add(armyId);
                continue;
            }

            if (!_armyMoveTimers.ContainsKey(armyId))
                _armyMoveTimers[armyId] = GetMoveDelay(armyId);

            _armyMoveTimers[armyId] -= (float)delta;

            if (_armyMoveTimers[armyId] <= 0f)
            {
                var army = GetArmyById(armyId);
                if (army == null)
                {
                    finishedIds.Add(armyId);
                    continue;
                }

                int nextRegionId = path.Peek();
                var nextRegion = GetRegionById(nextRegionId);
                var currentRegion = GetRegionById(army.RegionId);
                if (nextRegion == null || currentRegion == null)
                {
                    finishedIds.Add(armyId);
                    continue;
                }

                if (_armiesInBattle.Contains(armyId))
                    continue;

                if (army.HP <= 10)
                {
                    _armyMoveTimers[armyId] = GetMoveDelay(armyId);
                    continue;
                }

                var enemy = GetEnemyArmyInRegion(nextRegionId, army.PlayerId);
                // Лимит: не больше MaxArmiesPerRegion своих на клетке.
                // Упрётся — приказ снимается (через finishedIds: словарь правится вне итерации).
                if (enemy == null && CountOwnArmies(nextRegionId, army.PlayerId) >= GameManager.MaxArmiesPerRegion)
                {
                    GD.Print($"Армия #{army.Id}: клетка {nextRegion.RegionName} заполнена!");
                    finishedIds.Add(armyId);
                    if (_selectedArmy != null && _selectedArmy.Id == armyId)
                        UpdateHighlightedPath(armyId);
                    continue;
                }
                if (enemy != null)
                {
                    // Дипломатия: без войны чужих не трогаем.
                    var rel = GameManager.GetRelation(army.PlayerId, enemy.PlayerId);
                    if (rel != RelationState.War)
                    {
                        GD.Print(rel == RelationState.Pact
                            ? $"Армия #{army.Id}: пакт! Сначала объявите войну."
                            : $"Армия #{army.Id}: граница закрыта — объявите войну.");
                        finishedIds.Add(armyId);
                        if (_selectedArmy != null && _selectedArmy.Id == armyId)
                            UpdateHighlightedPath(armyId);
                        continue;
                    }

                    // Проигравший отходит — ждём короткую паузу и заходим
                    // только в пустую клетку, бой не начинаем.
                    if (_retreatTargets.ContainsKey(enemy.Id))
                    {
                        _armyMoveTimers[armyId] = RetreatWaitDelay;
                        continue;
                    }

                    if (enemy.IsWounded)
                    {
                        _armyMoveTimers[armyId] = GetMoveDelay(armyId);

                        if (_activeBattles.ContainsKey(enemy.Id))
                        {
                            _activeBattles[enemy.Id].Attackers.Add(army);
                            _armiesInBattle.Add(armyId);
                            _armiesInBattle.Add(enemy.Id);
                        }
                        else
                        {
                            var state = new BattleState { Defender = enemy };
                            state.Attackers.Add(army);
                            _activeBattles[enemy.Id] = state;
                            _armiesInBattle.Add(armyId);
                            _armiesInBattle.Add(enemy.Id);
                            HandleGroupBattle(state);
                        }
                        continue;
                    }

                    _armyMoveTimers[armyId] = GetMoveDelay(armyId);

                    if (_activeBattles.ContainsKey(enemy.Id))
                    {
                        _activeBattles[enemy.Id].Attackers.Add(army);
                        _armiesInBattle.Add(armyId);
                        GD.Print($"Армия #{army.Id} присоединилась к атаке на Армию #{enemy.Id} (всего атакующих: {_activeBattles[enemy.Id].Attackers.Count})");
                    }
                    else
                    {
                        var state = new BattleState { Defender = enemy };
                        state.Attackers.Add(army);
                        _activeBattles[enemy.Id] = state;
                        _armiesInBattle.Add(armyId);
                        _armiesInBattle.Add(enemy.Id);
                        HandleGroupBattle(state);
                    }
                    continue;
                }

                int prevRegionId = army.RegionId;

                // Дипломатия: мирный заход в чужую owned-клетку — только пактом.
                // Без войны и без пакта граница закрыта.
                if (nextRegion.OwnerId >= 0 && nextRegion.OwnerId != army.PlayerId)
                {
                    var relStep = GameManager.GetRelation(army.PlayerId, nextRegion.OwnerId);
                    if (relStep == RelationState.Neutral)
                    {
                        GD.Print($"Армия #{army.Id}: граница закрыта — объявите войну.");
                        finishedIds.Add(armyId);
                        if (_selectedArmy != null && _selectedArmy.Id == armyId)
                            UpdateHighlightedPath(armyId);
                        continue;
                    }
                }

                army.RegionId = nextRegionId;
                army.Position = nextRegion.Center;
                UpdateArmyPositions(nextRegionId);
                UpdateArmyPositions(prevRegionId);

                if (nextRegion.OwnerId != army.PlayerId)
                {
                    nextRegion.OwnerId = army.PlayerId;
                    nextRegion.OwningNation = GetNationById(army.PlayerId);
                    nextRegion.Color = nextRegion.OwningNation != null ? nextRegion.OwningNation.Color : Colors.White;
                    SyncRegionCapture(nextRegion);
                    _statCaptures[army.PlayerId] = _statCaptures.GetValueOrDefault(army.PlayerId, 0) + 1;
                    AudioHub.Instance?.PlayCapture();
                    FlashCapture(nextRegion.Id);
                    GD.Print($"Армия #{army.Id} захватила {nextRegion.RegionName}!");
                }

                path.Dequeue();
                _armyMoveTimers[armyId] = GetMoveDelay(armyId);

                if (_selectedArmy != null && _selectedArmy.Id == armyId)
                    UpdateHighlightedPath(armyId);

                GD.Print($"Армия #{army.Id} → {nextRegion.RegionName} (осталось шагов: {path.Count})");
            }
        }

        foreach (int id in finishedIds)
        {
            _armyPaths.Remove(id);
            _armyMoveTimers.Remove(id);
        }

        _incomeTimer += (float)delta;
        if (_incomeTimer >= GameManager.IncomeInterval)
        {
            CollectIncome();
            _incomeTimer -= GameManager.IncomeInterval;
        }

        if (!_gameOver)
        {
            _gameTime += (float)delta;
            CheckVictory();

            bool observer = IsObserver();
            if (observer != _wasObserver)
            {
                _wasObserver = observer;
                if (_observerLabel != null)
                    _observerLabel.Visible = observer;
                if (observer)
                {
                    _selectedArmy = null;
                    ClearSecondArmy();
                    _regionPanel.HideArmyInfo();
                }
            }
        }

        if (_armyPaths.Count == 0 && _highlightedPath.Count > 0)
        {
            _highlightedPath.Clear();
            _overlay?.Refresh();
        }

        // Анимация стрелок: перерисовка каждый кадр, пока кто-то идёт,
        // плюс один финальный кадр очистки, иначе последний кадр зависнет.
        bool overlayActive = _armyPaths.Count > 0 || _retreatTimers.Count > 0 || _captureFlashes.Count > 0;
        if (overlayActive || _wasOverlayActive)
            _overlay?.Refresh();
        _wasOverlayActive = overlayActive;
    }

    private void FlashCapture(int regionId)
    {
        _captureFlashes[regionId] = CaptureFlashDuration;
    }

    private void UpdateHighlightedPath(int armyId)
    {
        _highlightedPath.Clear();
        if (_armyPaths.ContainsKey(armyId))
        {
            var army = GetArmyById(armyId);
            if (army != null)
                _highlightedPath.Add(army.RegionId);

            _highlightedPath.AddRange(_armyPaths[armyId]);
        }
        _overlay?.Refresh();
    }

    // Отрисовка индикаторов (подсветка пути + стрелки) на слое поверх карты.
    public void DrawIndicators(CanvasItem canvas)
    {
        if (_highlightedPath.Count >= 2)
        {
            for (int i = 0; i < _highlightedPath.Count - 1; i++)
            {
                var r1 = GetRegionById(_highlightedPath[i]);
                var r2 = GetRegionById(_highlightedPath[i + 1]);
                if (r1 != null && r2 != null)
                {
                    canvas.DrawLine(r1.Center, r2.Center, new Color(1f, 0.85f, 0.2f, 0.8f), 3f);
                }
            }

            for (int i = 1; i < _highlightedPath.Count; i++)
            {
                var r = GetRegionById(_highlightedPath[i]);
                if (r != null)
                    canvas.DrawCircle(r.Center, 5f, new Color(1f, 0.85f, 0.2f, 0.9f));
            }
        }

        DrawMoveArrows(canvas);
        DrawCaptureFlashes(canvas);
    }

    private void DrawCaptureFlashes(CanvasItem canvas)
    {
        foreach (var kvp in _captureFlashes)
        {
            var region = GetRegionById(kvp.Key);
            if (region == null) continue;
            float ratio = Mathf.Clamp(kvp.Value / CaptureFlashDuration, 0f, 1f);
            float radius = 20f + (1f - ratio) * 45f;
            canvas.DrawArc(region.Center, radius, 0, Mathf.Tau, 32,
                new Color(1f, 0.85f, 0.2f, 0.9f * ratio), 4f);
        }
    }

    private void DrawMoveArrows(CanvasItem canvas)
    {
        var battleAttackers = new HashSet<int>();
        foreach (var battle in _activeBattles.Values)
        {
            if (battle == null) continue;
            foreach (var a in battle.Attackers)
                if (IsInstanceValid(a)) battleAttackers.Add(a.Id);
        }

        var battleRed = new Color(0.9f, 0.2f, 0.2f, 0.95f);

        foreach (var kvp in _armyPaths)
        {
            int armyId = kvp.Key;
            var path = kvp.Value;
            if (path == null || path.Count == 0) continue;

            var army = GetArmyById(armyId);
            if (army == null || !IsInstanceValid(army)) continue;

            var nextRegion = GetRegionById(path.Peek());
            if (nextRegion == null) continue;

            // Бой: стрелка не пропадает, а горит красной весь бой.
            if (_armiesInBattle.Contains(armyId))
            {
                if (!battleAttackers.Contains(armyId)) continue;
                DrawArrow(canvas, army.Position, nextRegion.Center, 1f, battleRed);
                continue;
            }

            // Ожидание освобождения клетки: стрелки нет, она появится
            // вместе с полным замахом после ухода проигравшего.
            var waitingOn = GetEnemyArmyInRegion(nextRegion.Id, army.PlayerId);
            if (waitingOn != null && _retreatTargets.ContainsKey(waitingOn.Id))
                continue;

            float timer = _armyMoveTimers.GetValueOrDefault(armyId, GetMoveDelay(armyId));
            float progress = Mathf.Clamp(1f - timer / GetMoveDelay(armyId), 0f, 1f);

            DrawArrow(canvas, army.Position, nextRegion.Center, progress);
        }

        foreach (var kvp in _retreatTimers)
        {
            int armyId = kvp.Key;
            var army = GetArmyById(armyId);
            if (army == null || !IsInstanceValid(army)) continue;
            if (!_retreatTargets.TryGetValue(armyId, out int targetId)) continue;
            var target = GetRegionById(targetId);
            if (target == null) continue;

            float progress = Mathf.Clamp(1f - kvp.Value / GetMoveDelay(armyId), 0f, 1f);
            DrawArrow(canvas, army.Position, target.Center, progress);
        }
    }

    private void DrawArrow(CanvasItem canvas, Vector2 from, Vector2 to, float progress, Color? fill = null)
    {
        Vector2 dir = to - from;
        if (dir.Length() < 1f) return;
        dir = dir.Normalized();

        // Укорачиваем стрелку, чтобы не залезала под значки армий.
        float margin = 16f;
        float fullLen = from.DistanceTo(to) - margin * 2f;
        if (fullLen <= 0f) return;
        Vector2 start = from + dir * margin;
        Vector2 end = from + dir * (margin + fullLen);

        var bgColor = new Color(0.1f, 0.1f, 0.1f, 0.75f);
        var fillColor = fill ?? new Color(0.2f, 0.85f, 0.3f, 0.95f);

        canvas.DrawLine(start, end, bgColor, 5f);
        if (progress > 0f)
            canvas.DrawLine(start, start + dir * (fullLen * progress), fillColor, 5f);

        // Наконечник.
        float headLen = 10f;
        float headWidth = 7f;
        Vector2 headBase = end - dir * headLen;
        Vector2 perp = new Vector2(-dir.Y, dir.X);
        var headPoints = new Vector2[]
        {
            end,
            headBase + perp * headWidth / 2f,
            headBase - perp * headWidth / 2f,
        };
        canvas.DrawColoredPolygon(headPoints, progress >= 1f ? fillColor : bgColor);
    }

    private List<int> FindPath(int fromId, int toId)
    {
        if (fromId == toId) return new List<int>();

        var visited = new HashSet<int> { fromId };
        var queue = new Queue<(int regionId, List<int> path)>();
        var startRegion = GetRegionById(fromId);
        if (startRegion == null) return null;

        foreach (int nId in startRegion.Neighbors)
        {
            if (!visited.Contains(nId))
            {
                visited.Add(nId);
                queue.Enqueue((nId, new List<int> { fromId, nId }));
            }
        }

        while (queue.Count > 0)
        {
            var (currentId, currentPath) = queue.Dequeue();

            if (currentId == toId)
                return currentPath;

            var currentRegion = GetRegionById(currentId);
            if (currentRegion == null) continue;

            foreach (int nId in currentRegion.Neighbors)
            {
                if (!visited.Contains(nId))
                {
                    visited.Add(nId);
                    var newPath = new List<int>(currentPath) { nId };
                    queue.Enqueue((nId, newPath));
                }
            }
        }

        return null;
    }

    public void QueueArmyMove(int armyId, int targetId)
    {
        var army = GetArmyById(armyId);
        if (army == null) return;
        if (army.RegionId == targetId) return;

        _armyPaths.Remove(armyId);
        _armyMoveTimers.Remove(armyId);
        _retreatTimers.Remove(armyId);
        _retreatTargets.Remove(armyId);
        _highlightedPath.Clear();

        var path = FindPath(army.RegionId, targetId);
        if (path == null || path.Count == 0)
        {
            GD.Print($"Путь не найден: {army.RegionId} -> {targetId}");
            _overlay?.Refresh();
            return;
        }

        // FindPath включает стартовую клетку — режем её, иначе первый шаг
        // будет холостым (а проверки лимита/врага сработают по своей клетке).
        if (path[0] == army.RegionId)
            path.RemoveAt(0);
        if (path.Count == 0) return;

        var queue = new Queue<int>(path);
        _armyPaths[armyId] = queue;
        _armyMoveTimers[armyId] = MoveDelay;

        // Жёлтый путь — только для выбранной армии.
        if (_selectedArmy != null && _selectedArmy.Id == armyId)
        {
            _highlightedPath.Add(army.RegionId);
            _highlightedPath.AddRange(path);
        }
        _overlay?.Refresh();

        GD.Print($"Армия #{armyId}: путь [{string.Join("→", path)}] ({path.Count} шагов)");
    }

    public void CancelArmyPath(int armyId)
    {
        _armyPaths.Remove(armyId);
        _armyMoveTimers.Remove(armyId);
        _retreatTimers.Remove(armyId);
        _retreatTargets.Remove(armyId);
        UpdateHighlightedPath(armyId);
    }

    private void CancelAllPaths()
    {
        _armyPaths.Clear();
        _armyMoveTimers.Clear();
        _retreatTimers.Clear();
        _retreatTargets.Clear();
        _highlightedPath.Clear();
        _overlay?.Refresh();
    }

    private bool RetreatArmy(Army army)
    {
        if (!IsInstanceValid(army)) return false;

        var current = GetRegionById(army.RegionId);
        if (current == null) return false;

        int foundId = -1;
        // Предпочитаем свои клетки без вражеских армий, чтобы не прыгнуть
        // в клетку наступающего (иначе два врага на клетке без боя).
        // Полные клетки (лимит своих) пропускаем.
        foreach (int nId in current.Neighbors)
        {
            var region = GetRegionById(nId);
            if (region != null && region.OwnerId == army.PlayerId
                && GetEnemyArmyInRegion(nId, army.PlayerId) == null
                && CountOwnArmies(nId, army.PlayerId) < GameManager.MaxArmiesPerRegion)
            {
                foundId = nId;
                break;
            }
        }
        if (foundId < 0)
        {
            foreach (int nId in current.Neighbors)
            {
                var region = GetRegionById(nId);
                if (region != null && region.OwnerId == army.PlayerId
                    && CountOwnArmies(nId, army.PlayerId) < GameManager.MaxArmiesPerRegion)
                {
                    foundId = nId;
                    break;
                }
            }
        }

        if (foundId < 0)
        {
            GD.Print($"Армия #{army.Id} погибла — нет пути для отступления!");
            _statLosses[army.PlayerId] = _statLosses.GetValueOrDefault(army.PlayerId, 0) + 1;
            int prevId = army.RegionId;
            if (_selectedArmy != null && _selectedArmy.Id == army.Id)
            {
                _selectedArmy = null;
                _regionPanel.HideArmyInfo();
            }
            _armies.Remove(army);
            army.QueueFree();
            UpdateArmyPositions(prevId);
            return false;
        }

        int prevRegionId = army.RegionId;
        var target = GetRegionById(foundId);

        // Цена отступления — арьергард: доля солдат (меньше с Ветеранами).
        // Кончились — рассыпалась.
        int deserters = (int)Mathf.Ceil(army.Soldiers * GameManager.DeserterRate(army.PlayerId));
        army.Soldiers -= deserters;
        if (army.Soldiers <= 0)
        {
            GD.Print($"Армия #{army.Id} рассыпалась при отступлении!");
            _statLosses[army.PlayerId] = _statLosses.GetValueOrDefault(army.PlayerId, 0) + 1;
            if (_selectedArmy != null && _selectedArmy.Id == army.Id)
            {
                _selectedArmy = null;
                _regionPanel.HideArmyInfo();
            }
            _armies.Remove(army);
            _retreatTimers.Remove(army.Id);
            _retreatTargets.Remove(army.Id);
            army.QueueFree();
            UpdateArmyPositions(prevRegionId);
            return false;
        }

        army.IsWounded = true;
        army.IsSelected = false;
        army.QueueRedraw();

        _armyPaths.Remove(army.Id);
        _armyMoveTimers.Remove(army.Id);

        // Отступление с задержкой, как обычный шаг: телепорт — по истечении таймера в _Process.
        _retreatTargets[army.Id] = foundId;
        _retreatTimers[army.Id] = GetMoveDelay(army.Id);

        UpdateArmyPositions(prevRegionId);

        if (_selectedArmy != null && _selectedArmy.Id == army.Id)
        {
            _selectedArmy = null;
            _regionPanel.HideArmyInfo();
        }

        GD.Print($"Армия #{army.Id} отступает в {target.RegionName} (HP={army.HP}, ранена)");
        return true;
    }

    private async void HandleGroupBattle(BattleState state)
    {
        if (IsInstanceValid(state.Defender))
            state.DefenderStartHp = state.Defender.HP;
        int result = await Battle(state.Attackers, state.Defender);

        foreach (var a in state.Attackers)
            _armiesInBattle.Remove(a.Id);

        _armiesInBattle.Remove(state.Defender.Id);
        _activeBattles.Remove(state.Defender.Id);

        if (result == 0)
        {
            GD.Print($"Атакующие проиграли бой против Армии #{state.Defender.Id}!");
            foreach (var a in state.Attackers)
            {
                if (IsInstanceValid(a) && a.HP > 0 && a.HP <= 10)
                    RetreatArmy(a);
                else if (IsInstanceValid(a))
                {
                    if (a.HP < a.MaxHP)
                        a.IsWounded = true;
                    _armyPaths.Remove(a.Id);
                    _armyMoveTimers.Remove(a.Id);
                    UpdateArmyPositions(a.RegionId);
                }
            }
            // Победивший защитник тоже ранен — иначе слабый (HP≤10) с висящим
            // приказом замрёт навсегда: шаг заблокирован, а регена без метки нет.
            if (IsInstanceValid(state.Defender) && state.Defender.HP > 0 && state.Defender.HP < state.Defender.MaxHP)
            {
                state.Defender.IsWounded = true;
                state.Defender.QueueRedraw();
            }
            return;
        }

        if (result == 2)
        {
            GD.Print($"Ничья! Обе стороны ослаблены.");
            foreach (var a in state.Attackers)
                if (IsInstanceValid(a) && a.HP > 0) RetreatArmy(a);
            if (IsInstanceValid(state.Defender) && state.Defender.HP > 0) RetreatArmy(state.Defender);
            return;
        }

        foreach (var a in state.Attackers)
                if (IsInstanceValid(a) && a.HP > 0 && a.HP < a.MaxHP)
                a.IsWounded = true;

        GD.Print($"Атакующие победили!");
        // Уходящий после боя защитник (последний шанс) уже обрабатывается:
        // RetreatArmy вызван в Battle, дублировать нельзя (там же метка IsWounded).
        // Была при смерти (красный квадрат на начало боя) — разбита без отступления.
        if (IsInstanceValid(state.Defender) && state.Defender.HP > 0
            && !_retreatTargets.ContainsKey(state.Defender.Id))
        {
            if (state.DefenderStartHp <= Army.CriticalHp)
            {
                GD.Print($"Армия #{state.Defender.Id} разбита — была при смерти!");
                _statLosses[state.Defender.PlayerId] = _statLosses.GetValueOrDefault(state.Defender.PlayerId, 0) + 1;
                int deadRegionId = state.Defender.RegionId;
                int deadId = state.Defender.Id;
                _armies.Remove(state.Defender);
                _retreatTimers.Remove(deadId);
                _retreatTargets.Remove(deadId);
                state.Defender.QueueFree();
                UpdateArmyPositions(deadRegionId);
                if (NetworkManager.Instance.IsServer)
                    NetworkManager.Instance.Rpc(nameof(NetworkManager.RpcSyncArmyDestroyed), deadId);
            }
            else
            {
                RetreatArmy(state.Defender);
            }
        }
        // Наступление — через общий механизм шагов: _Process сделает шаг
        // с задержкой MoveDelay, заливкой стрелки, захватом и подсветкой.
        // Слабые победители встанут на привал: отхилятся регеном и пойдут дальше.
        foreach (var a in state.Attackers)
        {
            if (!IsInstanceValid(a)) continue;
            if (!_armyPaths.ContainsKey(a.Id) || _armyPaths[a.Id].Count == 0) continue;
            _armyMoveTimers[a.Id] = GetMoveDelay(a.Id);
        }
    }

    // --- ИИ ---

    private void SpawnAI()
    {
        // Одиночка (не бог): все чужие нации — ИИ.
        if (!GameManager.Instance.IsMultiplayerGame && !GameManager.Instance.IsGodMode)
        {
            for (int i = 0; i < _nations.Count; i++)
            {
                if (i == GameManager.Instance.PlayerNationId) continue;
                SpawnBot(i, GameManager.Instance.AIDifficulty);
            }
            return;
        }

        // Сеть: ботов водит сервер.
        if (GameManager.Instance.IsMultiplayerGame && NetworkManager.Instance.IsServer)
        {
            int total = GameManager.Instance.TotalPlayers;
            int bots = GameManager.Instance.BotCount;
            for (int n = total - bots; n < total && n < _nations.Count; n++)
                SpawnBot(n, 1);
        }
    }

    private void SpawnBot(int nationId, int difficulty)
    {
        var ai = new AIPlayer();
        ai.Setup(this, nationId, difficulty);
        AddChild(ai);
        GD.Print($"ИИ создан для нации {nationId} (сложность {difficulty})");
    }

    // --- Nations ---

    private void InitNations()
    {
        _nations.Add(new Nation(0, "Красный",  new Color(0.9f, 0.25f, 0.25f)));
        _nations.Add(new Nation(1, "Синий",    new Color(0.25f, 0.4f, 0.9f)));
        _nations.Add(new Nation(2, "Зелёный",  new Color(0.25f, 0.85f, 0.4f)));
        _nations.Add(new Nation(3, "Розовый",  new Color(0.9f, 0.45f, 0.7f)));

        GD.Print($"Initialized {_nations.Count} nations:");
        foreach (var n in _nations)
            GD.Print($"  [{n.Id}] {n.Name}");

        int playerId = GameManager.Instance.PlayerNationId;
        foreach (var n in _nations)
            n.IsPlayer = (n.Id == playerId);

        if (playerId >= 0)
            GD.Print($"Player nation: {_nations[playerId].Name}");
        else
            GD.Print("No player nation selected");
    }

    // --- Map generation ---

    private void GenerateMap()
    {
        foreach (var child in GetChildren())
        {
            if (child is Region)
                child.QueueFree();
        }

        var centers = GenerateHexCenters();

        Vector2 screenCenter = GetViewportRect().Size / 2f;
        for (int i = 0; i < centers.Count; i++)
            centers[i] += screenCenter;

        for (int i = 0; i < centers.Count; i++)
        {
            var region = new Region();
            AddChild(region);

            Nation nation = null;
            float distToCenter = centers[i].DistanceTo(screenCenter);
            if (distToCenter > HexCellRadius * Mathf.Sqrt(3f) * 1.01f)
            {
                float angle = Mathf.Atan2(centers[i].Y - screenCenter.Y, centers[i].X - screenCenter.X);
                nation = GetZoneNation(angle);
            }

            Vector2[] vertices = GenerateHexagon(centers[i]);
            region.Initialize(i, $"Region_{i}", vertices, nation);
        }

        GD.Print($"Generated {centers.Count} regions");
    }

    private void GenerateNeighbors()
    {
        var allRegions = GetChildren().OfType<Region>().ToList();
        float threshold = HexCellRadius * Mathf.Sqrt(3f) * 1.15f;

        for (int i = 0; i < allRegions.Count; i++)
        {
            for (int j = i + 1; j < allRegions.Count; j++)
            {
                if (allRegions[i].Center.DistanceTo(allRegions[j].Center) < threshold)
                {
                    allRegions[i].Neighbors.Add(allRegions[j].Id);
                    allRegions[j].Neighbors.Add(allRegions[i].Id);
                }
            }
        }

        GD.Print($"Generated neighbors for {allRegions.Count} regions");

        var r39 = GetRegionById(39);
        if (r39 != null)
            GD.Print($"Region 39: pos={r39.Center}, owner={r39.OwnerId}, neighbors=[{string.Join(",", r39.Neighbors)}]");
    }

    private Nation GetZoneNation(float angle)
    {
        if (angle >= -Mathf.Pi / 4f && angle < Mathf.Pi / 4f)
            return _nations[0];
        if (angle >= Mathf.Pi / 4f && angle < 3f * Mathf.Pi / 4f)
            return _nations[1];
        if (angle >= -3f * Mathf.Pi / 4f && angle < -Mathf.Pi / 4f)
            return _nations[2];
        return _nations[3];
    }

    public Nation GetNationById(int id)
    {
        foreach (var n in _nations)
            if (n.Id == id)
                return n;
        return null;
    }

    public List<Nation> GetAllNations() => _nations;

    public Region GetRegionById(int id)
    {
        foreach (var child in GetChildren())
            if (child is Region region && region.Id == id)
                return region;
        return null;
    }

    public Army GetArmyById(int id)
    {
        foreach (var army in _armies)
            if (army.Id == id)
                return army;
        return null;
    }

    // --- Armies ---

    public Army CreateArmy(int regionId, int playerId, int initialStrength, UnitType type = UnitType.Infantry)
    {
        Region region = GetRegionById(regionId);
        if (region == null) return null;

        var army = new Army();
        army.Initialize(_nextArmyId++, regionId, playerId, initialStrength, type);
        army.Position = region.Center;
        army.ZIndex = 1;
        AddChild(army);
        _armies.Add(army);

        GD.Print($"Created Army #{army.Id} in {region.RegionName} (strength: {initialStrength}, type: {type})");
        return army;
    }

    // --- Economy ---

    private void CollectIncome()
    {
        GameManager.Instance.Day++;

        foreach (var nation in _nations)
        {
            int id = nation.Id;
            int income = GameManager.BaseIncomeFor(id) + GameManager.Instance.Mines[id] * GameManager.MineIncome;
            GameManager.Instance.Gold[id] += income;

            // Наука: базовый тик + университеты (upkeep золотом за каждое очко).
            int uniWant = GameManager.Instance.Universities.GetValueOrDefault(id, 0) * GameManager.UniversityPoints;
            int afford = System.Math.Min(uniWant, GameManager.Instance.Gold[id] / GameManager.UniversityUpkeepPerPoint);
            int upkeepPaid = afford * GameManager.UniversityUpkeepPerPoint;
            GameManager.Instance.Gold[id] -= upkeepPaid;
            GameManager.Instance.DayUpkeep[id] = upkeepPaid;
            GameManager.Instance.Research[id] =
                GameManager.Instance.Research.GetValueOrDefault(id, 0) + GameManager.BaseResearch + afford;

            if (GameManager.Instance.BuildTimers[id] > 0)
            {
                GameManager.Instance.BuildTimers[id] -= GameManager.IncomeInterval;
                if (GameManager.Instance.BuildTimers[id] <= 0)
                {
                    GameManager.Instance.BuildTimers[id] = 0;
                    CompleteOrder(nation);
                }
            }
        }

        // Истечение пактов (раз в тик, детерминировано на всех пирах).
        var expiredPacts = new List<int>();
        foreach (var kvp in new List<KeyValuePair<int, float>>(GameManager.Instance.PactTimers))
        {
            float left = kvp.Value - GameManager.IncomeInterval;
            if (left <= 0f)
                expiredPacts.Add(kvp.Key);
            else
                GameManager.Instance.PactTimers[kvp.Key] = left;
        }
        foreach (int key in expiredPacts)
        {
            GameManager.Instance.PactTimers.Remove(key);
            GameManager.Instance.Relations[key] = (int)RelationState.Neutral;
            GD.Print($"Пакт истёк, снова нейтралитет (пара {key})");
        }

        UpdateGoldHUD();
    }

    private void CompleteOrder(Nation nation)
    {
        int id = nation.Id;
        string order = GameManager.Instance.BuildOrders.GetValueOrDefault(id, "");
        GameManager.Instance.BuildOrders[id] = "";

        // Форты применяются мгновенно при заказе — через очередь идут
        // только шахты и университеты. Пустой заказ = ничего не делаем.
        if (order == "uni")
        {
            GameManager.Instance.Universities[id] =
                GameManager.Instance.Universities.GetValueOrDefault(id, 0) + 1;
            GD.Print($"Университет построен для {nation.Name}! Всего: {GameManager.Instance.Universities[id]}");
            return;
        }

        if (order == "mine")
        {
            GameManager.Instance.Mines[id]++;
            GD.Print($"Шахта построена для {nation.Name}! Всего: {GameManager.Instance.Mines[id]}");
        }
    }

    public bool BuildMineForPlayer(int regionId = -1)
    {
        bool god = GameManager.Instance.IsGodMode;
        int playerId = god && regionId >= 0
            ? GetRegionById(regionId)?.OwnerId ?? -1
            : GameManager.Instance.PlayerNationId;
        if (playerId < 0) return false;
        if (GameManager.Instance.Mines[playerId] >= GameManager.MaxMines) return false;
        int mineCost = GameManager.GetMineCost(GameManager.Instance.Mines[playerId]);
        if (!god)
        {
            if (GameManager.Instance.Gold[playerId] < mineCost) return false;
            if (GameManager.Instance.BuildTimers[playerId] > 0) return false;
            GameManager.Instance.Gold[playerId] -= mineCost;
        }
        else if (GameManager.Instance.BuildTimers[playerId] > 0) return false;

        GameManager.Instance.BuildTimers[playerId] = GameManager.BuildTime;
        GameManager.Instance.BuildOrders[playerId] = "mine";
        AudioHub.Instance?.PlayBuild();
        GD.Print($"Постройка шахты начата для {GetNationById(playerId).Name}");
        return true;
    }

    private void UpdateGoldHUD()
    {
        if (_goldValueLabel == null || _scienceValueLabel == null) return;
        int playerId = GameManager.Instance.PlayerNationId;
        if (playerId < 0) return;
        _goldValueLabel.Text = $"{GameManager.Instance.Gold.GetValueOrDefault(playerId, 0)}";
        _scienceValueLabel.Text = $"{GameManager.Instance.Research.GetValueOrDefault(playerId, 0)}";
        if (_dayLabel != null)
            _dayLabel.Text = $"День {GameManager.Instance.Day}";
    }

    public bool IsCapitalRegion(int regionId)
    {
        if (GameManager.Instance.IsGodMode)
            return _capitalRegions.ContainsValue(regionId);
        int playerId = GameManager.Instance.PlayerNationId;
        if (playerId < 0) return false;
        return _capitalRegions.ContainsKey(playerId) && _capitalRegions[playerId] == regionId;
    }

    public bool CreateArmyForPlayer(int regionId, int soldiers, UnitType type = UnitType.Infantry)
    {
        bool god = GameManager.Instance.IsGodMode;
        int playerId = god
            ? GetRegionById(regionId)?.OwnerId ?? -1
            : GameManager.Instance.PlayerNationId;
        if (playerId < 0) return false;

        if (!_capitalRegions.ContainsKey(playerId)) return false;
        if (_capitalRegions[playerId] != regionId) return false;

        if (CountOwnArmies(regionId, playerId) >= GameManager.MaxArmiesPerRegion) return false;
        if (!god && !GameManager.CanRecruit(playerId, type)) return false;

        int armyCount = GetArmiesOfNation(playerId).Count;
        int cost = GameManager.GetArmyCost(soldiers, type, armyCount, playerId);
        if (!god)
        {
            int gold = GameManager.Instance.Gold.GetValueOrDefault(playerId, 0);
            if (gold < cost) return false;
            GameManager.Instance.Gold[playerId] -= cost;
        }

        var army = CreateArmy(regionId, playerId, soldiers, type);
        if (army == null)
        {
            if (!god)
                GameManager.Instance.Gold[playerId] += cost;
            return false;
        }

        UpdateArmyPositions(regionId);
        UpdateGoldHUD();
        AudioHub.Instance?.PlaySpawn();
        GD.Print($"Игрок создал армию: {soldiers} солдат за {(god ? 0 : cost)} золота");
        return true;
    }

    // --- Server-side methods for multiplayer ---

    public void ServerCreateArmy(int regionId, int soldiers, int playerId, UnitType type = UnitType.Infantry)
    {
        if (playerId < 0) return;
        if (!System.Enum.IsDefined(typeof(UnitType), type)) return;
        if (!GameManager.CanRecruit(playerId, type)) return;
        if (CountOwnArmies(regionId, playerId) >= GameManager.MaxArmiesPerRegion) return;

        int armyCount = GetArmiesOfNation(playerId).Count;
        int cost = GameManager.GetArmyCost(soldiers, type, armyCount, playerId);
        int gold = GameManager.Instance.Gold.GetValueOrDefault(playerId, 0);
        if (gold < cost) return;

        GameManager.Instance.Gold[playerId] -= cost;

        var army = CreateArmy(regionId, playerId, soldiers, type);
        if (army == null)
        {
            GameManager.Instance.Gold[playerId] += cost;
            return;
        }

        UpdateArmyPositions(regionId);

        AudioHub.Instance?.PlaySpawn();

        if (NetworkManager.Instance.IsServer)
        {
            NetworkManager.Instance.Rpc(nameof(NetworkManager.RpcSyncArmyCreated), army.Id, regionId, playerId, soldiers, (int)type);
            SyncEconomyToClients(playerId);
        }
    }

    public bool BuildUniversityForPlayer()
    {
        bool god = GameManager.Instance.IsGodMode;
        int playerId = GameManager.Instance.PlayerNationId;
        if (playerId < 0) return false;
        if (GameManager.Instance.Universities.GetValueOrDefault(playerId, 0) >= GameManager.MaxUniversities)
            return false;

        if (!god)
        {
            if (GameManager.Instance.Gold.GetValueOrDefault(playerId, 0) < GameManager.UniversityCost)
                return false;
            if (GameManager.Instance.BuildTimers.GetValueOrDefault(playerId, 0f) > 0) return false;
            GameManager.Instance.Gold[playerId] -= GameManager.UniversityCost;
        }
        else if (GameManager.Instance.BuildTimers.GetValueOrDefault(playerId, 0f) > 0) return false;

        GameManager.Instance.BuildTimers[playerId] = GameManager.BuildTime;
        GameManager.Instance.BuildOrders[playerId] = "uni";
        AudioHub.Instance?.PlayBuild();
        GD.Print($"Университет строится для {GetNationById(playerId).Name}");
        return true;
    }

    public void ServerBuildUniversity(int playerId)
    {
        if (playerId < 0) return;
        if (GameManager.Instance.Universities.GetValueOrDefault(playerId, 0) >= GameManager.MaxUniversities)
            return;
        if (GameManager.Instance.Gold.GetValueOrDefault(playerId, 0) < GameManager.UniversityCost) return;
        if (GameManager.Instance.BuildTimers.GetValueOrDefault(playerId, 0f) > 0) return;

        GameManager.Instance.Gold[playerId] -= GameManager.UniversityCost;
        GameManager.Instance.BuildTimers[playerId] = GameManager.BuildTime;
        GameManager.Instance.BuildOrders[playerId] = "uni";
        AudioHub.Instance?.PlayBuild();
        SyncEconomyToClients(playerId);
    }

    public static bool CanResearch(int playerId, int techId)
    {
        if (techId < 0 || techId > 10) return false;
        if (playerId < 0) return false;
        if (GameManager.HasTech(playerId, techId)) return false;
        foreach (int req in GameManager.TechRequires(techId))
        {
            if (!GameManager.HasTech(playerId, req)) return false;
        }
        if (GameManager.Instance.Gold.GetValueOrDefault(playerId, 0) < GameManager.TechGoldCost(techId))
            return false;
        if (GameManager.Instance.Research.GetValueOrDefault(playerId, 0) < GameManager.TechResearchCost(techId))
            return false;
        return true;
    }

    public bool ResearchTechForPlayer(int techId)
    {
        bool god = GameManager.Instance.IsGodMode;
        int playerId = GameManager.Instance.PlayerNationId;
        if (playerId < 0 || techId < 0 || techId > 10 || GameManager.HasTech(playerId, techId)) return false;
        foreach (int req in GameManager.TechRequires(techId))
        {
            if (!GameManager.HasTech(playerId, req)) return false;
        }

        if (!god)
        {
            if (!CanResearch(playerId, techId)) return false;
            GameManager.Instance.Gold[playerId] -= GameManager.TechGoldCost(techId);
            GameManager.Instance.Research[playerId] -= GameManager.TechResearchCost(techId);
        }

        GameManager.Instance.TechMask[playerId] =
            GameManager.Instance.TechMask.GetValueOrDefault(playerId, 0) | (1 << techId);
        AudioHub.Instance?.PlayBuild();
        UpdateGoldHUD();
        GD.Print($"{GetNationById(playerId).Name} изучил: {GameManager.TechName(techId)}");
        return true;
    }

    public void ServerResearchTech(int playerId, int techId)
    {
        if (!CanResearch(playerId, techId)) return;
        GameManager.Instance.Gold[playerId] -= GameManager.TechGoldCost(techId);
        GameManager.Instance.Research[playerId] -= GameManager.TechResearchCost(techId);
        GameManager.Instance.TechMask[playerId] =
            GameManager.Instance.TechMask.GetValueOrDefault(playerId, 0) | (1 << techId);
        AudioHub.Instance?.PlayBuild();
        GD.Print($"Исследовано: {GameManager.TechName(techId)} (нация {playerId})");
        SyncEconomyToClients(playerId);
    }

    private void SyncEconomyToClients(int playerId)
    {
        if (!NetworkManager.Instance.IsServer) return;
        NetworkManager.Instance.Rpc(nameof(NetworkManager.RpcSyncEconomy),
            playerId,
            GameManager.Instance.Gold.GetValueOrDefault(playerId, 0),
            GameManager.Instance.Mines.GetValueOrDefault(playerId, 0),
            GameManager.Instance.BuildTimers.GetValueOrDefault(playerId, 0f),
            GameManager.Instance.Research.GetValueOrDefault(playerId, 0),
            GameManager.Instance.Universities.GetValueOrDefault(playerId, 0),
            GameManager.Instance.TechMask.GetValueOrDefault(playerId, 0),
            GameManager.Instance.BuildOrders.GetValueOrDefault(playerId, ""));
    }

    // --- Дипломатия ---

    public static bool CanSetRelation(int a, int b)
    {
        if (a < 0 || b < 0 || a == b) return false;
        if (a > 3 || b > 3) return false;
        return true;
    }

    private void ApplyRelation(int a, int b, RelationState state)
    {
        int key = GameManager.RelationKey(a, b);
        GameManager.Instance.Relations[key] = (int)state;
        if (state == RelationState.Pact)
            GameManager.Instance.PactTimers[key] = GameManager.PactDuration;
        else
            GameManager.Instance.PactTimers.Remove(key);

        string an = GetNationById(a)?.Name ?? "?";
        string bn = GetNationById(b)?.Name ?? "?";
        GD.Print(state == RelationState.War
            ? $"{an} объявил войну: {bn}!"
            : $"Пакт о ненападении: {an} — {bn}");
    }

    public bool DeclareWarForPlayer(int other)
    {
        int me = GameManager.Instance.PlayerNationId;
        if (!CanSetRelation(me, other)) return false;
        var current = GameManager.GetRelation(me, other);
        // Пакт блокирует войну — только дождаться истечения.
        if (current == RelationState.Pact) return false;
        if (current == RelationState.War) return false;
        ApplyRelation(me, other, RelationState.War);
        AudioHub.Instance?.PlayClick();
        return true;
    }

    public bool MakePactForPlayer(int other)
    {
        int me = GameManager.Instance.PlayerNationId;
        if (!CanSetRelation(me, other)) return false;
        if (GameManager.GetRelation(me, other) == RelationState.Pact) return false;
        ApplyRelation(me, other, RelationState.Pact);
        AudioHub.Instance?.PlayClick();
        return true;
    }

    public bool ProposePeaceForPlayer(int other)
    {
        int me = GameManager.Instance.PlayerNationId;
        if (!CanSetRelation(me, other)) return false;
        // Мир — только из войны (уставшим). Из пакта/нейтралитета нечего прекращать.
        if (GameManager.GetRelation(me, other) != RelationState.War) return false;
        ApplyRelation(me, other, RelationState.Neutral);
        AudioHub.Instance?.PlayClick();
        return true;
    }

    public void ServerSetRelation(int nationA, int nationB, RelationState state)
    {
        if (!CanSetRelation(nationA, nationB)) return;
        var current = GameManager.GetRelation(nationA, nationB);
        if (current == state) return;
        // Пакт блокирует войну; мир — только из войны.
        if (state == RelationState.War && current == RelationState.Pact) return;
        if (state == RelationState.Neutral && current != RelationState.War) return;
        ApplyRelation(nationA, nationB, state);
        SyncRelationToClients(nationA, nationB);
    }

    private void SyncRelationToClients(int nationA, int nationB)
    {
        if (!NetworkManager.Instance.IsServer) return;
        int key = GameManager.RelationKey(nationA, nationB);
        NetworkManager.Instance.Rpc(nameof(NetworkManager.RpcSyncRelation),
            nationA, nationB,
            (int)GameManager.GetRelation(nationA, nationB),
            GameManager.Instance.PactTimers.GetValueOrDefault(key, 0f));
    }

    public void ServerMoveArmy(int armyId, int targetId)
    {
        QueueArmyMove(armyId, targetId);

        if (NetworkManager.Instance.IsServer)
            NetworkManager.Instance.Rpc(nameof(NetworkManager.RpcSyncArmyMove), armyId, targetId);
    }

    public bool BuildFortForPlayer(int regionId = -1)
    {
        bool god = GameManager.Instance.IsGodMode;
        int playerId = god && regionId >= 0
            ? GetRegionById(regionId)?.OwnerId ?? -1
            : GameManager.Instance.PlayerNationId;
        if (playerId < 0) return false;
        if (!_capitalRegions.TryGetValue(playerId, out int capId)) return false;
        if (regionId < 0) regionId = capId;
        if (regionId != capId) return false;

        var region = GetRegionById(capId);
        if (region == null || region.FortLevel >= GameManager.MaxFortLevel) return false;

        int cost = GameManager.GetFortCost(region.FortLevel + 1);
        if (!god)
        {
            if (GameManager.Instance.Gold[playerId] < cost) return false;
            if (GameManager.Instance.BuildTimers[playerId] > 0) return false;
            GameManager.Instance.Gold[playerId] -= cost;
        }
        else if (GameManager.Instance.BuildTimers[playerId] > 0) return false;

        GameManager.Instance.BuildTimers[playerId] = GameManager.BuildTime;
        region.FortLevel++;
        RefreshCapitalMarkers();
        UpdateGoldHUD();
        AudioHub.Instance?.PlayBuild();
        GD.Print($"Укрепления {GetNationById(playerId).Name}: уровень {region.FortLevel}");
        return true;
    }

    public void ServerBuildFort(int playerId)
    {
        if (playerId < 0) return;
        if (!_capitalRegions.TryGetValue(playerId, out int capId)) return;
        var region = GetRegionById(capId);
        if (region == null || region.FortLevel >= GameManager.MaxFortLevel) return;

        int cost = GameManager.GetFortCost(region.FortLevel + 1);
        if (GameManager.Instance.Gold[playerId] < cost) return;
        if (GameManager.Instance.BuildTimers[playerId] > 0) return;

        GameManager.Instance.Gold[playerId] -= cost;
        GameManager.Instance.BuildTimers[playerId] = GameManager.BuildTime;
        region.FortLevel++;
        RefreshCapitalMarkers();
        AudioHub.Instance?.PlayBuild();

        if (NetworkManager.Instance.IsServer)
        {
            SyncRegionCapture(region);
            SyncEconomyToClients(playerId);
        }
    }
    public void ServerBuildMine(int playerId)
    {
        if (playerId < 0) return;
        if (GameManager.Instance.Mines[playerId] >= GameManager.MaxMines) return;
        int mineCost = GameManager.GetMineCost(GameManager.Instance.Mines[playerId]);
        if (GameManager.Instance.Gold[playerId] < mineCost) return;
        if (GameManager.Instance.BuildTimers[playerId] > 0) return;

        GameManager.Instance.Gold[playerId] -= mineCost;
        GameManager.Instance.BuildTimers[playerId] = GameManager.BuildTime;
        GameManager.Instance.BuildOrders[playerId] = "mine";
        AudioHub.Instance?.PlayBuild();

        SyncEconomyToClients(playerId);
    }

    public async void MoveArmyToRegion(int armyId, int targetRegionId)
    {
        var army = GetArmyById(armyId);
        var target = GetRegionById(targetRegionId);
        if (army == null || target == null)
        {
            GD.Print("Армия или регион не найдены!");
            return;
        }

        var current = GetRegionById(army.RegionId);
        if (current == null || !current.Neighbors.Contains(targetRegionId))
        {
            GD.Print("Нет пути! Регионы не соседние.");
            return;
        }

        GD.Print($"Армия #{army.Id} перемещается: {current.RegionName} -> {target.RegionName}");

        army.RegionId = targetRegionId;
        army.Position = target.Center;
        UpdateArmyPositions(army.RegionId);
        UpdateArmyPositions(current.Id);

        var enemy = GetEnemyArmyInRegion(targetRegionId, army.PlayerId);

        if (enemy != null)
        {
            var attackers = new List<Army> { army };
            _armiesInBattle.Add(army.Id);
            _armiesInBattle.Add(enemy.Id);
            int result = await Battle(attackers, enemy);
            _armiesInBattle.Remove(army.Id);
            _armiesInBattle.Remove(enemy.Id);
            if (result == 0 || result == 2)
            {
                if (IsInstanceValid(army) && army.HP > 0 && army.HP <= 10 && !army.IsWounded)
                    RetreatArmy(army);
                else if (IsInstanceValid(army) && army.IsWounded && army.HP <= 10)
                {
                    _armies.Remove(army);
                    army.QueueFree();
                    GD.Print($"Раненая Армия #{army.Id} уничтожена!");
                }
            }
            else if (result == 1 && IsInstanceValid(army) && army.HP > 0 && army.HP < army.MaxHP)
            {
                army.IsWounded = true;
            }
        }
        else
        {
            if (target.OwnerId != army.PlayerId)
            {
                target.OwnerId = army.PlayerId;
                target.OwningNation = GetNationById(army.PlayerId);
                target.Color = target.OwningNation != null ? target.OwningNation.Color : Colors.White;
                SyncRegionCapture(target);
                GD.Print($"Регион {target.RegionName} (Id={target.Id}) захвачен! OwnerId={target.OwnerId}");
            }
            else
            {
                GD.Print($"Регион {target.RegionName} (Id={target.Id}) уже наш! OwnerId={target.OwnerId}");
            }
        }
    }

    private Army GetEnemyArmyInRegion(int regionId, int playerId)
    {
        foreach (var army in _armies)
            if (army.RegionId == regionId && army.PlayerId != playerId)
                return army;
        return null;
    }

    private List<Army> GetAllArmiesInRegion(int regionId)
    {
        var result = new List<Army>();
        foreach (var army in _armies)
            if (army.RegionId == regionId)
                result.Add(army);
        return result;
    }

    public void UpdateArmyPositions(int regionId)
    {
        var region = GetRegionById(regionId);
        if (region == null) return;

        var armies = GetAllArmiesInRegion(regionId);
        int count = armies.Count;

        if (count == 1)
        {
            armies[0].Position = region.Center;
        }
        else if (count > 1)
        {
            float offsetDist = 18f;
            for (int i = 0; i < count; i++)
            {
                float angle = Mathf.Tau * i / count;
                Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * offsetDist;
                armies[i].Position = region.Center + offset;
            }
        }
    }

    private void DestroyDefender(Army defender, List<Army> attackers)
    {
        GD.Print($"Армия #{defender.Id} уничтожена!");
        _statLosses[defender.PlayerId] = _statLosses.GetValueOrDefault(defender.PlayerId, 0) + 1;
        int defenderId = defender.Id;
        int defenderRegionId = defender.RegionId;
        var region = GetRegionById(defenderRegionId);
        if (region != null && attackers.Count > 0)
        {
            int attackerId = attackers[0].PlayerId;
            region.OwnerId = attackerId;
            region.OwningNation = GetNationById(attackerId);
            region.Color = region.OwningNation != null ? region.OwningNation.Color : Colors.White;
            SyncRegionCapture(region);
            _statCaptures[attackerId] = _statCaptures.GetValueOrDefault(attackerId, 0) + 1;
            AudioHub.Instance?.PlayCapture();
            FlashCapture(region.Id);
            GD.Print($"Регион {region.RegionName} захвачен!");
        }

        _armies.Remove(defender);
        _retreatTimers.Remove(defender.Id);
        _retreatTargets.Remove(defender.Id);
        defender.QueueFree();
        UpdateArmyPositions(defenderRegionId);
        if (NetworkManager.Instance.IsServer)
            NetworkManager.Instance.Rpc(nameof(NetworkManager.RpcSyncArmyDestroyed), defenderId);
    }

    private void PurgeDeadArmies(List<Army> armies)
    {
        foreach (var a in armies)
        {
            if (!IsInstanceValid(a) || a.HP > 0) continue;
            GD.Print($"Армия #{a.Id} погибла в бою!");
            _statLosses[a.PlayerId] = _statLosses.GetValueOrDefault(a.PlayerId, 0) + 1;
            int armyId = a.Id;
            int regionId = a.RegionId;
            _armies.Remove(a);
            _retreatTimers.Remove(a.Id);
            _retreatTargets.Remove(a.Id);
            a.QueueFree();
            UpdateArmyPositions(regionId);
            if (NetworkManager.Instance.IsServer)
                NetworkManager.Instance.Rpc(nameof(NetworkManager.RpcSyncArmyDestroyed), armyId);
        }
        armies.RemoveAll(a => !IsInstanceValid(a) || a.HP <= 0);
    }

    private async System.Threading.Tasks.Task<int> Battle(List<Army> attackers, Army defender)
    {
        _statBattles++;
        string attackerNames = string.Join(", ", attackers.Select(a => $"#{a.Id}(солдат:{a.Soldiers})"));
        GD.Print($"Битва! Армии [{attackerNames}] vs Армия #{defender.Id} (солдат:{defender.Soldiers})!");

        int round = 0;
        while (true)
        {
            attackers.RemoveAll(a => !IsInstanceValid(a) || a.HP <= 0);
            if (attackers.Count == 0)
            {
                GD.Print("Все атакующие уничтожены!");
                if (IsInstanceValid(defender) && defender.HP <= 0)
                    DestroyDefender(defender, attackers);
                return 0;
            }

            if (defender.HP <= 0)
            {
                DestroyDefender(defender, attackers);
                return 1;
            }

            round++;
            await ToSignal(GetTree().CreateTimer(BattleRoundDelay), SceneTreeTimer.SignalName.Timeout);

            float totalAtkPower = 0f;
            bool groupAttack = attackers.Count > 1;
            foreach (var a in attackers)
            {
                float tact = groupAttack && GameManager.HasTech(a.PlayerId, 9) ? 1.15f : 1f;
                totalAtkPower += a.Soldiers * UnitStats.DamageMult(a.Type)
                    * GameManager.DamageTechMult(a.PlayerId) * tact;
            }

            float defPower = Mathf.Max(1f, defender.Soldiers * UnitStats.DamageMult(defender.Type)
                * GameManager.DamageTechMult(defender.PlayerId));
            float atkPower = Mathf.Max(1f, totalAtkPower);

            // Детерминированный рандом: сид от боя и раунда — все пиры
            // получают одинаковый множитель при тех же участниках.
            var battleRng = new RandomNumberGenerator();
            battleRng.Seed = (ulong)defender.Id * 1000003UL + (ulong)round;
            float randMult = battleRng.RandfRange(0.7f, 1.3f);

            // Урон зависит от соотношения эффективной силы (солдаты × тип):
            // большая армия бьёт сильнее и получает меньше.
            // Потолок держит бой читаемым (не короче ~11 раундов).
            // Укрепления столицы снижают входящий урон защитнику.
            float atkRatio = Mathf.Clamp(atkPower / defPower, BattleRatioMin, BattleRatioMax);
            float defRatio = Mathf.Clamp(defPower / atkPower, BattleRatioMin, BattleRatioMax);

            float fortMult = 1f;
            var defRegion = GetRegionById(defender.RegionId);
            if (defRegion != null && defRegion.FortLevel > 0)
                fortMult = 1f - GameManager.FortDamageReduction * defRegion.FortLevel;

            int defenderHPLoss = Mathf.Clamp((int)(BattleBaseHit * atkRatio * randMult * 0.7f * fortMult), 1, BattleMaxHitPerRound);
            int defenderHpBefore = defender.HP;
            defender.HP -= defenderHPLoss;

            foreach (var a in attackers)
            {
                if (!IsInstanceValid(a) || a.HP <= 0) continue;
                float tactShare = groupAttack && GameManager.HasTech(a.PlayerId, 9) ? 1.15f : 1f;
                float share = a.Soldiers * UnitStats.DamageMult(a.Type)
                    * GameManager.DamageTechMult(a.PlayerId) * tactShare / atkPower;
                int loss = Mathf.Clamp((int)(BattleBaseHit * defRatio * randMult * share), 1, BattleMaxHitPerRound);
                a.HP -= loss;
                if (IsInstanceValid(a)) a.QueueRedraw();
            }

            // Серверный синк HP каждый раунд: клиенты подхватывают значения,
            // развилки «слаб/не слаб» и длины боёв сходятся.
            if (NetworkManager.Instance.IsServer)
            {
                NetworkManager.Instance.Rpc(nameof(NetworkManager.RpcSyncArmyHP),
                    defender.Id, defender.HP, defender.Soldiers, defender.IsWounded);
                foreach (var a in attackers)
                {
                    if (!IsInstanceValid(a)) continue;
                    NetworkManager.Instance.Rpc(nameof(NetworkManager.RpcSyncArmyHP),
                        a.Id, a.HP, a.Soldiers, a.IsWounded);
                }
            }

            // Эффекты раунда: вспышки урона (звук ударов и тряска отключены).
            // До проверки побега — решающий удар тоже виден.
            if (IsInstanceValid(defender)) defender.FlashHit();
            foreach (var a in attackers)
                if (IsInstanceValid(a)) a.FlashHit();

            // Последний шанс: ушедший в минус защитник уходит после боя с 1 HP —
            // но только если до смертельного раунда не был при смерти
            // (красноквадратные не сбегают, см. правило красного квадрата).
            if (IsInstanceValid(defender) && defender.HP <= 0)
            {
                PurgeDeadArmies(attackers);
                if (defenderHpBefore > Army.CriticalHp)
                {
                    defender.HP = 1;
                    defender.QueueRedraw();
                    GD.Print($"Армия #{defender.Id} уцелела с 1 HP и уходит после боя!");
                    RetreatArmy(defender);
                }
                else
                {
                    GD.Print($"Армия #{defender.Id} добита (была при смерти)!");
                    DestroyDefender(defender, attackers);
                }
                return 1;
            }

            if (IsInstanceValid(defender)) defender.QueueRedraw();

            if (_selectedArmy != null && IsInstanceValid(_selectedArmy))
                _regionPanel.ShowArmyInfo(_selectedArmy.Id, _selectedArmy.Soldiers, _selectedArmy.HP, _selectedArmy.MaxHP, _selectedArmy.Type);
            else if (_selectedArmy != null)
                _regionPanel.HideArmyInfo();

            string logAttackers = string.Join(", ", attackers.Where(a => IsInstanceValid(a)).Select(a => $"#{a.Id}:{a.Soldiers}"));
            GD.Print($"Раунд {round}: Атакующие [{logAttackers}] (-{defenderHPLoss} HP врага), Армия #{defender.Id} HP={defender.HP}");

            bool defendersWeak = defender.HP <= 10;
            bool attackersWeak = true;
            foreach (var a in attackers)
                if (IsInstanceValid(a) && a.HP > 10) { attackersWeak = false; break; }

            if (defendersWeak || attackersWeak)
            {
                GD.Print($"Бой прекращён! Защитник: {defender.HP}HP, Атакующие Weak={attackersWeak}");
                PurgeDeadArmies(attackers);
                if (defendersWeak && attackersWeak) return 2;
                if (defendersWeak) return 1;
                return 0;
            }
        }
    }

    // --- Совмещение армий (Shift-выбор пары) ---

    private Army PickArmyAt(Vector2 clickPos, int excludeId = -1)
    {
        Army best = null;
        float bestDist = float.MaxValue;
        foreach (var army in _armies)
        {
            if (!IsInstanceValid(army) || army.Id == excludeId) continue;
            float d = clickPos.DistanceTo(army.Position);
            if (d < army.GetRadius() + 5f && d < bestDist)
            {
                bestDist = d;
                best = army;
            }
        }
        return best;
    }

    private bool IsMergeable(Army a, Army b)
    {
        if (a == null || b == null || !IsInstanceValid(a) || !IsInstanceValid(b)) return false;
        if (a.Id == b.Id) return false;
        if (a.PlayerId != b.PlayerId) return false;
        if (a.RegionId != b.RegionId) return false;
        if (a.Type != b.Type) return false;
        if (_armiesInBattle.Contains(a.Id) || _armiesInBattle.Contains(b.Id)) return false;
        if (_retreatTargets.ContainsKey(a.Id) || _retreatTargets.ContainsKey(b.Id)) return false;
        return true;
    }

    // Пара развалилась сама (смерть/бой/отход) — снять без спроса.
    // Несовпадение клетки/типа — оставить, показать причину.
    private bool IsPairBroken()
    {
        var a = _selectedArmy;
        var b = _secondArmy;
        if (b == null) return false;
        if (a == null || !IsInstanceValid(a) || !IsInstanceValid(b)) return true;
        if (_armiesInBattle.Contains(a.Id) || _armiesInBattle.Contains(b.Id)) return true;
        if (_retreatTargets.ContainsKey(a.Id) || _retreatTargets.ContainsKey(b.Id)) return true;
        return false;
    }

    private string MergeReason()
    {
        var a = _selectedArmy;
        var b = _secondArmy;
        if (a == null || b == null || !IsInstanceValid(a) || !IsInstanceValid(b)) return "";
        if (a.PlayerId != b.PlayerId) return "Чужие армии";
        if (a.RegionId != b.RegionId) return "Разные клетки";
        if (a.Type != b.Type) return "Разный тип";
        if (_armiesInBattle.Contains(a.Id) || _armiesInBattle.Contains(b.Id)) return "В бою";
        if (_retreatTargets.ContainsKey(a.Id) || _retreatTargets.ContainsKey(b.Id)) return "Отступает";
        return "";
    }

    private void ClearSecondArmy()
    {
        if (_secondArmy != null && IsInstanceValid(_secondArmy))
        {
            _secondArmy.IsSelected = false;
            _secondArmy.QueueRedraw();
        }
        _secondArmy = null;
        _regionPanel.HideMergeInfo();
    }

    private void RefreshMergeUI()
    {
        if (_regionPanel == null) return;
        if (_secondArmy != null && IsInstanceValid(_secondArmy)
            && _selectedArmy != null && IsInstanceValid(_selectedArmy))
        {
            if (IsMergeable(_selectedArmy, _secondArmy))
            {
                int total = _selectedArmy.Soldiers + _secondArmy.Soldiers;
                int result = System.Math.Min(100, total);
                int loss = total - result;
                string txt = loss > 0 ? $"Совместить: {result} (потеря {loss})" : $"Совместить: {result}";
                _regionPanel.ShowMergeInfo(txt, true);
            }
            else
            {
                _regionPanel.ShowMergeInfo(MergeReason(), false);
            }
            return;
        }
        _regionPanel.HideMergeInfo();
    }

    private bool DoMerge(Army keep, Army remove)
    {
        int cap = GameManager.MaxSoldiersFor(keep.PlayerId);
        int total = keep.Soldiers + remove.Soldiers;
        int result = System.Math.Min(cap, total);
        keep.HP = System.Math.Min(keep.MaxHP,
            (keep.HP * keep.Soldiers + remove.HP * remove.Soldiers) / System.Math.Max(1, total));
        keep.Soldiers = result;
        keep.IsWounded = keep.HP < keep.MaxHP;
        keep.QueueRedraw();

        int regionId = keep.RegionId;
        int removeId = remove.Id;
        _armies.Remove(remove);
        _retreatTimers.Remove(removeId);
        _retreatTargets.Remove(removeId);
        remove.QueueFree();
        UpdateArmyPositions(regionId);

        if (NetworkManager.Instance.IsServer)
            NetworkManager.Instance.Rpc(nameof(NetworkManager.RpcSyncArmyDestroyed), removeId);
        return true;
    }

    public bool MergeArmies(int keepId, int removeId)
    {
        var a = GetArmyById(keepId);
        var b = GetArmyById(removeId);
        if (!IsMergeable(a, b)) return false;

        if (!DoMerge(a, b)) return false;

        AudioHub.Instance?.PlayClick();
        GD.Print($"Армии #{keepId} + #{removeId} совмещены: {a.Soldiers} солдат, HP={a.HP}");
        ClearSecondArmy();
        _regionPanel.ShowArmyInfo(a.Id, a.Soldiers, a.HP, a.MaxHP, a.Type);
        return true;
    }

    public void ServerMergeArmies(int keepId, int removeId, int playerId)
    {
        var a = GetArmyById(keepId);
        var b = GetArmyById(removeId);
        if (a == null || b == null || !IsInstanceValid(a) || !IsInstanceValid(b)) return;
        if (a.PlayerId != playerId || b.PlayerId != playerId) return;
        if (!IsMergeable(a, b)) return;

        if (!DoMerge(a, b)) return;

        if (NetworkManager.Instance.IsServer)
            NetworkManager.Instance.Rpc(nameof(NetworkManager.RpcSyncArmyHP),
                a.Id, a.HP, a.Soldiers, a.IsWounded);
    }

    public void TryMergeSelected()
    {
        if (!IsMergeable(_selectedArmy, _secondArmy)) return;
        if (GameManager.Instance.IsMultiplayerGame)
        {
            NetworkManager.Instance.SendCommand(new GameCommand
            {
                Type = CommandType.MergeArmies,
                PlayerId = GameManager.Instance.PlayerNationId,
                Arg1 = _selectedArmy.Id,
                Arg2 = _secondArmy.Id,
            });
        }
        else
        {
            MergeArmies(_selectedArmy.Id, _secondArmy.Id);
        }
    }

    // --- Input ---

    public override void _UnhandledInput(InputEvent @event)
    {
        // ESC обрабатывается глобально в AudioHub (он Always и живёт на паузе).
        if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed)
        {
            if (mouseEvent.ButtonIndex == MouseButton.Left)
            {
                // После конца игры, в режиме наблюдателя и при открытых
                // вкладках — только камера (ПКМ-панорама работает всегда).
                if (_gameOver || IsObserver() || IsTabPanelOpen()) return;

                Vector2 clickPos = GetGlobalMousePosition();

                // Shift-клик при выбранной армии — вторая в пару, раньше приказов:
                // иначе клик по армии перехватывает ветка перемещения.
                if (mouseEvent.ShiftPressed
                    && _selectedArmy != null && IsInstanceValid(_selectedArmy))
                {
                    var second = PickArmyAt(clickPos, _selectedArmy.Id);
                    if (second != null)
                    {
                        if (GameManager.Instance.IsMultiplayerGame && second.PlayerId != GameManager.Instance.PlayerNationId)
                            return;
                        if (_armiesInBattle.Contains(second.Id) || _retreatTargets.ContainsKey(second.Id))
                            return;
                        if (_secondArmy != null && IsInstanceValid(_secondArmy) && _secondArmy.Id == second.Id)
                        {
                            ClearSecondArmy();
                        }
                        else
                        {
                            ClearSecondArmy();
                            second.IsSelected = true;
                            second.QueueRedraw();
                            _secondArmy = second;
                            GD.Print($"Вторая армия ID: {second.Id}, солдат: {second.Soldiers}, регион: {second.RegionId}");
                        }
                        RefreshMergeUI();
                        return;
                    }
                }

                // 1. Army selected + click on region = queue move
                if (_selectedArmy != null)
                {
                    foreach (var child in GetChildren())
                    {
                        if (child is Region region
                            && Geometry2D.IsPointInPolygon(clickPos, region.Polygon))
                        {
                            if (GameManager.Instance.IsMultiplayerGame)
                            {
                                NetworkManager.Instance.SendCommand(new GameCommand
                                {
                                    Type = CommandType.MoveArmy,
                                    PlayerId = GameManager.Instance.PlayerNationId,
                                    Arg1 = _selectedArmy.Id,
                                    Arg2 = region.Id,
                                });
                            }
                            else
                            {
                                QueueArmyMove(_selectedArmy.Id, region.Id);
                            }

                            _selectedArmy.IsSelected = false;
                            _selectedArmy.QueueRedraw();
                            _selectedArmy = null;
                            _regionPanel.HideArmyInfo();
                            ClearSecondArmy();
                            return;
                        }
                    }

                    _selectedArmy.IsSelected = false;
                    _selectedArmy.QueueRedraw();
                    _selectedArmy = null;
                    _regionPanel.HideArmyInfo();
                    ClearSecondArmy();
                    _highlightedPath.Clear();
                    _overlay?.Refresh();
                }

                // 2. Click on army — select it (only your own armies)
                var picked = PickArmyAt(clickPos);
                if (picked != null)
                {
                    var army = picked;
                    if (GameManager.Instance.IsMultiplayerGame && army.PlayerId != GameManager.Instance.PlayerNationId)
                        return;

                    ClearSecondArmy();
                    army.IsSelected = true;
                    army.QueueRedraw();
                    _selectedArmy = army;
                    _regionPanel.ShowArmyInfo(army.Id, army.Soldiers, army.HP, army.MaxHP, army.Type);
                    UpdateHighlightedPath(army.Id);
                    RefreshMergeUI();

                    var region = GetRegionById(army.RegionId);
                    if (region != null)
                    {
                        string ownerName = region.OwningNation != null ? region.OwningNation.Name : "Нейтральная";
                        EmitSignal("RegionSelected", region.RegionName, region.Gold, ownerName, region.Id, IsCapitalRegion(region.Id));
                    }

                    GD.Print($"Выбрана армия ID: {army.Id}, солдат: {army.Soldiers}, регион: {army.RegionId}");
                    return;
                }

                // 3. Region selection
                bool hitRegion = false;
                foreach (var child in GetChildren())
                {
                    if (child is Region region
                        && Geometry2D.IsPointInPolygon(clickPos, region.Polygon))
                    {
                        if (_selectedRegion != null)
                            _selectedRegion.Color = _selectedOriginalColor;

                        _selectedOriginalColor = region.Color;
                        region.Color = Colors.Green;
                        _selectedRegion = region;
                        hitRegion = true;

                        string ownerName = region.OwningNation != null ? region.OwningNation.Name : "Нейтральная";
                        EmitSignal("RegionSelected", region.RegionName, region.Gold, ownerName, region.Id, IsCapitalRegion(region.Id));
                        GD.Print($"Selected: {region.RegionName}");
                        break;
                    }
                }

                if (!hitRegion && _selectedRegion != null)
                {
                    DeselectRegion();
                }
            }
            else if (mouseEvent.ButtonIndex == MouseButton.Right)
            {
                if (_selectedArmy != null)
                {
                    CancelArmyPath(_selectedArmy.Id);
                    _selectedArmy.IsSelected = false;
                    _selectedArmy.QueueRedraw();
                    _selectedArmy = null;
                    _regionPanel.HideArmyInfo();
                    ClearSecondArmy();
                    _highlightedPath.Clear();
                    _overlay?.Refresh();
                }

                DeselectRegion();
            }
        }
    }

    public void DeselectRegion()
    {
        if (_selectedRegion == null) return;
        _selectedRegion.Color = _selectedOriginalColor;
        GD.Print($"Deselected: {_selectedRegion.RegionName}");
        _selectedRegion = null;
        EmitSignal("RegionDeselected");
    }

    // --- Map generation helpers ---

    private void PlaceCapitals()
    {
        var nationPositions = new Dictionary<int, List<(Vector2 center, Region region)>>();
        foreach (var child in GetChildren())
        {
            if (child is Region region && region.OwningNation != null)
            {
                if (!nationPositions.ContainsKey(region.NationId))
                    nationPositions[region.NationId] = new List<(Vector2, Region)>();
                nationPositions[region.NationId].Add((region.Center, region));
            }
        }

        string[] capitalNames = { "Москва", "Париж", "Вашингтон", "Лондон" };
        float outerRadius = 10f;
        float innerRadius = 5f;

        foreach (var kvp in nationPositions)
        {
            var entries = kvp.Value;
            if (entries.Count == 0) continue;

            Vector2 sum = Vector2.Zero;
            foreach (var e in entries)
                sum += e.center;
            Vector2 nationCenter = sum / entries.Count;

            int closestIdx = 0;
            float closestDist = nationCenter.DistanceTo(entries[0].center);
            for (int i = 1; i < entries.Count; i++)
            {
                float dist = nationCenter.DistanceTo(entries[i].center);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestIdx = i;
                }
            }

            _capitalRegions[kvp.Key] = entries[closestIdx].region.Id;

            var marker = new Node2D();
            marker.Position = entries[closestIdx].center;
            marker.ZIndex = 2;
            AddChild(marker);
            _capitalMarkers[kvp.Key] = marker;

            float or = outerRadius;
            float ir = innerRadius;
            var capRegion = entries[closestIdx].region;
            marker.Draw += () =>
            {
                marker.DrawCircle(Vector2.Zero, or, Colors.White);
                marker.DrawCircle(Vector2.Zero, ir, Colors.Black);
                // Шевроны укреплений под маркером.
                for (int i = 0; i < capRegion.FortLevel; i++)
                    marker.DrawCircle(new Vector2((i - (capRegion.FortLevel - 1) / 2f) * 9f, or + 7f), 3f, Colors.Gold);
            };
            marker.QueueRedraw();

            var label = new Label();
            label.Text = kvp.Key < capitalNames.Length ? capitalNames[kvp.Key] : $"Столица {kvp.Key}";
            label.AddThemeFontSizeOverride("font_size", 11);
            label.AddThemeColorOverride("font_color", Colors.White);
            label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
            label.AddThemeConstantOverride("shadow_offset_x", 1);
            label.AddThemeConstantOverride("shadow_offset_y", 1);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.MouseFilter = Control.MouseFilterEnum.Ignore;
            label.Position = new Vector2(-30, -(outerRadius + 16));
            label.Size = new Vector2(60, 0);
            marker.AddChild(label);
        }
    }

    private void ShowPlayerHUD()
    {
        int playerId = GameManager.Instance.PlayerNationId;
        if (playerId < 0) return;

        var nation = _nations[playerId];

        var hud = new CanvasLayer();
        // Always: кнопки «Меню»/«Звук» должны работать на паузе.
        hud.ProcessMode = ProcessModeEnum.Always;
        AddChild(hud);

        var label = new Label();
        label.Text = nation.Name;
        label.AddThemeFontSizeOverride("font_size", 28);
        label.AddThemeColorOverride("font_color", nation.Color);
        label.Position = new Vector2(68, 20);
        hud.AddChild(label);

        _goldValueLabel = new Label();
        _goldValueLabel.AddThemeFontSizeOverride("font_size", 22);
        _goldValueLabel.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.2f));

        _scienceValueLabel = new Label();
        _scienceValueLabel.AddThemeFontSizeOverride("font_size", 22);
        _scienceValueLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.8f, 1f));

        _dayLabel = new Label();
        _dayLabel.AddThemeFontSizeOverride("font_size", 24);
        _dayLabel.AddThemeColorOverride("font_color", Colors.White);

        // Верхняя плашка по центру: наука слева, день в середине, золото справа.
        // Чёрная подложка выделяет счётчик на фоне карты.
        var topBack = new PanelContainer();
        topBack.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        topBack.OffsetLeft = -220f;
        topBack.OffsetRight = 220f;
        topBack.OffsetTop = 12f;
        topBack.OffsetBottom = 48f;
        topBack.MouseFilter = Control.MouseFilterEnum.Ignore;
        var topStyle = new StyleBoxFlat();
        topStyle.BgColor = new Color(0f, 0f, 0f, 0.65f);
        topStyle.CornerRadiusTopLeft = 8;
        topStyle.CornerRadiusTopRight = 8;
        topStyle.CornerRadiusBottomLeft = 8;
        topStyle.CornerRadiusBottomRight = 8;
        topStyle.ContentMarginLeft = 10f;
        topStyle.ContentMarginRight = 10f;
        topStyle.ContentMarginTop = 6f;
        topStyle.ContentMarginBottom = 6f;
        topBack.AddThemeStyleboxOverride("panel", topStyle);
        hud.AddChild(topBack);
        _topBack = topBack;

        var topBar = new HBoxContainer();
        topBar.Alignment = BoxContainer.AlignmentMode.Center;
        topBar.AddThemeConstantOverride("separation", 10);
        topBar.MouseFilter = Control.MouseFilterEnum.Ignore;
        topBack.AddChild(topBar);

        topBar.AddChild(GameIcons.MakeIcon("science", 22f));
        topBar.AddChild(_scienceValueLabel);
        var daySpacerL = new Control();
        daySpacerL.CustomMinimumSize = new Vector2(24, 0);
        daySpacerL.MouseFilter = Control.MouseFilterEnum.Ignore;
        topBar.AddChild(daySpacerL);
        topBar.AddChild(_dayLabel);
        var daySpacerR = new Control();
        daySpacerR.CustomMinimumSize = new Vector2(24, 0);
        daySpacerR.MouseFilter = Control.MouseFilterEnum.Ignore;
        topBar.AddChild(daySpacerR);
        topBar.AddChild(GameIcons.MakeIcon("gold", 22f));
        topBar.AddChild(_goldValueLabel);
        foreach (var child in topBar.GetChildren())
        {
            if (child is Control ctl)
                ctl.MouseFilter = Control.MouseFilterEnum.Ignore;
        }
        UpdateGoldHUD();

        if (GameManager.Instance.IsGodMode)
        {
            var godLabel = new Label();
            godLabel.Text = "РЕЖИМ БОГА";
            godLabel.AddThemeFontSizeOverride("font_size", 22);
            godLabel.AddThemeColorOverride("font_color", new Color(1f, 0.4f, 0.1f));
            godLabel.Position = new Vector2(68, 88);
            hud.AddChild(godLabel);
        }

        _observerLabel = new Label();
        _observerLabel.Text = "Вы выбыли — наблюдаете";
        _observerLabel.AddThemeFontSizeOverride("font_size", 22);
        _observerLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.75f));
        _observerLabel.Position = new Vector2(68, 120);
        _observerLabel.Visible = false;
        hud.AddChild(_observerLabel);

        var menuBtn = new TextureButton();
        menuBtn.CustomMinimumSize = new Vector2(44, 44);
        menuBtn.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        menuBtn.OffsetLeft = 12f;
        menuBtn.OffsetTop = 12f;
        menuBtn.OffsetRight = 56f;
        menuBtn.OffsetBottom = 56f;
        menuBtn.TextureNormal = MakeHamburgerTexture();
        menuBtn.TooltipText = "Меню (ESC)";
        menuBtn.Pressed += () => TogglePauseMenu();
        hud.AddChild(menuBtn);

        // Тултип экономики: ховер по плашке → справа-снизу от мышки.
        _ecoTip = new PanelContainer();
        var tipStyle = new StyleBoxFlat();
        tipStyle.BgColor = new Color(0f, 0f, 0f, 0.85f);
        tipStyle.CornerRadiusTopLeft = 8;
        tipStyle.CornerRadiusTopRight = 8;
        tipStyle.CornerRadiusBottomLeft = 8;
        tipStyle.CornerRadiusBottomRight = 8;
        tipStyle.ContentMarginLeft = 12f;
        tipStyle.ContentMarginRight = 12f;
        tipStyle.ContentMarginTop = 8f;
        tipStyle.ContentMarginBottom = 8f;
        _ecoTip.AddThemeStyleboxOverride("panel", tipStyle);
        _ecoTip.MouseFilter = Control.MouseFilterEnum.Ignore;
        _ecoTip.Visible = false;
        _ecoTip.ZIndex = 5;
        hud.AddChild(_ecoTip);

        _ecoTipBody = new Label();
        _ecoTipBody.AddThemeFontSizeOverride("font_size", 15);
        _ecoTipBody.AddThemeColorOverride("font_color", Colors.White);
        _ecoTipBody.MouseFilter = Control.MouseFilterEnum.Ignore;
        _ecoTip.AddChild(_ecoTipBody);

        UpdateGoldHUD();
    }

    private void RefreshEcoTip()
    {
        if (_ecoTipBody == null) return;
        int playerId = GameManager.Instance.PlayerNationId;
        if (playerId < 0) return;

        int mines = GameManager.Instance.Mines.GetValueOrDefault(playerId, 0);
        int baseInc = GameManager.BaseIncomeFor(playerId);
        int mineInc = mines * GameManager.MineIncome;
        int unis = GameManager.Instance.Universities.GetValueOrDefault(playerId, 0);
        int upkeep = GameManager.Instance.DayUpkeep.GetValueOrDefault(playerId, 0);
        int net = baseInc + mineInc - upkeep;
        int sciPts = GameManager.Instance.Research.GetValueOrDefault(playerId, 0);
        int sciInc = GameManager.BaseResearch + unis * GameManager.UniversityPoints;

        _ecoTipBody.Text =
            $"Доходы / день:\n" +
            $"  База: +{baseInc}\n" +
            $"  Шахты ({mines}): +{mineInc}\n" +
            $"Расходы / день:\n" +
            $"  Университеты ({unis}): −{upkeep}\n" +
            $"Итого: {(net >= 0 ? "+" : "")}{net}\n" +
            $"Наука: {sciPts} (+{sciInc}/день)";
    }

    private Texture2D MakeHamburgerTexture()
    {
        const int size = 44;
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var bg = new Color(0.2f, 0.5f, 0.8f);
        var bar = Colors.White;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool inBar = x >= 10 && x < 34
                    && ((y >= 12 && y < 16) || (y >= 20 && y < 24) || (y >= 28 && y < 32));
                img.SetPixel(x, y, inBar ? bar : bg);
            }
        }
        return ImageTexture.CreateFromImage(img);
    }

    private CanvasLayer _pauseLayer;

    public void TogglePauseMenu()
    {
        AudioHub.Instance?.PlayClick();
        if (_pauseLayer != null && IsInstanceValid(_pauseLayer))
        {
            ClosePauseMenu();
            return;
        }
        if (_gameOver) return;

        _pauseLayer = new CanvasLayer();
        _pauseLayer.Layer = 60;
        _pauseLayer.ProcessMode = ProcessModeEnum.Always;
        AddChild(_pauseLayer);

        var dim = new ColorRect();
        dim.Color = new Color(0f, 0f, 0f, 0.6f);
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        dim.MouseFilter = Control.MouseFilterEnum.Stop;
        _pauseLayer.AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        center.MouseFilter = Control.MouseFilterEnum.Ignore;
        _pauseLayer.AddChild(center);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 12);
        center.AddChild(vbox);

        var title = new Label();
        title.Text = "Пауза";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 40);
        title.AddThemeColorOverride("font_color", Colors.White);
        vbox.AddChild(title);

        var soundRow = new HBoxContainer();
        soundRow.Alignment = BoxContainer.AlignmentMode.Center;
        soundRow.AddThemeConstantOverride("separation", 10);
        vbox.AddChild(soundRow);

        var soundLabel = new Label();
        soundLabel.Text = "Звук:";
        soundLabel.AddThemeFontSizeOverride("font_size", 20);
        soundLabel.AddThemeColorOverride("font_color", Colors.White);
        soundRow.AddChild(soundLabel);

        var muteMenuBtn = new Button();
        muteMenuBtn.CustomMinimumSize = new Vector2(90, 40);
        muteMenuBtn.AddThemeFontSizeOverride("font_size", 18);
        soundRow.AddChild(muteMenuBtn);

        var volSlider = new HSlider();
        volSlider.CustomMinimumSize = new Vector2(200, 40);
        volSlider.MinValue = 0;
        volSlider.MaxValue = 100;
        volSlider.Step = 1;
        volSlider.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        soundRow.AddChild(volSlider);

        void RefreshSoundUI()
        {
            bool muted = AudioHub.Instance != null && AudioHub.Instance.Muted;
            muteMenuBtn.Text = muted ? "ВЫКЛ" : "ВКЛ";
            volSlider.SetValueNoSignal((AudioHub.Instance?.Volume01 ?? 1f) * 100f);
        }
        RefreshSoundUI();
        muteMenuBtn.Pressed += () =>
        {
            AudioHub.Instance?.ToggleMute();
            RefreshSoundUI();
            AudioHub.Instance?.PlayClick();
        };
        volSlider.ValueChanged += (double v) =>
        {
            if (AudioHub.Instance == null) return;
            AudioHub.Instance.Volume01 = (float)v / 100f;
            if (AudioHub.Instance.Muted)
                AudioHub.Instance.ToggleMute();
            RefreshSoundUI();
        };

        var continueBtn = MakeMenuButton("Продолжить");
        continueBtn.Pressed += ClosePauseMenu;
        vbox.AddChild(continueBtn);

        string exitText = GameManager.Instance.IsMultiplayerGame ? "Отключиться" : "Выйти в меню";
        var exitBtn = MakeMenuButton(exitText);
        exitBtn.Pressed += OnGameOverMenuPressed;
        vbox.AddChild(exitBtn);

        // Пауза — только одиночка; в сети игра идёт дальше.
        if (!GameManager.Instance.IsMultiplayerGame)
            GetTree().Paused = true;
    }

    private void ClosePauseMenu()
    {
        AudioHub.Instance?.PlayClick();
        GetTree().Paused = false;
        if (_pauseLayer != null && IsInstanceValid(_pauseLayer))
            _pauseLayer.QueueFree();
        _pauseLayer = null;
    }

    private List<Vector2> GenerateHexCenters()
    {
        var centers = new List<Vector2>();
        int R = HexBoardRadius;

        for (int q = -(R - 1); q <= R - 1; q++)
        {
            for (int r = -(R - 1); r <= R - 1; r++)
            {
                int s = -q - r;
                if (System.Math.Abs(s) <= R - 1)
                {
                    Vector2 pos = CubeToPixel(q, r);
                    centers.Add(pos);
                }
            }
        }

        return centers;
    }

    private Vector2 CubeToPixel(int q, int r)
    {
        float x = HexCellRadius * Mathf.Sqrt(3f) * (q + r / 2f);
        float y = HexCellRadius * 1.5f * r;
        return new Vector2(x, y);
    }

    private void SyncRegionCapture(Region region)
    {
        if (NetworkManager.Instance.IsServer)
        {
            var c = region.Color;
            NetworkManager.Instance.Rpc(nameof(NetworkManager.RpcSyncRegionCaptured),
                region.Id, region.OwnerId, c.R, c.G, c.B, region.FortLevel);
        }
    }

    // --- Query API для ИИ (только чтение) ---

    public List<Region> GetAllRegions()
    {
        var result = new List<Region>();
        foreach (var child in GetChildren())
            if (child is Region region)
                result.Add(region);
        return result;
    }

    public List<Army> GetArmiesOfNation(int nationId)
    {
        var result = new List<Army>();
        foreach (var army in _armies)
            if (IsInstanceValid(army) && army.PlayerId == nationId)
                result.Add(army);
        return result;
    }

    public List<Army> GetArmiesInRegion(int regionId)
    {
        return GetAllArmiesInRegion(regionId);
    }

    public int CountOwnArmies(int regionId, int playerId)
    {
        int count = 0;
        foreach (var army in _armies)
        {
            if (!IsInstanceValid(army)) continue;
            if (army.RegionId == regionId && army.PlayerId == playerId)
                count++;
        }
        return count;
    }

    public List<int> FindPathTo(int fromId, int toId)
    {
        return FindPath(fromId, toId);
    }

    public int GetCapitalRegion(int nationId)
    {
        return _capitalRegions.GetValueOrDefault(nationId, -1);
    }

    public int GetNationCount()
    {
        return _nations.Count;
    }

    public bool IsArmyBusy(int armyId)
    {
        return _armiesInBattle.Contains(armyId)
            || _retreatTargets.ContainsKey(armyId)
            || (_armyPaths.TryGetValue(armyId, out var path) && path != null && path.Count > 0);
    }

    // --- Победа / поражение ---

    private bool IsContenderEliminated(int nationId)
    {
        if (!_capitalRegions.TryGetValue(nationId, out int capId)) return true;
        var cap = GetRegionById(capId);
        return cap == null || cap.OwnerId != nationId;
    }

    private List<int> GetContenders()
    {
        var list = new List<int>();
        if (GameManager.Instance.IsMultiplayerGame)
        {
            int n = Mathf.Clamp(GameManager.Instance.TotalPlayers, 2, _nations.Count);
            for (int i = 0; i < n; i++) list.Add(i);
        }
        else
        {
            for (int i = 0; i < _nations.Count; i++) list.Add(i);
        }
        return list;
    }

    private bool IsObserver()
    {
        return GameManager.Instance.IsMultiplayerGame
            && !_gameOver
            && IsContenderEliminated(GameManager.Instance.PlayerNationId);
    }

    public bool IsPlayerObserver()
    {
        return IsObserver();
    }

    private void CheckVictory()
    {
        if (_gameOver || _nations.Count == 0 || _capitalRegions.Count == 0) return;
        bool canJudge = !GameManager.Instance.IsMultiplayerGame || NetworkManager.Instance.IsServer;
        if (!canJudge) return;

        var alive = new List<int>();
        foreach (int n in GetContenders())
            if (!IsContenderEliminated(n)) alive.Add(n);

        if (alive.Count == 1) GameOver(alive[0]);
        else if (alive.Count == 0) GameOver(-1);
    }

    private void GameOver(int winnerId)
    {
        _gameOver = true;
        if (GameManager.Instance.IsMultiplayerGame && NetworkManager.Instance.IsServer)
            NetworkManager.Instance.Rpc(nameof(NetworkManager.RpcGameOver), winnerId);
        ShowGameOver(winnerId);
    }

    public void ShowGameOver(int winnerId)
    {
        _gameOver = true;
        int playerId = GameManager.Instance.PlayerNationId;
        bool isWin = winnerId >= 0 && winnerId == playerId;

        var layer = new CanvasLayer();
        layer.Layer = 50;
        AddChild(layer);

        var dim = new ColorRect();
        dim.Color = new Color(0f, 0f, 0f, 0.7f);
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        dim.MouseFilter = Control.MouseFilterEnum.Stop;
        layer.AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(center);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 12);
        center.AddChild(vbox);

        var title = new Label();
        if (winnerId < 0)
            title.Text = "Ничья!";
        else if (isWin)
            title.Text = "Победа!";
        else
            title.Text = $"Поражение. Победил: {GetNationById(winnerId)?.Name ?? "?"}";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 48);
        title.AddThemeColorOverride("font_color", isWin ? new Color(0.3f, 0.9f, 0.4f) : new Color(0.95f, 0.35f, 0.3f));
        vbox.AddChild(title);

        int minutes = (int)(_gameTime / 60f);
        int seconds = (int)(_gameTime % 60f);
        int losses = _statLosses.GetValueOrDefault(playerId, 0);
        int captures = _statCaptures.GetValueOrDefault(playerId, 0);

        var stats = new Label();
        stats.Text = $"Время: {minutes}:{seconds:D2}   День: {GameManager.Instance.Day}   Битв: {_statBattles}   Потери: {losses}   Захваты: {captures}";
        stats.HorizontalAlignment = HorizontalAlignment.Center;
        stats.AddThemeFontSizeOverride("font_size", 20);
        stats.AddThemeColorOverride("font_color", Colors.White);
        vbox.AddChild(stats);

        bool canRestart = !GameManager.Instance.IsMultiplayerGame || NetworkManager.Instance.IsServer;
        if (canRestart)
        {
            var againBtn = MakeMenuButton("Ещё раз");
            againBtn.Pressed += OnRematchPressed;
            vbox.AddChild(againBtn);
        }

        var menuBtn = MakeMenuButton("В меню");
        menuBtn.Pressed += OnGameOverMenuPressed;
        vbox.AddChild(menuBtn);

        if (isWin)
            AudioHub.Instance?.PlayVictory();
        else
            AudioHub.Instance?.PlayDefeat();

        GD.Print($"Игра окончена! Победитель: {winnerId}");
    }

    private Button MakeMenuButton(string text)
    {
        var btn = new Button();
        btn.Text = text;
        btn.CustomMinimumSize = new Vector2(300, 55);
        btn.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        btn.AddThemeFontSizeOverride("font_size", 22);
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.2f, 0.5f, 0.8f);
        style.CornerRadiusTopLeft = 8;
        style.CornerRadiusTopRight = 8;
        style.CornerRadiusBottomLeft = 8;
        style.CornerRadiusBottomRight = 8;
        btn.AddThemeStyleboxOverride("normal", style);
        var hover = new StyleBoxFlat();
        hover.BgColor = new Color(0.25f, 0.55f, 0.9f);
        hover.CornerRadiusTopLeft = 8;
        hover.CornerRadiusTopRight = 8;
        hover.CornerRadiusBottomLeft = 8;
        hover.CornerRadiusBottomRight = 8;
        btn.AddThemeStyleboxOverride("hover", hover);
        btn.AddThemeColorOverride("font_color", Colors.White);
        btn.AddThemeColorOverride("font_hover_color", Colors.White);
        return btn;
    }

    private void OnRematchPressed()
    {
        AudioHub.Instance?.PlayClick();
        if (GameManager.Instance.IsMultiplayerGame && NetworkManager.Instance.IsServer)
            NetworkManager.Instance.Rpc(nameof(NetworkManager.RpcRestartMatch));
        else if (!GameManager.Instance.IsMultiplayerGame)
            GetTree().ChangeSceneToFile("res://Main.tscn");
    }

    private void OnGameOverMenuPressed()
    {
        AudioHub.Instance?.PlayClick();
        GetTree().Paused = false;
        if (GameManager.Instance.IsMultiplayerGame)
        {
            if (NetworkManager.Instance.IsServer)
                NetworkManager.Instance.Rpc(nameof(NetworkManager.RpcServerStopping));
            NetworkManager.Instance.StopNetwork();
            GameManager.Instance.IsMultiplayerGame = false;
            GameManager.Instance.IsGodMode = false;
        }
        GetTree().ChangeSceneToFile("res://NationSelect.tscn");
    }

    private Vector2[] GenerateHexagon(Vector2 center)
    {
        Vector2[] vertices = new Vector2[6];
        float r = HexCellRadius * 0.92f;

        for (int i = 0; i < 6; i++)
        {
            float angle = Mathf.Pi / 6f + i * Mathf.Pi / 3f;
            vertices[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
        }

        return vertices;
    }
}
