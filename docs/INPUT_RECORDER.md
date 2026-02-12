# 🎮 Input Recorder — Hệ Thống Ghi Input Người Chơi

## Mục Đích

Ghi lại MỌI thao tác của người chơi (click, nhấn phím, tương tác NPC) vào file log. Bot sẽ "học" từ dữ liệu này để tự động hóa chính xác hơn.

## Các Loại Log

### 1. `USER_CLICK` — Click vào UI Element
```
[19:05:00.123] | ACTION: USER_CLICK: InventoryButton | Text: Túi đồ | Path: HUDCanvas/SafetyUI/QuickFeatureMenu/ButtonLayout/InventoryButton
```
- **Khi nào**: Người chơi click vào bất kỳ button/UI nào
- **Ghi gì**: Tên object, text hiển thị, đường dẫn UI hierarchy
- **Cách hoạt động**: Dùng `EventSystem.current.currentSelectedGameObject`

### 2. `USER_CLICK_WORLD` — Click vào Game World
```
[19:05:00.123] | ACTION: USER_CLICK_WORLD: ScreenPos(500,300)
```
- **Khi nào**: Click vào map/NPC/mob (không phải UI)
- **Ghi gì**: Tọa độ màn hình

### 3. `USER_KEY` — Nhấn Phím
```
[19:05:00.123] | ACTION: USER_KEY: W
```
- **Phím theo dõi**: WASD, mũi tên, Space, Enter, Escape, 1-5, E/Q/R/T, Tab

### 4. `NPC_DIALOG_OPENED` — NPC Dialog Mở
```
[19:05:00.123] | ACTION: NPC_DIALOG_OPENED: NPC=Ông Guhan | Dialog=Xin chào chiến binh!
```
- **Khi nào**: `NpcInteractPanel(Clone)` xuất hiện
- **Ghi gì**: Tên NPC, nội dung dialog

### 5. `USER_CLICK_NPC_BTN` — Click Button Trong NPC Dialog
```
[19:05:01.456] | ACTION: USER_CLICK_NPC_BTN: QuestButton | Text: Nhiệm vụ | NPC: Ông Guhan | Path: NpcInteractPanel(Clone)/Buttons/QuestButton
```
- **Khi nào**: Người chơi click BẤT KỲ button nào trong NPC dialog
- **Ghi gì**: Tên button, text, tên NPC, đường dẫn
- **Cách hoạt động**: Hook `onClick.AddListener` lên mọi button khi dialog mở

### 6. `NPC_DIALOG_CLOSED` — NPC Dialog Đóng
```
[19:05:02.789] | ACTION: NPC_DIALOG_CLOSED → re-trigger pathfind
```

## Ví Dụ Flow Hoàn Chỉnh

Khi người chơi nói chuyện với NPC và nhận quest:
```
[19:05:00] | ACTION: USER_CLICK_WORLD: ScreenPos(400,300)         ← Click vào NPC trên map
[19:05:00] | ACTION: NPC_DIALOG_OPENED: NPC=Ông Guhan | Dialog=... ← Dialog mở
[19:05:01] | ACTION: USER_CLICK_NPC_BTN: QuestBtn | Text: Nhiệm vụ  ← Click "Nhiệm vụ"
[19:05:02] | STATE: Quest=Tiêu diệt Khủng Long (0/2)              ← Quest thay đổi
[19:05:02] | ACTION: USER_CLICK_NPC_BTN: CloseBtn | Text: Đóng      ← Đóng dialog
[19:05:02] | ACTION: NPC_DIALOG_CLOSED → re-trigger pathfind        ← Dialog đóng
[19:05:03] | ACTION: FARM: IDLE → PATHFINDING | ...                  ← Farm machine bắt đầu
```

## File Log

- **Vị trí**: `BepInEx/plugins/StateLogs/session_YYYYMMDD_HHmmss.txt`
- **Mỗi session** tạo file mới
- **Thread-safe**: Dùng `ConcurrentQueue` + background thread

## Code Location

- **INPUT RECORDER**: Module trong `Update()` — ghi click + phím
- **NPC INTERACTION RECORDER**: Module trong `Update()` — hook NPC dialog buttons
- **`HookDialogButtons()`**: Method gắn onClick listener lên tất cả button
- **`LogStateAction()`**: Helper ghi 1 dòng log với timestamp + position + quest
