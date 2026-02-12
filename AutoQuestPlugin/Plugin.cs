using System;
using System.IO;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace AutoQuestPlugin
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class Plugin : BasePlugin
    {
        public const string PLUGIN_GUID = "com.autoquest.vtdc";
        public const string PLUGIN_NAME = "AutoQuest VuTruDaiChien";
        public const string PLUGIN_VERSION = "1.0.0";

        internal static new ManualLogSource Log;

        // Config
        private static string ConfigPath => Path.Combine(
            Path.GetDirectoryName(typeof(Plugin).Assembly.Location), 
            "bot_config.txt"
        );

        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo($"==============================");
            Log.LogInfo($"  {PLUGIN_NAME} v{PLUGIN_VERSION}");
            Log.LogInfo($"  Bot đã tiêm thành công!");
            Log.LogInfo($"==============================");

            // Đọc config
            LoadConfig();

            // Thêm BotController component
            AddComponent<BotController>();

            // Thêm RuntimeInspector - quét game objects khi đang chạy
            AddComponent<RuntimeInspector>();

            // Thêm DeepScanner - deep scan class methods/fields
            AddComponent<DeepScanner>();

            Log.LogInfo("[Bot] Plugin loaded! BotController + RuntimeInspector active.");
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var lines = File.ReadAllLines(ConfigPath);
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line))
                            continue;
                        
                        var parts = line.Split('=');
                        if (parts.Length == 2)
                        {
                            var key = parts[0].Trim();
                            var value = parts[1].Trim();
                            Log.LogInfo($"[Config] {key} = {value}");
                        }
                    }
                }
                else
                {
                    Log.LogWarning($"[Config] Không tìm thấy config: {ConfigPath}");
                    CreateDefaultConfig();
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"[Config] Lỗi đọc config: {ex.Message}");
            }
        }

        private void CreateDefaultConfig()
        {
            var defaultConfig = @"# AutoQuest Bot Config
# Cấu hình bot tự động

# Chế độ headless (tắt đồ họa để tiết kiệm tài nguyên)
headless_mode = true

# Giới hạn FPS (càng thấp càng nhẹ máy)
target_fps = 60

# Auto quest
auto_quest = true

# Auto battle (tự động chiến đấu)
auto_battle = true

# Random delay giữa action (giây) - anti-ban
min_delay = 0.5
max_delay = 2.0

# Thời gian chạy tối đa (giờ) - 0 = không giới hạn
max_runtime_hours = 8

# Device ID giả (mỗi acc 1 ID khác nhau) - anti-ban
spoof_device_id = true
";
            try
            {
                File.WriteAllText(ConfigPath, defaultConfig);
                Log.LogInfo($"[Config] Đã tạo config mặc định: {ConfigPath}");
            }
            catch (Exception ex)
            {
                Log.LogError($"[Config] Lỗi tạo config: {ex.Message}");
            }
        }
    }
}
