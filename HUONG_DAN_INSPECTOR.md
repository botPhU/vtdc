# 🔍 Hướng Dẫn Plugin — Inspector + Bot Controller + DeepScanner

Plugin **AutoQuestPlugin** gồm 3 module:
- **RuntimeInspector** — Quét game objects, UI, network
- **BotController** — Auto quest, auto attack, auto interact, headless mode
- **DeepScanner** — Deep scan class methods/fields bằng IL2CPP reflection

---

## 🔎 RuntimeInspector (F3-F8)

| Phím | Tên | Mô tả |
|------|-----|--------|
| **F3** | Scene Objects | Quét tất cả objects trong scene hiện tại |
| **F4** | MonoBehaviours | Tìm các component có hàm `Update()` |
| **F5** | UI Text | Tìm tất cả text đang hiển thị trên UI |
| **F6** | Buttons | Dump tất cả button và sự kiện click |
| **F7** | Singletons | Tìm các Singleton/Manager pattern |
| **F8** | Network | Tìm các class liên quan đến mạng |

---

## 🤖 Bot Controller

| Phím | Tên | Mô tả |
|------|-----|--------|
| **F1** | ★ AUTO ALL | Bật/tắt **quest + attack + interact** cùng lúc |
| **F2** | Headless Mode | Bật/tắt chế độ ẩn đồ họa, tiết kiệm tài nguyên |
| **F11** | Force Auto Login | Bắt buộc thực hiện auto login lại |
| **F12** | Player + Quest Status | Log thông tin nhân vật + nhiệm vụ hiện tại |

### F1 — Auto ALL bao gồm:
1. **Auto Quest** — Gọi `AutoMissionManager.auto()` để bật auto mission
2. **Auto Attack** — Click `AutoAttackButton` để bật auto tấn công
3. **Auto Interact** — Tự nhấn `InteractButton` mỗi 2 giây (khi nút hiển thị)
4. **Quest Monitor** — Đọc text từ `ShortMissionPanel` mỗi 15 giây

---

## 🔬 DeepScanner (F9, F10, Numpad)

| Phím | Tên | Mô tả |
|------|-----|--------|
| **F9** | Deep Scan AutoMissionManager | Dump toàn bộ methods, properties, fields + giá trị |
| **F10** | Deep Scan PlayerDataManager | Dump toàn bộ methods, properties, fields + giá trị |
| **Numpad0** | Cycle Scan | Quét lần lượt 15 class khác (AutoAttackButton, InteractButton, UIManager, NpcStateService...) |
| **Numpad1** | Scan ALL Managers | Tổng hợp tất cả 20 managers: tên, số methods/properties/fields |

### Output format (deep scan):
```
=== DEEP SCAN: AutoMissionManager ===
Type: AutoMissionManager
BaseType: MonoBehaviour

--- METHODS (32) ---
  [1] public void auto()
  [2] public void stop()
  [3] public Boolean get_state()
  ...

--- PROPERTIES (8) ---
  [1] Boolean state (get) = True
  ...

--- FIELDS (15) ---
  [1] non-public Int32 _currentMissionId = 1023
  ...
```

---

## 📋 Quy trình sử dụng

### Phân tích game (lần đầu):
1. Mở game qua Launcher → vào `MainGameScene`
2. Nhấn **F9** → deep scan `AutoMissionManager`
3. Nhấn **F10** → deep scan `PlayerDataManager`
4. Nhấn **Numpad1** → tổng quan tất cả managers
5. Xem file log tại `BepInEx/plugins/inspector_logs/deep_*.txt`

### Chạy bot:
1. Mở game → vào `MainGameScene`
2. Nhấn **F1** → bật AUTO ALL (quest + attack + interact)
3. Nhấn **F2** → bật Headless mode (nếu chạy nền)
4. Nhấn **F12** → kiểm tra trạng thái quest

### Kết quả lưu ở đâu?
`BepInEx/plugins/inspector_logs/`

| File pattern | Nội dung |
|---|---|
| `scene_*.txt` | Cấu trúc scene |
| `monobehaviours_*.txt` | Danh sách MonoBehaviours |
| `buttons_*.txt` | Danh sách buttons |
| `deep_*.txt` | Deep scan chi tiết class |
| `all_managers_*.txt` | Tổng hợp managers |
