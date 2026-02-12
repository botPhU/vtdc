using System;
using System.IO;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;
using BepInEx;

namespace AutoQuestPlugin
{
    /// <summary>
    /// StateObserver records changes in game state to a log file.
    /// Uses a background thread to write logs to avoid FPS drops in the main Unity thread.
    /// </summary>
    public class StateObserver : MonoBehaviour
    {
        // --- CONFIGURATION ---
        private string logPath;
        private readonly ConcurrentQueue<string> logQueue = new ConcurrentQueue<string>();
        private Thread writeThread;
        private volatile bool isRunning = false;

        // --- MONITORED VARIABLES (User to Connect) ---
        // Assign these in the Update loop based on your game logic
        public int currentStateId;
        public int currentStepIndex;
        public string currentLocation;
        public string currentActionTarget;

        // --- PREVIOUS FRAME STATE ---
        private int prevStateId = -1;
        private int prevStepIndex = -1;
        private string prevLocation = "";
        private string prevActionTarget = "";

        void Awake()
        {
            try 
            {
                // FORCE ABSOLUTE PATH to avoid any relative path issues
                string folder = @"c:\Users\Dell\Documents\vu tru dai chien\2026_02_11_OpenTest_015\BepInEx\plugins\StateLogs";
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                // Unique log file per session
                logPath = Path.Combine(folder, $"session_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                
                // Start Background Writer Thread
                isRunning = true;
                writeThread = new Thread(WriteLogLoop);
                writeThread.IsBackground = true;
                writeThread.Start();
                
                Plugin.Log.LogInfo($"[StateObserver] ✅ STARTED! Recording to: {logPath}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[StateObserver] ❌ Init Error: {ex.Message}");
            }
        }

        void Update()
        {
            // ========================================================================
            // 🟢 USER LOGIC HERE: CONNECT GAME VARIABLES TO OBSERVER
            // ========================================================================
            
            // Example (Uncomment and edit):
            // currentStateId = GameController.Instance.CurrentState;
            // currentStepIndex = QuestManager.Step;
            // currentLocation = MapManager.CurrentMapName;
            // currentActionTarget = Player.Target?.Name ?? "None";
            
            // ------------------------------------------------------------------------

            // Check for changes (Observer Pattern)
            bool isChanged = false;

            if (currentStateId != prevStateId) isChanged = true;
            else if (currentStepIndex != prevStepIndex) isChanged = true;
            else if (currentLocation != prevLocation) isChanged = true;
            else if (currentActionTarget != prevActionTarget) isChanged = true;

            if (isChanged)
            {
                // Format: [TIMESTAMP] | State_ID:{id} | Step_Index:{index} | Location:{loc} | Action_Target:{target}
                // Using standard string handling which is fast enough for state changes
                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                string logEntry = $"[{timestamp}] | State_ID:{currentStateId} | Step_Index:{currentStepIndex} | Location:{currentLocation ?? "null"} | Action_Target:{currentActionTarget ?? "null"}";
                
                // Enqueue for background writing (Zero FPS cost)
                logQueue.Enqueue(logEntry);

                // Update previous state
                prevStateId = currentStateId;
                prevStepIndex = currentStepIndex;
                prevLocation = currentLocation;
                prevActionTarget = currentActionTarget;
            }
        }

        /// <summary>
        /// Background loop to write logs to disk.
        /// </summary>
        private void WriteLogLoop()
        {
            try
            {
                using (StreamWriter sw = new StreamWriter(logPath, true))
                {
                    sw.AutoFlush = true; 
                    while (isRunning || !logQueue.IsEmpty)
                    {
                        if (logQueue.TryDequeue(out string log))
                        {
                            sw.WriteLine(log);
                        }
                        else
                        {
                            // Sleep to reduce CPU usage when queue is empty
                            Thread.Sleep(50); 
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Since this is a background thread, we can't easily log to Unity console, 
                // but we can try to suppress strictly to avoid crashes.
            }
        }

        void OnDestroy()
        {
            // Stop thread gracefully
            isRunning = false;
            // Wait a bit for queue to drain if needed (optional)
            if (writeThread != null && writeThread.IsAlive)
            {
                writeThread.Join(500); 
            }
        }
    }
}
