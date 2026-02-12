using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;
using System;
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
        TURNING_IN      // Đang trả quest (dialog)
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
        // State — mỗi module có flag riêng
        private bool _autoQuestEnabled = false;    // F1: Auto Quest + Pathfind
        private bool _autoAttackFlag = false;      // F3: Auto Attack
        private bool _autoInteractFlag = false;    // F4: Auto Interact NPC
        private bool _autoDialogFlag = false;      // F5: Auto NPC Dialog
        private bool _headlessMode = false;
        private float _statusLogTimer = 0f;
        private float _statusLogInterval = 30f;
        private string _currentScene = "";

        // Delayed find managers
        private float _findManagersTimer = -1f;
        private float _findManagersDelay = 5f;

        // Auto-login
        private float _autoLoginTimer = -1f;
        private float _autoLoginDelay = 4f; // Chờ 4 giây sau LoginScene load
        private bool _autoLoginDone = false;
        private string _loginUsername = "";
        private string _loginPassword = "";
        private int _loginServer = 0;
        private int _loginCharacter = 0;

        // Auto character select (sau khi login xong, tự chọn nhân vật vào game)
        private float _autoCharSelectTimer = -1f;
        private float _autoCharSelectDelay = 3f; // Chờ 3s sau login
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

        // Player position tracking (phát hiện nhân vật đứng yên)
        private Vector3 _lastPlayerPos = Vector3.zero;
        private float _idleTime = 0f;
        private float _idleThreshold = 3f;
        private float _moveCheckTimer = 0f;
        private float _moveCheckInterval = 0.5f;

        // NPC dialog timer
        private float _npcDialogTimer = 0f;
        private float _npcDialogInterval = 0.5f; // Check mỗi 0.5s (nhanh hơn để click hết hội thoại)
        private bool _npcDialogWasOpen = false; // Track dialog state để detect đóng

        // Quest change detection — dừng khi quest không đổi
        private string _trackedQuestText = "";
        private int _sameQuestPathfindCount = 0;
        private int _maxSameQuestPathfinds = 3; // Tối đa 3 lần click cùng quest
        private bool _isCollectQuest = false;    // Quest dạng "Thu thập" / "Nhặt"

        // Auto-enable quest timer (sau khi vào game tự động bật auto quest)
        private float _autoEnableQuestTimer = -1f;

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

        // Boss notification scanner
        private float _bossCheckTimer = 0f;
        private float _bossCheckInterval = 5f; // Scan mỗi 5s
        private string _lastBossNotification = "";
        private string _lastBossMap = "";
        private float _lastBossTime = 0f;
        private bool _killQuestDone = false; // Flag: kill quest vừa hoàn thành, cần pathfind

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
        /// Log an action to the STATE LOG file (for game state analysis)
        /// </summary>
        private void LogStateAction(string action)
        {
            if (!_stateLogRunning) return;
            try
            {
                string ts = DateTime.Now.ToString("HH:mm:ss.fff");
                string pos = "0,0,0";
                try { var cam = Camera.main; if (cam != null) pos = $"{cam.transform.position.x:F1},{cam.transform.position.y:F1},{cam.transform.position.z:F1}"; } catch {}
                string quest = "";
                try { quest = GetCurrentQuestText(); } catch {}
                string loc = "";
                try { loc = GetCurrentMapName() ?? ""; } catch {}
                string entry = $"[{ts}] | ACTION: {action} | Pos:{pos} | Location:{loc} | Quest:{quest}";
                _stateLogQueue.Enqueue(entry);
            }
            catch { }
        }

        void Start()
        {
            Plugin.Log.LogInfo("[BotController] Đang khởi tạo...");
            
            // Init State Logger (inline, no AddComponent needed)
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
                    Application.targetFrameRate = _targetFps;
                    QualitySettings.vSyncCount = 0;
                    Plugin.Log.LogInfo($"[Gfx] ✅ FPS: {_targetFps}, VSync: OFF");
                }

                // === 7. Tắt ParticleSystems nếu cần ===
                if (_disableParticles)
                {
                    DisableAllParticles();
                }

                // === 8. Tắt đèn phụ (giữ lại main directional light) ===
                DisableExtraLights();

                Plugin.Log.LogInfo("[Gfx] ✅ GraphicsSettings applied: LOW QUALITY");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Gfx] Error: {ex.Message}");
            }
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
                    accIdx = ParseJsonInt(json, "currentAccountIndex");
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
                        _loginUsername = ParseJsonString(obj, "username");
                        _loginPassword = ParseJsonString(obj, "password");
                        _loginServer = ParseJsonInt(obj, "server");
                        _loginCharacter = ParseJsonInt(obj, "character");
                        _configHeadless = ParseJsonBool(obj, "headless");
                        Plugin.Log.LogInfo($"[Bot] ✅ Config loaded: user={_loginUsername}, server={_loginServer}, char={_loginCharacter}, headless={_configHeadless}");

                        // Parse settings section (at root level)
                        int settingsStart = json.IndexOf("\"settings\"");
                        if (settingsStart >= 0)
                        {
                            int sObjStart = json.IndexOf('{', settingsStart);
                            int sObjEnd = json.IndexOf('}', sObjStart);
                            if (sObjStart >= 0 && sObjEnd >= 0)
                            {
                                string sObj = json.Substring(sObjStart, sObjEnd - sObjStart + 1);
                                _disableShadows = ParseJsonBool(sObj, "disableShadows");
                        _disableParticles = ParseJsonBool(sObj, "disableParticles");
                        _lowQuality = ParseJsonBool(sObj, "lowQuality");
                        _targetFps = ParseJsonInt(sObj, "targetFps");
                        if (_targetFps <= 0) _targetFps = 10;
                        _windowWidth = ParseJsonInt(sObj, "windowWidth");
                        _windowHeight = ParseJsonInt(sObj, "windowHeight");
                        _autoCleanRAM = ParseJsonBool(sObj, "autoCleanRAM");
                        _ultraLowRes = ParseJsonBool(sObj, "ultraLowRes");
                        _configAutoQuest = ParseJsonBool(sObj, "autoQuest");
                        _configSmartDialog = ParseJsonBool(sObj, "smartDialog");
                        _configCollectQuest = ParseJsonBool(sObj, "collectQuest");
                        _configAutoPathfind = ParseJsonBool(sObj, "autoPathfind");
                        // Default to true if key not found (ParseJsonBool returns false for missing)
                        if (!sObj.Contains("autoQuest")) _configAutoQuest = true;
                        if (!sObj.Contains("smartDialog")) _configSmartDialog = true;
                        if (!sObj.Contains("collectQuest")) _configCollectQuest = true;
                        if (!sObj.Contains("autoPathfind")) _configAutoPathfind = true;
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

        void Update()
        {
            // Resize check (Added)
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

            var currentScene = SceneManager.GetActiveScene().name;
            if (currentScene != _currentScene)
            {
                _currentScene = currentScene;
                Plugin.Log.LogInfo($"[Bot] Scene changed: {_currentScene}");
                _managersFound = false;

                // Dọn RAM khi đổi scene (nếu headless)
                if (_headlessMode && _autoCleanRAM && _lastCleanScene != _currentScene)
                {
                    _lastCleanScene = _currentScene;
                    CleanMemory($"scene-change:{_currentScene}");
                }
                
                if (_currentScene == "LoginScene")
                {
                    // Nếu đã login trước đó → bị disconnect → auto reconnect
                    if (_autoLoginDone && !string.IsNullOrEmpty(_loginUsername))
                    {
                        Plugin.Log.LogWarning("[Bot] ⚡ Phát hiện DISCONNECT → tự động đăng nhập lại!");
                        _autoLoginDone = false;
                        _charSelectDone = false;
                        _charSelectRetries = 0;
                        _autoLoginTimer = _autoLoginDelay + 2f; // Chờ thêm 2s
                    }
                    else if (!_autoLoginDone && !string.IsNullOrEmpty(_loginUsername))
                    {
                        _autoLoginTimer = _autoLoginDelay;
                        Plugin.Log.LogInfo($"[Bot] 🔑 Sẽ auto-login sau {_autoLoginDelay}s...");
                    }
                }
                else if (_currentScene != "InitScene")
                {
                    // Nếu đang auto quest → tìm managers nhanh hơn + reset cached refs
                    if (_autoQuestEnabled)
                    {
                        _findManagersTimer = 3f; // Nhanh hơn default
                        _autoAttackBtn = null;
                        _interactBtn = null;
                        _shortMissionBtn = null;
                        _shortMissionPanel = null;
                        _interactBtnWasVisible = false;
                        _autoAttackEnabled = false; // Cần re-enable
                        Plugin.Log.LogInfo("[Bot] 🗺️ Map transition detected → reset cached refs, re-finding managers...");
                    }
                    else
                    {
                        _findManagersTimer = _findManagersDelay;
                    }
                    Plugin.Log.LogInfo($"[Bot] Sẽ tìm managers sau {_findManagersTimer}s...");
                }

                // Re-apply graphics settings khi đổi scene (game hay reset lại)
                ApplyGraphicsSettings();
            }

            // Timer auto-login
            if (_autoLoginTimer > 0)
            {
                _autoLoginTimer -= Time.deltaTime;
                if (_autoLoginTimer <= 0)
                {
                    _autoLoginTimer = -1f;
                    DoAutoLogin();
                }
            }

            // Timer auto character select (sau khi login xong)
            if (_autoCharSelectTimer > 0)
            {
                _autoCharSelectTimer -= Time.deltaTime;
                if (_autoCharSelectTimer <= 0)
                {
                    _autoCharSelectTimer = -1f;
                    DoAutoSelectCharacter();
                }
            }

            // Timer enter game (clíck nút vào game sau khi chọn nhân vật)
            if (_enterGameTimer > 0)
            {
                _enterGameTimer -= Time.deltaTime;
                if (_enterGameTimer <= 0)
                {
                    _enterGameTimer = -1f;
                    DoAutoEnterGame();
                }
            }

            // Timer auto-enable quest (sau khi vào game)
            if (_autoEnableQuestTimer > 0)
            {
                _autoEnableQuestTimer -= Time.deltaTime;
                if (_autoEnableQuestTimer <= 0)
                {
                    _autoEnableQuestTimer = -1f;
                    if (!_autoQuestEnabled)
                    {
                        Plugin.Log.LogInfo("[Bot] 🎮 Auto-enable quest system sau khi vào game!");
                        ToggleAutoAll();
                    }
                }
            }

            // Timer tìm managers
            if (_findManagersTimer > 0)
            {
                _findManagersTimer -= Time.deltaTime;
                if (_findManagersTimer <= 0)
                {
                    _findManagersTimer = -1f;
                    FindManagers();

                    // Re-enable auto attack nếu đang auto quest (sau map transition)
                    if (_autoQuestEnabled && !_autoAttackEnabled)
                    {
                        ToggleAutoAttack(true);
                        Plugin.Log.LogInfo("[Bot] 🗺️ Re-enabled auto attack sau map transition");
                    }
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
                _gameSpeed = (_gameSpeed >= 3f) ? 1f : _gameSpeed + 1f;
                Time.timeScale = _gameSpeed;
                LogActivity($"⚡ Game Speed: x{_gameSpeed}");
            }
            if (Input.GetKeyDown(KeyCode.F11))
            {
                _autoLoginDone = false;
                DoAutoLogin();
            }
            if (Input.GetKeyDown(KeyCode.F12))
            {
                LogPlayerInfo();
                LogQuestStatus();
            }

            bool inGame = _currentScene != "LoginScene" && _currentScene != "InitScene";

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
                bool dialogOpen = false;
                try
                {
                    var npcPanelCheck = GameObject.Find("NpcInteractPanel(Clone)");
                    dialogOpen = npcPanelCheck != null && npcPanelCheck.activeSelf && npcPanelCheck.activeInHierarchy;
                }
                catch { }

                _pathfindTimer += Time.deltaTime;
                // Fix: Nếu _killQuestDone (vừa đánh xong quái), cho phép pathfind ngay cả khi chưa idle (đang chạy/đánh)
                if (_pathfindTimer >= _pathfindInterval && (_idleTime >= _idleThreshold || _killQuestDone) && _pathfindCooldown <= 0 && !dialogOpen)
                {
                    _pathfindTimer = 0f;
                    TriggerAutoPathfind();
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
            if (_autoDialogFlag && inGame)
            {
                _popupDismissTimer += Time.deltaTime;
                if (_popupDismissTimer >= _popupDismissInterval)
                {
                    _popupDismissTimer = 0f;
                    TryDismissPopups();
                }
            }

            // ======================== MODULE 5: GUIDE/TUTORIAL DISMISS (Phase 3) ========================
            if (_autoQuestEnabled && inGame)
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
                            if (eventSystem != null && eventSystem.currentSelectedGameObject != null)
                            {
                                var clicked = eventSystem.currentSelectedGameObject;
                                if (clicked != _lastClickedObject)
                                {
                                    _lastClickedObject = clicked;
                                    string objName = clicked.name ?? "unknown";
                                    string path = GetPath(clicked.transform);
                                    
                                    // Try get button text
                                    string btnText = "";
                                    try {
                                        var tmp = clicked.GetComponentInChildren<TextMeshProUGUI>();
                                        if (tmp != null) btnText = tmp.text ?? "";
                                    } catch {}
                                    
                                    LogStateAction($"USER_CLICK: {objName} | Text: {btnText} | Path: {path}");
                                }
                            }
                            else
                            {
                                // Click không vào UI → click vào game world (có thể click NPC/mob)
                                Vector3 mousePos = Input.mousePosition;
                                LogStateAction($"USER_CLICK_WORLD: ScreenPos({mousePos.x:F0},{mousePos.y:F0})");
                                _lastClickedObject = null;
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

            // ======================== STATE OBSERVER (inline logging) ========================
            if (_stateLogRunning)
            {
                try
                {
                    string qText = GetCurrentQuestText();
                    int stateId = qText.GetHashCode();
                    int stepIdx = GetQuestStepIndex(qText);
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

                // Build JSON manually (no System.Text.Json in IL2CPP)
                string json = "{\n" +
                    $"  \"username\": \"{EscapeJson(_loginUsername)}\",\n" +
                    $"  \"scene\": \"{EscapeJson(_currentScene)}\",\n" +
                    $"  \"map\": \"{EscapeJson(mapName)}\",\n" +
                    $"  \"zone\": \"{EscapeJson(zoneName)}\",\n" +
                    $"  \"posX\": \"{posX}\",\n" +
                    $"  \"posY\": \"{posY}\",\n" +
                    $"  \"quest\": \"{EscapeJson(questText)}\",\n" +
                    $"  \"questHint\": \"{EscapeJson(questHint)}\",\n" +
                    $"  \"autoEnabled\": {(_autoQuestEnabled ? "true" : "false")},\n" +
                    $"  \"attackEnabled\": {(_autoAttackEnabled ? "true" : "false")},\n" +
                    $"  \"headless\": {(_headlessMode ? "true" : "false")},\n" +
                    $"  \"collectQuest\": {(_isCollectQuest ? "true" : "false")},\n" +
                    $"  \"lastBoss\": \"{EscapeJson(_lastBossNotification)}\",\n" +
                    $"  \"lastBossMap\": \"{EscapeJson(_lastBossMap)}\",\n" +
                    $"  \"timestamp\": \"{DateTime.Now:HH:mm:ss}\"\n" +
                    "}";

                File.WriteAllText(_statusFilePath, json);
            }
            catch { /* Ignore file write errors */ }
        }

        private string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
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
                var loginPanel = FindSingletonByType("LoginPanel");
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
                        _autoEnableQuestTimer = 8f;
                        Plugin.Log.LogInfo("[Bot] 🎮 Sẽ tự động bật Auto Quest sau 8s...");
                        return;
                    }
                }
                else
                {
                    Plugin.Log.LogInfo("[Bot] 🎮 CharacterChoosingPanel đã tắt → có thể đã vào game!");
                    // Vẫn schedule auto-enable phòng trường hợp panel đóng trước khi timer fire
                    if (_autoEnableQuestTimer < 0)
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

        private string ParseJsonString(string json, string key)
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

        private int ParseJsonInt(string json, string key)
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

        private bool ParseJsonBool(string json, string key)
        {
            string search = $"\"{key}\"";
            int idx = json.IndexOf(search);
            if (idx < 0) return false;
            int colonIdx = json.IndexOf(':', idx + search.Length);
            if (colonIdx < 0) return false;
            string rest = json.Substring(colonIdx + 1).TrimStart();
            return rest.StartsWith("true", StringComparison.OrdinalIgnoreCase);
        }

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
                        CallMethod(_autoMissionManager, "auto");
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
                        CallMethod(_mainGame, "setPlayerOnAutoMode");
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
                    try { CallMethod(_autoMissionManager, "stop"); } catch { }
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
                bool dialogOpen = false;
                try
                {
                    var npcPanel = GameObject.Find("NpcInteractPanel(Clone)");
                    dialogOpen = npcPanel != null && npcPanel.activeSelf && npcPanel.activeInHierarchy;
                }
                catch { }

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
                    string path = GetPath(btn.transform);

                    // Capture local copies for closure
                    string capName = btnName;
                    string capText = btnText;
                    string capPath = path;

                    // XÓA tất cả listener cũ trước khi add mới (tránh tích lũy duplicate)
                    // Lưu ý: điều này cũng xóa listener game gốc, nhưng onClick.Invoke() 
                    // từ bot sẽ vẫn hoạt động vì bot gọi trực tiếp
                    // Chỉ log khi USER click (không phải bot invoke)
                    btn.onClick.AddListener((UnityEngine.Events.UnityAction)(() =>
                    {
                        try
                        {
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
        /// Click ShortMissionPanel để kích hoạt auto-pathfind đến quest NPC
        /// Có quest detection: kill quest thì skip, same quest thì giới hạn click
        /// </summary>
        private void TriggerAutoPathfind()
        {
            try
            {
                // === 1. Đọc quest text hiện tại ===
                string questText = GetCurrentQuestText();

                if (!string.IsNullOrEmpty(questText))
                {
                    // === 2. Collect quest detection (TRƯỚC kill quest) ===
                    // Quest "Thu thập", "Nhặt", "Collect" cũng có dạng (X/Y) nhưng KHÔNG phải kill quest
                    _isCollectQuest = questText.Contains("Thu thập") || questText.Contains("Nhặt") 
                        || questText.Contains("Collect") || questText.Contains("Lượm")
                        || questText.Contains("thu thập") || questText.Contains("nhặt")
                        || questText.Contains("Tìm") || questText.Contains("tìm")
                        || questText.Contains("Lấy") || questText.Contains("lấy")
                        || questText.Contains("Gặp") || questText.Contains("gặp")
                        || questText.Contains("Sử dụng") || questText.Contains("Dùng")
                        || questText.Contains("Đến") || questText.Contains("đến");

                    // === 3. Kill quest detection: có dạng (X/Y) thì SKIP pathfind ===
                    // CHỈ skip nếu KHÔNG phải collect quest
                    if (!_isCollectQuest)
                    {
                        int parenOpen = questText.IndexOf('(');
                        if (parenOpen >= 0)
                        {
                            int parenClose = questText.IndexOf(')', parenOpen);
                            if (parenClose > parenOpen)
                            {
                                string inside = questText.Substring(parenOpen + 1, parenClose - parenOpen - 1);
                                int slash = inside.IndexOf('/');
                                if (slash > 0)
                                {
                                    // Remove color tags trước khi parse
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
                                    slash = cleaned.IndexOf('/');
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
                                                // Removed return: user says clicking during kill quest also auto-attacks properly
                                            }
                                            else
                                            {
                                                // cur >= total → Kill quest HOÀN THÀNH! Force pathfind về NPC NGAY
                                                if (!_killQuestDone)
                                                {
                                                    _killQuestDone = true;
                                                    _sameQuestPathfindCount = 0;
                                                    _pathfindCooldown = 0;
                                                    LogActivity($"✅ Kill quest XONG: {questText} ({cur}/{total}) → pathfind về NPC!");
                                                }
                                                // KHÔNG return → fall through xuống phần click ShortMissionPanel
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        LogActivity($"📦 Collect quest: {questText} → pathfind to NPC");
                    }

                    // === 4. Quest change detection ===
                    if (questText == _trackedQuestText)
                    {
                        _sameQuestPathfindCount++;
                        if (_sameQuestPathfindCount >= _maxSameQuestPathfinds)
                        {
                            // Removed return: User wants continuous clicking even for same quest
                        }
                    }
                    else
                    {
                        // Quest thay đổi → reset counter
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
                            var found = FindInactiveChild(hudCanvas.transform, "ShortMissionPanel");
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
                    _pathfindCooldown = _pathfindCooldownTime;
                    _idleTime = 0f;
                    LogActivity("🚀 Pathfind: Clicked quest panel → đi đến NPC");
                    LogStateAction("PATHFIND click ShortMissionPanel → moving to NPC");
                }
                else
                {
                    Plugin.Log.LogWarning("[Bot] ❌ ShortMissionPanel not found (cả qua Find và hierarchy search)");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Bot] Auto Pathfind error: {ex.Message}");
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
                    _shortMissionPanel = FindSingletonByType("ShortMissionPanel");

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
                var state = CallMethodReturn<bool>(_autoMissionManager, "get_state");
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
        /// Lấy tên map hiện tại từ MiniMap/MapName (TextMeshProUGUI) hoặc MapName (TextMeshPro world-space)
        /// </summary>
        private string GetCurrentMapName()
        {
            try
            {
                // Strategy 1: Tìm MapName trong MiniMap (UI text trên minimap)
                var miniMap = GameObject.Find("MiniMap");
                if (miniMap != null)
                {
                    var mapNameGo = FindInactiveChild(miniMap.transform, "MapName");
                    if (mapNameGo != null)
                    {
                        // Thử TextMeshProUGUI (UI)
                        var tmpUI = mapNameGo.GetComponent<TextMeshProUGUI>();
                        if (tmpUI != null && !string.IsNullOrEmpty(tmpUI.text))
                            return tmpUI.text.Trim();

                        // Thử TextMeshPro (3D)
                        var tmp3D = mapNameGo.GetComponent<TMPro.TextMeshPro>();
                        if (tmp3D != null && !string.IsNullOrEmpty(tmp3D.text))
                            return tmp3D.text.Trim();
                    }
                }

                // Strategy 2: Tìm MinimapName (world-space TMPro trên gateway)
                var allTMP = GameObject.FindObjectsOfType<TMPro.TextMeshPro>();
                foreach (var tmp in allTMP)
                {
                    if (tmp == null || !tmp.gameObject.activeSelf) continue;
                    if (tmp.gameObject.name == "MinimapName" || tmp.gameObject.name == "MapName")
                    {
                        string text = tmp.text;
                        if (!string.IsNullOrEmpty(text))
                            return text.Trim();
                    }
                }
            }
            catch { }
            return "";
        }

        /// <summary>
        /// Lấy tên zone/khu vực từ ZoneObject hoặc ClanMemberZonePanel
        /// </summary>
        private string GetCurrentZoneName()
        {
            try
            {
                // Tìm ZoneObject trong MiniMap
                var miniMap = GameObject.Find("MiniMap");
                if (miniMap != null)
                {
                    var zoneGo = FindInactiveChild(miniMap.transform, "ZoneObject");
                    if (zoneGo != null)
                    {
                        // Tìm text component
                        var tmpUI = zoneGo.GetComponentInChildren<TextMeshProUGUI>(true);
                        if (tmpUI != null && !string.IsNullOrEmpty(tmpUI.text))
                            return tmpUI.text.Trim();

                        var tmp3D = zoneGo.GetComponentInChildren<TMPro.TextMeshPro>(true);
                        if (tmp3D != null && !string.IsNullOrEmpty(tmp3D.text))
                            return tmp3D.text.Trim();
                    }
                }

                // Fallback: tìm scene name
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
            try
            {
                // Tìm MainPlayer
                var mainCam = Camera.main;
                if (mainCam != null)
                    return mainCam.transform.position;
            }
            catch { }
            return Vector3.zero;
        }

        // ======================== AUTO ATTACK ========================

        private void ToggleAutoAttack(bool enable)
        {
            if (_autoAttackBtn == null)
            {
                // Try to find AutoAttackButton
                var go = GameObject.Find("AutoAttackButton");
                if (go != null)
                {
                    _autoAttackBtn = go.GetComponent<Button>();
                    if (_autoAttackBtn == null)
                    {
                        // Might be on a child
                        _autoAttackBtn = go.GetComponentInChildren<Button>();
                    }
                }
            }

            if (_autoAttackBtn != null)
            {
                try
                {
                    // Check current state and toggle if needed
                    if (enable && !_autoAttackEnabled)
                    {
                        _autoAttackBtn.onClick.Invoke();
                        _autoAttackEnabled = true;
                        Plugin.Log.LogInfo("[Bot] ✅ Auto Attack: BẬT (clicked AutoAttackButton)");
                    }
                    else if (!enable && _autoAttackEnabled)
                    {
                        _autoAttackBtn.onClick.Invoke();
                        _autoAttackEnabled = false;
                        Plugin.Log.LogInfo("[Bot] ⛔ Auto Attack: TẮT");
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Bot] Auto Attack error: {ex.Message}");
                }
            }
            else
            {
                Plugin.Log.LogWarning("[Bot] ⚠️ AutoAttackButton chưa tìm thấy");
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

        /// <summary>
        /// Tự động click qua NPC dialog:
        /// 1. NextPlace — tiếp tục hội thoại NPC
        /// 2. InteractPanelButton — nhận nhiệm vụ / hoàn thành quest
        /// 3. Các nút popup khác (OK, Đồng ý, Xác nhận)
        /// </summary>
        private void TryClickNpcDialog()
        {
            try
            {
                // === 1. Tìm NpcInteractPanel (dialog NPC) ===
                var npcPanel = GameObject.Find("NpcInteractPanel(Clone)");
                if (npcPanel != null && npcPanel.activeSelf && npcPanel.activeInHierarchy)
                {
                    // Log all children for debugging
                    string childNames = "";
                    for (int ci = 0; ci < npcPanel.transform.childCount; ci++)
                    {
                        var c = npcPanel.transform.GetChild(ci);
                        if (c != null) childNames += c.gameObject.name + (c.gameObject.activeSelf ? "(ON)" : "(OFF)") + ", ";
                    }
                    Plugin.Log.LogInfo($"[Bot] 🔍 NpcInteractPanel found! Children: [{childNames}]");

                    // === Ưu tiên 1: Tìm InteractButtonHolder → click nút quest (Nhiệm vụ, etc.) ===
                    // PHẢI check trước NextPlace vì NextPlace luôn active khi dialog mở
                    // === BUG FIX v41: Tìm tất cả button trong panel (kể cả nested) ===
                    // Không phụ thuộc vào InteractButtonHolder nữa
                    var allButtons = npcPanel.GetComponentsInChildren<Button>(true);
                    
                    Button questBtn = null;
                    Button talkBtn = null; // NextPlace, Nói chuyện
                    Button anyBtn = null;

                    foreach (var btn in allButtons)
                    {
                        if (btn == null || !btn.gameObject.activeSelf || !btn.interactable) continue;
                        
                        string btnName = btn.gameObject.name ?? "";
                        string btnText = "";
                        try {
                            var tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
                            if (tmp != null) btnText = tmp.text ?? "";
                            else {
                                var legacyText = btn.GetComponentInChildren<UnityEngine.UI.Text>();
                                if (legacyText != null) btnText = legacyText.text ?? "";
                            }
                        } catch {}

                        // Debug log - ENABLED for v42 debugging
                        Plugin.Log.LogInfo($"[Bot] 🔍 NPC Button: '{btnName}' | Text: '{btnText}' | Path: {GetPath(btn.transform)}");

                        // Skip close buttons
                        if (btnName.Contains("Close") || btnName.Contains("Exit") || btnName == "BtnClose") continue;

                        // 1. Quest keywords (PRIORITY)
                        if (btnText.Contains("Nhiệm vụ") || btnText.Contains("Nhận") || 
                            btnText.Contains("Hoàn thành") || btnText.Contains("Trả") ||
                            btnText.Contains("Đồng ý") || btnText.Contains("Quest"))
                        {
                            questBtn = btn;
                            break; // Found priority
                        }

                        // 1b. Name-based priority (v42 fix)
                        if (btnName == "InteractPanelButton") 
                        {
                            // Nếu chưa tìm thấy quest btn qua text, dùng cái này làm ứng viên số 1
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

                    // Execute click with priority
                    if (questBtn != null)
                    {
                        Plugin.Log.LogInfo($"[Bot] 📜 Click Quest Button (Priority): '{questBtn.gameObject.name}'");
                        LogStateAction($"CLICK NPC_QUEST_BTN: {questBtn.gameObject.name}");
                        questBtn.onClick.Invoke();
                        return;
                    }
                    if (talkBtn != null)
                    {
                        Plugin.Log.LogInfo($"[Bot] 💬 Click Talk/Next: '{talkBtn.gameObject.name}'");
                        LogStateAction($"CLICK NPC_TALK_BTN: {talkBtn.gameObject.name}");
                        talkBtn.onClick.Invoke();
                        return;
                    }
                    if (anyBtn != null)
                    {
                        Plugin.Log.LogInfo($"[Bot] 👆 Click Any Button (fallback): '{anyBtn.gameObject.name}'");
                        LogStateAction($"CLICK NPC_ANY_BTN: {anyBtn.gameObject.name}");
                        anyBtn.onClick.Invoke();
                        return;
                    }

                }

                // === 2. PopupCanvas scanning REMOVED ===
                // Trước đây bot scan toàn bộ PopupCanvas → click nhầm vào menu người chơi
                // Giờ chỉ xử lý NpcInteractPanel ở trên, popup dismiss do MODULE 4 xử lý
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Bot] NPC Dialog click error: {ex.Message}");
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
                    _shortMissionPanel = FindSingletonByType("ShortMissionPanel");
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

                        // Check nếu có PopupBehaviour → đây là popup cần dismiss
                        var popupComp = child.GetComponent<Component>();
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
                    _guideManager = FindSingletonByType("GuideManager");

                if (_guideManager == null || !_guideManager.gameObject.activeSelf) return;

                // Check hasGuide()
                bool hasGuide = false;
                try
                {
                    hasGuide = CallMethodReturn<bool>(_guideManager, "hasGuide");
                }
                catch { return; } // Không có method → skip

                if (hasGuide)
                {
                    try
                    {
                        CallMethodNoParam(_guideManager, "onGuideStop");
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
                    _autoAttackBlackBoard = FindSingletonByType("AutoAttackBlackBoardComponent");
            // Fallback: search via hierarchy since AutoAttackBlackBoard is active=False
            if (_autoAttackBlackBoard == null)
            {
                var serviceGo = GameObject.Find("Service");
                if (serviceGo != null)
                {
                    var found = FindInactiveChild(serviceGo.transform, "AutoAttackBlackBoard");
                    if (found != null)
                    {
                        var comps = found.GetComponentsInChildren<MonoBehaviour>(true);
                        foreach (var c in comps)
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
                    needRevive = CallMethodReturn<bool>(_autoAttackBlackBoard, "needToRevive");
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
                extractedMap = CleanTextTags(extractedMap);
            }
            else
            {
                int atIdx = text.IndexOf(" at ");
                if (atIdx >= 0)
                {
                    extractedMap = text.Substring(atIdx + 4).Trim();
                    extractedMap = CleanTextTags(extractedMap);
                }
            }

            if (!string.IsNullOrEmpty(extractedMap))
                _lastBossMap = extractedMap;

            // === LOG BOSS NOTIFICATION ===
            string cleanText = CleanTextTags(text);
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
        private string CleanTextTags(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string result = text;
            // Remove <color=#...>...</color> tags
            while (result.Contains("<color"))
            {
                int tagStart = result.IndexOf("<color");
                int tagEnd = result.IndexOf('>', tagStart);
                if (tagEnd > tagStart)
                    result = result.Substring(0, tagStart) + result.Substring(tagEnd + 1);
                else break;
            }
            result = result.Replace("</color>", "");
            // Remove other common tags
            result = result.Replace("<b>", "").Replace("</b>", "");
            result = result.Replace("<i>", "").Replace("</i>", "");
            result = result.Replace("<size=", "").Replace("</size>", "");
            // Remove remaining tags
            while (result.Contains("<") && result.Contains(">"))
            {
                int s = result.IndexOf('<');
                int e = result.IndexOf('>', s);
                if (e > s && e - s < 50) // Only short tags
                    result = result.Substring(0, s) + result.Substring(e + 1);
                else break;
            }
            return result.Trim();
        }

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

        // ======================== FIND MANAGERS ========================

        private void FindManagers()
        {
            Plugin.Log.LogInfo("[Bot] Đang tìm game managers + UI buttons...");
            try
            {
                // === Managers ===
                _autoMissionManager = FindSingletonByType("AutoMissionManager");
                if (_autoMissionManager != null)
                    Plugin.Log.LogInfo("[Bot] ✅ Tìm thấy AutoMissionManager!");
                else
                    Plugin.Log.LogWarning("[Bot] ❌ Không tìm thấy AutoMissionManager");

                _playerDataManager = FindSingletonByType("PlayerDataManager");
                if (_playerDataManager != null)
                    Plugin.Log.LogInfo("[Bot] ✅ Tìm thấy PlayerDataManager!");

                _gameManager = FindSingletonByType("GameManager");
                if (_gameManager != null)
                    Plugin.Log.LogInfo("[Bot] ✅ Tìm thấy GameManager!");

                _appManager = FindSingletonByType("AppManager");
                if (_appManager != null)
                    Plugin.Log.LogInfo("[Bot] ✅ Tìm thấy AppManager!");

                // === UI Buttons ===
                var autoAtkGO = GameObject.Find("AutoAttackButton");
                if (autoAtkGO != null)
                {
                    _autoAttackBtn = autoAtkGO.GetComponent<Button>();
                    if (_autoAttackBtn == null)
                        _autoAttackBtn = autoAtkGO.GetComponentInChildren<Button>();
                    Plugin.Log.LogInfo($"[Bot] ✅ AutoAttackButton: {(_autoAttackBtn != null ? "found" : "no Button comp")}");
                }
                else
                    Plugin.Log.LogWarning("[Bot] ❌ AutoAttackButton GO not found");

                var interactGO = GameObject.Find("InteractButton");
                if (interactGO != null)
                {
                    _interactBtn = interactGO.GetComponent<Button>();
                    if (_interactBtn == null)
                        _interactBtn = interactGO.GetComponentInChildren<Button>();
                    Plugin.Log.LogInfo($"[Bot] ✅ InteractButton: {(_interactBtn != null ? "found" : "no Button comp")}");
                }
                else
                    Plugin.Log.LogWarning("[Bot] ❌ InteractButton GO not found");

                // === Quest Panel ===
                _shortMissionPanel = FindSingletonByType("ShortMissionPanel");
                if (_shortMissionPanel != null)
                    Plugin.Log.LogInfo("[Bot] ✅ Tìm thấy ShortMissionPanel!");

                // === MainGame (trên SceneManager GO) ===
                _mainGame = FindSingletonByType("MainGame");
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
                _guideManager = FindSingletonByType("GuideManager");
                if (_guideManager != null)
                    Plugin.Log.LogInfo("[Bot] ✅ Tìm thấy GuideManager!");

                _autoAttackBlackBoard = FindSingletonByType("AutoAttackBlackBoardComponent");
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
                            var hasGI = HasMethod(mb, "gI");
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

        // ======================== HELPERS ========================

        private MonoBehaviour FindSingletonByType(string typeName)
        {
            var allMB = GameObject.FindObjectsOfType<MonoBehaviour>();
            foreach (var mb in allMB)
            {
                if (mb == null) continue;
                if (mb.GetIl2CppType().Name == typeName)
                    return mb;
            }
            return null;
        }

        private void CallMethod(MonoBehaviour target, string methodName)
        {
            var methods = target.GetIl2CppType().GetMethods();
            foreach (var m in methods)
            {
                if (m.Name == methodName)
                {
                    m.Invoke(target, null);
                    return;
                }
            }
            throw new Exception($"Method '{methodName}' not found on {target.GetIl2CppType().Name}");
        }

        /// <summary>
        /// Strictly call a parameterless method — throws if all overloads require params
        /// </summary>
        private void CallMethodNoParam(MonoBehaviour target, string methodName)
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

        private T CallMethodReturn<T>(MonoBehaviour target, string methodName)
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

        private bool HasMethod(MonoBehaviour target, string methodName)
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

        /// <summary>
        /// Tìm child GameObject theo tên trong hierarchy, kể cả inactive objects.
        /// GameObject.Find() chỉ tìm được active objects — method này tìm tất cả.
        /// </summary>
        private GameObject FindInactiveChild(Transform parent, string name)
        {
            if (parent == null) return null;
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child == null) continue;
                if (child.gameObject.name == name)
                    return child.gameObject;
                // Recursive search
                var found = FindInactiveChild(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private string GetPath(Transform t)
        {
            if (t.parent == null) return t.name;
            return GetPath(t.parent) + "/" + t.name;
        }

        private int GetQuestStepIndex(string questText)
        {
            if (string.IsNullOrEmpty(questText)) return 0;
            try
            {
                // Format: "Kill monsters (5/10)" -> return 5
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

        void OnDestroy()
        {
            _stateLogRunning = false;
            if (_stateWriteThread != null && _stateWriteThread.IsAlive)
                _stateWriteThread.Join(500);
        }
    }
}
                                    