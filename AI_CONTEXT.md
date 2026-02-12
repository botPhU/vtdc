# 🎮 VŨ TRỤ ĐẠI CHIẾN — AI Development Context
> Cập nhật: 2026-02-12 11:08 — Tài liệu này giúp AI mới hiểu toàn bộ dự án

## 📋 Tổng Quan Dự Án

**Game:** Vũ Trụ Đại Chiến (Unity, IL2CPP, Android/PC)  
**Mục tiêu:** Tạo bot auto-quest + multi-account cho game  
**Thư mục gốc:** `c:\Users\Dell\Documents\vu tru dai chien\2026_02_11_OpenTest_015\`

## 🏗️ Kiến Trúc Hệ Thống

```
2026_02_11_OpenTest_015/
├── Vũ Trụ Đại Chiến.exe          ← Game chính (Unity IL2CPP)
├── BepInEx/                       ← Framework mod (BepInEx 6 BE)
│   ├── plugins/
│   │   ├── AutoQuestPlugin.dll    ← Plugin đã build
│   │   ├── bot_status/            ← Status file cho launcher đọc
│   │   │   └── status_{user}.json ← Ghi mỗi 5s: quest, scene, state
│   │   └── inspector_logs/        ← Deep scan logs
│   │       ├── buttons_*.txt
│   │       ├── scene_*.txt
│   │       ├── deep_AutoMissionManager_*.txt
│   │       └── all_managers_*.txt
│   └── LogOutput.log              ← BepInEx log chính
│
├── AutoQuestPlugin/               ← Source code plugin (C# .NET 6)
│   ├── AutoQuestPlugin.csproj     ← Project file
│   ├── Class1.cs                  ← Plugin entry (BepInPlugin, Inspector, DeepScanner)
│   └── BotController.cs           ← Bot logic (auto login, quest, attack, interact)
│
├── tools/
│   ├── AccountLauncher/           ← Source Launcher V3 (WinForms .NET 8)
│   │   └── Form1.cs               ← UI: accounts, settings, inspector pages
│   └── LauncherV3/                ← Published Launcher (self-contained exe)
│       └── AccountLauncher.exe
│
├── accounts.json                  ← Config: tài khoản, settings
├── HUONG_DAN_INSPECTOR.md         ← Hướng dẫn phím tắt
└── AI_CONTEXT.md                  ← 📌 File này
```

## 🔧 Tech Stack

| Component | Technology |
|---|---|
| Game Engine | Unity 2021+ IL2CPP |
| Mod Framework | BepInEx 6 BE (Bleeding Edge) |
| Plugin | C# .NET 6.0, Il2CppInterop |
| Launcher | C# .NET 8.0 WinForms, self-contained |
| APIs | Unity API, IL2CPP Reflection, Win32 (PostMessage, EnumWindows) |

## 📦 Plugin: AutoQuestPlugin

### Files
- **Class1.cs** — Entry point (BepInPlugin), chứa `Inspector` + `DeepScanner` components
- **BotController.cs** — Main bot logic, ~1300 dòng

### Class1.cs Components
- **Plugin** (`BepInPlugin`): Load plugin, attach Inspector/DeepScanner/BotController
- **Inspector**: Scan scene khi đổi scene, dump buttons/objects ra file log
  - F3-F8: Scan buttons, scene, network, textures, etc.
  - Tự scan khi đổi scene
- **DeepScanner**: Deep scan managers bằng IL2CPP reflection
  - F9-F12, Numpad0/1: Deep scan các manager cụ thể
  - Dump methods, properties, fields, children, siblings

### BotController.cs Features

#### Hotkeys
| Key | Chức năng |
|---|---|
| F1 | Toggle AUTO ALL (Quest + Attack + Interact + Pathfind) |
| F2 | Toggle Headless Mode |
| F11 | Force Auto Login |
| F12 | Log Player + Quest Status |

#### Auto Systems (khi F1 bật)
1. **AutoMissionManager.auto()** — Bật hệ thống auto quest
2. **MainGame.setPlayerOnAutoMode()** — Bật auto di chuyển
3. **Click ShortMissionPanel** mỗi 5s — Trigger pathfind đến quest NPC
4. **Click InteractButton** mỗi 2s — Tương tác NPC
5. **TryClickNpcDialog** mỗi 3s — Auto click nút "Nhiệm vụ", "Nhận", "OK" trong NPC dialog
6. **ToggleAutoAttack** — Click AutoAttackButton

#### Auto Login
- Đọc `accounts.json` → parse username/password/server
- Hỗ trợ multi-instance qua env `VTDC_ACCOUNT_INDEX`
- Tự login khi vào LoginScene (chờ 4s, tìm TMP_InputField, nhập text, click Login)

#### Status File
- Ghi `BepInEx/plugins/bot_status/status_{username}.json` mỗi 5s
- Nội dung: username, scene, quest text, quest hint, autoEnabled, headless, timestamp
- Launcher đọc file này để hiển thị Quest/Map trên UI

#### Memory Optimizer
- Dọn RAM định kỳ khi headless mode
- `GC.Collect()`, `Resources.UnloadUnusedAssets()`

### Build & Deploy
```powershell
# Build plugin
cd AutoQuestPlugin
dotnet build -c Release

# Deploy
Copy-Item "bin\Release\net6.0\AutoQuestPlugin.dll" "..\BepInEx\plugins\" -Force
```

## 🖥️ Launcher V3 (AccountLauncher)

### UI Pages
1. **Accounts Page** — Bảng tài khoản với các cột:
   - #, Tên, Tài khoản, Sv, 🗺 Map, 📜 Nhiệm vụ, Auto, RAM, Trạng thái
   - Map + Nhiệm vụ đọc từ `bot_status/status_{user}.json`
2. **Settings Page** — Cài đặt graphics, window size, FPS, headless options
3. **Inspector Page** — Gửi phím F3-F12 đến game window, xem log files

### Key Functions
- `ResolveGamePath()` — Tìm game path (dùng Directory.GetFiles, tránh lỗi Unicode)
- `FindGameWindows()` — 3 chiến lược fallback: tracked processes → title match → process scan
- `LaunchInstance()` — Chạy game exe với env vars (VTDC_ACCOUNT_INDEX, etc.)
- `ReadBotStatus()` — Đọc status file từ plugin
- `ProcessMonitor_Tick()` — Refresh grid mỗi 5s

### Build & Publish
```powershell
cd tools\AccountLauncher
dotnet publish -c Release -r win-x64 --self-contained true -o "..\LauncherV3"
```

## 🎯 Game Structure (từ Deep Scan)

### Managers (trên Manager GameObject)
| Manager | Path | Methods quan trọng |
|---|---|---|
| AutoMissionManager | Manager/AutoMissionManager | `auto()`, `stop()`, `get_state()` |
| PlayerDataManager | Manager/PlayerDataManager | `gI()`, `getOtherPlayerData()` |
| UIManager | Manager/UIManager | `createNotifyPopup()`, `isShowing()` |
| AppManager | AppManager | `isIngame()`, `setInGame()`, `setOutGame()` |
| GuideManager | Manager/GuideManager | `hasGuide()`, `onGuideStop()` |

### Scene Managers (trên SceneManager GO)
| Manager | Methods quan trọng |
|---|---|
| MainGame | `setPlayerOnAutoMode()`, `getMyPlayer()`, `getMapManager()` |
| MainGameUIManager | `onGameUIPanelOpen()`, `onGameUIPanelClose()` |
| GameManager | `getMyPlayer()`, `getEntityManager()`, `getMapManager()` |

### UI Buttons (trên HUDCanvas)
| Button | Path | Shortcut |
|---|---|---|
| AutoAttackButton | HUDCanvas/SkillLayout/ | Q |
| InteractButton | HUDCanvas/SkillLayout/ | — |
| ShortMissionPanel | HUDCanvas/SafetyUI/QuickInfoPanel/ | — |
| Skill1-8 | HUDCanvas/SkillLayout/ | — |
| ChangeTargetButton | HUDCanvas/SkillLayout/ | — |

### Services
- AutoAttackBlackBoardComponent: `show()`, `getCurrSill()`, `needToRevive()`
- NpcStateService: `getMissionStateSprite()`, `getMissionStateDataAsset()`
- MobService: `getMiniMapIconByTye()`

## 🐛 Bugs Đã Fix

1. **Unicode path**: `File.Exists("Vũ Trụ Đại Chiến.exe")` fail → dùng `Directory.GetFiles("*.exe")`
2. **Unicode window title**: `GetWindowText` trả về sai → fallback process name scan
3. **Auto quest không di chuyển**: `AutoMissionManager.auto()` chỉ bật system, cần thêm click ShortMissionPanel + `setPlayerOnAutoMode()`
4. **Không nhận quest**: Thiếu auto-click nút "Nhiệm vụ" trong NPC dialog → thêm `TryClickNpcDialog()`
5. **DLL bị khóa**: Game đang chạy → phải tắt game trước khi deploy DLL mới

## 📊 Quest Flow Đã Test (Log 2026-02-12)

```
1. Auto Login: phubap89 → LoginScene → MainGameScene ✅
2. FindManagers: AutoMissionManager, PlayerDataManager, GameManager, MainGame ✅
3. F1 → AUTO ALL BẬT ✅
4. Click ShortMissionPanel → pathfind đến NPC ✅
5. Quest: "Gặp Quy Lão Tiên Sinh" → "Dắt Lunch về gặp Quy Lão" → "Tìm Bộ Tạp Chí (0/1)" → "Giao Bộ Tạp Chí" ✅
6. Auto Interact: Click mỗi 2s ✅
7. Quest tiến triển tự động qua nhiều bước ✅
```

## ⚠️ Lưu Ý Cho AI Mới

1. **IL2CPP Reflection**: Dùng `GetIl2CppType().GetMethods()` thay vì .NET reflection
2. **Method names bị obfuscate**: Hầu hết method names là random (ABCDEFG...), chỉ một số giữ tên gốc
3. **BepInEx 6 BE**: Khác BepInEx 5, dùng `Il2CppInterop`, `MonoBehaviourExtensions`
4. **Game update**: Method names sẽ thay đổi khi game update, cần deep scan lại
5. **Unicode**: Tên game "Vũ Trụ Đại Chiến" gây lỗi encoding ở nhiều chỗ
6. **No System.Text.Json**: Plugin IL2CPP không dùng được, phải build JSON bằng string concat
7. **accounts.json path**: Plugin đọc từ `Assembly.Location/../accounts.json`
8. **Multi-instance**: Mỗi instance nhận `VTDC_ACCOUNT_INDEX` env var
