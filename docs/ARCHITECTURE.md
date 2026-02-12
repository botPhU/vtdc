# 🏗️ Kiến Trúc Plugin — Auto Quest Bot

## Tổng Quan

Plugin chạy trên **BepInEx 6** (IL2CPP), inject vào Unity game "Vũ Trụ Đại Chiến".

```
Game Process
  └── BepInEx 6 (IL2CPP)
        └── Plugin.cs (Entry point)
              └── BotController.cs (MonoBehaviour, chạy trong Update loop)
                    ├── Module 1: Auto Quest + Pathfind
                    ├── Module 2: Auto Interact
                    ├── Module 3: Auto NPC Dialog
                    ├── Module 4: Auto Dismiss Popups
                    ├── Module 5: Guide/Tutorial Dismiss
                    ├── Module 6: Auto Revive
                    ├── Module 7: Boss Scanner
                    ├── Module 8: Command Console
                    ├── Farm State Machine
                    ├── Input Recorder
                    └── State Observer
```

## File Chính

### `Plugin.cs`
- Entry point của BepInEx plugin
- Đọc config từ `BepInEx/config/`
- Tạo `BotController` GameObject
- Truyền config (account, window size) vào `BotController`

### `BotController.cs` (~3700 dòng)
- **MonoBehaviour** chính, chạy mỗi frame trong `Update()`
- Chứa TẤT CẢ logic bot (modules, state machine, input recorder)
- Giao tiếp với game qua: `GameObject.Find()`, `GetComponent<>()`, `Button.onClick`

### `DeepScanner.cs`
- Scan game scene: tìm tất cả managers, components, methods
- Ghi kết quả ra file text trong `BepInEx/plugins/inspector_logs/`
- Hữu ích để reverse-engineer game API

### `RuntimeInspector.cs`
- Scan real-time: buttons, panels, text fields
- Ghi danh sách UI elements và trạng thái (active/inactive)

### `StateObserver.cs`
- (Legacy) Đã được tích hợp inline vào `BotController.cs`

## Update() Loop — Thứ Tự Chạy

Mỗi frame, `Update()` chạy theo thứ tự:

1. **Resize check** — Enforce window size
2. **Scene change detection** — Detect map transition
3. **Auto Login** — Login + select character + enter game
4. **Hotkeys** — F2/F3/F4/F5/F6/F11
5. **Player position tracking** — Track idle/moving
6. **🔄 Farm State Machine** — Quản lý vòng lặp quest
7. **Module 1: Auto Quest** — Pathfind + quest detection
8. **Module 2: Auto Interact** — Click interact button
9. **Module 3: Auto NPC Dialog** — Click NPC buttons
10. **NPC Interaction Recorder** — Hook button clicks
11. **Module 4: Dismiss Popups** — Đóng popup
12. **Module 5: Dismiss Guide** — Đóng hướng dẫn
13. **Module 6: Auto Revive** — Hồi sinh
14. **Module 7: Boss Scanner** — Scan thông báo boss
15. **Module 8: Command Console** — Đọc command từ Launcher
16. **Memory Optimizer** — Dọn RAM
17. **Status File** — Ghi trạng thái cho Launcher
18. **🎮 Input Recorder** — Ghi click + phím người chơi
19. **📊 State Observer** — Ghi log trạng thái

## Cách Game Hoạt Động (cho dev mới)

### Quest System
- Quest text hiển thị trên `ShortMissionPanel`
- Click `ShortMissionPanel` → game auto-pathfind đến NPC/target
- Kill quest format: `"Tiêu diệt X (<color>0</color>/2)"`
- Collect quest keywords: `"Thu thập"`, `"Nhặt"`, `"Lượm"`

### NPC Dialog
- Panel: `NpcInteractPanel(Clone)`
- Buttons: Quest button, Talk/Next button, Close button
- Dialog tự đóng khi hết text

### Interact Button
- `InteractButton` (HUD) — hiện khi gần NPC/vật phẩm
- Bot tự click khi thấy active

### Code Obfuscation
- Game code bị obfuscate (vd: `JMEEAJIFLEF`)
- → Dùng **UI-driven automation**: tìm button bằng `GameObject.Find()` rồi click
- → KHÔNG gọi game API trực tiếp

## Quy Tắc Khi Code

1. **Wrap trong try-catch** — Game objects có thể null bất cứ lúc nào
2. **Dùng GameObject.Find()** — Tìm UI theo tên
3. **GetComponentInChildren<>()** — Tìm component trong hierarchy
4. **Button.onClick.Invoke()** — Simulate click
5. **LogActivity()** — Ghi log cho BepInEx console
6. **LogStateAction()** — Ghi log cho State Observer file
7. **Build**: `cd AutoQuestPlugin && dotnet build`
