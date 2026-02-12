# 🔄 Farm State Machine

## Tổng Quan

Farm State Machine quản lý vòng lặp quest liên tục 24/7 với stuck detection và auto-retry.

## Sơ Đồ States

```
IDLE ──→ PATHFINDING ──→ KILLING ──→ RETURNING ──→ TURNING_IN ──→ IDLE
  │          │              │                          │
  │          ├──→ TALKING_NPC ────→ PATHFINDING         │
  │          │                                          │
  │          └──→ COLLECTING ──→ IDLE                   │
  │                                                     │
  └─────────────────────────────────────────────────────┘
```

## Chi Tiết States

| State | Mô tả | Chuyển khi |
|---|---|---|
| `IDLE` | Đợi quest mới | Có quest text → PATHFINDING |
| `PATHFINDING` | Đang di chuyển đến target | NPC dialog mở → TALKING_NPC |
| | | Kill quest detected → KILLING |
| | | Collect quest → COLLECTING |
| | | Stuck 15s → retry (max 5 lần) |
| `TALKING_NPC` | Đang trong NPC dialog | Dialog đóng + quest đổi → PATHFINDING |
| | | Dialog đóng + quest hết → IDLE (+1 quest) |
| `KILLING` | Đang đánh quái | Kill done (X/X) → RETURNING |
| | | Quest text đổi → PATHFINDING |
| `COLLECTING` | Đang thu thập | Quest đổi → PATHFINDING |
| | | Quest hết → IDLE (+1 quest) |
| `RETURNING` | Quay về NPC trả quest | NPC dialog mở → TURNING_IN |
| | | Stuck 15s → retry |
| `TURNING_IN` | Đang trả quest | Dialog đóng → IDLE (+1 quest) |

## Stuck Detection

- **Trigger**: Đứng yên > 15 giây trong PATHFINDING hoặc RETURNING
- **Action**: Force re-pathfind (click lại ShortMissionPanel)
- **Max retries**: 5 lần → reset về IDLE
- **Timeout**: 2 phút/state (4 phút cho KILLING)

## Log Format

Mỗi lần chuyển state, ghi vào State Log:
```
[19:05:00.123] | ACTION: FARM: IDLE → PATHFINDING | Có quest mới → bắt đầu pathfind | Quests: 5 | Time: 300s | Pos:23.8,7.6,-10.0 | Location:Ngôi Làng Oloong | Quest:Tiêu diệt Khủng Long (0/2)
```

## Code Location

- **Enum**: `FarmState` — đầu file `BotController.cs`
- **Fields**: `_farmState`, `_farmStateTimer`, `_farmStuckTimer`, etc.
- **Update()**: Gọi `UpdateFarmState()` mỗi frame
- **Method**: `UpdateFarmState()` — logic chuyển state
- **Helper**: `SetFarmState()` — chuyển state + log
