using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

public sealed class MultiplayerPrototype : MonoBehaviour
{
    private const string DefaultServerUrl = "https://cavegame-production.up.railway.app";
    private const float StateSendInterval = 1f / 30f;
    private const float ForcedStateSendInterval = 0.5f;
    private const float MinPositionDeltaSqr = 0.0004f;
    private const float MinRotationDelta = 1.5f;
    private const float MammothStateSendInterval = 1f / 20f;
    private const float MammothForcedStateSendInterval = 0.2f;
    private const float MammothRemoteLerpSpeed = 10f;
    private const float LobbyPingInterval = 2f;
    private const float GamePingInterval = 1f;
    private const float LobbyHeartbeatTimeout = 8f;
    private const float GameHeartbeatTimeout = 5f;
    private const float ReconnectBaseDelay = 0.35f;
    private const float ReconnectMaxDelay = 6f;
    private const float HudRefreshInterval = 0.25f;
    private const float SpawnHeightOffset = 0.75f;
    private const float SpawnNavMeshProbeHeight = 40f;
    private const float SpawnNavMeshSampleRadius = 80f;
    private const float SpawnRaycastHeight = 200f;
    private const float SpawnRaycastDistance = 600f;
    private const string LobbyMusicResourcePath = "music/FurReal-SpearMeDaddy";
    private const float LobbyMusicVolume = 0.38f;
    private const float InGameMusicVolume = 0.07f;
    private const float MusicFadeSpeed = 0.8f;
    private const float MusicLoopRestartPadding = 0.08f;
    private const string BuiltInFontName = "LegacyRuntime.ttf";
    private const string PlayerPrefabAssetPath = "Assets/Prefab_objects/Player_NEW.prefab";
    private const string PlayerPrefabResourcePath = "Player_NEW";
    private const int DefaultMaxPlayers = 4;
    private static Font cachedUiFont;
    private static Shader cachedObjectShader;
    public static MultiplayerPrototype Instance { get; private set; }

    private static readonly Vector2 MenuPanelSize = new Vector2(680f, 760f);
    private static readonly Color Ink = new Color(0.12f, 0.08f, 0.04f, 0.98f);
    private static readonly Color Panel = new Color(0.88f, 0.82f, 0.7f, 0.96f);
    private static readonly Color PanelSoft = new Color(0.95f, 0.91f, 0.8f, 0.96f);
    private static readonly Color Accent = new Color(0.72f, 0.34f, 0.12f, 0.98f);
    private static readonly Color AccentCool = new Color(0.23f, 0.4f, 0.34f, 0.98f);
    private static readonly Color Success = new Color(0.34f, 0.48f, 0.2f, 0.98f);
    private static readonly Color MutedText = new Color(0.34f, 0.27f, 0.19f, 0.92f);
    private static readonly Vector2 MenuPanelAnchoredPosition = new Vector2(0f, -42f);

    private CaveGameApiClient api;
    private CaveGameSocketClient lobbySocket;
    private CaveGameSocketClient gameSocket;
    private bool lobbyReconnectQueued;
    private bool suppressLobbyReconnect;
    private bool gameReconnectQueued;
    private int currentGameLobbyId = -1;

    private string authToken;
    private UserDto currentUser;
    private LobbyDto currentLobby;
    private LobbyMemberDto localMember;
    private bool gameStarted;
    private int stateSeq;
    private float nextStateSendTime;
    private float lastStateSendTime;
    private bool hasSentInitialState;
    private Vector3 lastSentPosition;
    private Vector3 lastSentEulerAngles;
    private Vector3 lastSentMoveTarget;
    private Vector3 lastSentAimTarget;
    private Vector3 lastSentGaitForward;
    private Vector3 lastSentLeftArmTarget;
    private Vector3 lastSentRightArmTarget;
    private int lastSentActionSeq;
    private string lastSentHeldObjectType = "none";
    private string lastSentHeldItemType = "";
    private float nextGamePingTime;
    private float nextLobbyPingTime;
    private float nextHudRefreshTime;
    private float lastGameRttMs = -1f;
    private float lastRemoteStateReceiveTime = -1f;
    private float remoteStateRateWindowStart;
    private int remoteStatesInWindow;
    private int remoteStatesPerSecond;

    private Canvas canvas;
    private GameObject menuBackdrop;
    private GameObject menuWordmarkRoot;
    private GameObject loginPanel;
    private GameObject findPanel;
    private GameObject lobbyPanel;
    private GameObject gameHudPanel;
    private GameObject loadingPanel;
    private AudioSource lobbyMusicSource;
    private float targetMusicVolume;

    private InputField serverInput;
    private InputField usernameInput;
    private InputField passwordInput;
    private InputField joinCodeInput;
    private Text loadingTitleText;
    private Text loadingStatusText;
    private Text loadingProgressText;
    private Image loadingProgressFill;
    private Text loginStatusText;
    private Text findStatusText;
    private Text lobbyTitleText;
    private Text lobbyCodeText;
    private Text lobbyHostText;
    private Text lobbyPlayersText;
    private Text lobbyStatusText;
    private Text gameStatusText;
    private Button readyButton;
    private Button startButton;
    private Button copyCodeButton;
    private Button leaveLobbyButton;
    private Image readyButtonImage;
    private Image startButtonImage;
    private readonly List<LobbySlotView> lobbySlotViews = new List<LobbySlotView>();

    private GameObject worldRoot;
    private WorldChunkRenderer worldChunkRenderer;
    private LocalCubeController localCube;
    private readonly Dictionary<string, RemoteCubeController> remoteCubes = new Dictionary<string, RemoteCubeController>();
    private readonly Dictionary<string, int> playerSlotsById = new Dictionary<string, int>();
    private Vector3 runtimeSpawnAnchor = Vector3.zero;
    private MammothStateDto pendingMammothState;
    private MammothHealthDto pendingMammothHealth;
    private EnemyHealth cachedMammothEnemy;
    private bool mammothRuntimeConfigured;
    private float ignoreIncomingMammothDeathUntil;
    private float nextMammothStateSendTime;
    private float lastMammothStateSendTime;
    private bool hasSentInitialMammothState;
    private Vector3 lastSentMammothPosition;
    private Vector3 lastSentMammothEulerAngles;
    private Vector3 targetRemoteMammothPosition;
    private Quaternion targetRemoteMammothRotation = Quaternion.identity;
    private bool hasRemoteMammothPose;
    private bool worldBootstrapReady;
    private bool worldBootstrapFailed;
    private float lastLoadingUiRefreshTime;

    [Header("Networking Debug")]
    [SerializeField] private bool verboseNetworkingLogs = true;
    [SerializeField] private bool logSocketPayloads = false;
    [SerializeField] private bool logRemoteStateDecisions = true;
    [SerializeField] private bool logHeartbeatMessages = false;

    private string debugClientTag;
    private int lobbyMessagesReceived;
    private int gameMessagesReceived;
    private int remoteStatesApplied;
    private int remoteStatesDroppedAsLocal;
    private int remoteStatesDroppedInvalid;
    private int remoteStatesSpawned;
    private int gameSocketReconnectAttempts;
    private int lobbySocketReconnectAttempts;
    private string lastLobbySocketCloseCode = "none";
    private string lastGameSocketCloseCode = "none";
    private bool isShuttingDown;
    private float lastGamePingSendTime = -1f;
    private float lastGamePongReceiveTime = -1f;
    private float lastLobbyPingSendTime = -1f;
    private float lastLobbyPongReceiveTime = -1f;
    private float lastGameHeartbeatReceiveTime = -1f;
    private float lastLobbyHeartbeatReceiveTime = -1f;
    private float lastLobbySocketCloseTime = -1f;
    private float lastGameSocketCloseTime = -1f;
    private float lastGameSocketCloseGapMs = -1f;
    private string lastLobbyEnvelopeType = "none";
    private string lastGameEnvelopeType = "none";
    private float lastLobbyEnvelopeTime = -1f;
    private float lastGameEnvelopeTime = -1f;
    private bool lobbyCloseExpected;
    private string lobbyCloseExpectedReason = "none";
    private bool gameCloseExpected;
    private string gameCloseExpectedReason = "none";
    private int lobbySocketGeneration;
    private int gameSocketGeneration;
    private bool lobbyHeartbeatCloseRequested;
    private bool gameHeartbeatCloseRequested;
    private float nextLobbyReconnectAllowedAt;
    private float nextGameReconnectAllowedAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateRuntimeBootstrap()
    {
        if (FindAnyObjectByType<MultiplayerPrototype>() != null)
        {
            return;
        }

        GameObject bootstrap = new GameObject("Multiplayer Prototype");
        bootstrap.AddComponent<MultiplayerPrototype>();
        DontDestroyOnLoad(bootstrap);
    }

    private void Awake()
    {
        Instance = this;
        Application.runInBackground = true;
        debugClientTag = System.Guid.NewGuid().ToString("N").Substring(0, 6);
        api = new CaveGameApiClient(DefaultServerUrl, () => authToken);
        BuildUi();
        ConfigureLobbyMusic();
        SetMenuChromeVisible(true);
        SetMusicTargetVolume(LobbyMusicVolume);
        NetLog("Bootstrap complete.");
        ShowLoading("Preparing world bootstrap...");
    }

    private void Start()
    {
        StartCoroutine(WaitForWorldBootstrapThenEnableAuth());
    }

    private void Update()
    {
        lobbySocket?.Pump();
        gameSocket?.Pump();

        if (!worldBootstrapReady && Time.unscaledTime - lastLoadingUiRefreshTime >= 0.1f)
        {
            lastLoadingUiRefreshTime = Time.unscaledTime;
            RefreshLoadingUi();
        }

        UpdateMusicVolume();

        TryConfigureMammothRuntime();
        UpdateRemoteMammothPose();

        if (pendingMammothState != null)
        {
            TryApplyMammothState(pendingMammothState);
        }

        if (pendingMammothHealth != null)
        {
            TryApplyMammothHealth(pendingMammothHealth);
        }

        if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
        {
            DumpMultiplayerDebugSnapshot();
        }

        if (!gameStarted && lobbySocket != null && lobbySocket.IsOpen && Time.unscaledTime >= nextLobbyPingTime)
        {
            nextLobbyPingTime = Time.unscaledTime + LobbyPingInterval;
            lastLobbyPingSendTime = Time.unscaledTime;
            lobbySocket.SendJson(JsonUtility.ToJson(new PingDto { clientTime = Time.realtimeSinceStartupAsDouble }));
            lobbySocket.SendJson(JsonUtility.ToJson(new HeartbeatDto { clientTime = Time.realtimeSinceStartupAsDouble }));
        }

        if (!gameStarted
            && lobbySocket != null
            && lobbySocket.IsOpen
            && !lobbyHeartbeatCloseRequested
            && IsHeartbeatTimedOut(lastLobbyPingSendTime, lastLobbyPongReceiveTime, lastLobbyHeartbeatReceiveTime, LobbyHeartbeatTimeout))
        {
            lobbyHeartbeatCloseRequested = true;
            NetLog($"Lobby heartbeat timeout detected. {DescribeHeartbeat(lastLobbyPingSendTime, lastLobbyPongReceiveTime, lastLobbyHeartbeatReceiveTime)}", true);
            QueueLobbyReconnectIfNeeded();
            MarkExpectedLobbyClose("heartbeat_timeout");
            lobbySocket.Close();
        }

        if (!gameStarted || gameSocket == null || !gameSocket.IsOpen)
        {
            return;
        }

        if (localCube != null && Time.unscaledTime >= nextStateSendTime && ShouldSendStateNow(Time.unscaledTime))
        {
            string outboundPlayerId = GetLocalPlayerId();
            if (string.IsNullOrWhiteSpace(outboundPlayerId))
            {
                return;
            }

            nextStateSendTime = Time.unscaledTime + StateSendInterval;
            PlayerStateDto state = PlayerStateDto.FromProceduralPlayer(
                outboundPlayerId,
                localMember != null ? localMember.userId : (currentUser != null ? currentUser.id : 0),
                ++stateSeq,
                localCube.Rig
            );
            gameSocket.SendJson(JsonUtility.ToJson(state));
            MarkStateSent(Time.unscaledTime);
        }

        if (Time.unscaledTime >= nextMammothStateSendTime && ShouldSendMammothStateNow(Time.unscaledTime))
        {
            nextMammothStateSendTime = Time.unscaledTime + MammothStateSendInterval;
            SendMammothStateUpdate();
        }

        if (Time.unscaledTime >= nextGamePingTime)
        {
            nextGamePingTime = Time.unscaledTime + GamePingInterval;
            lastGamePingSendTime = Time.unscaledTime;
            gameSocket.SendJson(JsonUtility.ToJson(new PingDto { clientTime = Time.realtimeSinceStartupAsDouble }));
            gameSocket.SendJson(JsonUtility.ToJson(new HeartbeatDto { clientTime = Time.realtimeSinceStartupAsDouble }));
        }

        if (!gameHeartbeatCloseRequested
            && IsHeartbeatTimedOut(lastGamePingSendTime, lastGamePongReceiveTime, lastGameHeartbeatReceiveTime, GameHeartbeatTimeout))
        {
            gameHeartbeatCloseRequested = true;
            NetLog($"Game heartbeat timeout detected. {DescribeHeartbeat(lastGamePingSendTime, lastGamePongReceiveTime, lastGameHeartbeatReceiveTime)}", true);
            QueueGameReconnectIfNeeded();
            MarkExpectedGameClose("heartbeat_timeout");
            gameSocket.Close();
        }

        if (Time.unscaledTime >= nextHudRefreshTime)
        {
            nextHudRefreshTime = Time.unscaledTime + HudRefreshInterval;
            RefreshGameHud();
        }
    }

    private void OnDestroy()
    {
        isShuttingDown = true;
        suppressLobbyReconnect = true;
        DetachWorldChunkRendererPlayers();
        lobbySocket?.Close();
        gameSocket?.Close();
        if (ReferenceEquals(Instance, this))
        {
            Instance = null;
        }
    }

    private void Login()
    {
        if (!EnsureWorldBootstrapReadyForUiAction())
        {
            return;
        }

        api = new CaveGameApiClient(serverInput.text, () => authToken);
        SetText(loginStatusText, "Authenticating...");

        StartCoroutine(api.CreateGuest(result =>
        {
            if (!result.IsSuccess)
            {
                SetText(loginStatusText, result.Error);
                return;
            }

            authToken = result.Value.token;
            currentUser = result.Value.user;
            string preferredName = string.IsNullOrWhiteSpace(usernameInput.text) ? currentUser.username : usernameInput.text.Trim();
            ShowFind($"Authenticated as {preferredName} ({currentUser.username}). Backend currently issued a guest token.");
        }));
    }

    private void CreateLobby()
    {
        if (!EnsureWorldBootstrapReadyForUiAction())
        {
            return;
        }

        SetText(findStatusText, "Creating lobby...");
        StartCoroutine(api.CreateLobby(4, result =>
        {
            if (!result.IsSuccess)
            {
                SetText(findStatusText, result.Error);
                return;
            }

            currentLobby = result.Value;
            localMember = FindMember(currentLobby, currentUser.id);
            CacheLobbyPlayerSlots();
            OpenLobbySocket();
            ShowLobby("Lobby created.");
        }));
    }

    private void JoinLobby()
    {
        if (!EnsureWorldBootstrapReadyForUiAction())
        {
            return;
        }

        string code = joinCodeInput.text;
        if (string.IsNullOrWhiteSpace(code))
        {
            SetText(findStatusText, "Enter a lobby code first.");
            return;
        }

        SetText(findStatusText, "Joining lobby...");
        StartCoroutine(api.JoinLobby(code, result =>
        {
            if (!result.IsSuccess)
            {
                SetText(findStatusText, result.Error);
                return;
            }

            currentLobby = result.Value.lobby;
            localMember = result.Value.member;
            CacheLobbyPlayerSlots();
            OpenLobbySocket();
            ShowLobby("Joined lobby.");
        }));
    }

    private void ToggleReady()
    {
        bool nextReady = localMember == null || !localMember.isReady;
        SetText(lobbyStatusText, nextReady ? "Marking ready..." : "Clearing ready...");

        StartCoroutine(api.SetReady(currentLobby.id, nextReady, result =>
        {
            if (!result.IsSuccess)
            {
                SetText(lobbyStatusText, result.Error);
                return;
            }

            ApplyLobbyEvent(result.Value);
            SetText(lobbyStatusText, nextReady ? "Ready." : "Not ready.");
        }));
    }

    private void StartLobby()
    {
        SetText(lobbyStatusText, "Starting lobby...");
        StartCoroutine(api.StartLobby(currentLobby.id, result =>
        {
            if (!result.IsSuccess)
            {
                SetText(lobbyStatusText, result.Error);
                return;
            }

            EnterGame(result.Value);
        }));
    }

    private void CopyLobbyCode()
    {
        if (currentLobby == null || string.IsNullOrWhiteSpace(currentLobby.code))
        {
            SetText(lobbyStatusText, "No lobby code to copy yet.");
            return;
        }

        GUIUtility.systemCopyBuffer = currentLobby.code;
        SetText(lobbyStatusText, $"Copied lobby code {currentLobby.code}.");
    }

    private void LeaveLobby()
    {
        suppressLobbyReconnect = true;
        MarkExpectedLobbyClose("leave_lobby");
        lobbySocket?.Close();
        lobbySocket = null;
        currentLobby = null;
        localMember = null;
        playerSlotsById.Clear();
        ShowFind("Left lobby. Create a new room or jump into another code.");
    }

    private void OpenLobbySocket()
    {
        suppressLobbyReconnect = false;
        lobbyReconnectQueued = false;
        if (lobbySocket != null && lobbySocket.IsOpen)
        {
            NetLog("Lobby socket open request skipped because socket is already open.");
            return;
        }

        int generation = ++lobbySocketGeneration;
        if (lobbySocket != null)
        {
            MarkExpectedLobbyClose("replace_lobby_socket");
            lobbySocket.Close();
        }

        CaveGameSocketClient socketClient = new CaveGameSocketClient();
        lobbySocket = socketClient;
        string url = api.BuildWebSocketUrl($"/ws/lobby/{currentLobby.id}/");
        NetLog($"Opening lobby socket: {url}");
        socketClient.Opened += () =>
        {
            if (generation != lobbySocketGeneration || isShuttingDown || this == null)
            {
                return;
            }

            SetText(lobbyStatusText, "Connected to lobby socket.");
            NetLog("Lobby socket opened.");
            nextLobbyPingTime = Time.unscaledTime;
            lastLobbyPingSendTime = Time.unscaledTime;
            lastLobbyPongReceiveTime = Time.unscaledTime;
            lastLobbyHeartbeatReceiveTime = Time.unscaledTime;
            lobbyHeartbeatCloseRequested = false;
            lobbySocketReconnectAttempts = 0;
            nextLobbyReconnectAllowedAt = 0f;
        };
        socketClient.ErrorReceived += error =>
        {
            if (generation != lobbySocketGeneration)
            {
                return;
            }

            SetText(lobbyStatusText, "Lobby socket error: " + error);
            NetLog("Lobby socket error: " + error, true);
            LogSocketTrace("Lobby socket trace on error", socketClient, true, false, "socket_error");
        };
        socketClient.Closed += closeCode =>
        {
            if (generation != lobbySocketGeneration || isShuttingDown || this == null)
            {
                return;
            }

            bool isCurrentSocket = ReferenceEquals(lobbySocket, socketClient);
            bool intentionalClose = !isCurrentSocket;
            string closeReason = intentionalClose ? "stale_socket_replaced" : "none";
            if (TryConsumeExpectedLobbyClose(out string expectedReason))
            {
                intentionalClose = true;
                closeReason = expectedReason;
            }

            lastLobbySocketCloseCode = closeCode;
            lastLobbySocketCloseTime = Time.unscaledTime;
            SetText(lobbyStatusText, "Lobby socket closed (" + closeCode + ").");
            bool warning = !intentionalClose && IsSocketCloseWarning(closeCode);
            NetLog($"Lobby socket closed: {closeCode} (intentional={intentionalClose}, reason={closeReason})", warning);
            LogSocketTrace("Lobby socket trace on close", socketClient, warning, intentionalClose, closeReason);
            if (!intentionalClose && isCurrentSocket)
            {
                QueueLobbyReconnectIfNeeded();
            }
        };
        socketClient.MessageReceived += HandleLobbySocketMessage;
        socketClient.Connect(url);
    }

    private void QueueLobbyReconnectIfNeeded()
    {
        if (suppressLobbyReconnect || lobbyReconnectQueued || currentLobby == null || gameStarted)
        {
            return;
        }
        if (lobbySocket != null && lobbySocket.IsOpen)
        {
            return;
        }
        if (Time.unscaledTime < nextLobbyReconnectAllowedAt)
        {
            return;
        }

        lobbyReconnectQueued = true;
        StartCoroutine(ReconnectLobbySocketAfterDelay());
    }

    private IEnumerator ReconnectLobbySocketAfterDelay()
    {
        float reconnectDelay = ComputeReconnectDelay(lobbySocketReconnectAttempts);
        yield return new WaitForSecondsRealtime(reconnectDelay);
        lobbyReconnectQueued = false;

        if (suppressLobbyReconnect || currentLobby == null || gameStarted)
        {
            yield break;
        }
        if (lobbySocket != null && lobbySocket.IsOpen)
        {
            yield break;
        }

        lobbySocketReconnectAttempts++;
        nextLobbyReconnectAllowedAt = Time.unscaledTime + reconnectDelay;
        SetText(lobbyStatusText, "Reconnecting lobby socket...");
        NetLog($"Lobby reconnect attempt #{lobbySocketReconnectAttempts} after {reconnectDelay:0.00}s.");
        OpenLobbySocket();
    }

    private void HandleLobbySocketMessage(string json)
    {
        if (isShuttingDown || this == null)
        {
            return;
        }

        lobbyMessagesReceived++;
        SocketTypeEnvelopeDto envelope = JsonUtility.FromJson<SocketTypeEnvelopeDto>(json);
        string envelopeType = envelope != null && !string.IsNullOrWhiteSpace(envelope.type) ? envelope.type : "unknown";
        if (logHeartbeatMessages || !IsHeartbeatEnvelope(envelopeType))
        {
            NetLog($"Lobby message #{lobbyMessagesReceived}: {envelopeType}");
        }
        lastLobbyEnvelopeType = envelopeType;
        lastLobbyEnvelopeTime = Time.unscaledTime;
        if (string.Equals(lastLobbyEnvelopeType, "pong", StringComparison.OrdinalIgnoreCase))
        {
            lastLobbyPongReceiveTime = Time.unscaledTime;
        }
        if (string.Equals(lastLobbyEnvelopeType, "heartbeat", StringComparison.OrdinalIgnoreCase))
        {
            lastLobbyHeartbeatReceiveTime = Time.unscaledTime;
        }
        if (logSocketPayloads)
        {
            NetLog($"Lobby payload: {json}");
        }
        switch (envelopeType)
        {
            case "lobby_snapshot":
                ApplyLobbySnapshot(JsonUtility.FromJson<LobbySnapshotDto>(json));
                break;
            case "player_ready_changed":
                ApplyLobbyEvent(JsonUtility.FromJson<LobbyEventDto>(json));
                break;
            case "player_joined":
            case "player_left":
                StartCoroutine(RefreshLobby("Lobby membership changed."));
                break;
            case "game_started":
                EnterGame(JsonUtility.FromJson<GameStartedDto>(json));
                break;
        }
    }

    private IEnumerator RefreshLobby(string status)
    {
        yield return api.GetLobby(currentLobby.id, result =>
        {
            if (result.IsSuccess)
            {
                currentLobby = result.Value;
                localMember = FindMember(currentLobby, currentUser.id);
                CacheLobbyPlayerSlots();
                RefreshLobbyUi(status);
            }
            else
            {
                SetText(lobbyStatusText, result.Error);
            }
        });
    }

    private void ApplyLobbySnapshot(LobbySnapshotDto snapshot)
    {
        currentLobby = new LobbyDto
        {
            id = snapshot.lobbyId,
            code = snapshot.code,
            hostId = snapshot.hostId,
            isStarted = snapshot.isStarted,
            members = snapshot.players,
        };
        localMember = FindMember(currentLobby, currentUser.id);
        CacheLobbyPlayerSlots();
        RefreshLobbyUi("Lobby snapshot received.");
    }

    private void ApplyLobbyEvent(LobbyEventDto lobbyEvent)
    {
        if (currentLobby?.members == null)
        {
            return;
        }

        foreach (LobbyMemberDto member in currentLobby.members)
        {
            if (member.userId == lobbyEvent.userId)
            {
                member.isReady = lobbyEvent.isReady;
                if (localMember != null && localMember.userId == member.userId)
                {
                    localMember = member;
                }
                break;
            }
        }

        RefreshLobbyUi(null);
    }

    private void EnterGame(GameStartedDto start)
    {
        if (gameStarted)
        {
            return;
        }

        gameStarted = true;
        ResetStateSendTracking();
        EnsureLocalMemberForGameStart(start);
        CacheGameStartedPlayerSlots(start);
        currentGameLobbyId = start != null ? start.lobbyId : -1;
        gameReconnectQueued = false;
        suppressLobbyReconnect = true;
        MarkExpectedLobbyClose("transition_to_game");
        lobbySocket?.Close();
        HideAllPanels();
        SetMenuChromeVisible(false);
        SetMusicTargetVolume(InGameMusicVolume);
        gameHudPanel.SetActive(true);
        SetText(gameStatusText, $"Game started in lobby {start.lobbyId}. WASD to move, Space to jump.");
        NetLog("Entering game. " + DescribeGameStarted(start));

        BuildGameWorld();
        TryApplyMammothState(pendingMammothState);
        TryApplyMammothHealth(pendingMammothHealth);
        PreSpawnRemotePlayers(start);
        OpenGameSocket(currentGameLobbyId);
    }

    private void EnsureLocalMemberForGameStart(GameStartedDto start)
    {
        if (currentUser == null || start?.players == null)
        {
            return;
        }

        foreach (GameStartedPlayerDto player in start.players)
        {
            if (player.userId != currentUser.id)
            {
                continue;
            }

            if (localMember == null)
            {
                localMember = new LobbyMemberDto
                {
                    userId = currentUser.id,
                    username = currentUser.username,
                    playerId = player.playerId,
                    slot = player.slot,
                    isReady = true
                };
            }
            else
            {
                localMember.playerId = player.playerId;
                localMember.slot = player.slot;
            }

            return;
        }
    }

    private void BuildGameWorld()
    {
        DetachWorldChunkRendererPlayers();

        if (worldRoot != null)
        {
            Destroy(worldRoot);
        }

        remoteCubes.Clear();
        worldRoot = new GameObject("Multiplayer Runtime World");
        worldChunkRenderer = FindAnyObjectByType<WorldChunkRenderer>();
        runtimeSpawnAnchor = ResolveRuntimeSpawnAnchor();

        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Prototype Floor";
        floor.transform.SetParent(worldRoot.transform);
        floor.transform.position = new Vector3(0f, -0.55f, 0f);
        floor.transform.localScale = new Vector3(32f, 1f, 32f);
        SetRendererColor(floor, new Color(0.22f, 0.5f, 0.24f));

        if (FindAnyObjectByType<Light>() == null)
        {
            GameObject lightObject = new GameObject("Directional Light");
            lightObject.transform.SetParent(worldRoot.transform);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        Camera camera = Camera.main;
        if (camera == null)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
        }

        CameraFollow sceneCameraFollow = camera.GetComponent<CameraFollow>();
        if (sceneCameraFollow != null && sceneCameraFollow.enabled)
        {
            sceneCameraFollow.enabled = false;
            NetLog("Disabled scene CameraFollow for multiplayer runtime camera.");
        }

        camera.clearFlags = CameraClearFlags.Skybox;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 1000f;

        Vector3 spawn = ResolveSafeSpawnPosition(ResolveLocalSpawnSlot(), currentUser != null ? currentUser.id : 0, "local");
        string localKey = BuildPlayerKey(localMember != null ? localMember.playerId : null, currentUser != null ? currentUser.id : 0);
        GameObject local = CreatePlayerCube("Local Player Cube", spawn, GetPlayerColor(localKey), true);
        localCube = local.GetComponent<LocalCubeController>();
        localCube.Setup(camera.transform);
        RegisterWorldChunkRendererPlayer(localCube.TrackedTransform, true);
    }

    private void PreSpawnRemotePlayers(GameStartedDto start)
    {
        if (start?.players == null)
        {
            return;
        }

        foreach (GameStartedPlayerDto player in start.players)
        {
            if (player == null)
            {
                continue;
            }

            if (currentUser != null && player.userId == currentUser.id)
            {
                continue;
            }

            string key = BuildPlayerKey(player.playerId, player.userId);
            if (remoteCubes.ContainsKey(key))
            {
                NetLog($"Remote cube pre-spawn skipped (already exists): {key}");
                continue;
            }

            GameObject remoteObject = CreatePlayerCube(
                "Remote Player Cube " + key,
                ResolveSafeSpawnPosition(player.slot, player.userId, $"remote-pre:{key}"),
                GetPlayerColor(key),
                false
            );
            RemoteCubeController remote = remoteObject.GetComponent<RemoteCubeController>();
            remoteCubes[key] = remote;
            RegisterWorldChunkRendererPlayer(remote.TrackedTransform, false);
            remoteStatesSpawned++;
            NetLog($"Pre-spawned remote cube key={key}, slot={player.slot}, userId={player.userId}, pos={remoteObject.transform.position}");
        }
    }

    private void OpenGameSocket(int lobbyId)
    {
        if (gameSocket != null && gameSocket.IsOpen)
        {
            NetLog("Game socket open request skipped because socket is already open.");
            return;
        }

        int generation = ++gameSocketGeneration;
        if (gameSocket != null)
        {
            MarkExpectedGameClose("replace_game_socket");
            gameSocket.Close();
        }

        CaveGameSocketClient socketClient = new CaveGameSocketClient();
        gameSocket = socketClient;
        string url = api.BuildWebSocketUrl($"/ws/game/{lobbyId}/");
        NetLog($"Opening game socket: {url}");
        socketClient.Opened += () =>
        {
            if (generation != gameSocketGeneration || isShuttingDown || this == null)
            {
                return;
            }

            SetText(gameStatusText, "Connected to game socket. Sending transform state at up to 30 Hz.");
            NetLog("Game socket opened.");
            nextGamePingTime = Time.unscaledTime;
            lastGamePingSendTime = Time.unscaledTime;
            lastGamePongReceiveTime = Time.unscaledTime;
            lastGameHeartbeatReceiveTime = Time.unscaledTime;
            gameHeartbeatCloseRequested = false;
            gameSocketReconnectAttempts = 0;
            nextGameReconnectAllowedAt = 0f;
        };
        socketClient.ErrorReceived += error =>
        {
            if (generation != gameSocketGeneration || isShuttingDown || this == null)
            {
                return;
            }

            SetText(gameStatusText, "Game socket error: " + error);
            NetLog("Game socket error: " + error, true);
            LogSocketTrace("Game socket trace on error", socketClient, true, false, "socket_error");
        };
        socketClient.Closed += closeCode =>
        {
            if (generation != gameSocketGeneration || isShuttingDown || this == null)
            {
                return;
            }

            bool isCurrentSocket = ReferenceEquals(gameSocket, socketClient);
            bool intentionalClose = !isCurrentSocket;
            string closeReason = intentionalClose ? "stale_socket_replaced" : "none";
            if (TryConsumeExpectedGameClose(out string expectedReason))
            {
                intentionalClose = true;
                closeReason = expectedReason;
            }

            float now = Time.unscaledTime;
            lastGameSocketCloseGapMs = lastGameSocketCloseTime >= 0f ? (now - lastGameSocketCloseTime) * 1000f : -1f;
            lastGameSocketCloseTime = now;
            lastGameSocketCloseCode = closeCode;
            SetText(gameStatusText, "Game socket closed (" + closeCode + ").");
            bool warning = !intentionalClose && IsSocketCloseWarning(closeCode);
            NetLog($"Game socket closed: {closeCode} (intentional={intentionalClose}, reason={closeReason})", warning);
            NetLog($"Game heartbeat before close: {DescribeHeartbeat(lastGamePingSendTime, lastGamePongReceiveTime, lastGameHeartbeatReceiveTime)}", warning);
            if (lastGameSocketCloseGapMs >= 0f)
            {
                NetLog($"Game socket close cadence: {lastGameSocketCloseGapMs:0}ms since previous close.", warning);
            }
            LogSocketTrace("Game socket trace on close", socketClient, warning, intentionalClose, closeReason);
            if (!intentionalClose && isCurrentSocket)
            {
                QueueGameReconnectIfNeeded();
            }
        };
        socketClient.MessageReceived += HandleGameSocketMessage;
        socketClient.Connect(url);
    }

    private void QueueGameReconnectIfNeeded()
    {
        if (!gameStarted || gameReconnectQueued || currentGameLobbyId <= 0)
        {
            return;
        }
        if (gameSocket != null && gameSocket.IsOpen)
        {
            return;
        }
        if (Time.unscaledTime < nextGameReconnectAllowedAt)
        {
            return;
        }

        gameReconnectQueued = true;
        StartCoroutine(ReconnectGameSocketAfterDelay());
    }

    private IEnumerator ReconnectGameSocketAfterDelay()
    {
        float reconnectDelay = ComputeReconnectDelay(gameSocketReconnectAttempts);
        yield return new WaitForSecondsRealtime(reconnectDelay);
        gameReconnectQueued = false;

        if (!gameStarted || currentGameLobbyId <= 0)
        {
            yield break;
        }
        if (gameSocket != null && gameSocket.IsOpen)
        {
            yield break;
        }

        gameSocketReconnectAttempts++;
        nextGameReconnectAllowedAt = Time.unscaledTime + reconnectDelay;
        SetText(gameStatusText, "Reconnecting game socket...");
        NetLog($"Game reconnect attempt #{gameSocketReconnectAttempts} after {reconnectDelay:0.00}s.");
        OpenGameSocket(currentGameLobbyId);
    }

    private void HandleGameSocketMessage(string json)
    {
        if (isShuttingDown || this == null)
        {
            return;
        }

        gameMessagesReceived++;
        SocketTypeEnvelopeDto envelope = JsonUtility.FromJson<SocketTypeEnvelopeDto>(json);
        string envelopeType = envelope != null && !string.IsNullOrWhiteSpace(envelope.type) ? envelope.type : "unknown";
        if (logHeartbeatMessages || !IsHeartbeatEnvelope(envelopeType))
        {
            NetLog($"Game message #{gameMessagesReceived}: {envelopeType}");
        }
        lastGameEnvelopeType = envelopeType;
        lastGameEnvelopeTime = Time.unscaledTime;
        if (logSocketPayloads)
        {
            NetLog($"Game payload: {json}");
        }
        switch (envelopeType)
        {
            case "room_snapshot":
                RoomSnapshotDto snapshot = JsonUtility.FromJson<RoomSnapshotDto>(json);
                if (snapshot.players == null)
                {
                    snapshot.players = Array.Empty<PlayerStateDto>();
                }

                foreach (PlayerStateDto player in snapshot.players)
                {
                    ApplyRemoteState(player);
                }

                TryApplyMammothState(snapshot.mammothState);
                TryApplyMammothHealth(snapshot.mammothHealth);
                break;
            case "player_state":
                ApplyRemoteState(JsonUtility.FromJson<PlayerStateDto>(json));
                break;
            case "mammoth_state":
                TryApplyMammothState(JsonUtility.FromJson<MammothStateDto>(json));
                break;
            case "mammoth_health":
                TryApplyMammothHealth(JsonUtility.FromJson<MammothHealthDto>(json));
                break;
            case "pong":
                HandleGamePong(JsonUtility.FromJson<PongDto>(json));
                break;
            case "heartbeat":
                lastGameHeartbeatReceiveTime = Time.unscaledTime;
                break;
            case "player_left":
                LobbyEventDto left = JsonUtility.FromJson<LobbyEventDto>(json);
                RemoveRemotePlayer(BuildPlayerKey(left != null ? left.playerId : null, left != null ? left.userId : 0));
                break;
        }
    }

    private void ApplyRemoteState(PlayerStateDto state)
    {
        if (state == null)
        {
            remoteStatesDroppedInvalid++;
            return;
        }

        bool hasLocalUserId = (localMember != null && localMember.userId > 0) || (currentUser != null && currentUser.id > 0);
        int effectiveLocalUserId = localMember != null && localMember.userId > 0
            ? localMember.userId
            : (currentUser != null ? currentUser.id : 0);

        bool isLocalByUserId = hasLocalUserId && state.userId > 0 && state.userId == effectiveLocalUserId;
        bool isLocalByPlayerIdFallback = !hasLocalUserId
            && localMember != null
            && !string.IsNullOrWhiteSpace(localMember.playerId)
            && !string.IsNullOrWhiteSpace(state.playerId)
            && state.playerId == localMember.playerId;

        if (isLocalByUserId || isLocalByPlayerIdFallback)
        {
            remoteStatesDroppedAsLocal++;
            if (logRemoteStateDecisions)
            {
                NetLog($"Dropped remote state as local. state.playerId={state.playerId}, state.userId={state.userId}, local.playerId={localMember?.playerId}, local.userId={effectiveLocalUserId}");
            }
            return;
        }

        string remoteKey = BuildPlayerKey(state.playerId, state.userId);

        if (!remoteCubes.TryGetValue(remoteKey, out RemoteCubeController remote))
        {
            Vector3 initialPosition = MultiplayerJson.ArrayToVector(state.position);
            if (initialPosition == Vector3.zero)
            {
                initialPosition = ResolveSafeSpawnPosition(0, state.userId, $"remote-state:{remoteKey}");
            }
            GameObject remoteObject = CreatePlayerCube(
                "Remote Player Cube " + remoteKey,
                initialPosition,
                GetPlayerColor(remoteKey),
                false
            );
            remote = remoteObject.GetComponent<RemoteCubeController>();
            remoteCubes[remoteKey] = remote;
            RegisterWorldChunkRendererPlayer(remote.TrackedTransform, false);
            remoteStatesSpawned++;
            if (logRemoteStateDecisions)
            {
                NetLog($"Spawned remote from state key={remoteKey}, state.userId={state.userId}, state.pos={initialPosition}");
            }
        }

        remote.ApplyState(state);
        remoteStatesApplied++;
        RecordRemoteStateReceived();
    }

    private void HandleGamePong(PongDto pong)
    {
        if (pong == null || pong.clientTime <= 0)
        {
            return;
        }

        lastGamePongReceiveTime = Time.unscaledTime;
        lastGameRttMs = Mathf.Max(0f, (float)((Time.realtimeSinceStartupAsDouble - pong.clientTime) * 1000.0));
    }

    private static bool IsHeartbeatEnvelope(string envelopeType)
    {
        return string.Equals(envelopeType, "ping", StringComparison.OrdinalIgnoreCase)
            || string.Equals(envelopeType, "pong", StringComparison.OrdinalIgnoreCase)
            || string.Equals(envelopeType, "heartbeat", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHeartbeatTimedOut(float lastPingTime, float lastPongTime, float lastHeartbeatTime, float timeoutSeconds)
    {
        if (lastPingTime < 0f)
        {
            return false;
        }

        float lastSignalTime = Mathf.Max(lastPongTime, lastHeartbeatTime);
        if (lastPingTime > lastSignalTime)
        {
            // A new ping was just sent and we have not received an ack yet.
            // Start timeout from that ping instead of stale previous-session acks.
            return (Time.unscaledTime - lastPingTime) > timeoutSeconds;
        }

        if (lastSignalTime < 0f)
        {
            return (Time.unscaledTime - lastPingTime) > timeoutSeconds;
        }

        return (Time.unscaledTime - lastSignalTime) > timeoutSeconds;
    }

    private static float ComputeReconnectDelay(int previousAttempts)
    {
        float expDelay = ReconnectBaseDelay * Mathf.Pow(2f, Mathf.Clamp(previousAttempts, 0, 6));
        return Mathf.Min(ReconnectMaxDelay, expDelay);
    }

    private static string DescribeHeartbeat(float lastPingTime, float lastPongTime, float lastHeartbeatTime)
    {
        string pingAge = lastPingTime >= 0f ? $"{Mathf.Max(0f, (Time.unscaledTime - lastPingTime) * 1000f):0}ms ago" : "n/a";
        string pongAge = lastPongTime >= 0f ? $"{Mathf.Max(0f, (Time.unscaledTime - lastPongTime) * 1000f):0}ms ago" : "n/a";
        string heartbeatAge = lastHeartbeatTime >= 0f ? $"{Mathf.Max(0f, (Time.unscaledTime - lastHeartbeatTime) * 1000f):0}ms ago" : "n/a";
        return $"lastPing={pingAge}, lastPong={pongAge}, lastHeartbeat={heartbeatAge}";
    }

    private void RecordRemoteStateReceived()
    {
        float now = Time.unscaledTime;
        lastRemoteStateReceiveTime = now;

        if (now - remoteStateRateWindowStart >= 1f)
        {
            remoteStatesPerSecond = remoteStatesInWindow;
            remoteStatesInWindow = 0;
            remoteStateRateWindowStart = now;
        }

        remoteStatesInWindow++;
    }

    private void RemoveRemotePlayer(string playerId)
    {
        if (!remoteCubes.TryGetValue(playerId, out RemoteCubeController remote))
        {
            return;
        }

        worldChunkRenderer?.UnregisterTrackedPlayer(remote.TrackedTransform);
        Destroy(remote.gameObject);
        remoteCubes.Remove(playerId);
    }

    private void RegisterWorldChunkRendererPlayer(Transform playerTransform, bool isPrimaryPlayer)
    {
        if (playerTransform == null)
        {
            return;
        }

        if (worldChunkRenderer == null)
        {
            worldChunkRenderer = FindAnyObjectByType<WorldChunkRenderer>();
        }

        if (worldChunkRenderer == null)
        {
            return;
        }

        if (isPrimaryPlayer)
        {
            worldChunkRenderer.SetPrimaryPlayer(playerTransform);
            return;
        }

        worldChunkRenderer.RegisterTrackedPlayer(playerTransform);
    }

    private void DetachWorldChunkRendererPlayers()
    {
        if (worldChunkRenderer == null)
        {
            return;
        }

        if (localCube != null)
        {
            worldChunkRenderer.UnregisterTrackedPlayer(localCube.TrackedTransform);
        }

        foreach (RemoteCubeController remote in remoteCubes.Values)
        {
            if (remote != null)
            {
                worldChunkRenderer.UnregisterTrackedPlayer(remote.TrackedTransform);
            }
        }
    }

    private string GetLocalPlayerId()
    {
        if (localMember != null && !string.IsNullOrWhiteSpace(localMember.playerId))
        {
            return localMember.playerId;
        }

        if (currentUser != null && currentUser.id > 0)
        {
            return $"player_{currentUser.id}";
        }

        return null;
    }

    private int ResolveLocalSpawnSlot()
    {
        if (localMember != null && localMember.slot >= 0)
        {
            return localMember.slot;
        }

        if (currentUser != null)
        {
            return Mathf.Abs(currentUser.id) % DefaultMaxPlayers;
        }

        return 0;
    }

    private bool ShouldSendStateNow(float now)
    {
        if (!hasSentInitialState)
        {
            return true;
        }

        if (now - lastStateSendTime >= ForcedStateSendInterval)
        {
            return true;
        }

        Transform cubeTransform = localCube.TrackedTransform;
        if ((cubeTransform.position - lastSentPosition).sqrMagnitude >= MinPositionDeltaSqr)
        {
            return true;
        }

        if (Quaternion.Angle(Quaternion.Euler(lastSentEulerAngles), cubeTransform.rotation) >= MinRotationDelta)
        {
            return true;
        }

        ProceduralPlayerRig rig = localCube.Rig;
        if (rig != null)
        {
            if (rig.RunTarget != null && (rig.RunTarget.position - lastSentMoveTarget).sqrMagnitude >= MinPositionDeltaSqr)
            {
                return true;
            }

            if (rig.AimTarget != null && (rig.AimTarget.position - lastSentAimTarget).sqrMagnitude >= MinPositionDeltaSqr)
            {
                return true;
            }

            if ((rig.GaitForward - lastSentGaitForward).sqrMagnitude >= MinPositionDeltaSqr)
            {
                return true;
            }

            if ((rig.LeftArmTargetWorld - lastSentLeftArmTarget).sqrMagnitude >= MinPositionDeltaSqr)
            {
                return true;
            }

            if ((rig.RightArmTargetWorld - lastSentRightArmTarget).sqrMagnitude >= MinPositionDeltaSqr)
            {
                return true;
            }

            if (rig.ActionSequence != lastSentActionSeq)
            {
                return true;
            }
        }

        string heldObjectType = GetHeldObjectType(localCube);
        string heldItemType = GetHeldItemType(localCube);
        if (!string.Equals(heldObjectType, lastSentHeldObjectType, StringComparison.Ordinal) ||
            !string.Equals(heldItemType, lastSentHeldItemType, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private void MarkStateSent(float now)
    {
        hasSentInitialState = true;
        lastStateSendTime = now;
        Transform cubeTransform = localCube.TrackedTransform;
        lastSentPosition = cubeTransform.position;
        lastSentEulerAngles = cubeTransform.eulerAngles;

        ProceduralPlayerRig rig = localCube.Rig;
        if (rig != null)
        {
            lastSentMoveTarget = rig.RunTarget != null ? rig.RunTarget.position : cubeTransform.position;
            lastSentAimTarget = rig.AimTarget != null ? rig.AimTarget.position : cubeTransform.position + cubeTransform.forward;
            lastSentGaitForward = rig.GaitForward;
            lastSentLeftArmTarget = rig.LeftArmTargetWorld;
            lastSentRightArmTarget = rig.RightArmTargetWorld;
            lastSentActionSeq = rig.ActionSequence;
        }

        lastSentHeldObjectType = GetHeldObjectType(localCube);
        lastSentHeldItemType = GetHeldItemType(localCube);
    }

    private static string GetHeldObjectType(LocalCubeController player)
    {
        if (player == null)
        {
            return "none";
        }

        PlayerWeaponPickup weaponPickup = player.GetComponent<PlayerWeaponPickup>();
        if (weaponPickup != null && weaponPickup.HasWeapon)
        {
            return "weapon";
        }

        PlayerItemPickup itemPickup = player.GetComponent<PlayerItemPickup>();
        if (itemPickup != null && itemPickup.HasItem)
        {
            return "item";
        }

        return "none";
    }

    private static string GetHeldItemType(LocalCubeController player)
    {
        if (player == null)
        {
            return "";
        }

        PlayerWeaponPickup weaponPickup = player.GetComponent<PlayerWeaponPickup>();
        if (weaponPickup != null && weaponPickup.HasWeapon)
        {
            return "spear";
        }

        PlayerItemPickup itemPickup = player.GetComponent<PlayerItemPickup>();
        if (itemPickup != null && itemPickup.HasItem && itemPickup.HeldItem != null)
        {
            return itemPickup.HeldItem.ItemType.ToString();
        }

        return "";
    }

    private bool IsLocalMammothAuthority()
    {
        return gameStarted && ResolveLocalSpawnSlot() == 0;
    }

    private void TryConfigureMammothRuntime()
    {
        if (!gameStarted)
        {
            return;
        }

        EnemyHealth mammoth = GetCachedMammothEnemy();
        if (mammoth == null)
        {
            mammothRuntimeConfigured = false;
            return;
        }

        if (!mammothRuntimeConfigured)
        {
            SetMammothAuthorityMode(mammoth, IsLocalMammothAuthority());
            mammothRuntimeConfigured = true;
        }
    }

    private EnemyHealth GetCachedMammothEnemy()
    {
        if (cachedMammothEnemy != null)
        {
            return cachedMammothEnemy;
        }

        cachedMammothEnemy = FindMammothEnemy();
        return cachedMammothEnemy;
    }

    private static void SetBehaviourEnabled<T>(Component root, bool isEnabled) where T : Behaviour
    {
        if (root == null)
        {
            return;
        }

        T behaviour = root.GetComponent<T>();
        if (behaviour != null)
        {
            behaviour.enabled = isEnabled;
        }
    }

    private static void SetMammothAuthorityMode(EnemyHealth mammoth, bool isAuthority)
    {
        if (mammoth == null)
        {
            return;
        }

        MammothMovement movement = mammoth.GetComponent<MammothMovement>();
        if (!isAuthority && movement != null)
        {
            movement.Stop();
        }

        NavMeshAgent agent = mammoth.GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            if (!isAuthority && agent.enabled)
            {
                agent.ResetPath();
                agent.velocity = Vector3.zero;
            }

            agent.enabled = isAuthority;
        }

        SetBehaviourEnabled<MammothBrain>(mammoth, isAuthority);
        SetBehaviourEnabled<MammothActionController>(mammoth, isAuthority);
        SetBehaviourEnabled<MammothCombat>(mammoth, isAuthority);
        SetBehaviourEnabled<MammothSenses>(mammoth, isAuthority);
        SetBehaviourEnabled<MammothMovement>(mammoth, isAuthority);
    }

    private bool ShouldSendMammothStateNow(float now)
    {
        if (!IsLocalMammothAuthority() || !gameStarted || gameSocket == null || !gameSocket.IsOpen)
        {
            return false;
        }

        EnemyHealth mammoth = GetCachedMammothEnemy();
        if (mammoth == null || mammoth.IsDead)
        {
            return false;
        }

        if (!hasSentInitialMammothState)
        {
            return true;
        }

        if (now - lastMammothStateSendTime >= MammothForcedStateSendInterval)
        {
            return true;
        }

        Transform mammothTransform = mammoth.transform;
        if ((mammothTransform.position - lastSentMammothPosition).sqrMagnitude >= MinPositionDeltaSqr)
        {
            return true;
        }

        return Quaternion.Angle(Quaternion.Euler(lastSentMammothEulerAngles), mammothTransform.rotation) >= MinRotationDelta;
    }

    private void SendMammothStateUpdate()
    {
        EnemyHealth mammoth = GetCachedMammothEnemy();
        if (mammoth == null)
        {
            return;
        }

        MammothStateDto state = MammothStateDto.FromEnemyHealth(
            currentGameLobbyId,
            currentUser != null ? currentUser.id : 0,
            mammoth
        );
        gameSocket.SendJson(JsonUtility.ToJson(state));

        hasSentInitialMammothState = true;
        lastMammothStateSendTime = Time.unscaledTime;
        lastSentMammothPosition = mammoth.transform.position;
        lastSentMammothEulerAngles = mammoth.transform.eulerAngles;
    }

    private void UpdateRemoteMammothPose()
    {
        if (IsLocalMammothAuthority() || !hasRemoteMammothPose)
        {
            return;
        }

        EnemyHealth mammoth = GetCachedMammothEnemy();
        if (mammoth == null || mammoth.IsDead)
        {
            return;
        }

        float step = Mathf.Clamp01(Time.unscaledDeltaTime * MammothRemoteLerpSpeed);
        Transform mammothTransform = mammoth.transform;
        mammothTransform.position = Vector3.Lerp(mammothTransform.position, targetRemoteMammothPosition, step);
        mammothTransform.rotation = Quaternion.Slerp(mammothTransform.rotation, targetRemoteMammothRotation, step);
    }

    private void RefreshGameHud()
    {
        string rtt = lastGameRttMs >= 0f ? $"{lastGameRttMs:0} ms" : "measuring";
        string lastRemote = lastRemoteStateReceiveTime >= 0f
            ? $"{Mathf.Max(0f, (Time.unscaledTime - lastRemoteStateReceiveTime) * 1000f):0} ms ago"
            : "none yet";

        SetText(
            gameStatusText,
            $"WASD move, Space jump\nSocket RTT: {rtt} | Remote states: {remoteStatesPerSecond}/s | Last remote: {lastRemote}\nRelay: direct Daphne process, up to 30 Hz\nDBG {debugClientTag}: gameMsg={gameMessagesReceived}, applied={remoteStatesApplied}, spawned={remoteStatesSpawned}, droppedLocal={remoteStatesDroppedAsLocal}");
    }

    public static void NotifyEnemyDamaged(EnemyHealth enemyHealth, int damage)
    {
        if (Instance == null)
        {
            return;
        }

        Instance.SendMammothHealthUpdate(enemyHealth, damage);
    }

    public static bool ShouldDeferEnemyDeath(EnemyHealth enemyHealth)
    {
        return Instance != null && Instance.gameStarted && IsMammothEnemy(enemyHealth);
    }

    private void SendMammothHealthUpdate(EnemyHealth enemyHealth, int damage)
    {
        if (!gameStarted || gameSocket == null || !gameSocket.IsOpen || enemyHealth == null || !IsMammothEnemy(enemyHealth))
        {
            return;
        }

        MammothHealthDto update = MammothHealthDto.FromEnemyHealth(currentGameLobbyId, enemyHealth, damage);
        gameSocket.SendJson(JsonUtility.ToJson(update));
    }

    private void TryApplyMammothState(MammothStateDto mammothState)
    {
        if (mammothState == null)
        {
            return;
        }

        if (IsLocalMammothAuthority())
        {
            return;
        }

        if (Time.unscaledTime < ignoreIncomingMammothDeathUntil && mammothState.currentHealth <= 0)
        {
            return;
        }

        EnemyHealth mammoth = GetCachedMammothEnemy();
        if (mammoth == null)
        {
            pendingMammothState = mammothState;
            return;
        }

        pendingMammothState = null;
        TryConfigureMammothRuntime();
        ApplyMammothHealthFallbackFromState(mammoth, mammothState.currentHealth, mammothState.maxHealth);

        targetRemoteMammothPosition = MultiplayerJson.ArrayToVector(mammothState.position);
        targetRemoteMammothRotation = Quaternion.Euler(MultiplayerJson.ArrayToVector(mammothState.rotation));
        hasRemoteMammothPose = true;

        Transform mammothTransform = mammoth.transform;
        if ((mammothTransform.position - targetRemoteMammothPosition).sqrMagnitude > 100f)
        {
            mammothTransform.position = targetRemoteMammothPosition;
        }
    }

    private void TryApplyMammothHealth(MammothHealthDto mammothHealth)
    {
        if (mammothHealth == null)
        {
            return;
        }

        if (IsLocalMammothAuthority())
        {
            return;
        }

        if (Time.unscaledTime < ignoreIncomingMammothDeathUntil && mammothHealth.currentHealth <= 0)
        {
            return;
        }

        EnemyHealth mammoth = GetCachedMammothEnemy();
        if (mammoth == null)
        {
            pendingMammothHealth = mammothHealth;
            return;
        }

        pendingMammothHealth = null;
        mammoth.ApplyNetworkHealth(mammothHealth.currentHealth, mammothHealth.maxHealth, mammothHealth.damage);
    }

    private static void ApplyMammothHealthFallbackFromState(EnemyHealth mammoth, int stateCurrentHealth, int stateMaxHealth)
    {
        if (mammoth == null)
        {
            return;
        }

        // Non-authority clients primarily rely on mammoth_health events.
        // State snapshots are fallback-only so delayed packets cannot roll health backwards.
        bool maxChanged = stateMaxHealth != mammoth.MaxHealth;
        bool isLowerHealth = stateCurrentHealth < mammoth.CurrentHealth;
        bool alreadyDead = mammoth.CurrentHealth <= 0 && stateCurrentHealth <= 0;

        if (maxChanged || isLowerHealth || alreadyDead)
        {
            mammoth.ApplyNetworkHealth(stateCurrentHealth, stateMaxHealth);
        }
    }

    private static EnemyHealth FindMammothEnemy()
    {
        EnemyHealth[] enemies = FindObjectsByType<EnemyHealth>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        EnemyHealth fallback = null;

        foreach (EnemyHealth enemy in enemies)
        {
            if (!IsMammothEnemy(enemy))
            {
                continue;
            }

            if (HasEnemyHealthAncestor(enemy.transform))
            {
                if (fallback == null)
                {
                    fallback = enemy;
                }

                continue;
            }

            if (enemy.GetComponent<MammothBrain>() != null || enemy.GetComponent<NavMeshAgent>() != null)
            {
                return enemy;
            }

            if (fallback == null)
            {
                fallback = enemy;
            }
        }

        return fallback;
    }

    public static void NotifyMammothRespawned(EnemyHealth mammoth)
    {
        if (Instance == null || mammoth == null)
        {
            return;
        }

        Instance.cachedMammothEnemy = mammoth;
        Instance.pendingMammothState = null;
        Instance.pendingMammothHealth = null;
        Instance.mammothRuntimeConfigured = false;
        Instance.hasRemoteMammothPose = false;
        Instance.ignoreIncomingMammothDeathUntil = Time.unscaledTime + 6f;
        Instance.lastSentMammothPosition = mammoth.transform.position;
        Instance.lastSentMammothEulerAngles = mammoth.transform.eulerAngles;
        Instance.hasSentInitialMammothState = false;
        Instance.TryConfigureMammothRuntime();
    }

    public static Transform GetClosestPlayerTransform(Vector3 origin)
    {
        if (Instance != null)
        {
            Transform runtimePlayer = Instance.FindClosestRuntimePlayerTransform(origin);
            if (runtimePlayer != null)
            {
                return runtimePlayer;
            }
        }

        return FindClosestFallbackPlayerTransform(origin);
    }

    public static void GetActivePlayerTransforms(List<Transform> results)
    {
        if (results == null)
        {
            return;
        }

        results.Clear();

        if (Instance != null)
        {
            Instance.CollectRuntimePlayerTransforms(results);
            if (results.Count > 0)
            {
                return;
            }
        }

        PlayerHealth[] playerHealths = FindObjectsByType<PlayerHealth>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (PlayerHealth playerHealth in playerHealths)
        {
            AddUniquePlayerTransform(results, playerHealth != null ? playerHealth.transform : null);
        }

        GameObject[] taggedPlayers = GameObject.FindGameObjectsWithTag("Player");
        foreach (GameObject taggedPlayer in taggedPlayers)
        {
            AddUniquePlayerTransform(results, taggedPlayer != null ? taggedPlayer.transform : null);
        }
    }

    public static bool TryGetLocalRespawnPosition(out Vector3 respawnPosition)
    {
        if (Instance != null)
        {
            respawnPosition = Instance.ResolveSafeSpawnPosition(
                Instance.ResolveLocalSpawnSlot(),
                Instance.currentUser != null ? Instance.currentUser.id : 0,
                "local-respawn");
            return true;
        }

        respawnPosition = Vector3.zero;
        return false;
    }

    private Transform FindClosestRuntimePlayerTransform(Vector3 origin)
    {
        Transform closest = null;
        float closestDistanceSqr = float.PositiveInfinity;

        ConsiderPlayerTransform(localCube != null ? localCube.TrackedTransform : null, origin, ref closest, ref closestDistanceSqr);

        foreach (RemoteCubeController remote in remoteCubes.Values)
        {
            ConsiderPlayerTransform(remote != null ? remote.TrackedTransform : null, origin, ref closest, ref closestDistanceSqr);
        }

        return closest;
    }

    private void CollectRuntimePlayerTransforms(List<Transform> results)
    {
        AddUniquePlayerTransform(results, localCube != null ? localCube.TrackedTransform : null);

        foreach (RemoteCubeController remote in remoteCubes.Values)
        {
            AddUniquePlayerTransform(results, remote != null ? remote.TrackedTransform : null);
        }
    }

    private static Transform FindClosestFallbackPlayerTransform(Vector3 origin)
    {
        Transform closest = null;
        float closestDistanceSqr = float.PositiveInfinity;

        PlayerHealth[] playerHealths = FindObjectsByType<PlayerHealth>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (PlayerHealth playerHealth in playerHealths)
        {
            ConsiderPlayerTransform(playerHealth != null ? playerHealth.transform : null, origin, ref closest, ref closestDistanceSqr);
        }

        GameObject[] taggedPlayers = GameObject.FindGameObjectsWithTag("Player");
        foreach (GameObject taggedPlayer in taggedPlayers)
        {
            ConsiderPlayerTransform(taggedPlayer != null ? taggedPlayer.transform : null, origin, ref closest, ref closestDistanceSqr);
        }

        return closest;
    }

    private static void ConsiderPlayerTransform(
        Transform candidate,
        Vector3 origin,
        ref Transform closest,
        ref float closestDistanceSqr)
    {
        if (candidate == null || !candidate.gameObject.activeInHierarchy)
        {
            return;
        }

        float distanceSqr = (candidate.position - origin).sqrMagnitude;
        if (distanceSqr >= closestDistanceSqr)
        {
            return;
        }

        closest = candidate;
        closestDistanceSqr = distanceSqr;
    }

    private static void AddUniquePlayerTransform(List<Transform> results, Transform candidate)
    {
        if (results == null || candidate == null || !candidate.gameObject.activeInHierarchy)
        {
            return;
        }

        if (!results.Contains(candidate))
        {
            results.Add(candidate);
        }
    }

    private static bool IsMammothEnemy(EnemyHealth enemyHealth)
    {
        if (enemyHealth == null)
        {
            return false;
        }

        string enemyName = enemyHealth.gameObject.name;
        return enemyName.IndexOf("Mammoth", StringComparison.OrdinalIgnoreCase) >= 0
            || enemyName.IndexOf("Mamoth", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool HasEnemyHealthAncestor(Transform transform)
    {
        if (transform == null)
        {
            return false;
        }

        Transform current = transform.parent;
        while (current != null)
        {
            if (current.GetComponent<EnemyHealth>() != null)
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private void ResetStateSendTracking()
    {
        stateSeq = 0;
        nextStateSendTime = 0f;
        lastStateSendTime = 0f;
        hasSentInitialState = false;
        lastSentPosition = Vector3.zero;
        lastSentEulerAngles = Vector3.zero;
        lastSentMoveTarget = Vector3.zero;
        lastSentAimTarget = Vector3.zero;
        lastSentGaitForward = Vector3.zero;
        lastSentLeftArmTarget = Vector3.zero;
        lastSentRightArmTarget = Vector3.zero;
        lastSentActionSeq = 0;
        lastSentHeldObjectType = "none";
        lastSentHeldItemType = "";
        nextMammothStateSendTime = 0f;
        lastMammothStateSendTime = 0f;
        hasSentInitialMammothState = false;
        lastSentMammothPosition = Vector3.zero;
        lastSentMammothEulerAngles = Vector3.zero;
        hasRemoteMammothPose = false;
        targetRemoteMammothPosition = Vector3.zero;
        targetRemoteMammothRotation = Quaternion.identity;
        cachedMammothEnemy = null;
        mammothRuntimeConfigured = false;
        ignoreIncomingMammothDeathUntil = 0f;
        nextGamePingTime = 0f;
        nextLobbyPingTime = 0f;
        nextHudRefreshTime = 0f;
        lastGameRttMs = -1f;
        lastRemoteStateReceiveTime = -1f;
        remoteStateRateWindowStart = Time.unscaledTime;
        remoteStatesInWindow = 0;
        remoteStatesPerSecond = 0;
        gameMessagesReceived = 0;
        remoteStatesApplied = 0;
        remoteStatesDroppedAsLocal = 0;
        remoteStatesDroppedInvalid = 0;
        remoteStatesSpawned = 0;
        lastGamePingSendTime = -1f;
        lastGamePongReceiveTime = -1f;
        lastLobbyPingSendTime = -1f;
        lastLobbyPongReceiveTime = -1f;
        lastGameHeartbeatReceiveTime = -1f;
        lastLobbyHeartbeatReceiveTime = -1f;
        gameHeartbeatCloseRequested = false;
        lobbyHeartbeatCloseRequested = false;
        lastGameSocketCloseTime = -1f;
        lastGameSocketCloseGapMs = -1f;
        lastGameEnvelopeType = "none";
        lastGameEnvelopeTime = -1f;
        lastLobbyEnvelopeType = "none";
        lastLobbyEnvelopeTime = -1f;
    }

    private void DumpMultiplayerDebugSnapshot()
    {
        StringBuilder sb = new StringBuilder(256);
        sb.Append("=== Multiplayer Debug Snapshot === ");
        sb.Append("client=").Append(debugClientTag);
        sb.Append(", userId=").Append(currentUser != null ? currentUser.id : 0);
        sb.Append(", localPlayerId=").Append(GetLocalPlayerId() ?? "null");
        sb.Append(", lobbyId=").Append(currentLobby != null ? currentLobby.id : -1);
        sb.Append(", gameLobbyId=").Append(currentGameLobbyId);
        sb.Append(", lobbyMsg=").Append(lobbyMessagesReceived);
        sb.Append(", gameMsg=").Append(gameMessagesReceived);
        sb.Append(", remoteApplied=").Append(remoteStatesApplied);
        sb.Append(", remoteSpawned=").Append(remoteStatesSpawned);
        sb.Append(", remoteDroppedLocal=").Append(remoteStatesDroppedAsLocal);
        sb.Append(", remoteDroppedInvalid=").Append(remoteStatesDroppedInvalid);
        sb.Append(", remoteCubeCount=").Append(remoteCubes.Count);
        sb.Append(", lastLobbyClose=").Append(lastLobbySocketCloseCode);
        sb.Append(", lastGameClose=").Append(lastGameSocketCloseCode);
        sb.Append(", lastPingAgoMs=").Append(lastGamePingSendTime >= 0f ? ((Time.unscaledTime - lastGamePingSendTime) * 1000f).ToString("0") : "n/a");
        sb.Append(", lastPongAgoMs=").Append(lastGamePongReceiveTime >= 0f ? ((Time.unscaledTime - lastGamePongReceiveTime) * 1000f).ToString("0") : "n/a");
        sb.Append(", lobbyPingAgoMs=").Append(lastLobbyPingSendTime >= 0f ? ((Time.unscaledTime - lastLobbyPingSendTime) * 1000f).ToString("0") : "n/a");
        sb.Append(", lobbyPongAgoMs=").Append(lastLobbyPongReceiveTime >= 0f ? ((Time.unscaledTime - lastLobbyPongReceiveTime) * 1000f).ToString("0") : "n/a");
        sb.Append(", lastLobbyMsg=").Append(lastLobbyEnvelopeType).Append("@").Append(FormatAgo(lastLobbyEnvelopeTime));
        sb.Append(", lastGameMsg=").Append(lastGameEnvelopeType).Append("@").Append(FormatAgo(lastGameEnvelopeTime));
        sb.Append(", lastGameCloseGapMs=").Append(lastGameSocketCloseGapMs >= 0f ? lastGameSocketCloseGapMs.ToString("0") : "n/a");
        if (lobbySocket != null)
        {
            sb.Append(", lobbySocket={").Append(lobbySocket.GetDebugSnapshot()).Append("}");
        }
        if (gameSocket != null)
        {
            sb.Append(", gameSocket={").Append(gameSocket.GetDebugSnapshot()).Append("}");
        }
        Debug.Log(sb.ToString());
    }

    private string DescribeGameStarted(GameStartedDto start)
    {
        if (start?.players == null)
        {
            return "game_started payload missing players.";
        }

        StringBuilder sb = new StringBuilder(128);
        sb.Append("game_started players=");
        for (int i = 0; i < start.players.Length; i++)
        {
            GameStartedPlayerDto p = start.players[i];
            if (p == null)
            {
                continue;
            }

            if (i > 0)
            {
                sb.Append(" | ");
            }

            sb.Append("{uid=").Append(p.userId)
              .Append(", pid=").Append(p.playerId)
              .Append(", slot=").Append(p.slot)
              .Append("}");
        }

        return sb.ToString();
    }

    private void NetLog(string message, bool warning = false)
    {
        if (!verboseNetworkingLogs)
        {
            return;
        }

        string formatted = $"[MP:{debugClientTag}] {message}";
        if (warning)
        {
            Debug.LogWarning(formatted);
        }
        else
        {
            Debug.Log(formatted);
        }
    }

    private void LogSocketTrace(string prefix, CaveGameSocketClient socket, bool warning, bool intentionalClose, string reason)
    {
        if (socket == null)
        {
            NetLog(prefix + ": socket=null", warning);
            return;
        }

        NetLog($"{prefix}: intentional={intentionalClose}, reason={reason}, {socket.GetDebugSnapshot()}", warning);
    }

    private void MarkExpectedLobbyClose(string reason)
    {
        lobbyCloseExpected = true;
        lobbyCloseExpectedReason = reason;
    }

    private void MarkExpectedGameClose(string reason)
    {
        gameCloseExpected = true;
        gameCloseExpectedReason = reason;
    }

    private bool TryConsumeExpectedLobbyClose(out string reason)
    {
        reason = lobbyCloseExpectedReason;
        if (!lobbyCloseExpected)
        {
            return false;
        }

        lobbyCloseExpected = false;
        lobbyCloseExpectedReason = "none";
        return true;
    }

    private bool TryConsumeExpectedGameClose(out string reason)
    {
        reason = gameCloseExpectedReason;
        if (!gameCloseExpected)
        {
            return false;
        }

        gameCloseExpected = false;
        gameCloseExpectedReason = "none";
        return true;
    }

    private static bool TryExtractCloseCode(string closeCode, out int numericCode)
    {
        numericCode = -1;
        if (string.IsNullOrWhiteSpace(closeCode))
        {
            return false;
        }

        int i = 0;
        while (i < closeCode.Length && char.IsWhiteSpace(closeCode[i]))
        {
            i++;
        }

        int start = i;
        while (i < closeCode.Length && char.IsDigit(closeCode[i]))
        {
            i++;
        }

        if (i <= start)
        {
            return false;
        }

        return int.TryParse(closeCode.Substring(start, i - start), out numericCode);
    }

    private static string FormatAgo(float timestamp)
    {
        if (timestamp < 0f)
        {
            return "n/a";
        }

        return $"{Mathf.Max(0f, (Time.unscaledTime - timestamp) * 1000f):0}ms";
    }

    private void BuildUi()
    {
        EnsureEventSystem();

        GameObject canvasObject = new GameObject("Wallow Multiplayer UI");
        DontDestroyOnLoad(canvasObject);
        canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObject.AddComponent<GraphicRaycaster>();

        menuBackdrop = CreateMenuBackdrop();
        menuWordmarkRoot = CreateMenuWordmark();

        loadingPanel = CreatePanel("Loading Panel");
        AddKicker(loadingPanel.transform, "WORLD BOOT");
        loadingTitleText = AddTitle(loadingPanel.transform, "Preparing The World");
        AddText(loadingPanel.transform, "We wait for terrain, chunks, navigation, and spawn setup before opening multiplayer controls.", 18, MutedText, TextAnchor.MiddleLeft, 88f);
        CreateProgressBar(loadingPanel.transform, out loadingProgressFill, out loadingProgressText);
        loadingStatusText = AddText(loadingPanel.transform, "", 16, MutedText, TextAnchor.MiddleLeft, 54f);

        loginPanel = CreatePanel("Login Panel");
        AddKicker(loginPanel.transform, "WALLOW ONLINE");
        AddTitle(loginPanel.transform, "Enter The Cave");
        AddText(loginPanel.transform, "Spin up a guest token, then create or join a lobby from the same backend.", 18, MutedText, TextAnchor.MiddleLeft, 64f);
        serverInput = AddInput(loginPanel.transform, "Server URL", DefaultServerUrl, false);
        usernameInput = AddInput(loginPanel.transform, "Display Name", "wallow-runner", false);
        passwordInput = AddInput(loginPanel.transform, "Password (reserved)", "", true);
        AddButton(loginPanel.transform, "Connect To Wallow", Login, Accent);
        loginStatusText = AddText(loginPanel.transform, "", 16, MutedText, TextAnchor.MiddleLeft, 56f);

        findPanel = CreatePanel("Find Games Panel");
        AddKicker(findPanel.transform, "MULTIPLAYER");
        AddTitle(findPanel.transform, "Lobby Control");
        AddText(findPanel.transform, "Host a four-player cave run or enter a friend code to join their lobby.", 18, MutedText, TextAnchor.MiddleLeft, 64f);
        AddButton(findPanel.transform, "Create New Lobby", CreateLobby, Accent);
        joinCodeInput = AddInput(findPanel.transform, "Lobby Code", "", false);
        AddButton(findPanel.transform, "Join By Code", JoinLobby, AccentCool);
        findStatusText = AddText(findPanel.transform, "", 16, MutedText, TextAnchor.MiddleLeft, 56f);

        lobbyPanel = CreatePanel("Lobby Panel");
        AddKicker(lobbyPanel.transform, "WALLOW PARTY");
        lobbyTitleText = AddTitle(lobbyPanel.transform, "Lobby");
        lobbyCodeText = AddText(lobbyPanel.transform, "", 30, Accent, TextAnchor.MiddleLeft, 46f);
        lobbyHostText = AddText(lobbyPanel.transform, "", 16, MutedText, TextAnchor.MiddleLeft, 48f);
        lobbyPlayersText = AddText(lobbyPanel.transform, "", 16, MutedText, TextAnchor.MiddleLeft, 36f);

        GameObject slotGrid = new GameObject("Player Slot Grid");
        slotGrid.transform.SetParent(lobbyPanel.transform, false);
        VerticalLayoutGroup slotLayout = slotGrid.AddComponent<VerticalLayoutGroup>();
        slotLayout.spacing = 8f;
        slotLayout.childControlHeight = true;
        slotLayout.childForceExpandHeight = false;
        slotLayout.childControlWidth = true;
        slotLayout.childForceExpandWidth = true;
        slotGrid.AddComponent<LayoutElement>().preferredHeight = 264f;
        for (int slot = 0; slot < DefaultMaxPlayers; slot++)
        {
            lobbySlotViews.Add(CreateLobbySlot(slotGrid.transform, slot));
        }

        GameObject actionRow = AddRow(lobbyPanel.transform, "Lobby Actions", 52f);
        readyButton = AddButton(actionRow.transform, "Ready Up", ToggleReady, Success);
        readyButtonImage = readyButton.targetGraphic as Image;
        startButton = AddButton(actionRow.transform, "Start Run", StartLobby, Accent);
        startButtonImage = startButton.targetGraphic as Image;

        GameObject utilityRow = AddRow(lobbyPanel.transform, "Lobby Utility", 44f);
        copyCodeButton = AddButton(utilityRow.transform, "Copy Code", CopyLobbyCode, AccentCool);
        leaveLobbyButton = AddButton(utilityRow.transform, "Leave", LeaveLobby, new Color(0.82f, 0.23f, 0.25f));
        lobbyStatusText = AddText(lobbyPanel.transform, "", 16, MutedText, TextAnchor.MiddleLeft, 56f);

        gameHudPanel = CreatePanel("Game HUD", false);
        gameHudPanel.GetComponent<RectTransform>().anchorMin = new Vector2(0f, 1f);
        gameHudPanel.GetComponent<RectTransform>().anchorMax = new Vector2(0f, 1f);
        gameHudPanel.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
        gameHudPanel.GetComponent<RectTransform>().anchoredPosition = new Vector2(20f, -20f);
        gameHudPanel.GetComponent<RectTransform>().sizeDelta = new Vector2(620f, 168f);
        AddKicker(gameHudPanel.transform, "LIVE RUN");
        gameStatusText = AddText(gameHudPanel.transform, "", 16, MutedText, TextAnchor.MiddleLeft, 78f);
    }

    private GameObject CreatePanel(string name, bool ornate = true)
    {
        GameObject panel = new GameObject(name);
        panel.transform.SetParent(canvas.transform, false);
        Image image = panel.AddComponent<Image>();
        image.color = Panel;
        image.raycastTarget = false;
        Shadow shadow = panel.AddComponent<Shadow>();
        shadow.effectColor = new Color(Ink.r, Ink.g, Ink.b, 0.45f);
        shadow.effectDistance = new Vector2(8f, -10f);
        Outline outline = panel.AddComponent<Outline>();
        outline.effectColor = new Color(Ink.r, Ink.g, Ink.b, 0.75f);
        outline.effectDistance = new Vector2(2f, -2f);
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = MenuPanelSize;
        rect.anchoredPosition = MenuPanelAnchoredPosition;

        CreatePanelFill(panel.transform, "Inset", new Vector2(18f, 18f), new Vector2(-18f, -18f), PanelSoft, new Color(Ink.r, Ink.g, Ink.b, 0.25f));
        if (ornate)
        {
            CreatePanelStrip(panel.transform, "Footer Band", 20f, 66f, new Color(0.82f, 0.75f, 0.61f, 0.45f), new Color(Ink.r, Ink.g, Ink.b, 0.12f));
            CreatePanelRule(panel.transform, "Top Rule", 90f, new Color(Ink.r, Ink.g, Ink.b, 0.14f));
            CreatePanelRule(panel.transform, "Bottom Rule", 74f, new Color(Ink.r, Ink.g, Ink.b, 0.12f));
        }

        VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = ornate ? new RectOffset(40, 40, 34, 34) : new RectOffset(28, 28, 24, 24);
        layout.spacing = 14f;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;

        return panel;
    }

    private GameObject AddRow(Transform parent, string name, float height)
    {
        GameObject row = new GameObject(name);
        row.transform.SetParent(parent, false);
        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 12f;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        row.AddComponent<LayoutElement>().preferredHeight = height;
        return row;
    }

    private Text AddKicker(Transform parent, string value)
    {
        Text text = AddText(parent, value, 14, AccentCool, TextAnchor.MiddleLeft, 28f);
        text.fontStyle = FontStyle.Bold;
        return text;
    }

    private Text AddTitle(Transform parent, string value)
    {
        Text text = AddText(parent, value, 38, Ink, TextAnchor.MiddleLeft, 58f);
        text.fontStyle = FontStyle.Bold;
        return text;
    }

    private Text AddText(Transform parent, string value, int size, Color color, TextAnchor alignment, float preferredHeight)
    {
        GameObject textObject = new GameObject("Text");
        textObject.transform.SetParent(parent, false);
        Text text = textObject.AddComponent<Text>();
        text.text = value;
        text.font = GetUiFont();
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        LayoutElement layout = textObject.AddComponent<LayoutElement>();
        layout.preferredHeight = preferredHeight;
        return text;
    }

    private void CreateProgressBar(Transform parent, out Image progressFill, out Text progressText)
    {
        GameObject row = AddRow(parent, "Loading Progress Row", 48f);

        GameObject progressBackground = new GameObject("Progress Background");
        progressBackground.transform.SetParent(row.transform, false);
        Image backgroundImage = progressBackground.AddComponent<Image>();
        backgroundImage.color = new Color(0.42f, 0.31f, 0.18f, 0.2f);
        Outline backgroundOutline = progressBackground.AddComponent<Outline>();
        backgroundOutline.effectColor = new Color(Ink.r, Ink.g, Ink.b, 0.32f);
        backgroundOutline.effectDistance = new Vector2(1f, -1f);
        RectTransform backgroundRect = progressBackground.GetComponent<RectTransform>();
        backgroundRect.sizeDelta = new Vector2(0f, 24f);
        LayoutElement backgroundLayout = progressBackground.AddComponent<LayoutElement>();
        backgroundLayout.flexibleWidth = 1f;
        backgroundLayout.preferredHeight = 24f;

        GameObject fillObject = new GameObject("Progress Fill");
        fillObject.transform.SetParent(progressBackground.transform, false);
        progressFill = fillObject.AddComponent<Image>();
        progressFill.color = Accent;
        progressFill.raycastTarget = false;
        RectTransform fillRect = progressFill.GetComponent<RectTransform>();
        fillRect.anchorMin = new Vector2(0f, 0f);
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        GameObject progressLabel = new GameObject("Progress Label");
        progressLabel.transform.SetParent(row.transform, false);
        progressText = progressLabel.AddComponent<Text>();
        progressText.font = GetUiFont();
        progressText.fontSize = 18;
        progressText.fontStyle = FontStyle.Bold;
        progressText.color = Ink;
        progressText.alignment = TextAnchor.MiddleRight;
        progressText.text = "0%";
        LayoutElement labelLayout = progressLabel.AddComponent<LayoutElement>();
        labelLayout.preferredWidth = 72f;
    }

    private InputField AddInput(Transform parent, string placeholder, string initialValue, bool password)
    {
        GameObject root = new GameObject(placeholder);
        root.transform.SetParent(parent, false);
        Image image = root.AddComponent<Image>();
        image.color = new Color(0.98f, 0.95f, 0.87f, 0.98f);
        Outline outline = root.AddComponent<Outline>();
        outline.effectColor = new Color(Ink.r, Ink.g, Ink.b, 0.52f);
        outline.effectDistance = new Vector2(1f, -1f);
        Shadow shadow = root.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.08f);
        shadow.effectDistance = new Vector2(0f, -2f);
        InputField input = root.AddComponent<InputField>();
        input.text = initialValue;
        input.contentType = password ? InputField.ContentType.Password : InputField.ContentType.Standard;
        root.AddComponent<LayoutElement>().preferredHeight = 50f;

        Text text = CreateInputText(root.transform, "Text", Color.black);
        Text placeholderText = CreateInputText(root.transform, "Placeholder", new Color(0.45f, 0.45f, 0.45f));
        placeholderText.text = placeholder;
        input.textComponent = text;
        input.placeholder = placeholderText;
        return input;
    }

    private Text CreateInputText(Transform parent, string name, Color color)
    {
        GameObject textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);
        Text text = textObject.AddComponent<Text>();
        text.font = GetUiFont();
        text.fontSize = 16;
        text.color = color;
        text.alignment = TextAnchor.MiddleLeft;
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(16f, 8f);
        rect.offsetMax = new Vector2(-16f, -8f);
        return text;
    }

    private Button AddButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, Color color)
    {
        GameObject buttonObject = new GameObject(label);
        buttonObject.transform.SetParent(parent, false);
        Image image = buttonObject.AddComponent<Image>();
        image.color = color;
        Outline outline = buttonObject.AddComponent<Outline>();
        outline.effectColor = new Color(Ink.r, Ink.g, Ink.b, 0.45f);
        outline.effectDistance = new Vector2(1f, -1f);
        Shadow shadow = buttonObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(Ink.r, Ink.g, Ink.b, 0.2f);
        shadow.effectDistance = new Vector2(0f, -4f);
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);
        buttonObject.AddComponent<LayoutElement>().preferredHeight = 48f;

        GameObject notch = new GameObject("Notch");
        notch.transform.SetParent(buttonObject.transform, false);
        Image notchImage = notch.AddComponent<Image>();
        notchImage.color = new Color(1f, 1f, 1f, 0.18f);
        notchImage.raycastTarget = false;
        RectTransform notchRect = notch.GetComponent<RectTransform>();
        notchRect.anchorMin = new Vector2(0f, 0f);
        notchRect.anchorMax = new Vector2(0f, 1f);
        notchRect.pivot = new Vector2(0f, 0.5f);
        notchRect.sizeDelta = new Vector2(12f, 0f);
        notchRect.anchoredPosition = Vector2.zero;

        Text text = AddText(buttonObject.transform, label, 18, new Color(1f, 0.98f, 0.94f), TextAnchor.MiddleCenter, 48f);
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        RectTransform rect = text.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        Destroy(text.GetComponent<LayoutElement>());
        return button;
    }

    private LobbySlotView CreateLobbySlot(Transform parent, int slot)
    {
        GameObject card = new GameObject("Slot " + (slot + 1));
        card.transform.SetParent(parent, false);
        Image background = card.AddComponent<Image>();
        background.color = PanelSoft;
        Outline outline = card.AddComponent<Outline>();
        outline.effectColor = new Color(Ink.r, Ink.g, Ink.b, 0.22f);
        outline.effectDistance = new Vector2(1f, -1f);
        Shadow shadow = card.AddComponent<Shadow>();
        shadow.effectColor = new Color(Ink.r, Ink.g, Ink.b, 0.08f);
        shadow.effectDistance = new Vector2(0f, -2f);
        HorizontalLayoutGroup row = card.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(0, 14, 0, 0);
        row.spacing = 12f;
        row.childControlHeight = true;
        row.childForceExpandHeight = true;
        row.childControlWidth = false;
        row.childForceExpandWidth = false;
        card.AddComponent<LayoutElement>().preferredHeight = 58f;

        GameObject accent = new GameObject("Accent");
        accent.transform.SetParent(card.transform, false);
        Image accentImage = accent.AddComponent<Image>();
        accentImage.color = GetPlayerColor("slot-" + slot);
        LayoutElement accentLayout = accent.AddComponent<LayoutElement>();
        accentLayout.preferredWidth = 8f;
        accentLayout.minWidth = 8f;

        GameObject content = new GameObject("Content");
        content.transform.SetParent(card.transform, false);
        VerticalLayoutGroup contentLayout = content.AddComponent<VerticalLayoutGroup>();
        contentLayout.padding = new RectOffset(0, 0, 7, 7);
        contentLayout.spacing = 0f;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandHeight = false;
        content.AddComponent<LayoutElement>().flexibleWidth = 1f;

        Text nameText = AddText(content.transform, "Open Slot", 18, Ink, TextAnchor.MiddleLeft, 26f);
        nameText.fontStyle = FontStyle.Bold;
        Text statusText = AddText(content.transform, "Waiting for player", 14, MutedText, TextAnchor.MiddleLeft, 22f);

        return new LobbySlotView(background, accentImage, nameText, statusText);
    }

    private void ApplyLobbySlot(LobbySlotView view, LobbyMemberDto member, int slot)
    {
        if (member == null)
        {
            view.Background.color = new Color(0.85f, 0.79f, 0.68f, 0.88f);
            view.Accent.color = new Color(0.49f, 0.43f, 0.34f, 0.8f);
            view.NameText.text = $"Slot {slot + 1} - Open";
            view.StatusText.text = "Invite a runner with the code above";
            view.StatusText.color = MutedText;
            return;
        }

        bool isLocal = currentUser != null && member.userId == currentUser.id;
        view.Background.color = isLocal ? new Color(0.97f, 0.89f, 0.74f, 0.98f) : PanelSoft;
        view.Accent.color = GetPlayerColor(member.playerId);
        view.NameText.text = $"{member.username}{(isLocal ? " (you)" : string.Empty)}";
        view.StatusText.text = member.isReady ? "Ready for the drop" : "Tuning gear";
        view.StatusText.color = member.isReady ? Success : MutedText;
    }

    private void ShowLogin(string status)
    {
        HideAllPanels();
        SetMenuChromeVisible(true);
        SetMusicTargetVolume(LobbyMusicVolume);
        loginPanel.SetActive(true);
        SetText(loginStatusText, status);
    }

    private void ShowFind(string status)
    {
        HideAllPanels();
        SetMenuChromeVisible(true);
        SetMusicTargetVolume(LobbyMusicVolume);
        findPanel.SetActive(true);
        SetText(findStatusText, status);
    }

    private void ShowLobby(string status)
    {
        HideAllPanels();
        SetMenuChromeVisible(true);
        SetMusicTargetVolume(LobbyMusicVolume);
        lobbyPanel.SetActive(true);
        RefreshLobbyUi(status);
    }

    private void ShowLoading(string status)
    {
        HideAllPanels();
        SetMenuChromeVisible(true);
        SetMusicTargetVolume(LobbyMusicVolume);
        loadingPanel.SetActive(true);
        SetText(loadingTitleText, worldBootstrapFailed ? "World Bootstrap Stalled" : "Preparing The World");
        SetText(loadingStatusText, status);
    }

    private void RefreshLobbyUi(string status)
    {
        if (currentLobby == null)
        {
            return;
        }

        bool isHost = currentUser != null && currentLobby.hostId == currentUser.id;
        int memberCount = CountMembers(currentLobby);
        int readyCount = CountReadyMembers(currentLobby);
        bool allReady = memberCount > 0 && readyCount == memberCount;

        SetText(lobbyTitleText, "Lobby " + currentLobby.id);
        SetText(lobbyCodeText, $"CODE {currentLobby.code}");
        SetText(lobbyHostText, isHost
            ? "You are the host. Launch unlocks when every joined player is ready."
            : "Waiting for the host to launch once the party is ready.");
        SetText(lobbyPlayersText, $"{readyCount}/{Mathf.Max(memberCount, 1)} ready - {memberCount}/{LobbyCapacity(currentLobby)} players in cave party");

        if (localMember != null)
        {
            bool localReady = localMember.isReady;
            SetButtonText(readyButton, localReady ? "Stand Down" : "Ready Up");
            SetButtonVisual(readyButton, readyButtonImage, localReady ? AccentCool : Success);
        }

        startButton.interactable = isHost && allReady && !currentLobby.isStarted;
        SetButtonVisual(startButton, startButtonImage, startButton.interactable ? Accent : PanelSoft);
        copyCodeButton.interactable = !string.IsNullOrWhiteSpace(currentLobby.code);
        leaveLobbyButton.interactable = true;

        for (int i = 0; i < lobbySlotViews.Count; i++)
        {
            LobbyMemberDto member = FindMemberInSlot(currentLobby, i);
            ApplyLobbySlot(lobbySlotViews[i], member, i);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            SetText(lobbyStatusText, status);
        }
    }

    private void CacheLobbyPlayerSlots()
    {
        playerSlotsById.Clear();
        if (currentLobby?.members == null)
        {
            return;
        }

        foreach (LobbyMemberDto member in currentLobby.members)
        {
            if (!string.IsNullOrWhiteSpace(member.playerId))
            {
                playerSlotsById[member.playerId] = member.slot;
            }

            if (member.userId > 0)
            {
                playerSlotsById[$"player_{member.userId}"] = member.slot;
            }
        }
    }

    private void CacheGameStartedPlayerSlots(GameStartedDto start)
    {
        if (start?.players == null)
        {
            return;
        }

        foreach (GameStartedPlayerDto player in start.players)
        {
            if (!string.IsNullOrWhiteSpace(player.playerId))
            {
                playerSlotsById[player.playerId] = player.slot;
            }

            if (player.userId > 0)
            {
                playerSlotsById[$"player_{player.userId}"] = player.slot;
            }
        }
    }

    private void HideAllPanels()
    {
        loadingPanel.SetActive(false);
        loginPanel.SetActive(false);
        findPanel.SetActive(false);
        lobbyPanel.SetActive(false);
        gameHudPanel.SetActive(false);
    }

    private IEnumerator WaitForWorldBootstrapThenEnableAuth()
    {
        ShowLoading("Searching for world renderer...");

        float searchDeadline = Time.unscaledTime + 20f;

        while (worldChunkRenderer == null && Time.unscaledTime < searchDeadline)
        {
            worldChunkRenderer = FindAnyObjectByType<WorldChunkRenderer>();
            RefreshLoadingUi();
            yield return null;
        }

        if (worldChunkRenderer == null)
        {
            worldBootstrapReady = true;
            worldBootstrapFailed = true;
            ShowLogin("World generator was not found. Multiplayer UI was unlocked without bootstrap gating.");
            yield break;
        }

        while (!worldChunkRenderer.IsBootstrapComplete)
        {
            RefreshLoadingUi();
            yield return null;
        }

        worldBootstrapReady = true;
        ShowLogin("World loaded. Enter a display name, then authenticate with the backend.");
    }

    private void RefreshLoadingUi()
    {
        if (loadingPanel == null || !loadingPanel.activeSelf)
        {
            return;
        }

        if (worldChunkRenderer == null)
        {
            SetText(loadingTitleText, "Preparing The World");
            SetText(loadingProgressText, "0%");
            SetText(loadingStatusText, "Searching for world renderer...");
            SetProgressFill(0f);
            return;
        }

        float progress = Mathf.Clamp01(worldChunkRenderer.BootstrapProgress);
        SetText(loadingTitleText, worldBootstrapFailed ? "World Bootstrap Stalled" : "Preparing The World");
        SetText(loadingProgressText, $"{Mathf.RoundToInt(progress * 100f)}%");
        SetText(loadingStatusText, worldChunkRenderer.BootstrapStatus);
        SetProgressFill(progress);
    }

    private bool EnsureWorldBootstrapReadyForUiAction()
    {
        if (worldBootstrapReady)
        {
            return true;
        }

        ShowLoading("The world is still loading. Multiplayer unlocks when bootstrap is complete.");
        return false;
    }

    private void SetProgressFill(float progress)
    {
        if (loadingProgressFill == null)
        {
            return;
        }

        RectTransform rect = loadingProgressFill.rectTransform;
        rect.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
    }

    private static int LobbyCapacity(LobbyDto lobby)
    {
        return lobby != null && lobby.maxPlayers > 0 ? lobby.maxPlayers : DefaultMaxPlayers;
    }

    private static int CountMembers(LobbyDto lobby)
    {
        return lobby?.members == null ? 0 : lobby.members.Length;
    }

    private static int CountReadyMembers(LobbyDto lobby)
    {
        if (lobby?.members == null)
        {
            return 0;
        }

        int ready = 0;
        foreach (LobbyMemberDto member in lobby.members)
        {
            if (member.isReady)
            {
                ready++;
            }
        }

        return ready;
    }

    private static LobbyMemberDto FindMemberInSlot(LobbyDto lobby, int slot)
    {
        if (lobby?.members == null)
        {
            return null;
        }

        foreach (LobbyMemberDto member in lobby.members)
        {
            if (member.slot == slot)
            {
                return member;
            }
        }

        return null;
    }

    private static LobbyMemberDto FindMember(LobbyDto lobby, int userId)
    {
        if (lobby?.members == null)
        {
            return null;
        }

        foreach (LobbyMemberDto member in lobby.members)
        {
            if (member.userId == userId)
            {
                return member;
            }
        }

        return null;
    }

    private static Vector3 SpawnForSlot(int slot)
    {
        Vector3[] spawns =
        {
            new Vector3(-4f, 0.5f, -4f),
            new Vector3(4f, 0.5f, -4f),
            new Vector3(-4f, 0.5f, 4f),
            new Vector3(4f, 0.5f, 4f),
        };
        return spawns[Mathf.Abs(slot) % spawns.Length];
    }

    private Vector3 SpawnForPlayer(int slot, int userId)
    {
        Vector3 baseSpawn = SpawnForSlot(slot);
        if (userId == 0)
        {
            return runtimeSpawnAnchor + baseSpawn;
        }

        // Secondary separation in case backend sends duplicate/invalid slots.
        int hash = Mathf.Abs(userId);
        float offsetX = ((hash % 3) - 1) * 6f;
        float offsetZ = (((hash / 3) % 3) - 1) * 6f;
        return runtimeSpawnAnchor + baseSpawn + new Vector3(offsetX, 0f, offsetZ);
    }

    private Vector3 ResolveSafeSpawnPosition(int slot, int userId, string context)
    {
        Vector3 candidate = SpawnForPlayer(slot, userId);
        if (TrySampleNavMeshSpawn(candidate, out Vector3 navMeshPosition))
        {
            return navMeshPosition;
        }

        if (TryResolveGroundHeight(candidate, out float groundHeight))
        {
            Vector3 grounded = new Vector3(candidate.x, groundHeight + SpawnHeightOffset, candidate.z);
            NetLog($"Ground-height spawn fallback ({context}) at {grounded}");
            return grounded;
        }

        float safeY = Mathf.Max(candidate.y + SpawnHeightOffset, runtimeSpawnAnchor.y + SpawnHeightOffset, SpawnHeightOffset);
        Vector3 fallback = new Vector3(candidate.x, safeY, candidate.z);
        NetLog($"Final spawn fallback ({context}) at {fallback}", true);
        return fallback;
    }

    private static bool TrySampleNavMeshSpawn(Vector3 nearPosition, out Vector3 safePosition)
    {
        Vector3 probe = nearPosition + Vector3.up * SpawnNavMeshProbeHeight;
        if (NavMesh.SamplePosition(probe, out NavMeshHit hit, SpawnNavMeshSampleRadius, NavMesh.AllAreas))
        {
            safePosition = hit.position + Vector3.up * SpawnHeightOffset;
            return true;
        }

        safePosition = Vector3.zero;
        return false;
    }

    private static bool TryResolveGroundHeight(Vector3 nearPosition, out float groundHeight)
    {
        Vector3 rayOrigin = nearPosition + Vector3.up * SpawnRaycastHeight;
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, SpawnRaycastDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            groundHeight = hit.point.y;
            return true;
        }

        Terrain terrain = Terrain.activeTerrain;
        if (terrain != null)
        {
            groundHeight = terrain.SampleHeight(nearPosition) + terrain.transform.position.y;
            return true;
        }

        groundHeight = 0f;
        return false;
    }

    private static string BuildPlayerKey(string playerId, int userId)
    {
        if (!string.IsNullOrWhiteSpace(playerId))
        {
            return playerId;
        }

        if (userId > 0)
        {
            return $"player_{userId}";
        }

        return "player_0";
    }

    private GameObject CreatePlayerCube(string objectName, Vector3 position, Color color, bool isLocal)
    {
        GameObject playerPrefab = ResolveProceduralPlayerPrefab();
        GameObject cube;

        if (playerPrefab != null)
        {
            cube = Instantiate(playerPrefab, position, Quaternion.identity, worldRoot.transform);
            cube.name = objectName.Replace("Cube", "Procedural");
            cube.SetActive(true);
            SetRendererColorRecursive(cube, color);
        }
        else
        {
            cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = objectName;
            cube.transform.SetParent(worldRoot.transform);
            cube.transform.position = position;
            SetRendererColor(cube, color);
        }

        ProceduralPlayerRig rig = cube.GetComponent<ProceduralPlayerRig>();
        if (rig == null)
        {
            rig = cube.AddComponent<ProceduralPlayerRig>();
        }

        rig.Configure(isLocal);
        rig.FitVisualsToCubeHeight();
        rig.PlaceCoreAt(position);

        Rigidbody body = cube.GetComponent<Rigidbody>();
        if (body == null)
        {
            body = cube.AddComponent<Rigidbody>();
        }

        body.freezeRotation = true;
        if (isLocal)
        {
            bool proceduralMovement = rig != null && rig.HasLegController;
            body.isKinematic = proceduralMovement;
            body.useGravity = !proceduralMovement;
            if (cube.GetComponent<LocalCubeController>() == null)
            {
                cube.AddComponent<LocalCubeController>();
            }
        }
        else
        {
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            if (cube.GetComponent<RemoteCubeController>() == null)
            {
                cube.AddComponent<RemoteCubeController>();
            }
        }

        return cube;
    }

    private GameObject ResolveProceduralPlayerPrefab()
    {
        GameObject resourcesPrefab = Resources.Load<GameObject>(PlayerPrefabResourcePath);
        if (resourcesPrefab != null)
        {
            return resourcesPrefab;
        }

        GameObject sceneTemplate = FindScenePlayerTemplate();
        if (sceneTemplate != null)
        {
            return sceneTemplate;
        }

#if UNITY_EDITOR
        return AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabAssetPath);
#else
        return null;
#endif
    }

    private GameObject FindScenePlayerTemplate()
    {
        ProceduralPlayerRig[] rigs = FindObjectsByType<ProceduralPlayerRig>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        foreach (ProceduralPlayerRig rig in rigs)
        {
            if (rig == null ||
                rig.transform.IsChildOf(transform) ||
                (worldRoot != null && rig.transform.IsChildOf(worldRoot.transform)))
            {
                continue;
            }

            return rig.gameObject;
        }

        AutoRunLegPairController[] legControllers = FindObjectsByType<AutoRunLegPairController>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        foreach (AutoRunLegPairController legController in legControllers)
        {
            if (legController == null ||
                legController.transform.IsChildOf(transform) ||
                (worldRoot != null && legController.transform.IsChildOf(worldRoot.transform)))
            {
                continue;
            }

            return legController.transform.root.gameObject;
        }

        return null;
    }

    private Vector3 ResolveRuntimeSpawnAnchor()
    {
        WorldChunkRenderer chunkRenderer = FindAnyObjectByType<WorldChunkRenderer>();
        if (chunkRenderer != null)
        {
            Vector3 arenaAnchor = chunkRenderer.GetArenaCenterWorldPosition(SpawnHeightOffset);
            NetLog($"Using arena spawn anchor from WorldChunkRenderer at {arenaAnchor}.");
            return arenaAnchor;
        }

        GameObject[] roots = FindObjectsByType<GameObject>(FindObjectsInactive.Exclude);
        foreach (GameObject root in roots)
        {
            if (root == null || !root.activeInHierarchy)
            {
                continue;
            }

            if (!string.Equals(root.name, "Player", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (root.GetComponent<LocalCubeController>() != null || root.transform.IsChildOf(transform))
            {
                continue;
            }

            Vector3 anchor = root.transform.position;
            root.SetActive(false);
            NetLog($"Disabled scene Player object at {anchor} and using it as multiplayer spawn anchor.");
            return new Vector3(anchor.x, Mathf.Max(anchor.y, 0.5f), anchor.z);
        }

        Vector3 fallback = Vector3.zero;
        if (Camera.main != null)
        {
            fallback = Camera.main.transform.position;
        }

        if (TrySampleNavMeshSpawn(fallback, out Vector3 navMeshFallback))
        {
            NetLog($"No active scene Player found; using NavMesh fallback anchor at {navMeshFallback}.");
            return navMeshFallback;
        }

        if (TryResolveGroundHeight(fallback, out float groundHeight))
        {
            Vector3 groundedFallback = new Vector3(fallback.x, groundHeight + SpawnHeightOffset, fallback.z);
            NetLog($"No active scene Player found; using ground fallback anchor at {groundedFallback}.");
            return groundedFallback;
        }

        Vector3 finalFallback = new Vector3(fallback.x, Mathf.Max(fallback.y, SpawnHeightOffset), fallback.z);
        NetLog($"No active scene Player found; using default fallback anchor at {finalFallback}.", true);
        return finalFallback;
    }

    private static bool IsSocketCloseWarning(string closeCode)
    {
        if (TryExtractCloseCode(closeCode, out int numericCode))
        {
            return numericCode != 1000;
        }

        return !string.Equals(closeCode, "Normal", StringComparison.OrdinalIgnoreCase);
    }

    private Color GetPlayerColor(string playerId)
    {
        Color[] colors =
        {
            new Color(1f, 0.85f, 0.05f), // host / slot 0: yellow
            new Color(0.1f, 0.45f, 1f), // slot 1: blue
            new Color(0.15f, 0.85f, 0.3f), // slot 2: green
            new Color(0.95f, 0.2f, 0.95f), // slot 3: magenta
        };

        if (!string.IsNullOrWhiteSpace(playerId) && playerSlotsById.TryGetValue(playerId, out int slot))
        {
            return colors[Mathf.Abs(slot) % colors.Length];
        }

        return new Color(0.8f, 0.8f, 0.8f);
    }

    private static void SetText(Text text, string value)
    {
        if (text != null)
        {
            text.text = value ?? string.Empty;
        }
    }

    private static void SetButtonText(Button button, string value)
    {
        Text text = button.GetComponentInChildren<Text>();
        if (text != null)
        {
            text.text = value;
        }
    }

    private static void SetButtonVisual(Button button, Image image, Color enabledColor)
    {
        if (image != null)
        {
            image.color = button != null && button.interactable ? enabledColor : new Color(0.5f, 0.44f, 0.34f, 0.85f);
        }
    }

    private GameObject CreateMenuBackdrop()
    {
        GameObject backdrop = new GameObject("Menu Backdrop");
        backdrop.transform.SetParent(canvas.transform, false);
        RectTransform rect = backdrop.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = backdrop.AddComponent<Image>();
        image.color = new Color(0.15f, 0.11f, 0.07f, 0.4f);
        image.raycastTarget = false;

        GameObject haze = new GameObject("Menu Haze");
        haze.transform.SetParent(backdrop.transform, false);
        Image hazeImage = haze.AddComponent<Image>();
        hazeImage.color = new Color(0.95f, 0.79f, 0.48f, 0.06f);
        hazeImage.raycastTarget = false;
        RectTransform hazeRect = haze.GetComponent<RectTransform>();
        hazeRect.anchorMin = new Vector2(0.5f, 0.5f);
        hazeRect.anchorMax = new Vector2(0.5f, 0.5f);
        hazeRect.pivot = new Vector2(0.5f, 0.5f);
        hazeRect.sizeDelta = new Vector2(1100f, 900f);

        return backdrop;
    }

    private GameObject CreateMenuWordmark()
    {
        GameObject root = new GameObject("Menu Wordmark");
        root.transform.SetParent(canvas.transform, false);
        RectTransform rect = root.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(760f, 180f);
        rect.anchoredPosition = new Vector2(0f, -20f);

        Text titleShadow = CreateWordmarkText(root.transform, "Fur Real?", 72, new Color(0f, 0f, 0f, 0.24f), new Vector2(5f, -5f));
        titleShadow.alignment = TextAnchor.MiddleCenter;
        titleShadow.raycastTarget = false;

        Text title = CreateWordmarkText(root.transform, "Fur Real?", 72, new Color(0.98f, 0.95f, 0.88f), Vector2.zero);
        title.alignment = TextAnchor.MiddleCenter;
        title.raycastTarget = false;

        Text subtitle = CreateWordmarkText(root.transform, "old-world lobby and hunting party", 20, new Color(0.93f, 0.86f, 0.7f, 0.95f), new Vector2(0f, -58f));
        subtitle.alignment = TextAnchor.MiddleCenter;
        subtitle.raycastTarget = false;
        return root;
    }

    private Text CreateWordmarkText(Transform parent, string value, int size, Color color, Vector2 anchoredPosition)
    {
        GameObject textObject = new GameObject("Wordmark Text");
        textObject.transform.SetParent(parent, false);
        Text text = textObject.AddComponent<Text>();
        text.font = GetUiFont();
        text.fontStyle = FontStyle.Bold;
        text.fontSize = size;
        text.color = color;
        text.text = value;
        text.alignment = TextAnchor.MiddleCenter;
        RectTransform rect = text.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(760f, 96f);
        rect.anchoredPosition = anchoredPosition;
        return text;
    }

    private void CreatePanelFill(Transform parent, string name, Vector2 offsetMin, Vector2 offsetMax, Color color, Color outlineColor)
    {
        GameObject fill = new GameObject(name);
        fill.transform.SetParent(parent, false);
        LayoutElement layout = fill.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;
        Image image = fill.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        Outline outline = fill.AddComponent<Outline>();
        outline.effectColor = outlineColor;
        outline.effectDistance = new Vector2(1f, -1f);
        RectTransform rect = fill.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private void CreatePanelStrip(Transform parent, string name, float bottomOffset, float height, Color color, Color outlineColor)
    {
        GameObject strip = new GameObject(name);
        strip.transform.SetParent(parent, false);
        LayoutElement layout = strip.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;
        Image image = strip.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        Outline outline = strip.AddComponent<Outline>();
        outline.effectColor = outlineColor;
        outline.effectDistance = new Vector2(1f, -1f);
        RectTransform rect = strip.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.offsetMin = new Vector2(20f, bottomOffset);
        rect.offsetMax = new Vector2(-20f, bottomOffset + height);
    }

    private void CreatePanelRule(Transform parent, string name, float bottomOffset, Color color)
    {
        GameObject rule = new GameObject(name);
        rule.transform.SetParent(parent, false);
        LayoutElement layout = rule.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;
        Image image = rule.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        RectTransform rect = rule.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.offsetMin = new Vector2(34f, bottomOffset);
        rect.offsetMax = new Vector2(-34f, bottomOffset + 2f);
    }

    private void ConfigureLobbyMusic()
    {
        lobbyMusicSource = gameObject.AddComponent<AudioSource>();
        lobbyMusicSource.playOnAwake = false;
        lobbyMusicSource.loop = true;
        lobbyMusicSource.spatialBlend = 0f;
        lobbyMusicSource.volume = 0f;
        lobbyMusicSource.ignoreListenerPause = true;
        lobbyMusicSource.clip = Resources.Load<AudioClip>(LobbyMusicResourcePath);

        if (lobbyMusicSource.clip == null)
        {
            Debug.LogWarning($"MultiplayerPrototype could not load lobby music at Resources/{LobbyMusicResourcePath}.");
            return;
        }

        lobbyMusicSource.Play();
    }

    private void UpdateMusicVolume()
    {
        if (lobbyMusicSource == null || lobbyMusicSource.clip == null)
        {
            return;
        }

        if (ShouldRestartLobbyMusic())
        {
            lobbyMusicSource.time = 0f;
            lobbyMusicSource.Play();
        }

        lobbyMusicSource.volume = Mathf.MoveTowards(lobbyMusicSource.volume, targetMusicVolume, MusicFadeSpeed * Time.unscaledDeltaTime);
    }

    private bool ShouldRestartLobbyMusic()
    {
        if (lobbyMusicSource == null || lobbyMusicSource.clip == null)
        {
            return false;
        }

        if (lobbyMusicSource.isPlaying)
        {
            return false;
        }

        if (lobbyMusicSource.loop)
        {
            return true;
        }

        return lobbyMusicSource.time >= Mathf.Max(0f, lobbyMusicSource.clip.length - MusicLoopRestartPadding);
    }

    private void SetMusicTargetVolume(float volume)
    {
        targetMusicVolume = Mathf.Clamp01(volume);
    }

    private void SetMenuChromeVisible(bool visible)
    {
        if (menuBackdrop != null)
        {
            menuBackdrop.SetActive(visible);
        }

        if (menuWordmarkRoot != null)
        {
            menuWordmarkRoot.SetActive(visible);
        }
    }

    private static void SetRendererColor(GameObject target, Color color)
    {
        Renderer renderer = target.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material = CreateRuntimeMaterial(color);
        }
    }

    private static void SetRendererColorRecursive(GameObject target, Color color)
    {
        if (target == null)
        {
            return;
        }

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer != null)
            {
                renderer.material = CreateRuntimeMaterial(color);
            }
        }
    }

    private static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null)
        {
            return;
        }

        GameObject eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<EventSystem>();
        eventSystem.AddComponent<InputSystemUIInputModule>();
    }

    private static Font GetUiFont()
    {
        if (cachedUiFont != null)
        {
            return cachedUiFont;
        }

        cachedUiFont = Resources.GetBuiltinResource<Font>(BuiltInFontName);
        return cachedUiFont;
    }

    private static Material CreateRuntimeMaterial(Color color)
    {
        Shader shader = GetRuntimeObjectShader();
        Material material = new Material(shader);
        material.color = color;
        return material;
    }

    private static Shader GetRuntimeObjectShader()
    {
        if (cachedObjectShader != null)
        {
            return cachedObjectShader;
        }

        cachedObjectShader = Shader.Find("Universal Render Pipeline/Lit");
        if (cachedObjectShader == null)
        {
            cachedObjectShader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        if (cachedObjectShader == null)
        {
            cachedObjectShader = Shader.Find("Unlit/Color");
        }

        if (cachedObjectShader == null)
        {
            cachedObjectShader = Shader.Find("Standard");
        }

        return cachedObjectShader;
    }

    private sealed class LobbySlotView
    {
        public LobbySlotView(Image background, Image accent, Text nameText, Text statusText)
        {
            Background = background;
            Accent = accent;
            NameText = nameText;
            StatusText = statusText;
        }

        public Image Background { get; }
        public Image Accent { get; }
        public Text NameText { get; }
        public Text StatusText { get; }
    }
}

[DefaultExecutionOrder(-130)]
public sealed class LocalCubeController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float jumpForce = 3f;
    [SerializeField] private float aimRotationSpeed = 18f;
    [SerializeField] private float runTargetDistance = 3f;
    [SerializeField] private float gaitTurnSpeedDegrees = 900f;

    [Header("Combat Setup")]
    [SerializeField] private Vector3 weaponHolderLocalPosition = new Vector3(0.24f, 0.16f, 0.3f);
    [SerializeField] private Vector3 attackPointLocalPosition = new Vector3(0f, 0.5f, 1.4f);
    [SerializeField] private float pickupRangeRadius = 1.5f;

    private Rigidbody body;
    private Transform cameraTransform;
    private bool isGrounded = true;
    private Vector3 previousPosition;
    private PlayerMouseAim mouseAim;
    private ProceduralPlayerRig rig;
    private Vector3 fallbackMoveDirection;
    private Vector3 latestAimPoint;
    private bool hasLatestAimPoint;
    private Vector3 heldGaitForward = Vector3.forward;
    private bool hasHeldGaitForward;

    public Vector3 Velocity { get; private set; }
    public ProceduralPlayerRig Rig => rig;
    public Transform TrackedTransform => rig != null ? rig.CoreNode : transform;

    public void Setup(Transform cameraTransform)
    {
        this.cameraTransform = cameraTransform;
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        rig = GetComponent<ProceduralPlayerRig>();
        if (rig == null)
        {
            rig = gameObject.AddComponent<ProceduralPlayerRig>();
        }

        rig.Configure(true);
        rig.ConfigureMovementSpeed(moveSpeed);

        if (body != null && rig.HasLegController)
        {
            body.isKinematic = true;
            body.useGravity = false;
            body.freezeRotation = true;
        }

        previousPosition = TrackedTransform.position;
        mouseAim = GetComponent<PlayerMouseAim>();

        SetupCombat();
    }

    private void Update()
    {
        UpdateProceduralTargets();
        HandleProceduralJumpInput();

        if (cameraTransform != null)
        {
            cameraTransform.position = TrackedTransform.position + new Vector3(0f, 8f, -9f);
            cameraTransform.rotation = Quaternion.Euler(45f, 0f, 0f);
        }
    }

    private void FixedUpdate()
    {
        if (mouseAim == null)
        {
            mouseAim = GetComponent<PlayerMouseAim>();
        }

        Keyboard keyboard = Keyboard.current;
        if (body == null)
        {
            return;
        }

        if (keyboard != null &&
            keyboard.spaceKey.wasPressedThisFrame &&
            (rig == null || !rig.HasLegController) &&
            isGrounded &&
            !body.isKinematic)
        {
            body.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
            isGrounded = false;
        }

        if (rig == null || !rig.HasLegController)
        {
            body.MovePosition(body.position + fallbackMoveDirection * moveSpeed * Time.fixedDeltaTime);
        }

        bool useFallbackPhysics = rig == null || !rig.HasLegController;

        if (hasLatestAimPoint && useFallbackPhysics)
        {
            Vector3 aimDirection = latestAimPoint - TrackedTransform.position;
            aimDirection.y = 0f;

            if (aimDirection.sqrMagnitude <= 0.001f)
            {
                aimDirection = fallbackMoveDirection;
            }

            if (aimDirection.sqrMagnitude > 0.001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(aimDirection, Vector3.up);
                body.MoveRotation(
                    Quaternion.Slerp(
                        body.rotation,
                        targetRotation,
                        aimRotationSpeed * Time.fixedDeltaTime
                    )
                );
            }
        }
        else if (fallbackMoveDirection.sqrMagnitude > 0.001f && useFallbackPhysics)
        {
            body.MoveRotation(Quaternion.LookRotation(fallbackMoveDirection));
        }

        Velocity = (TrackedTransform.position - previousPosition) / Time.fixedDeltaTime;
        previousPosition = TrackedTransform.position;
    }

    private void UpdateProceduralTargets()
    {
        if (rig == null)
        {
            return;
        }

        if (mouseAim == null)
        {
            mouseAim = GetComponent<PlayerMouseAim>();
        }

        hasLatestAimPoint = mouseAim != null && mouseAim.TryGetMouseWorldPoint(out latestAimPoint);
        if (hasLatestAimPoint)
        {
            rig.SetAimTarget(latestAimPoint);
        }

        Vector2 inputAxes = GetWasdAxes();
        bool shiftHeld = mouseAim != null && mouseAim.IsAimModifierPressed;
        Vector3 corePosition = rig.CoreNode.position;

        fallbackMoveDirection = Vector3.zero;

        if (!hasHeldGaitForward)
        {
            heldGaitForward = Vector3.ProjectOnPlane(rig.GaitForward, Vector3.up);
            if (heldGaitForward.sqrMagnitude <= 0.001f)
            {
                heldGaitForward = Vector3.forward;
            }

            heldGaitForward.Normalize();
            hasHeldGaitForward = true;
        }

        if (shiftHeld && hasLatestAimPoint)
        {
            Vector3 toMouse = Vector3.ProjectOnPlane(latestAimPoint - corePosition, Vector3.up);
            if (toMouse.sqrMagnitude > 0.04f)
            {
                heldGaitForward = Vector3.RotateTowards(
                    heldGaitForward,
                    toMouse.normalized,
                    gaitTurnSpeedDegrees * Mathf.Deg2Rad * Time.deltaTime,
                    0f
                ).normalized;
            }
        }

        Vector3 basisForward = Vector3.ProjectOnPlane(heldGaitForward, Vector3.up);
        if (basisForward.sqrMagnitude <= 0.001f)
        {
            basisForward = Vector3.forward;
        }

        basisForward.Normalize();
        rig.SetGaitForward(basisForward);

        if (inputAxes.sqrMagnitude > 0.001f)
        {
            Vector3 basisRight = Vector3.Cross(Vector3.up, basisForward).normalized;
            Vector3 moveDirection = basisRight * inputAxes.x + basisForward * inputAxes.y;

            if (moveDirection.sqrMagnitude > 0.001f)
            {
                moveDirection.Normalize();
                rig.SetRunTarget(corePosition + moveDirection * runTargetDistance);
                fallbackMoveDirection = moveDirection;
            }
            else
            {
                rig.SetRunTarget(corePosition);
            }
        }
        else
        {
            rig.SetRunTarget(corePosition);
        }

        bool hasWeapon = GetComponent<PlayerWeaponPickup>()?.HasWeapon ?? false;
        bool hasItem = GetComponent<PlayerItemPickup>()?.HasItem ?? false;

        ProceduralPlayerRig.CarryPose carryPose = hasWeapon
            ? ProceduralPlayerRig.CarryPose.OneHandWeapon
            : hasItem
                ? ProceduralPlayerRig.CarryPose.TwoHandItem
                : ProceduralPlayerRig.CarryPose.None;

        rig.ApplyCarryPose(carryPose);
        rig.UseLocalArmTargets();
    }

    private void HandleProceduralJumpInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || rig == null || !rig.HasLegController)
        {
            return;
        }

        if (keyboard.spaceKey.wasPressedThisFrame)
        {
            rig.RequestJump();
        }
    }

    private static Vector2 GetWasdAxes()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return Vector2.zero;
        }

        Vector2 axes = Vector2.zero;
        if (keyboard.wKey.isPressed) axes.y += 1f;
        if (keyboard.sKey.isPressed) axes.y -= 1f;
        if (keyboard.dKey.isPressed) axes.x += 1f;
        if (keyboard.aKey.isPressed) axes.x -= 1f;
        return axes.sqrMagnitude > 1f ? axes.normalized : axes;
    }

    private void SetupCombat()
    {
        Transform weaponHolder = rig != null && rig.WeaponHolder != null
            ? rig.WeaponHolder
            : CreateChildIfMissing("WeaponHolder", weaponHolderLocalPosition);

        Transform itemHolder = rig != null && rig.ItemHolder != null
            ? rig.ItemHolder
            : CreateChildIfMissing("ItemHolder", new Vector3(0.3f, 0.12f, 0.42f));

        Transform attackPoint = CreateChildIfMissing(
            "AttackPoint",
            attackPointLocalPosition
        );

        Transform pickupRange = CreateChildIfMissing(
            "PickupRange",
            Vector3.zero
        );

        SphereCollider pickupCollider = pickupRange.GetComponent<SphereCollider>();

        if (pickupCollider == null)
        {
            pickupCollider = pickupRange.gameObject.AddComponent<SphereCollider>();
        }

        pickupCollider.isTrigger = true;
        pickupCollider.radius = pickupRangeRadius;

        PlayerWeaponPickup weaponPickup = GetComponent<PlayerWeaponPickup>();

        if (weaponPickup == null)
        {
            weaponPickup = gameObject.AddComponent<PlayerWeaponPickup>();
        }

        if (GetComponent<PlayerCarryController>() == null)
        {
            gameObject.AddComponent<PlayerCarryController>();
        }

        PlayerItemPickup itemPickup = GetComponent<PlayerItemPickup>();

        if (itemPickup == null)
        {
            itemPickup = gameObject.AddComponent<PlayerItemPickup>();
        }

        if (mouseAim == null)
        {
            mouseAim = GetComponent<PlayerMouseAim>();
        }

        if (mouseAim == null)
        {
            mouseAim = gameObject.AddComponent<PlayerMouseAim>();
        }

        weaponPickup.Initialize(weaponHolder);
        itemPickup.Initialize(itemHolder);

        PlayerCombat combat = GetComponent<PlayerCombat>();

        if (combat == null)
        {
            combat = gameObject.AddComponent<PlayerCombat>();
        }

        int enemyLayerMask = LayerMask.GetMask("Enemy");

        if (enemyLayerMask == 0)
        {
            Debug.LogWarning("Enemy layer was not found. Create a layer named Enemy and assign it to your enemy.");
        }

        combat.Initialize(weaponPickup, attackPoint, enemyLayerMask);

        PlayerHealth health = GetComponent<PlayerHealth>();

        if (health == null)
        {
            health = gameObject.AddComponent<PlayerHealth>();
        }

        if (GetComponent<PrototypePlayerRespawn>() == null)
        {
            gameObject.AddComponent<PrototypePlayerRespawn>();
        }

        if (GetComponent<PlayerHealthBarUI>() == null)
        {
            gameObject.AddComponent<PlayerHealthBarUI>();
        }

        if (GetComponent<PlayerCrafting>() == null)
        {
            gameObject.AddComponent<PlayerCrafting>();
        }

        gameObject.tag = "Player";

        Debug.Log("Combat setup added to local multiplayer player with PlayerHealth.");
    }

    private Transform CreateChildIfMissing(string childName, Vector3 localPosition)
    {
        Transform existing = transform.Find(childName);

        if (existing != null)
        {
            existing.localPosition = localPosition;
            existing.localRotation = Quaternion.identity;
            existing.localScale = Vector3.one;
            return existing;
        }

        GameObject child = new GameObject(childName);
        child.transform.SetParent(transform);
        child.transform.localPosition = localPosition;
        child.transform.localRotation = Quaternion.identity;
        child.transform.localScale = Vector3.one;

        return child.transform;
    }

    private void OnCollisionEnter(Collision collision)
    {
        foreach (ContactPoint contact in collision.contacts)
        {
            if (contact.normal.y > 0.5f)
            {
                isGrounded = true;
                break;
            }
        }
    }
}

[DefaultExecutionOrder(-130)]
public sealed class RemoteCubeController : MonoBehaviour
{
    [SerializeField] private float interpolationSpeed = 12f;
    [SerializeField] private float snapDistance = 3f;

    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private ProceduralPlayerRig rig;
    private GameObject heldProp;
    private string heldPropKey = "none";

    public ProceduralPlayerRig Rig => rig;
    public Transform TrackedTransform => rig != null ? rig.CoreNode : transform;

    private void Awake()
    {
        rig = GetComponent<ProceduralPlayerRig>();
        if (rig == null)
        {
            rig = gameObject.AddComponent<ProceduralPlayerRig>();
        }

        rig.Configure(false);
        targetPosition = TrackedTransform.position;
        targetRotation = TrackedTransform.rotation;
    }

    public void ApplyState(PlayerStateDto state)
    {
        targetPosition = MultiplayerJson.ArrayToVector(state.position);
        targetRotation = Quaternion.Euler(MultiplayerJson.ArrayToVector(state.rotation));

        Vector3 moveTarget = state.moveTarget != null && state.moveTarget.Length >= 3
            ? MultiplayerJson.ArrayToVector(state.moveTarget)
            : targetPosition;

        Vector3 aimTarget = state.aimTarget != null && state.aimTarget.Length >= 3
            ? MultiplayerJson.ArrayToVector(state.aimTarget)
            : targetPosition + targetRotation * Vector3.forward;

        Vector3 gaitForward = state.gaitForward != null && state.gaitForward.Length >= 3
            ? MultiplayerJson.ArrayToVector(state.gaitForward)
            : Vector3.ProjectOnPlane(aimTarget - targetPosition, Vector3.up);

        rig.SetRunTarget(moveTarget);
        rig.SetAimTarget(aimTarget);
        if (gaitForward.sqrMagnitude > 0.001f)
        {
            rig.SetGaitForward(gaitForward.normalized);
        }

        rig.ApplyCarryPose(GetCarryPoseFromState(state));

        if (state.leftArmTarget != null && state.rightArmTarget != null)
        {
            rig.ApplyRemoteArmTargets(
                MultiplayerJson.ArrayToVector(state.leftArmTarget),
                MultiplayerJson.ArrayToVector(state.rightArmTarget)
            );
        }

        UpdateHeldProp(state.heldObjectType, state.heldItemType);
    }

    private static ProceduralPlayerRig.CarryPose GetCarryPoseFromState(PlayerStateDto state)
    {
        if (state == null || string.IsNullOrWhiteSpace(state.heldObjectType))
        {
            return ProceduralPlayerRig.CarryPose.None;
        }

        if (string.Equals(state.heldObjectType, "weapon", StringComparison.OrdinalIgnoreCase))
        {
            return ProceduralPlayerRig.CarryPose.OneHandWeapon;
        }

        if (string.Equals(state.heldObjectType, "item", StringComparison.OrdinalIgnoreCase))
        {
            return ProceduralPlayerRig.CarryPose.TwoHandItem;
        }

        return ProceduralPlayerRig.CarryPose.None;
    }

    private void Update()
    {
        if (rig != null && rig.HasLegController)
        {
            rig.ReconcileCoreToward(targetPosition, targetRotation, interpolationSpeed, snapDistance);
            return;
        }

        transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * interpolationSpeed);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * interpolationSpeed);
    }

    private void UpdateHeldProp(string heldObjectType, string heldItemType)
    {
        heldObjectType = string.IsNullOrWhiteSpace(heldObjectType) ? "none" : heldObjectType;
        heldItemType = string.IsNullOrWhiteSpace(heldItemType) ? "" : heldItemType;

        string nextKey = heldObjectType + ":" + heldItemType;
        if (string.Equals(nextKey, heldPropKey, StringComparison.Ordinal) && heldProp != null)
        {
            PlaceHeldProp(heldObjectType);
            return;
        }

        if (heldProp != null)
        {
            Destroy(heldProp);
            heldProp = null;
        }

        heldPropKey = nextKey;

        if (string.Equals(heldObjectType, "none", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        heldProp = CreateHeldProp(heldObjectType, heldItemType);
        PlaceHeldProp(heldObjectType);
    }

    private GameObject CreateHeldProp(string heldObjectType, string heldItemType)
    {
        GameObject prop;

        if (string.Equals(heldObjectType, "weapon", StringComparison.OrdinalIgnoreCase))
        {
            prop = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            prop.name = "Remote Spear Prop";
            prop.transform.localScale = new Vector3(0.055f, 0.55f, 0.055f);
            SetPropColor(prop, new Color(0.55f, 0.36f, 0.18f, 1f));
        }
        else if (string.Equals(heldItemType, "Rock", StringComparison.OrdinalIgnoreCase))
        {
            prop = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            prop.name = "Remote Rock Prop";
            prop.transform.localScale = Vector3.one * 0.22f;
            SetPropColor(prop, new Color(0.32f, 0.32f, 0.32f, 1f));
        }
        else
        {
            prop = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            prop.name = "Remote Item Prop";
            prop.transform.localScale = new Vector3(0.055f, 0.28f, 0.055f);
            SetPropColor(prop, new Color(0.48f, 0.3f, 0.13f, 1f));
        }

        Collider collider = prop.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }

        return prop;
    }

    private void PlaceHeldProp(string heldObjectType)
    {
        if (heldProp == null || rig == null)
        {
            return;
        }

        Transform holder = string.Equals(heldObjectType, "weapon", StringComparison.OrdinalIgnoreCase)
            ? rig.WeaponHolder
            : rig.ItemHolder;

        if (holder == null)
        {
            return;
        }

        heldProp.transform.SetParent(holder, false);
        heldProp.transform.localPosition = Vector3.zero;
        heldProp.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
    }

    private static void SetPropColor(GameObject prop, Color color)
    {
        Renderer renderer = prop != null ? prop.GetComponent<Renderer>() : null;
        if (renderer == null)
        {
            return;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader);
        material.color = color;
        renderer.material = material;
    }
}
