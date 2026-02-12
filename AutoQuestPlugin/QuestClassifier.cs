using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace AutoQuestPlugin
{
    // ======================== QUEST TYPE ENUM ========================
    public enum QuestType
    {
        UNKNOWN,    // Chưa phân loại được
        MOVE,       // Di chuyển (Băng qua, Tiến đến)
        TALK,       // Nói chuyện NPC (Gặp, Báo cáo, Dắt...về, Giao...cho)
        KILL,       // Tiêu diệt mob (Tiêu diệt X (0/N))
        USE_ITEM,   // Sử dụng item (Dùng, Nhận)
        COLLECT,    // Thu thập (Tìm lại)
        JOIN,       // Gia nhập (Gia nhập Bang Hội)
    }

    // ======================== QUEST ACTION ENUM ========================
    public enum QuestAction
    {
        NONE,
        CLICK_SHORTMISSION,      // Click ShortMissionPanel (auto-pathfind)
        PATHFIND_AND_TALK_NPC,   // Pathfind → NPC dialog → click quest button
        PRESS_MOVEMENT_KEYS,     // Bấm phím mũi tên di chuyển (tutorial quest)
        PRESS_SKILL_KEY,         // Bấm phím kỹ năng (skill 1, 2, 3...)
        WAIT_KILL,               // Chờ user/auto kill
        WAIT_USER,               // Chờ user thao tác
        CLICK_ITEM,              // Click item UI
    }

    // ======================== QUEST INFO ========================
    public class QuestInfo
    {
        public QuestType Type { get; set; } = QuestType.UNKNOWN;
        public QuestAction Action { get; set; } = QuestAction.NONE;
        public string QuestText { get; set; } = "";
        public string Target { get; set; } = "";     // tên mob, NPC, item
        public int RequiredCount { get; set; } = 0;   // số lượng cần (kill quest)
        public int CurrentCount { get; set; } = 0;    // số lượng hiện tại
        public float Confidence { get; set; } = 0f;   // 0-1, độ tin cậy phân loại
        public string MatchedKeyword { get; set; } = "";

        public override string ToString()
        {
            string countStr = RequiredCount > 0 ? $" ({CurrentCount}/{RequiredCount})" : "";
            return $"[{Type}] {QuestText}{countStr} → {Action} (conf={Confidence:F2}, kw={MatchedKeyword})";
        }
    }

    // ======================== PATTERN ENTRY (learned from logs) ========================
    public class QuestPattern
    {
        public string Keyword { get; set; } = "";
        public string Type { get; set; } = "UNKNOWN";
        public List<string> Actions { get; set; } = new List<string>();
        public float SuccessRate { get; set; } = 0f;
        public int SeenCount { get; set; } = 0;
    }

    public class PatternDatabase
    {
        public List<QuestPattern> Patterns { get; set; } = new List<QuestPattern>();
        public int TotalSessions { get; set; } = 0;
        public string LastUpdated { get; set; } = "";
    }

    // ======================== QUEST CLASSIFIER ========================
    public class QuestClassifier
    {
        // Keyword → QuestType mapping (ưu tiên từ trên xuống)
        private static readonly List<(string keyword, QuestType type, QuestAction action, float confidence)> _rules = new List<(string, QuestType, QuestAction, float)>
        {
            // KILL quests (highest priority — có số/số)
            ("Tiêu diệt",   QuestType.KILL,     QuestAction.WAIT_KILL,               0.95f),
            
            // MOVE quests
            // Normal move (ShortMission pathfind)
            ("Băng qua",     QuestType.MOVE,     QuestAction.CLICK_SHORTMISSION,      0.95f),
            ("Tiến đến",     QuestType.MOVE,     QuestAction.CLICK_SHORTMISSION,      0.95f),
            ("Tìm",          QuestType.MOVE,     QuestAction.CLICK_SHORTMISSION,      0.90f),
            ("Di chuyển",    QuestType.MOVE,     QuestAction.CLICK_SHORTMISSION,      0.90f), // Di chuyển theo hướng... (Auto)
            
            // Tutorial move (Key press mandatory)
            ("Làm quen",     QuestType.MOVE,     QuestAction.PRESS_MOVEMENT_KEYS,     0.95f),
            
            // TALK quests (NPC interaction)
            ("Báo cáo với",  QuestType.TALK,     QuestAction.PATHFIND_AND_TALK_NPC,   0.90f),
            ("Gặp",          QuestType.TALK,     QuestAction.PATHFIND_AND_TALK_NPC,   0.85f),
            ("Dắt",          QuestType.TALK,     QuestAction.PATHFIND_AND_TALK_NPC,   0.85f),
            ("Giao",         QuestType.TALK,     QuestAction.PATHFIND_AND_TALK_NPC,   0.85f),
            ("Nhận",         QuestType.TALK,     QuestAction.PATHFIND_AND_TALK_NPC,   0.80f),
            
            // USE_ITEM quests
            ("Dùng Thẻ",     QuestType.USE_ITEM, QuestAction.PRESS_SKILL_KEY,         0.90f),
            ("Dùng",         QuestType.USE_ITEM, QuestAction.PRESS_SKILL_KEY,         0.80f),
            
            // COLLECT quests
            ("Tìm lại",      QuestType.COLLECT,  QuestAction.CLICK_SHORTMISSION,      0.80f),
            ("Thu thập",     QuestType.COLLECT,  QuestAction.CLICK_SHORTMISSION,      0.80f),
            
            // JOIN quests
            ("Gia nhập",     QuestType.JOIN,     QuestAction.WAIT_USER,               0.75f),
        };

        // Regex để parse kill count: (0/4), (<color=#FF0000>0</color>/4)
        private static readonly Regex _killCountRegex = new Regex(
            @"\((?:<color=[^>]*>)?(\d+)(?:</color>)?/(\d+)\)",
            RegexOptions.Compiled);

        // Learned patterns từ file
        private PatternDatabase _learnedPatterns = new PatternDatabase();
        private string _patternsFilePath;

        // Quest completion tracking
        private string _lastQuestText = "";
        private QuestInfo _lastQuestInfo = null;
        private DateTime _lastQuestStartTime = DateTime.Now;
        private List<string> _lastQuestActions = new List<string>();

        // Stats
        public int TotalClassified { get; private set; } = 0;
        public int TotalSuccessful { get; private set; } = 0;

        // ======================== CONSTRUCTOR ========================
        public QuestClassifier(string pluginDir)
        {
            _patternsFilePath = Path.Combine(pluginDir, "quest_patterns.json");
            LoadPatterns();
        }

        // ======================== CLASSIFY ========================
        /// <summary>
        /// Phân loại quest từ text. Trả về QuestInfo với type, action, confidence.
        /// </summary>
        public QuestInfo Classify(string questText)
        {
            if (string.IsNullOrWhiteSpace(questText))
                return new QuestInfo { Type = QuestType.UNKNOWN, Action = QuestAction.NONE };

            // Clean quest text (remove color tags)
            string cleanText = Regex.Replace(questText, @"<[^>]+>", "");

            var info = new QuestInfo { QuestText = cleanText };

            // 1. Check kill count regex first
            var killMatch = _killCountRegex.Match(questText);
            if (killMatch.Success)
            {
                info.CurrentCount = int.Parse(killMatch.Groups[1].Value);
                info.RequiredCount = int.Parse(killMatch.Groups[2].Value);
            }

            // 2. Match keywords (priority order — first match wins)
            foreach (var rule in _rules)
            {
                if (cleanText.Contains(rule.keyword))
                {
                    info.Type = rule.type;
                    info.Action = rule.action;
                    info.Confidence = rule.confidence;
                    info.MatchedKeyword = rule.keyword;

                    // Extract target name
                    info.Target = ExtractTarget(cleanText, rule.keyword, info.Type);
                    break;
                }
            }

            // 3. Check learned patterns (may boost confidence or classify UNKNOWN)
            if (info.Type == QuestType.UNKNOWN)
            {
                var learned = FindLearnedPattern(cleanText);
                if (learned != null)
                {
                    info.Type = ParseQuestType(learned.Type);
                    info.Action = GetDefaultAction(info.Type);
                    info.Confidence = Math.Min(learned.SuccessRate, 0.70f); // Cap learned confidence
                    info.MatchedKeyword = $"[learned] {learned.Keyword}";
                }
            }

            // 4. Default fallback for unclassified → try ShortMissionPanel
            if (info.Type == QuestType.UNKNOWN)
            {
                info.Action = QuestAction.CLICK_SHORTMISSION;
                info.Confidence = 0.30f;
                info.MatchedKeyword = "[fallback]";
            }

            TotalClassified++;
            return info;
        }

        // ======================== TRACK QUEST COMPLETION ========================
        /// <summary>
        /// Gọi mỗi khi quest text thay đổi. Ghi nhận quest cũ đã hoàn thành.
        /// </summary>
        public void OnQuestChanged(string newQuestText, string actionsTaken)
        {
            if (!string.IsNullOrEmpty(_lastQuestText) && _lastQuestText != newQuestText)
            {
                // Quest cũ đã hoàn thành (quest text thay đổi = success)
                if (_lastQuestInfo != null)
                {
                    TotalSuccessful++;
                    UpdateLearnedPattern(_lastQuestInfo, true, actionsTaken);
                }
            }

            _lastQuestText = newQuestText;
            _lastQuestInfo = Classify(newQuestText);
            _lastQuestStartTime = DateTime.Now;
            _lastQuestActions.Clear();
        }

        /// <summary>
        /// Ghi lại action đã thực hiện cho quest hiện tại
        /// </summary>
        public void RecordAction(string action)
        {
            _lastQuestActions.Add(action);
        }

        // ======================== PATTERN LEARNING ========================
        private void UpdateLearnedPattern(QuestInfo info, bool success, string actionsTaken)
        {
            if (string.IsNullOrEmpty(info.MatchedKeyword) || info.MatchedKeyword.StartsWith("["))
                return; // Skip fallback/learned patterns

            var existing = _learnedPatterns.Patterns
                .FirstOrDefault(p => p.Keyword == info.MatchedKeyword);

            if (existing != null)
            {
                existing.SeenCount++;
                // Rolling average success rate
                existing.SuccessRate = (existing.SuccessRate * (existing.SeenCount - 1) + (success ? 1f : 0f)) / existing.SeenCount;
            }
            else
            {
                _learnedPatterns.Patterns.Add(new QuestPattern
                {
                    Keyword = info.MatchedKeyword,
                    Type = info.Type.ToString(),
                    Actions = actionsTaken.Split(',').Select(a => a.Trim()).ToList(),
                    SuccessRate = success ? 1f : 0f,
                    SeenCount = 1
                });
            }

            _learnedPatterns.LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            _learnedPatterns.TotalSessions++;
            SavePatterns();
        }

        // ======================== LEARN FROM LOG FILE ========================
        /// <summary>
        /// Parse một session log file và trích xuất quest patterns.
        /// Gọi khi plugin khởi động để học từ dữ liệu cũ.
        /// </summary>
        public int LearnFromLogFile(string logPath)
        {
            if (!File.Exists(logPath)) return 0;

            int patternsLearned = 0;
            string prevQuest = "";
            QuestInfo prevInfo = null;

            try
            {
                var lines = File.ReadAllLines(logPath);
                foreach (var line in lines)
                {
                    // Extract Quest text
                    int qIdx = line.IndexOf("Quest:");
                    if (qIdx < 0) continue;

                    string questText = line.Substring(qIdx + 6).Trim();
                    if (string.IsNullOrWhiteSpace(questText)) continue;

                    // Quest changed = previous quest completed
                    if (questText != prevQuest && !string.IsNullOrEmpty(prevQuest))
                    {
                        if (prevInfo != null && prevInfo.Type != QuestType.UNKNOWN)
                        {
                            UpdateLearnedPattern(prevInfo, true, "FROM_LOG");
                            patternsLearned++;
                        }
                    }

                    prevQuest = questText;
                    prevInfo = Classify(questText);
                }
            }
            catch { }

            SavePatterns();
            return patternsLearned;
        }

        // ======================== FILE I/O ========================
        private void LoadPatterns()
        {
            try
            {
                if (File.Exists(_patternsFilePath))
                {
                    string json = File.ReadAllText(_patternsFilePath);
                    _learnedPatterns = SimpleJsonParser.Deserialize<PatternDatabase>(json);
                }
            }
            catch { _learnedPatterns = new PatternDatabase(); }
        }

        private void SavePatterns()
        {
            try
            {
                string dir = Path.GetDirectoryName(_patternsFilePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string json = SimpleJsonParser.Serialize(_learnedPatterns);
                File.WriteAllText(_patternsFilePath, json);
            }
            catch { }
        }

        // ======================== HELPERS ========================
        private string ExtractTarget(string questText, string keyword, QuestType type)
        {
            try
            {
                int idx = questText.IndexOf(keyword);
                if (idx < 0) return "";

                string after = questText.Substring(idx + keyword.Length).Trim();

                // Remove count pattern
                after = Regex.Replace(after, @"\s*\(\d+/\d+\)$", "");

                // For TALK: "Gặp Bà Cụ để tìm..." → "Bà Cụ"
                if (type == QuestType.TALK)
                {
                    int deIdx = after.IndexOf(" để ");
                    if (deIdx > 0) after = after.Substring(0, deIdx);
                    int choIdx = after.IndexOf(" cho ");
                    if (choIdx > 0) after = after.Substring(0, choIdx);
                }

                // For KILL: "Tiêu diệt Heo Tím cản đường" → "Heo Tím"
                if (type == QuestType.KILL)
                {
                    // Take first 2-3 words as mob name
                    var words = after.Split(' ');
                    if (words.Length >= 2) after = string.Join(" ", words.Take(3));
                }

                return after.Trim();
            }
            catch { return ""; }
        }

        private QuestPattern FindLearnedPattern(string questText)
        {
            return _learnedPatterns.Patterns
                .Where(p => questText.Contains(p.Keyword) && p.SeenCount >= 2)
                .OrderByDescending(p => p.SuccessRate)
                .FirstOrDefault();
        }

        private QuestType ParseQuestType(string typeStr)
        {
            if (Enum.TryParse<QuestType>(typeStr, out var result)) return result;
            return QuestType.UNKNOWN;
        }

        private QuestAction GetDefaultAction(QuestType type)
        {
            switch (type)
            {
                case QuestType.MOVE: return QuestAction.CLICK_SHORTMISSION;
                case QuestType.TALK: return QuestAction.PATHFIND_AND_TALK_NPC;
                case QuestType.KILL: return QuestAction.WAIT_KILL;
                case QuestType.USE_ITEM: return QuestAction.PRESS_SKILL_KEY;
                case QuestType.COLLECT: return QuestAction.CLICK_SHORTMISSION;
                case QuestType.JOIN: return QuestAction.WAIT_USER;
                default: return QuestAction.CLICK_SHORTMISSION;
            }
        }

        /// <summary>
        /// Get summary string for logging
        /// </summary>
        public string GetStats()
        {
            return $"Classified={TotalClassified} Successful={TotalSuccessful} Patterns={_learnedPatterns.Patterns.Count}";
        }
    }

    // ======================== SIMPLE JSON HELPER ========================
    // Minimal JSON serializer for IL2CPP compatibility (no Newtonsoft)
    public static class SimpleJsonParser
    {
        public static string Serialize(PatternDatabase db)
        {
            var lines = new List<string>();
            lines.Add("{");
            lines.Add($"  \"totalSessions\": {db.TotalSessions},");
            lines.Add($"  \"lastUpdated\": \"{EscapeJson(db.LastUpdated)}\",");
            lines.Add("  \"patterns\": [");

            for (int i = 0; i < db.Patterns.Count; i++)
            {
                var p = db.Patterns[i];
                string actions = string.Join(", ", p.Actions.Select(a => $"\"{EscapeJson(a)}\""));
                string comma = i < db.Patterns.Count - 1 ? "," : "";
                lines.Add("    {");
                lines.Add($"      \"keyword\": \"{EscapeJson(p.Keyword)}\",");
                lines.Add($"      \"type\": \"{EscapeJson(p.Type)}\",");
                lines.Add($"      \"actions\": [{actions}],");
                lines.Add($"      \"successRate\": {p.SuccessRate:F4},");
                lines.Add($"      \"seenCount\": {p.SeenCount}");
                lines.Add($"    }}{comma}");
            }

            lines.Add("  ]");
            lines.Add("}");
            return string.Join("\n", lines);
        }

        public static T Deserialize<T>(string json) where T : new()
        {
            // Simple pattern-based parser for our specific schema
            if (typeof(T) == typeof(PatternDatabase))
            {
                var db = new PatternDatabase();

                // Parse totalSessions
                var sessionMatch = Regex.Match(json, @"""totalSessions"":\s*(\d+)");
                if (sessionMatch.Success) db.TotalSessions = int.Parse(sessionMatch.Groups[1].Value);

                // Parse lastUpdated
                var updatedMatch = Regex.Match(json, @"""lastUpdated"":\s*""([^""]*)""");
                if (updatedMatch.Success) db.LastUpdated = updatedMatch.Groups[1].Value;

                // Parse patterns
                var patternMatches = Regex.Matches(json, 
                    @"""keyword"":\s*""([^""]*)""\s*,\s*""type"":\s*""([^""]*)""\s*,\s*""actions"":\s*\[([^\]]*)\]\s*,\s*""successRate"":\s*([\d.]+)\s*,\s*""seenCount"":\s*(\d+)");

                foreach (Match m in patternMatches)
                {
                    var actions = Regex.Matches(m.Groups[3].Value, @"""([^""]*)""")
                        .Cast<Match>().Select(am => am.Groups[1].Value).ToList();

                    db.Patterns.Add(new QuestPattern
                    {
                        Keyword = m.Groups[1].Value,
                        Type = m.Groups[2].Value,
                        Actions = actions,
                        SuccessRate = float.Parse(m.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture),
                        SeenCount = int.Parse(m.Groups[5].Value)
                    });
                }

                return (T)(object)db;
            }

            return new T();
        }

        private static string EscapeJson(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }
    }
}
