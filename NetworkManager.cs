using Godot;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public partial class NetworkManager : Node
{
    public static NetworkManager Instance { get; private set; }

    public const int DefaultPort = 7000;
    public const int BroadcastPort = 7001;
    public const int MaxPlayers = 4;
    public const int ProtocolVersion = 4;
    private const float ConnectionTimeout = 10.0f;

    public bool IsServer { get; private set; }
    public bool IsClient { get; private set; }
    public bool IsNetworked { get; private set; }
    public int LocalPeerId { get; private set; }
    public int MaxPlayersCount { get; set; } = 2;

    private Dictionary<int, int> _peerNationMap = new();
    private List<int> _connectedPeers = new();
    private List<int> _readyPeers = new();

    // Connection timeout
    private float _connectionTimer = -1f;
    private bool _waitingForConnection = false;

    // LAN Discovery
    private UdpClient _broadcastSender;
    private UdpClient _broadcastListener;
    private Thread _broadcastListenerThread;
    private bool _isBroadcasting;
    private volatile bool _isListening;
    private float _broadcastTimer;
    private const float BroadcastInterval = 2.0f;
    private int _serverPort = DefaultPort;
    private List<DiscoveredServer> _pendingServers = new();
    private object _pendingLock = new();

    public List<DiscoveredServer> DiscoveredServers { get; private set; } = new();
    public event Action ServerListUpdated;

    public struct DiscoveredServer
    {
        public string Host;
        public int Port;
        public string GameName;
        public int CurrentPlayers;
        public int MaxPlayers;
    }

    public event Action<int> PlayerConnected;
    public event Action<int> PlayerDisconnected;
    public event Action AllPlayersReady;
    public event Action<int, string> ConnectionFailed;
    public event Action ServerStarted;

    public override void _Ready()
    {
        Instance = this;
        _pendingLock = new object();
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        GD.Print("[Net] NetworkManager инициализирован");
    }

    public override void _Process(double delta)
    {
        if (_isBroadcasting)
        {
            _broadcastTimer -= (float)delta;
            if (_broadcastTimer <= 0f)
            {
                SendBroadcast();
                _broadcastTimer = BroadcastInterval;
            }
        }

        if (_waitingForConnection)
        {
            _connectionTimer -= (float)delta;
            if (_connectionTimer <= 0f)
            {
                _waitingForConnection = false;
                GD.PrintErr("[Net] Таймаут подключения!");
                ConnectionFailed?.Invoke(-1, "Таймаут подключения (10 сек). Проверьте IP, порт и firewall.");
                CallDeferred(nameof(DeferredStopNetwork));
            }
        }

        List<DiscoveredServer> snapshot = null;
        lock (_pendingLock)
        {
            if (_pendingServers.Count > 0)
            {
                snapshot = new List<DiscoveredServer>(_pendingServers);
                _pendingServers.Clear();
            }
        }

        if (snapshot != null)
        {
            foreach (var s in snapshot)
                AddDiscoveredServerInternal(s);
        }
    }

    public override void _ExitTree()
    {
        StopBroadcast();
        StopListening();
    }

    // --- Server ---

    public void CreateServer(int port = DefaultPort, int maxPlayers = 2)
    {
        MaxPlayersCount = maxPlayers;
        _serverPort = port;

        GD.Print($"[Net] Создание сервера: порт={port}, игроков={maxPlayers}");

        var peer = new ENetMultiplayerPeer();
        var error = peer.CreateServer(port, maxPlayers);
        if (error != Error.Ok)
        {
            string msg = $"Не удалось создать сервер: {error}";
            GD.PrintErr($"[Net] {msg}");
            ConnectionFailed?.Invoke(-1, msg);
            return;
        }

        Multiplayer.MultiplayerPeer = peer;
        IsServer = true;
        IsNetworked = true;
        LocalPeerId = 1;
        _connectedPeers.Clear();
        _readyPeers.Clear();
        _peerNationMap.Clear();

        GD.Print($"[Net] Сервер создан! порт={port}, PeerId=1");
        StartBroadcast(port, maxPlayers);
        ServerStarted?.Invoke();
    }

    // --- Client ---

    public void JoinServer(string host, int port = DefaultPort)
    {
        GD.Print($"[Net] Подключение к {host}:{port}...");

        _connectedPeers.Clear();
        _readyPeers.Clear();
        _peerNationMap.Clear();

        var peer = new ENetMultiplayerPeer();
        var error = peer.CreateClient(host, port);
        if (error != Error.Ok)
        {
            string msg = $"Не удалось начать подключение: {error}";
            GD.PrintErr($"[Net] {msg}");
            ConnectionFailed?.Invoke(-1, msg);
            return;
        }

        Multiplayer.MultiplayerPeer = peer;
        IsClient = true;
        IsNetworked = true;

        _waitingForConnection = true;
        _connectionTimer = ConnectionTimeout;

        GD.Print($"[Net] ENet клиент создан, ожидание ответа... (таймаут {ConnectionTimeout} сек)");
    }

    public void StopNetwork()
    {
        if (Multiplayer.MultiplayerPeer != null)
        {
            Multiplayer.MultiplayerPeer.Close();
            Multiplayer.MultiplayerPeer = null;
        }
        IsServer = false;
        IsClient = false;
        IsNetworked = false;
        LocalPeerId = 0;
        _connectedPeers.Clear();
        _readyPeers.Clear();
        _peerNationMap.Clear();
        _waitingForConnection = false;
        StopBroadcast();
        GD.Print("[Net] Сеть остановлена");
    }

    public void DeferredStopNetwork()
    {
        StopNetwork();
    }

    // --- Nation ---

    public void AssignNation(int peerId, int nationId)
    {
        _peerNationMap[peerId] = nationId;
        Rpc(nameof(RpcAssignNation), peerId, nationId,
            GameManager.Instance.TotalPlayers, GameManager.Instance.BotCount);
    }

    // Раздача наций перед стартом: люди по порядку, боты — следующие нации.
    public void AssignNationsForMatch(List<int> peerIdsInOrder, int botCount)
    {
        _peerNationMap.Clear();
        int n = 0;
        foreach (int pid in peerIdsInOrder)
        {
            if (n >= 4) break;
            _peerNationMap[pid] = n++;
        }
        int total = Mathf.Min(4, peerIdsInOrder.Count + botCount);
        GameManager.Instance.TotalPlayers = total;
        GameManager.Instance.BotCount = Mathf.Max(0, total - peerIdsInOrder.Count);
        GameManager.Instance.PeerNationMap = new Dictionary<int, int>(_peerNationMap);
        foreach (var kv in _peerNationMap)
            Rpc(nameof(RpcAssignNation), kv.Key, kv.Value,
                GameManager.Instance.TotalPlayers, GameManager.Instance.BotCount);
    }

    public int GetNationForPeer(int peerId)
    {
        return _peerNationMap.GetValueOrDefault(peerId, -1);
    }

    public int GetPeerForNation(int nationId)
    {
        foreach (var kvp in _peerNationMap)
            if (kvp.Value == nationId)
                return kvp.Key;
        return -1;
    }

    public List<int> GetAllPeerIds()
    {
        var ids = new List<int>();
        if (IsServer) ids.Add(1);
        ids.AddRange(_connectedPeers);
        return ids;
    }

    public void PlayerReady()
    {
        if (IsServer)
        {
            _readyPeers.Add(1);
            Rpc(nameof(RpcPlayerReady), 1);
        }
        else
        {
            RpcId(1, nameof(RpcPlayerReady), LocalPeerId);
        }
    }

    // --- Server RPCs ---

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
    public void RpcPlayerReady(int peerId)
    {
        if (!_readyPeers.Contains(peerId))
            _readyPeers.Add(peerId);

        GD.Print($"Игрок {peerId} готов ({_readyPeers.Count}/{MaxPlayersCount})");

        if (_readyPeers.Count >= MaxPlayersCount)
            AllPlayersReady?.Invoke();
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
    public void RpcAssignNation(int peerId, int nationId, int totalPlayers = 1, int botCount = 0)
    {
        _peerNationMap[peerId] = nationId;
        GameManager.Instance.PeerNationMap[peerId] = nationId;
        if (peerId == LocalPeerId)
            GameManager.Instance.PlayerNationId = nationId;
        GameManager.Instance.TotalPlayers = totalPlayers;
        GameManager.Instance.BotCount = botCount;
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    public void RpcStartGame()
    {
        GD.Print("Игра начинается!");
        GameManager.Instance.IsMultiplayerGame = true;
        GetTree().ChangeSceneToFile("res://NationSelect.tscn");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    public void RpcGameOver(int winnerId)
    {
        GD.Print($"Игра окончена! Победитель: {winnerId}");
        var map = GetNodeOrNull<MapGenerator>("/root/Main");
        map?.ShowGameOver(winnerId);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    public void RpcRestartMatch()
    {
        GD.Print("Рестарт матча!");
        GetTree().ChangeSceneToFile("res://Main.tscn");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    public void RpcServerStopping()
    {
        if (IsServer) return;
        GD.Print("Сервер завершил игру, выход в меню.");
        StopNetwork();
        GameManager.Instance.IsMultiplayerGame = false;
        GameManager.Instance.IsGodMode = false;
        GetTree().ChangeSceneToFile("res://NationSelect.tscn");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    public void RpcSyncArmyCreated(int armyId, int regionId, int playerId, int soldiers, int typeInt)
    {
        var map = GetNodeOrNull<MapGenerator>("/root/Main");
        if (map == null) return;

        var existing = map.GetArmyById(armyId);
        if (existing != null) return;

        var army = map.CreateArmy(regionId, playerId, soldiers, (UnitType)typeInt);
        if (army != null)
            map.UpdateArmyPositions(regionId);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    public void RpcSyncArmyMove(int armyId, int targetRegionId)
    {
        var map = GetNodeOrNull<MapGenerator>("/root/Main");
        if (map == null) return;
        map.QueueArmyMove(armyId, targetRegionId);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    public void RpcSyncArmyDestroyed(int armyId)
    {
        var map = GetNodeOrNull<MapGenerator>("/root/Main");
        if (map == null) return;
        var army = map.GetArmyById(armyId);
        if (army != null)
        {
            int prevId = army.RegionId;
            map._armies.Remove(army);
            army.QueueFree();
            map.UpdateArmyPositions(prevId);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    public void RpcSyncRegionCaptured(int regionId, int ownerId, float r, float g, float b, int fort)
    {
        var map = GetNodeOrNull<MapGenerator>("/root/Main");
        if (map == null) return;
        var region = map.GetRegionById(regionId);
        if (region == null) return;

        region.OwnerId = ownerId;
        region.Color = new Color(r, g, b);
        region.FortLevel = fort;
        map.RefreshCapitalMarkers();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    public void RpcSyncEconomy(int nationId, int gold, int mines, float buildTimer,
        int research = 0, int unis = 0, int techMask = 0, string buildOrder = "")
    {
        GameManager.Instance.Gold[nationId] = gold;
        GameManager.Instance.Mines[nationId] = mines;
        GameManager.Instance.BuildTimers[nationId] = buildTimer;
        GameManager.Instance.Research[nationId] = research;
        GameManager.Instance.Universities[nationId] = unis;
        GameManager.Instance.TechMask[nationId] = techMask;
        GameManager.Instance.BuildOrders[nationId] = buildOrder;
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    public void RpcSyncArmyHP(int armyId, int hp, int soldiers, bool isWounded)
    {
        var map = GetNodeOrNull<MapGenerator>("/root/Main");
        if (map == null) return;
        var army = map.GetArmyById(armyId);
        if (army == null) return;

        army.HP = hp;
        army.Soldiers = soldiers;
        army.IsWounded = isWounded;
        army.QueueRedraw();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    public void RpcSyncBattleResult(int defenderId, int result)
    {
        GD.Print($"Бой завершён: защитник #{defenderId}, результат={result}");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    public void RpcAnnounceWar(int nationA, int nationB)
    {
        var map = GetNodeOrNull<MapGenerator>("/root/Main");
        map?.AnnounceWar(nationA, nationB);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    public void RpcSyncPactOffer(int fromNation, int toNation)
    {
        int key = GameManager.RelationKey(fromNation, toNation);
        GameManager.Instance.PactOffers[key] = fromNation;
        GameManager.Instance.PactOfferTimers[key] = GameManager.PactOfferTimeout;
        GD.Print($"Предложен пакт: {fromNation} → {toNation}");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    public void RpcSyncPactWithdraw(int nationA, int nationB)
    {
        int key = GameManager.RelationKey(nationA, nationB);
        GameManager.Instance.PactOffers.Remove(key);
        GameManager.Instance.PactOfferTimers.Remove(key);
        GD.Print($"Предложение пакта снято (пара {key})");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    public void RpcSyncRelation(int nationA, int nationB, int stateInt, float pactLeft)
    {
        int key = GameManager.RelationKey(nationA, nationB);
        GameManager.Instance.Relations[key] = stateInt;
        if ((RelationState)stateInt == RelationState.Pact && pactLeft > 0f)
            GameManager.Instance.PactTimers[key] = pactLeft;
        else
            GameManager.Instance.PactTimers.Remove(key);
        GD.Print($"Дипломатия: {nationA}-{nationB} = {(RelationState)stateInt}");
    }

    // --- Client sends to server ---

    public void SendCommand(GameCommand cmd)
    {
        if (IsServer)
        {
            ProcessCommand(cmd);
        }
        else
        {
            RpcId(1, nameof(RpcSendCommand), (int)cmd.Type, cmd.PlayerId, cmd.Arg1, cmd.Arg2, cmd.Arg3);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    public void RpcSendCommand(int typeInt, int playerId, int arg1, int arg2, int arg3)
    {
        var cmd = new GameCommand
        {
            Type = (CommandType)typeInt,
            PlayerId = playerId,
            Arg1 = arg1,
            Arg2 = arg2,
            Arg3 = arg3,
        };
        // Дипломатия — строго от своей нации (анти-подмена).
        if (cmd.Type == CommandType.SetRelation
            || cmd.Type == CommandType.ProposePact
            || cmd.Type == CommandType.AnswerPact)
        {
            int sender = Multiplayer.GetRemoteSenderId();
            int senderNation = _peerNationMap.GetValueOrDefault(sender, -1);
            if (senderNation < 0) return;
            cmd.PlayerId = senderNation;
        }
        ProcessCommand(cmd);
    }

    private void ProcessCommand(GameCommand cmd)
    {
        var map = GetNodeOrNull<MapGenerator>("/root/Main");
        if (map == null) return;

        switch (cmd.Type)
        {
            case CommandType.CreateArmy:
                map.ServerCreateArmy(cmd.Arg1, cmd.Arg2, cmd.PlayerId, (UnitType)cmd.Arg3);
                break;
            case CommandType.MoveArmy:
                map.ServerMoveArmy(cmd.Arg1, cmd.Arg2);
                break;
            case CommandType.BuildMine:
                map.ServerBuildMine(cmd.PlayerId);
                break;
            case CommandType.BuildFort:
                map.ServerBuildFort(cmd.PlayerId);
                break;
            case CommandType.MergeArmies:
                map.ServerMergeArmies(cmd.Arg1, cmd.Arg2, cmd.PlayerId);
                break;
            case CommandType.BuildUniversity:
                map.ServerBuildUniversity(cmd.PlayerId);
                break;
            case CommandType.Research:
                map.ServerResearchTech(cmd.PlayerId, cmd.Arg1);
                break;
            case CommandType.SetRelation:
                map.ServerSetRelation(cmd.PlayerId, cmd.Arg1, (RelationState)cmd.Arg2);
                break;
            case CommandType.ProposePact:
                map.ServerProposePact(cmd.PlayerId, cmd.Arg1);
                break;
            case CommandType.AnswerPact:
                map.ServerAnswerPact(cmd.PlayerId, cmd.Arg1, cmd.Arg2 != 0);
                break;
        }
    }

    // --- LAN Discovery ---

    private void StartBroadcast(int port, int maxPlayers)
    {
        try
        {
            _broadcastSender = new UdpClient();
            _broadcastSender.EnableBroadcast = true;
            _isBroadcasting = true;
            _broadcastTimer = 0f;
            GD.Print($"[Net] LAN-рассылка запущена на порту {BroadcastPort}");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[Net] Ошибка запуска рассылки: {e.Message}");
        }
    }

    private void StopBroadcast()
    {
        _isBroadcasting = false;
        try { _broadcastSender?.Close(); } catch { }
        _broadcastSender = null;
    }

    private void SendBroadcast()
    {
        if (_broadcastSender == null) return;
        try
        {
            string gameName = "Nation Path";
            int connected = _connectedPeers.Count;
            string data = $"NATIONPATH|{_serverPort}|{gameName}|{connected}|{MaxPlayersCount}";
            byte[] bytes = Encoding.UTF8.GetBytes(data);
            _broadcastSender.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, BroadcastPort));
        }
        catch (Exception e)
        {
            GD.PrintErr($"[Net] Ошибка рассылки: {e.Message}");
        }
    }

    public void StartListeningForServers()
    {
        if (_isListening) return;
        try
        {
            _broadcastListener = new UdpClient(BroadcastPort);
            _isListening = true;
            _broadcastListenerThread = new Thread(ListenLoop)
            {
                IsBackground = true
            };
            _broadcastListenerThread.Start();
            GD.Print("[Net] Прослушивание LAN-серверов запущено");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[Net] Ошибка прослушивания: {e.Message}");
            StopListening();
        }
    }

    public void StopListening()
    {
        _isListening = false;
        try { _broadcastListener?.Close(); } catch { }
        _broadcastListener = null;
        _broadcastListenerThread = null;
        DiscoveredServers.Clear();
    }

    private void ListenLoop()
    {
        while (_isListening && _broadcastListener != null)
        {
            try
            {
                IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = _broadcastListener.Receive(ref remoteEP);
                string message = Encoding.UTF8.GetString(data);

                if (message.StartsWith("NATIONPATH|"))
                {
                    string[] parts = message.Split('|');
                    if (parts.Length >= 5)
                    {
                        int serverPort = int.Parse(parts[1]);
                        string gameName = parts[2];
                        int current = int.Parse(parts[3]);
                        int max = int.Parse(parts[4]);
                        string host = remoteEP.Address.ToString();

                        bool isSelf = IsServer && (host == GetLocalIPAddress() || host == "127.0.0.1");
                        if (!isSelf)
                        {
                            var server = new DiscoveredServer
                            {
                                Host = host,
                                Port = serverPort,
                                GameName = gameName,
                                CurrentPlayers = current,
                                MaxPlayers = max,
                            };
                            lock (_pendingLock)
                            {
                                _pendingServers.Add(server);
                            }
                            GD.Print($"[Net] LAN: найден сервер {host}:{serverPort}");
                        }
                    }
                }
            }
            catch (SocketException) when (!_isListening) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception e)
            {
                GD.PrintErr($"[Net] Ошибка приёма broadcast: {e.Message}");
            }
        }
    }

    private void AddDiscoveredServerInternal(DiscoveredServer server)
    {
        for (int i = 0; i < DiscoveredServers.Count; i++)
        {
            if (DiscoveredServers[i].Host == server.Host && DiscoveredServers[i].Port == server.Port)
            {
                DiscoveredServers[i] = server;
                ServerListUpdated?.Invoke();
                return;
            }
        }
        DiscoveredServers.Add(server);
        ServerListUpdated?.Invoke();
    }

    private string GetLocalIPAddress()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect("8.8.8.8", 80);
            return ((IPEndPoint)socket.LocalEndPoint).Address.ToString();
        }
        catch
        {
            return "127.0.0.1";
        }
    }

    // --- Callbacks ---

    private void OnPeerConnected(long id)
    {
        int peerId = (int)id;
        GD.Print($"[Net] Godot:.peer подключился id={peerId}");

        if (IsClient && peerId == 1)
        {
            _waitingForConnection = false;
            GD.Print($"[Net] Подключено к серверу! PeerId={peerId}");
            return;
        }

        if (_connectedPeers.Contains(peerId))
        {
            GD.Print($"[Net] Игрок {peerId} уже в списке, пропуск");
            return;
        }

        _connectedPeers.Add(peerId);
        PlayerConnected?.Invoke(peerId);
        GD.Print($"[Net] Игрок {peerId} добавлен в список ({_connectedPeers.Count} всего)");

        if (IsServer && _connectedPeers.Count >= MaxPlayersCount - 1)
        {
            GD.Print("[Net] Все игроки подключились!");
        }
    }

    private void OnPeerDisconnected(long id)
    {
        int peerId = (int)id;
        _connectedPeers.Remove(peerId);
        _readyPeers.Remove(peerId);
        _peerNationMap.Remove(peerId);
        PlayerDisconnected?.Invoke(peerId);
        GD.Print($"[Net] Игрок {peerId} отключился");
    }

    private void OnConnectedToServer()
    {
        LocalPeerId = Multiplayer.GetUniqueId();
        _waitingForConnection = false;
        GD.Print($"[Net] Подключено к серверу! LocalPeerId={LocalPeerId}");
        RpcId(1, nameof(RpcClientReady), LocalPeerId);
    }

    private void OnConnectionFailed()
    {
        GD.PrintErr("[Net] Godot multiplayer.ConnectionFailed сработал!");
        _waitingForConnection = false;
        ConnectionFailed?.Invoke(-1, "Не удалось подключиться к серверу");
        CallDeferred(nameof(DeferredStopNetwork));
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    public void RpcClientReady(int peerId)
    {
        GD.Print($"[Net] Клиент {peerId} сообщил о готовности");
    }
}
