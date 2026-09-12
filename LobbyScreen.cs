using Godot;
using System.Collections.Generic;

public partial class LobbyScreen : Control
{
    private LineEdit _hostInput;
    private LineEdit _portInput;
    private LineEdit _joinPortInput;
    private Button _createBtn;
    private Button _joinBtn;
    private Button _startBtn;
    private Button _backBtn;
    private Button _lanBtn;
    private Button _wanBtn;
    private Label _wanLabel;
    private HttpRequest _wanRequest;
    private Label _statusLabel;
    private Label _playersLabel;
    private OptionButton _playerCountOption;
    private OptionButton _botCountOption;
    private VBoxContainer _serverList;
    private Label _lanHint;

    public override void _Ready()
    {
        var bg = new ColorRect();
        bg.Color = new Color(0.08f, 0.08f, 0.12f);
        bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(bg);

        var centerContainer = new CenterContainer();
        centerContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(centerContainer);

        var mainBox = new VBoxContainer();
        mainBox.CustomMinimumSize = new Vector2(460, 680);
        mainBox.AddThemeConstantOverride("separation", 6);
        centerContainer.AddChild(mainBox);

        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        mainBox.AddChild(scroll);

        var panel = new VBoxContainer();
        panel.CustomMinimumSize = new Vector2(420, 0);
        panel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        panel.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(panel);

        var title = new Label();
        title.Text = "NATION PATH";
        title.AddThemeFontSizeOverride("font_size", 32);
        title.AddThemeColorOverride("font_color", new Color(0.25f, 0.85f, 0.4f));
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        panel.AddChild(title);

        var subtitle = new Label();
        subtitle.Text = "Сетевая игра";
        subtitle.AddThemeFontSizeOverride("font_size", 16);
        subtitle.AddThemeColorOverride("font_color", Colors.LightGray);
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        subtitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        panel.AddChild(subtitle);

        AddSpacer(panel, 10);

        // --- Host panel ---
        var hostTitle = new Label();
        hostTitle.Text = "Создать игру";
        hostTitle.AddThemeFontSizeOverride("font_size", 18);
        hostTitle.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.2f));
        hostTitle.HorizontalAlignment = HorizontalAlignment.Center;
        hostTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        panel.AddChild(hostTitle);

        var portGrid = new GridContainer();
        portGrid.Columns = 2;
        portGrid.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        portGrid.AddThemeConstantOverride("h_separation", 10);
        portGrid.AddThemeConstantOverride("v_separation", 4);
        panel.AddChild(portGrid);

        AddFieldLabel(portGrid, "Порт:");
        _portInput = new LineEdit();
        _portInput.Text = NetworkManager.DefaultPort.ToString();
        _portInput.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _portInput.AddThemeFontSizeOverride("font_size", 14);
        portGrid.AddChild(_portInput);

        AddFieldLabel(portGrid, "Игроков:");
        _playerCountOption = new OptionButton();
        _playerCountOption.AddItem("2");
        _playerCountOption.AddItem("3");
        _playerCountOption.AddItem("4");
        _playerCountOption.Selected = 0;
        _playerCountOption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _playerCountOption.AddThemeFontSizeOverride("font_size", 14);
        portGrid.AddChild(_playerCountOption);

        AddFieldLabel(portGrid, "Боты:");
        _botCountOption = new OptionButton();
        _botCountOption.AddItem("0");
        _botCountOption.AddItem("1");
        _botCountOption.AddItem("2");
        _botCountOption.Selected = 0;
        _botCountOption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _botCountOption.AddThemeFontSizeOverride("font_size", 14);
        portGrid.AddChild(_botCountOption);

        _createBtn = new Button();
        _createBtn.Text = "Создать сервер";
        _createBtn.CustomMinimumSize = new Vector2(0, 34);
        _createBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _createBtn.AddThemeFontSizeOverride("font_size", 14);
        _createBtn.Pressed += OnCreatePressed;
        panel.AddChild(_createBtn);

        AddSpacer(panel, 4);
        panel.AddChild(new HSeparator());
        AddSpacer(panel, 4);

        // --- Join panel ---
        var joinTitle = new Label();
        joinTitle.Text = "Подключиться";
        joinTitle.AddThemeFontSizeOverride("font_size", 18);
        joinTitle.AddThemeColorOverride("font_color", new Color(0.25f, 0.4f, 0.9f));
        joinTitle.HorizontalAlignment = HorizontalAlignment.Center;
        joinTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        panel.AddChild(joinTitle);

        var ipGrid = new GridContainer();
        ipGrid.Columns = 2;
        ipGrid.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        ipGrid.AddThemeConstantOverride("h_separation", 10);
        ipGrid.AddThemeConstantOverride("v_separation", 4);
        panel.AddChild(ipGrid);

        AddFieldLabel(ipGrid, "IP:");
        _hostInput = new LineEdit();
        _hostInput.Text = "127.0.0.1";
        _hostInput.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _hostInput.AddThemeFontSizeOverride("font_size", 14);
        ipGrid.AddChild(_hostInput);

        AddFieldLabel(ipGrid, "Порт:");
        _joinPortInput = new LineEdit();
        _joinPortInput.Text = NetworkManager.DefaultPort.ToString();
        _joinPortInput.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _joinPortInput.AddThemeFontSizeOverride("font_size", 14);
        ipGrid.AddChild(_joinPortInput);

        _joinBtn = new Button();
        _joinBtn.Text = "Подключиться";
        _joinBtn.CustomMinimumSize = new Vector2(0, 34);
        _joinBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _joinBtn.AddThemeFontSizeOverride("font_size", 14);
        _joinBtn.Pressed += OnJoinPressed;
        panel.AddChild(_joinBtn);

        AddSpacer(panel, 4);

        // --- LAN ---
        _lanBtn = new Button();
        _lanBtn.Text = "Поиск в LAN";
        _lanBtn.CustomMinimumSize = new Vector2(0, 30);
        _lanBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _lanBtn.AddThemeFontSizeOverride("font_size", 13);
        _lanBtn.Pressed += OnLanPressed;
        panel.AddChild(_lanBtn);

        _wanBtn = new Button();
        _wanBtn.Text = "Мой внешний IP";
        _wanBtn.CustomMinimumSize = new Vector2(0, 30);
        _wanBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _wanBtn.AddThemeFontSizeOverride("font_size", 13);
        _wanBtn.Pressed += OnWanPressed;
        panel.AddChild(_wanBtn);

        _wanLabel = new Label();
        _wanLabel.Text = "";
        _wanLabel.AddThemeFontSizeOverride("font_size", 12);
        _wanLabel.AddThemeColorOverride("font_color", Colors.LightGray);
        _wanLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _wanLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        panel.AddChild(_wanLabel);

        _serverList = new VBoxContainer();
        _serverList.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _serverList.AddThemeConstantOverride("separation", 2);
        panel.AddChild(_serverList);

        _lanHint = new Label();
        _lanHint.Text = "";
        _lanHint.AddThemeFontSizeOverride("font_size", 11);
        _lanHint.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        _lanHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _lanHint.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        panel.AddChild(_lanHint);

        AddSpacer(panel, 4);

        // --- Status (всегда видимы, под скроллом) ---
        _playersLabel = new Label();
        _playersLabel.Text = "";
        _playersLabel.AddThemeFontSizeOverride("font_size", 13);
        _playersLabel.AddThemeColorOverride("font_color", Colors.LightGray);
        _playersLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _playersLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        mainBox.AddChild(_playersLabel);

        _statusLabel = new Label();
        _statusLabel.Text = "";
        _statusLabel.AddThemeFontSizeOverride("font_size", 13);
        _statusLabel.AddThemeColorOverride("font_color", Colors.Green);
        _statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _statusLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        mainBox.AddChild(_statusLabel);

        _startBtn = new Button();
        _startBtn.Text = "Начать игру";
        _startBtn.CustomMinimumSize = new Vector2(0, 38);
        _startBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _startBtn.AddThemeFontSizeOverride("font_size", 16);
        _startBtn.Visible = false;
        _startBtn.Pressed += OnStartPressed;
        panel.AddChild(_startBtn);

        AddSpacer(panel, 4);

        _backBtn = new Button();
        _backBtn.Text = "Назад";
        _backBtn.CustomMinimumSize = new Vector2(0, 30);
        _backBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _backBtn.AddThemeFontSizeOverride("font_size", 13);
        _backBtn.Pressed += OnBackPressed;
        panel.AddChild(_backBtn);

        NetworkManager.Instance.PlayerConnected += OnPlayerConnected;
        NetworkManager.Instance.PlayerDisconnected += OnPlayerDisconnected;
        NetworkManager.Instance.ServerStarted += OnServerStarted;
        NetworkManager.Instance.ConnectionFailed += OnConnectionFailed;
        NetworkManager.Instance.ServerListUpdated += OnServerListUpdated;
    }

    private void AddSpacer(VBoxContainer parent, float height)
    {
        var s = new Control();
        s.CustomMinimumSize = new Vector2(0, height);
        parent.AddChild(s);
    }

    private void AddFieldLabel(GridContainer parent, string text)
    {
        var label = new Label();
        label.Text = text;
        label.AddThemeFontSizeOverride("font_size", 14);
        label.AddThemeColorOverride("font_color", Colors.White);
        label.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        parent.AddChild(label);
    }

    public override void _ExitTree()
    {
        _wanRequest?.QueueFree();
        _wanRequest = null;
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.PlayerConnected -= OnPlayerConnected;
            NetworkManager.Instance.PlayerDisconnected -= OnPlayerDisconnected;
            NetworkManager.Instance.ServerStarted -= OnServerStarted;
            NetworkManager.Instance.ConnectionFailed -= OnConnectionFailed;
            NetworkManager.Instance.ServerListUpdated -= OnServerListUpdated;
        }
        NetworkManager.Instance?.StopListening();
    }

    private void OnLanPressed()
    {
        NetworkManager.Instance.StartListeningForServers();
        _lanBtn.Disabled = true;
        _lanBtn.Text = "Поиск...";
        _lanHint.Text = "Ищем серверы в сети... Для игры через интернет используйте Radmin VPN или ZeroTier — они создают виртуальную LAN-сеть.";
        ShowStatus("Поиск серверов в LAN...", Colors.Yellow);
    }

    private void OnWanPressed()
    {
        _wanBtn.Disabled = true;
        _wanBtn.Text = "Узнаём...";
        _wanLabel.Text = "";
        AudioHub.Instance?.PlayClick();

        _wanRequest?.QueueFree();
        _wanRequest = new HttpRequest();
        _wanRequest.Timeout = 10;
        AddChild(_wanRequest);
        _wanRequest.RequestCompleted += OnWanCompleted;
        var err = _wanRequest.Request("https://api.ipify.org");
        if (err != Error.Ok)
        {
            _wanRequest.QueueFree();
            _wanRequest = null;
            _wanBtn.Disabled = false;
            _wanBtn.Text = "Мой внешний IP";
            _wanLabel.Text = "Нет соединения. Проверьте интернет.";
        }
    }

    private void OnWanCompleted(long result, long responseCode, string[] headers, byte[] body)
    {
        _wanRequest?.QueueFree();
        _wanRequest = null;
        _wanBtn.Disabled = false;
        _wanBtn.Text = "Мой внешний IP";

        string ip = "";
        try { ip = System.Text.Encoding.UTF8.GetString(body).Trim(); } catch { }
        bool looksLikeIp = ip.Length >= 7 && ip.Length <= 45 && (ip.Contains(".") || ip.Contains(":"));

        if (result == (long)HttpRequest.Result.Success && responseCode == 200 && looksLikeIp)
        {
            _wanLabel.Text = $"Ваш IP: {ip}";
            DisplayServer.ClipboardSet(ip);
            ShowStatus("Внешний IP скопирован в буфер обмена!", Colors.Green);
        }
        else
        {
            _wanLabel.Text = "Не удалось узнать IP (нет интернета?).";
        }
    }

    private void OnServerListUpdated()
    {
        foreach (var child in _serverList.GetChildren())
            child.QueueFree();

        var servers = NetworkManager.Instance.DiscoveredServers;
        if (servers.Count == 0)
        {
            _lanHint.Text = "Серверы не найдены. Убедитесь, что друг запустил сервер в одной сети (WiFi/Radmin VPN).";
            return;
        }

        _lanHint.Text = $"Найдено серверов: {servers.Count}. Нажмите чтобы подключиться.";
        _lanHint.AddThemeColorOverride("font_color", Colors.Green);

        foreach (var server in servers)
        {
            var btn = new Button();
            btn.Text = $"{server.GameName}  |  {server.Host}:{server.Port}  |  {server.CurrentPlayers}/{server.MaxPlayers} игроков";
            btn.CustomMinimumSize = new Vector2(0, 28);
            btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            btn.AddThemeFontSizeOverride("font_size", 12);

            var style = new StyleBoxFlat();
            style.BgColor = new Color(0.15f, 0.2f, 0.3f);
            style.CornerRadiusTopLeft = 4;
            style.CornerRadiusTopRight = 4;
            style.CornerRadiusBottomLeft = 4;
            style.CornerRadiusBottomRight = 4;
            btn.AddThemeStyleboxOverride("normal", style);

            var hoverStyle = new StyleBoxFlat();
            hoverStyle.BgColor = new Color(0.2f, 0.3f, 0.5f);
            hoverStyle.CornerRadiusTopLeft = 4;
            hoverStyle.CornerRadiusTopRight = 4;
            hoverStyle.CornerRadiusBottomLeft = 4;
            hoverStyle.CornerRadiusBottomRight = 4;
            btn.AddThemeStyleboxOverride("hover", hoverStyle);

            btn.AddThemeColorOverride("font_color", Colors.White);
            btn.AddThemeColorOverride("font_hover_color", Colors.White);

            string host = server.Host;
            int port = server.Port;
            btn.Pressed += () =>
            {
                _hostInput.Text = host;
                _joinPortInput.Text = port.ToString();
                OnJoinPressed();
            };

            _serverList.AddChild(btn);
        }

        ShowStatus($"Найдено {servers.Count} сервер(ов)!", Colors.Green);
    }

    private void OnCreatePressed()
    {
        if (!int.TryParse(_portInput.Text, out int port) || port < 1 || port > 65535)
        {
            ShowStatus("Неверный порт! (1-65535)", Colors.Red);
            return;
        }

        int maxPlayers = int.Parse(_playerCountOption.GetItemText(_playerCountOption.Selected));

        _createBtn.Disabled = true;
        _joinBtn.Disabled = true;
        ShowStatus($"Создание сервера на порту {port}...", Colors.Yellow);

        GD.Print($"[Lobby] Создание сервера: порт={port}, игроков={maxPlayers}");
        NetworkManager.Instance.CreateServer(port, maxPlayers);
    }

    private void OnJoinPressed()
    {
        string host = _hostInput.Text.Trim();
        if (string.IsNullOrEmpty(host))
        {
            ShowStatus("Введите IP адрес!", Colors.Red);
            return;
        }

        if (!int.TryParse(_joinPortInput.Text, out int port) || port < 1 || port > 65535)
        {
            ShowStatus("Неверный порт! (1-65535)", Colors.Red);
            return;
        }

        _joinBtn.Disabled = true;
        _createBtn.Disabled = true;
        ShowStatus($"Подключение к {host}:{port}...", Colors.Yellow);

        GD.Print($"[Lobby] Подключение: {host}:{port}");
        NetworkManager.Instance.StopListening();
        NetworkManager.Instance.JoinServer(host, port);
    }

    private void ShowStatus(string text, Color color)
    {
        _statusLabel.Text = text;
        _statusLabel.AddThemeColorOverride("font_color", color);
    }

    private void OnStartPressed()
    {
        GameManager.Instance.IsMultiplayerGame = true;
        GameManager.Instance.IsGodMode = false;

        int humans = NetworkManager.Instance.MaxPlayersCount;
        int bots = int.Parse(_botCountOption.GetItemText(_botCountOption.Selected));
        bots = Mathf.Clamp(bots, 0, 4 - humans);
        GameManager.Instance.TotalPlayers = humans + bots;
        GameManager.Instance.BotCount = bots;

        // Раздача наций: люди по порядку подключения, боты — остальные.
        NetworkManager.Instance.AssignNationsForMatch(
            NetworkManager.Instance.GetAllPeerIds(), bots);
        NetworkManager.Instance.Rpc(nameof(NetworkManager.RpcStartGame));
    }

    private void OnBackPressed()
    {
        NetworkManager.Instance.StopNetwork();
        GetTree().ChangeSceneToFile("res://NationSelect.tscn");
    }

    private void OnServerStarted()
    {
        int max = NetworkManager.Instance.MaxPlayersCount;
        ShowStatus($"Сервер запущен! Ожидание игроков (1/{max})...", Colors.Green);
        _playersLabel.Text = $"Сервер активен | Порт: {_portInput.Text} | Игроков: 1/{max}";
        _startBtn.Visible = false;
        GD.Print($"[Lobby] Сервер запущен, ожидание {max - 1} игроков");
    }

    private void OnPlayerConnected(int peerId)
    {
        int max = NetworkManager.Instance.MaxPlayersCount;
        int count = NetworkManager.Instance.GetAllPeerIds().Count;
        _playersLabel.Text = $"Игроков: {count}/{max}";
        ShowStatus($"Игрок {peerId} подключился!", Colors.Cyan);

        if (NetworkManager.Instance.IsServer && count >= max)
        {
            _startBtn.Visible = true;
            ShowStatus("Все игроки подключились! Нажмите 'Начать игру'", Colors.Green);
        }
        GD.Print($"[Lobby] Игрок {peerId} подключился ({count}/{max})");
    }

    private void OnPlayerDisconnected(int peerId)
    {
        int max = NetworkManager.Instance.MaxPlayersCount;
        int count = NetworkManager.Instance.GetAllPeerIds().Count;
        _playersLabel.Text = $"Игроков: {count}/{max}";
        ShowStatus($"Игрок {peerId} отключился", Colors.Red);
        _startBtn.Visible = false;
    }

    private void OnConnectionFailed(int peerId, string reason)
    {
        ShowStatus($"ОШИБКА: {reason}", Colors.Red);
        _createBtn.Disabled = false;
        _joinBtn.Disabled = false;
        GD.PrintErr($"[Lobby] Ошибка: {reason}");
    }
}
