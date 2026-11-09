# Treasure Ledger

FFXIV 卫月（Dalamud）插件：宝物库挖宝的掉落统计与账本。

从 ACT 的 Triggernometry 触发器脚本迁移而来，参考脚本保存在 [`reference/`](reference/)。

## 功能

- **掉落统计**：按宝图类型分类累计，覆盖 G8 / G10 / G12 / G14-G15 / G17-G18 / 深层绿 / 运河
- **三种来源分离计数**：小怪掉落、宝箱开启、投掷分配，宝箱与投掷自动去重
- **物品开关**：默认全部统计，可逐项关闭
- **自定义指令**：查询指令与重置指令均可改名
- **多频道播报**：跨服贝 / 本服贝 / 小队 / 默语（默认仅默语）
- **进队重置**：加入小队、开招募时自动清零
- **统计面板**：金币 / 图数 / 洞数 / 底数，附来源拆分

## 指令

| 默认指令 | 作用 |
|---|---|
| `/挖宝查询` | 在聊天栏输出本次统计 |
| `/挖宝重置` | 清零本次统计 |

两个指令都能在插件面板里改名。

## 安装

1. 游戏内 `/xlplugins` → 开发插件 → 添加
2. 目录指向编译输出的 `TreasureLedger.dll`
3. 或直接把 `TreasureLedger.dll` + `TreasureLedger.json` 放进 `%APPDATA%\XIVLauncherCN\devPlugins\TreasureLedger\`

## 编译

需要 .NET 10 SDK 与 `Dalamud.NET.Sdk/15.0.0`。

```bash
cd src/TreasureLedger
dotnet build -c Release
```

## 数据来源

掉落直接从游戏的 `LogMessage` 事件取，不解析聊天文本，参数里就是 ItemId：

| LogMessageId | 含义 | 参数 |
|---|---|---|
| 1232 | 获得了"物品" | `[0]` ItemId |
| 1233 | 获得了"物品"×N | `[0]` ItemId, `[1]` 数量 |
| 1254 | 获得了新的战利品（投掷） | `[0]` ItemId, `[1]` 数量 |
| 1258 | 获得了 N 金币 | `[0]` 金额 |
| 2165 | 获得了 N 个神典石 | `[0]` 数量 |

副本用 `ClientState.TerritoryType` 判定。这些编号与 ACT 通用，已实测 1279 = 巡梦金库。

## 状态

第一版进行中。已完成：掉落捕获、去重、物品开关、指令、频道播报、进队重置、配置界面。

待做：长期存储与历史查询、标记类辅助功能（本地标点 / 寻宝标记 / 转盘提醒 / 传送提醒 / 下底自动退出）。

## 致谢

格式与判定规则源自 Triggernometry 触发器「挖宝会计」。
