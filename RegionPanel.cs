using Godot;
using System.Collections.Generic;

public partial class RegionPanel : CanvasLayer
{
    private Panel _panel;
    private Label _nameLabel;
    private Label _ownerLabel;
    private Label _armyInfoLabel;
    private Button _mergeBtn;
    private Button _buildMineBtn;
    private Label _minesLabel;
    private Label _buildTimerLabel;
    private Button _buildFortBtn;
    private Label _fortLabel;
    private Button _buildUniBtn;
    private Label _uniLabel;
    private Label _statusLabel;

    private int _currentRegionId = -1;
    private bool _isCapital = false;
    private string _currentOwnerName = "";
    private float _updateTimer = 0f;

    public override void _Ready()
    {
        Layer = 10;

        _panel = new Panel();
        AddChild(_panel);

        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.12f, 0.12f, 0.18f, 0.9f);
        style.CornerRadiusTopLeft = 8;
        style.CornerRadiusTopRight = 8;
        style.CornerRadiusBottomLeft = 8;
        style.CornerRadiusBottomRight = 8;
        style.ContentMarginLeft = 16;
        style.ContentMarginRight = 16;
        style.ContentMarginTop = 12;
        style.ContentMarginBottom = 12;
        _panel.AddThemeStyleboxOverride("panel", style);

        var viewportSize = GetViewport().GetVisibleRect().Size;
        float panelWidth = 260f;
        float panelHeight = 400f;
        _panel.Position = new Vector2(viewportSize.X - panelWidth - 20, viewportSize.Y - panelHeight - 20);
        _panel.Size = new Vector2(panelWidth, panelHeight);

        var vbox = new VBoxContainer();
        vbox.OffsetLeft = 16;
        vbox.OffsetTop = 12;
        vbox.OffsetRight = -16;
        vbox.OffsetBottom = -12;
        _panel.AddChild(vbox);

        _nameLabel = new Label();
        _nameLabel.AddThemeFontSizeOverride("font_size", 18);
        _nameLabel.AddThemeColorOverride("font_color", Colors.White);
        vbox.AddChild(_nameLabel);

        var spacer1 = new Control();
        spacer1.CustomMinimumSize = new Vector2(0, 6);
        vbox.AddChild(spacer1);

        _ownerLabel = new Label();
        _ownerLabel.AddThemeFontSizeOverride("font_size", 16);
        _ownerLabel.AddThemeColorOverride("font_color", Colors.LightGray);
        vbox.AddChild(_ownerLabel);

        _armyInfoLabel = new Label();
        _armyInfoLabel.AddThemeFontSizeOverride("font_size", 15);
        _armyInfoLabel.AddThemeColorOverride("font_color", new Color(0.5f, 1f, 0.5f));
        _armyInfoLabel.Text = "";
        _armyInfoLabel.Visible = false;
        vbox.AddChild(_armyInfoLabel);

        _mergeBtn = new Button();
        _mergeBtn.Text = "Совместить";
        _mergeBtn.AddThemeFontSizeOverride("font_size", 14);
        _mergeBtn.Pressed += OnMergePressed;
        _mergeBtn.Visible = false;
        vbox.AddChild(_mergeBtn);

        var spacer2 = new Control();
        spacer2.CustomMinimumSize = new Vector2(0, 6);
        vbox.AddChild(spacer2);

        _buildMineBtn = new Button();
        _buildMineBtn.Text = $"Построить шахту ({GameManager.MineCost} золота)";
        _buildMineBtn.AddThemeFontSizeOverride("font_size", 14);
        _buildMineBtn.Pressed += OnBuildMinePressed;
        _buildMineBtn.Visible = false;
        vbox.AddChild(_buildMineBtn);

        var spacer5 = new Control();
        spacer5.CustomMinimumSize = new Vector2(0, 4);
        vbox.AddChild(spacer5);

        _minesLabel = new Label();
        _minesLabel.AddThemeFontSizeOverride("font_size", 14);
        _minesLabel.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.2f));
        _minesLabel.Text = "";
        _minesLabel.Visible = false;
        vbox.AddChild(_minesLabel);

        _buildTimerLabel = new Label();
        _buildTimerLabel.AddThemeFontSizeOverride("font_size", 14);
        _buildTimerLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.8f, 1f));
        _buildTimerLabel.Text = "";
        _buildTimerLabel.Visible = false;
        vbox.AddChild(_buildTimerLabel);

        var spacerFort = new Control();
        spacerFort.CustomMinimumSize = new Vector2(0, 6);
        vbox.AddChild(spacerFort);

        _buildFortBtn = new Button();
        _buildFortBtn.Text = "Укрепить";
        _buildFortBtn.AddThemeFontSizeOverride("font_size", 14);
        _buildFortBtn.Pressed += OnBuildFortPressed;
        _buildFortBtn.Visible = false;
        vbox.AddChild(_buildFortBtn);

        var spacerFort2 = new Control();
        spacerFort2.CustomMinimumSize = new Vector2(0, 4);
        vbox.AddChild(spacerFort2);

        _fortLabel = new Label();
        _fortLabel.AddThemeFontSizeOverride("font_size", 14);
        _fortLabel.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.4f));
        _fortLabel.Text = "";
        _fortLabel.Visible = false;
        vbox.AddChild(_fortLabel);

        var spacerUni = new Control();
        spacerUni.CustomMinimumSize = new Vector2(0, 6);
        vbox.AddChild(spacerUni);

        _buildUniBtn = new Button();
        _buildUniBtn.Text = "Построить университет";
        _buildUniBtn.AddThemeFontSizeOverride("font_size", 14);
        _buildUniBtn.Pressed += OnBuildUniPressed;
        _buildUniBtn.Visible = false;
        vbox.AddChild(_buildUniBtn);

        var spacerUni2 = new Control();
        spacerUni2.CustomMinimumSize = new Vector2(0, 4);
        vbox.AddChild(spacerUni2);

        _uniLabel = new Label();
        _uniLabel.AddThemeFontSizeOverride("font_size", 14);
        _uniLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.8f, 1f));
        _uniLabel.Text = "";
        _uniLabel.Visible = false;
        vbox.AddChild(_uniLabel);

        var spacer6 = new Control();
        spacer6.CustomMinimumSize = new Vector2(0, 4);
        vbox.AddChild(spacer6);

        _statusLabel = new Label();
        _statusLabel.AddThemeFontSizeOverride("font_size", 14);
        _statusLabel.AddThemeColorOverride("font_color", Colors.Green);
        _statusLabel.Text = "";
        vbox.AddChild(_statusLabel);

        Hide();
    }

    public override void _Process(double delta)
    {
        if (!Visible || !_isCapital) return;

        _updateTimer += (float)delta;
        if (_updateTimer < 0.5f) return;
        _updateTimer = 0f;

        int playerId = GetActingPlayerId();
        if (playerId < 0) return;

        int mines = GameManager.Instance.Mines.GetValueOrDefault(playerId, 0);
        int gold = GameManager.Instance.Gold.GetValueOrDefault(playerId, 0);
        float buildTimer = GameManager.Instance.BuildTimers.GetValueOrDefault(playerId, 0);

        _minesLabel.Text = $"Шахт: {mines}/{GameManager.MaxMines}";

        bool godMode = GameManager.Instance.IsGodMode;
        int nextMineCost = GameManager.GetMineCost(mines);
        bool canBuild = godMode
            ? mines < GameManager.MaxMines && buildTimer <= 0
            : mines < GameManager.MaxMines
            && gold >= nextMineCost
            && buildTimer <= 0;
        _buildMineBtn.Disabled = !canBuild;
        _buildMineBtn.Text = $"Построить шахту ({nextMineCost} золота)";

        if (buildTimer > 0)
            _buildTimerLabel.Text = $"Строится: {buildTimer:F1}с";
        else
            _buildTimerLabel.Text = "";

        UpdateFortUI(playerId, godMode);
        UpdateUniUI(playerId, godMode);
    }

    private void UpdateUniUI(int actingId, bool god)
    {
        int unis = GameManager.Instance.Universities.GetValueOrDefault(actingId, 0);
        int gold = GameManager.Instance.Gold.GetValueOrDefault(actingId, 0);
        float bt = GameManager.Instance.BuildTimers.GetValueOrDefault(actingId, 0);

        _uniLabel.Text = $"Университеты: {unis}/{GameManager.MaxUniversities}";

        if (unis >= GameManager.MaxUniversities)
        {
            _buildUniBtn.Text = "Университеты: МАКС";
            _buildUniBtn.Disabled = true;
            return;
        }

        _buildUniBtn.Text = god
            ? "Построить университет (бог)"
            : $"Построить университет ({GameManager.UniversityCost} золота)";

        bool canUni = god
            ? bt <= 0
            : gold >= GameManager.UniversityCost && bt <= 0;
        _buildUniBtn.Disabled = !canUni;
    }

    private void UpdateFortUI(int actingId, bool god)
    {
        var map = GetNodeOrNull<MapGenerator>("/root/Main");
        int level = map?.GetRegionById(_currentRegionId)?.FortLevel ?? 0;
        int gold = GameManager.Instance.Gold.GetValueOrDefault(actingId, 0);
        float bt = GameManager.Instance.BuildTimers.GetValueOrDefault(actingId, 0);

        _fortLabel.Text = $"Укрепления: {level}/{GameManager.MaxFortLevel}";

        if (level >= GameManager.MaxFortLevel)
        {
            _buildFortBtn.Text = "Укрепления: МАКС";
            _buildFortBtn.Disabled = true;
            return;
        }

        int cost = GameManager.GetFortCost(level + 1);
        _buildFortBtn.Text = god
            ? $"Укрепить до {level + 1} (бог)"
            : $"Укрепить до {level + 1} ({cost} золота)";

        bool canFort = god
            ? bt <= 0
            : gold >= cost && bt <= 0;
        _buildFortBtn.Disabled = !canFort;
    }

    public void OnRegionSelected(string name, int gold, string ownerName, int regionId, bool isCapital)
    {
        _currentRegionId = regionId;
        _isCapital = isCapital;
        _currentOwnerName = ownerName;
        _statusLabel.Text = "";

        _nameLabel.Text = $"Регион: {name}";
        _ownerLabel.Text = $"Владелец: {ownerName}";

        int playerId = GameManager.Instance.PlayerNationId;
        bool isOwnedByPlayer = playerId >= 0 && ownerName != "Нейтральная"
            && GetNodeOrNull<MapGenerator>("/root/Main")?.GetNationById(playerId)?.Name == ownerName;

        var map = GetNodeOrNull<MapGenerator>("/root/Main");
        bool observer = map != null && map.IsPlayerObserver();
        if (observer) isOwnedByPlayer = false;

        bool god = GameManager.Instance.IsGodMode && ownerName != "Нейтральная" && !observer;

        _buildMineBtn.Visible = isCapital && (isOwnedByPlayer || god);
        _minesLabel.Visible = isCapital && (isOwnedByPlayer || god);
        _buildTimerLabel.Visible = isCapital && (isOwnedByPlayer || god);
        _buildFortBtn.Visible = isCapital && (isOwnedByPlayer || god);
        _fortLabel.Visible = isCapital && (isOwnedByPlayer || god);
        _buildUniBtn.Visible = isCapital && (isOwnedByPlayer || god);
        _uniLabel.Visible = isCapital && (isOwnedByPlayer || god);

        if (isCapital && (isOwnedByPlayer || god))
        {
            int actingId = god ? GetActingPlayerId() : playerId;
            int mines = GameManager.Instance.Mines.GetValueOrDefault(actingId, 0);
            int g = GameManager.Instance.Gold.GetValueOrDefault(actingId, 0);
            float bt = GameManager.Instance.BuildTimers.GetValueOrDefault(actingId, 0);

            _minesLabel.Text = $"Шахт: {mines}/{GameManager.MaxMines}";

            int nextCost = GameManager.GetMineCost(mines);
            bool canBuild = god
                ? mines < GameManager.MaxMines && bt <= 0
                : mines < GameManager.MaxMines && g >= nextCost && bt <= 0;
            _buildMineBtn.Disabled = !canBuild;
            _buildMineBtn.Text = $"Построить шахту ({nextCost} золота)";

            if (bt > 0)
                _buildTimerLabel.Text = $"Строится: {bt:F1}с";
            else
                _buildTimerLabel.Text = "";

            UpdateFortUI(actingId, god);
            UpdateUniUI(actingId, god);
        }

        Show();
    }

    public void OnRegionDeselected()
    {
        _currentRegionId = -1;
        _isCapital = false;
        _statusLabel.Text = "";
        _armyInfoLabel.Visible = false;
        Hide();
    }

    public void ShowArmyInfo(int armyId, int soldiers, int hp, int maxHp, UnitType type = UnitType.Infantry)
    {
        string typeName = type switch
        {
            UnitType.Militia => "Ополчение",
            UnitType.Elite => "Элита",
            _ => "Пехота",
        };
        _armyInfoLabel.Text = $"Армия #{armyId} [{typeName}]  Солдат: {soldiers}  HP: {hp}/{maxHp}";
        _armyInfoLabel.Visible = true;
    }

    public void HideArmyInfo()
    {
        _armyInfoLabel.Visible = false;
    }

    public void ShowMergeInfo(string text, bool enabled)
    {
        _mergeBtn.Text = text;
        _mergeBtn.Disabled = !enabled;
        _mergeBtn.Visible = true;
    }

    public void HideMergeInfo()
    {
        _mergeBtn.Visible = false;
    }

    private void OnMergePressed()
    {
        AudioHub.Instance?.PlayClick();
        GetNodeOrNull<MapGenerator>("/root/Main")?.TryMergeSelected();
    }

    private int GetActingPlayerId()
    {
        if (!GameManager.Instance.IsGodMode)
            return GameManager.Instance.PlayerNationId;
        var map = GetNodeOrNull<MapGenerator>("/root/Main");
        return map?.GetRegionById(_currentRegionId)?.OwnerId ?? -1;
    }

    private void OnBuildMinePressed()
    {
        var map = GetNodeOrNull<MapGenerator>("/root/Main");
        if (map == null) return;

        if (GameManager.Instance.IsMultiplayerGame)
        {
            NetworkManager.Instance.SendCommand(new GameCommand
            {
                Type = CommandType.BuildMine,
                PlayerId = GameManager.Instance.PlayerNationId,
            });
            _statusLabel.Text = "Запрос отправлен";
            _statusLabel.AddThemeColorOverride("font_color", Colors.Yellow);
        }
        else
        {
            bool success = map.BuildMineForPlayer(_currentRegionId);
            if (success)
            {
                AudioHub.Instance?.PlayClick();
                _statusLabel.Text = "Шахта строится!";
                _statusLabel.AddThemeColorOverride("font_color", Colors.Green);
            }
            else
            {
                _statusLabel.Text = "Нельзя построить!";
                _statusLabel.AddThemeColorOverride("font_color", Colors.Red);
            }
        }
    }

    private void OnBuildFortPressed()
    {
        var map = GetNodeOrNull<MapGenerator>("/root/Main");
        if (map == null) return;

        if (GameManager.Instance.IsMultiplayerGame)
        {
            NetworkManager.Instance.SendCommand(new GameCommand
            {
                Type = CommandType.BuildFort,
                PlayerId = GameManager.Instance.PlayerNationId,
            });
            _statusLabel.Text = "Запрос отправлен";
            _statusLabel.AddThemeColorOverride("font_color", Colors.Yellow);
        }
        else
        {
            bool success = map.BuildFortForPlayer(_currentRegionId);
            if (success)
            {
                AudioHub.Instance?.PlayClick();
                _statusLabel.Text = "Укрепления строятся!";
                _statusLabel.AddThemeColorOverride("font_color", Colors.Green);
            }
            else
            {
                _statusLabel.Text = "Нельзя построить!";
                _statusLabel.AddThemeColorOverride("font_color", Colors.Red);
            }
        }
    }

    private void OnBuildUniPressed()
    {
        var map = GetNodeOrNull<MapGenerator>("/root/Main");
        if (map == null) return;

        if (GameManager.Instance.IsMultiplayerGame)
        {
            NetworkManager.Instance.SendCommand(new GameCommand
            {
                Type = CommandType.BuildUniversity,
                PlayerId = GameManager.Instance.PlayerNationId,
            });
            _statusLabel.Text = "Запрос отправлен";
            _statusLabel.AddThemeColorOverride("font_color", Colors.Yellow);
        }
        else
        {
            bool success = map.BuildUniversityForPlayer();
            if (success)
            {
                AudioHub.Instance?.PlayClick();
                _statusLabel.Text = "Университет строится!";
                _statusLabel.AddThemeColorOverride("font_color", Colors.Green);
            }
            else
            {
                _statusLabel.Text = "Нельзя построить!";
                _statusLabel.AddThemeColorOverride("font_color", Colors.Red);
            }
        }
    }
}
