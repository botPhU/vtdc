using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Reflection;

namespace AutoQuestPlugin
{
    /// <summary>
    /// DeepScanner — Deep scan game classes bằng IL2CPP reflection.
    /// Dump toàn bộ methods, properties, fields của class cụ thể.
    /// 
    /// Hotkeys:
    ///   F9  = Deep scan AutoMissionManager
    ///   F10 = Deep scan PlayerDataManager  
    ///   Numpad0 = Deep scan AutoAttackButton / InteractButton / ShortMissionPanel (cycle)
    ///   Numpad1 = Scan tất cả Managers trong scene
    /// </summary>
    public class DeepScanner : MonoBehaviour
    {
        public DeepScanner(IntPtr ptr) : base(ptr) { }

        private string _logDir;
        private int _cycleIndex = 0;
        private string[] _cycleTargets = new string[] 
        { 
            "AutoAttackButton", "InteractButton", "ShortMissionPanel",
            "ActionButtonController", "SkillButton", "MainGame",
            "MainGameUIManager", "UIManager", "NpcStateService",
            "AutoAttackBlackBoardComponent", "GameManager", "GodDragonManager",
            "ChatManager", "GuideManager", "MobService"
        };

        public void Start()
        {
            _logDir = Path.Combine(
                Path.GetDirectoryName(typeof(Plugin).Assembly.Location),
                "inspector_logs"
            );
            Directory.CreateDirectory(_logDir);

            Plugin.Log.LogInfo("============================================");
            Plugin.Log.LogInfo("  [DeepScanner] KHỞI ĐỘNG!");
            Plugin.Log.LogInfo("  F9     = Deep scan AutoMissionManager");
            Plugin.Log.LogInfo("  F10    = Deep scan PlayerDataManager");
            Plugin.Log.LogInfo("  Num0   = Cycle scan other classes");
            Plugin.Log.LogInfo("  Num1   = Scan ALL managers in scene");
            Plugin.Log.LogInfo("============================================");
        }

        public void Update()
        {
            if (Input.GetKeyDown(KeyCode.F9))
            {
                DeepScanByName("AutoMissionManager");
            }

            if (Input.GetKeyDown(KeyCode.F10))
            {
                DeepScanByName("PlayerDataManager");
            }

            if (Input.GetKeyDown(KeyCode.Keypad0))
            {
                string target = _cycleTargets[_cycleIndex % _cycleTargets.Length];
                _cycleIndex++;
                DeepScanByName(target);
            }

            if (Input.GetKeyDown(KeyCode.Keypad1))
            {
                ScanAllManagers();
            }
        }

        // ======================== DEEP SCAN ========================

        /// <summary>
        /// Deep scan a specific MonoBehaviour type by name.
        /// Dumps all methods, properties, fields.
        /// </summary>
        private void DeepScanByName(string typeName)
        {
            Plugin.Log.LogInfo($"[DeepScan] Đang quét: {typeName}...");

            try
            {
                // Find the component in scene
                MonoBehaviour target = null;
                var allMB = GameObject.FindObjectsOfType<MonoBehaviour>();
                foreach (var mb in allMB)
                {
                    if (mb == null) continue;
                    if (mb.GetIl2CppType().Name == typeName)
                    {
                        target = mb;
                        break;
                    }
                }

                if (target == null)
                {
                    Plugin.Log.LogWarning($"[DeepScan] ❌ Không tìm thấy '{typeName}' trong scene hiện tại!");
                    return;
                }

                var il2cppType = target.GetIl2CppType();
                var sb = new StringBuilder();
                var timestamp = DateTime.Now.ToString("HHmmss");

                sb.AppendLine($"=== DEEP SCAN: {typeName} ===");
                sb.AppendLine($"Time: {DateTime.Now}");
                sb.AppendLine($"Scene: {SceneManager.GetActiveScene().name}");
                sb.AppendLine($"GameObject: {target.gameObject.name}");
                sb.AppendLine($"Path: {GetGameObjectPath(target.gameObject)}");
                sb.AppendLine($"Type: {il2cppType.FullName}");
                sb.AppendLine($"BaseType: {il2cppType.BaseType?.FullName ?? "N/A"}");
                sb.AppendLine($"Active: {target.gameObject.activeSelf}");
                sb.AppendLine($"Enabled: {target.enabled}");
                sb.AppendLine();

                // === METHODS ===
                try
                {
                    var methods = il2cppType.GetMethods(
                        Il2CppSystem.Reflection.BindingFlags.Public |
                        Il2CppSystem.Reflection.BindingFlags.NonPublic |
                        Il2CppSystem.Reflection.BindingFlags.Instance |
                        Il2CppSystem.Reflection.BindingFlags.Static |
                        Il2CppSystem.Reflection.BindingFlags.DeclaredOnly
                    );

                    sb.AppendLine($"--- METHODS ({methods.Length}) ---");
                    int methodIdx = 0;
                    foreach (var m in methods)
                    {
                        if (m == null) continue;
                        methodIdx++;
                        try
                        {
                            string returnType = m.ReturnType?.Name ?? "void";
                            string access = m.IsPublic ? "public" : "non-public";
                            string isStatic = m.IsStatic ? " static" : "";

                            // Get parameters
                            var paramSb = new StringBuilder();
                            try
                            {
                                var parameters = m.GetParameters();
                                for (int i = 0; i < parameters.Length; i++)
                                {
                                    var p = parameters[i];
                                    if (i > 0) paramSb.Append(", ");
                                    paramSb.Append($"{p.ParameterType?.Name ?? "?"} {p.Name}");
                                }
                            }
                            catch { paramSb.Append("?"); }

                            sb.AppendLine($"  [{methodIdx}] {access}{isStatic} {returnType} {m.Name}({paramSb})");
                        }
                        catch (Exception ex)
                        {
                            sb.AppendLine($"  [{methodIdx}] ERROR: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"--- METHODS: ERROR: {ex.Message} ---");
                }
                sb.AppendLine();

                // === PROPERTIES ===
                try
                {
                    var properties = il2cppType.GetProperties(
                        Il2CppSystem.Reflection.BindingFlags.Public |
                        Il2CppSystem.Reflection.BindingFlags.NonPublic |
                        Il2CppSystem.Reflection.BindingFlags.Instance |
                        Il2CppSystem.Reflection.BindingFlags.Static |
                        Il2CppSystem.Reflection.BindingFlags.DeclaredOnly
                    );

                    sb.AppendLine($"--- PROPERTIES ({properties.Length}) ---");
                    int propIdx = 0;
                    foreach (var p in properties)
                    {
                        if (p == null) continue;
                        propIdx++;
                        try
                        {
                            string propType = p.PropertyType?.Name ?? "?";
                            bool canRead = p.CanRead;
                            bool canWrite = p.CanWrite;
                            string access = canRead && canWrite ? "get/set" : (canRead ? "get" : "set");

                            string value = "N/A";
                            if (canRead)
                            {
                                try
                                {
                                    var getter = p.GetGetMethod();
                                    if (getter != null && !getter.IsStatic && getter.GetParameters().Length == 0)
                                    {
                                        var val = getter.Invoke(target, null);
                                        value = val?.ToString() ?? "null";
                                        // Truncate long values
                                        if (value.Length > 100) value = value.Substring(0, 100) + "...";
                                    }
                                }
                                catch (Exception ex) { value = $"ERR:{ex.Message}"; }
                            }

                            sb.AppendLine($"  [{propIdx}] {propType} {p.Name} ({access}) = {value}");
                        }
                        catch (Exception ex)
                        {
                            sb.AppendLine($"  [{propIdx}] ERROR: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"--- PROPERTIES: ERROR: {ex.Message} ---");
                }
                sb.AppendLine();

                // === FIELDS ===
                try
                {
                    var fields = il2cppType.GetFields(
                        Il2CppSystem.Reflection.BindingFlags.Public |
                        Il2CppSystem.Reflection.BindingFlags.NonPublic |
                        Il2CppSystem.Reflection.BindingFlags.Instance |
                        Il2CppSystem.Reflection.BindingFlags.Static |
                        Il2CppSystem.Reflection.BindingFlags.DeclaredOnly
                    );

                    sb.AppendLine($"--- FIELDS ({fields.Length}) ---");
                    int fieldIdx = 0;
                    foreach (var f in fields)
                    {
                        if (f == null) continue;
                        fieldIdx++;
                        try
                        {
                            string fieldType = f.FieldType?.Name ?? "?";
                            string access = f.IsPublic ? "public" : "non-public";
                            string isStatic = f.IsStatic ? " static" : "";

                            string value = "N/A";
                            try
                            {
                                var val = f.GetValue(f.IsStatic ? null : target);
                                value = val?.ToString() ?? "null";
                                if (value.Length > 100) value = value.Substring(0, 100) + "...";
                            }
                            catch (Exception ex) { value = $"ERR:{ex.Message}"; }

                            sb.AppendLine($"  [{fieldIdx}] {access}{isStatic} {fieldType} {f.Name} = {value}");
                        }
                        catch (Exception ex)
                        {
                            sb.AppendLine($"  [{fieldIdx}] ERROR: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"--- FIELDS: ERROR: {ex.Message} ---");
                }
                sb.AppendLine();

                // === CHILD GAMEOBJECTS (only first level) ===
                sb.AppendLine($"--- CHILD GAMEOBJECTS ---");
                for (int i = 0; i < target.transform.childCount; i++)
                {
                    var child = target.transform.GetChild(i);
                    sb.Append($"  [{i}] {child.name} (active={child.gameObject.activeSelf})");
                    // List components on each child
                    var childComps = child.GetComponents<Component>();
                    foreach (var c in childComps)
                    {
                        if (c == null) continue;
                        var cName = c.GetIl2CppType().Name;
                        if (cName != "Transform" && cName != "RectTransform")
                            sb.Append($" → {cName}");
                    }
                    sb.AppendLine();
                }
                sb.AppendLine();

                // === SIBLING COMPONENTS ON SAME GAMEOBJECT ===
                sb.AppendLine($"--- SIBLING COMPONENTS (on {target.gameObject.name}) ---");
                var siblings = target.gameObject.GetComponents<Component>();
                foreach (var s in siblings)
                {
                    if (s == null) continue;
                    sb.AppendLine($"  → {s.GetIl2CppType().FullName}");
                }

                // Write to file
                string fileName = $"deep_{typeName}_{timestamp}.txt";
                string filePath = Path.Combine(_logDir, fileName);
                File.WriteAllText(filePath, sb.ToString());

                Plugin.Log.LogInfo($"[DeepScan] ✅ {typeName}: {methodIdx(sb)} | Saved: {fileName}");
                Plugin.Log.LogInfo($"[DeepScan] File: {filePath}");

                // Also log summary to console
                LogSummaryToConsole(typeName, sb.ToString());
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[DeepScan] ❌ Error scanning {typeName}: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void LogSummaryToConsole(string typeName, string fullContent)
        {
            // Log first interesting methods to console for quick reference
            var lines = fullContent.Split('\n');
            int count = 0;
            foreach (var line in lines)
            {
                if (line.TrimStart().StartsWith("[") && count < 20)
                {
                    Plugin.Log.LogInfo($"  {line.Trim()}");
                    count++;
                }
            }
            if (count >= 20)
                Plugin.Log.LogInfo($"  ... và thêm nữa (xem file log)");
        }

        // ======================== SCAN ALL MANAGERS ========================

        private void ScanAllManagers()
        {
            Plugin.Log.LogInfo("[DeepScan] === SCANNING ALL MANAGERS ===");

            string[] managerNames = new string[]
            {
                "AutoMissionManager", "PlayerDataManager", "UIManager",
                "GameManager", "MainGame", "MainGameUIManager",
                "AutoAttackBlackBoardComponent", "NpcStateService",
                "SpaceShipManager", "GodDragonManager", "InvitationManager",
                "ChatManager", "GuideManager", "DropEffectMapManager",
                "ActivityGameNotifyManager", "DependGameStateManager",
                "MobService", "AppManager", "SocketComponent",
                "MessageProcessManager"
            };

            var sb = new StringBuilder();
            sb.AppendLine("=== ALL MANAGERS SCAN ===");
            sb.AppendLine($"Time: {DateTime.Now}");
            sb.AppendLine($"Scene: {SceneManager.GetActiveScene().name}");
            sb.AppendLine();

            var allMB = GameObject.FindObjectsOfType<MonoBehaviour>();
            int found = 0;

            foreach (string name in managerNames)
            {
                MonoBehaviour target = null;
                foreach (var mb in allMB)
                {
                    if (mb == null) continue;
                    if (mb.GetIl2CppType().Name == name)
                    {
                        target = mb;
                        break;
                    }
                }

                if (target != null)
                {
                    found++;
                    var il2cppType = target.GetIl2CppType();
                    
                    int methodCount = 0;
                    int propCount = 0;
                    int fieldCount = 0;
                    
                    try { methodCount = il2cppType.GetMethods(Il2CppSystem.Reflection.BindingFlags.Public | Il2CppSystem.Reflection.BindingFlags.NonPublic | Il2CppSystem.Reflection.BindingFlags.Instance | Il2CppSystem.Reflection.BindingFlags.DeclaredOnly).Length; } catch { }
                    try { propCount = il2cppType.GetProperties(Il2CppSystem.Reflection.BindingFlags.Public | Il2CppSystem.Reflection.BindingFlags.NonPublic | Il2CppSystem.Reflection.BindingFlags.Instance | Il2CppSystem.Reflection.BindingFlags.DeclaredOnly).Length; } catch { }
                    try { fieldCount = il2cppType.GetFields(Il2CppSystem.Reflection.BindingFlags.Public | Il2CppSystem.Reflection.BindingFlags.NonPublic | Il2CppSystem.Reflection.BindingFlags.Instance | Il2CppSystem.Reflection.BindingFlags.DeclaredOnly).Length; } catch { }

                    string line = $"  ✅ {name} on:{target.gameObject.name} | M:{methodCount} P:{propCount} F:{fieldCount}";
                    sb.AppendLine(line);
                    Plugin.Log.LogInfo(line);

                    // List key methods
                    try
                    {
                        var methods = il2cppType.GetMethods(
                            Il2CppSystem.Reflection.BindingFlags.Public |
                            Il2CppSystem.Reflection.BindingFlags.Instance |
                            Il2CppSystem.Reflection.BindingFlags.DeclaredOnly
                        );
                        foreach (var m in methods)
                        {
                            if (m == null) continue;
                            string mName = m.Name;
                            // Skip Unity built-in methods
                            if (mName == "Update" || mName == "Start" || mName == "Awake" ||
                                mName == "OnEnable" || mName == "OnDisable" || mName == "OnDestroy" ||
                                mName.StartsWith("get_") || mName.StartsWith("set_"))
                                continue;
                            sb.AppendLine($"       → {m.ReturnType?.Name ?? "void"} {mName}()");
                        }
                    }
                    catch { }
                }
                else
                {
                    sb.AppendLine($"  ❌ {name}");
                }
            }

            sb.AppendLine();
            sb.AppendLine($"Found: {found}/{managerNames.Length}");

            string fileName = $"all_managers_{DateTime.Now:HHmmss}.txt";
            string filePath = Path.Combine(_logDir, fileName);
            File.WriteAllText(filePath, sb.ToString());
            Plugin.Log.LogInfo($"[DeepScan] ✅ Saved: {fileName} ({found} managers found)");
        }

        // ======================== HELPERS ========================

        private string GetGameObjectPath(GameObject go)
        {
            string path = go.name;
            var parent = go.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }

        // Helper to count items from scan result string
        private string methodIdx(StringBuilder sb)
        {
            string content = sb.ToString();
            int mCount = 0, pCount = 0, fCount = 0;
            
            var lines = content.Split('\n');
            string section = "";
            foreach (var line in lines)
            {
                if (line.Contains("--- METHODS")) section = "M";
                else if (line.Contains("--- PROPERTIES")) section = "P";
                else if (line.Contains("--- FIELDS")) section = "F";
                else if (line.Contains("--- CHILD")) section = "C";
                
                if (line.TrimStart().StartsWith("[") && section != "C")
                {
                    if (section == "M") mCount++;
                    else if (section == "P") pCount++;
                    else if (section == "F") fCount++;
                }
            }
            return $"M:{mCount} P:{pCount} F:{fCount}";
        }
    }
}
