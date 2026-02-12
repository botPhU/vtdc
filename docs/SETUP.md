# 🛠️ Hướng Dẫn Cài Đặt

## Yêu Cầu

- **OS**: Windows 10/11
- **.NET SDK**: 6.0 trở lên ([Download](https://dotnet.microsoft.com/download/dotnet/6.0))
- **Game**: Vũ Trụ Đại Chiến (bản OpenTest)
- **BepInEx 6**: IL2CPP BE build

## Bước 1: Cài BepInEx 6

1. Download BepInEx 6 BE (IL2CPP) từ: https://builds.bepinex.dev/projects/bepinex_be
2. Giải nén vào thư mục game (cùng cấp với `Vũ Trụ Đại Chiến.exe`)
3. Chạy game 1 lần để BepInEx tạo các thư mục
4. Tắt game

Cấu trúc sau khi cài:
```
Game Folder/
├── BepInEx/
│   ├── core/
│   ├── config/
│   ├── plugins/      ← Plugin DLL sẽ nằm đây
│   └── interop/      ← Generated IL2CPP bindings
├── doorstop_config.ini
├── winhttp.dll
└── Vũ Trụ Đại Chiến.exe
```

## Bước 2: Clone Repo

```bash
cd "đường dẫn thư mục game"
git clone https://github.com/YOUR_USERNAME/YOUR_REPO.git .
```

Hoặc nếu đã có game folder:
```bash
git init
git remote add origin https://github.com/YOUR_USERNAME/YOUR_REPO.git
git pull origin main
```

## Bước 3: Build Plugin

```bash
cd AutoQuestPlugin
dotnet build
```

Build thành công → DLL tự copy vào `BepInEx/plugins/AutoQuestPlugin.dll`

## Bước 4: Chạy Game

1. Mở game bình thường (hoặc qua Launcher)
2. Plugin tự load khi game khởi động
3. Xem log: `BepInEx/LogOutput.log`

## Config

File config: `BepInEx/config/com.yourname.autoquestplugin.cfg`

| Key | Mô tả | Mặc định |
|---|---|---|
| `AutoLoginEnabled` | Tự đăng nhập | true |
| `Username` | Tài khoản | "" |
| `Password` | Mật khẩu | "" |
| `WindowWidth` | Chiều rộng cửa sổ | 800 |
| `WindowHeight` | Chiều cao cửa sổ | 600 |

## Hotkeys

| Phím | Chức năng |
|---|---|
| F2 | Toggle Headless Mode (tắt render) |
| F3 | Toggle Auto Attack |
| F4 | Toggle Auto Interact |
| F5 | Toggle Auto NPC Dialog |
| F6 | Game Speed (x1/x2/x3) |
| F11 | Force Re-Login |

## Troubleshooting

### Plugin không load
- Kiểm tra `BepInEx/LogOutput.log` có dòng `[AutoQuestPlugin]` không
- Đảm bảo `winhttp.dll` và `doorstop_config.ini` ở thư mục game

### Build lỗi
- Kiểm tra `BepInEx/interop/` có file DLL không (cần chạy game ít nhất 1 lần)
- Kiểm tra path trong `AutoQuestPlugin.csproj` đúng chưa

### DLL bị lock
- Tắt game trước khi build
- Hoặc build riêng rồi copy thủ công: `copy bin\Debug\net6.0\AutoQuestPlugin.dll ..\BepInEx\plugins\`
