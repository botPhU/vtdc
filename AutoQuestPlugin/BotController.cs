using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;
using System;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using static BepInEx.Unity.IL2CPP.Utils.MonoBehaviourExtensions;

namespace AutoQuestPlugin
{
    // Farm State Machine
    public enum FarmState
    {
        IDLE,           // Đợi quest / chưa có nhiệm vụ
        PATHFINDING,    // Đang di chuyển đến NPC/target
        TALKING_NPC,    // Đang trong NPC dialog
        KILLING,        // Đang đánh quái (kill quest)
        COLLECTING,     // Đang thu thập (collect quest)
        RETURNING,      // Đang quay về NPC trả quest
        TURNING_IN,     // Đang trả quest (dialog)
        SEARCH_TARGET   // Đang tìm quái/NPC (Resume Quest)
    }

    /// <summary>
    /// BotController - Auto Login + Auto Quest + Auto Attack + Headless Mode
    /// 
    /// Hotkeys:
    ///   F1 = Bật/Tắt Auto Quest + Pathfind
    ///   F2 = Bật/Tắt Headless Mode
    ///   F3 = Bật/Tắt Auto Attack
    ///   F4 = Bật/Tắt Auto Interact NPC
    ///   F5 = Bật/Tắt Auto NPC Dialog
    ///   F11 = Force Auto Login
    ///   F12 = Log Player Info + Quest Status
    /// </summary>
    public class BotController : MonoBehaviour
    {
        public BotController(IntPtr ptr) : base(ptr) { }

        private int _consecutivePathfindFailures = 0; // Track pathfind failures
        // State — mỗi module có flag riêng
        private bool _autoQuestEnabled = false;    // F1: Auto Quest + Pathfind
        private bool _autoAttackFlag = false;      // F3: Auto Attack
        private bool _autoInteractFlag = false;    // F4: Auto Interact NPC
        private bool _autoDialogFlag = false;      // F5: Auto NPC Dialog
        private bool _headlessMode = false;
        private bool _liteMode = false; // Lite mode flag
        private float _statusLogTimer = 0f;
        private float _statusLogInterval = 30f;
        private string _currentScene = "";

        // Delayed find managers
        private float _findManagersTimer = -1f;
        private float _findManagersDelay = 5f;

        // Auto-login
        private float _autoLoginTimer = -1f;
        private float _autoLoginDelay = 0.5f; // Chờ 0.5 giây sau LoginScene load (nhanh nhất có thể)
        private bool _autoLoginDone = false;
        private string _loginUsername = "";
        private string _loginPassword = "";
        private int _loginServer = 0;
        private int _loginCharacter = 0;

        // Auto character select (sau khi login xong, tự chọn nhân vật vào game)
        private float _autoCharSelectTimer = -1f;
        private float _autoCharSelectDelay = 1f; // Chờ 1s sau login
        private bool _charSelectDone = false;
        private int _charSelectRetries = 0;
        private int _charSelectMaxRetries = 5; // Thử tối đa 5 lần
        private float _enterGameTimer = -1f;    // Timer click nút vào game sau khi chọn nhân vật

        // Graphics settings from config
        private bool _disableShadows = true;
        private bool _disableParticles = true;
        private bool _lowQuality = true;
        private int _targetFps = 10;
        private int _windowWidth = 0;
        private int _windowHeight = 0;
        private bool _configHeadless = false; // Headless từ account config
        private bool _autoCleanRAM = true;    // Tự dọn RAM định kỳ
        private bool _ultraLowRes = true;     // Giảm resolution tối đa khi headless

        // Phase 2 toggles from config
        private bool _configAutoQuest = true;    // Tự bật quest khi vào game
        private bool _configSmartDialog = true;  // Smart dialog (ưu tiên ! ?)
        private bool _configCollectQuest = true; // Collect quest auto interact
        private bool _configAutoPathfind = true; // Re-pathfind khi đóng dialog

        // Memory optimizer
        private float _memCleanTimer = 0f;
        private float _memCleanInterval = 60f; // Dọn RAM mỗi 60 giây
        private string _lastCleanScene = "";

        // Cached references — Managers
        private MonoBehaviour _autoMissionManager;
        
        // Auto Move Fields
        private QuestStep _currentQuestActionStep = null;
        private string _lastAnalyzedQuest = "";
        private HashSet<byte> _pressedKeys = new HashSet<byte>();
        private float _autoMoveCheckTimer = 0f;

        private MonoBehaviour _gameManager;
        private MonoBehaviour _appManager;
        private MonoBehaviour _playerDataManager;
        private MonoBehaviour _mainGame;  // MainGame trên SceneManager GO
        private bool _managersFound = false;
        private bool _guideErrorLogged = false;

        // Cached references — UI Buttons
        private Button _autoAttackBtn;
        private Button _interactBtn;
        private Button _shortMissionBtn;  // Button trên ShortMissionPanel
        private bool _autoAttackEnabled = false;

        // Cached references — Quest Panel
        private MonoBehaviour _shortMissionPanel;
        private string _lastQuestText = "";

        // State Observer (inline - no separate component needed)
        private string _stateLogPath;
        private readonly ConcurrentQueue<string> _stateLogQueue = new ConcurrentQueue<string>();
        private Thread _stateWriteThread;
        private volatile bool _stateLogRunning = false;
        private int _prevStateId = -1;
        private int _prevStepIndex = -1;
        private string _prevLocation = "";
        private string _prevActionTarget = "";

        // Enhanced State Tracking (Phase 1 - Manual Play Recording)
        private string _logPrevQuestText = "";
        private string _logPrevMapName = "";
        private bool _logPrevDialogOpen = false;
        private bool _isPlayerDead = false;
        private string _startupMap = "";
        private int _startupZone = -1;
        private bool _startupTeleportDone = false;
        private string _logPrevPanelState = "";
        private float _logEnhancedTimer = 0f;
        private string _lastStatStatus = ""; // To deduplicate STAT logs
        private string _lastActionTarget = ""; // Target for ResumeQuest logic

        // Auto-interact — smart click (chỉ click khi button MỚI xuất hiện)
        private float _interactCheckTimer = 0f;
        private float _interactCheckInterval = 0.5f; // Check nhanh mỗi 0.5s
        private bool _interactBtnWasVisible = false;  // Track trạng thái trước
        private float _questLogTimer = 0f;
        private float _questLogInterval = 15f;

        // Auto-pathfind timer (click quest panel khi nhân vật dừng lại)
        private float _pathfindTimer = 0f;
        private float _pathfindInterval = 8f;
        private float _pathfindCooldown = 0f;
        private float _pathfindCooldownTime = 15f;
        private float _idleThreshold = 5f; // Restored field
        private float _autoEnterGameTimer = -1f; // Restored field

        private bool _isInteracting = false;
        private bool _isFighting = false;
        
        // New Idle Check for Auto Quest
        private float _idleQuestTimer = 0f;
        private float _idleQuestThreshold = 3.0f; // 3 seconds idle -> trigger quest
        private GameObject _cachedLocalPlayer; // Cached player reference 

        // Player position tracking (phát hiện nhân vật đứng yên)
        private Vector3 _lastPlayerPos = Vector3.zero;
        
        // Anti-loop dialogue tracking
        private string _lastDialoguePanelName = "";
        private int _consecutiveDialogueClicks = 0;
        private float _idleTime = 0f;
        private float _moveCheckTimer = 0f; // Added field
        private float _moveCheckInterval = 0.5f;

        // NPC dialog timer
        private float _npcDialogTimer = 0f;
        private float _npcDialogInterval = 0.5f; // Check mỗi 0.5s (nhanh hơn để click hết hội thoại)
        private bool _npcDialogWasOpen = false; // Track dialog state để detect đóng
        private string _lastNpcDialogText = "";
        private string _lastClickedNpcButton = "";
        private int _npcDialogStuckCounter = 0;
        private const int MAX_NPC_DIALOG_STRICT_REPEATS = 4;

        // Quest change detection — dừng khi quest không đổi
        private string _trackedQuestText = "";
        private int _sameQuestPathfindCount = 0;
        private int _maxSameQuestPathfinds = 3; // Tối đa 3 lần click cùng quest
        private bool _isCollectQuest = false;    // Quest dạng "Thu thập" / "Nhặt"

        // Auto-enable quest timer (sau khi vào game tự động bật auto quest)
        private float _autoEnableQuestTimer = -1f;

        // === USER INTERACTION PAUSE ===
        private float _lastUserActionTime = 0f;
        private Vector3 _lastMousePos = Vector3.zero;

        // === ANTI-STUCK LOOP DETECTION ===
        private string _stuckCheckQuest = "";
        private int _stuckCounter = 0;
        private float _stuckCheckTimer = 0f;
        private const float STUCK_CHECK_INTERVAL = 5f; // Check every 5s
        private const int STUCK_THRESHOLD = 6; // 6 * 5s = 30s stuck -> Escape



        // Status file timer (ghi trạng thái ra file mỗi 5s để Launcher đọc)
        private float _statusFileTimer = 0f;
        private float _statusFileInterval = 5f;
        private string _statusFilePath = "";

        // Phase 3: Auto-dismiss popups (ReceiveGiftPanel, Backdrop, etc.)
        private float _popupDismissTimer = 0f;
        private float _popupDismissInterval = 2f; // Check mỗi 2s

        // Phase 3: Guide/Tutorial auto-dismiss
        private float _guideDismissTimer = 0f;
        private float _guideDismissInterval = 3f; // Check mỗi 3s
        private MonoBehaviour _guideManager;

        // Phase 3: Auto-revive detection
        private float _reviveCheckTimer = 0f;
        private float _reviveCheckInterval = 5f; // Check mỗi 5s
        private MonoBehaviour _autoAttackBlackBoard;
        private float _reviveCooldown = 0f; // Tránh spam revive

        // Zone UI References (Cached)
        private GameObject _zoneObject;
        private GameObject _mapNameObject;

        // Boss notification scanner
        private float _bossCheckTimer = 0f;
        private float _bossCheckInterval = 5f; // Scan mỗi 5s
        private string _lastBossNotification = "";
        private string _lastBossMap = "";
        private float _lastBossTime = 0f;
        private bool _killQuestDone = false; 
        
        // Combat Zone Detection
        private bool _inCombatZone = false;
        private float _zoneCheckTimer = 0f;

        // === MAP NAME DICTIONARY ===
        private readonly Dictionary<string, string> _sceneNameDict = new Dictionary<string, string>
        {
            { "MainGameScene", "Làng Aru" },
            { "NgoaiO", "Ngoại Ô" },
            { "KhuRungAru", "Rừng Aru" },
            { "ThanhPhoKaio", "Thành Phố Kaio" },
            // Add more as discovered
        };

        // === USER PAUSE HELPERS ===
        private bool GlobalPauseCheck()
        {
            // Detect input
            if (Input.anyKey || (Input.mousePosition - _lastMousePos).sqrMagnitude > 5.0f)
            {
                _lastUserActionTime = Time.time;
                _lastMousePos = Input.mousePosition;
            }

            // Strict Pause
            if (Time.time - _lastUserActionTime < 5f)
            {
                // NOTE: Removed NavMeshAgent.GetComponent call — it crashes IL2CPP interop
                // with TypeInitializationException every frame, killing the entire Update loop.
                return true; // PAUSED
            }
            return false; // ACTIVE
        }

        // ======================== FARM STATE MACHINE ========================
        private FarmState _farmState = FarmState.IDLE;
        private float _farmStateTimer = 0f;          // Thời gian ở state hiện tại
        private float _farmStuckTimer = 0f;           // Thời gian bị stuck (không di chuyển)
        private string _farmQuestAtStart = "";        // Quest text khi bắt đầu state
        private int _farmPathfindRetries = 0;          // Số lần retry pathfind
        private const int MAX_PATHFIND_RETRIES = 5;
        private const float STUCK_TIMEOUT = 15f;       // 15s không di chuyển = stuck
        private const float STATE_TIMEOUT = 120f;      // 2 phút timeout cho mỗi state
        private int _farmQuestsCompleted = 0;           // Đếm quest đã hoàn thành
        private float _farmTotalTime = 0f;             // Tổng thời gian farm

        // ======================== INPUT RECORDER ========================
        private GameObject _lastClickedObject = null;
        private float _inputRecordTimer = 0f;
        private readonly KeyCode[] _trackedKeys = new KeyCode[] {
            KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D,
            KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow,
            KeyCode.Space, KeyCode.Return, KeyCode.Escape,
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5,
            KeyCode.E, KeyCode.Q, KeyCode.R, KeyCode.T, KeyCode.Tab
        };
        private HashSet<int> _hookedBtnIds = new HashSet<int>();  // Track button đã hook
        private string _lastNpcName = "";  // Tên NPC đang tương tác
        private bool _botInvoking = false;  // Flag: true khi BOT gọi onClick.Invoke() (không phải user click)
        private float _lastUserActivityTime = 0f; // Track last manual interaction

        // AI Quest Classifier
        private QuestClassifier _questClassifier = null;
        private QuestInfo _currentQuestInfo = null;
        private string _prevClassifiedQuest = "";
        
        // Smart Dialog Sequence (from Analyzer DB)
        private List<string> _currentQuestDialogSequence = new List<string>();

        // Tutorial movement (khi ShortMissionPanel không khả dụng)
        private float _tutorialMoveTimer = 0f;
        private float _tutorialMoveInterval = 1.5f;  // Thử di chuyển mỗi 1.5s
        private int _tutorialMoveDir = 0;             // Hướng di chuyển (0=right, 1=up, 2=left, 3=down)

        // Tutorial skill press
        private float _skillPressTimer = 0f;
        private float _skillPressInterval = 2.0f;     // Bấm skill mỗi 2s

        // Command console (Launcher ↔ Bot communication)
        private float _cmdCheckTimer = 0f;
        private float _cmdCheckInterval = 2f; // Check mỗi 2s
        private string _commandFilePath = "";
        private string _responseFilePath = "";

        // Activity log ring buffer (for "log" command)
        private readonly List<string> _activityLog = new List<string>();
        private const int MAX_LOG_ENTRIES = 50;
        private string _activityLogFilePath = ""; // For live update in Launcher
        private float _gameSpeed = 1f;           // Current game speed (Time.timeScale)
        private float _periodicLogTimer = 0f;    // Timer for 1s periodic logging

        /// <summary>
        /// Log a bot activity to both BepInEx console, in-memory ring buffer, and file (for live dashboard)
        /// </summary>
        private void LogActivity(string msg)
        {
            try
            {
                if (string.IsNullOrEmpty(_loginUsername)) return;

                // Lazy init log path
                if (string.IsNullOrEmpty(_activityLogFilePath))
                {
                    string dllPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    string pluginDir = Path.GetDirectoryName(dllPath);
                    string statusDir = Path.Combine(pluginDir, "bot_status");
                    if (!Directory.Exists(statusDir)) Directory.CreateDirectory(statusDir);
                    _activityLogFilePath = Path.Combine(statusDir, $"activity_log_{_loginUsername}.txt");
                    // Create if not exists with empty content
                    if (!File.Exists(_activityLogFilePath))
                        File.WriteAllText(_activityLogFilePath, "");
                }

                string time = DateTime.Now.ToString("HH:mm:ss");
                string entry = $"[{time}] {msg}";
                Plugin.Log.LogInfo($"[Bot] {msg}");
                
                // 1. Memory buffer
                _activityLog.Add(entry);
                if (_activityLog.Count > MAX_LOG_ENTRIES)
                    _activityLog.RemoveAt(0);

                // 2. File append
                if (!string.IsNullOrEmpty(_activityLogFilePath))
                {
                    File.AppendAllText(_activityLogFilePath, entry + "\n");
                }
            } catch {}
        }

        /// <summary>
        /// Log an action to the STATE LOG file in a structured format.
        /// Enhanced format includes ZONE and PANELS for detailed quest flow analysis.
        /// </summary>
        private void LogStateAction(string action)
        {
            if (!_stateLogRunning) return;
            try
            {
                string time = DateTime.Now.ToString("HH:mm:ss.fff");
                string posStr = "0.0,0.0,0.0";
                try { 
                    var cam = Camera.main; 
                    if (cam != null) {
                        Vector3 p = cam.transform.position;
                        posStr = $"{p.x:F1},{p.y:F1},{p.z:F1}";
                    }
                } catch {}

                string map = "Unknown";
                try { map = GetCurrentMapName() ?? "Unknown"; } catch {}

                string zone = "Unknown";
                try { zone = GetCurrentZoneName() ?? "Unknown"; } catch {}

                string quest = "None";
                try { quest = GetCurrentQuestText() ?? "None"; } catch {}

                string panels = GetVisiblePanels();

                string autoQ = _autoQuestEnabled ? "ON" : "OFF";

                // Enhanced format for high-fidelity manual play recording
                string entry = $"[TIME:{time}] | [POS:{posStr}] | [LOC:{map}] | [ZONE:{zone}] | [AQ:{autoQ}] | [PANELS:{panels}] | [QUEST:{quest}] | [ACTION:{action}]";
                _stateLogQueue.Enqueue(entry);
            }
            catch { }
        }

        /// <summary>
        /// Detect which UI panels are currently visible — for quest flow analysis.
        /// </summary>
        private string GetVisiblePanels()
        {
            var visible = new System.Collections.Generic.List<string>();
            try
            {
                string[] panelNames = {
                    "NpcInteractPanel(Clone)", "NpcInteractPanel", "DialogPanel", "StoryPanel",
                    "DlgNpc", "ConfirmBox", "ShortMissionPanel", "ReceiveGiftPanel",
                    "CharacterChoosingPanel", "BossNotifPanel", "PanelMenu",
                    "SkillInfoPanel", "MapPanel", "InventoryPanel", "QuestPanel"
                };
                foreach (var name in panelNames)
                {
                    try
                    {
                        var obj = GameObject.Find(name);
                        if (obj != null && obj.activeInHierarchy)
                            visible.Add(name.Replace("(Clone)", ""));
                    }
                    catch { }
                }
            }
            catch { }
            return visible.Count > 0 ? string.Join(",", visible) : "None";
        }



        public void Start()
        {
            Plugin.Log.LogInfo("[BotController] Đang khởi tạo...");
            if (File.Exists(_stateLogPath)) File.Delete(_stateLogPath); // Init State Logger (inline, no AddComponent needed)
            try
            {
                string pluginDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                string stateDir = Path.Combine(pluginDir, "StateLogs");
                if (!Directory.Exists(stateDir)) Directory.CreateDirectory(stateDir);
                _stateLogPath = Path.Combine(stateDir, $"session_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                _stateLogRunning = true;
                _stateWriteThread = new Thread(StateLogWriteLoop) { IsBackground = true };
                _stateWriteThread.Start();
                Plugin.Log.LogInfo($"[StateObserver] ✅ Recording to: {_stateLogPath}");

                // Init Quest Classifier (học từ log cũ)
                _questClassifier = new QuestClassifier(pluginDir);
                
                // Học từ tất cả session log cũ
                int totalLearned = 0;
                try
                {
                    var logFiles = Directory.GetFiles(stateDir, "session_*.txt");
                    foreach (var logFile in logFiles)
                    {
                        if (logFile == _stateLogPath) continue; // Skip file đang ghi
                        int learned = _questClassifier.LearnFromLogFile(logFile);
                        totalLearned += learned;
                    }
                    Plugin.Log.LogInfo($"[QuestAI] ✅ Learned {totalLearned} patterns from {logFiles.Length} log files");
                    Plugin.Log.LogInfo($"[QuestAI] Stats: {_questClassifier.GetStats()}");

                    // Load Quest Database from Analyzer Output (NEW)
                    string dbPath = Path.Combine(pluginDir, "quest_db.txt");
                    if (File.Exists(dbPath))
                    {
                        QuestDatabase.LoadDatabase(dbPath);
                    }
                    else
                    {
                        Plugin.Log.LogWarning($"[AutoQuest] Database not found at: {dbPath}. Run analyzer first!");
                    }
                }
                catch (Exception lex)
                {
                    Plugin.Log.LogWarning($"[QuestAI] Learn from logs error: {lex.Message}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[StateObserver] ❌ Init Error: {ex.Message}");
            }

            Plugin.Log.LogInfo("[BotController] ================================");
            Plugin.Log.LogInfo("[BotController]   AUTO LOGIN từ accounts.json");
            Plugin.Log.LogInfo("[BotController]   F1 = AUTO ALL (Quest+Attack+Interact)");
            Plugin.Log.LogInfo("[BotController]   F2 = Headless Mode (Bật/Tắt)");
            Plugin.Log.LogInfo("[BotController]   F11 = Force Auto Login");
            Plugin.Log.LogInfo("[BotController]   F12 = Log Player + Quest Status");
            Plugin.Log.LogInfo("[BotController] ================================");

            LoadAccountConfig();
            ApplyGraphicsSettings();

            // Auto-enable headless nếu account config có headless=true
            if (_configHeadless && !_headlessMode)
            {
                Plugin.Log.LogInfo("[Headless] ★ Auto-enabled từ config!");
                EnableHeadless();
            }

            // Init status file path cho Launcher đọc
            try
            {
                string pluginDir = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                string statusDir = Path.Combine(pluginDir, "bot_status");
                if (!Directory.Exists(statusDir))
                    Directory.CreateDirectory(statusDir);
                string safeName = string.IsNullOrEmpty(_loginUsername) ? "default" : _loginUsername;
                _statusFilePath = Path.Combine(statusDir, $"status_{safeName}.json");
                _commandFilePath = Path.Combine(statusDir, $"cmd_{safeName}.txt");
                _responseFilePath = Path.Combine(statusDir, $"resp_{safeName}.txt");
                Plugin.Log.LogInfo($"[Bot] Status file: {_statusFilePath}");
                Plugin.Log.LogInfo($"[Bot] Command file: {_commandFilePath}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Bot] Status file init error: {ex.Message}");
            }
        }

        private float _resizeTimer = 0f;
        private float _nextResizeCheck = 0f;



        private void ApplyGraphicsSettings()
        {
            try
            {
                // === 1. Quality Level thấp nhất ===
                QualitySettings.SetQualityLevel(0, true); // Fastest
                Plugin.Log.LogInfo("[Gfx] ✅ QualityLevel = 0 (Fastest)");

                // === 2. Tắt shadow hoàn toàn ===
                QualitySettings.shadows = ShadowQuality.Disable;
                QualitySettings.shadowResolution = ShadowResolution.Low;
                QualitySettings.shadowDistance = 0;
                QualitySettings.shadowCascades = 0;

                // === 3. Giảm rendering ===
                QualitySettings.pixelLightCount = 0;
                QualitySettings.antiAliasing = 0;
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
                QualitySettings.softParticles = false;
                QualitySettings.realtimeReflectionProbes = false;
                QualitySettings.billboardsFaceCameraPosition = false;
                QualitySettings.skinWeights = SkinWeights.OneBone;
                QualitySettings.lodBias = 0.3f;
                QualitySettings.maximumLODLevel = 2;
                QualitySettings.particleRaycastBudget = 0;

                // === 4. Texture quality thấp nhất ===
                try { QualitySettings.globalTextureMipmapLimit = 3; }
                catch { try { QualitySettings.masterTextureLimit = 3; } catch { } }

                // === 5. Render distance ===
                QualitySettings.shadowDistance = 0;

                // === 6. FPS lock ===
                if (_targetFps > 0)
                {
                    Application.targetFrameRate = _liteMode ? Math.Min(15, _targetFps) : _targetFps;
                    QualitySettings.vSyncCount = 0;
                    Plugin.Log.LogInfo($"[Gfx] ✅ FPS: {Application.targetFrameRate} (Lite:{_liteMode}), VSync: OFF");
                }

                // === 7. Tắt ParticleSystems nếu cần ===
                if (_disableParticles || _liteMode)
                {
                    DisableAllParticles();
                }

                // === 8. Tắt đèn phụ (giữ lại main directional light) ===
                if (_liteMode) DisableExtraLights();

                // === 9. Lite Mode: Model Hiding & Extra CPU Savings ===
                if (_liteMode)
                {
                    ApplyLiteModeOptimizations();
                }

                Plugin.Log.LogInfo($"[Gfx] ✅ GraphicsSettings applied: {(_liteMode ? "LITE MODE" : "LOW QUALITY")}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Gfx] Apply error: {ex.Message}");
            }
        }

        private void ApplyLiteModeOptimizations()
        {
            try
            {
                Plugin.Log.LogInfo("[LiteMode] 🛠️ Applying aggressive optimizations...");
                
                // Hide other entities (Models)
                var renderers = GameObject.FindObjectsOfType<SkinnedMeshRenderer>();
                int meshCount = 0;
                foreach (var r in renderers)
                {
                    if (r == null || r.gameObject == null) continue;
                    
                    // Don't hide local player if we can identify it
                    string goName = r.gameObject.name;
                    if (goName.Contains("MainPlayer") || goName.Contains("Local") || (r.transform.root != null && r.transform.root.gameObject.name.Contains("Player")))
                        continue;

                    r.enabled = false;
                    meshCount++;
                }

                // Disable Animators (CPU heavy)
                var animators = GameObject.FindObjectsOfType<Animator>();
                int animCount = 0;
                foreach (var a in animators)
                {
                    if (a == null) continue;
                    
                    string goName = a.gameObject.name;
                    if (goName.Contains("MainPlayer") || goName.Contains("Local") || (a.transform.root != null && a.transform.root.gameObject.name.Contains("Player")))
                        continue;

                    a.enabled = false;
                    animCount++;
                }

                Plugin.Log.LogInfo($"[LiteMode] ✅ Hidden: {meshCount} meshes, {animCount} anims");

                // Auto-enable Auto Attack when entering Lite mode
                ToggleAutoAttack(true);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[LiteMode] Opt error: {ex.Message}"); }
        }

        private void ToggleLiteMode()
        {
            _liteMode = !_liteMode;
            Plugin.Log.LogInfo($"[LiteMode] 🔄 Toggled: {(_liteMode ? "✅ ON" : "❌ OFF")}");
            
            if (_liteMode)
            {
                _autoAttackFlag = true; // Auto-enable the logic flag
                ToggleAutoAttack(true); // Force the click
            }
            
            ApplyGraphicsSettings();
        }

        /// <summary>
        /// Tắt tất cả ParticleSystem trong scene (giảm lag đáng kể)
        /// </summary>
        private void DisableAllParticles()
        {
            try
            {
                var particles = GameObject.FindObjectsOfType<ParticleSystem>();
                int count = 0;
                foreach (var ps in particles)
                {
                    if (ps != null)
                    {
                        ps.Stop(true);
                        ps.Clear(true);
                        var renderer = ps.GetComponent<ParticleSystemRenderer>();
                        if (renderer != null) renderer.enabled = false;
                        count++;
                    }
                }
                if (count > 0)
                    Plugin.Log.LogInfo($"[Gfx] ✅ Particles: {count} disabled");
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[Gfx] Particles error: {ex.Message}"); }
        }

        /// <summary>
        /// Tắt tất cả đèn phụ, chỉ giữ 1 directional light chính
        /// </summary>
        private void DisableExtraLights()
        {
            try
            {
                var lights = GameObject.FindObjectsOfType<Light>();
                int disabled = 0;
                bool keptMain = false;
                foreach (var light in lights)
                {
                    if (light == null) continue;
                    // Giữ lại 1 directional light (main light)
                    if (!keptMain && light.type == LightType.Directional)
                    {
                        keptMain = true;
                        light.shadows = LightShadows.None; // Tắt shadow của main light
                        continue;
                    }
                    light.enabled = false;
                    disabled++;
                }
                if (disabled > 0)
                    Plugin.Log.LogInfo($"[Gfx] ✅ Lights: {disabled} disabled, kept 1 main");
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[Gfx] Lights error: {ex.Message}"); }
        }

        // ======================== LOAD CONFIG ========================
        // QUAN TRỌNG: Không dùng List<T> hay struct làm return type
        // vì IL2CPP sẽ strip method đó khi T là managed type

        private void LoadAccountConfig()
        {
            try
            {
                string pluginDir = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                string configPath = Path.Combine(pluginDir, "accounts.json");
                
                if (!File.Exists(configPath))
                {
                    Plugin.Log.LogWarning($"[Bot] accounts.json không tìm thấy: {configPath}");
                    return;
                }

                string json = File.ReadAllText(configPath);
                Plugin.Log.LogInfo($"[Bot] Đọc accounts.json ({json.Length} chars)");

                // Parse account index - prefer env var (for multi-instance support)
                int accIdx = 0;
                string envIdx = Environment.GetEnvironmentVariable("VTDC_ACCOUNT_INDEX");
                if (!string.IsNullOrEmpty(envIdx) && int.TryParse(envIdx, out int parsedIdx))
                {
                    accIdx = parsedIdx;
                    Plugin.Log.LogInfo($"[Bot] Account index from ENV: {accIdx}");
                }
                else
                {
                    accIdx = BotHelper.ParseJsonInt(json, "currentAccountIndex");
                    Plugin.Log.LogInfo($"[Bot] Account index from config: {accIdx}");
                }

                // Parse trực tiếp account thứ accIdx (inline, không dùng List<T>)
                int arrStart = json.IndexOf("\"accounts\"");
                if (arrStart < 0) { Plugin.Log.LogWarning("[Bot] Không tìm thấy 'accounts' trong JSON"); return; }
                arrStart = json.IndexOf('[', arrStart);
                if (arrStart < 0) return;

                int currentIdx = 0;
                int pos = arrStart;
                while (true)
                {
                    int objStart = json.IndexOf('{', pos);
                    if (objStart < 0) break;
                    int objEnd = json.IndexOf('}', objStart);
                    if (objEnd < 0) break;

                    if (currentIdx == accIdx)
                    {
                        string obj = json.Substring(objStart, objEnd - objStart + 1);
                        _loginUsername = BotHelper.ParseJsonString(obj, "username");
                        _loginPassword = BotHelper.ParseJsonString(obj, "password");
                        _loginServer = BotHelper.ParseJsonInt(obj, "server");
                        _loginCharacter = BotHelper.ParseJsonInt(obj, "character");
                        _configHeadless = BotHelper.ParseJsonBool(obj, "headless");
                        // Parse per-account autoQuest (overrides global settings)
                        if (obj.Contains("autoQuest"))
                        {
                            _configAutoQuest = obj.Contains("\"autoQuest\": true") || obj.Contains("\"autoQuest\":true");
                            Plugin.Log.LogInfo($"[Bot] ⚙️ Per-account autoQuest={_configAutoQuest}");
                        }
                        Plugin.Log.LogInfo($"[Bot] ✅ Config loaded: user={_loginUsername}, server={_loginServer}, char={_loginCharacter}, headless={_configHeadless}, autoQuest={_configAutoQuest}");

                        // Parse settings section (at root level)
                        int settingsStart = json.IndexOf("\"settings\"");
                        if (settingsStart >= 0)
                        {
                            int sObjStart = json.IndexOf('{', settingsStart);
                            int sObjEnd = json.IndexOf('}', sObjStart);
                            if (sObjStart >= 0 && sObjEnd >= 0)
                            {
                                string sObj = json.Substring(sObjStart, sObjEnd - sObjStart + 1);
                                _disableShadows = BotHelper.ParseJsonBool(sObj, "disableShadows");
                        _disableParticles = BotHelper.ParseJsonBool(sObj, "disableParticles");
                        _lowQuality = BotHelper.ParseJsonBool(sObj, "lowQuality");
                        _targetFps = BotHelper.ParseJsonInt(sObj, "targetFps");
                        if (_targetFps <= 0) _targetFps = 10;
                        _windowWidth = BotHelper.ParseJsonInt(sObj, "windowWidth");
                        _windowHeight = BotHelper.ParseJsonInt(sObj, "windowHeight");
                        _autoCleanRAM = BotHelper.ParseJsonBool(sObj, "autoCleanRAM");
                        _ultraLowRes = BotHelper.ParseJsonBool(sObj, "ultraLowRes");
                        _liteMode = BotHelper.ParseJsonBool(sObj, "liteMode");
                        // Global settings autoQuest is a FALLBACK — per-account value takes priority
                        if (!obj.Contains("autoQuest"))
                        {
                            _configAutoQuest = sObj.Contains("\"autoQuest\": true") || sObj.Contains("\"autoQuest\":true");
                            if (!sObj.Contains("autoQuest")) _configAutoQuest = true;
                        }
                        if (!sObj.Contains("smartDialog")) _configSmartDialog = true;
                        if (!sObj.Contains("collectQuest")) _configCollectQuest = true;
                        if (!sObj.Contains("autoPathfind")) _configAutoPathfind = true;
                        
                        _startupMap = BotHelper.ParseJsonString(sObj, "startupMap");
                        _startupZone = BotHelper.ParseJsonInt(sObj, "startupZone");
                        if (!sObj.Contains("startupZone")) _startupZone = -1;
                        Plugin.Log.LogInfo($"[Bot] ⚙️ Settings: shadows={!_disableShadows}, particles={!_disableParticles}, quality={(_lowQuality?"low":"default")}, fps={_targetFps}, window={_windowWidth}x{_windowHeight}");
                        Plugin.Log.LogInfo($"[Bot] ⚙️ Multi-acc: autoCleanRAM={_autoCleanRAM}, ultraLowRes={_ultraLowRes}");
                        Plugin.Log.LogInfo($"[Bot] ⚙️ Phase2: autoQuest={_configAutoQuest}, smartDialog={_configSmartDialog}, collectQuest={_configCollectQuest}, autoPathfind={_configAutoPathfind}");
                            }
                        }
                        return;
                    }
                    currentIdx++;
                    pos = objEnd + 1;

                    // Dừng khi gặp ']'
                    int nextBracket = json.IndexOf(']', pos);
                    int nextObj = json.IndexOf('{', pos);
                    if (nextBracket >= 0 && (nextObj < 0 || nextBracket < nextObj))
                        break;
                }
                Plugin.Log.LogWarning($"[Bot] Account index {accIdx} ngoài phạm vi (có {currentIdx} accounts)");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Bot] Lỗi đọc config: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // ======================== UPDATE ========================

        public void Update()
        {
            // === AUTO LOGIN LOGIC (BEFORE GlobalPause — login must always run!) ===
            if (_currentScene == "LoginScene")
            {
                if (_autoLoginTimer == -1f) _autoLoginTimer = Time.time;
                
                if (!_autoLoginDone && Time.time - _autoLoginTimer > _autoLoginDelay)
                {
                    HandleAutoLogin();
                    _autoLoginDone = true; 
                    _autoCharSelectTimer = Time.time;
                }
                
                if (_autoLoginDone && !_charSelectDone && Time.time - _autoCharSelectTimer > _autoCharSelectDelay)
                {
                     HandleCharacterSelect();
                }

                if (_charSelectDone)
                {
                    if (_autoEnterGameTimer == -1f) _autoEnterGameTimer = Time.time;
                    if (Time.time - _autoEnterGameTimer > 1f)
                    {
                        HandleEnterGame();
                        _autoEnterGameTimer = Time.time;
                    }
                }
            }

            // === 0. GLOBAL PAUSE CHECK (after login logic, so login always works) ===
            if (GlobalPauseCheck()) return;

            // === USER ACTIVITY CHECK ===
            try
            {
                if (Input.anyKey || (Input.mousePosition - _lastMousePos).sqrMagnitude > 1.0f)
                {
                    _lastUserActionTime = Time.time;
                    _lastMousePos = Input.mousePosition;
                }
            }
            catch {}

            // Resize check
            if (_windowWidth > 0 && _windowHeight > 0 && _resizeTimer < 10f)
            {
                _resizeTimer += Time.deltaTime;
                if (Time.time > _nextResizeCheck)
                {
                    _nextResizeCheck = Time.time + 1f;
                    if (Screen.fullScreen) { Screen.fullScreen = false; Screen.fullScreenMode = FullScreenMode.Windowed; }
                    if (Screen.width != _windowWidth || Screen.height != _windowHeight)
                    {
                        Screen.SetResolution(_windowWidth, _windowHeight, false);
                        Plugin.Log.LogInfo($"[Gfx] Enforcing resolution: {_windowWidth}x{_windowHeight}");
                    }
                }
            }

            // === SCENE CHANGE DETECTION ===
            string currentScene = SceneManager.GetActiveScene().name;
            if (currentScene != _currentScene)
            {
                _currentScene = currentScene;
                Plugin.Log.LogInfo($"[Bot] Scene changed: {_currentScene}");
                _managersFound = false;

                if (_headlessMode && _autoCleanRAM && _lastCleanScene != _currentScene)
                {
                    _lastCleanScene = _currentScene;
                    CleanMemory($"scene-change:{_currentScene}");
                }
                
                if (_currentScene == "LoginScene")
                {
                    if (_autoLoginDone && !string.IsNullOrEmpty(_loginUsername))
                    {
                        Plugin.Log.LogWarning("[Bot] ⚡ Phát hiện DISCONNECT → tự động đăng nhập lại!");
                        _autoLoginDone = false;
                        _charSelectDone = false;
                        _autoLoginTimer = -1f;
                        _autoEnterGameTimer = -1f;
                    }
                }
                else if (_currentScene == "MainGameScene")
                {
                    if (!_managersFound)
                    {
                        _findManagersTimer = 5f;
                        Plugin.Log.LogInfo("[Bot] 🔍 Scheduling FindManagers() in 5s...");
                    }
                    if (_autoEnableQuestTimer < 0 && _configAutoQuest)
                    {
                        _autoEnableQuestTimer = 8f;
                        Plugin.Log.LogInfo("[Bot] ⏰ Scheduling Auto Quest enable in 8s...");
                    }
                }
                ApplyGraphicsSettings();
            }

            // --- 1. Startup Teleport Logic ---
            CheckStartupTeleport();
            
            // --- IDLE CHECK FOR AUTO QUEST ---
            // Fix: If Auto Quest is active but player stands still for 3s -> Force click Quest Panel
            // --- IDLE CHECK FOR AUTO QUEST ---
            // Fix: If Auto Quest is active but player stands still for 3s -> Force click Quest Panel
            if (_autoQuestEnabled)
            {
                 // Try to find player if not cached
                 if (_cachedLocalPlayer == null) 
                 {
                     _cachedLocalPlayer = GameObject.Find("MainPlayer");
                     if (_cachedLocalPlayer == null) _cachedLocalPlayer = GameObject.Find("LocalPlayer");
                 }

                 if (_cachedLocalPlayer != null)
                 {
                     // Check if player moved (Interval Check for robustness)
                     _moveCheckTimer += Time.deltaTime;
                     if (_moveCheckTimer > 0.5f)
                     {
                         float movedDist = Vector3.Distance(_cachedLocalPlayer.transform.position, _lastPlayerPos);
                         _lastPlayerPos = _cachedLocalPlayer.transform.position; // Update for next interval
                         _moveCheckTimer = 0f;

                         // If moved > 0.1 in 0.5s -> active
                         bool isMoving = movedDist > 0.1f;
                         bool isActing = isMoving || _botInvoking || _isFighting || _isInteracting;

                         if (isActing)
                         {
                             _idleQuestTimer = 0f;
                         }
                         else
                         {
                             // Only increment timer if we are truly idle in this interval
                             // We add 0.5s to the timer since we checked over 0.5s
                             _idleQuestTimer += 0.5f; 
                         }
                     }

                     if (_idleQuestTimer > 5.0f) // Increased threshold to 5s
                     {
                         Plugin.Log.LogWarning($"[Bot] ⚠️ Detected IDLE for 5s while Auto Quest is ON. Triggering Quest Panel Update...");
                         TriggerAutoPathfind(true); // FORCE click to bypass kill quest check
                         _idleQuestTimer = 0f; // Reset to avoid spamming
                     }
                 }
            }
            else
            {
                _idleQuestTimer = 0f; // Reset if auto quest is off
            }
            
            // _enterGameTimer handled by AUTO LOGIN LOGIC block above

            // Timer auto-enable quest (sau khi vào game)
            if (_autoEnableQuestTimer > 0)
            {
                _autoEnableQuestTimer -= Time.deltaTime;
                if (_autoEnableQuestTimer <= 0)
                {
                    _autoEnableQuestTimer = -1f;
                    if (_configAutoQuest)
                    {
                        Plugin.Log.LogInfo("[Bot] ⏰ Auto-Enable Quest (Start)");
                        if (!_autoQuestEnabled) ToggleAutoAll();
                    }
                }
            }

            bool inGame = _currentScene != null && _currentScene != "LoginScene" && _currentScene != "InitScene";

            // Helper timers
            if (Time.time - _zoneCheckTimer > 2.0f)
            {
                UpdateZoneStatus();
                _zoneCheckTimer = Time.time;
            }

            // === ZONE AUTO ATTACK TRIGGER ===
            // Bật auto attack nếu đang auto quest (ở combat zone) HOẶC đang bật LiteMode
            // Guard: only try if _mainGame has been found (via FindManagers)
            if (inGame && _mainGame != null && ((_autoQuestEnabled && _inCombatZone) || _liteMode))
            {
                EnsureAutoAttack();
            }

            // === PERIODIC FINDMANAGERS RETRY ===
            // If managers not found yet and we've been in-game for a while, retry
            if (inGame && !_managersFound && _findManagersTimer < 0 && Time.time > 15f)
            {
                _findManagersTimer = 3f;
                Plugin.Log.LogInfo("[Bot] 🔄 Retrying FindManagers() (not found yet)...");
            }

                if (_findManagersTimer > 0)
                {
                    _findManagersTimer -= Time.deltaTime;
                    if (_findManagersTimer <= 0)
                    {
                        _findManagersTimer = -1f;
                        FindManagers();
                    }
                }

            // ======================== HOTKEYS ========================
            // F1 disabled — dùng Phase 2 config thay vì hotkey
            // if (Input.GetKeyDown(KeyCode.F1))
            //     ToggleAutoAll();
            if (Input.GetKeyDown(KeyCode.F2))
                ToggleHeadless();
            if (Input.GetKeyDown(KeyCode.F3))
                ToggleAutoAttackKey();  // Attack riêng
            if (Input.GetKeyDown(KeyCode.F4))
                ToggleAutoInteractKey(); // Interact riêng
            if (Input.GetKeyDown(KeyCode.F5))
                ToggleAutoDialogKey();   // NPC Dialog riêng
            if (Input.GetKeyDown(KeyCode.F6))
            {
                ResumeQuest(); // Auto-Resume based on Quest Text
            }
            if (Input.GetKeyDown(KeyCode.F7))
            {
                _gameSpeed = (_gameSpeed >= 3f) ? 1f : _gameSpeed + 1f;
                Time.timeScale = _gameSpeed;
                LogActivity($"⚡ Game Speed: x{_gameSpeed}");
            }
            if (Input.GetKeyDown(KeyCode.F11))
            {
                _autoLoginDone = false;
                HandleAutoLogin();
            }
            if (Input.GetKeyDown(KeyCode.F12))
            {
                LogPlayerInfo();
                LogQuestStatus();
            }
            if (Input.GetKeyDown(KeyCode.F8))
            {
                ToggleLiteMode();
            }

            // ======================== PERIODIC STATUS ========================
            _statusLogTimer += Time.deltaTime;
            if (_statusLogTimer >= _statusLogInterval && _autoQuestEnabled)
            {
                _statusLogTimer = 0f;
                LogAutoQuestStatus();
            }

            // ======================== PLAYER POSITION TRACKING ========================
            // Luôn track vị trí (dùng chung cho pathfind + interact)
            if (inGame)
            {
                _moveCheckTimer += Time.deltaTime;
                if (_moveCheckTimer >= _moveCheckInterval)
                {
                    _moveCheckTimer = 0f;
                    Vector3 currentPos = Vector3.zero;
                    try
                    {
                        var mainCam = Camera.main;
                        if (mainCam != null)
                            currentPos = mainCam.transform.position;
                    }
                    catch { }

                    float dist = Vector3.Distance(currentPos, _lastPlayerPos);
                    if (dist > 0.1f) _consecutivePathfindFailures = 0; // Reset fail count when moving

                    if (dist < 0.1f)
                    {
                        _idleTime += _moveCheckInterval;
                        if (_idleTime == _idleThreshold) // Log once when becoming idle
                            LogStateAction($"IDLE at {currentPos.x:F1},{currentPos.y:F1},{currentPos.z:F1}");
                    }
                    else
                    {
                        if (_idleTime >= _idleThreshold) // Was idle, now moving
                            LogStateAction($"MOVING from {_lastPlayerPos.x:F1},{_lastPlayerPos.y:F1},{_lastPlayerPos.z:F1}");
                        _idleTime = 0f;
                    }
                    _lastPlayerPos = currentPos;
                }
            }

            // ======================== PERIODIC STATE LOGGING (Reduced Spam) ========================
            if (inGame && _stateLogRunning)
            {
                _periodicLogTimer += Time.deltaTime;
                string currentStatus = (_idleTime >= _idleThreshold) ? "IDLE" : "RUNNING";
                
                // Log if status changed OR every 5 seconds (heartbeat)
                if (currentStatus != _lastStatStatus || _periodicLogTimer >= 5.0f)
                {
                    _periodicLogTimer = 0f;
                    _lastStatStatus = currentStatus;
                    LogStateAction($"STAT: {currentStatus}"); 
                }
            }

            // ======================== ENHANCED TRANSITION DETECTION (Always-on Recording) ========================
            if (inGame && _stateLogRunning)
            {
                // --- PER-FRAME: Keyboard Input Logging ---
                try
                {
                    foreach (var key in _trackedKeys)
                    {
                        if (Input.GetKeyDown(key))
                        {
                            LogStateAction($"KEY_PRESS: {key}");
                        }
                    }
                    // Mouse click logging (left click) — UI + 3D World
                    if (Input.GetMouseButtonDown(0))
                    {
                        string clickTarget = "Screen";
                        bool hitUI = false;
                        try
                        {
                            var eventSys = EventSystem.current;
                            if (eventSys != null && eventSys.currentSelectedGameObject != null)
                            {
                                hitUI = true;
                                var selObj = eventSys.currentSelectedGameObject;
                                string btnText = "";
                                try
                                {
                                    var tmp = selObj.GetComponentInChildren<TextMeshProUGUI>();
                                    if (tmp != null) btnText = tmp.text ?? "";
                                }
                                catch { }
                                clickTarget = $"{selObj.name}" + (string.IsNullOrEmpty(btnText) ? "" : $" [{btnText}]");
                            }
                        }
                        catch { }
                        
                        // 3D World raycast — detect boss/NPC/entity clicks
                        if (!hitUI)
                        {
                            try
                            {
                                var cam = Camera.main;
                                if (cam != null)
                                {
                                    Ray ray = cam.ScreenPointToRay(Input.mousePosition);
                                    RaycastHit hit;
                                    if (Physics.Raycast(ray, out hit, 100f))
                                    {
                                        var hitObj = hit.collider.gameObject;
                                        string objName = hitObj.name ?? "?";
                                        string tag = "";
                                        try { tag = hitObj.tag ?? ""; } catch { }
                                        Vector3 hitPos = hit.point;
                                        clickTarget = $"3D:{objName} [tag:{tag}] at ({hitPos.x:F1},{hitPos.y:F1},{hitPos.z:F1})";
                                        
                                        // Boss/NPC detection keywords
                                        string nameLower = objName.ToLower();
                                        if (nameLower.Contains("boss") || nameLower.Contains("npc") ||
                                            nameLower.Contains("monster") || nameLower.Contains("enemy") ||
                                            nameLower.Contains("mob") || tag == "Enemy" || tag == "NPC")
                                        {
                                            LogStateAction($"TARGET_CLICK: {objName} | Tag: {tag} | Pos: ({hitPos.x:F1},{hitPos.y:F1},{hitPos.z:F1})");
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                        LogStateAction($"MOUSE_CLICK: {clickTarget}");
                    }
                }
                catch { }

                // --- EVERY 0.5s: State Transition Checks ---
                _logEnhancedTimer += Time.deltaTime;
                if (_logEnhancedTimer >= 0.5f) // Check mỗi 0.5s
                {
                    _logEnhancedTimer = 0f;
                    try
                    {
                        // --- QUEST TRANSITION (with end_pos tracking) ---
                        string curQuest = "";
                        try { curQuest = GetCurrentQuestText() ?? ""; } catch { }
                        if (!string.IsNullOrEmpty(curQuest) && curQuest != _logPrevQuestText)
                        {
                            // Log END position of previous quest
                            if (!string.IsNullOrEmpty(_logPrevQuestText))
                            {
                                string endPos = "0,0,0";
                                string endMap = "?";
                                try {
                                    var cam = Camera.main;
                                    if (cam != null) {
                                        Vector3 p = cam.transform.position;
                                        endPos = $"{p.x:F1},{p.y:F1},{p.z:F1}";
                                    }
                                } catch {}
                                try { endMap = GetCurrentMapName() ?? "?"; } catch {}
                                LogStateAction($"QUEST_END_POS: {endPos} | Map: {endMap} | Quest: {_logPrevQuestText}");
                            }

                            if (string.IsNullOrEmpty(_logPrevQuestText))
                                LogStateAction($"QUEST_START: {curQuest}");
                            else
                                LogStateAction($"QUEST_CHANGED: [{_logPrevQuestText}] → [{curQuest}]");
                            _logPrevQuestText = curQuest;
                        }

                        // --- MAP TRANSITION ---
                        string curMap = "";
                        try { curMap = GetCurrentMapName() ?? ""; } catch { }
                        if (!string.IsNullOrEmpty(curMap) && curMap != _logPrevMapName)
                        {
                            if (!string.IsNullOrEmpty(_logPrevMapName))
                                LogStateAction($"MAP_CHANGED: [{_logPrevMapName}] → [{curMap}]");
                            _logPrevMapName = curMap;
                        }

                        // --- DIALOG/PANEL OPEN/CLOSE + AUTO-HOOK BUTTONS ---
                        bool dialogOpen = false;
                        try { dialogOpen = IsDialogueOpen(); } catch { }
                        if (dialogOpen != _logPrevDialogOpen)
                        {
                            if (dialogOpen)
                            {
                                string panels = GetVisiblePanels();
                                LogStateAction($"DIALOG_OPENED: {panels}");

                                // AUTO-HOOK: Tự động hook buttons để log user clicks
                                try
                                {
                                    string[] dialogPanelNames = {
                                        "NpcInteractPanel(Clone)", "NpcInteractPanel",
                                        "DialogPanel", "StoryPanel", "DlgNpc",
                                        "ConfirmBox", "PanelMenu"
                                    };
                                    foreach (var pName in dialogPanelNames)
                                    {
                                        var panel = GameObject.Find(pName);
                                        if (panel != null && panel.activeInHierarchy)
                                        {
                                            HookDialogButtons(panel);
                                            // Log all available buttons in panel
                                            var allBtns = panel.GetComponentsInChildren<Button>(true);
                                            foreach (var b in allBtns)
                                            {
                                                if (b == null || !b.gameObject.activeSelf) continue;
                                                string bName = b.gameObject.name ?? "?";
                                                string bText = BotHelper.btnTextFromButton(b);
                                                LogStateAction($"NPC_BTN_AVAILABLE: {bName} | Text: {bText} | Panel: {pName}");
                                            }
                                        }
                                    }
                                }
                                catch { }
                            }
                            else
                            {
                                LogStateAction("DIALOG_CLOSED");
                            }
                            _logPrevDialogOpen = dialogOpen;
                        }

                        // --- VISIBLE PANELS CHANGED ---
                        string curPanels = GetVisiblePanels();
                        if (curPanels != _logPrevPanelState)
                        {
                            if (_logPrevPanelState != "" && _logPrevPanelState != "None")
                                LogStateAction($"PANELS_CHANGED: [{_logPrevPanelState}] → [{curPanels}]");
                            _logPrevPanelState = curPanels;
                        }
                    }
                    catch { }
                }
            }

            // ======================== FARM STATE MACHINE ========================
            if (_autoQuestEnabled && inGame)
            {
                _farmStateTimer += Time.deltaTime;
                _farmTotalTime += Time.deltaTime;
                UpdateFarmState();
            }

            // ======================== MODULE 1: AUTO QUEST + PATHFIND (F1) ========================
            if (_autoQuestEnabled && inGame)
            {
                // Pathfind: CHỈ click khi idle + cooldown xong + quest chưa bị lặp
                if (_pathfindCooldown > 0)
                    _pathfindCooldown -= Time.deltaTime;

                // Skip pathfind nếu NPC dialog đang mở
                bool dialogOpen = IsDialogueOpen();

                _pathfindTimer += Time.deltaTime;
                // Fix: Nếu _killQuestDone (vừa đánh xong quái), cho phép pathfind ngay cả khi chưa idle (đang chạy/đánh)
                // NEW: Skip pathfind nếu quest đang là PRESS_MOVEMENT_KEYS (tutorial) hoặc PRESS_SKILL_KEY
                bool isSpecialAction = _currentQuestInfo != null && 
                                      (_currentQuestInfo.Action == QuestAction.PRESS_MOVEMENT_KEYS || 
                                       _currentQuestInfo.Action == QuestAction.PRESS_SKILL_KEY);

                if (_pathfindTimer >= _pathfindInterval && (_idleTime >= _idleThreshold || _killQuestDone) 
                    && _pathfindCooldown <= 0 && !dialogOpen && !isSpecialAction)
                {
                    _pathfindTimer = 0f;

                    // === NEW: CHECK FOR DIRECT MOVEMENT (MoveToMap) ===
                    bool handled = false;
                    try
                    {
                        var questText = GetCurrentQuestText();
                        // Update Analysis if quest changed
                        if (questText != _lastAnalyzedQuest)
                        {
                            _lastAnalyzedQuest = questText;
                            if (!string.IsNullOrEmpty(questText))
                                _currentQuestActionStep = QuestDatabase.GetActionForQuest(questText);
                            else
                                _currentQuestActionStep = null;
                        }

                        // Execute Direct Move if applicable
                        if (_currentQuestActionStep != null && _currentQuestActionStep.Action == ActionType.MoveToMap)
                        {
                            ProcessAutoMovement(_currentQuestActionStep.TargetPos);
                            handled = true;
                        }
                    }
                    catch { }

                    if (!handled)
                    {
                        // Stop any lingering movement keys
                        ReleaseAllMovementKeys();
                        TriggerAutoPathfind();
                    }
                }
                else if (_autoQuestEnabled && _currentQuestActionStep != null && _currentQuestActionStep.Action == ActionType.MoveToMap)
                {
                    // DIRECT MOVE: Must run every frame (or frequent check) for smooth movement
                    // The pathfindTimer check above is too slow (5s).
                    // We need a faster timer for movement loop.
                }

                // === CONTINUOUS MOVEMENT LOOP (Outside Interval) ===
                if (_autoQuestEnabled && inGame && !dialogOpen && _currentQuestActionStep != null && _currentQuestActionStep.Action == ActionType.MoveToMap)
                {
                    _autoMoveCheckTimer += Time.deltaTime;
                     // Run movement logic every 0.1s
                    if (_autoMoveCheckTimer >= 0.1f)
                    {
                         _autoMoveCheckTimer = 0f;
                         ProcessAutoMovement(_currentQuestActionStep.TargetPos);
                    }
                }
                else
                {
                    // Ensure keys are released if not moving
                   if (_pressedKeys.Count > 0) ReleaseAllMovementKeys();
                }


                // Collect quest: click interact khi idle (quest "Thu thập" / "Nhặt")
                if (_isCollectQuest && _idleTime >= 2f && !dialogOpen)
                {
                    try
                    {
                        if (_interactBtn == null)
                        {
                            var go = GameObject.Find("InteractButton");
                            if (go != null)
                            {
                                _interactBtn = go.GetComponent<Button>();
                                if (_interactBtn == null)
                                    _interactBtn = go.GetComponentInChildren<Button>();
                            }
                        }
                        if (_interactBtn != null && _interactBtn.gameObject.activeSelf && _interactBtn.interactable)
                        {
                            _interactBtn.onClick.Invoke();
                            _idleTime = 0f;
                            Plugin.Log.LogInfo("[Bot] 📦 [QUEST] Collect quest: Clicked InteractButton (idle)");
                            LogStateAction("CLICK InteractButton (idle collect)");
                        }
                    }
                    catch { }
                }

                // Log quest status định kỳ
                _questLogTimer += Time.deltaTime;
                if (_questLogTimer >= _questLogInterval)
                {
                    _questLogTimer = 0f;
                    LogQuestStatus();
                }
            }

            // ======================== MODULE 1B: TUTORIAL MOVEMENT (AI-driven) ========================
            // Khi quest là MOVE nhưng ShortMissionPanel không hoạt động (tutorial đầu game)
            if (_autoQuestEnabled && inGame && _currentQuestInfo != null 
                && _currentQuestInfo.Action == QuestAction.PRESS_MOVEMENT_KEYS)
            {
                _tutorialMoveTimer += Time.deltaTime;
                if (_tutorialMoveTimer >= _tutorialMoveInterval)
                {
                    _tutorialMoveTimer = 0f;
                    // Force using Key Press (TryTutorialMovement) - không thử ShortMissionPanel nữa vì nó là trap ở tutorial
                    TryTutorialMovement();
                }
            }
            
            // ======================== MODULE 1C: WAIT USER / CLICK ITEM ========================
            if (_autoQuestEnabled && inGame && _currentQuestInfo != null)
            {
                if (_currentQuestInfo.Action == QuestAction.WAIT_USER)
                {
                    if (_idleTime > 5f) LogStateAction("WAIT_USER: Please perform action manually...");
                }
                else if (_currentQuestInfo.Action == QuestAction.CLICK_ITEM)
                {
                    // TODO: Implement Inventory Click
                    if (_idleTime > 5f) LogStateAction("CLICK_ITEM: Auto-click item not implemented. Please use item.");
                }
            }

            // ======================== MODULE 1C: WAIT USER / CLICK ITEM ========================
            if (_autoQuestEnabled && inGame && _currentQuestInfo != null)
            {
                if (_currentQuestInfo.Action == QuestAction.WAIT_USER)
                {
                    if (_idleTime > 5f) LogStateAction("WAIT_USER: Please perform action manually...");
                }
                else if (_currentQuestInfo.Action == QuestAction.CLICK_ITEM)
                {
                    // TODO: Implement Inventory Click
                    if (_idleTime > 5f) LogStateAction("CLICK_ITEM: Auto-click item not implemented. Please use item.");
                }
            }

            // ======================== MODULE 1C: AUTO SKILL QUEST (AI-driven) ========================
            // Khi quest là USE_ITEM và yêu cầu bấm phím kỹ năng (Dùng Thẻ Kỹ Năng X)
            if (_autoQuestEnabled && inGame && _currentQuestInfo != null 
                && _currentQuestInfo.Action == QuestAction.PRESS_SKILL_KEY)
            {
                _skillPressTimer += Time.deltaTime;
                if (_skillPressTimer >= _skillPressInterval)
                {
                    _skillPressTimer = 0f;
                    
                    // Parse skill number from quest text ("...Kỹ Năng 2" -> 2)
                    int skillNum = 0;
                    var match = System.Text.RegularExpressions.Regex.Match(_currentQuestInfo.QuestText, @"\d+");
                    if (match.Success)
                    {
                        int.TryParse(match.Value, out skillNum);
                    }

                    if (skillNum >= 1 && skillNum <= 5)
                    {
                        // Map 1-5 to Alpha1-Alpha5
                        KeyCode[] skillKeys = { KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5 };
                        KeyCode key = skillKeys[skillNum - 1];
                        
                        // Simulate key press logic (log only for now, actual input via Input is read-only)
                        // BotController doesn't have a way to inject key press into Unity Input system directly easily
                        // BUT we can use the InputRecorder's replay mechanism OR just Log it and hope user does it?
                        // NO, user expects automation.
                        // We can't easily simulate Input.GetKeyDown in Unity without an external tool or rigorous hooking.
                        // However, many games use UI buttons for skills on mobile/PC. 
                        // Let's try to find the Skill Button UI and click it!
                        
                        string btnName = $"SkillButton{skillNum}"; // Hypothesized name
                        // Or "Skill_{skillNum}", "AttackButton_{skillNum}" check hierarchy
                        
                        bool clicked = false;
                        // Strategy: Find explicit skill buttons if possible, otherwise rely on user? 
                        // User said "dùng các nút ấy chứ không phải click" which implies interacting with the controls.
                        // Wait, user said "dùng các nút ấy" (use those buttons) "chữ không phải click" (not click [ShortMission?]).
                        // It might mean PRESS KEYBOARD BUTTONS. 
                        // Since we can't inject StartCoroutine(SimulateKeyPress) easily safely...
                        // Actually, we can click the UI button corresponding to the skill.
                        
                        // Let's try to find UI buttons for skills: "SkillButton_1", "Slot_1", etc.
                        // For now, let's look for objects with "Skill" and the number.
                        
                        // Common names: SkillSlot_1, Skill_1, etc.
                        // Let's create a helper to find and click skill button.
                        TryClickSkillButton(skillNum);
                    }
                }
            }

            // ======================== MODULE 1D: AUTO TAME / WORLD INTERACT (AI-driven) ========================
            // Handle quests like "Thuần phục Đại Vương Oloong" that require clicking the 3D character
            if (_autoQuestEnabled && inGame && _currentQuestInfo != null && 
                (_currentQuestInfo.QuestText.Contains("Thuần phục") || _currentQuestInfo.QuestText.Contains("thuần phục")))
            {
                _tameInteractTimer += Time.deltaTime;
                if (_tameInteractTimer >= 1.0f) // Check every 1s
                {
                    _tameInteractTimer = 0f;
                    TryTameInteraction();
                }
            }

            // ======================== MODULE 2: AUTO INTERACT (F4) ========================
            if (_autoInteractFlag && inGame)
            {
                _interactCheckTimer += Time.deltaTime;
                if (_interactCheckTimer >= _interactCheckInterval)
                {
                    _interactCheckTimer = 0f;
                    TryAutoInteract();
                }
            }

            // ======================== MODULE 3: AUTO NPC DIALOG (F5) ========================
            if (_autoDialogFlag && inGame)
            {
                _npcDialogTimer += Time.deltaTime;
                if (_npcDialogTimer >= _npcDialogInterval)
                {
                    _npcDialogTimer = 0f;
                    TryClickNpcDialog();
                }

                // Detect dialog close → re-trigger pathfind
                bool dialogNow = false;
                try
                {
                    var npcPanelNow = GameObject.Find("NpcInteractPanel(Clone)");
                    dialogNow = npcPanelNow != null && npcPanelNow.activeSelf && npcPanelNow.activeInHierarchy;
                }
                catch { }

                if (_npcDialogWasOpen && !dialogNow && _autoQuestEnabled)
                {
                    // Dialog vừa đóng → force pathfind ngay
                    _pathfindTimer = _pathfindInterval;
                    _sameQuestPathfindCount = 0;
                    _trackedQuestText = "";
                    _idleTime = _idleThreshold; // Force idle để pathfind trigger ngay
                    Plugin.Log.LogInfo("[Bot] 💬 NPC Dialog đóng → re-trigger pathfind!");
                    LogStateAction("NPC_DIALOG_CLOSED → re-trigger pathfind");
                }

                // *** NPC INTERACTION RECORDER ***
                if (!_npcDialogWasOpen && dialogNow)
                {
                    // Dialog vừa mở → ghi lại NPC info + hook button clicks
                    _hookedBtnIds.Clear();
                    try
                    {
                        var npcPanel2 = GameObject.Find("NpcInteractPanel(Clone)");
                        if (npcPanel2 != null)
                        {
                            string npcName = "unknown";
                            string dialogText = "";

                            // Tìm tất cả text trong dialog
                            var allTexts = npcPanel2.GetComponentsInChildren<TextMeshProUGUI>(true);
                            foreach (var t in allTexts)
                            {
                                if (t == null) continue;
                                string txt = t.text ?? "";
                                if (string.IsNullOrWhiteSpace(txt)) continue;
                                string goName = t.gameObject.name ?? "";
                                
                                if (goName.Contains("Name") || goName.Contains("name") || goName.Contains("Title"))
                                    npcName = txt.Trim();
                                else if (goName.Contains("Content") || goName.Contains("Dialog") || goName.Contains("Talk"))
                                    dialogText = txt.Trim();
                            }

                            _lastNpcName = npcName;
                            LogStateAction($"NPC_DIALOG_OPENED: NPC={npcName} | Dialog={dialogText}");

                            // Hook onClick trên MỌI button trong dialog
                            HookDialogButtons(npcPanel2);
                        }
                    }
                    catch { }
                }
                // Khi dialog đang mở: liên tục hook button mới (dialog có thể thêm button)
                else if (dialogNow)
                {
                    try
                    {
                        var npcPanel3 = GameObject.Find("NpcInteractPanel(Clone)");
                        if (npcPanel3 != null) HookDialogButtons(npcPanel3);
                    }
                    catch { }
                }

                _npcDialogWasOpen = dialogNow;
            }

            // ======================== MODULE 4: AUTO-DISMISS POPUPS (Phase 3) ========================
            // Update user activity time if any input detected
            if (Input.anyKey || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
            {
                _lastUserActivityTime = Time.time;
            }

            bool isUserBusy = (Time.time - _lastUserActivityTime < 5.0f);

            if (_autoDialogFlag && inGame && !isUserBusy)
            {
                _popupDismissTimer += Time.deltaTime;
                if (_popupDismissTimer >= _popupDismissInterval)
                {
                    _popupDismissTimer = 0f;
                    TryDismissPopups();
                }
            }

            // ======================== MODULE 5: GUIDE/TUTORIAL DISMISS (Phase 3) ========================
            if (_autoQuestEnabled && inGame && !isUserBusy)
            {
                _guideDismissTimer += Time.deltaTime;
                if (_guideDismissTimer >= _guideDismissInterval)
                {
                    _guideDismissTimer = 0f;
                    TryDismissGuide();
                }
            }

            // ======================== MODULE 6: AUTO-REVIVE (Phase 3) ========================
            if (_autoQuestEnabled && inGame)
            {
                if (_reviveCooldown > 0)
                    _reviveCooldown -= Time.deltaTime;

                _reviveCheckTimer += Time.deltaTime;
                if (_reviveCheckTimer >= _reviveCheckInterval)
                {
                    _reviveCheckTimer = 0f;
                    TryAutoRevive();
                }
            }

            // ======================== MODULE 7: BOSS NOTIFICATION SCANNER ========================
            if (inGame)
            {
                _bossCheckTimer += Time.deltaTime;
                if (_bossCheckTimer >= _bossCheckInterval)
                {
                    _bossCheckTimer = 0f;
                    ScanBossNotifications();
                }
            }

            // ======================== MODULE 8: COMMAND CONSOLE (Launcher ↔ Bot) ========================
            if (inGame && !string.IsNullOrEmpty(_commandFilePath))
            {
                _cmdCheckTimer += Time.deltaTime;
                if (_cmdCheckTimer >= _cmdCheckInterval)
                {
                    _cmdCheckTimer = 0f;
                    CheckAndProcessCommand();
                }
            }

            // Memory optimizer - dọn RAM định kỳ
            if (_autoCleanRAM && _headlessMode)
            {
                _memCleanTimer += Time.deltaTime;
                if (_memCleanTimer >= _memCleanInterval)
                {
                    _memCleanTimer = 0f;
                    CleanMemory("periodic");
                }
            }

            // Status file - ghi trạng thái ra file cho Launcher đọc
            _statusFileTimer += Time.deltaTime;
            if (_statusFileTimer >= _statusFileInterval)
            {
                _statusFileTimer = 0f;
                WriteStatusFile();
            }

            // ======================== MODULE 9: TELEPORT STATE MACHINE ========================
            if (inGame)
            {
                UpdateTeleport();
                
                // FIXED: Call Farm State Machine
                if (_autoQuestEnabled)
                {
                    UpdateFarmState();
                }
            }

            // ======================== INPUT RECORDER (ghi lại click + phím của người chơi) ========================
            if (_stateLogRunning && inGame)
            {
                try
                {
                    // --- 1. Mouse Click → UI Element ---
                    if (Input.GetMouseButtonDown(0))
                    {
                        try
                        {
                            var eventSystem = EventSystem.current;
                            if (eventSystem != null && eventSystem.IsPointerOverGameObject())
                            {
                                if (eventSystem.currentSelectedGameObject != null)
                                {
                                    var clicked = eventSystem.currentSelectedGameObject;
                                    if (clicked != _lastClickedObject)
                                    {
                                        _lastClickedObject = clicked;
                                        string objName = clicked.name ?? "unknown";
                                        string path = BotHelper.GetPath(clicked.transform);
                                        
                                        string btnText = "";
                                        try {
                                            var tmp = clicked.GetComponentInChildren<TextMeshProUGUI>();
                                            if (tmp != null) btnText = tmp.text ?? "";
                                        } catch {}
                                        
                                        LogStateAction($"USER_CLICK_UI: {objName} | Text: {btnText} | Path: {path}");
                                    }
                                }
                            }
                            else
                            {
                                // Click không vào UI → click vào game world (có thể click NPC/mob)
                                _lastClickedObject = null;
                                try {
                                    Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                                    RaycastHit hit;
                                    if (Physics.Raycast(ray, out hit))
                                    {
                                        GameObject hitObj = hit.collider.gameObject;
                                        string objName = hitObj.name ?? "unknown";
                                        string tagStr = hitObj.tag ?? "";
                                        
                                        // Scan logic: check for mob/actor identifiers
                                        string info = "";
                                        try {
                                            var comps = hitObj.GetComponents<Component>();
                                            foreach (var c in comps) {
                                                if (c == null) continue;
                                                string tName = c.GetIl2CppType().Name;
                                                if (tName.Contains("Mob") || tName.Contains("Boss") || 
                                                    tName.Contains("Actor") || tName.Contains("NPC"))
                                                    info += $"[{tName}] ";
                                            }
                                        } catch { }

                                        LogStateAction($"USER_CLICK_3D: {objName} | Tag: {tagStr} | Info: {info}| Pos: {hit.point.x:F1},{hit.point.y:F1},{hit.point.z:F1}");
                                    }
                                    else {
                                        Vector3 mousePos = Input.mousePosition;
                                        LogStateAction($"USER_CLICK_WORLD: ScreenPos({mousePos.x:F0},{mousePos.y:F0})");
                                    }
                                } catch {
                                    Vector3 mousePos = Input.mousePosition;
                                    LogStateAction($"USER_CLICK_WORLD_ERR: ScreenPos({mousePos.x:F0},{mousePos.y:F0})");
                                }
                            }
                        }
                        catch { }
                    }

                    // --- 2. Keyboard Press ---
                    foreach (var key in _trackedKeys)
                    {
                        if (Input.GetKeyDown(key))
                        {
                            LogStateAction($"USER_KEY: {key}");
                        }
                    }
                }
                catch { }
            }

            // Tame Interaction (Phase 3)
            if (_autoQuestEnabled && inGame && _currentQuestInfo != null && _currentQuestInfo.Type == QuestType.TAME)
            {
                _tameInteractTimer += Time.deltaTime;
                if (_tameInteractTimer >= 1.0f) // Check mỗi 1s
                {
                    _tameInteractTimer = 0f;
                    TryTameInteraction();
                }
            }

            // ======================== STATE OBSERVER (inline logging) ========================
            if (_stateLogRunning)
            {
                try
                {
                    string qText = GetCurrentQuestText();
                    int stateId = qText.GetHashCode();
                    int stepIdx = BotHelper.GetQuestStepIndex(qText);
                    string loc = GetCurrentMapName() ?? "null";
                    string target = "None";

                    bool changed = (stateId != _prevStateId) || (stepIdx != _prevStepIndex)
                                || (loc != _prevLocation) || (target != _prevActionTarget);

                    if (changed)
                    {
                        string ts = DateTime.Now.ToString("HH:mm:ss.fff");
                        string entry = $"[{ts}] | State_ID:{stateId} | Step_Index:{stepIdx} | Location:{loc} | Action_Target:{target} | Quest:{qText}";
                        _stateLogQueue.Enqueue(entry);
                        _prevStateId = stateId;
                        _prevStepIndex = stepIdx;
                        _prevLocation = loc;
                        _prevActionTarget = target;

                        // === AI QUEST CLASSIFICATION ===
                        if (_questClassifier != null && !string.IsNullOrEmpty(qText) && qText != _prevClassifiedQuest)
                        {
                            // Quest đã thay đổi → classify và log
                            string actionsTaken = _currentQuestInfo != null ? _currentQuestInfo.Action.ToString() : "";
                            _questClassifier.OnQuestChanged(qText, actionsTaken);
                            _currentQuestInfo = _questClassifier.Classify(qText);
                            _prevClassifiedQuest = qText;
                            
                            // Reset tutorial direction (Start with D/Right)
                            _tutorialMoveDir = 0; 
                            _skillPressTimer = 0f; // Reset skill timer too

                            // Log classification result
                            LogStateAction($"QUEST_CLASSIFIED: {_currentQuestInfo}");
                            Plugin.Log.LogInfo($"[QuestAI] 🧠 {_currentQuestInfo}");
                        }
                    }
                }
                catch { }
            }
        }

        // ======================== STATUS FILE (cho Launcher) ========================

        /// <summary>
        /// Ghi trạng thái bot ra JSON file để Launcher V3 đọc và hiển thị
        /// </summary>
        private void WriteStatusFile()
        {
            if (string.IsNullOrEmpty(_statusFilePath)) return;
            try
            {
                // Lấy quest text từ ShortMissionPanel
                string questText = "";
                string questHint = "";
                if (_shortMissionPanel != null && _shortMissionPanel.gameObject.activeSelf)
                {
                    var texts = _shortMissionPanel.gameObject.GetComponentsInChildren<TextMeshProUGUI>(true);
                    foreach (var t in texts)
                    {
                        if (t == null) continue;
                        string txt = t.text ?? "";
                        if (string.IsNullOrWhiteSpace(txt)) continue;
                        string goName = t.gameObject.name ?? "";

                        if (goName.Contains("DescriptionMission") || goName.Contains("Description"))
                        {
                            // Strip rich text tags
                            questText = System.Text.RegularExpressions.Regex.Replace(txt, "<.*?>", "");
                        }
                        else if (goName.Contains("HintMisisonText") || goName.Contains("Hint"))
                        {
                            questHint = txt;
                        }
                    }
                }

                // Lấy map/zone info
                string mapName = "";
                string zoneName = "";
                try
                {
                    mapName = GetCurrentMapName();
                    zoneName = GetCurrentZoneName();
                }
                catch { }

                // Lấy player position
                string posX = "0", posY = "0";
                try
                {
                    Vector3 pos = GetPlayerPosition();
                    posX = pos.x.ToString("F0");
                    posY = pos.y.ToString("F0");
                }
                catch { }

                // Determine Status Message
                string statusMsg = "";
                if (GlobalPauseCheck()) statusMsg = "⛔ Tạm dừng (User)";
                else if (_isPlayerDead) statusMsg = "💀 Chờ hồi sinh...";
                else if (_stuckCounter > 10) statusMsg = "⚠️ Đang gỡ kẹt...";
                else if (_autoQuestEnabled)
                {
                     if (_inCombatZone) statusMsg = "⚔️ Đang chiến đấu";
                     else statusMsg = "🏃 Đang làm nhiệm vụ";
                }
                else if (_autoAttackEnabled) statusMsg = "⚔️ Treo đánh quái";
                else statusMsg = "💤 Đang nghỉ";

                // Build JSON manually (no System.Text.Json in IL2CPP)
                string json = "{\n" +
                    $"  \"username\": \"{BotHelper.EscapeJson(_loginUsername)}\",\n" +
                    $"  \"scene\": \"{BotHelper.EscapeJson(_currentScene)}\",\n" +
                    $"  \"map\": \"{BotHelper.EscapeJson(mapName)}\",\n" +
                    $"  \"zone\": \"{BotHelper.EscapeJson(zoneName)}\",\n" +
                    $"  \"level\": \"{GetPlayerLevel()}\",\n" +
                    $"  \"posX\": \"{posX}\",\n" +
                    $"  \"posY\": \"{posY}\",\n" +
                    $"  \"quest\": \"{BotHelper.EscapeJson(questText)}\",\n" +
                    $"  \"questHint\": \"{BotHelper.EscapeJson(questHint)}\",\n" +
                    $"  \"status\": \"{BotHelper.EscapeJson(statusMsg)}\",\n" +
                    $"  \"autoEnabled\": {(_autoQuestEnabled ? "true" : "false")},\n" +
                    $"  \"attackEnabled\": {(_autoAttackEnabled ? "true" : "false")},\n" +
                    $"  \"headless\": {(_headlessMode ? "true" : "false")},\n" +
                    $"  \"collectQuest\": {(_isCollectQuest ? "true" : "false")},\n" +
                    $"  \"lastBoss\": \"{BotHelper.EscapeJson(_lastBossNotification)}\",\n" +
                    $"  \"lastBossMap\": \"{BotHelper.EscapeJson(_lastBossMap)}\",\n" +
                    $"  \"timestamp\": \"{DateTime.Now:HH:mm:ss}\"\n" +
                    "}";

                File.WriteAllText(_statusFilePath, json);
            }
            catch { /* Ignore file write errors */ }
        }



        // ======================== AUTO LOGIN (FIX) ========================
        // Dùng Unity API trực tiếp: TMP_InputField.text và Button.onClick.Invoke()
        // KHÔNG dùng Il2Cpp reflection cho field access (bị strip)

        private void DoAutoLogin()
        {
            if (string.IsNullOrEmpty(_loginUsername))
            {
                Plugin.Log.LogWarning("[Bot] Không có thông tin đăng nhập! Kiểm tra accounts.json");
                return;
            }

            Plugin.Log.LogInfo($"[Bot] 🔑 Đang auto-login: {_loginUsername}...");

            try
            {
                // Cách 1: Tìm LoginPanel GameObject rồi lấy TMP_InputField children
                var loginPanel = BotHelper.FindSingletonByType("LoginPanel");
                if (loginPanel == null)
                {
                    Plugin.Log.LogWarning("[Bot] LoginPanel chưa tìm thấy! Thử tìm trên toàn scene...");
                    // Tìm tất cả TMP_InputField trên scene
                    DoAutoLoginGlobal();
                    return;
                }

                Plugin.Log.LogInfo($"[Bot] Tìm thấy LoginPanel trên: {loginPanel.gameObject.name}");
                Plugin.Log.LogInfo($"[Bot] Khởi tạo BotController... (Scene: {SceneManager.GetActiveScene().name})");


                
                // Lấy TMP_InputField trên LoginPanel (MyInputField chứa TMP_InputField bên trong)
                var inputFields = loginPanel.gameObject.GetComponentsInChildren<TMP_InputField>(true);
                Plugin.Log.LogInfo($"[Bot] Tìm thấy {inputFields.Length} TMP_InputField trên LoginPanel");

                if (inputFields.Length >= 2)
                {
                    // InputField đầu tiên = username, thứ hai = password
                    inputFields[0].text = _loginUsername;
                    inputFields[1].text = _loginPassword;
                    Plugin.Log.LogInfo($"[Bot] ✅ Đã điền: [{_loginUsername}] / [****]");
                }
                else if (inputFields.Length == 1)
                {
                    inputFields[0].text = _loginUsername;
                    Plugin.Log.LogInfo("[Bot] ⚠️ Chỉ tìm thấy 1 InputField, đã điền username");
                }
                else
                {
                    Plugin.Log.LogWarning("[Bot] Không tìm thấy TMP_InputField nào!");
                    return;
                }

                // Tìm và click Button đăng nhập
                // MyButton wraps Unity Button at field offset 0x28  
                // Dùng GetComponentsInChildren<Button> để tìm trực tiếp
                var buttons = loginPanel.gameObject.GetComponentsInChildren<Button>(true);
                Plugin.Log.LogInfo($"[Bot] Tìm thấy {buttons.Length} Button trên LoginPanel");

                // Log tên từng button để debug
                for (int i = 0; i < buttons.Length; i++)
                {
                    var btn = buttons[i];
                    if (btn != null)
                    {
                        Plugin.Log.LogInfo($"[Bot]   Button[{i}]: {btn.gameObject.name}");
                    }
                }

                // Click button đầu tiên (login button)  
                // Thường loginButton là button đầu tiên trong hierarchy
                if (buttons.Length > 0)
                {
                    // Tìm button có tên chứa "login" hoặc "đăng nhập"
                    Button targetBtn = null;
                    foreach (var btn in buttons)
                    {
                        if (btn == null) continue;
                        string name = btn.gameObject.name.ToLower();
                        if (name.Contains("login") || name.Contains("dangnhap") || 
                            name.Contains("enter") || name.Contains("submit"))
                        {
                            targetBtn = btn;
                            break;
                        }
                    }
                    
                    // Nếu không tìm thấy theo tên, lấy button đầu tiên
                    if (targetBtn == null)
                        targetBtn = buttons[0];

                    Plugin.Log.LogInfo($"[Bot] Clicking: {targetBtn.gameObject.name}");
                    targetBtn.onClick.Invoke();
                    Plugin.Log.LogInfo("[Bot] ✅ Đã click Login!");
                    _autoLoginDone = true;

                    // Schedule auto character select
                    _charSelectDone = false;
                    _charSelectRetries = 0;
                    _autoCharSelectTimer = _autoCharSelectDelay;
                    Plugin.Log.LogInfo($"[Bot] 🎮 Sẽ tự chọn nhân vật sau {_autoCharSelectDelay}s...");
                }
                else
                {
                    Plugin.Log.LogWarning("[Bot] Không tìm thấy Button nào!");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Bot] ❌ Lỗi auto-login: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Tự động chọn nhân vật sau khi login xong
        /// Tìm CharacterChoosingPanel → click nhân vật đầu tiên (hoặc theo config)
        /// </summary>
        private void DoAutoSelectCharacter()
        {
            if (_charSelectDone) return;

            _charSelectRetries++;
            Plugin.Log.LogInfo($"[Bot] 🎮 Auto Character Select (lần {_charSelectRetries}/{_charSelectMaxRetries})...");

            try
            {
                // === 1. Tìm CharacterChoosingPanel ===
                var charPanel = GameObject.Find("CharacterChoosingPanel");
                // Cũng tìm clone nếu có
                if (charPanel == null)
                    charPanel = GameObject.Find("CharacterChoosingPanel(Clone)");

                if (charPanel == null || !charPanel.activeSelf)
                {
                    Plugin.Log.LogInfo("[Bot] 🎮 CharacterChoosingPanel chưa hiện, đợi thêm...");
                    if (_charSelectRetries < _charSelectMaxRetries)
                    {
                        _autoCharSelectTimer = 2f; // Thử lại sau 2s
                    }
                    else
                    {
                        Plugin.Log.LogWarning("[Bot] ⚠️ Không tìm thấy CharacterChoosingPanel sau nhiều lần thử!");
                    }
                    return;
                }

                Plugin.Log.LogInfo("[Bot] 🎮 Tìm thấy CharacterChoosingPanel!");

                // === 2. Tìm ChooseCharacterLayout chứa các nhân vật ===
                var layout = charPanel.transform.Find("ChooseCharacterLayout");
                if (layout == null)
                {
                    // Fallback: tìm bất kỳ layout nào
                    var layouts = charPanel.GetComponentsInChildren<UnityEngine.UI.HorizontalLayoutGroup>(true);
                    if (layouts != null && layouts.Length > 0)
                        layout = layouts[0].transform;
                }

                if (layout != null && layout.childCount > 0)
                {
                    // Chọn nhân vật theo index (mặc định = 0, hoặc _loginCharacter)
                    int charIndex = Math.Min(_loginCharacter, layout.childCount - 1);
                    if (charIndex < 0) charIndex = 0;

                    var charObj = layout.GetChild(charIndex);
                    if (charObj != null)
                    {
                        // Click vào nhân vật (có thể là Button hoặc click handler)
                        var btn = charObj.GetComponent<Button>();
                        if (btn == null)
                            btn = charObj.GetComponentInChildren<Button>();

                        if (btn != null)
                        {
                            btn.onClick.Invoke();
                            Plugin.Log.LogInfo($"[Bot] 🎮 Clicked nhân vật [{charIndex}]: {charObj.name}");
                        }
                        else
                        {
                            Plugin.Log.LogInfo($"[Bot] 🎮 Nhân vật [{charIndex}] không có Button, thử click trực tiếp...");
                        }
                    }
                }
                else
                {
                    Plugin.Log.LogWarning("[Bot] ⚠️ Không tìm thấy ChooseCharacterLayout hoặc không có nhân vật!");
                }

                // === 3. Schedule click nút vào game sau 1.5s ===
                _enterGameTimer = 1.5f;

                _charSelectDone = true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Bot] ❌ Auto character select error: {ex.Message}");
                if (_charSelectRetries < _charSelectMaxRetries)
                    _autoCharSelectTimer = 2f;
            }
        }

        /// <summary>
        /// Tìm và click nút xác nhận/vào game trên CharacterChoosingPanel
        /// </summary>
        private void DoAutoEnterGame()
        {
            try
            {
                var charPanel = GameObject.Find("CharacterChoosingPanel");
                if (charPanel == null)
                    charPanel = GameObject.Find("CharacterChoosingPanel(Clone)");

                if (charPanel != null && charPanel.activeSelf)
                {
                    var buttons = charPanel.GetComponentsInChildren<Button>(true);
                    foreach (var btn in buttons)
                    {
                        if (btn == null || !btn.gameObject.activeSelf) continue;
                        string name = btn.gameObject.name ?? "";
                        if (name == "BackButton") continue;

                        btn.onClick.Invoke();
                        Plugin.Log.LogInfo($"[Bot] 🎮 Clicked '{name}' trên CharacterChoosingPanel → vào game!");

                        // Schedule auto-enable quest system sau 8s (chờ game load xong)
                        // CHỈ bật nếu config cho phép autoQuest
                        if (_configAutoQuest)
                        {
                            _autoEnableQuestTimer = 8f;
                            Plugin.Log.LogInfo("[Bot] 🎮 Sẽ tự động bật Auto Quest sau 8s...");
                        }
                        else
                        {
                            Plugin.Log.LogInfo("[Bot] 🎮 Auto Quest đã TẮT trong config → không tự bật.");
                        }
                        return;
                    }
                }
                else
                {
                    Plugin.Log.LogInfo("[Bot] 🎮 CharacterChoosingPanel đã tắt → có thể đã vào game!");
                    // Vẫn schedule auto-enable phòng trường hợp panel đóng trước khi timer fire
                    if (_autoEnableQuestTimer < 0 && _configAutoQuest)
                    {
                        _autoEnableQuestTimer = 5f;
                        Plugin.Log.LogInfo("[Bot] 🎮 Sẽ tự động bật Auto Quest sau 5s...");
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Bot] Enter game error: {ex.Message}");
            }
        }

        // Fallback: tìm InputField trên toàn scene
        private void DoAutoLoginGlobal()
        {
            try
            {
                var allInputs = GameObject.FindObjectsOfType<TMP_InputField>();
                Plugin.Log.LogInfo($"[Bot] Global: tìm thấy {allInputs.Length} TMP_InputField");

                if (allInputs.Length >= 2)
                {
                    allInputs[0].text = _loginUsername;
                    allInputs[1].text = _loginPassword;
                    Plugin.Log.LogInfo($"[Bot] ✅ Global: Đã điền username/password");

                    // Tìm button gần nhất
                    var allButtons = GameObject.FindObjectsOfType<Button>();
                    foreach (var btn in allButtons)
                    {
                        if (btn == null) continue;
                        string name = btn.gameObject.name.ToLower();
                        if (name.Contains("login") || name.Contains("dangnhap") ||
                            name.Contains("enter") || name.Contains("submit"))
                        {
                            btn.onClick.Invoke();
                            Plugin.Log.LogInfo($"[Bot] ✅ Global: Clicked {btn.gameObject.name}");
                            _autoLoginDone = true;
                            return;
                        }
                    }
                    // Click button đầu tiên
                    if (allButtons.Length > 0)
                    {
                        allButtons[0].onClick.Invoke();
                        Plugin.Log.LogInfo($"[Bot] ✅ Global: Clicked {allButtons[0].gameObject.name}");
                        _autoLoginDone = true;
                    }
                }
                else
                {
                    Plugin.Log.LogWarning("[Bot] Global: Không đủ InputField!");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Bot] ❌ Global login error: {ex.Message}");
            }
        }

        // ======================== JSON PARSER ========================
        // Chỉ dùng string/int return types (IL2CPP compatible)



        // ======================== AUTO ALL (QUEST + ATTACK + INTERACT) ========================

        private void ToggleAutoAll()
        {
            if (!_managersFound)
                FindManagers();

            _autoQuestEnabled = !_autoQuestEnabled;

            if (_autoQuestEnabled)
            {
                // Bật tất cả module
                _autoAttackFlag = true;
                _autoInteractFlag = true;
                _autoDialogFlag = true;

                Plugin.Log.LogInfo("[Bot] ★★★ AUTO ALL: BẬT ★★★");
                Plugin.Log.LogInfo("[Bot]   F1=Quest+Pathfind  F3=Attack  F4=Interact  F5=Dialog");

                // 1. Auto Quest (via AutoMissionManager)
                if (_autoMissionManager != null)
                {
                    try
                    {
                        BotHelper.CallMethod(_autoMissionManager, "auto");
                        Plugin.Log.LogInfo("[Bot] ✅ [QUEST] Auto Quest: BẬT");
                    }
                    catch (Exception ex) { Plugin.Log.LogWarning($"[Bot] Auto Quest error: {ex.Message}"); }
                }

                // 2. Auto Attack
                ToggleAutoAttack(true);
                Plugin.Log.LogInfo("[Bot] ✅ [ATTACK] Auto Attack: BẬT (F3)");

                // 3. Pathfind lần đầu
                _sameQuestPathfindCount = 0;
                _trackedQuestText = "";
                TriggerAutoPathfind();

                // 4. setPlayerOnAutoMode
                if (_mainGame != null)
                {
                    try
                    {
                        // FIXED: setPlayerOnAutoMode needs a bool parameter
                        BotHelper.CallMethod(_mainGame, "setPlayerOnAutoMode", new object[] { true });
                        Plugin.Log.LogInfo("[Bot] ✅ setPlayerOnAutoMode: BẬT");
                    }
                    catch (Exception ex) { Plugin.Log.LogWarning($"[Bot] setPlayerOnAutoMode error: {ex.Message}"); }
                }

                Plugin.Log.LogInfo("[Bot] ✅ [INTERACT] Auto Interact: BẬT (F4)");
                Plugin.Log.LogInfo("[Bot] ✅ [DIALOG] Auto NPC Dialog: BẬT (F5)");

                LogQuestStatus();
            }
            else
            {
                // Tắt tất cả
                _autoAttackFlag = false;
                _autoInteractFlag = false;
                _autoDialogFlag = false;

                Plugin.Log.LogInfo("[Bot] ⛔⛔⛔ AUTO ALL: TẮT ⛔⛔⛔");

                if (_autoMissionManager != null)
                {
                    try { BotHelper.CallMethod(_autoMissionManager, "stop"); } catch { }
                }
                ToggleAutoAttack(false);
            }
        }

        // ======================== TOGGLE RIÊNG TỬNG MODULE ========================

        private void ToggleAutoAttackKey()
        {
            _autoAttackFlag = !_autoAttackFlag;
            ToggleAutoAttack(_autoAttackFlag);
            Plugin.Log.LogInfo($"[Bot] [ATTACK] Auto Attack: {(_autoAttackFlag ? "BẬT" : "TẮT")} (F3)");
        }

        private void ToggleAutoInteractKey()
        {
            _autoInteractFlag = !_autoInteractFlag;
            _interactBtnWasVisible = false; // Reset
            Plugin.Log.LogInfo($"[Bot] [INTERACT] Auto Interact: {(_autoInteractFlag ? "BẬT" : "TẮT")} (F4)");
        }

        private void ToggleAutoDialogKey()
        {
            _autoDialogFlag = !_autoDialogFlag;
            Plugin.Log.LogInfo($"[Bot] [DIALOG] Auto NPC Dialog: {(_autoDialogFlag ? "BẬT" : "TẮT")} (F5)");
        }

        // === SMART LOOP DETECTION FIELDS ===
        private string _lastClickedButtonSignature = "";
        private int _clickSpamCount = 0;
        private HashSet<string> _ignoredButtonSignatures = new HashSet<string>();
        private float _lastDialogActionTime = 0f;


        // ======================== FARM STATE MACHINE ========================

        /// <summary>
        /// Quản lý trạng thái farm: IDLE → PATHFINDING → KILLING/COLLECTING → RETURNING → TURNING_IN → IDLE
        /// Có stuck detection và auto-retry để loop 24/7
        /// </summary>
        private void UpdateFarmState()
        {
            try
            {
                string currentQuest = GetCurrentQuestText();
                bool hasQuest = !string.IsNullOrEmpty(currentQuest);
                
                // Detect NPC dialog đang mở
                bool dialogOpen = IsDialogueOpen();

                // === USER PAUSE CHECK ===
                if (Time.time - _lastUserActionTime < 5f)
                {
                     // User is active -> Pause Bot
                     // Optional: Show status "Paused by User"
                     return; 
                }

                // === ANTI-STUCK LOOP CHECK ===
                _stuckCheckTimer += Time.deltaTime;
                if (_stuckCheckTimer >= 1.0f) // Check per second
                {
                    _stuckCheckTimer = 0f;
                    // Hash check (ignore numbers)
                    string questHash = System.Text.RegularExpressions.Regex.Replace(currentQuest, @"\d+", "#");
                    
                    if (questHash == _stuckCheckQuest && !string.IsNullOrEmpty(currentQuest))
                    {
                        _stuckCounter++;
                        // 15 seconds threshold
                        if (_stuckCounter >= 15)
                        {
                            Plugin.Log.LogWarning($"[Bot] ⚠️ STUCK DETECTED! Quest unchanged for 15s. Escaping NPC...");
                            
                            // 1. Close Panels
                            CloseAllPanels();

                            // 2. Click Outside (Center of Screen)
                            ClickOutsideUI();

                            // 3. Force re-click quest
                            TriggerAutoPathfind(true); 
                            
                            _stuckCounter = 0; // Reset
                        }
                    }
                    else
                    {
                        _stuckCheckQuest = questHash;
                        _stuckCounter = 0;
                    }
                }

                // Update quest flags FIRST to fix race condition
                AnalyzeQuest(currentQuest);

                // Detect kill quest progress
                bool isKillQuest = false;
                int killCurrent = 0, killTotal = 0;
                if (hasQuest && !_isCollectQuest)
                {
                    try
                    {
                        int po = currentQuest.IndexOf('(');
                        int pc = po >= 0 ? currentQuest.IndexOf(')', po) : -1;
                        if (po >= 0 && pc > po)
                        {
                            string inside = currentQuest.Substring(po + 1, pc - po - 1);
                            // Clean color tags
                            while (inside.Contains("<color"))
                            {
                                int ts2 = inside.IndexOf("<color");
                                int te2 = inside.IndexOf('>', ts2);
                                if (te2 > ts2) inside = inside.Substring(0, ts2) + inside.Substring(te2 + 1);
                                else break;
                            }
                            inside = inside.Replace("</color>", "");
                            int sl = inside.IndexOf('/');
                            if (sl > 0)
                            {
                                int.TryParse(inside.Substring(0, sl).Trim(), out killCurrent);
                                int.TryParse(inside.Substring(sl + 1).Trim(), out killTotal);
                                if (killTotal > 0) isKillQuest = true;
                            }
                        }
                    }
                    catch { }
                }

                // Detect stuck (không di chuyển khi đang pathfind)
                if (_farmState == FarmState.PATHFINDING || _farmState == FarmState.RETURNING)
                {
                    if (_idleTime >= 2f) // Đứng yên > 2s
                        _farmStuckTimer += Time.deltaTime;
                    else
                        _farmStuckTimer = 0f;
                }

                FarmState prevState = _farmState;

                switch (_farmState)
                {
                    // --- IDLE: Đợi quest ---
                    case FarmState.IDLE:
                        if (hasQuest)
                        {
                            SetFarmState(FarmState.PATHFINDING, "Có quest mới → bắt đầu pathfind");
                            _farmQuestAtStart = currentQuest;
                        }
                        break;

                    // --- PATHFINDING: Đang di chuyển đến target ---
                    case FarmState.PATHFINDING:
                        // Action: Click ShortMissionPanel periodicaly
                        if (_pathfindCooldown <= 0f)
                        {
                            TriggerAutoPathfind();
                            _pathfindCooldown = _pathfindInterval; // Reset cooldown (e.g. 3s)
                        }
                        else
                        {
                             _pathfindCooldown -= Time.deltaTime;
                        }

                        // Quest thay đổi = đã nhận quest → chuyển trạng thái
                        if (hasQuest && currentQuest != _farmQuestAtStart)
                        {
                            _farmQuestAtStart = currentQuest;
                            _farmPathfindRetries = 0;
                        }

                        // Dialog mở = đến NPC rồi
                        if (dialogOpen)
                        {
                            SetFarmState(FarmState.TALKING_NPC, "Đến NPC → đang nói chuyện");
                        }
                        // Kill quest detected
                        else if (isKillQuest && killCurrent < killTotal)
                        {
                            SetFarmState(FarmState.KILLING, $"Kill quest: {killCurrent}/{killTotal}");
                        }
                        // Collect quest detected
                        else if (_isCollectQuest)
                        {
                            SetFarmState(FarmState.COLLECTING, "Collect quest bắt đầu");
                        }
                        // Stuck detection
                        else if (_farmStuckTimer >= STUCK_TIMEOUT)
                        {
                            _farmStuckTimer = 0f;
                            _farmPathfindRetries++;
                            if (_farmPathfindRetries >= MAX_PATHFIND_RETRIES)
                            {
                                LogStateAction($"FARM_STUCK: {MAX_PATHFIND_RETRIES} retries → reset về IDLE");
                                SetFarmState(FarmState.IDLE, $"Stuck {MAX_PATHFIND_RETRIES} lần → reset");
                                _farmPathfindRetries = 0;
                            }
                            else
                            {
                                LogStateAction($"FARM_STUCK: retry pathfind ({_farmPathfindRetries}/{MAX_PATHFIND_RETRIES})");
                                // Force re-pathfind 
                                _pathfindTimer = _pathfindInterval;
                                _pathfindCooldown = 0f;
                                _idleTime = _idleThreshold;
                            }
                        }
                        // Timeout
                        else if (_farmStateTimer >= STATE_TIMEOUT)
                        {
                            SetFarmState(FarmState.IDLE, "Pathfind timeout → reset");
                        }
                        break;

                    // --- TALKING_NPC: Đang trong dialog ---
                    case FarmState.TALKING_NPC:
                        // Action: Click Dialog Options
                        if (_dialogueCooldownTimer <= 0f)
                        {
                            HandleDialogue();
                        }
                        else
                        {
                            _dialogueCooldownTimer -= Time.deltaTime;
                        }

                        if (!dialogOpen)
                        {
                            // Dialog đóng → kiểm tra quest có thay đổi
                            string afterQuest = GetCurrentQuestText();
                            if (afterQuest != _farmQuestAtStart)
                            {
                                // Quest đổi → nhận quest mới hoặc hoàn thành
                                if (string.IsNullOrEmpty(afterQuest))
                                {
                                    _farmQuestsCompleted++;
                                    SetFarmState(FarmState.IDLE, $"Quest hoàn thành! (Tổng: {_farmQuestsCompleted})");
                                }
                                else
                                {
                                    _farmQuestAtStart = afterQuest;
                                    SetFarmState(FarmState.PATHFINDING, "Nhận quest mới → pathfind");
                                }
                            }
                            else
                            {
                                // Quest giữ nguyên → tiếp tục pathfind
                                SetFarmState(FarmState.PATHFINDING, "Dialog đóng, quest giữ nguyên → pathfind");
                            }
                        }
                        else if (_farmStateTimer >= STATE_TIMEOUT)
                        {
                            SetFarmState(FarmState.IDLE, "Dialog timeout → reset");
                        }
                        break;

                    // --- KILLING: Đang đánh quái ---
                    case FarmState.KILLING:
                        if (dialogOpen)
                        {
                            SetFarmState(FarmState.TALKING_NPC, "Dialog mở trong lúc kill");
                        }
                        else if (isKillQuest && killCurrent >= killTotal)
                        {
                            SetFarmState(FarmState.RETURNING, $"Kill quest XONG ({killCurrent}/{killTotal}) → quay về NPC");
                        }
                        else if (!isKillQuest && hasQuest && currentQuest != _farmQuestAtStart)
                        {
                            // Quest text đổi = đã hoàn thành kill
                            SetFarmState(FarmState.PATHFINDING, "Quest thay đổi sau kill → pathfind");
                            _farmQuestAtStart = currentQuest;
                        }
                        else if (_farmStateTimer >= STATE_TIMEOUT * 2) // Kill cho 4 phút
                        {
                            SetFarmState(FarmState.IDLE, "Kill timeout → reset");
                        }
                        break;

                    // --- COLLECTING: Đang thu thập ---
                    case FarmState.COLLECTING:
                        if (dialogOpen)
                        {
                            SetFarmState(FarmState.TALKING_NPC, "Dialog mở → nói chuyện NPC");
                        }
                        else if (hasQuest && currentQuest != _farmQuestAtStart)
                        {
                            SetFarmState(FarmState.PATHFINDING, "Collect quest thay đổi → pathfind");
                            _farmQuestAtStart = currentQuest;
                        }
                        else if (!hasQuest)
                        {
                            _farmQuestsCompleted++;
                            SetFarmState(FarmState.IDLE, $"Collect XONG! (Tổng: {_farmQuestsCompleted})");
                        }
                        else if (_farmStateTimer >= STATE_TIMEOUT)
                        {
                            SetFarmState(FarmState.IDLE, "Collect timeout → reset");
                        }
                        break;

                    // --- RETURNING: Đang quay về NPC trả quest ---
                    case FarmState.RETURNING:
                        if (dialogOpen)
                        {
                            SetFarmState(FarmState.TURNING_IN, "Đến NPC → trả quest");
                        }
                        else if (_farmStuckTimer >= STUCK_TIMEOUT)
                        {
                            _farmStuckTimer = 0f;
                            _farmPathfindRetries++;
                            LogStateAction($"RETURN_STUCK: retry pathfind ({_farmPathfindRetries})");
                            _pathfindTimer = _pathfindInterval;
                            _pathfindCooldown = 0f;
                            _idleTime = _idleThreshold;
                            if (_farmPathfindRetries >= MAX_PATHFIND_RETRIES)
                            {
                                SetFarmState(FarmState.IDLE, "Return stuck → reset");
                                _farmPathfindRetries = 0;
                            }
                        }
                        else if (_farmStateTimer >= STATE_TIMEOUT)
                        {
                            SetFarmState(FarmState.IDLE, "Return timeout → reset");
                        }
                        break;

                    // --- TURNING_IN: Đang trả quest ---
                    case FarmState.TURNING_IN:
                        if (!dialogOpen)
                        {
                            string afterTurnIn = GetCurrentQuestText();
                            if (afterTurnIn != _farmQuestAtStart || string.IsNullOrEmpty(afterTurnIn))
                            {
                                _farmQuestsCompleted++;
                                SetFarmState(FarmState.IDLE, $"Trả quest XONG! (Tổng: {_farmQuestsCompleted})");
                            }
                            else
                            {
                                SetFarmState(FarmState.PATHFINDING, "Trả quest chưa xong → pathfind lại");
                            }
                        }
                        else if (_farmStateTimer >= STATE_TIMEOUT)
                        {
                            SetFarmState(FarmState.IDLE, "Turn-in timeout → reset");
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Farm] Error: {ex.Message}");
            }
        }

        private void SetFarmState(FarmState newState, string reason)
        {
            FarmState old = _farmState;
            _farmState = newState;
            _farmStateTimer = 0f;
            _farmStuckTimer = 0f;
            string msg = $"FARM: {old} → {newState} | {reason} | Quests: {_farmQuestsCompleted} | Time: {_farmTotalTime:F0}s";
            LogStateAction(msg);
            Plugin.Log.LogInfo($"[Farm] {msg}");

            // Auto-Attack Logic Integration
            if (newState == FarmState.KILLING)
            {
                EnsureAutoAttack();
            }
        }

        /// <summary>
        /// Hook onClick listener lên tất cả button trong NPC dialog panel.
        /// Mỗi khi người chơi click button, log sẽ ghi lại tên button + text.
        /// </summary>
        private void HookDialogButtons(GameObject dialogPanel)
        {
            try
            {
                var allBtns = dialogPanel.GetComponentsInChildren<Button>(true);
                foreach (var btn in allBtns)
                {
                    if (btn == null) continue;
                    int btnId = btn.GetInstanceID();
                    if (_hookedBtnIds.Contains(btnId)) continue; // Đã hook rồi
                    _hookedBtnIds.Add(btnId);

                    // Capture button info
                    string btnName = btn.gameObject.name ?? "unknown";
                    string btnText = "";
                    try {
                        var tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
                        if (tmp != null) btnText = tmp.text ?? "";
                    } catch {}
                    string path = BotHelper.GetPath(btn.transform);

                    // Capture local copies for closure
                    string capName = btnName;
                    string capText = btnText;
                    string capPath = path;

                    // Hook listener - CHỈ log khi USER click (không phải bot invoke)
                    btn.onClick.AddListener((UnityEngine.Events.UnityAction)(() =>
                    {
                        try
                        {
                            // Skip logging khi bot đang invoke (không phải user click)
                            if (_botInvoking) return;
                            
                            // Re-read text at click time (có thể đã thay đổi)
                            string currentText = capText;
                            try {
                                var tmp2 = btn.GetComponentInChildren<TextMeshProUGUI>();
                                if (tmp2 != null) currentText = tmp2.text ?? capText;
                            } catch {}
                            LogStateAction($"USER_CLICK_NPC_BTN: {capName} | Text: {currentText} | NPC: {_lastNpcName} | Path: {capPath}");
                        }
                        catch { }
                    }));

                    Plugin.Log.LogInfo($"[InputRec] Hooked button: {btnName} ({btnText})");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[InputRec] HookDialogButtons error: {ex.Message}");
            }
        }

        /// <summary>
        /// Phân tích quest text để xác định cờ _isCollectQuest và các thông tin khác
        /// </summary>
        private void AnalyzeQuest(string questText)
        {
            try
            {
                if (string.IsNullOrEmpty(questText)) return;

                // Quest "Thu thập", "Nhặt", "Collect" cũng có dạng (X/Y) nhưng KHÔNG phải kill quest
                _isCollectQuest = questText.Contains("Thu thập") || questText.Contains("Nhặt") 
                    || questText.Contains("Collect") || questText.Contains("Lượm")
                    || questText.Contains("thu thập") || questText.Contains("nhặt")
                    || questText.Contains("Tìm") || questText.Contains("tìm")
                    || questText.Contains("Lấy") || questText.Contains("lấy")
                    || questText.Contains("Gặp") || questText.Contains("gặp")
                    || questText.Contains("Sử dụng") || questText.Contains("Dùng")
                    || mergeQuestKeywords(questText);

                // Helper for additional keywords
                bool mergeQuestKeywords(string text) {
                    return text.Contains("Thuần phục") || text.Contains("thuần phục")
                        || text.Contains("Đánh") || text.Contains("đánh")
                        || text.Contains("Thách đấu") || text.Contains("thách đấu"); // Added Thách đấu
                }
            }
            catch {}
        }

        /// <summary>
        /// Click ShortMissionPanel để kích hoạt auto-pathfind đến quest NPC
        /// Có quest detection: kill quest thì skip, same quest thì giới hạn click
        /// </summary>
        private void TriggerAutoPathfind(bool force = false)
        {
            try
            {
                // === 0. Strict Dialog Check ===
                if (IsDialogueOpen())
                {
                    Plugin.Log.LogInfo("[Bot] 🛑 Pathfind ABORTED: Dialog is open -> Switch to HandleDialogue");
                    HandleDialogue(); // Tự động xử lý dialog thay vì đứng im
                    return;
                }

                // === 0.1 Cooldown Check ===
                if (_dialogueCooldownTimer > 0f)
                {
                    _dialogueCooldownTimer -= Time.deltaTime;
                    return; // Đợi cooldown
                }

                // === 1. Đọc quest text hiện tại ===
                string questText = GetCurrentQuestText();

                if (!string.IsNullOrEmpty(questText))
                {
                    // Update quest flags
                    AnalyzeQuest(questText);

                    // === 3. Kill quest detection: có dạng (X/Y) thì SKIP pathfind ===
                    // CHỈ skip nếu KHÔNG phải collect quest và KHÔNG phải tame quest
                    // FIX: If 'force' is true (Idle detected), ignore kill quest skip and CLICK anyway
                    if (!force && !_isCollectQuest && (_currentQuestInfo == null || _currentQuestInfo.Type != QuestType.TAME))
                    {
                        // Logic parsing kill quest để log (đã có trong UpdateFarmState, ở đây chỉ để log action)
                        // ...
                        // Simplify: Trust _isCollectQuest checks
                         int parenOpen = questText.IndexOf('(');
                         if (parenOpen >= 0)
                         {
                             // Check if valid 0/1 logic...
                             // For simplicity: If it's NOT collect/tame, and has (X/Y), let UpdateFarmState handle the switching.
                             // TriggerAutoPathfind just clicks if we are here.
                             // But we should LOG consistent with state.
                             // ... (Existing parsing logic for logging) ...
                             
                             // Re-using existing parsing block for consistency
                            int parenClose = questText.IndexOf(')', parenOpen);
                            if (parenClose > parenOpen)
                            {
                                string inside = questText.Substring(parenOpen + 1, parenClose - parenOpen - 1);
                                // ... (Cleaning logic) ...
                                string cleaned = inside;
                                while (cleaned.Contains("<color"))
                                {
                                    int tagStart = cleaned.IndexOf("<color");
                                    int tagEnd = cleaned.IndexOf('>', tagStart);
                                    if (tagEnd > tagStart)
                                        cleaned = cleaned.Substring(0, tagStart) + cleaned.Substring(tagEnd + 1);
                                    else break;
                                }
                                cleaned = cleaned.Replace("</color>", "");
                                int slash = cleaned.IndexOf('/');
                                if (slash > 0)
                                {
                                    string left = cleaned.Substring(0, slash).Trim();
                                    string right = cleaned.Substring(slash + 1).Trim();
                                    int cur, total;
                                    if (int.TryParse(left, out cur) && int.TryParse(right, out total))
                                    {
                                        if (cur < total)
                                        {
                                            LogActivity($"⚔️ Kill quest: {questText} ({cur}/{total}) → pathfind click");
                                            _killQuestDone = false;
                                        }
                                        else
                                        {
                                            if (!_killQuestDone)
                                            {
                                                _killQuestDone = true;
                                                _sameQuestPathfindCount = 0;
                                                _pathfindCooldown = 0;
                                                LogActivity($"✅ Kill quest XONG: {questText} ({cur}/{total}) → pathfind về NPC!");
                                            }
                                        }
                                    }
                                }
                            }
                         }
                    }
                    else
                    {
                        LogActivity($"📦 Collect/Talk quest: {questText} → pathfind to NPC");
                    }

                    // === 4. Quest change detection ===
                    if (questText == _trackedQuestText)
                    {
                        _sameQuestPathfindCount++;
                    }
                    else
                    {
                        _trackedQuestText = questText;
                        _sameQuestPathfindCount = 0;
                        LogActivity($"🌟 Quest mới: {questText}{(_isCollectQuest ? " (📦 collect)" : "")}");
                    }
                }

                // === 4. Click ShortMissionPanel ===
                // ShortMissionPanel nằm trong QuickInfoPanel, cả 2 có thể bị inactive (collapsed)
                // GameObject.Find() không tìm được inactive objects → phải search qua transform hierarchy
                if (_shortMissionBtn == null)
                {
                    // Strategy 1: GameObject.Find (chỉ hoạt động khi active)
                    var go = GameObject.Find("ShortMissionPanel");
                    if (go != null)
                    {
                        _shortMissionBtn = go.GetComponent<Button>();
                        if (_shortMissionBtn == null)
                            _shortMissionBtn = go.GetComponentInChildren<Button>();
                    }

                    // Strategy 2: Search qua HUDCanvas hierarchy (tìm được cả inactive)
                    if (_shortMissionBtn == null)
                    {
                        var hudCanvas = GameObject.Find("HUDCanvas");
                        if (hudCanvas != null)
                        {
                            var found = BotHelper.FindInactiveChild(hudCanvas.transform, "ShortMissionPanel");
                            if (found != null)
                            {
                                _shortMissionBtn = found.GetComponent<Button>();
                                if (_shortMissionBtn == null)
                                    _shortMissionBtn = found.GetComponentInChildren<Button>(true);
                                Plugin.Log.LogInfo($"[Bot] 🔍 Found ShortMissionPanel via hierarchy search (active={found.activeSelf})");
                            }
                        }
                    }
                }

                if (_shortMissionBtn != null)
                {
                    // Ensure QuickInfoPanel (parent) is active
                    var quickInfoPanel = _shortMissionBtn.transform.parent?.gameObject;
                    if (quickInfoPanel != null && quickInfoPanel.name == "QuickInfoPanel" && !quickInfoPanel.activeSelf)
                    {
                        quickInfoPanel.SetActive(true);
                        Plugin.Log.LogInfo("[Bot] 📋 Activated QuickInfoPanel (was collapsed)");
                    }

                    // Ensure ShortMissionPanel itself is active
                    if (!_shortMissionBtn.gameObject.activeSelf)
                    {
                        _shortMissionBtn.gameObject.SetActive(true);
                        Plugin.Log.LogInfo("[Bot] 📋 Activated ShortMissionPanel (was collapsed)");
                    }

                    // Click it!
                    _shortMissionBtn.onClick.Invoke();
                    
                    // Reset failure count on success (if we reached this point, we clicked)
                    _consecutivePathfindFailures = 0; 

                    _pathfindCooldown = _pathfindCooldownTime;
                    _idleTime = 0f;
                    LogActivity($"🚀 Pathfind: Clicked quest panel");
                    LogStateAction($"PATHFIND click ShortMissionPanel → moving to NPC");
                }
                else
                {
                    _consecutivePathfindFailures++;
                    Plugin.Log.LogWarning($"[Bot] ❌ ShortMissionPanel not found (Failures: {_consecutivePathfindFailures})");

                    // Refresh managers every 3 failures
                    if (_consecutivePathfindFailures % 3 == 0)
                    {
                        Plugin.Log.LogInfo("[Bot] 🔄 Re-scanning managers to refresh UI references...");
                        FindManagers();
                    }

                    // Direct Search Fallback
                    if (_consecutivePathfindFailures >= 5)
                    {
                         TryFindTargetAndMove(); 
                    }

                    // Force Expand Fallback
                    if (_consecutivePathfindFailures >= 10)
                    {
                        var quickInfo = GameObject.Find("QuickInfoPanel");
                        if (quickInfo != null)
                        {
                            quickInfo.SetActive(true);
                            var expand = quickInfo.transform.parent?.Find("ExpandButton")?.gameObject;
                            if (expand != null) expand.SetActive(true);
                        }
                    }

                    _pathfindCooldown = _pathfindCooldownTime * 2f; // Slow down retries on failure
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Bot] Auto Pathfind error: {ex.Message}");
                _pathfindCooldown = _pathfindCooldownTime;
            }
        }

        // Win32 API for keyboard simulation
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

        private const int KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const int KEYEVENTF_KEYUP = 0x0002;
        private const byte VK_UP = 0x26;
        private const byte VK_DOWN = 0x28;
        private const byte VK_LEFT = 0x25;
        private const byte VK_RIGHT = 0x27;
        
        // WASD
        private const byte VK_W = 0x57;
        private const byte VK_A = 0x41;
        private const byte VK_S = 0x53;
        private const byte VK_D = 0x44;

        // === AUTO MOVEMENT HELPERS ===
        
        private void ProcessAutoMovement(Vector3? targetPos)
        {
            if (targetPos == null) return;
            Vector3 target = targetPos.Value;

            // Check if we are on the correct map?
            // Assuming same map for now or BotController logic handles map check elsewhere.
            
            // Check distance
            float dist = Vector3.Distance(_lastPlayerPos, target);
            if (dist < 1.5f) // Arrived (1.5m tolerance)
            {
                 ReleaseAllMovementKeys();
                 // Optionally interact or wait?
                 return;
            }

            // Calculate direction
            Vector3 dir = (target - _lastPlayerPos).normalized;
            
            // Simple 8-direction WASD
            // Priority: W/S then A/D
            
            bool up = dir.z > 0.3f;    // Threshold 0.3 to allow diagonal
            bool down = dir.z < -0.3f;
            bool right = dir.x > 0.3f;
            bool left = dir.x < -0.3f;
            
            SetKeyStatus(VK_W, up);
            SetKeyStatus(VK_S, down);
            SetKeyStatus(VK_D, right);
            SetKeyStatus(VK_A, left);
            
            // Keep idle timer low so we appear "Active"
            _idleTime = 0f;
        }

        private void SetKeyStatus(byte key, bool press)
        {
             bool isPressed = _pressedKeys.Contains(key);
             if (press && !isPressed) {
                 keybd_event(key, 0, KEYEVENTF_EXTENDEDKEY, 0); // Press
                 _pressedKeys.Add(key);
             }
             else if (!press && isPressed) {
                 keybd_event(key, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, 0); // Release
                 _pressedKeys.Remove(key);
             }
        }

        private void ReleaseAllMovementKeys()
        {
            if (_pressedKeys.Count == 0) return;
            // Copy to list to avoid modification exception
            var keys = new List<byte>(_pressedKeys);
            foreach(var k in keys) SetKeyStatus(k, false);
            Plugin.Log.LogInfo("[Bot] 🛑 Released all movement keys.");
        }


        /// <summary>
        /// Tutorial movement: khi quest yêu cầu di chuyển nhưng ShortMissionPanel không khả dụng.
        /// Simulate di chuyển bằng cách:
        /// 1. Gửi phím cứng OS (W, A, S, D / Arrows) - VÌ GAME YÊU CẦU NÚT THẬT
        /// 2. (Fallback) Tìm Joystick/Pad UI
        /// </summary>
        private void TryTutorialMovement()
        {
            try
            {
                // === 1. Simulate Physical Key Press (OS Level) ===
                // Xoay vòng: Right (D) -> Up (W) -> Left (A) -> Down (S)
                // Mỗi lần gọi giữ phím 0.5s rồi nhả (logic giữ cần coroutine hoặc invoke, ở đây ta nhấn giữ 1 frame dài)
                
                byte[] keys = { VK_D, VK_W, VK_A, VK_S };
                byte key = keys[_tutorialMoveDir % 4];
                
                // Press down
                keybd_event(key, 0, KEYEVENTF_EXTENDEDKEY, 0);
                
                // Log
                string kName = key == VK_D ? "D/Right" : key == VK_W ? "W/Up" : key == VK_A ? "A/Left" : "S/Down";
                Plugin.Log.LogInfo($"[Bot] 🎹 Tutorial Move: Pressing Key {kName} (Physical)");
                LogStateAction($"TUTORIAL_MOVE: PressKey {kName}");

                // Release after brief delay (handled by next Update or immediate? 
                // Better to hold it. But Update calls this every 1.5s. 
                // We should release previous key first?)
                
                // Simple approach: Press and Release immediately implies "tap". 
                // Movement usually needs "hold".
                // Let's release 100ms later using a Thread or just hope tap works. 
                // User said "ấn nút", maybe multiple taps works. 
                // Let's try Press -> Sleep 200ms -> Release
                
                // Note: Block main thread 200ms is bad but expected for tutorial step
                System.Threading.Thread.Sleep(500);
                keybd_event(key, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, 0);

                _tutorialMoveDir++;

                // === 2. Fallback: Joystick UI (như cũ) ===
                // (Giữ lại code cũ phòng khi key không ăn nếu window mất focus)
                string[] joystickNames = { "Joystick", "JoystickPad", "MoveJoystick", "MovePad", "VirtualJoystick", "TouchPad" };
                foreach (var jName in joystickNames)
                {
                    var joyObj = GameObject.Find(jName);
                    if (joyObj != null && joyObj.activeSelf)
                    {
                        var eventSystem = EventSystem.current;
                        if (eventSystem != null)
                        {
                            var pointerData = new PointerEventData(eventSystem);
                            Vector2 joyCenter = joyObj.transform.position;
                            float dragDist = 50f;
                            Vector2[] dirs = { new Vector2(dragDist, 0), new Vector2(0, dragDist), new Vector2(-dragDist, 0), new Vector2(0, -dragDist) };
                            Vector2 dragDir = dirs[_tutorialMoveDir % 4];
                            pointerData.position = joyCenter + dragDir;
                            pointerData.delta = dragDir;
                            ExecuteEvents.Execute(joyObj, pointerData, ExecuteEvents.dragHandler);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Bot] TryTutorialMovement error: {ex.Message}");
            }
        }

        private float _tameInteractTimer = 0f;
        
        /// <summary>
        /// Logic chuyên biệt cho quest "Thuần phục": tìm target trong 3D world và "click" vào nó
        /// </summary>
        private void TryTameInteraction()
        {
            try
            {
                if (_currentQuestInfo == null || string.IsNullOrEmpty(_currentQuestInfo.Target)) return;
                
                string targetName = _currentQuestInfo.Target;
                var obj = FindClosestObject(targetName);
                
                if (obj != null)
                {
                    float dist = Vector3.Distance(_lastPlayerPos, obj.transform.position);
                    
                    // Nếu đang ở gần (< 5m), thử tương tác
                    if (dist <= 5f)
                    {
                        Plugin.Log.LogInfo($"[Bot] 🎯 Tame Target found: '{obj.name}' at {dist:F1}m. Attempting interaction...");
                        
                        // 1. Thử gọi các method tương tác phổ biến qua Reflection
                        bool interacted = false;
                        var comps = obj.GetComponents<Component>();
                        foreach (var c in comps)
                        {
                            if (c == null) continue;
                            var type = c.GetIl2CppType();
                            
                            // Method names to search for
                            string[] interactMethods = { "Interact", "OnClick", "OnPointerClick", "OnMouseDown", "Talk", "Execute" };
                            
                            foreach (var mName in interactMethods)
                            {
                                try
                                {
                                    Il2CppSystem.Reflection.MethodInfo m = null;
                                    foreach (var method in type.GetMethods()) { if (method.Name.Equals(mName, StringComparison.OrdinalIgnoreCase)) { m = method; break; } }
                                    if (m != null)
                                    {
                                        // OnPointerClick typically needs PointerEventData, others might be parameterless
                                        if (m.GetParameters().Length == 0)
                                        {
                                            m.Invoke(c, null);
                                            Plugin.Log.LogInfo($"[Bot] 🤝 Interacted with '{obj.name}' via {type.Name}.{m.Name}()");
                                            interacted = true;
                                            break;
                                        }
                                    }
                                }
                                catch { }
                            }
                            if (interacted) break;
                        }

                        if (interacted)
                        {
                            LogStateAction($"TAME_INTERACT: Clicked {obj.name}");
                            LogStateAction($"TAME_INTERACT: Clicked {obj.name}");
                            _consecutivePathfindFailures = 0;
                            // Auto-Attack after tame interaction
                            EnsureAutoAttack();
                        }
                        else
                        {
                            // Fallback: Nếu không tìm thấy method, log ra để user biết tên Component
                            var compNames = new System.Collections.Generic.List<string>();
                            foreach (var comp in comps) { if (comp != null) compNames.Add(comp.GetIl2CppType().Name); }
                            string compList = string.Join(", ", compNames);
                            Plugin.Log.LogWarning($"[Bot] ⚠️ Found target but no interact method. Components: [{compList}]");
                            
                            // FORCE Auto-Attack anyway (cho trường hợp mob cần đánh)
                            EnsureAutoAttack();
                        }
                    }
                    else
                    {
                        // Chưa đến gần -> Pathfind sẽ lo, nhưng ta log progress
                        // Plugin.Log.LogInfo($"[Bot] 🏃 Tame target '{obj.name}' is too far ({dist:F1}m). Pathfinding...");
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Bot] TryTameInteraction error: {ex.Message}");
            }
        }

        // ======================== FALLBACK SEARCH ========================
        private void TryFindTargetAndMove()
        {
            if (_currentQuestInfo == null || string.IsNullOrEmpty(_currentQuestInfo.Target)) return;

            string target = _currentQuestInfo.Target;
            Plugin.Log.LogInfo($"[Bot] Active Search: Looking for '{target}'...");

            // 1. Search target name
            var obj = FindClosestObject(target);
            
            // 2. Search specific hints (Tàu Sayyan -> Tau / Ship / Arrow)
            if (obj == null && (target.Contains("Tàu") || target.Contains("Tìm")))
            {
                obj = FindClosestObject("Tau");
                if (obj == null) obj = FindClosestObject("Ship");
                if (obj == null) obj = FindClosestObject("Arrow"); // Mũi tên hướng dẫn
                if (obj == null) obj = FindClosestObject("Indicator");
            }
            
            // 3. Search for "Thịt Sói" (Drop item?)
            if (obj == null && target.Contains("Thịt"))
            {
                 obj = FindClosestObject("Item"); 
            }

            if (obj != null)
            {
                Plugin.Log.LogInfo($"[Bot] Active Search: Found '{obj.name}' at {obj.transform.position}.");
                Plugin.Log.LogInfo($"[Bot] >>> PLEASE MOVE MANUALLY TO TARGET <<<");
                LogStateAction($"ACTIVE_SEARCH: Found {obj.name} -> Please Move Manually");
                _consecutivePathfindFailures = 0; // Reset to avoid spam
            }
        }

        // ======================== HELPER: DIALOG DETECTION & HANDLING ========================
        
        private float _dialogueCooldownTimer = 0f;
        private float _lastAutoAttackTriggerTime = 0f;

        /// <summary>
        /// Ensures game's Auto Attack mode is enabled.
        /// Throttled to prevent spamming.
        /// </summary>
        private void EnsureAutoAttack()
        {
            try
            {
                // Respect user's manual toggle (F3 or command) — unless LiteMode is ON
                if (!_autoAttackFlag && !_liteMode) return;
                if (!_autoAttackEnabled) return; // Respect 'toggle attack' command

                if (Time.time - _lastAutoAttackTriggerTime < 5.0f) return; // Throttle to 5s to reduce log spam

                // Sync with game's internal state
                bool gameOnAuto = false;
                if (_mainGame != null) {
                    try { gameOnAuto = BotHelper.CallMethodReturn<bool>(_mainGame, "get_isOnAutoMode"); } catch {}
                }

                if (!gameOnAuto)
                {
                    Plugin.Log.LogInfo("[Bot] ⚔️ Auto-Trigger: Re-enabling Auto Attack (Native State was OFF)");
                    ToggleAutoAttack(true);
                    _lastAutoAttackTriggerTime = Time.time;
                }
                else {
                    _autoAttackEnabled = true; // Sync local flag
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Bot] EnsureAutoAttack error: {ex.Message}");
            }
        }

        /// <summary>
        /// Global check: Có bất kỳ dialog quan trọng nào đang mở không?
        /// Bao gồm: NPC Interact, Story Panel, Dialog Panel...
        /// </summary>
        private bool IsDialogueOpen()
        {
            try
            {
                // 1. NpcInteractPanel(Clone) - Common NPC options
                var npcPanel = GameObject.Find("NpcInteractPanel(Clone)");
                if (npcPanel != null && npcPanel.activeInHierarchy) return true;

                // 2. DialogPanel (Generic)
                var dialogPanel = GameObject.Find("DialogPanel");
                if (dialogPanel != null && dialogPanel.activeInHierarchy) return true;

                // 3. StoryPanel (Quest story)
                var storyPanel = GameObject.Find("StoryPanel");
                if (storyPanel != null && storyPanel.activeInHierarchy) return true;

                // 4. Dialogue (Common NPC dialogue panel)
                var dialog = GameObject.Find("Dialogue");
                if (dialog != null && dialog.activeInHierarchy) return true;

                // 5. DialogueMessageHolder
                var holder = GameObject.Find("DialogueMessageHolder");
                if (holder != null && holder.activeInHierarchy) return true;
                
                // 4. New User Requested Panels
                if (CheckPanel("DlgNpc")) return true;
                if (CheckPanel("PanelMenu")) return true;
                if (CheckPanel("ConfirmBox")) return true;

                // 4. NpcInteractPanel original (sometimes used)
                var npcPanelOrig = GameObject.Find("NpcInteractPanel");
                if (npcPanelOrig != null && npcPanelOrig.activeInHierarchy) return true;

                return false;
            }
            catch { return false; }
        }

        private bool CheckPanel(string name)
        {
             var obj = GameObject.Find(name);
             return obj != null && obj.activeInHierarchy;
        }

        /// <summary>
        /// Tự động xử lý hội thoại: Chọn option đầu tiên hoặc Confirm
        /// </summary>
        private void HandleDialogue()
        {
            try 
            {
                 // Logic đơn giản: Tìm tất cả button trong các panel đang mở và click cái đầu tiên
                 // Ưu tiên các panel quan trọng
                 
                  GameObject[] panels = {
                    GameObject.Find("NpcInteractPanel(Clone)"),
                    GameObject.Find("DialogPanel"),
                    GameObject.Find("StoryPanel"),
                    GameObject.Find("DlgNpc"),
                    GameObject.Find("ConfirmBox"),
                    GameObject.Find("PopupPanel"),
                    GameObject.Find("ChoiceZonePanel") // Added for zone switch observation
                 };

                 foreach (var p in panels)
                 {
                     if (p != null && p.activeInHierarchy)
                     {
                         // Strategy 1: Find prioritized buttons (Quest/Complete)
                         var buttons = p.GetComponentsInChildren<Button>(true);
                         Button bestBtn = null;
                         
                         foreach (var btn in buttons)
                         {
                             if (btn == null || !btn.interactable) continue;
                             
                             // --- SMART LOOP PREVENTION ---
                             string btnSig = $"{p.name}/{btn.name}/{btn.transform.GetSiblingIndex()}";
                             if (_ignoredButtonSignatures.Contains(btnSig)) continue;
                             // -----------------------------

                             string btnText = "";
                             var tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
                             if (tmp != null) btnText = tmp.text ?? "";

                             string l = btnText.ToLower();
                             // Keywords for quest progression (Vietnamese + English)
                             if (l.Contains("nhiệm vụ") || l.Contains("hoàn thành") || l.Contains("nhận") || 
                                 l.Contains("xác nhận") || l.Contains("đồng ý") || l.Contains("tiếp tục") ||
                                 l.Contains("trả") || l.Contains("giao") || l.Contains("skip") || l.Contains("bỏ qua") ||
                                 l.Contains("đóng") || l.Contains("close") || l.Contains("ok") ||
                                 l.Contains("quest") || l.Contains("complete") || l.Contains("accept") || l.Contains("confirm") ||
                                 l.Contains("thách đấu"))
                             {
                                 bestBtn = btn;
                                 break; // Take the first matching priority button
                             }
                         }

                         // Strategy 2: If no priority button, take the first available one (that isn't ignored)
                         if (bestBtn == null)
                         {
                             foreach (var btn in buttons)
                             {
                                 if (btn == null || !btn.interactable) continue;
                                 string btnSig = $"{p.name}/{btn.name}/{btn.transform.GetSiblingIndex()}";
                                 if (_ignoredButtonSignatures.Contains(btnSig)) continue;
                                 bestBtn = btn;
                                 break;
                             }
                         }

                         if (bestBtn != null)
                         {
                             // Check for loop
                             string currentSig = $"{p.name}/{bestBtn.name}/{bestBtn.transform.GetSiblingIndex()}";
                             if (currentSig == _lastClickedButtonSignature)
                             {
                                 _clickSpamCount++;
                                 if (_clickSpamCount > 5) // Threshold: 5 times same button
                                 {
                                     Plugin.Log.LogWarning($"[Bot] ⚠️ Detected Loop on button '{bestBtn.name}' ({currentSig}). Ignoring it.");
                                     _ignoredButtonSignatures.Add(currentSig);
                                     _clickSpamCount = 0;
                                     _lastClickedButtonSignature = ""; // Reset to allow switching
                                     return; // Skip this frame, next frame will pick next button
                                 }
                             }
                             else
                             {
                                 _clickSpamCount = 0;
                                 _lastClickedButtonSignature = currentSig;
                             }

                             Plugin.Log.LogInfo($"[Bot] 🗣️ HandleDialogue: Clicking '{bestBtn.name}' (Text: {bestBtn.name}) in '{p.name}' [Spam: {_clickSpamCount}]");
                             _botInvoking = true;
                             bestBtn.onClick.Invoke();
                             _botInvoking = false;
                             _dialogueCooldownTimer = 1.0f; // Cooldown 1s
                             _lastDialogActionTime = Time.time;
                             return;
                         }
                     }
                 }

                 // === FALLBACK: If no suitable buttons found or all ignored ===
                 // If dialog is open but we can't click anything (or ignored everything)
                 if (Time.time - _lastDialogActionTime > 3.0f)
                 {
                     Plugin.Log.LogWarning("[Bot] ⚠️ Stuck in Dialog (No valid buttons). Fallback: Click Quest Panel.");
                     TriggerAutoPathfind(); // Will try to click quest panel
                     _lastDialogActionTime = Time.time; // Reset timer to avoid spamming fallback
                     _ignoredButtonSignatures.Clear(); // Clear ignore list to give them another chance after a "refresh"
                 }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Bot] HandleDialogue error: {ex.Message}");
            }
        }

        private GameObject FindClosestObject(string partialName)
        {
            if (string.IsNullOrEmpty(partialName)) return null;
            
            var allObjs = FindObjectsOfType<Transform>();
            GameObject closest = null;
            float minDst = 9999f;
            Vector3 myPos = _lastPlayerPos; // Fixed variable name

            foreach (var t in allObjs)
            {
                if (t.gameObject.activeInHierarchy && t.name.IndexOf(partialName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Filter out UI
                    if (t.gameObject.layer == 5) continue; // 5 = UI usually

                    float d = Vector3.Distance(myPos, t.position);
                    if (d < minDst)
                    {
                        minDst = d;
                        closest = t.gameObject;
                    }
                }
            }
            return closest;
        }

        /// <summary>
        /// Tìm skill button trên UI và click (giả lập phím bấm)
        /// </summary>
        private void TryClickSkillButton(int skillNum)
        {
            try
            {
                // List các pattern tên button có thể có (cần check hierarchy thực tế)
                // Giả sừ: SkillButton1, Skill_1, Slot1, AttackButton1...
                string[] patterns = { 
                    $"SkillButton{skillNum}", 
                    $"SkillButton_{skillNum}", 
                    $"Skill{skillNum}", 
                    $"Skill_{skillNum}", 
                    $"Slot{skillNum}", 
                    $"Slot_{skillNum}", 
                    $"AttackButton{skillNum}" 
                };

                bool clicked = false;
                foreach (var name in patterns)
                {
                    var btnObj = GameObject.Find(name);
                    // Nếu tìm thấy theo tên chính xác
                    if (btnObj != null && btnObj.activeSelf)
                    {
                        var btn = btnObj.GetComponent<Button>();
                        if (btn == null) btn = btnObj.GetComponentInChildren<Button>();
                        
                        if (btn != null && btn.interactable)
                        {
                            _botInvoking = true;
                            btn.onClick.Invoke();
                            _botInvoking = false;
                            Plugin.Log.LogInfo($"[Bot] ⚔️ Auto Skill: Clicked {name} (Skill {skillNum})");
                            LogStateAction($"SKILL_CLICK: {name} (Skill {skillNum})");
                            clicked = true;
                            break;
                        }
                    }
                }
                
                // Nếu chưa click được, thử tìm trong cụm SkillPanel/AttackPanel
                if (!clicked)
                {
                    // Fallback strategy: tìm cha rồi tìm con
                    string[] parentNames = { "SkillPanel", "AttackStart", "MainCanvas", "HUDCanvas" };
                    foreach (var pName in parentNames)
                    {
                         var parent = GameObject.Find(pName);
                         if (parent != null)
                         {
                             // Tìm đệ quy
                             foreach (var name in patterns)
                             {
                                 var child = BotHelper.FindInactiveChild(parent.transform, name); // Helper function
                                 if (child != null && child.gameObject.activeSelf)
                                 {
                                     var btn = child.GetComponent<Button>();
                                     if (btn != null && btn.interactable)
                                     {
                                         _botInvoking = true;
                                         btn.onClick.Invoke();
                                         _botInvoking = false;
                                         Plugin.Log.LogInfo($"[Bot] ⚔️ Auto Skill (Hierarchy): Clicked {name} in {pName}");
                                         LogStateAction($"SKILL_CLICK: {name} (Skill {skillNum})");
                                         clicked = true;
                                         goto EndSearch;
                                     }
                                 }
                             }
                         }
                    }
                }
                
                EndSearch:
                if (!clicked)
                {
                    Plugin.Log.LogWarning($"[Bot] ⚠️ Could not find Skill Button for Skill {skillNum}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Bot] TryClickSkillButton error: {ex.Message}");
            }
        }

        /// <summary>
        /// Lấy quest text hiện tại từ DescriptionMission trên ShortMissionPanel
        /// </summary>
        private string GetCurrentQuestText()
        {
            try
            {
                if (_shortMissionPanel == null)
                    _shortMissionPanel = BotHelper.FindSingletonByType("ShortMissionPanel");

                if (_shortMissionPanel != null && _shortMissionPanel.gameObject.activeSelf)
                {
                    var texts = _shortMissionPanel.gameObject.GetComponentsInChildren<TextMeshProUGUI>(true);
                    foreach (var t in texts)
                    {
                        if (t == null) continue;
                        if (t.gameObject.name == "DescriptionMission")
                            return t.text ?? "";
                    }
                }
            }
            catch { }
            return "";
        }

        private void LogAutoQuestStatus()
        {
            if (_autoMissionManager == null) return;
            try
            {
                var state = BotHelper.CallMethodReturn<bool>(_autoMissionManager, "get_state");
                Plugin.Log.LogInfo($"[Bot] Auto Quest: {(state ? "ĐANG CHẠY" : "TẮT")}");
            }
            catch { }

            // === Map & Zone Info ===
            try
            {
                string mapName = GetCurrentMapName();
                string zoneName = GetCurrentZoneName();
                Vector3 playerPos = GetPlayerPosition();

                if (!string.IsNullOrEmpty(mapName) || !string.IsNullOrEmpty(zoneName))
                {
                    Plugin.Log.LogInfo($"[Bot] 🗺️ Map: {(string.IsNullOrEmpty(mapName) ? "???" : mapName)} | Zone: {(string.IsNullOrEmpty(zoneName) ? "???" : zoneName)} | Pos: ({playerPos.x:F0}, {playerPos.y:F0})");
                }
            }
            catch { }

            // Also log quest text
            LogQuestStatus();
        }

        /// <summary>
        /// Lấy tên map hiện tại từ MiniMap/MapName (TextMeshProUGUI) hoặc MapGateway
        /// </summary>
        private string GetCurrentMapName()
        {
            // 1. Check Hardcoded Dictionary (Fastest & Most Accurate)
            if (_sceneNameDict.TryGetValue(_currentScene, out string fixedName))
                return fixedName;

            // 2. MapGateway (TMPro)
            if (_mapNameObject == null)
            {
                 var mapGateway = GameObject.Find("MapGateway");
                 if (mapGateway != null)
                 {
                     var txt = mapGateway.GetComponentInChildren<TextMeshProUGUI>();
                     if (txt != null)
                     {
                         _mapNameObject = txt.gameObject;
                         return txt.text.Trim();
                     }
                 }
            }
            else
            {
                var txt = _mapNameObject.GetComponent<TextMeshProUGUI>();
                if (txt != null) return txt.text.Trim();
            }

            return _currentScene; // Fallback
        }

        /// <summary>
        /// Lấy tên zone/khu vực từ ZoneObject hoặc ClanMemberZonePanel
        /// </summary>
        private string GetCurrentZoneName()
        {
            try
            {
                // Strategy 0: MapGateway (Direct field: miniMapText)
                var mapGatewayGo = GameObject.Find("MapGateway");
                if (mapGatewayGo != null)
                {
                    var mapGateway = mapGatewayGo.GetComponent("MapGateway");
                    if (mapGateway != null)
                    {
                        string zoneName = BotHelper.GetFieldValue<string>(mapGateway.Cast<MonoBehaviour>(), "miniMapText");
                        if (!string.IsNullOrEmpty(zoneName)) return zoneName.Trim();
                    }
                }

                // Strategy 1: MiniMap UI Fallback
                var miniMap = GameObject.Find("MiniMap");
                if (miniMap != null)
                {
                    var zoneGo = BotHelper.FindInactiveChild(miniMap.transform, "ZoneObject");
                    if (zoneGo != null)
                    {
                        var tmpUI = zoneGo.GetComponentInChildren<TextMeshProUGUI>(true);
                        if (tmpUI != null && !string.IsNullOrEmpty(tmpUI.text))
                            return tmpUI.text.Trim();
                    }
                }

                return _currentScene ?? "";
            }
            catch { }
            return "";
        }

        /// <summary>
        /// Lấy vị trí player hiện tại
        /// </summary>
        private Vector3 GetPlayerPosition()
        {
            if (_lastPlayerPos != Vector3.zero) return _lastPlayerPos;
             // Fallback logic if needed
            return Vector3.zero;
        }

        /// <summary>
        /// Lấy cấp độ nhân vật từ UI TextLevelDisplay
        /// </summary>
        private string GetPlayerLevel()
        {
            try
            {
                // Strategy 1: Reflection (Internal Data)
                if (_gameManager != null)
                {
                    try {
                        var mainPlayer = _gameManager.GetIl2CppType().GetMethod("MJNLNELHCDK").Invoke(_gameManager, null);
                        if (mainPlayer != null)
                        {
                            var expInfo = mainPlayer.GetIl2CppType().GetMethod("getMainPlayerExpInfo").Invoke(mainPlayer, null);
                            if (expInfo != null)
                            {
                                // MLBEOIBCDHH = Level
                                var levelField = expInfo.GetIl2CppType().GetField("MLBEOIBCDHH", (Il2CppSystem.Reflection.BindingFlags)36); // Instance=4 | NonPublic=32
                                if (levelField != null)
                                {
                                    int level = levelField.GetValue(expInfo).Unbox<int>();
                                    if (level > 0) return level.ToString();
                                }
                            }
                        }
                    } catch {}
                }

                // Strategy 2: UI Search (Fallback)
                var levelGo = GameObject.Find("TextLevelDisplay");
                if (levelGo != null)
                {
                    var allTexts = levelGo.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true);
                    foreach (var t in allTexts)
                    {
                        if (t != null && !string.IsNullOrWhiteSpace(t.text))
                        {
                            string levelText = t.text.Trim();
                            if (levelText.Contains("."))
                                return levelText.Split('.').Last().Trim();
                            return levelText;
                        }
                    }
                }
            }
            catch { }
            return "0";
        }

        // ======================== AUTO ATTACK ========================

        private void ToggleAutoAttack(bool enable)
        {
            try
            {
                if (_mainGame != null)
                {
                    // Use the game's native method for reliable toggling
                    try {
                        BotHelper.CallMethod(_mainGame, "setPlayerOnAutoMode", new object[] { enable });
                    } catch (Exception ex) {
                        Plugin.Log.LogWarning($"[Bot] Native AutoMode call failed: {ex.Message}");
                    }
                }

                // UI Button Fallback/Sync
                if (_autoAttackBtn == null)
                {
                    var go = GameObject.Find("AutoAttackButton");
                    if (go != null) _autoAttackBtn = go.GetComponent<Button>();
                }

                if (enable && !_autoAttackEnabled)
                {
                    bool clicked = false;
                    if (_autoAttackBtn != null) 
                    {
                        Plugin.Log.LogInfo("[Bot] ⚔️ Clicking AutoAttackButton UI...");
                        
                        // Force active if needed
                        if (!_autoAttackBtn.gameObject.activeInHierarchy) {
                            Plugin.Log.LogWarning("[Bot] ⚠️ AutoAttackButton hidden, forcing active...");
                            _autoAttackBtn.gameObject.SetActive(true);
                        }

                        _autoAttackBtn.onClick.Invoke();
                        clicked = true;
                        
                        // Fallback: Try SendMessage for custom handlers (OnPointerClick)
                        _autoAttackBtn.SendMessage("OnPointerClick", new PointerEventData(EventSystem.current), SendMessageOptions.DontRequireReceiver);
                    }
                    else 
                    {
                        // Last ditch effort: Find it continuously if null
                        var go = GameObject.Find("AutoAttackButton");
                        if (go != null) {
                             go.SendMessage("OnPointerClick", new PointerEventData(EventSystem.current), SendMessageOptions.DontRequireReceiver);
                             Plugin.Log.LogInfo("[Bot] ⚔️ Clicked AutoAttackButton via GameObject.Find fallback.");
                             clicked = true;
                        }
                    }

                    if (clicked) Plugin.Log.LogInfo("[Bot] ✅ Auto Attack: BẬT (UI Clicked)");
                    else Plugin.Log.LogWarning("[Bot] ⚠️ Auto Attack: BẬT (UI Not Found)");
                    
                    _autoAttackEnabled = true;
                }
                else if (!enable && _autoAttackEnabled)
                {
                    if (_autoAttackBtn != null) 
                    {
                        Plugin.Log.LogInfo("[Bot] ⛔ Clicking AutoAttackButton UI to Disable...");
                         _autoAttackBtn.onClick.Invoke();
                         _autoAttackBtn.SendMessage("OnPointerClick", new PointerEventData(EventSystem.current), SendMessageOptions.DontRequireReceiver);
                    }
                    _autoAttackEnabled = false;
                    Plugin.Log.LogInfo("[Bot] ⛔ Auto Attack: TẮT");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Bot] Auto Attack error: {ex.Message}");
            }
        }

        // ======================== AUTO INTERACT ========================

        /// <summary>
        /// Smart Auto Interact: chỉ click khi InteractButton MỚI xuất hiện
        /// (transition từ invisible → visible), không spam
        /// </summary>
        private void TryAutoInteract()
        {
            try
            {
                if (_interactBtn == null)
                {
                    var go = GameObject.Find("InteractButton");
                    if (go != null)
                    {
                        _interactBtn = go.GetComponent<Button>();
                        if (_interactBtn == null)
                            _interactBtn = go.GetComponentInChildren<Button>();
                    }
                }

                bool isVisible = _interactBtn != null && _interactBtn.gameObject.activeSelf && _interactBtn.interactable;

                // Chỉ click khi button MỚI xuất hiện (false → true)
                if (isVisible && !_interactBtnWasVisible)
                {
                    _interactBtn.onClick.Invoke();
                    LogActivity("🤝 Auto Interact: click NPC button");
                    LogStateAction("CLICK InteractButton (NPC nearby)");
                }
                _interactBtnWasVisible = isVisible;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Bot] Interact error: {ex.Message}");
            }
        }

        // ======================== AUTO CLICK NPC DIALOG ========================

        private int _noNpcButtonCycles = 0; // Track consecutive cycles with no buttons found

        /// <summary>
        /// Tự động click qua NPC dialog:
        /// 1. NextPlace — tiếp tục hội thoại NPC
        /// 2. InteractPanelButton — nhận nhiệm vụ / hoàn thành quest
        /// 3. Các nút popup khác (OK, Đồng ý, Xác nhận)
        /// Fallback: Nếu không thấy nút, click ra ngoài (Backdrop) hoặc đóng panel để refresh
        /// </summary>
        private void TryClickNpcDialog()
        {
            try
            {
                // === 1. Tìm NpcInteractPanel (dialog NPC) ===
                var npcPanel = GameObject.Find("NpcInteractPanel(Clone)");
                if (npcPanel != null && npcPanel.activeSelf && npcPanel.activeInHierarchy)
                {
                    // === Capture Current Dialog Text for anti-loop ===
                    string currentDialogText = "";
                    try {
                        var texts = npcPanel.GetComponentsInChildren<TextMeshProUGUI>(true);
                        foreach(var t in texts) {
                            if (t.gameObject.name == "Text" || t.gameObject.name == "Content" || t.gameObject.name == "Desc") {
                                if (!string.IsNullOrEmpty(t.text)) {
                                    currentDialogText = t.text.Trim();
                                    break;
                                }
                            }
                        }
                    } catch {}

                    // Detect loop: same text + same panel
                    if (currentDialogText == _lastNpcDialogText && _lastNpcDialogText != "") {
                        // Text didn't change since last cycle
                        // Wait a bit or increment stuck counter
                    } else {
                        // Text changed, reset stuck counter
                        _npcDialogStuckCounter = 0;
                    }
                    _lastNpcDialogText = currentDialogText;

                    // === BUG FIX v43: Tìm tất cả button trong panel (kể cả nested) ===
                    var allButtons = npcPanel.GetComponentsInChildren<Button>(true);

                    // === NEW: Capture NPC Name ===
                    if (string.IsNullOrEmpty(_lastNpcName) || _noNpcButtonCycles == 0)
                    {
                         try {
                            var texts = npcPanel.GetComponentsInChildren<TextMeshProUGUI>(true);
                            foreach(var t in texts)
                            {
                                if (t.gameObject.name == "Name" || t.gameObject.name == "Title" || t.gameObject.name == "NpcName")
                                {
                                    if (!string.IsNullOrEmpty(t.text))
                                    {
                                        _lastNpcName = t.text;
                                        Plugin.Log.LogInfo($"[Bot] 🗣️ NPC Found: {_lastNpcName}"); 
                                        break;
                                    }
                                }
                            }
                         } catch {}
                    }
                    
                    Button questBtn = null;
                    Button talkBtn = null; // NextPlace, Nói chuyện
                    Button anyBtn = null;
                    Button priorityBtn = null; // From DB Sequence
                    Button iconBtn = null; // NEW: Priority for buttons with ! or ? icons

                    // Check Smart Dialog Sequence first
                    string preferredBtnText = (_currentQuestDialogSequence.Count > 0) ? _currentQuestDialogSequence[0] : "";

                    foreach (var btn in allButtons)
                    {
                        if (btn == null || !btn.gameObject.activeSelf || !btn.interactable) continue;
                        
                        string btnName = btn.gameObject.name ?? "";
                        string btnText = "";
                        try {
                            btnText = BotHelper.btnTextFromButton(btn);
                        } catch {}

                        // Anti-loop: skip if we've clicked this button too many times on the same text
                        if (_npcDialogStuckCounter >= MAX_NPC_DIALOG_STRICT_REPEATS && btnName == _lastClickedNpcButton && btnName != "NextPlace") {
                            continue; 
                        }

                        // Skip close buttons
                        if (btnName.Contains("Close") || btnName.Contains("Exit") || btnName == "BtnClose") continue;

                        // 0. Icon Priority (!) or (?)
                        try {
                            var images = btn.GetComponentsInChildren<UnityEngine.UI.Image>(true);
                            foreach(var img in images) {
                                if (img.sprite != null) {
                                    string spriteName = img.sprite.name.ToLower();
                                    if (spriteName.Contains("icon_quest") || spriteName.Contains("!") || spriteName.Contains("task") || spriteName.Contains("quest")) {
                                        iconBtn = btn;
                                        break;
                                    }
                                }
                            }
                        } catch {}

                        // 0. Smart Dialog Priority (from DB)
                        if (!string.IsNullOrEmpty(preferredBtnText) && btnText.Contains(preferredBtnText))
                        {
                            priorityBtn = btn;
                            break; // Immediate priority!
                        }

                        // 1. Quest keywords (PRIORITY)
                        if (btnText.Contains("Nhiệm vụ") || btnText.Contains("Nhận") || 
                            btnText.Contains("Hoàn thành") || btnText.Contains("Trả") ||
                            btnText.Contains("Đồng ý") || btnText.Contains("Quest") ||
                            btnText.Contains("Xác nhận") || btnText.Contains("OK"))
                        {
                            questBtn = btn;
                        }

                        // 1b. Name-based priority
                        if (btnName == "InteractPanelButton") 
                        {
                            if (questBtn == null) questBtn = btn;
                        }

                        // 2. Talk keywords (NextPlace)
                        if (btnName == "NextPlace" || btnText.Contains("Nói chuyện") || btnText.Contains("Tiếp"))
                        {
                            if (talkBtn == null) talkBtn = btn;
                        }

                        // 3. Fallback
                        if (anyBtn == null) anyBtn = btn;
                    }

                    // Loop recovery: if stuck, and we are about to click the same button, try to find another or close
                    if (_npcDialogStuckCounter >= MAX_NPC_DIALOG_STRICT_REPEATS) {
                        Plugin.Log.LogWarning($"[Bot] 🔄 NPC Dialog is STUCK on '{_lastClickedNpcButton}'. Trying to force exit or switch.");
                        // Try any other button that isn't the stuck one
                        if (questBtn != null && questBtn.gameObject.name != _lastClickedNpcButton) priorityBtn = questBtn;
                        else if (talkBtn != null && talkBtn.gameObject.name != _lastClickedNpcButton) priorityBtn = talkBtn;
                        else if (anyBtn != null && anyBtn.gameObject.name != _lastClickedNpcButton) priorityBtn = anyBtn;
                        else {
                            // Find close button as last resort
                            foreach (var btn in allButtons) {
                                if (btn.gameObject.name.Contains("Close") || btn.gameObject.name.Contains("Exit") || btn.gameObject.name == "BtnClose") {
                                    priorityBtn = btn;
                                    break;
                                }
                            }
                        }
                        
                        if (priorityBtn == null) {
                            Plugin.Log.LogInfo("[Bot] ⛔ Can't break loop. Closing panel manually.");
                            npcPanel.SetActive(false);
                            _npcDialogStuckCounter = 0;
                            return;
                        }
                    }

                    // Execution click with priority
                    var targetBtn = iconBtn ?? priorityBtn ?? questBtn ?? talkBtn ?? anyBtn;
                    
                    if (targetBtn != null)
                    {
                        _noNpcButtonCycles = 0;
                        string btnName = targetBtn.gameObject.name;
                        
                        // Increment stuck counter if same button
                        if (btnName == _lastClickedNpcButton && btnName != "NextPlace") {
                            _npcDialogStuckCounter++;
                        } else {
                            _npcDialogStuckCounter = 0;
                            _lastClickedNpcButton = btnName;
                        }

                        string btnTextLog = BotHelper.btnTextFromButton(targetBtn);
                        Plugin.Log.LogInfo($"[Bot] {(iconBtn != null ? "❗ Icon" : "🧠 Smart")} Dialog Click: '{btnName}' ({btnTextLog}) [Stuck:{_npcDialogStuckCounter}]");
                        LogStateAction($"CLICK NPC_BTN: {btnName}");
                        _botInvoking = true;
                        targetBtn.onClick.Invoke();
                        _botInvoking = false;
                        
                        // Remove used button from sequence (FIFO) if it was the sequence button
                        if (targetBtn == priorityBtn && _currentQuestDialogSequence.Count > 0) 
                            _currentQuestDialogSequence.RemoveAt(0);
                    }
                    else
                    {
                        _noNpcButtonCycles++;
                        // Fallback: If no buttons found after multiple cycles, try clicking backdrop or closing
                        if (_noNpcButtonCycles > 10)
                        {
                            Plugin.Log.LogInfo("[Bot] ⛔ No dialog buttons found for too long. Forcing close.");
                            npcPanel.SetActive(false);
                            _noNpcButtonCycles = 0;
                        }
                    }
                }
                else
                {
                    _noNpcButtonCycles = 0; // Reset khi không có panel
                    _npcDialogStuckCounter = 0;
                    _lastClickedNpcButton = "";
                    _lastNpcDialogText = "";
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Bot] NPC Dialog error: {ex.Message}");
            }
        }



        /// <summary>
        /// Tìm Button con theo tên trong GameObject
        /// </summary>
        private Button FindChildButton(GameObject parent, string childName)
        {
            var transform = parent.transform;
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child == null) continue;
                if (child.gameObject.name == childName)
                    return child.GetComponent<Button>();
                // Tìm sâu hơn 1 level
                for (int j = 0; j < child.childCount; j++)
                {
                    var grandChild = child.GetChild(j);
                    if (grandChild != null && grandChild.gameObject.name == childName)
                        return grandChild.GetComponent<Button>();
                }
            }
            return null;
        }

        // ======================== QUEST MONITOR ========================

        private void LogQuestStatus()
        {
            try
            {
                // Find ShortMissionPanel
                if (_shortMissionPanel == null)
                {
                    _shortMissionPanel = BotHelper.FindSingletonByType("ShortMissionPanel");
                }

                if (_shortMissionPanel != null && _shortMissionPanel.gameObject.activeSelf)
                {
                    // Read all TMPro text from children
                    var texts = _shortMissionPanel.gameObject.GetComponentsInChildren<TextMeshProUGUI>(true);
                    if (texts != null && texts.Length > 0)
                    {
                        Plugin.Log.LogInfo("[Bot] === QUEST STATUS ===");
                        foreach (var t in texts)
                        {
                            if (t == null) continue;
                            string txt = t.text;
                            if (string.IsNullOrWhiteSpace(txt)) continue;
                            Plugin.Log.LogInfo($"[Bot]   📋 [{t.gameObject.name}] {txt}");
                        }
                    }

                    // Also try MyUIText (game's custom text component)
                    var allComps = _shortMissionPanel.gameObject.GetComponentsInChildren<Component>(true);
                    foreach (var c in allComps)
                    {
                        if (c == null) continue;
                        var typeName = c.GetIl2CppType().Name;
                        if (typeName == "MyUIText")
                        {
                            // Try to read text property
                            try
                            {
                                var textProp = c.GetIl2CppType().GetProperty("text");
                                if (textProp != null)
                                {
                                    var val = textProp.GetGetMethod()?.Invoke(c, null);
                                    if (val != null)
                                    {
                                        string txt = val.ToString();
                                        if (!string.IsNullOrWhiteSpace(txt))
                                            Plugin.Log.LogInfo($"[Bot]   📋 [MyUIText:{c.name}] {txt}");
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
                else
                {
                    Plugin.Log.LogInfo("[Bot] 📋 ShortMissionPanel: không hiển thị");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Bot] Quest status error: {ex.Message}");
            }
        }

        // ======================== HEADLESS MODE (ENHANCED) ========================

        private void ToggleHeadless()
        {
            if (_headlessMode)
                DisableHeadless();
            else
                EnableHeadless();
        }

        private void EnableHeadless()
        {
            _headlessMode = true;
            try
            {
                // === 1. FPS tối thiểu ===
                int headlessFps = Math.Max(1, _targetFps > 0 ? _targetFps : 5);
                Application.targetFrameRate = headlessFps;
                QualitySettings.vSyncCount = 0;
                Plugin.Log.LogInfo($"[Headless] ✅ FPS={headlessFps}");

                // === 2. Tắt tất cả Camera ===
                var cameras = Camera.allCameras;
                foreach (var cam in cameras)
                    if (cam != null) cam.enabled = false;
                Plugin.Log.LogInfo($"[Headless] ✅ {cameras.Length} cameras OFF");

                // === 3. Tắt Audio hoàn toàn ===
                try
                {
                    AudioListener.pause = true;
                    AudioListener.volume = 0f;
                    var audioSources = GameObject.FindObjectsOfType<AudioSource>();
                    int audioCount = 0;
                    foreach (var src in audioSources)
                    {
                        if (src != null) { src.enabled = false; audioCount++; }
                    }
                    Plugin.Log.LogInfo($"[Headless] ✅ Audio OFF ({audioCount} sources)");
                }
                catch (Exception ex) { Plugin.Log.LogWarning($"[Headless] Audio skip: {ex.Message}"); }

                // === 4. Giảm Texture quality (mipmap limit) ===
                try
                {
                    QualitySettings.globalTextureMipmapLimit = 4; // Chỉ load mipmap nhỏ nhất
                    Plugin.Log.LogInfo("[Headless] ✅ Texture mipmap=4 (min quality)");
                }
                catch { QualitySettings.masterTextureLimit = 4; } // Fallback for older Unity

                // === 5. Thu nhỏ Resolution ===
                if (_ultraLowRes)
                {
                    Screen.SetResolution(160, 120, false);
                    Plugin.Log.LogInfo("[Headless] ✅ Resolution=160x120");
                }

                // === 6. Tắt tất cả Particle Systems ===
                try
                {
                    var particles = GameObject.FindObjectsOfType<ParticleSystem>();
                    int pCount = 0;
                    foreach (var ps in particles)
                    {
                        if (ps != null) { ps.Stop(true); ps.Clear(true); pCount++; }
                    }
                    Plugin.Log.LogInfo($"[Headless] ✅ {pCount} particles stopped");
                }
                catch (Exception ex) { Plugin.Log.LogWarning($"[Headless] Particles skip: {ex.Message}"); }

                // === 7. Force LOD thấp nhất ===
                QualitySettings.lodBias = 0.1f;
                QualitySettings.maximumLODLevel = 2;
                Plugin.Log.LogInfo("[Headless] ✅ LOD=lowest");

                // === 8. Các tối ưu khác ===
                QualitySettings.shadows = ShadowQuality.Disable;
                QualitySettings.shadowDistance = 0;
                QualitySettings.pixelLightCount = 0;
                QualitySettings.antiAliasing = 0;
                QualitySettings.softParticles = false;
                QualitySettings.realtimeReflectionProbes = false;
                QualitySettings.skinWeights = SkinWeights.OneBone;

                // === 9. Force cleanup RAM ===
                CleanMemory("headless-init");

                Plugin.Log.LogInfo("[Headless] ★ HEADLESS MODE BẬT (tối ưu tối đa)!");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Headless] Error: {ex.Message}");
            }
        }

        private void DisableHeadless()
        {
            _headlessMode = false;
            try
            {
                Application.targetFrameRate = -1;
                var cameras = Camera.allCameras;
                foreach (var cam in cameras)
                    if (cam != null) cam.enabled = true;

                AudioListener.pause = false;
                AudioListener.volume = 1f;
                var audioSources = GameObject.FindObjectsOfType<AudioSource>();
                foreach (var src in audioSources)
                    if (src != null) src.enabled = true;

                QualitySettings.globalTextureMipmapLimit = 0;
                QualitySettings.lodBias = 1f;
                QualitySettings.maximumLODLevel = 0;

                Plugin.Log.LogInfo("[Headless] ⛔ TẮT! Đã khôi phục graphics.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Headless] Disable error: {ex.Message}");
            }
        }

        // ======================== MEMORY OPTIMIZER ========================

        private void CleanMemory(string reason)
        {
            try
            {
                long beforeMB = GC.GetTotalMemory(false) / 1024 / 1024;
                Resources.UnloadUnusedAssets();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long afterMB = GC.GetTotalMemory(false) / 1024 / 1024;
                Plugin.Log.LogInfo($"[MemOpt] 🧹 {reason}: {beforeMB}MB → {afterMB}MB (freed {beforeMB - afterMB}MB)");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[MemOpt] Error: {ex.Message}");
            }
        }

        // ======================== AUTO-DISMISS POPUPS (Phase 3) ========================

        /// <summary>
        /// Tự động đóng các popup chặn gameplay:
        /// 1. ReceiveGiftPanel (nhận quà) — click outside để dismiss
        /// 2. Bất kỳ panel nào có PopupBehaviour trên PopupCanvas
        /// 3. Fallback: click Backdrop (overlay toàn màn hình)
        /// </summary>
        private void TryDismissPopups()
        {
            try
            {
                // === 1. Tìm ReceiveGiftPanel(Clone) — popup nhận quà ===
                var receiveGift = GameObject.Find("ReceiveGiftPanel(Clone)");
                if (receiveGift != null && receiveGift.activeSelf && receiveGift.activeInHierarchy)
                {
                    // Thử gọi method close/hide trên PopupBehaviour
                    bool dismissed = false;
                    var comps = receiveGift.GetComponents<Component>();
                    foreach (var comp in comps)
                    {
                        if (comp == null) continue;
                        var typeName = comp.GetIl2CppType().Name;
                        if (typeName == "PopupBehaviour" || typeName == "ReceiveGiftPanel")
                        {
                            // Thử các method đóng phổ biến
                            string[] closeMethods = { "close", "hide", "dismiss", "onClose", "Close", "Hide" };
                            foreach (var methodName in closeMethods)
                            {
                                try
                                {
                                    var methods = comp.GetIl2CppType().GetMethods();
                                    foreach (var m in methods)
                                    {
                                        if (m.Name.ToLower() == methodName.ToLower())
                                        {
                                            m.Invoke(comp, null);
                                            Plugin.Log.LogInfo($"[Bot] 🎁 Dismissed ReceiveGiftPanel via {typeName}.{m.Name}()");
                                            dismissed = true;
                                            break;
                                        }
                                    }
                                    if (dismissed) break;
                                }
                                catch { }
                            }
                            if (dismissed) break;
                        }
                    }

                    // Fallback: deactivate trực tiếp
                    if (!dismissed)
                    {
                        receiveGift.SetActive(false);
                        Plugin.Log.LogInfo("[Bot] 🎁 Dismissed ReceiveGiftPanel (SetActive=false)");
                        dismissed = true;
                    }

                    if (dismissed)
                    {
                        // Force pathfind lại sau khi đóng popup
                        if (_autoQuestEnabled)
                        {
                            _pathfindTimer = _pathfindInterval;
                            _sameQuestPathfindCount = 0;
                            _idleTime = _idleThreshold;
                        }
                        return;
                    }
                }

                // === 2. Scan PopupCanvas cho bất kỳ popup active nào có PopupBehaviour ===
                var popupCanvas = GameObject.Find("PopupCanvas");
                if (popupCanvas != null)
                {
                    for (int i = 0; i < popupCanvas.transform.childCount; i++)
                    {
                        var child = popupCanvas.transform.GetChild(i);
                        if (child == null || !child.gameObject.activeSelf) continue;
                        string childName = child.gameObject.name ?? "";

                        // Skip SafetyArea (luôn active, không phải popup)
                        if (childName == "SafetyArea") continue;

                        // Whitelist: Skip panels that user might want to interact with
                        string[] whitelist = { "Menu", "Shop", "Inventory", "Store", "Bag", "Equipment", "ItemDetail", "Skill", "Pet", "Friend", "FeatureMenu", "QuickFeatureMenu" };
                        bool isWhitelisted = false;
                        foreach (var w in whitelist)
                        {
                            if (childName.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                isWhitelisted = true;
                                break;
                            }
                        }
                        if (isWhitelisted) continue;

                        // Check nếu có PopupBehaviour → đây là popup cần dismiss
                        bool hasPopupBehaviour = false;
                        var childComps = child.gameObject.GetComponents<Component>();
                        foreach (var cc in childComps)
                        {
                            if (cc == null) continue;
                            if (cc.GetIl2CppType().Name == "PopupBehaviour")
                            {
                                hasPopupBehaviour = true;
                                break;
                            }
                        }

                        if (!hasPopupBehaviour) continue;

                        // Skip NpcInteractPanel — handled by TryClickNpcDialog()
                        if (childName.Contains("NpcInteractPanel")) continue;

                        // Thử click bất kỳ button close/confirm nào
                        bool clickedButton = false;
                        var btns = child.gameObject.GetComponentsInChildren<Button>(false);
                        foreach (var btn in btns)
                        {
                            if (btn == null || !btn.gameObject.activeSelf || !btn.interactable) continue;
                            string btnName = btn.gameObject.name ?? "";

                            // Tìm nút đóng/confirm phổ biến
                            if (btnName.Contains("Close") || btnName.Contains("OK") || 
                                btnName.Contains("Confirm") || btnName.Contains("Accept"))
                            {
                                btn.onClick.Invoke();
                                Plugin.Log.LogInfo($"[Bot] 🎁 Popup '{childName}': Clicked '{btnName}'");
                                clickedButton = true;
                                break;
                            }

                            // Check text labels
                            var tmpTexts = btn.gameObject.GetComponentsInChildren<TextMeshProUGUI>(false);
                            foreach (var tmp in tmpTexts)
                            {
                                if (tmp == null) continue;
                                string text = tmp.text ?? "";
                                if (text.Contains("OK") || text.Contains("Đồng ý") || text.Contains("Xác nhận") ||
                                    text.Contains("Tiếp tục") || text.Contains("Nhận") || text.Contains("Hoàn thành"))
                                {
                                    btn.onClick.Invoke();
                                    Plugin.Log.LogInfo($"[Bot] 🎁 Popup '{childName}': Clicked '{btnName}' (text='{text}')");
                                    clickedButton = true;
                                    break;
                                }
                            }
                            if (clickedButton) break;
                        }

                        // Nếu không tìm thấy button → deactivate popup
                        if (!clickedButton)
                        {
                            child.gameObject.SetActive(false);
                            Plugin.Log.LogInfo($"[Bot] 🎁 Popup '{childName}': Dismissed (SetActive=false)");
                        }

                        // Force pathfind
                        if (_autoQuestEnabled)
                        {
                            _pathfindTimer = _pathfindInterval;
                            _sameQuestPathfindCount = 0;
                            _idleTime = _idleThreshold;
                        }
                        return;
                    }
                }

                // === 3. Fallback: Click Backdrop (overlay toàn màn hình khi có popup) ===
                var backdrop = GameObject.Find("Backdrop");
                if (backdrop != null && backdrop.activeSelf && backdrop.activeInHierarchy)
                {
                    // Backdrop visible = có popup đang mở → thử click
                    var backdropBtn = backdrop.GetComponent<Button>();
                    if (backdropBtn != null && backdropBtn.interactable)
                    {
                        backdropBtn.onClick.Invoke();
                        Plugin.Log.LogInfo("[Bot] 🎁 Clicked Backdrop (dismiss popup overlay)");
                        return;
                    }

                    // Fallback: tìm component Backdrop và gọi method onClick/onPointerClick
                    var backdropComps = backdrop.GetComponents<Component>();
                    foreach (var bc in backdropComps)
                    {
                        if (bc == null) continue;
                        var typeName = bc.GetIl2CppType().Name;
                        if (typeName == "Backdrop")
                        {
                            string[] clickMethods = { "onClick", "OnClick", "onPointerClick", "close", "hide" };
                            foreach (var methodName in clickMethods)
                            {
                                try
                                {
                                    var methods = bc.GetIl2CppType().GetMethods();
                                    foreach (var m in methods)
                                    {
                                        if (m.Name == methodName)
                                        {
                                            m.Invoke(bc, null);
                                            Plugin.Log.LogInfo($"[Bot] 🎁 Clicked Backdrop via {methodName}()");
                                            return;
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Bot] Popup dismiss error: {ex.Message}");
            }
        }

        // ======================== GUIDE/TUTORIAL DISMISS (Phase 3) ========================

        /// <summary>
        /// Tự động skip tutorial/guide overlays qua GuideManager.onGuideStop()
        /// </summary>
        private void TryDismissGuide()
        {
            try
            {
                if (_guideManager == null)
                    _guideManager = BotHelper.FindSingletonByType("GuideManager");

                if (_guideManager == null || !_guideManager.gameObject.activeSelf) return;

                // Check hasGuide()
                bool hasGuide = false;
                try
                {
                    hasGuide = BotHelper.CallMethodReturn<bool>(_guideManager, "hasGuide");
                }
                catch { return; } // Không có method → skip

                if (hasGuide)
                {
                    try
                    {
                        BotHelper.CallMethodNoParam(_guideManager, "onGuideStop");
                        Plugin.Log.LogInfo("[Bot] 📖 Guide/Tutorial dismissed via GuideManager.onGuideStop()");
                    }
                    catch (Exception ex)
                    {
                        if (!_guideErrorLogged)
                        {
                            Plugin.Log.LogWarning($"[Bot] Guide dismiss error (will not repeat): {ex.Message}");
                            _guideErrorLogged = true;
                        }
                    }
                }
            }
            catch { }
        }

        // ======================== AUTO-REVIVE (Phase 3) ========================

        /// <summary>
        /// Phát hiện nhân vật chết và tự động revive
        /// Dùng AutoAttackBlackBoardComponent.needToRevive()
        /// </summary>
        private void TryAutoRevive()
        {
            try
            {
                if (_reviveCooldown > 0) return;

                if (_autoAttackBlackBoard == null)
                    _autoAttackBlackBoard = BotHelper.FindSingletonByType("AutoAttackBlackBoardComponent");
            // Fallback: search via hierarchy since AutoAttackBlackBoard is active=False
            if (_autoAttackBlackBoard == null)
            {
                var serviceGo = GameObject.Find("Service");
                if (serviceGo != null)
                {
                    var found = BotHelper.FindInactiveChild(serviceGo.transform, "AutoAttackBlackBoard");
                    if (found != null)
                    {
                        foreach (var c in found.GetComponentsInChildren<MonoBehaviour>(true))
                        {
                            if (c.GetIl2CppType().Name == "AutoAttackBlackBoardComponent")
                            {
                                _autoAttackBlackBoard = c;
                                Plugin.Log.LogInfo("[Bot] ðŸ” Found AutoAttackBlackBoard via Service hierarchy");
                                break;
                            }
                        }
                    }
                }
            }

                if (_autoAttackBlackBoard == null) return;

                bool needRevive = false;
                try
                {
                    needRevive = BotHelper.CallMethodReturn<bool>(_autoAttackBlackBoard, "needToRevive");
                }
                catch { return; }

                if (needRevive)
                {
                    Plugin.Log.LogWarning("[Bot] ☠️ Nhân vật ĐÃ CHẾT! Đang tìm nút Revive...");

                    // Tìm nút revive trên UI
                    
            // PopupCanvas search for revive buttons
            var popupCanvas = GameObject.Find("PopupCanvas");
            if (popupCanvas != null)
            {
                var popupBtns = popupCanvas.GetComponentsInChildren<Button>(true);
                foreach (var pbtn in popupBtns)
                {
                    if (pbtn == null || !pbtn.gameObject.activeSelf) continue;
                    string pbtnName = pbtn.gameObject.name ?? "";
                    if (pbtnName.Contains("Revive") || pbtnName.Contains("revive") ||
                        pbtnName.Contains("Confirm") || pbtnName.Contains("Accept"))
                    {
                        pbtn.onClick.Invoke();
                        Plugin.Log.LogInfo($"[Bot] âœ¨ Revive: Clicked '{pbtnName}' on PopupCanvas");
                        _reviveCooldown = 10f;
                        _farmState = FarmState.IDLE;
                        _farmStuckTimer = 0f;
                        return;
                    }
                }
            }

            string[] reviveNames = {
                        "ReviveButton", "BtnRevive", "ReviveBtn", "Revive", "ConfirmButton", "OKButton", "ConfirmButton", "OKButton",
                        "FreeReviveButton", "FreeRevive", "ReviveHere", "AcceptButton", "AcceptButton"
                    };

                    foreach (var name in reviveNames)
                    {
                        var go = GameObject.Find(name);
                        if (go != null && go.activeSelf)
                        {
                            var btn = go.GetComponent<Button>();
                            if (btn == null) btn = go.GetComponentInChildren<Button>();
                            if (btn != null && btn.interactable)
                            {
                                btn.onClick.Invoke();
                                Plugin.Log.LogInfo($"[Bot] ✨ Revive: Clicked '{name}'");
                                _reviveCooldown = 10f; // Cooldown 10s tránh spam
                                _farmState = FarmState.IDLE;
                                _farmStuckTimer = 0f;
                                return;
                            }
                        }
                    }

                    // Fallback: tìm bất kỳ button nào có text "Hồi sinh" / "Revive"
                    var allBtns = GameObject.FindObjectsOfType<Button>();
                    foreach (var btn in allBtns)
                    {
                        if (btn == null || !btn.gameObject.activeSelf || !btn.interactable) continue;
                        var tmpTexts = btn.gameObject.GetComponentsInChildren<TextMeshProUGUI>(false);
                        foreach (var tmp in tmpTexts)
                        {
                            if (tmp == null) continue;
                            string text = tmp.text ?? "";
                            if (text.Contains("Hồi sinh") || text.Contains("Revive") || 
                                text.Contains("hồi sinh") || text.Contains("Miễn phí"))
                            {
                                btn.onClick.Invoke();
                                Plugin.Log.LogInfo($"[Bot] ✨ Revive: Clicked '{btn.gameObject.name}' (text='{text}')");
                                _reviveCooldown = 10f;
                                _farmState = FarmState.IDLE;
                                _farmStuckTimer = 0f;
                                return;
                            }
                        }
                    }

                    Plugin.Log.LogWarning("[Bot] ☠️ Không tìm thấy nút Revive!");
                    _reviveCooldown = 15f; // Chờ lâu hơn
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Bot] Revive check error: {ex.Message}");
            }
        }

        // ======================== BOSS NOTIFICATION SCANNER ========================

        /// <summary>
        /// Scan system chat, notifications, and warning UI for boss spawn messages.
        /// Boss announcements typically appear as system chat: "Boss [TênBoss] đã xuất hiện tại [Map]"
        /// </summary>
        private void ScanBossNotifications()
        {
            try
            {
                // === Strategy 1: Scan SystemChatObject(Clone) ===
                // System chat messages (boss announcements) appear as cloned objects
                var systemChatGo = GameObject.Find("SystemChatObject(Clone)");
                if (systemChatGo != null && systemChatGo.activeSelf)
                {
                    // Get all TMPro text in the system chat
                    var tmpTexts = systemChatGo.GetComponentsInChildren<TextMeshProUGUI>(true);
                    foreach (var tmp in tmpTexts)
                    {
                        if (tmp == null) continue;
                        string text = tmp.text ?? "";
                        if (string.IsNullOrEmpty(text)) continue;

                        // Check for boss keywords
                        if (IsBossNotification(text))
                        {
                            ProcessBossNotification(text, "SystemChat");
                        }
                    }
                }

                // === Strategy 2: Scan ChatPanel / ChatScrollView ===
                // Recent chat messages might contain boss announcements
                var chatPanel = GameObject.Find("ChatPanel");
                if (chatPanel != null && chatPanel.activeSelf)
                {
                    var chatTexts = chatPanel.GetComponentsInChildren<TextMeshProUGUI>(true);
                    foreach (var tmp in chatTexts)
                    {
                        if (tmp == null) continue;
                        string text = tmp.text ?? "";
                        if (string.IsNullOrEmpty(text) || text.Length < 5) continue;

                        if (IsBossNotification(text))
                        {
                            ProcessBossNotification(text, "ChatPanel");
                        }
                    }
                }

                // === Strategy 3: Check WarningObject/WarningLogo ===
                // Boss Warning headers that appear on screen
                var warningGo = GameObject.Find("WarningObject");
                if (warningGo != null && warningGo.activeSelf)
                {
                    var warningTexts = warningGo.GetComponentsInChildren<TextMeshProUGUI>(true);
                    foreach (var tmp in warningTexts)
                    {
                        if (tmp == null) continue;
                        string text = tmp.text ?? "";
                        if (!string.IsNullOrEmpty(text) && text.Length > 2)
                        {
                            // WarningLogo text might indicate boss
                            if (IsBossNotification(text) || text.Contains("Cảnh báo") || text.Contains("Warning"))
                            {
                                ProcessBossNotification(text, "WarningLogo");
                            }
                        }
                    }
                }

                // === Strategy 4: Check all active TextMeshPro for system broadcast ===
                // Some games show boss notifications as floating 3D text
                var allTMP3D = GameObject.FindObjectsOfType<TMPro.TextMeshPro>();
                foreach (var tmp in allTMP3D)
                {
                    if (tmp == null || !tmp.gameObject.activeSelf) continue;
                    string goName = tmp.gameObject.name ?? "";
                    // Only check notification-type objects, not map labels
                    if (goName.Contains("Notify") || goName.Contains("Announce") || goName.Contains("Broadcast") || goName.Contains("System"))
                    {
                        string text = tmp.text ?? "";
                        if (IsBossNotification(text))
                        {
                            ProcessBossNotification(text, $"TMP3D({goName})");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Bot] Boss scan error: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if text contains boss-related keywords
        /// </summary>
        private bool IsBossNotification(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length < 3) return false;
            string lower = text.ToLower();
            return lower.Contains("boss") || lower.Contains("xuất hiện") || 
                   lower.Contains("world boss") || lower.Contains("field boss") ||
                   lower.Contains("thủ lĩnh") || lower.Contains("quái vương") ||
                   lower.Contains("trùm") || lower.Contains("đại boss") ||
                   lower.Contains("boss đã") || lower.Contains("xuất hiện tại");
        }

        // ======================== ZONE DETECTION ========================
        
        /// <summary>
        /// Check if we are in a PvE or PvP zone based on UI text
        /// </summary>
        private void UpdateZoneStatus()
        {
            try 
            {
                 bool isCombat = false;
                 
                 string mapName = GetCurrentMapName().ToLower();
                 string zoneName = GetCurrentZoneName().ToLower();

                 if (zoneName.Contains("pve") || zoneName.Contains("pvp") || zoneName.Contains("chiến") || zoneName.Contains("dã ngoại")) 
                     isCombat = true;
                 
                 if (!isCombat && (mapName.Contains("oloong") || mapName.Contains("phó bản") || mapName.Contains("boss") || mapName.Contains("đảo")))
                     isCombat = true;

                 // Deep scan fallback removed for efficiency since we have direct data
                 _inCombatZone = isCombat;
             }
             catch { }
        }

        private string GetTextFromObject(GameObject go)
        {
            if (go == null) return "";
            var tmp = go.GetComponent<TextMeshProUGUI>();
            if (tmp != null) return tmp.text;
            var t = go.GetComponent<Text>();
            if (t != null) return t.text;
            
            // Search children
            tmp = go.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null) return tmp.text;
            return "";
        }

        /// <summary>
        /// Process and log a boss notification, extract map info if possible
        /// </summary>
        private void ProcessBossNotification(string text, string source)
        {
            // Avoid duplicate notifications
            if (text == _lastBossNotification && Time.time - _lastBossTime < 60f)
                return;

            _lastBossNotification = text;
            _lastBossTime = Time.time;

            // Try to extract map name from notification text
            string extractedMap = "";
            // Pattern: "... tại [MapName]" or "... at [MapName]"
            int taiIdx = text.IndexOf("tại ");
            if (taiIdx >= 0)
            {
                extractedMap = text.Substring(taiIdx + 4).Trim();
                // Clean up: remove trailing punctuation, HTML tags
                extractedMap = BotHelper.CleanTextTags(extractedMap);
            }
            else
            {
                int atIdx = text.IndexOf(" at ");
                if (atIdx >= 0)
                {
                    extractedMap = text.Substring(atIdx + 4).Trim();
                    extractedMap = BotHelper.CleanTextTags(extractedMap);
                }
            }

            if (!string.IsNullOrEmpty(extractedMap))
                _lastBossMap = extractedMap;

            // === LOG BOSS NOTIFICATION ===
            string cleanText = BotHelper.CleanTextTags(text);
            Plugin.Log.LogWarning($"[Bot] 🐉 BOSS: {cleanText}");
            if (!string.IsNullOrEmpty(extractedMap))
                Plugin.Log.LogWarning($"[Bot] 🗺️ BOSS MAP: {extractedMap}");
            Plugin.Log.LogWarning($"[Bot] 📡 Source: {source}");

            // Also write to status file for Launcher to read
            try
            {
                string bossFile = Path.Combine(Path.GetDirectoryName(_statusFilePath) ?? "", "boss_notification.txt");
                if (!string.IsNullOrEmpty(bossFile))
                {
                    string content = $"[{DateTime.Now:HH:mm:ss}] {cleanText}\n";
                    if (!string.IsNullOrEmpty(extractedMap))
                        content += $"Map: {extractedMap}\n";
                    content += $"Source: {source}\n---\n";
                    File.AppendAllText(bossFile, content);
                }
            }
            catch { }
        }

        /// <summary>
        /// Clean HTML/rich text tags from string
        /// </summary>


        // ======================== COMMAND CONSOLE (Launcher ↔ Bot) ========================

        /// <summary>
        /// Check for command file from Launcher, process it, and write response
        /// Supports: status, ping, quest, map, boss, toggle auto, toggle attack
        /// </summary>
        private void CheckAndProcessCommand()
        {
            if (string.IsNullOrEmpty(_commandFilePath)) return;
            try
            {
                if (!File.Exists(_commandFilePath)) return;

                // Read and delete command file
                string command = File.ReadAllText(_commandFilePath).Trim().ToLower();
                File.Delete(_commandFilePath);

                if (string.IsNullOrEmpty(command)) return;

                Plugin.Log.LogInfo($"[Bot] 📩 Command received: {command}");

                string response = "";
                string time = DateTime.Now.ToString("HH:mm:ss");

                switch (command)
                {
                    case "ping":
                        response = $"[{time}] ✅ PONG! Bot đang hoạt động.\n" +
                                   $"Username: {_loginUsername}\n" +
                                   $"Scene: {_currentScene}\n" +
                                   $"Uptime: {Time.time:F0}s";
                        break;

                    case "status":
                        string mapN = "", zoneN = "";
                        try { mapN = GetCurrentMapName(); } catch { }
                        try { zoneN = GetCurrentZoneName(); } catch { }
                        Vector3 ppos = Vector3.zero;
                        try { ppos = GetPlayerPosition(); } catch { }
                        string qt = GetCurrentQuestText() ?? "—";

                        response = $"[{time}] 📊 FULL STATUS REPORT\n" +
                                   $"━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                                   $"👤 Username: {_loginUsername}\n" +
                                   $"🎮 Scene: {_currentScene}\n" +
                                   $"🗺️ Map: {(string.IsNullOrEmpty(mapN) ? "—" : mapN)}\n" +
                                   $"📍 Zone: {(string.IsNullOrEmpty(zoneN) ? "—" : zoneN)}\n" +
                                   $"📐 Position: ({ppos.x:F0}, {ppos.y:F0}, {ppos.z:F0})\n" +
                                   $"━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                                   $"📋 Quest: {qt}\n" +
                                   $"⚔️ Auto Quest: {(_autoQuestEnabled ? "✅ ON" : "❌ OFF")}\n" +
                                   $"🗡️ Auto Attack: {(_autoAttackEnabled ? "✅ ON" : "❌ OFF")}\n" +
                                   $"👁️ Headless: {(_headlessMode ? "✅ ON" : "❌ OFF")}\n" +
                                   $"📦 Collect Quest: {(_isCollectQuest ? "YES" : "NO")}\n" +
                                   $"━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                                   $"⏱️ Idle: {_idleTime:F1}s | Pathfind CD: {_pathfindCooldown:F1}s\n" +
                                   $"🕐 Uptime: {Time.time:F0}s | FPS: {(1f / Time.deltaTime):F0}";
                        break;

                    case "quest":
                        string questInfo = GetCurrentQuestText() ?? "Không có quest";
                        response = $"[{time}] 📋 QUEST INFO\n{questInfo}";
                        if (_isCollectQuest) response += "\n📦 Loại: Collect Quest";
                        break;

                    case "map":
                        string mapInfo = "", zoneInfo = "";
                        try { mapInfo = GetCurrentMapName(); } catch { }
                        try { zoneInfo = GetCurrentZoneName(); } catch { }
                        Vector3 pos2 = Vector3.zero;
                        try { pos2 = GetPlayerPosition(); } catch { }
                        response = $"[{time}] 🗺️ MAP INFO\n" +
                                   $"Map: {(string.IsNullOrEmpty(mapInfo) ? "—" : mapInfo)}\n" +
                                   $"Zone: {(string.IsNullOrEmpty(zoneInfo) ? "—" : zoneInfo)}\n" +
                                   $"Pos: ({pos2.x:F0}, {pos2.y:F0}, {pos2.z:F0})";
                        break;

                    case string s when s.StartsWith("teleport"):
                         // Format: teleport MapName ZoneName
                         // Note: Arguments might contain spaces, so likely split by known separators or fixed index?
                         // Launcher sends: teleport {Map} {Zone}
                         // Since Map can have spaces (e.g. "Ngôi Làng Oloong"), we need robust parsing.
                         // But for now, let's assume space separation and Map is 1 word? No, "Ngôi Làng Oloong" is 3 words.
                         // Launcher should probably quote it or use a delimiter?
                         // Let's assume the LAST argument is Zone (usually 1 word like "Zone 1") and the rest is Map.
                         
                         string[] parts = s.Substring(9).Trim().Split(' ');
                         if (parts.Length >= 2)
                         {
                             // Simple heuristic: Last part is Zone, rest is Map
                             // Example: "Ngôi Làng Oloong Zone 1" -> Map="Ngôi Làng Oloong", Zone="Zone 1" (Wait, Zone 1 is 2 words)
                             // Better: Launcher sends "teleport <Map>|<Zone>" using a pipe?
                             // Or we just try to match.
                             // Let's rely on arguments being passed clearly.
                             // If we split by Space, it's ambiguous.
                             // Let's assume the user/launcher uses a pipe '|' separator if they implemented it that way.
                             // But my plan said "teleport <map> <zone>".
                             // Let's update Launcher to send "teleport <Map>|<Zone>" to be safe.
                             // Here, I will support both space (if simple) and pipe.
                             
                             string args = s.Substring(8).Trim();
                             string tMap = "", tZone = "";
                             
                             if (args.Contains("|"))
                             {
                                 string[] p = args.Split('|');
                                 tMap = p[0].Trim();
                                 if (p.Length > 1) tZone = p[1].Trim();
                             }
                             else
                             {
                                 // Fallback: If no pipe, assume arguments are spaced.
                                 tMap = args;
                             }
                             
                             DoTeleport(tMap, tZone);
                             response = $"[{time}] 🚀 Teleporting to [{tMap}] - [{tZone}]...";
                         }
                         else
                         {
                             response = $"[{time}] ⚠️ Invalid teleport syntax. Use: teleport MapName|ZoneName";
                         }
                         break;

                    case "login":
                        Plugin.Log.LogInfo("[Bot] 📩 Login command received from Launcher!");
                        _autoLoginDone = false;
                        _charSelectDone = false;
                        HandleAutoLogin();
                        response = $"[{time}] 🔑 Login triggered for '{_loginUsername}' on scene '{_currentScene}'";
                        break;

                    case "boss":
                        if (string.IsNullOrEmpty(_lastBossNotification))
                            response = $"[{time}] 🐉 Chưa phát hiện boss nào.";
                        else
                            response = $"[{time}] 🐉 BOSS INFO\n" +
                                       $"Notification: {_lastBossNotification}\n" +
                                       $"Map: {(string.IsNullOrEmpty(_lastBossMap) ? "—" : _lastBossMap)}";
                        break;

                    case "toggle auto":
                        _autoQuestEnabled = !_autoQuestEnabled;
                        response = $"[{time}] ⚡ Auto Quest → {(_autoQuestEnabled ? "✅ BẬT" : "❌ TẮT")}";
                        LogActivity($"⚡ Auto Quest toggled: {(_autoQuestEnabled ? "ON" : "OFF")}");
                        break;

                    case "toggle attack":
                        _autoAttackEnabled = !_autoAttackEnabled;
                        response = $"[{time}] ⚡ Auto Attack → {(_autoAttackEnabled ? "✅ BẬT" : "❌ TẮT")}";
                        LogActivity($"⚡ Auto Attack toggled: {(_autoAttackEnabled ? "ON" : "OFF")}");
                        break;

                    case "log":
                        if (_activityLog.Count == 0)
                            response = $"[{time}] 📜 Chưa có hoạt động nào được ghi.";
                        else
                        {
                            response = $"[{time}] 📜 ACTIVITY LOG ({_activityLog.Count} entries)\n";
                            response += "━━━━━━━━━━━━━━━━━━━━━━━━━━\n";
                            for (int i = 0; i < _activityLog.Count; i++)
                                response += _activityLog[i] + "\n";
                        }
                        break;

                    case string s when s.StartsWith("speed"):
                        string[] sparts = s.Split(' ');
                        if (sparts.Length > 1 && float.TryParse(sparts[1], out float ns))
                            _gameSpeed = ns;
                        else
                            _gameSpeed = (_gameSpeed >= 3f) ? 1f : _gameSpeed + 1f;

                        Time.timeScale = _gameSpeed;
                        response = $"[{time}] ⚡ Game Speed → x{_gameSpeed}";
                        LogActivity($"⚡ Game Speed: x{_gameSpeed} {(sparts.Length > 1 ? "(set)" : "(toggle)")}");
                        break;

                    default:
                        response = $"[{time}] ❓ Lệnh không hợp lệ: '{command}'\n" +
                                   $"Các lệnh hỗ trợ: ping, status, quest, map, boss, log, speed, toggle auto, toggle attack";
                        break;
                }

                // Write response file
                if (!string.IsNullOrEmpty(response) && !string.IsNullOrEmpty(_responseFilePath))
                {
                    File.WriteAllText(_responseFilePath, response);
                    Plugin.Log.LogInfo($"[Bot] 📤 Response written: {response.Split('\n')[0]}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Bot] Command error: {ex.Message}");
            }
        }

        // ======================== TELEPORT STATE MACHINE (UI AUTOMATION) ========================
        private int _teleportState = 0; // 0=Idle, 1=OpenZone, 2=SelectZone, 3=OpenMap, 4=SelectMap
        private string _targetMapName = "";
        private string _targetZoneName = "";
        private float _teleportTimer = 0f;
        private float _teleportTimeout = 0f;

        private void UpdateTeleport()
        {
            if (_teleportState == 0) return;

            _teleportTimer += Time.deltaTime;
            _teleportTimeout += Time.deltaTime;

            if (_teleportTimeout > 20f) // 20s timeout
            {
                Plugin.Log.LogWarning("[Bot] ⚠️ Teleport timed out! Resetting.");
                _teleportState = 0;
                return;
            }

            switch (_teleportState)
            {
                case 1: // OPEN ZONE PANEL
                    if (_teleportTimer > 1.0f)
                    {
                        var zoneBtn = GameObject.Find("HUDCanvas/SafetyUI/MiniMap/ZoneObject");
                        if (zoneBtn != null)
                        {
                            ClickButton(zoneBtn);
                            Plugin.Log.LogInfo("[Bot] 🖱️ Clicked ZoneObject to open panel.");
                            _teleportState = 2; // Move to select
                            _teleportTimer = 0f;
                        }
                        else
                        {
                            Plugin.Log.LogWarning("[Bot] ❌ ZoneObject not found!");
                            _teleportState = 0;
                        }
                    }
                    break;

                case 2: // SELECT ZONE
                    if (_teleportTimer > 1.5f) // Wait for panel to open
                    {
                        if (ClickTextButton(_targetZoneName, "ZonePanel")) 
                        {
                             Plugin.Log.LogInfo($"[Bot] ✅ Clicked Zone: {_targetZoneName}");
                             _teleportState = 0;
                             
                             if (!string.IsNullOrEmpty(_targetMapName) && _targetMapName != GetCurrentMapName())
                             {
                                 _teleportState = 3; 
                                 _teleportTimer = 0f;
                             }
                        }
                        else if (_teleportTimer > 5f)
                        {
                            Plugin.Log.LogWarning($"[Bot] ⚠️ Could not find Zone [{_targetZoneName}] in UI.");
                            _teleportState = 0;
                        }
                    }
                    break;

                case 3: // OPEN MAP PANEL
                    if (_teleportTimer > 1.0f)
                    {
                        var mapBtn = GameObject.Find("HUDCanvas/SafetyUI/MiniMap/MapName");
                        if (mapBtn != null)
                        {
                            ClickButton(mapBtn);
                            Plugin.Log.LogInfo("[Bot] 🖱️ Clicked MapName to open World Map.");
                            _teleportState = 4;
                            _teleportTimer = 0f;
                        }
                        else
                        {
                            Plugin.Log.LogWarning("[Bot] ❌ MapName button not found!");
                            _teleportState = 0;
                        }
                    }
                    break;

                case 4: // SELECT MAP
                    if (_teleportTimer > 2.0f)
                    {
                        if (ClickTextButton(_targetMapName, "MapPanel"))
                        {
                            Plugin.Log.LogInfo($"[Bot] ✅ Clicked Map: {_targetMapName}");
                             _teleportState = 5; // Check for confirm
                             _teleportTimer = 0f;
                        }
                        else if (_teleportTimer > 5f)
                        {
                            Plugin.Log.LogWarning($"[Bot] ⚠️ Could not find Map [{_targetMapName}] in UI.");
                            _teleportState = 0;
                        }
                    }
                    break;
                    
                case 5: // CONFIRM MAP (If needed)
                    if (_teleportTimer > 1.0f)
                    {
                        _teleportState = 0;
                    }
                    break;
            }
        }

        private bool ClickTextButton(string textToFind, string debugContext)
        {
            if (string.IsNullOrEmpty(textToFind)) return false;
            var allTxt = GameObject.FindObjectsOfType<TextMeshProUGUI>();
            foreach (var txt in allTxt)
            {
                if (txt == null) continue;
                if (txt.gameObject.activeInHierarchy && txt.text.Contains(textToFind))
                {
                    var btn = txt.GetComponentInParent<Button>();
                    if (btn != null)
                    {
                        btn.onClick.Invoke();
                        return true;
                    }
                    txt.SendMessageUpwards("OnPointerClick", new PointerEventData(EventSystem.current), SendMessageOptions.DontRequireReceiver);
                    return true;
                }
            }
            return false;
        }

        private void ClickButton(GameObject go)
        {
            if (go == null) return;
            var btn = go.GetComponent<Button>();
            if (btn != null) btn.onClick.Invoke();
            else go.SendMessage("OnPointerClick", new PointerEventData(EventSystem.current), SendMessageOptions.DontRequireReceiver);
        }

        private void DoTeleport(string map, string zone)
        {
            Plugin.Log.LogInfo($"[Bot] 🚀 Init Teleport UI: Map='{map}', Zone='{zone}'");
            _targetMapName = map;
            _targetZoneName = zone;
            _teleportTimeout = 0f;
            _teleportTimer = 0f;

            string currentMap = GetCurrentMapName();
            Plugin.Log.LogInfo($"[Bot] Teleport Check: Current='{currentMap}' vs Target='{map}'");

            if (!string.IsNullOrEmpty(map) && map != currentMap)
            {
                Plugin.Log.LogInfo("[Bot] 🗺️ Different Map -> Trigger Map Teleport Mode");
                _teleportState = 3;
            }
            else if (!string.IsNullOrEmpty(zone) && zone != GetCurrentZoneName())
            {
                Plugin.Log.LogInfo("[Bot] 📍 Same Map, Different Zone -> Trigger Zone Change Mode");
                _teleportState = 1; 
            }
            else
            {
                Plugin.Log.LogInfo("[Bot] ✅ Already at target location (Map & Zone match).");
            }
        }

        // ======================== FIND MANAGERS ========================

        private void FindManagers()
        {
            Plugin.Log.LogInfo("[Bot] Đang tìm game managers + UI buttons...");
            try
            {
                // === Managers ===
                _autoMissionManager = BotHelper.FindSingletonByType("AutoMissionManager");
                if (_autoMissionManager != null)
                    Plugin.Log.LogInfo("[Bot] ✅ Tìm thấy AutoMissionManager!");
                else
                    Plugin.Log.LogWarning("[Bot] ❌ Không tìm thấy AutoMissionManager");

                _playerDataManager = BotHelper.FindSingletonByType("PlayerDataManager");
                if (_playerDataManager != null)
                    Plugin.Log.LogInfo("[Bot] ✅ Tìm thấy PlayerDataManager!");

                _gameManager = BotHelper.FindSingletonByType("GameManager");
                if (_gameManager != null)
                    Plugin.Log.LogInfo("[Bot] ✅ Tìm thấy GameManager!");

                _appManager = BotHelper.FindSingletonByType("AppManager");
                if (_appManager != null)
                    Plugin.Log.LogInfo("[Bot] ✅ Tìm thấy AppManager!");

                _guideManager = BotHelper.FindSingletonByType("GuideManager");
                _autoAttackBlackBoard = BotHelper.FindSingletonByType("AutoAttackBlackBoardComponent");

                // === UI Buttons (Muted if not found) ===
                var hudCanvas = GameObject.Find("HUDCanvas");
                if (hudCanvas != null)
                {
                    // AutoAttackButton in SkillLayout - DEEP SEARCH
                    // First try direct path
                    Transform skillLayout = hudCanvas.transform.Find("SkillLayout");
                    if (skillLayout == null) skillLayout = hudCanvas.transform; // Fallback to canvas root search if layout moved

                    var autoAtkGO = BotHelper.FindInactiveChild(skillLayout, "AutoAttackButton");
                    if (autoAtkGO != null)
                    {
                        _autoAttackBtn = autoAtkGO.GetComponent<Button>();
                        // If no Button component, look for the Custom Component "AutoAttackButton" and see if we can get a Button from it or children
                        if (_autoAttackBtn == null)
                            _autoAttackBtn = autoAtkGO.GetComponentInChildren<Button>(true);
                            
                        Plugin.Log.LogInfo($"[Bot] ✅ Found AutoAttackButton at {BotHelper.GetPath(autoAtkGO.transform)} (Active: {autoAtkGO.activeInHierarchy})");
                    }
                    else
                    {
                         Plugin.Log.LogWarning("[Bot] ❌ AutoAttackButton NOT found in SkillLayout!");
                    }

                    // InteractButton in SkillLayout
                    var interactGO = BotHelper.FindInactiveChild(skillLayout, "InteractButton");
                    if (interactGO != null)
                    {
                        _interactBtn = interactGO.GetComponent<Button>();
                        if (_interactBtn == null)
                            _interactBtn = interactGO.GetComponentInChildren<Button>(true);
                    }
                }

                // === Quest Panel ===
                _shortMissionPanel = BotHelper.FindSingletonByType("ShortMissionPanel");
                if (_shortMissionPanel != null)
                    Plugin.Log.LogInfo("[Bot] ✅ Tìm thấy ShortMissionPanel!");

                // === MainGame (trên SceneManager GO) ===
                _mainGame = BotHelper.FindSingletonByType("MainGame");
                if (_mainGame != null)
                    Plugin.Log.LogInfo("[Bot] ✅ Tìm thấy MainGame!");
                else
                    Plugin.Log.LogWarning("[Bot] ❌ Không tìm thấy MainGame");

                // === ShortMissionPanel Button ===
                var missionGO = GameObject.Find("ShortMissionPanel");
                if (missionGO != null)
                {
                    _shortMissionBtn = missionGO.GetComponent<Button>();
                    if (_shortMissionBtn == null)
                        _shortMissionBtn = missionGO.GetComponentInChildren<Button>();
                    Plugin.Log.LogInfo($"[Bot] ✅ ShortMissionPanel Button: {(_shortMissionBtn != null ? "found" : "no Button comp")}");
                }

                // === Phase 3 Managers ===
                _guideManager = BotHelper.FindSingletonByType("GuideManager");
                if (_guideManager != null)
                    Plugin.Log.LogInfo("[Bot] ✅ Tìm thấy GuideManager!");

                _autoAttackBlackBoard = BotHelper.FindSingletonByType("AutoAttackBlackBoardComponent");
                if (_autoAttackBlackBoard != null)
                    Plugin.Log.LogInfo("[Bot] ✅ Tìm thấy AutoAttackBlackBoardComponent!");

                _managersFound = (_autoMissionManager != null);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Bot] Lỗi tìm managers: {ex.Message}");
            }
        }

        // ======================== SCAN SINGLETONS ========================

        private void ScanSingletons()
        {
            Plugin.Log.LogInfo("[Bot] === SCAN SINGLETONS ===");
            var allMB = GameObject.FindObjectsOfType<MonoBehaviour>();
            var typeCounts = new Dictionary<string, int>();

            foreach (var mb in allMB)
            {
                if (mb == null) continue;
                var typeName = mb.GetIl2CppType().FullName;
                if (!typeCounts.ContainsKey(typeName))
                    typeCounts[typeName] = 0;
                typeCounts[typeName]++;
            }

            int idx = 0;
            foreach (var kvp in typeCounts)
            {
                if (kvp.Value == 1)
                {
                    foreach (var mb in allMB)
                    {
                        if (mb == null) continue;
                        if (mb.GetIl2CppType().FullName == kvp.Key)
                        {
                            var hasGI = BotHelper.HasMethod(mb, "gI");
                            Plugin.Log.LogInfo($"  [{++idx}] {kvp.Key} on:{mb.gameObject.name} gI={hasGI}");
                            break;
                        }
                    }
                }
            }
            Plugin.Log.LogInfo($"[Bot] Tổng {idx} singletons");
        }

        // ======================== LOG PLAYER INFO ========================

        private void LogPlayerInfo()
        {
            Plugin.Log.LogInfo("[Bot] === PLAYER INFO ===");
            try
            {
                if (_gameManager == null) FindManagers();
                if (_gameManager == null)
                {
                    Plugin.Log.LogWarning("[Bot] GameManager chưa tìm thấy!");
                    return;
                }

                var mainPlayerGO = GameObject.Find("MainPlayer");
                if (mainPlayerGO != null)
                {
                    var pos = mainPlayerGO.transform.position;
                    Plugin.Log.LogInfo($"[Bot] MainPlayer at ({pos.x:F1}, {pos.y:F1}, {pos.z:F1})");
                    var comps = mainPlayerGO.GetComponents<Component>();
                    foreach (var c in comps)
                    {
                        if (c == null) continue;
                        Plugin.Log.LogInfo($"  → {c.GetIl2CppType().FullName}");
                    }
                }
                else
                {
                    var allMB = GameObject.FindObjectsOfType<MonoBehaviour>();
                    foreach (var mb in allMB)
                    {
                        if (mb == null) continue;
                        var typeName = mb.GetIl2CppType().Name;
                        if (typeName.Contains("Player"))
                        {
                            var pos = mb.gameObject.transform.position;
                            Plugin.Log.LogInfo($"  [{typeName}] at ({pos.x:F1}, {pos.y:F1}) on:{mb.gameObject.name}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Bot] Lỗi: {ex.Message}");
            }
        }




        // ======================== STATE LOG BACKGROUND WRITER ========================
        private void StateLogWriteLoop()
        {
            try
            {
                using (var sw = new StreamWriter(_stateLogPath, true))
                {
                    sw.AutoFlush = true;
                    while (_stateLogRunning || !_stateLogQueue.IsEmpty)
                    {
                        if (_stateLogQueue.TryDequeue(out string log))
                        {
                            sw.WriteLine(log);
                        }
                        else
                        {
                            Thread.Sleep(50);
                        }
                    }
                }
            }
            catch { }
        }

        // ======================== QUEST RESUME SYSTEM (Text-Based) ========================
        private void ResumeQuest()
        {
            try
            {
                string questText = GetCurrentQuestText();
                if (string.IsNullOrEmpty(questText))
                {
                    LogActivity("⚠️ Không tìm thấy quest nào để resume!");
                    return;
                }

                LogActivity($"🔄 Đang phân tích quest: '{questText}'...");
                var step = QuestDatabase.GetActionForQuest(questText);

                if (step == null || step.Action == ActionType.Unknown)
                {
                    LogActivity($"❌ Không hiểu đề bài: '{questText}'. Vui lòng làm thủ công!");
                    return;
                }

                LogActivity($"✅ Đã hiểu! Hành động: {step.Description}");

                // Execute based on Action
                switch (step.Action)
                {
                    case ActionType.KillMob:
                        LogActivity($"⚔️ Chuyển sang chế độ ĐÁNH QUÁI: {step.TargetName}");
                        _lastActionTarget = step.TargetName;
                        _autoAttackFlag = true;
                        _farmState = FarmState.SEARCH_TARGET;
                        break;

                    case ActionType.TalkNPC:
                        LogActivity($"🗣️ Chuyển sang chế độ TÌM NPC: {step.TargetName}");
                        _lastActionTarget = step.TargetName;
                        _autoInteractFlag = true;
                        
                        // Load preferred dialog sequence
                        if (step.DialogAnswers != null && step.DialogAnswers.Count > 0)
                        {
                            _currentQuestDialogSequence = new List<string>(step.DialogAnswers);
                            LogActivity($"📜 Đã tải {step.DialogAnswers.Count} câu trả lời mẫu từ DB.");
                        }
                        else
                        {
                            _currentQuestDialogSequence.Clear();
                        }
                        break;

                    case ActionType.MoveToMap:
                        _currentQuestActionStep = step;
                        LogActivity($"🗺️ Auto-Move ACTIVATED: Go to {step.TargetMap} @ {step.TargetPos}");
                        break;
                }
            }
            catch (Exception ex)
            {
                LogActivity($"❌ Lỗi ResumeQuest: {ex.Message}");
            }
        }

        private void CheckStartupTeleport()
        {
            if (_startupTeleportDone || string.IsNullOrEmpty(_startupMap) || !_managersFound) return;
            if (_currentScene != "MainGameScene") return;

            // Wait until character is truly loaded (using _playerDataManager as proxy)
            if (_playerDataManager == null) return;

            try
            {
                Plugin.Log.LogInfo($"[Bot] 🚀 Startup Teleport Request: {_startupMap} (Zone: {_startupZone})");
                DoTeleport(_startupMap, _startupZone.ToString());
                _startupTeleportDone = true; // Mark as done to avoid loop
                Plugin.Log.LogInfo("[Bot] ✅ Startup Teleport triggered.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Bot] Startup Teleport error: {ex.Message}");
            }
        }

        void OnDestroy()
        {
            _stateLogRunning = false;
            if (_stateWriteThread != null && _stateWriteThread.IsAlive)
                _stateWriteThread.Join(500);
        }
        private void CloseAllPanels()
        {
             try 
             {
                 // Find all buttons with "Close" in name
                 var buttons = GameObject.FindObjectsOfType<Button>();
                 foreach (var btn in buttons)
                 {
                     if (btn == null || !btn.gameObject.activeInHierarchy) continue;
                     string name = btn.gameObject.name.ToLower();
                     if (name.Contains("close") || name.Contains("exit") || name.Contains("thoat"))
                     {
                         Plugin.Log.LogInfo($"[Bot] 🚑 Anti-Stuck: Clicking close button '{btn.gameObject.name}'");
                         btn.onClick.Invoke();
                     }
                 }
             }
             catch {}
        }

        private void ClickOutsideUI()
        {
            try
            {
                var backdrop = GameObject.Find("Backdrop");
                if (backdrop != null && backdrop.activeSelf)
                {
                    var btn = backdrop.GetComponent<Button>();
                    if (btn != null)
                    {
                        btn.onClick.Invoke();
                        Plugin.Log.LogInfo("[Bot] 🌑 ClickOutsideUI: Clicked Backdrop");
                    }
                }
            }
            catch { }
        }

        // ======================== AUTO LOGIN HELPERS ========================

        public void HandleAutoLogin()
        {
            if (string.IsNullOrEmpty(_loginUsername) || string.IsNullOrEmpty(_loginPassword))
            {
                Plugin.Log.LogWarning("[AutoLogin] ❌ Username/Password empty in config!");
                return;
            }

            try
            {
                Plugin.Log.LogInfo($"[AutoLogin] 🔑 UI Login: Setting credentials for '{_loginUsername}'...");

                // --- Strategy 1: Find by EXACT name (from scene scan) ---
                // LoginPanel > LoginForm > UserName (TMP_InputField)
                // LoginPanel > LoginForm > Password (TMP_InputField)
                // LoginPanel > LoginForm > Login (Button)
                
                bool userSet = false;
                bool passSet = false;

                // Try exact names first
                var userNameGo = GameObject.Find("UserName");
                var passwordGo = GameObject.Find("Password");

                if (userNameGo != null)
                {
                    var tmpInput = userNameGo.GetComponent<TMP_InputField>();
                    if (tmpInput != null)
                    {
                        tmpInput.text = _loginUsername;
                        userSet = true;
                        Plugin.Log.LogInfo($"[AutoLogin] ✅ Set Username on 'UserName' (exact match)");
                    }
                }
                
                if (passwordGo != null)
                {
                    var tmpInput = passwordGo.GetComponent<TMP_InputField>();
                    if (tmpInput != null)
                    {
                        tmpInput.text = _loginPassword;
                        passSet = true;
                        Plugin.Log.LogInfo($"[AutoLogin] ✅ Set Password on 'Password' (exact match)");
                    }
                }

                // --- Strategy 2: Fallback fuzzy search ---
                if (!userSet || !passSet)
                {
                    Plugin.Log.LogInfo("[AutoLogin] ⚠️ Exact names not found, trying fuzzy search...");
                    var allInputs = Resources.FindObjectsOfTypeAll<TMP_InputField>();
                    foreach (var input in allInputs)
                    {
                        if (input == null || !input.gameObject.activeInHierarchy) continue;
                        string goName = input.gameObject.name;
                        
                        if (!userSet && (goName == "UserName" || goName == "Account" || goName.ToLower().Contains("user") || goName.ToLower().Contains("account")))
                        {
                            input.text = _loginUsername;
                            userSet = true;
                            Plugin.Log.LogInfo($"[AutoLogin] ✅ Set Username on '{goName}' (fuzzy)");
                        }
                        else if (!passSet && (goName == "Password" || goName.ToLower().Contains("pass")))
                        {
                            input.text = _loginPassword;
                            passSet = true;
                            Plugin.Log.LogInfo($"[AutoLogin] ✅ Set Password on '{goName}' (fuzzy)");
                        }
                    }
                }

                if (userSet && passSet)
                {
                    Plugin.Log.LogInfo("[AutoLogin] ✅ Credentials set! Clicking Login button...");
                    
                    // Find Login button (exact: "Login" on LoginPanel)
                    var loginGo = GameObject.Find("Login");
                    if (loginGo != null && loginGo.activeInHierarchy)
                    {
                        var btn = loginGo.GetComponent<Button>();
                        if (btn != null)
                        {
                            Plugin.Log.LogInfo("[AutoLogin] 🚀 Clicking 'Login' button!");
                            btn.onClick.Invoke();
                            return;
                        }
                    }

                    // Fallback: search buttons
                    var allBtns = Resources.FindObjectsOfTypeAll<Button>();
                    foreach (var btn in allBtns)
                    {
                        if (btn == null || !btn.gameObject.activeInHierarchy) continue;
                        string bName = btn.gameObject.name;
                        if (bName == "Login" || bName == "LoginButton" || bName == "btnLogin" ||
                            bName.ToLower().Contains("login") || bName.ToLower().Contains("dangnhap"))
                        {
                            // Avoid register panel's LoginButton
                            var parent = btn.transform.parent;
                            bool isRegisterPanel = false;
                            while (parent != null)
                            {
                                if (parent.name == "Register") { isRegisterPanel = true; break; }
                                parent = parent.parent;
                            }
                            if (isRegisterPanel) continue;

                            Plugin.Log.LogInfo($"[AutoLogin] 🚀 Clicking Login Button: '{bName}'");
                            btn.onClick.Invoke();
                            break;
                        }
                    }
                }
                else
                {
                    // Log all active TMP_InputFields for debugging
                    Plugin.Log.LogWarning($"[AutoLogin] ⚠️ Could not find Input fields! UserSet:{userSet} PassSet:{passSet}");
                    try
                    {
                        var debugInputs = Resources.FindObjectsOfTypeAll<TMP_InputField>();
                        foreach (var input in debugInputs)
                        {
                            if (input == null) continue;
                            Plugin.Log.LogInfo($"[AutoLogin] 🔍 TMP_InputField: '{input.gameObject.name}' active={input.gameObject.activeInHierarchy}");
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[AutoLogin] ❌ Login error: {ex.Message}");
            }
        }

        public void HandleCharacterSelect()
        {
            try
            {
                // From scene scan: CharacterChoosingPanel (active=False initially, becomes True after login)
                //   └─ ChooseCharacterLayout (HorizontalLayoutGroup)
                //        └─ Character slots (children)
                
                var panel = GameObject.Find("CharacterChoosingPanel");
                if (panel == null || !panel.activeInHierarchy)
                {
                    // Try to find even if inactive (scene scan shows it exists but active=False)
                    var allGOs = Resources.FindObjectsOfTypeAll<GameObject>();
                    foreach (var go in allGOs)
                    {
                        if (go != null && go.name == "CharacterChoosingPanel" && go.activeInHierarchy)
                        {
                            panel = go;
                            break;
                        }
                    }
                }

                if (panel != null && panel.activeInHierarchy)
                {
                    Plugin.Log.LogInfo("[AutoLogin] 👤 Character Selection Screen detected!");

                    // Find ChooseCharacterLayout and click first slot
                    var layout = panel.GetComponentInChildren<UnityEngine.UI.HorizontalLayoutGroup>();
                    if (layout != null)
                    {
                        // Click first child with a Button
                        for (int i = 0; i < layout.transform.childCount; i++)
                        {
                            var child = layout.transform.GetChild(i);
                            if (child == null || !child.gameObject.activeSelf) continue;
                            var btn = child.GetComponent<Button>();
                            if (btn != null)
                            {
                                Plugin.Log.LogInfo($"[AutoLogin] 👆 Clicking character slot: '{child.name}'");
                                btn.onClick.Invoke();
                                break;
                            }
                        }
                    }
                    
                    _charSelectDone = true;
                    Plugin.Log.LogInfo("[AutoLogin] ✅ Character selected!");
                }
                else
                {
                    // If we're already in MainGameScene, we're done
                    if (_currentScene == "MainGameScene")
                    {
                        _charSelectDone = true;
                        _autoLoginDone = true;
                        Plugin.Log.LogInfo("[AutoLogin] ✅ Already in MainGameScene, skip char select.");
                    }
                    else
                    {
                        Plugin.Log.LogInfo("[AutoLogin] ⏳ CharacterChoosingPanel not visible yet...");
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[AutoLogin] ❌ Char Select error: {ex.Message}");
            }
        }

        public void HandleEnterGame()
        {
            try
            {
                Button btnToClick = null;
                
                // === STRATEGY 1: Find buttons on CharacterChoosingPanel ===
                GameObject panel = GameObject.Find("CharacterChoosingPanel");
                if (panel == null)
                {
                    var allGOs = Resources.FindObjectsOfTypeAll<GameObject>();
                    foreach (var go in allGOs)
                    {
                        if (go != null && go.name == "CharacterChoosingPanel" && go.activeInHierarchy)
                        {
                            panel = go;
                            break;
                        }
                    }
                }

                if (panel != null && panel.activeInHierarchy)
                {
                    Plugin.Log.LogInfo("[AutoLogin] 🎮 CharacterChoosingPanel active! Scanning buttons...");
                    var panelButtons = panel.GetComponentsInChildren<Button>(false);
                    Button fallbackBtn = null;
                    
                    foreach (var btn in panelButtons)
                    {
                        if (btn == null || !btn.gameObject.activeInHierarchy) continue;
                        string bName = btn.gameObject.name.ToLower();
                        
                        // Skip "Tạo mới" (Create New) buttons  
                        if (bName.Contains("create") || bName.Contains("tao") || bName.Contains("new")) continue;
                        
                        string buttonText = "";
                        var tmpText = btn.GetComponentInChildren<TextMeshProUGUI>();
                        if (tmpText != null) buttonText = tmpText.text;
                        Plugin.Log.LogInfo($"[AutoLogin]   → Button: '{btn.gameObject.name}' text='{buttonText}'");
                        
                        string textLow = (buttonText ?? "").ToLower();
                        if (textLow.Contains("vào") || textLow.Contains("vao") || textLow.Contains("game") ||
                            textLow.Contains("enter") || textLow.Contains("play") || textLow.Contains("start") ||
                            bName.Contains("enter") || bName.Contains("play") || bName.Contains("vao"))
                        {
                            btnToClick = btn;
                            Plugin.Log.LogInfo($"[AutoLogin] ✅ Matched: '{btn.gameObject.name}' text='{buttonText}'");
                            break;
                        }
                        
                        // Fallback: first non-create button
                        if (fallbackBtn == null && !textLow.Contains("tạo") && !textLow.Contains("mới"))
                            fallbackBtn = btn;
                    }
                    
                    if (btnToClick == null && fallbackBtn != null)
                    {
                        btnToClick = fallbackBtn;
                        Plugin.Log.LogInfo($"[AutoLogin] 🔄 Using fallback button: '{fallbackBtn.gameObject.name}'");
                    }
                }

                // === STRATEGY 2: Common button names ===
                if (btnToClick == null)
                {
                    string[] btnNames = { "PlayButton", "btnPlay", "btnEnter", "ButtonPlay", 
                        "btn_play", "StartGameButton", "StartButton", "EnterGame", "VaoGame" };
                    foreach (var name in btnNames)
                    {
                        var obj = GameObject.Find(name);
                        if (obj != null && obj.activeInHierarchy)
                        {
                            btnToClick = obj.GetComponent<Button>();
                            if (btnToClick != null)
                            {
                                Plugin.Log.LogInfo($"[AutoLogin] 🎮 Found by name: '{name}'");
                                break;
                            }
                        }
                    }
                }

                // === STRATEGY 3: Global button text search ===
                if (btnToClick == null)
                {
                    var allBtns = Resources.FindObjectsOfTypeAll<Button>();
                    foreach (var btn in allBtns)
                    {
                        if (btn == null || !btn.gameObject.activeInHierarchy) continue;
                        string bName = btn.gameObject.name.ToLower();
                        if (bName.Contains("close") || bName.Contains("back") || bName.Contains("exit")) continue;
                        
                        string buttonText = "";
                        var tmpText = btn.GetComponentInChildren<TextMeshProUGUI>();
                        if (tmpText != null) buttonText = tmpText.text.ToLower();
                        
                        if (bName.Contains("play") || bName.Contains("start") || bName.Contains("enter") ||
                            bName.Contains("vaogame") || bName.Contains("vao") ||
                            buttonText.Contains("vào") || buttonText.Contains("vao") ||
                            buttonText.Contains("game") || buttonText.Contains("play") || buttonText.Contains("bắt đầu"))
                        {
                            btnToClick = btn;
                            Plugin.Log.LogInfo($"[AutoLogin] 🎮 Found by global search: '{btn.gameObject.name}' text='{buttonText}'");
                            break;
                        }
                    }
                }

                if (btnToClick != null)
                {
                    Plugin.Log.LogInfo($"[AutoLogin] 🚀 Clicking '{btnToClick.gameObject.name}' to Enter Game!");
                    btnToClick.onClick.Invoke();
                }
                else
                {
                    Plugin.Log.LogWarning("[AutoLogin] ⏳ No enter button found, retrying...");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[AutoLogin] ❌ Enter Game error: {ex.Message}");
            }
        }
    }

    // ======================== BOT HELPER (STATICS) ========================
    // Moving reflection helpers to a separate static class avoids IL2CPP signature exhaustion
    // and unsupported interop type warnings in the registered MonoBehaviour.
    public static class BotHelper
    {
        public static MonoBehaviour FindSingletonByType(string typeName)
        {
            var allMB = GameObject.FindObjectsOfType<MonoBehaviour>(true);
            foreach (var mb in allMB)
            {
                if (mb == null) continue;
                if (mb.GetIl2CppType().Name == typeName)
                    return mb;
            }
            return null;
        }

        public static T GetFieldValue<T>(MonoBehaviour target, string fieldName)
        {
            var fields = target.GetIl2CppType().GetFields(Il2CppSystem.Reflection.BindingFlags.Public | Il2CppSystem.Reflection.BindingFlags.NonPublic | Il2CppSystem.Reflection.BindingFlags.Instance);
            foreach (var f in fields)
            {
                if (f.Name == fieldName)
                {
                    var result = f.GetValue(target);
                    if (result != null)
                    {
                        if (typeof(T) == typeof(string))
                            return (T)(object)result.ToString();
                        return (T)Convert.ChangeType(result.ToString(), typeof(T));
                    }
                    return default(T);
                }
            }
            throw new Exception($"Field '{fieldName}' not found on {target.GetIl2CppType().Name}");
        }

        public static T GetFieldValueFromObject<T>(Il2CppSystem.Object target, string fieldName)
        {
            var fields = target.GetIl2CppType().GetFields(Il2CppSystem.Reflection.BindingFlags.Public | Il2CppSystem.Reflection.BindingFlags.NonPublic | Il2CppSystem.Reflection.BindingFlags.Instance);
            foreach (var f in fields)
            {
                if (f.Name == fieldName)
                {
                    var result = f.GetValue(target);
                    if (result != null)
                    {
                        if (typeof(T) == typeof(string))
                            return (T)(object)result.ToString();
                        return (T)Convert.ChangeType(result.ToString(), typeof(T));
                    }
                    return default(T);
                }
            }
            throw new Exception($"Field '{fieldName}' not found");
        }

        public static void CallMethod(MonoBehaviour target, string methodName, object[] parameters = null)
        {
            var methods = target.GetIl2CppType().GetMethods();
            foreach (var m in methods)
            {
                if (m.Name == methodName)
                {
                    var parms = m.GetParameters();
                    if (parameters == null || parameters.Length == 0)
                    {
                        if (parms == null || parms.Length == 0) {
                            m.Invoke(target, null);
                            return;
                        }
                    }
                    else if (parms != null && parms.Length == parameters.Length)
                    {
                        var il2CppParams = new Il2CppSystem.Object[parameters.Length];
                        for (int i = 0; i < parameters.Length; i++)
                        {
                            if (parameters[i] is bool b)
                                il2CppParams[i] = (Il2CppSystem.Object)Il2CppSystem.Boolean.Parse(b.ToString());
                            else if (parameters[i] is int val)
                                il2CppParams[i] = (Il2CppSystem.Object)Il2CppSystem.Int32.Parse(val.ToString());
                            else if (parameters[i] is string s)
                                il2CppParams[i] = (Il2CppSystem.Object)s;
                            else
                                il2CppParams[i] = (Il2CppSystem.Object)parameters[i];
                        }
                        m.Invoke(target, il2CppParams);
                        return;
                    }
                }
            }
            throw new Exception($"Method '{methodName}' with {parameters?.Length ?? 0} params not found on {target.GetIl2CppType().Name}");
        }

        public static void CallMethodNoParam(MonoBehaviour target, string methodName)
        {
            var methods = target.GetIl2CppType().GetMethods();
            foreach (var m in methods)
            {
                if (m.Name == methodName)
                {
                    var parms = m.GetParameters();
                    if (parms == null || parms.Length == 0)
                    {
                        m.Invoke(target, null);
                        return;
                    }
                }
            }
            throw new Exception($"No parameterless '{methodName}' on {target.GetIl2CppType().Name}");
        }

        public static T CallMethodReturn<T>(MonoBehaviour target, string methodName)
        {
            var methods = target.GetIl2CppType().GetMethods();
            foreach (var m in methods)
            {
                if (m.Name == methodName)
                {
                    var result = m.Invoke(target, null);
                    if (result != null)
                        return (T)Convert.ChangeType(result.ToString(), typeof(T));
                    return default(T);
                }
            }
            throw new Exception($"Method '{methodName}' not found");
        }

        public static bool HasMethod(MonoBehaviour target, string methodName)
        {
            try
            {
                var methods = target.GetIl2CppType().GetMethods();
                foreach (var m in methods)
                    if (m.Name == methodName) return true;
            }
            catch { }
            return false;
        }

        public static GameObject FindInactiveChild(Transform parent, string name)
        {
            if (parent == null) return null;
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child == null) continue;
                if (child.gameObject.name == name)
                    return child.gameObject;
                var found = FindInactiveChild(child, name);
                if (found != null) return found;
            }
            return null;
        }

        public static string GetPath(Transform t)
        {
            if (t.parent == null) return t.name;
            return GetPath(t.parent) + "/" + t.name;
        }

        public static int GetQuestStepIndex(string questText)
        {
            if (string.IsNullOrEmpty(questText)) return 0;
            try
            {
                int open = questText.IndexOf('(');
                if (open >= 0)
                {
                    string sub = questText.Substring(open + 1);
                    int slash = sub.IndexOf('/');
                    if (slash > 0)
                    {
                        string num = sub.Substring(0, slash);
                        if (int.TryParse(num, out int val)) return val;
                    }
                }
            }
            catch {}
            return 0;
        }

        public static string ParseJsonString(string json, string key)
        {
            string search = $"\"{key}\"";
            int idx = json.IndexOf(search);
            if (idx < 0) return "";
            int colonIdx = json.IndexOf(':', idx + search.Length);
            if (colonIdx < 0) return "";
            int quoteStart = json.IndexOf('"', colonIdx + 1);
            if (quoteStart < 0) return "";
            int quoteEnd = json.IndexOf('"', quoteStart + 1);
            if (quoteEnd < 0) return "";
            return json.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
        }

        public static int ParseJsonInt(string json, string key)
        {
            string search = $"\"{key}\"";
            int idx = json.IndexOf(search);
            if (idx < 0) return 0;
            int colonIdx = json.IndexOf(':', idx + search.Length);
            if (colonIdx < 0) return 0;
            string rest = json.Substring(colonIdx + 1).TrimStart();
            string numStr = "";
            foreach (char c in rest)
            {
                if (char.IsDigit(c) || c == '-') numStr += c;
                else if (numStr.Length > 0) break;
            }
            return int.TryParse(numStr, out int result) ? result : 0;
        }

        public static bool ParseJsonBool(string json, string key)
        {
            string search = $"\"{key}\"";
            int idx = json.IndexOf(search);
            if (idx < 0) return false;
            int colonIdx = json.IndexOf(':', idx + search.Length);
            if (colonIdx < 0) return false;
            string rest = json.Substring(colonIdx + 1).TrimStart();
            return rest.StartsWith("true", StringComparison.OrdinalIgnoreCase);
        }

        public static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
        }

        public static string CleanTextTags(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string result = text;
            while (result.Contains("<color"))
            {
                int tagStart = result.IndexOf("<color");
                int tagEnd = result.IndexOf('>', tagStart);
                if (tagEnd > tagStart)
                    result = result.Substring(0, tagStart) + result.Substring(tagEnd + 1);
                else break;
            }
            result = result.Replace("</color>", "");
            result = result.Replace("<b>", "").Replace("</b>", "");
            result = result.Replace("<i>", "").Replace("</i>", "");
            result = result.Replace("<size=", "").Replace("</size>", "");
            while (result.Contains("<") && result.Contains(">"))
            {
                int s = result.IndexOf('<');
                int e = result.IndexOf('>', s);
                if (e > s && e - s < 50)
                    result = result.Substring(0, s) + result.Substring(e + 1);
                else break;
            }
            return result.Trim();
        }

        public static string btnTextFromButton(Button btn)
        {
            if (btn == null) return "";
            GameObject go = btn.gameObject;
            var tmp = go.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null) return tmp.text;
            var t = go.GetComponentInChildren<Text>();
            if (t != null) return t.text;
            t = go.GetComponent<Text>();
            if (t != null) return t.text;
            tmp = go.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null) return tmp.text;
            return "";
        }
    }
}
