using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;

namespace AutoQuestPlugin
{
    /// <summary>
    /// RuntimeInspector - Tự động quét tất cả GameObject và Component khi game chạy.
    /// Mục đích: Tìm ra class nào là Quest, Battle, Login, Player...
    /// dù tên đã bị obfuscate (OLMJOHOJKFL...).
    /// 
    /// Kết quả sẽ được ghi vào file log để phân tích.
    /// </summary>
    public class RuntimeInspector : MonoBehaviour
    {
        private float _scanInterval = 10f; // Quét mỗi 10 giây
        private float _lastScanTime = 0f;

        private string _logDir;
        private HashSet<string> _loggedScenes = new HashSet<string>();

        void Start()
        {
            _logDir = Path.Combine(
                Path.GetDirectoryName(typeof(Plugin).Assembly.Location),
                "inspector_logs"
            );
            Directory.CreateDirectory(_logDir);

            Plugin.Log.LogInfo("============================================");
            Plugin.Log.LogInfo("  [RuntimeInspector] KHỞI ĐỘNG!");
            Plugin.Log.LogInfo("  Logs sẽ được ghi vào: " + _logDir);
            Plugin.Log.LogInfo("  F3 = Quét toàn bộ Scene hiện tại");
            Plugin.Log.LogInfo("  F4 = Tìm các class có Update()");
            Plugin.Log.LogInfo("  F5 = Tìm UI Text (hiện máu/tên)");
            Plugin.Log.LogInfo("  F6 = Dump tất cả Button OnClick");
            Plugin.Log.LogInfo("  F7 = Tìm Singleton/Manager patterns");
            Plugin.Log.LogInfo("  F8 = Tìm Network/Message classes");
            Plugin.Log.LogInfo("============================================");
        }

        void Update()
        {
            // F3 = Full scene scan
            if (Input.GetKeyDown(KeyCode.F3))
            {
                ScanCurrentScene();
            }

            // F4 = Tìm class có MonoBehaviour (Update loop)
            if (Input.GetKeyDown(KeyCode.F4))
            {
                FindMonoBehaviours();
            }

            // F5 = Tìm tất cả UI Text
            if (Input.GetKeyDown(KeyCode.F5))
            {
                FindAllUIText();
            }

            // F6 = Dump Button OnClick handlers
            if (Input.GetKeyDown(KeyCode.F6))
            {
                DumpButtonHandlers();
            }

            // F7 = Tìm Singleton/Manager patterns
            if (Input.GetKeyDown(KeyCode.F7))
            {
                FindSingletonManagers();
            }

            // F8 = Tìm Network/Session classes
            if (Input.GetKeyDown(KeyCode.F8))
            {
                FindNetworkClasses();
            }

            // Auto scan khi đổi scene
            AutoScanNewScene();
        }

        // ============ QUÉT SCENE ============

        private void AutoScanNewScene()
        {
            if (Time.time - _lastScanTime < _scanInterval) return;
            _lastScanTime = Time.time;

            var sceneName = SceneManager.GetActiveScene().name;
            if (!_loggedScenes.Contains(sceneName))
            {
                _loggedScenes.Add(sceneName);
                Plugin.Log.LogInfo($"[Inspector] Scene mới: {sceneName} - Đang quét...");
                ScanCurrentScene();
            }
        }

        /// <summary>
        /// F3: Quét TOÀN BỘ GameObject trong scene hiện tại
        /// Ghi ra file: scene_{tên}.txt
        /// </summary>
        private void ScanCurrentScene()
        {
            try
            {
                var sceneName = SceneManager.GetActiveScene().name;
                var sb = new StringBuilder();
                sb.AppendLine($"=== SCENE SCAN: {sceneName} ===");
                sb.AppendLine($"Time: {DateTime.Now}");
                sb.AppendLine();

                // Lấy tất cả root objects
                var rootObjects = SceneManager.GetActiveScene().GetRootGameObjects();
                sb.AppendLine($"Root Objects: {rootObjects.Length}");
                sb.AppendLine();

                foreach (var root in rootObjects)
                {
                    ScanGameObject(root, sb, 0);
                }

                // Ghi file
                var filename = $"scene_{sceneName}_{DateTime.Now:HHmmss}.txt";
                var filepath = Path.Combine(_logDir, filename);
                File.WriteAllText(filepath, sb.ToString());

                Plugin.Log.LogInfo($"[Inspector] Đã quét scene '{sceneName}' -> {filepath}");
                Plugin.Log.LogInfo($"[Inspector] Tổng {rootObjects.Length} root objects");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Inspector] Lỗi quét scene: {ex.Message}");
            }
        }

        private void ScanGameObject(GameObject go, StringBuilder sb, int depth)
        {
            var indent = new string(' ', depth * 2);
            sb.AppendLine($"{indent}[GameObject] {go.name} (active={go.activeSelf})");

            // Liệt kê components
            var components = go.GetComponents<Component>();
            foreach (var comp in components)
            {
                if (comp == null) continue;
                var typeName = comp.GetIl2CppType().FullName;
                sb.AppendLine($"{indent}  → Component: {typeName}");
            }

            // Đệ quy children (giới hạn 3 level để tránh quá dài)
            if (depth < 3)
            {
                for (int i = 0; i < go.transform.childCount; i++)
                {
                    ScanGameObject(go.transform.GetChild(i).gameObject, sb, depth + 1);
                }
            }
        }

        // ============ TÌM MONOBEHAVIOUR (CÁC CLASS CÓ UPDATE) ============

        /// <summary>
        /// F4: Tìm tất cả MonoBehaviour đang active
        /// Đây là cách tìm class điều khiển chính (quest, battle...)
        /// </summary>
        private void FindMonoBehaviours()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== MONOBEHAVIOUR SCAN ===");
                sb.AppendLine($"Time: {DateTime.Now}");
                sb.AppendLine();

                // Tìm TẤT CẢ MonoBehaviour đang tồn tại
                var allMB = GameObject.FindObjectsOfType<MonoBehaviour>();
                sb.AppendLine($"Tổng MonoBehaviour: {allMB.Length}");
                sb.AppendLine();

                // Nhóm theo type
                var grouped = new Dictionary<string, List<string>>();
                foreach (var mb in allMB)
                {
                    if (mb == null) continue;
                    var typeName = mb.GetIl2CppType().FullName;
                    if (!grouped.ContainsKey(typeName))
                        grouped[typeName] = new List<string>();
                    grouped[typeName].Add(mb.gameObject.name);
                }

                // Sắp xếp theo số lượng instance
                foreach (var kvp in grouped.OrderByDescending(x => x.Value.Count))
                {
                    sb.AppendLine($"[{kvp.Value.Count}x] {kvp.Key}");
                    foreach (var objName in kvp.Value.Take(5))
                    {
                        sb.AppendLine($"       → on: {objName}");
                    }
                    if (kvp.Value.Count > 5)
                        sb.AppendLine($"       ... và {kvp.Value.Count - 5} nữa");
                    sb.AppendLine();
                }

                var filepath = Path.Combine(_logDir, $"monobehaviours_{DateTime.Now:HHmmss}.txt");
                File.WriteAllText(filepath, sb.ToString());

                Plugin.Log.LogInfo($"[Inspector] Tìm thấy {grouped.Count} loại MonoBehaviour");
                Plugin.Log.LogInfo($"[Inspector] File: {filepath}");

                // Log top 10 ra console luôn
                int count = 0;
                foreach (var kvp in grouped.OrderByDescending(x => x.Value.Count))
                {
                    Plugin.Log.LogInfo($"  [{kvp.Value.Count}x] {kvp.Key} → {kvp.Value.First()}");
                    if (++count >= 15) break;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Inspector] Lỗi tìm MB: {ex.Message}");
            }
        }

        // ============ TÌM UI TEXT ============

        /// <summary>
        /// F5: Tìm tất cả UI Text components (hiện máu, tên, damage...)
        /// Giúp reverse engineer UI game
        /// </summary>
        private void FindAllUIText()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== UI TEXT SCAN ===");
                sb.AppendLine($"Time: {DateTime.Now}");
                sb.AppendLine();

                // Tìm UnityEngine.UI.Text
                var texts = GameObject.FindObjectsOfType<UnityEngine.UI.Text>();
                sb.AppendLine($"--- UnityEngine.UI.Text: {texts.Length} ---");
                foreach (var t in texts)
                {
                    if (t == null) continue;
                    var textContent = t.text ?? "(null)";
                    if (textContent.Length > 100)
                        textContent = textContent.Substring(0, 100) + "...";
                    sb.AppendLine($"  [{t.gameObject.name}] \"{textContent}\"");
                    // Liệt kê parent chain để biết thuộc panel nào
                    var parent = t.transform.parent;
                    var chain = "";
                    int maxDepth = 4;
                    while (parent != null && maxDepth-- > 0)
                    {
                        chain = parent.name + "/" + chain;
                        parent = parent.parent;
                    }
                    if (!string.IsNullOrEmpty(chain))
                        sb.AppendLine($"    Path: {chain}");
                }

                var filepath = Path.Combine(_logDir, $"ui_text_{DateTime.Now:HHmmss}.txt");
                File.WriteAllText(filepath, sb.ToString());

                Plugin.Log.LogInfo($"[Inspector] Tìm thấy {texts.Length} UI.Text");
                Plugin.Log.LogInfo($"[Inspector] File: {filepath}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Inspector] Lỗi tìm UI Text: {ex.Message}");
            }
        }

        // ============ DUMP BUTTON HANDLERS ============

        /// <summary>
        /// F6: Tìm tất cả Button và xem onClick gọi hàm gì
        /// Đây là cách tìm hàm "Nhận Quest", "Tấn Công", v.v.
        /// </summary>
        private void DumpButtonHandlers()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== BUTTON HANDLER SCAN ===");
                sb.AppendLine($"Time: {DateTime.Now}");
                sb.AppendLine();

                var buttons = GameObject.FindObjectsOfType<UnityEngine.UI.Button>();
                sb.AppendLine($"Tổng Button: {buttons.Length}");
                sb.AppendLine();

                foreach (var btn in buttons)
                {
                    if (btn == null) continue;
                    sb.AppendLine($"[Button] {btn.gameObject.name} (active={btn.gameObject.activeSelf})");

                    // Parent chain
                    var parent = btn.transform.parent;
                    var chain = "";
                    int maxDepth = 4;
                    while (parent != null && maxDepth-- > 0)
                    {
                        chain = parent.name + "/" + chain;
                        parent = parent.parent;
                    }
                    sb.AppendLine($"  Path: {chain}");

                    // OnClick listener count
                    var onclick = btn.onClick;
                    if (onclick != null)
                    {
                        var count = onclick.GetPersistentEventCount();
                        sb.AppendLine($"  OnClick Persistent Listeners: {count}");
                        for (int i = 0; i < count; i++)
                        {
                            var targetObj = onclick.GetPersistentTarget(i);
                            var methodName = onclick.GetPersistentMethodName(i);
                            var targetType = targetObj != null ? targetObj.GetIl2CppType().FullName : "null";
                            sb.AppendLine($"    [{i}] {targetType}.{methodName}()");
                        }
                    }

                    // Tìm Text con (label của button)
                    var childText = btn.GetComponentInChildren<UnityEngine.UI.Text>();
                    if (childText != null && !string.IsNullOrEmpty(childText.text))
                    {
                        sb.AppendLine($"  Label: \"{childText.text}\"");
                    }
                    sb.AppendLine();
                }

                var filepath = Path.Combine(_logDir, $"buttons_{DateTime.Now:HHmmss}.txt");
                File.WriteAllText(filepath, sb.ToString());

                Plugin.Log.LogInfo($"[Inspector] Tìm thấy {buttons.Length} Button");
                Plugin.Log.LogInfo($"[Inspector] File: {filepath}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Inspector] Lỗi dump buttons: {ex.Message}");
            }
        }

        // ============ TÌM SINGLETON / MANAGER ============

        /// <summary>
        /// F7: Tìm các class kiểu "Manager" (singleton pattern)
        /// Game thường có: QuestManager, BattleManager, PlayerManager...
        /// Dù tên obfuscate, pattern singleton vẫn nhận ra được
        /// </summary>
        private void FindSingletonManagers()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== SINGLETON/MANAGER SCAN ===");
                sb.AppendLine($"Time: {DateTime.Now}");
                sb.AppendLine();
                sb.AppendLine("Tìm MonoBehaviour chỉ có 1 instance (Singleton pattern):");
                sb.AppendLine();

                var allMB = GameObject.FindObjectsOfType<MonoBehaviour>();
                var typeCounts = new Dictionary<string, List<MonoBehaviour>>();

                foreach (var mb in allMB)
                {
                    if (mb == null) continue;
                    var typeName = mb.GetIl2CppType().FullName;
                    if (!typeCounts.ContainsKey(typeName))
                        typeCounts[typeName] = new List<MonoBehaviour>();
                    typeCounts[typeName].Add(mb);
                }

                // Singleton = chỉ có 1 instance → khả năng cao là Manager
                sb.AppendLine("--- SINGLETON (1 instance) - Khả năng cao là Manager ---");
                int index = 0;
                foreach (var kvp in typeCounts.Where(x => x.Value.Count == 1).OrderBy(x => x.Key))
                {
                    var mb = kvp.Value[0];
                    var go = mb.gameObject;
                    
                    // Đếm số children (Manager thường có ít/không có children visible)
                    int childCount = go.transform.childCount;
                    
                    // Kiểm tra có phải root object không
                    bool isRoot = go.transform.parent == null;
                    
                    sb.AppendLine($"  [{++index}] {kvp.Key}");
                    sb.AppendLine($"       GameObject: {go.name}");
                    sb.AppendLine($"       IsRoot: {isRoot}, Children: {childCount}");
                    
                    // Liệt kê tất cả component trên cùng GameObject
                    var comps = go.GetComponents<Component>();
                    foreach (var comp in comps)
                    {
                        if (comp == null) continue;
                        var compType = comp.GetIl2CppType().FullName;
                        if (compType != kvp.Key) // Không lặp lại chính nó
                            sb.AppendLine($"       + OtherComp: {compType}");
                    }
                    sb.AppendLine();
                }

                var filepath = Path.Combine(_logDir, $"singletons_{DateTime.Now:HHmmss}.txt");
                File.WriteAllText(filepath, sb.ToString());

                var singletonCount = typeCounts.Count(x => x.Value.Count == 1);
                Plugin.Log.LogInfo($"[Inspector] Tìm thấy {singletonCount} Singleton patterns");
                Plugin.Log.LogInfo($"[Inspector] File: {filepath}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Inspector] Lỗi tìm singletons: {ex.Message}");
            }
        }

        // ============ TÌM NETWORK CLASSES ============

        /// <summary>
        /// F8: Tìm class liên quan đến network/socket
        /// Game NRO dùng socket để giao tiếp server
        /// </summary>
        private void FindNetworkClasses()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== NETWORK/SESSION SCAN ===");
                sb.AppendLine($"Time: {DateTime.Now}");
                sb.AppendLine();

                // Tìm tất cả type đang được sử dụng
                var allMB = GameObject.FindObjectsOfType<MonoBehaviour>();
                
                sb.AppendLine("--- Tìm class có chứa từ khóa liên quan network ---");
                foreach (var mb in allMB)
                {
                    if (mb == null) continue;
                    var il2cppType = mb.GetIl2CppType();
                    var typeName = il2cppType.FullName;
                    
                    // Kiểm tra methods
                    var methods = il2cppType.GetMethods();
                    bool hasNetworkHints = false;
                    var methodList = new List<string>();

                    foreach (var method in methods)
                    {
                        var mName = method.Name;
                        // Tìm method nhận sbyte, byte[] params (dấu hiệu network message)
                        var parms = method.GetParameters();
                        foreach (var p in parms)
                        {
                            var pType = p.ParameterType.Name;
                            if (pType == "SByte" || pType == "Byte[]" || pType == "DataOutputStream" || 
                                pType == "DataInputStream" || pType.Contains("Stream") ||
                                pType.Contains("Socket") || pType.Contains("Message"))
                            {
                                hasNetworkHints = true;
                                methodList.Add($"{mName}({pType} {p.Name})");
                            }
                        }
                    }

                    if (hasNetworkHints)
                    {
                        sb.AppendLine($"\n[NETWORK?] {typeName}");
                        sb.AppendLine($"  GameObject: {mb.gameObject.name}");
                        foreach (var m in methodList.Take(20))
                        {
                            sb.AppendLine($"  → {m}");
                        }
                    }
                }

                // Tìm class có field kiểu Socket/Stream
                sb.AppendLine("\n--- Tìm class có field Socket/Stream ---");
                foreach (var mb in allMB)
                {
                    if (mb == null) continue;
                    var il2cppType = mb.GetIl2CppType();
                    var fields = il2cppType.GetFields(
                        Il2CppSystem.Reflection.BindingFlags.Instance | 
                        Il2CppSystem.Reflection.BindingFlags.NonPublic | 
                        Il2CppSystem.Reflection.BindingFlags.Public
                    );
                    
                    foreach (var f in fields)
                    {
                        var fType = f.FieldType.Name;
                        if (fType.Contains("Socket") || fType.Contains("Stream") || 
                            fType.Contains("TcpClient") || fType.Contains("WebSocket") ||
                            fType.Contains("NetworkStream"))
                        {
                            sb.AppendLine($"  {il2cppType.FullName}.{f.Name} : {fType}");
                        }
                    }
                }

                var filepath = Path.Combine(_logDir, $"network_{DateTime.Now:HHmmss}.txt");
                File.WriteAllText(filepath, sb.ToString());

                Plugin.Log.LogInfo($"[Inspector] Network scan hoàn tất");
                Plugin.Log.LogInfo($"[Inspector] File: {filepath}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Inspector] Lỗi network scan: {ex.Message}");
            }
        }
    }
}
