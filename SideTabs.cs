using Godot;
using System.Collections.Generic;

// Боковые вкладки справа: Дипломатия / Исследования / Армия.
// Вкладка армии: создание войск сразу в столицу, без выбора региона.
public partial class SideTabs : CanvasLayer
{
    private VBoxContainer _tabBar;
    private Panel _contentPanel;
    private VBoxContainer _diploBox;
    private Button[] _diploRowBtns = new Button[4];
    private Label[] _diploRowStatus = new Label[4];
    private ColorRect[] _diploRowFlags = new ColorRect[4];
    private ColorRect _diploFlag;
    private Label _diploName;
    private Label _diploStatus;
    private Label _diploWars;
    private Label _diploPacts;
    private Label _diploTimer;
    private Button _warBtn;
    private Button _pactBtn;
    private Button _peaceBtn;
    private int _diploSelected = -1;
    private VBoxContainer _researchBox;
    private Label _researchPointsLabel;
    private Label _scienceValueLabel;
    private Label _uniLabel;
    private TechTreeView _techTree;
    private ScrollContainer _treeScroll;
    private bool _rmbDrag = false;
    private VBoxContainer _armyBox;

    private HSlider _soldierSlider;
    private Label _sliderLabel;
    private Label _goldLabel;
    private Label _statusLabel;
    private Button _createArmyBtn;
    private Button _militiaBtn;
    private Button _infantryBtn;
    private Button _eliteBtn;
    private OptionButton _nationOption;
    private UnitType _selectedType = UnitType.Infantry;

    private int _activeTab = -1; // вкладки по умолчанию закрыты
    private float _updateTimer = 0f;

    public override void _Ready()
    {
        Layer = 9;

        _tabBar = new VBoxContainer();
        _tabBar.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _tabBar.OffsetLeft = -140f;
        _tabBar.OffsetTop = 70f;
        _tabBar.OffsetRight = -20f;
        _tabBar.OffsetBottom = 220f;
        _tabBar.AddThemeConstantOverride("separation", 6);
        AddChild(_tabBar);

        AddTabButton("Дипломатия", 0);
        AddTabButton("Исследования", 1);
        AddTabButton("Армия", 2);

        _contentPanel = new Panel();
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.12f, 0.12f, 0.18f, 0.92f);
        style.CornerRadiusTopLeft = 8;
        style.CornerRadiusTopRight = 8;
        style.CornerRadiusBottomLeft = 8;
        style.CornerRadiusBottomRight = 8;
        style.ContentMarginLeft = 14;
        style.ContentMarginRight = 14;
        style.ContentMarginTop = 12;
        style.ContentMarginBottom = 12;
        _contentPanel.AddThemeStyleboxOverride("panel", style);
        _contentPanel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _contentPanel.OffsetLeft = 20f;
        _contentPanel.OffsetTop = 20f;
        _contentPanel.OffsetRight = -20f;
        _contentPanel.OffsetBottom = -20f;
        AddChild(_contentPanel);

        var closeBtn = new Button();
        closeBtn.Text = "X";
        closeBtn.AddThemeFontSizeOverride("font_size", 14);
        closeBtn.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        closeBtn.OffsetLeft = -174f;
        closeBtn.OffsetTop = 6f;
        closeBtn.OffsetRight = -146f;
        closeBtn.OffsetBottom = 30f;
        closeBtn.Pressed += () => CloseTabs();
        _contentPanel.AddChild(closeBtn);

        _diploBox = MakeDiploBox();
        _researchBox = MakeResearchBox();
        _armyBox = MakeArmyBox();

        // Крестик поверх контента: боксы на весь прямоугольник,
        // иначе они перехватывают клики.
        closeBtn.MoveToFront();
        // Полоса вкладок — поверх почти полноэкранной панели.
        _tabBar.MoveToFront();

        RefreshTabs();
    }

    private void AddTabButton(string text, int idx)
    {
        var btn = new Button();
        btn.Text = text;
        btn.CustomMinimumSize = new Vector2(120, 40);
        btn.AddThemeFontSizeOverride("font_size", 14);
        btn.ToggleMode = true;
        btn.Pressed += () => SelectTab(idx);
        btn.SetMeta("tab_idx", idx);
        _tabBar.AddChild(btn);
    }

    private void SelectTab(int idx)
    {
        if (_activeTab == idx && _contentPanel.Visible)
        {
            CloseTabs();
            return;
        }
        _activeTab = idx;
        AudioHub.Instance?.PlayClick();
        // Большое меню не должно спорить с меню клетки.
        Map()?.DeselectRegion();
        RefreshTabs();
    }

    private void CloseTabs()
    {
        _activeTab = -1;
        AudioHub.Instance?.PlayClick();
        RefreshTabs();
    }

    private void RefreshTabs()
    {
        foreach (var child in _tabBar.GetChildren())
        {
            if (child is Button btn && btn.HasMeta("tab_idx"))
                btn.ButtonPressed = _activeTab >= 0 && (int)btn.GetMeta("tab_idx") == _activeTab;
        }
        _diploBox.Visible = _activeTab == 0;
        _researchBox.Visible = _activeTab == 1;
        _armyBox.Visible = _activeTab == 2;
        _contentPanel.Visible = _activeTab >= 0;
        UpdateArmyUI();
        UpdateResearchUI();
        UpdateDiploUI();
    }

    private VBoxContainer MakeDiploBox()
    {
        var box = new VBoxContainer();
        box.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        box.OffsetLeft = 14f;
        box.OffsetTop = 12f;
        box.OffsetRight = -130f;
        box.OffsetBottom = -12f;
        box.MouseFilter = Control.MouseFilterEnum.Ignore;
        box.AddThemeConstantOverride("separation", 6);
        box.Visible = false;
        _contentPanel.AddChild(box);

        var title = new Label();
        title.Text = "Дипломатия";
        title.AddThemeFontSizeOverride("font_size", 18);
        title.AddThemeColorOverride("font_color", Colors.White);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(title);

        var split = new HBoxContainer();
        split.AddThemeConstantOverride("separation", 12);
        split.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        box.AddChild(split);

        var left = new VBoxContainer();
        left.CustomMinimumSize = new Vector2(220, 0);
        left.AddThemeConstantOverride("separation", 4);
        split.AddChild(left);

        for (int i = 0; i < 4; i++)
        {
            int nationId = i;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            row.CustomMinimumSize = new Vector2(0, 34);
            left.AddChild(row);

            var flag = new ColorRect();
            flag.CustomMinimumSize = new Vector2(24, 24);
            flag.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(flag);
            _diploRowFlags[i] = flag;

            var btn = new Button();
            btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            btn.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            btn.CustomMinimumSize = new Vector2(0, 30);
            btn.AddThemeFontSizeOverride("font_size", 13);
            btn.ToggleMode = true;
            btn.Pressed += () => SelectDiploNation(nationId);
            row.AddChild(btn);
            _diploRowBtns[i] = btn;

            var status = new Label();
            status.CustomMinimumSize = new Vector2(92, 0);
            status.AddThemeFontSizeOverride("font_size", 12);
            status.AddThemeColorOverride("font_color", Colors.LightGray);
            status.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            status.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(status);
            _diploRowStatus[i] = status;
        }

        var right = new VBoxContainer();
        right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        right.AddThemeConstantOverride("separation", 6);
        split.AddChild(right);

        var flagRow = new HBoxContainer();
        flagRow.AddThemeConstantOverride("separation", 8);
        right.AddChild(flagRow);

        _diploFlag = new ColorRect();
        _diploFlag.CustomMinimumSize = new Vector2(40, 40);
        flagRow.AddChild(_diploFlag);

        _diploName = new Label();
        _diploName.AddThemeFontSizeOverride("font_size", 20);
        _diploName.AddThemeColorOverride("font_color", Colors.White);
        _diploName.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        flagRow.AddChild(_diploName);

        _diploStatus = new Label();
        _diploStatus.AddThemeFontSizeOverride("font_size", 15);
        right.AddChild(_diploStatus);

        _diploWars = new Label();
        _diploWars.AddThemeFontSizeOverride("font_size", 13);
        _diploWars.AddThemeColorOverride("font_color", new Color(1f, 0.5f, 0.5f));
        _diploWars.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        right.AddChild(_diploWars);

        _diploPacts = new Label();
        _diploPacts.AddThemeFontSizeOverride("font_size", 13);
        _diploPacts.AddThemeColorOverride("font_color", new Color(0.5f, 0.9f, 0.5f));
        _diploPacts.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        right.AddChild(_diploPacts);

        _diploTimer = new Label();
        _diploTimer.AddThemeFontSizeOverride("font_size", 13);
        _diploTimer.AddThemeColorOverride("font_color", Colors.LightGray);
        right.AddChild(_diploTimer);

        _warBtn = new Button();
        _warBtn.Text = "Объявить войну";
        _warBtn.AddThemeFontSizeOverride("font_size", 14);
        _warBtn.Pressed += OnDeclareWarPressed;
        right.AddChild(_warBtn);

        _pactBtn = new Button();
        _pactBtn.Text = "Пакт о ненападении";
        _pactBtn.AddThemeFontSizeOverride("font_size", 14);
        _pactBtn.Pressed += OnMakePactPressed;
        right.AddChild(_pactBtn);

        _peaceBtn = new Button();
        _peaceBtn.Text = "Мирный договор";
        _peaceBtn.AddThemeFontSizeOverride("font_size", 14);
        _peaceBtn.Pressed += OnProposePeacePressed;
        _peaceBtn.Visible = false;
        right.AddChild(_peaceBtn);

        return box;
    }

    private void SelectDiploNation(int nationId)
    {
        _diploSelected = nationId;
        AudioHub.Instance?.PlayClick();
        UpdateDiploUI();
    }

    private int DiploActingNation()
    {
        return GameManager.Instance.PlayerNationId;
    }

    private void UpdateDiploUI()
    {
        var map = Map();
        if (map == null || _diploBox == null || !_diploBox.Visible) return;

        int me = DiploActingNation();
        bool observer = IsObserver();
        if (_diploSelected < 0 || _diploSelected == me)
        {
            _diploSelected = -1;
            for (int i = 0; i < 4; i++)
            {
                if (i == me) continue;
                _diploSelected = i;
                break;
            }
        }

        for (int i = 0; i < 4; i++)
        {
            var nation = map.GetNationById(i);
            string name = nation != null ? nation.Name : $"Нация {i}";
            Color color = nation != null ? nation.Color : Colors.Gray;
            if (_diploRowFlags[i] != null)
                _diploRowFlags[i].Color = color;

            if (i == me)
            {
                if (_diploRowBtns[i] != null)
                {
                    _diploRowBtns[i].Text = $"{name} (вы)";
                    _diploRowBtns[i].Disabled = true;
                    _diploRowBtns[i].ButtonPressed = false;
                }
                if (_diploRowStatus[i] != null) _diploRowStatus[i].Text = "—";
                continue;
            }

            var rel = GameManager.GetRelation(me, i);
            if (_diploRowBtns[i] != null)
            {
                _diploRowBtns[i].Text = name;
                _diploRowBtns[i].Disabled = false;
                _diploRowBtns[i].ButtonPressed = i == _diploSelected;
            }
            if (_diploRowStatus[i] != null)
            {
                _diploRowStatus[i].Text = GameManager.RelationName(rel);
                _diploRowStatus[i].AddThemeColorOverride("font_color",
                    rel == RelationState.War ? new Color(1f, 0.4f, 0.4f)
                    : rel == RelationState.Pact ? new Color(0.4f, 0.9f, 0.4f)
                    : Colors.LightGray);
            }
        }

        if (_diploSelected < 0) return;
        var target = map.GetNationById(_diploSelected);
        string targetName = target != null ? target.Name : $"Нация {_diploSelected}";
        Color targetColor = target != null ? target.Color : Colors.Gray;
        var targetRel = GameManager.GetRelation(me, _diploSelected);

        if (_diploFlag != null) _diploFlag.Color = targetColor;
        if (_diploName != null) _diploName.Text = targetName;
        if (_diploStatus != null)
        {
            _diploStatus.Text = $"Статус: {GameManager.RelationName(targetRel)}";
            _diploStatus.AddThemeColorOverride("font_color",
                targetRel == RelationState.War ? new Color(1f, 0.4f, 0.4f)
                : targetRel == RelationState.Pact ? new Color(0.4f, 0.9f, 0.4f)
                : Colors.White);
        }

        string wars = "";
        string pacts = "";
        for (int i = 0; i < 4; i++)
        {
            if (i == _diploSelected) continue;
            var other = map.GetNationById(i);
            string otherName = other != null ? other.Name : $"Нация {i}";
            var r = GameManager.GetRelation(_diploSelected, i);
            if (r == RelationState.War) wars += (wars.Length > 0 ? ", " : "") + otherName;
            if (r == RelationState.Pact) pacts += (pacts.Length > 0 ? ", " : "") + otherName;
        }
        if (_diploWars != null)
            _diploWars.Text = "Воюет с: " + (wars.Length > 0 ? wars : "—");
        if (_diploPacts != null)
            _diploPacts.Text = "Пакты: " + (pacts.Length > 0 ? pacts : "—");
        if (_diploTimer != null)
        {
            if (targetRel == RelationState.Pact)
            {
                int key = GameManager.RelationKey(me, _diploSelected);
                float left = GameManager.Instance.PactTimers.GetValueOrDefault(key, 0f);
                _diploTimer.Text = $"Пакт истекает через: {(int)(left / 60f)}:{(int)(left % 60f):D2}";
            }
            else
            {
                _diploTimer.Text = "";
            }
        }

        if (_warBtn != null)
        {
            _warBtn.Disabled = observer || targetRel == RelationState.War || targetRel == RelationState.Pact;
            _warBtn.Text = targetRel == RelationState.War ? "Идёт война"
                : targetRel == RelationState.Pact ? "Пакт (война невозможна)"
                : "Объявить войну";
        }
        if (_pactBtn != null)
        {
            _pactBtn.Disabled = observer || targetRel == RelationState.Pact;
            _pactBtn.Text = targetRel == RelationState.Pact ? "Пакт действует" : "Пакт о ненападении";
        }
        if (_peaceBtn != null)
        {
            _peaceBtn.Visible = targetRel == RelationState.War;
            _peaceBtn.Disabled = observer;
        }
    }

    private void OnDeclareWarPressed()
    {
        var map = Map();
        if (map == null || IsObserver() || _diploSelected < 0) return;
        AudioHub.Instance?.PlayClick();
        int me = DiploActingNation();

        if (GameManager.Instance.IsMultiplayerGame)
        {
            NetworkManager.Instance.SendCommand(new GameCommand
            {
                Type = CommandType.SetRelation,
                PlayerId = me,
                Arg1 = _diploSelected,
                Arg2 = (int)RelationState.War,
            });
        }
        else
        {
            map.DeclareWarForPlayer(_diploSelected);
        }
        UpdateDiploUI();
    }

    private void OnMakePactPressed()
    {
        var map = Map();
        if (map == null || IsObserver() || _diploSelected < 0) return;
        AudioHub.Instance?.PlayClick();
        int me = DiploActingNation();

        if (GameManager.Instance.IsMultiplayerGame)
        {
            NetworkManager.Instance.SendCommand(new GameCommand
            {
                Type = CommandType.SetRelation,
                PlayerId = me,
                Arg1 = _diploSelected,
                Arg2 = (int)RelationState.Pact,
            });
        }
        else
        {
            map.MakePactForPlayer(_diploSelected);
        }
        UpdateDiploUI();
    }

    private void OnProposePeacePressed()
    {
        var map = Map();
        if (map == null || IsObserver() || _diploSelected < 0) return;
        AudioHub.Instance?.PlayClick();
        int me = DiploActingNation();

        if (GameManager.Instance.IsMultiplayerGame)
        {
            NetworkManager.Instance.SendCommand(new GameCommand
            {
                Type = CommandType.SetRelation,
                PlayerId = me,
                Arg1 = _diploSelected,
                Arg2 = (int)RelationState.Neutral,
            });
        }
        else
        {
            map.ProposePeaceForPlayer(_diploSelected);
        }
        UpdateDiploUI();
    }

    private VBoxContainer MakeStubBox(string text)
    {
        var box = new VBoxContainer();
        box.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        box.OffsetLeft = 14f;
        box.OffsetTop = 12f;
        box.OffsetRight = -130f;
        box.OffsetBottom = -12f;
        box.MouseFilter = Control.MouseFilterEnum.Ignore;
        box.Visible = false;
        _contentPanel.AddChild(box);

        var label = new Label();
        label.Text = text;
        label.AddThemeFontSizeOverride("font_size", 16);
        label.AddThemeColorOverride("font_color", Colors.LightGray);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(label);
        return box;
    }

    private VBoxContainer MakeResearchBox()
    {
        var box = new VBoxContainer();
        box.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        box.OffsetLeft = 14f;
        box.OffsetTop = 12f;
        box.OffsetRight = -130f;
        box.OffsetBottom = -12f;
        box.MouseFilter = Control.MouseFilterEnum.Ignore;
        box.AddThemeConstantOverride("separation", 6);
        box.Visible = false;
        _contentPanel.AddChild(box);

        var title = new Label();
        title.Text = "Исследования";
        title.AddThemeFontSizeOverride("font_size", 18);
        title.AddThemeColorOverride("font_color", Colors.White);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(title);

        _researchPointsLabel = new Label();
        _researchPointsLabel.AddThemeFontSizeOverride("font_size", 14);
        _researchPointsLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.8f, 1f));
        _scienceValueLabel = new Label();
        _scienceValueLabel.AddThemeFontSizeOverride("font_size", 14);
        _scienceValueLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.8f, 1f));
        var balancesRow = new HBoxContainer();
        balancesRow.AddThemeConstantOverride("separation", 6);
        balancesRow.AddChild(GameIcons.MakeIcon("science"));
        balancesRow.AddChild(_scienceValueLabel);
        var balancesSpacer = new Control();
        balancesSpacer.CustomMinimumSize = new Vector2(10, 0);
        balancesRow.AddChild(balancesSpacer);
        balancesRow.AddChild(GameIcons.MakeIcon("gold"));
        balancesRow.AddChild(_researchPointsLabel);
        box.AddChild(balancesRow);

        _uniLabel = new Label();
        _uniLabel.AddThemeFontSizeOverride("font_size", 14);
        _uniLabel.AddThemeColorOverride("font_color", Colors.White);
        box.AddChild(_uniLabel);

        var uniHint = new Label();
        uniHint.Text = "Стройка — в столице (панель региона)";
        uniHint.AddThemeFontSizeOverride("font_size", 12);
        uniHint.AddThemeColorOverride("font_color", Colors.Gray);
        box.AddChild(uniHint);

        var sep = new HSeparator();
        box.AddChild(sep);

        // Дереву нужен гарантированный габарит (раскладка на фиксированных
        // координатах): минимум 1150×660, дальше крутит скролл.
        var treeScroll = new ScrollContainer();
        treeScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        box.AddChild(treeScroll);
        _treeScroll = treeScroll;

        _techTree = new TechTreeView();
        _techTree.CustomMinimumSize = new Vector2(1150, 660);
        _techTree.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _techTree.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _techTree.BuyPressed += OnResearchBuy;
        treeScroll.AddChild(_techTree);

        return box;
    }

    private void UpdateResearchUI()
    {
        var map = Map();
        if (map == null || _researchBox == null || !_researchBox.Visible) return;

        bool god = IsGod();
        int nation = ActingNation();
        bool observer = IsObserver();

        int pts = GameManager.Instance.Research.GetValueOrDefault(nation, 0);
        int unis = GameManager.Instance.Universities.GetValueOrDefault(nation, 0);
        int gold = GameManager.Instance.Gold.GetValueOrDefault(nation, 0);

        _researchPointsLabel.Text = god ? "∞" : gold.ToString();
        _scienceValueLabel.Text = pts.ToString();
        _uniLabel.Text = $"Университеты: {unis}/{GameManager.MaxUniversities}";

        _techTree?.Refresh(nation, gold, pts, god, observer);
    }

    private void OnResearchBuy(int techId)
    {
        var map = Map();
        if (map == null || IsObserver()) return;
        AudioHub.Instance?.PlayClick();

        if (GameManager.Instance.IsMultiplayerGame)
        {
            NetworkManager.Instance.SendCommand(new GameCommand
            {
                Type = CommandType.Research,
                PlayerId = GameManager.Instance.PlayerNationId,
                Arg1 = techId,
            });
        }
        else
        {
            map.ResearchTechForPlayer(techId);
        }
        UpdateResearchUI();
    }

    private VBoxContainer MakeArmyBox()
    {
        var box = new VBoxContainer();
        box.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        box.OffsetLeft = 14f;
        box.OffsetTop = 12f;
        box.OffsetRight = -130f;
        box.OffsetBottom = -12f;
        box.MouseFilter = Control.MouseFilterEnum.Ignore;
        box.AddThemeConstantOverride("separation", 6);
        box.Visible = false;
        _contentPanel.AddChild(box);

        var title = new Label();
        title.Text = "Создание армии";
        title.AddThemeFontSizeOverride("font_size", 18);
        title.AddThemeColorOverride("font_color", Colors.White);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(title);

        _nationOption = new OptionButton();
        _nationOption.AddThemeFontSizeOverride("font_size", 13);
        _nationOption.Visible = false;
        _nationOption.ItemSelected += (long _) => UpdateArmyUI();
        box.AddChild(_nationOption);

        _goldLabel = new Label();
        _goldLabel.AddThemeFontSizeOverride("font_size", 14);
        _goldLabel.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.2f));
        var goldRow = new HBoxContainer();
        goldRow.AddThemeConstantOverride("separation", 6);
        goldRow.AddChild(GameIcons.MakeIcon("gold"));
        goldRow.AddChild(_goldLabel);
        box.AddChild(goldRow);

        _sliderLabel = new Label();
        _sliderLabel.AddThemeFontSizeOverride("font_size", 14);
        _sliderLabel.AddThemeColorOverride("font_color", Colors.White);
        box.AddChild(_sliderLabel);

        _soldierSlider = new HSlider();
        _soldierSlider.MinValue = GameManager.MinSoldiers;
        _soldierSlider.MaxValue = GameManager.MaxSoldiers;
        _soldierSlider.Step = 10;
        _soldierSlider.Value = GameManager.MinSoldiers;
        _soldierSlider.ValueChanged += OnSliderChanged;
        box.AddChild(_soldierSlider);

        var typeRow = new HBoxContainer();
        typeRow.AddThemeConstantOverride("separation", 6);
        box.AddChild(typeRow);

        _militiaBtn = MakeTypeButton("Ополчение", UnitType.Militia);
        _infantryBtn = MakeTypeButton("Пехота", UnitType.Infantry);
        _eliteBtn = MakeTypeButton("Элита", UnitType.Elite);
        typeRow.AddChild(_militiaBtn);
        typeRow.AddChild(_infantryBtn);
        typeRow.AddChild(_eliteBtn);

        _createArmyBtn = new Button();
        _createArmyBtn.Text = "Создать армию";
        _createArmyBtn.AddThemeFontSizeOverride("font_size", 15);
        _createArmyBtn.Pressed += OnCreateArmyPressed;
        box.AddChild(_createArmyBtn);

        _statusLabel = new Label();
        _statusLabel.AddThemeFontSizeOverride("font_size", 13);
        _statusLabel.AddThemeColorOverride("font_color", Colors.Green);
        _statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(_statusLabel);

        SelectUnitType(UnitType.Infantry);
        return box;
    }

    private Button MakeTypeButton(string text, UnitType type)
    {
        var btn = new Button();
        btn.Text = text;
        btn.ToggleMode = true;
        btn.AddThemeFontSizeOverride("font_size", 12);
        btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        btn.Pressed += () => SelectUnitType(type);
        return btn;
    }

    private void SelectUnitType(UnitType type)
    {
        _selectedType = type;
        _militiaBtn.ButtonPressed = type == UnitType.Militia;
        _infantryBtn.ButtonPressed = type == UnitType.Infantry;
        _eliteBtn.ButtonPressed = type == UnitType.Elite;
        OnSliderChanged(_soldierSlider.Value);
    }

    private MapGenerator Map()
    {
        return GetNodeOrNull<MapGenerator>("/root/Main");
    }

    private bool IsGod()
    {
        return GameManager.Instance.IsGodMode;
    }

    private int ActingNation()
    {
        if (IsGod() && _nationOption != null && _nationOption.ItemCount > 0)
            return _nationOption.GetItemId(_nationOption.Selected);
        return GameManager.Instance.PlayerNationId;
    }

    private int ActingCapital()
    {
        var map = Map();
        if (map == null) return -1;
        return map.GetCapitalRegion(ActingNation());
    }

    private bool IsObserver()
    {
        var map = Map();
        return map != null && map.IsPlayerObserver();
    }

    public bool IsAnyTabOpen()
    {
        return _contentPanel != null && _contentPanel.Visible && _activeTab >= 0;
    }

    public override void _Process(double delta)
    {
        _updateTimer += (float)delta;
        if (_updateTimer < 0.5f) return;
        _updateTimer = 0f;
        if (!_contentPanel.Visible) return;
        if (_armyBox.Visible)
            UpdateArmyUI();
        if (_researchBox.Visible)
            UpdateResearchUI();
        if (_diploBox.Visible)
            UpdateDiploUI();
    }

    // ПКМ-драг по дереву исследований (ScrollContainer сам ПКМ не умеет).
    // Камера при этом заблокирована, конфликта нет; мимо дерева — игнор.
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Right)
        {
            if (mb.Pressed)
            {
                _rmbDrag = _researchBox.Visible && _contentPanel.Visible
                    && _treeScroll != null && IsInstanceValid(_treeScroll)
                    && _treeScroll.GetGlobalRect().HasPoint(mb.Position);
            }
            else
            {
                _rmbDrag = false;
            }
        }
        else if (@event is InputEventMouseMotion motion && _rmbDrag)
        {
            if (_treeScroll == null || !IsInstanceValid(_treeScroll))
            {
                _rmbDrag = false;
                return;
            }
            _treeScroll.ScrollVertical -= (int)motion.Relative.Y;
            _treeScroll.ScrollHorizontal -= (int)motion.Relative.X;
        }
    }

    private void UpdateArmyUI()
    {
        var map = Map();
        if (map == null || _armyBox == null || !_armyBox.Visible) return;

        bool god = IsGod();
        if (_nationOption != null)
        {
            bool showNations = god;
            if (_nationOption.Visible != showNations)
                _nationOption.Visible = showNations;
            if (showNations && _nationOption.ItemCount == 0)
            {
                for (int i = 0; i < 4; i++)
                {
                    var nc = map.GetNationById(i);
                    _nationOption.AddItem(nc != null ? nc.Name : $"Нация {i}", i);
                }
                _nationOption.Selected = Mathf.Clamp(GameManager.Instance.PlayerNationId, 0, 3);
            }
        }

        int nation = ActingNation();
        int capitalId = ActingCapital();
        bool observer = IsObserver();
        bool canCreate = capitalId >= 0 && !observer;
        bool capitalFull = canCreate
            && !map.RegionHasRoom(capitalId, nation, (int)_soldierSlider.Value);

        int gold = GameManager.Instance.Gold.GetValueOrDefault(nation, 0);
        _goldLabel.Text = god ? "∞ (бог)" : gold.ToString();

        int soldiers = (int)_soldierSlider.Value;
        int armyCount = map.GetArmiesOfNation(nation).Count;
        int cost = GameManager.GetArmyCost(soldiers, _selectedType, armyCount, nation);
        if ((int)_soldierSlider.MaxValue != GameManager.MaxSoldiersFor(nation))
        {
            _soldierSlider.MaxValue = GameManager.MaxSoldiersFor(nation);
            _soldierSlider.Value = Mathf.Min((float)_soldierSlider.Value, (float)GameManager.MaxSoldiersFor(nation));
            soldiers = (int)_soldierSlider.Value;
            cost = GameManager.GetArmyCost(soldiers, _selectedType, armyCount, nation);
        }
        _sliderLabel.Text = god
            ? $"Солдат: {soldiers} (бог: бесплатно)"
            : $"Солдат: {soldiers} (Стоимость: {cost})";

        _soldierSlider.Editable = canCreate;
        _militiaBtn.Disabled = !canCreate;
        _infantryBtn.Disabled = !canCreate || (!god && !GameManager.CanRecruit(nation, UnitType.Infantry));
        _eliteBtn.Disabled = !canCreate || (!god && !GameManager.CanRecruit(nation, UnitType.Elite));
        _infantryBtn.Text = !god && !GameManager.CanRecruit(nation, UnitType.Infantry) ? "Пехота (тех)" : "Пехота";
        _eliteBtn.Text = !god && !GameManager.CanRecruit(nation, UnitType.Elite) ? "Элита (тех)" : "Элита";

        if (!god && !GameManager.CanRecruit(nation, _selectedType))
        {
            _selectedType = UnitType.Militia;
            _militiaBtn.ButtonPressed = true;
            _infantryBtn.ButtonPressed = false;
            _eliteBtn.ButtonPressed = false;
        }

        if (!canCreate)
        {
            _createArmyBtn.Disabled = true;
            _createArmyBtn.Text = observer ? "Наблюдение" : "Нет столицы";
        }
        else if (capitalFull)
        {
            _createArmyBtn.Disabled = true;
            _createArmyBtn.Text = $"Клетка заполнена ({map.CountOwnArmies(capitalId, nation)}/{GameManager.MaxArmiesPerRegion}, {map.CountOwnSoldiers(capitalId, nation)}/{GameManager.MaxSoldiersPerRegion})";
        }
        else if (god)
        {
            _createArmyBtn.Disabled = false;
            _createArmyBtn.Text = "Создать (бог)";
        }
        else
        {
            _createArmyBtn.Disabled = gold < cost;
            _createArmyBtn.Text = $"Создать за {cost} золота";
        }
    }

    private void OnSliderChanged(double value)
    {
        UpdateArmyUI();
    }

    private void OnCreateArmyPressed()
    {
        var map = Map();
        if (map == null) return;

        int nation = ActingNation();
        int capitalId = ActingCapital();
        if (capitalId < 0 || IsObserver()) return;
        if (!IsGod() && !GameManager.CanRecruit(nation, _selectedType)) return;

        int soldiers = (int)_soldierSlider.Value;

        if (GameManager.Instance.IsMultiplayerGame)
        {
            NetworkManager.Instance.SendCommand(new GameCommand
            {
                Type = CommandType.CreateArmy,
                PlayerId = nation,
                Arg1 = capitalId,
                Arg2 = soldiers,
                Arg3 = (int)_selectedType,
            });
            _statusLabel.Text = $"Запрос отправлен ({soldiers} солдат)";
            _statusLabel.AddThemeColorOverride("font_color", Colors.Yellow);
        }
        else
        {
            bool success = map.CreateArmyForPlayer(capitalId, soldiers, _selectedType);
            if (success)
            {
                AudioHub.Instance?.PlayClick();
                _statusLabel.Text = $"Армия создана! ({soldiers} солдат)";
                _statusLabel.AddThemeColorOverride("font_color", Colors.Green);
            }
            else
            {
                _statusLabel.Text = "Недостаточно золота!";
                _statusLabel.AddThemeColorOverride("font_color", Colors.Red);
            }
            UpdateArmyUI();
        }
    }
}
