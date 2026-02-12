# 🖥️ Hướng Dẫn Build Launcher Tool

## Tổng Quan

**AccountLauncher** là công cụ quản lý multi-account:
- Thêm/sửa/xóa tài khoản
- Launch nhiều game instances cùng lúc
- Dashboard theo dõi trạng thái bot real-time
- Gửi lệnh cho bot (start/stop/scan)
- Inspector: scan game objects từ xa

## Yêu Cầu

- .NET SDK **8.0** trở lên
- Windows 10/11 (WinForms)

## Build Nhanh

```bash
cd tools/AccountLauncher
dotnet build
```

Output: `bin/Debug/net8.0-windows/AccountLauncher.exe`

## Build Publish (standalone EXE)

```bash
# Framework-dependent (nhẹ, cần .NET 8 runtime)
dotnet publish -c Release -o publish

# Self-contained (nặng ~170MB, chạy không cần cài .NET)
dotnet publish -c Release -r win-x64 --self-contained -o publish_sc
```

## Cấu Trúc Code

```
tools/AccountLauncher/
├── AccountLauncher.csproj   # Project file (.NET 8 WinForms)
├── Form1.cs                 # UI + Logic chính (~2600 dòng)
└── Program.cs               # Entry point
```

### Form1.cs — Các Phần Chính

| Section | Dòng | Mô tả |
|---|---|---|
| **Constructor** | 79-114 | Khởi tạo, tìm game path, load config |
| **BuildUI** | 210-278 | Tạo giao diện chính (sidebar + pages) |
| **BuildAccountsPage** | 280-403 | Trang quản lý tài khoản |
| **BuildSettingsPage** | 405-607 | Trang cài đặt (window size, auto-quest toggles) |
| **BuildDashboardPage** | 1116-1368 | Trang dashboard bot status |
| **BuildInspectorPage** | 1711-2056 | Trang inspector (scan game) |
| **BuildGuidePage** | 2058-2120 | Trang hướng dẫn |
| **LaunchInstance** | 999-1079 | Logic launch game + truyền config |
| **SendBotCommand** | 1628-1708 | Gửi command cho bot qua file IPC |
| **Data Models** | 2401-2436 | AccountInfo, AppSettings, AccountConfig |

## Cách Launcher Giao Tiếp Với Bot

```
Launcher ──→ accounts.json ──→ Plugin đọc (auto login)
Launcher ──→ cmd_{username}.txt ──→ Bot đọc + thực thi
Bot ──→ status_{username}.json ──→ Launcher đọc (dashboard)
Bot ──→ resp_{username}.txt ──→ Launcher đọc (response)
```

### File IPC:
| File | Hướng | Mô tả |
|---|---|---|
| `accounts.json` | Launcher → Plugin | Danh sách tài khoản + config |
| `cmd_{username}.txt` | Launcher → Bot | Lệnh: `scan_buttons`, `toggle_quest`, etc. |
| `status_{username}.json` | Bot → Launcher | Trạng thái: scene, quest, level, HP |
| `resp_{username}.txt` | Bot → Launcher | Response lệnh scan |

## Thêm Tính Năng Mới

### Thêm Page Mới
1. Tạo method `BuildXxxPage()` trong `Form1.cs`
2. Thêm nav button trong `BuildUI()`
3. Thêm case trong `SwitchPage()`

### Thêm Bot Command Mới
1. Trong Launcher: ghi command vào `cmd_{username}.txt`
2. Trong `BotController.cs`: thêm case trong `CheckAndProcessCommand()`

### Thêm Dashboard Widget
1. Trong `BuildDashboardPage()`: thêm Label/Panel
2. Trong `RefreshDashboard()`: đọc data từ `status_{username}.json`

## UI Style Guide

Launcher dùng **dark theme** tự custom:
- Background: `#1E1E2E` (dark purple-gray)
- Sidebar: `#181825` (darker)
- Accent: `#89B4FA` (blue)
- Text: `#CDD6F4` (light)
- Cards: `#313244` (medium gray)
- Success: `#A6E3A1` (green)
- Warning: `#FAB387` (orange)
- Error: `#F38BA8` (red)

```csharp
// Ví dụ tạo button
var btn = CreateSolidButton("🚀 Launch", Color.FromArgb(137, 180, 250));
btn.Click += (s, e) => LaunchSelected();
```
