# 📋 Requirements — Yêu Cầu Hệ Thống & Dependencies

## Hệ Thống

| Yêu cầu | Phiên bản | Ghi chú |
|---|---|---|
| **OS** | Windows 10/11 | 64-bit |
| **RAM** | 4GB+ | Headless mode: ~200MB/instance |
| **.NET SDK** | 6.0+ | Cho plugin (`AutoQuestPlugin`) |
| **.NET SDK** | 8.0+ | Cho launcher (`AccountLauncher`) |
| **Game** | Vũ Trụ Đại Chiến | Bản OpenTest 2026 |

## Download Links

| Phần mềm | Link |
|---|---|
| .NET 6 SDK | https://dotnet.microsoft.com/download/dotnet/6.0 |
| .NET 8 SDK | https://dotnet.microsoft.com/download/dotnet/8.0 |
| BepInEx 6 BE | https://builds.bepinex.dev/projects/bepinex_be |
| Git | https://git-scm.com/download/win |

## Dependencies — Plugin (`AutoQuestPlugin`)

| Package | Version | Nguồn |
|---|---|---|
| `BepInEx.Core` | 6.x | `BepInEx/core/` |
| `BepInEx.Unity.IL2CPP` | 6.x | `BepInEx/core/` |
| `BepInEx.Unity.Common` | 6.x | `BepInEx/core/` |
| `Il2CppInterop.Runtime` | 1.5.0 | `BepInEx/core/` |
| `0Harmony` | 2.x | `BepInEx/core/` |
| `UnityEngine.CoreModule` | 2022.3 | `BepInEx/interop/` (auto-generated) |
| `UnityEngine.UI` | 2022.3 | `BepInEx/interop/` |
| `UnityEngine.InputLegacyModule` | 2022.3 | `BepInEx/interop/` |
| `Unity.TextMeshPro` | - | `BepInEx/interop/` |
| `Assembly-CSharp` | - | `BepInEx/interop/` (game code) |

> **Lưu ý:** Các file trong `BepInEx/interop/` được tự sinh ra khi chạy game lần đầu với BepInEx. KHÔNG có trong git repo.

## Dependencies — Launcher (`AccountLauncher`)

| Package | Version | Ghi chú |
|---|---|---|
| `System.Windows.Forms` | Built-in | .NET 8 WinForms |
| `System.Text.Json` | Built-in | Đọc/ghi JSON config |
| `System.Diagnostics.Process` | Built-in | Quản lý game processes |

> Launcher **không cần** NuGet packages, tất cả đều có sẵn trong .NET SDK.

## Cấu Trúc Thư Mục Cần Thiết

```
Game Folder/
├── BepInEx/
│   ├── core/           ← BepInEx DLLs (download)
│   ├── interop/        ← Auto-generated sau khi chạy game 1 lần
│   ├── config/         ← Config files
│   └── plugins/        ← Plugin DLL (build output)
├── AutoQuestPlugin/    ← Source code (git clone)
├── tools/
│   └── AccountLauncher/ ← Launcher source
├── doorstop_config.ini ← BepInEx doorstop
└── winhttp.dll         ← BepInEx doorstop
```

## Kiểm Tra Cài Đặt

```bash
# Kiểm tra .NET
dotnet --version

# Kiểm tra Git
git --version

# Build plugin
cd AutoQuestPlugin
dotnet build

# Build launcher
cd tools/AccountLauncher
dotnet build
```
