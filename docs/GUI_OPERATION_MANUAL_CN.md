# MCUScope GUI 操作使用手册（中文）

## 1. 文档信息

- 文档名称：MCUScope GUI 操作使用手册（中文）
- 适用 PC 工程：`src/MCUScope`（WPF/.NET 8）
- 适用 MCU 工程：`Reference/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100`
- 基线协议：`docs/UART_PROTOCOL.md`（`0xAA 0x55` 帧头 UART 协议）
- 当前手册对应日期：2026-02-16

本手册面向现场调试、联调和维护人员，目标是按“可直接上手”的方式说明：

1. GUI 如何连接 MCU。
2. 如何做变量读写和软件示波。
3. 当前版本哪些功能已实现，哪些仍是占位实现。
4. 下一步改进计划和验收标准。

---

## 2. 系统总体说明

### 2.1 架构

- PC 侧：`MCUScope.exe`（WPF）
- 传输层：UART（串口）
- MCU 侧：`ICS2_RX26T.c` 负责协议解析、按地址变量读写、波形上传

数据流：

1. GUI 发送命令帧（GetInfo/Read/Write/StartScope 等）。
2. MCU 在 `ics2_watchpoint()` 中解析命令并执行。
3. MCU 返回变量值（`0x83`）或波形数据（`0x84`）。
4. GUI 刷新 Watch 区和示波图。

### 2.2 当前 MCU 通信关键参数

- 串口：SCI6 (`P81/TXD6`, `P80/RXD6`)
- 中断优先级：`ICS_INT_LEVEL = 4`
- BRR：`ICS_BRR = 4`（约 1 Mbps，PCLKB=40MHz）
- 参考文件：
  - `Reference/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100/src/application/mcu/rx26t/r_app_mcu.h`
  - `Reference/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100/src/application/user_interface/ics/ICS2_RX26T.c`

---

## 3. 运行准备

### 3.1 PC 侧

1. 安装 .NET 8 SDK。
2. 在仓库根目录执行：

```bash
dotnet build MCUScope.sln
dotnet run --project src/MCUScope/MCUScope.csproj
```

### 3.2 MCU 侧

1. 确认固件已编译并烧录。
2. 串口线连接到 SCI6 对应引脚（P81/P80）。
3. 串口参数：`1000000, 8N1, 无流控`。

### 3.3 快速联通自检（建议）

可先运行脚本做协议自检：

```bash
python tools/uart_smoke.py --port COM5 --baud 1000000 --scope --scope-var 0:0x00001829:u8 --scope-var 1:0x00007650:f32
```

---

## 4. GUI 主界面说明

界面由 5 个区域组成：菜单栏、工具栏、左侧 Watch、中间图表、右侧 Scope Controls。

### 4.1 菜单栏

- `File`
  - `New/Open/Save/Save As`：工程文件（`.mcuproj`）管理
  - `Load Variables/Update Variables`：加载变量文件（`.csv/.map/.sym/.xml`）
  - `Save Chart Data/Load Chart Data`：波形文件（`.dtlcd`）保存/加载
- `Settings`
  - `Variables Settings`：变量属性编辑窗口（当前为部分实现）
  - `Communication Settings`：串口波特率与基准时钟设置
- `Tools`
  - `Array Editor`
  - `Custom Control Panel`
  - `Timetable Player`
- `Help`
  - `使用说明（中文）`：直接打开本手册（`docs/GUI_OPERATION_MANUAL_CN.md`）
  - `About MCUScope`

### 4.2 工具栏

- COM 口下拉框
- `Connect` / `Disconnect`
- `Update Variables`

### 4.3 左侧 Watch 区

- 最多 24 行 Watch 变量。
- 列说明：
  - `Name`：变量名下拉（来自变量文件）
  - `R`：是否参与批量读
  - `Read Value`：读回值
  - `W`：是否参与批量写
  - `Write Value`：待写入值
- 按钮说明：
  - `Read`：对勾选 R 的变量逐条发起读请求
  - `Write`：对勾选 W 的变量逐条发起写请求
  - `Auto Read`：按微秒周期自动执行 Read
  - `us` 输入框：最小支持 `1 us`

### 4.4 中间图表区

- `Scope Chart`：主波形
- `Zoom`：缩放波形
- `FFT`：频谱图
- 下方 `Scope Values` 表显示每个通道的 `Min/Max/Average` 等

### 4.5 右侧 Scope Controls

- `Run/Stop`：启动/停止采样
- `Time`：`Mode`, `Sec/Div`, `Sample`, `Length`
- `Trigger`：`Position`, `Level`, `Source`, `Mode`, `Edge`
- `Channel`：单通道显示参数（Val/Div、Position、Offset、Visible）
- `Cursor`：X1/X2/Y1/Y2 开关
- `Zoom`：缩放参数
- `Save`：波形保存/加载，Auto Save
- `FFT`：开关、窗函数、幅值标度

---

## 5. 标准操作流程

### 5.1 连接 MCU

1. 打开 `Settings -> Communication Settings`。
2. 设置 `Baud Rate`：
   - 可直接输入任意正整数（例如 `1000000`、`1500000`）。
   - 也可从预置列表快速选择常用波特率。
3. 选择 COM 口，点击 `Connect`。
4. 状态栏应显示 `Connected, CPU:RX26T...`。

说明：

- GUI 优先使用 `Baud Rate`。
- 若 `Baud Rate` 为空/0，则按 `BaseClockMHz * 1e6 / 8` 推算波特率。

### 5.2 加载变量

1. 点击 `File -> Load Variables`。
2. 选择 `.map/.sym/.csv/.xml` 之一。
3. 加载成功后，弹窗会显示“已加载变量数量”。
4. 可在以下窗口看到变量变化：
   - 左侧 Watch `Name` 下拉框
   - 中间 Scope Values `Name` 下拉框
   - `Settings -> Variables Settings` 变量列表（地址/类型/缩放等）

### 5.3 变量读取

1. 在 Watch 表中选择变量并勾选 `R`。
2. 点击 `Read`。
3. 查看 `Read Value` 更新。

### 5.4 变量写入

1. 勾选 `W`，填写 `Write Value`。
2. 点击 `Write`。
3. 建议随后执行一次 `Read` 校验写入结果。

### 5.5 软件示波器

1. 在 `Scope Values` 中选择要显示的通道并设为 `Visible=true`。
2. 每个通道可配置 `Val/Div`, `Position`, `Offset`。
3. 设置 `Sec/Div`, `Sample`。
4. 点击 `Run`，收到数据后图形刷新。
5. 点击 `Stop` 结束。

### 5.6 波形数据保存与回放

- 保存：`File -> Save Chart Data` 或右侧 `Save`
  - `.dtlcd`：JSON 格式完整数据
  - 同时可导出 CSV（时间列 + 通道列）
- 加载：`File -> Load Chart Data`

---

## 6. MCU 变量访问机制（当前固件）

说明：

- 当前固件已取消固定白名单。
- GUI 按“变量地址 + 类型”直接发起读写请求。
- 为避免访问无效地址导致异常，MCU 会做 RAM 区间校验（可在 `ICS2_RX26T.c` 宏中调整）。
- 实际读写对象由你加载的 `.map/.sym/.csv/.xml` 变量文件决定。

推荐做法：

1. 始终从最新 map/sym 文件加载变量，不要手工猜地址。
2. 写入前先读一次，确认类型和地址匹配。
3. 对控制变量先小步修改，再观察反馈量。

---

## 7. 示波通道变量自由绑定（当前固件）

当前版本已支持“通道自由选变量”：

- GUI 每个通道（M1..M12）可独立选择任意变量名。
- 启动采样时，GUI 会把每个可见通道对应的 `slot + type + address` 发送给 MCU。
- MCU 按通道配置实时采样，不再绑定固定内置信号表。

补充：

- GUI 发起采样时，MCU `record_length` 会被限制在 `8..256`。
- GUI 若设置更大长度，MCU会自动截断到 256。

---

## 8. 各工具窗口使用说明（现状）

### 8.1 Variable Settings

- 入口：`Settings -> Variables Settings`
- 目标：修改类型、缩放、读写权限、别名、注释
- 当前状态：已实现完整回写。点击 `OK` 后会立即更新 Watch/Scope 使用的变量定义和显示名。

### 8.2 Array Editor

- 入口：`Tools -> Array Editor`
- 提供网格和 CSV 导入导出。
- 当前状态：`READ/WRITE` 仅弹窗提示，尚未真正下发顺序读写命令。

### 8.3 Custom Control Panel

- 入口：`Tools -> Custom Control Panel`
- 可添加 Slider/Toggle/Display 控件。
- 当前状态：控件 UI 可创建，但读写动作是占位注释，未真正调用协议层。

### 8.4 Timetable Player

- 入口：`Tools -> Timetable Player`
- 可加载 CSV 并按时间推进播放。
- 当前状态：播放时界面与行高亮会更新，但变量写入代码仍是占位注释。

---

## 9. 常见问题与排查

### 9.1 无法连接

1. 检查 MCU 固件是否运行并启用了 SCI6。
2. 检查 COM 口是否被其他工具占用。
3. 确认波特率与 MCU 一致（推荐 `1000000`）。
4. 观察状态栏是否收到 `GetInfo` 反馈。

### 9.2 变量读写失败

1. 检查变量地址是否来自当前固件 map/sym（旧地址会失败）。
2. 检查读写类型是否匹配（如 `u8/i16/f32`）。
3. 检查目标地址是否在 MCU 允许的 RAM 区间内。
4. 建议先用 `uart_smoke.py` 对同一变量做读写验证。

### 9.3 示波图无数据

1. 至少一个通道 `Visible=true` 且有名称。
2. 串口已连接且 MCU 在运行控制循环。
3. `SamplePeriod/RecordLength` 在 MCU支持范围内。
4. 检查该通道变量地址/类型是否有效（建议先在 Watch 中可读再上示波）。

### 9.4 加载 MAP 后变量窗口无变化

1. 先看加载成功弹窗中的数量是否大于 0。
2. 若数量为 0，说明 MAP 格式可能与解析规则不匹配，建议先导出 CSV 再加载。
3. 若数量大于 0 但下拉框未刷新，先重新打开 `Variables Settings` 窗口。
4. 确认加载的是当前固件构建输出的最新 `.map`，不是旧版本。

---

## 10. 当前不足与后续改善计划

以下为根据当前源码梳理出的缺口与优先级计划。

### 10.1 P0（必须优先完成，影响核心可用性）

| ID | 问题 | 影响 | 目标验收 |
|---|---|---|---|
| P0-3 | Trigger 参数 MCU 仅存储未参与触发判定 | 触发设置对采样行为无效 | 实现 Auto/Single/Normal 与边沿/阈值判定 |
| P0-4 | Array Editor 读写未落地 | 无法用于数组在线调参 | 支持连续地址读写与批量 ACK/NACK 反馈 |
| P0-5 | Custom Control Panel 未接协议 | 自定义控件不可控机 | Slider/Toggle/Display 全部接入读写 API |
| P0-6 | Timetable Player 未真正下发写命令 | 时序脚本无法驱动 MCU | 播放周期内可稳定写入并记录错误行 |

### 10.2 P1（功能完善与易用性）

| ID | 问题 | 影响 | 目标验收 |
|---|---|---|---|
| P1-1 | Cursor 开关存在但无真实光标计算 | 无法做 dX/dY 测量 | 图上可拖拽光标并更新 `CursorInfoText` |
| P1-2 | `TimeMode=Roll` 无独立实现 | 与 Buffer 模式体验一致 | 实现滚动示波显示策略 |
| P1-3 | FFT Source/Scope 选项过少 | 频谱分析维度受限 | Source 支持按通道筛选；Scope 支持主图/缩放源选择 |
| P1-4 | 写入结果无明确 ACK/NACK 呈现 | 调参失败定位慢 | 在状态栏/日志显示每次写入结果 |
| P1-5 | `1 us` 轮询会造成高CPU占用（设计上允许） | 高频轮询可能影响PC实时性 | 增加“速率限制/负载告警”开关并默认启用 |
| P1-6 | MAP 解析规则仍依赖编译器输出格式 | 部分工程会出现“加载0变量” | 增加 MAP 解析模板和格式诊断提示 |

### 10.3 P2（工程质量与自动化）

| ID | 问题 | 影响 | 目标验收 |
|---|---|---|---|
| P2-1 | 缺少 GUI 协议层单元测试 | 回归依赖手工联调 | 为打包/解包、变量编码、波形解析补充单测 |
| P2-2 | 缺少工具窗口的端到端自动回归 | 升级后易出现隐藏退化 | 增加串口仿真器 + UI 自动化回归脚本 |
| P2-3 | 运行日志粒度偏少 | 现场问题追踪慢 | 增加按会话保存通信日志与错误统计 |

---

## 11. 建议的执行顺序（落地节奏）

1. 先做 P0-1/P0-3，保证“变量配置-触发-采样”链路真实可用。
2. 再做 P0-4/P0-5/P0-6，补齐 Tools 三个窗口的真实控制能力。
3. 完成 P1（Cursor/Roll/FFT/ACK 展示）提升调试效率。
4. 最后做 P2（自动化与日志）保证持续迭代稳定。

---

## 12. 与其它文档的关系

- 协议细节：`docs/UART_PROTOCOL.md`
- MCU 变量/算法调试：`docs/MCU_CODE_AND_MOTOR_DEBUG_GUIDE.md`
- 常用命令：`docs/USAGE.md`
- 回归测试：`docs/TESTING.md`

本手册以“GUI操作”为主，电机算法调试细节请配合 `docs/MCU_CODE_AND_MOTOR_DEBUG_GUIDE.md` 使用。
