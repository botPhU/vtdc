# 🤖 Vũ Trụ Đại Chiến — Auto Quest Bot

Bot tự động hoàn thành nhiệm vụ cho game **Vũ Trụ Đại Chiến** (Unity IL2CPP), sử dụng BepInEx 6.

## ✨ Tính Năng

| Tính năng | Mô tả | Hotkey |
|---|---|---|
| **Auto Quest** | Tự pathfind + nói chuyện NPC + nhận/trả quest | F1 (config) |
| **Auto Attack** | Tự đánh quái khi có quest kill | F3 |
| **Auto Interact** | Tự click nút tương tác khi gần NPC/vật phẩm | F4 |
| **Auto NPC Dialog** | Tự click các button trong dialog NPC | F5 |
| **Auto Revive** | Tự hồi sinh khi chết | Tự động |
| **Headless Mode** | Tắt render 3D, tiết kiệm RAM (cho multi-account) | F2 |
| **Auto Login** | Tự đăng nhập + chọn nhân vật + vào game | Tự động |
| **Farm State Machine** | Vòng lặp quest liên tục với stuck detection | Tự động |
| **Input Recorder** | Ghi lại click + phím người chơi để bot "học" | Tự động |
| **Game Speed** | Tăng tốc game x1/x2/x3 | F6 |

## 📁 Cấu Trúc Dự Án

```
├── AutoQuestPlugin/          # 🔧 Source code chính
│   ├── BotController.cs      # Bot logic: quest, attack, farm state machine
│   ├── Plugin.cs             # BepInEx plugin entry point
│   ├── DeepScanner.cs        # Scan game objects và managers
│   ├── RuntimeInspector.cs   # Inspector: scan buttons, panels, texts
│   └── StateObserver.cs      # (Legacy) State observer component
│
├── tools/AccountLauncher/    # 🖥️ Launcher cho multi-account
│   ├── Form1.cs              # UI launcher
│   └── ...
│
├── BepInEx/                  # ⚙️ BepInEx config (chỉ config, ko DLL)
│   └── config/
│
├── docs/                     # 📖 Tài liệu
│   ├── ARCHITECTURE.md       # Kiến trúc plugin
│   ├── SETUP.md              # Hướng dẫn cài đặt
│   ├── FARM_STATE_MACHINE.md # Giải thích state machine
│   └── INPUT_RECORDER.md     # Giải thích hệ thống ghi input
│
├── HUONG_DAN_INSPECTOR.md    # Hướng dẫn Inspector
├── HUONG_DAN_STATE_OBSERVER.txt # Hướng dẫn State Observer
└── AI_CONTEXT.md             # Context cho AI coding
```

## 🚀 Cài Đặt Nhanh

Xem chi tiết: [docs/SETUP.md](docs/SETUP.md)

1. Cài BepInEx 6 BE vào thư mục game
2. Clone repo này vào thư mục game
3. `cd AutoQuestPlugin && dotnet build`
4. Mở game → Plugin tự chạy

## 🔧 Yêu Cầu

- .NET SDK 6.0+
- BepInEx 6 (IL2CPP BE build)
- Game: Vũ Trụ Đại Chiến (phiên bản OpenTest)

## 👥 Đóng Góp

Xem [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) để hiểu cấu trúc code trước khi code.
