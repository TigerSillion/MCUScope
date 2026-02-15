# ICS++ 协议与 UART 对接详细指南

## 1. 项目概述

本项目使用的是 **Desk Top Laboratories Inc.**（日本桌面实验室公司）开发的 **ICS++（In Circuit Scope Plus）** 系统。它是一个面向电力电子领域的**实时变量波形查看工具**，功能类似于示波器，但观测的是 MCU 内部全局变量的实时波形。

PC 端软件为 **DTLScope.exe**（基于 .NET Framework 4.5 / WPF 开发）。

---

## 2. ICS++ 协议是否开源？

### 明确答案：协议和库均不开源

根据官方库手册（P30002-A2-001）第 3.1 节原文：

> **"ICS++ library source code and the communication protocol are not disclosed."**
> （ICS++ 库源代码和通信协议不公开。）

具体来说：

| 内容 | 开源状态 | 说明 |
|------|---------|------|
| 通信协议规范 | **不公开** | 协议帧格式、握手机制等均未公开 |
| MCU 端库源码 | **不公开** | 只提供编译好的 `.lib` + `.h` 文件 |
| PC 端软件源码 | **不公开** | 只提供编译好的 `.exe` + `.dll` 文件 |
| 库函数 API | **公开** | 头文件中声明了两个函数供用户调用 |

### 你能使用的 API（仅两个函数）

```c
// 初始化函数
void ics2_init(void* addr, char port, char level, char speed, char mode);

// 数据传输函数（放在周期性中断中调用）
void ics2_watchpoint(void);
```

### 库文件组成

每个 CPU 系列对应一组库文件：
```
ics2_<CPUNAME>.h      // 头文件（定义端口宏、函数声明）
ics2_<CPUNAME>.lib    // 预编译库文件（闭源）
```

---

## 3. 系统硬件架构

### 完整连接链路

```
Target CPU ──UART──> Target I/F Unit ──光纤──> ICS++ Unit(W2002) ──USB──> PC(DTLScope)
  (你的MCU)        (目标接口板)            (ICS++主机)              (上位机软件)
```

### 你的场景：仅有 UART

如果你没有 ICS++ 硬件（W2002 主机 + Target I/F 接口板），而只有一个 UART 口想对接 DTLScope GUI，那需要理解以下关键点：

**DTLScope.exe 不是通用串口调试工具。** 它通过 USB 连接 ICS++ 硬件，ICS++ 硬件再通过光纤/电信号转接到你的 MCU 的 UART。PC 端不直接和 MCU 的 UART 通信。

通信路径实际上是：
```
PC (USB) <--> ICS++ Board <--> User CPU (UART)
   通信路径A          通信路径B
```

---

## 4. "8 倍速率"是怎么回事？

### 这不是 UART 的 8 倍速——而是 ICS++ 硬件的时钟设置规则

根据库手册第 2.3-2.7 节，ICS++ 的通信速率计算公式为：

```
Communication_Rate = PCLKB / (8 × (speed + 1))  [Mbps]
```

其中：
- **PCLKB** = 目标 CPU 的外设时钟频率
- **speed** = 整数参数（≥0），在 `ics2_init()` 的第4个参数中设置

### "8倍"的含义

在配置 ICS++ 硬件（W1004/W2002 等）时，需要在 DTLScope 的 `Settings -> Communication Settings` 中设置一个时钟频率值。这个值必须是**通信速率的 8 倍**。

> 原文：*"Please set the frequency which is 8 times the communication rate with PC software (DTLScope.exe)."*

例如：
| 通信速率 | DTLScope 中设置的时钟 |
|---------|---------------------|
| 1 Mbps  | 8 MHz              |
| 2.5 Mbps | 20 MHz            |
| 5 Mbps  | 40 MHz             |

对于旧型号 W1003，这个 8 倍关系体现在硬件上——需要更换板上的晶振，晶振频率 = 通信速率 × 8。

### 实际速率范围（以 W2002 为例）

| 参数 | 值 |
|------|---|
| 支持速率范围 | 0.5 Mbps ~ 8 Mbps |
| 最大记录长度 | 262143 points |
| 最小采样周期 | 20 μs |
| 最大采样周期 | 10 ms |
| 最大波形通道 | 12 ch |

### 以 RX23T（PCLKB = 40MHz）为例

| speed 参数 | 实际通信速率 |
|-----------|------------|
| 0 | 5 Mbps |
| 1 | 2.5 Mbps |
| 2 | 1.67 Mbps |
| 3 | 1.25 Mbps |
| 4 | 1 Mbps |
| 5 | 0.833 Mbps |

---

## 5. 如何仅用 UART 对接此 GUI

### 方案分析

由于 ICS++ 协议不公开，你有以下几种可行路径：

### 方案 A：购买 ICS++ 硬件（推荐，如果预算允许）

这是正规方式。你需要：
1. **ICS++ W2002 主机**
2. **Target I/F 接口板**（分 5V 和 3.3V 两种）
3. **光纤线缆**（2m）
4. **USB 线缆**

在 MCU 端：
1. 将 `ics2_<CPU>.h` 和 `ics2_<CPU>.lib` 加入工程
2. 初始化时调用 `ics2_init()`
3. 在周期性中断（如载波中断）中调用 `ics2_watchpoint()`
4. ICS++ 库会自动占用 MCU 的 1 个 UART + DMA/DTC 资源

MCU 端代码示例（以 RX23T 为例）：
```c
#include "ics2_RX23T.h"

// DTC 向量表（需位于 RAM 中，低12位为0）
#pragma section B BDTCVectorTable
unsigned long dtc_vector_table[256];
#pragma section

void main(void) {
    // ... 其他初始化 ...

    // 参数说明：
    // dtc_vector_table: DTC 向量表首地址
    // ICS_SCI1_PD3_PD5: 使用 SCI1，PD3(TXD)、PD5(RXD)
    // 6: 中断优先级
    // 0: speed=0, 即 PCLKB/(8×1) = 5Mbps @40MHz
    // 1: mode 1, 32bit 8ch 两次传输模式
    ics2_init((void*)dtc_vector_table, ICS_SCI1_PD3_PD5, 6, 0, 1);
}

// 在周期性中断中调用
void carrier_interrupt(void) {
    // ... 你的控制代码 ...
    ics2_watchpoint();  // 传输变量数据给 ICS++
}
```

### 方案 B：逆向协议（技术难度高，不推荐）

由于协议不公开，理论上可以用逻辑分析仪抓取 MCU UART 口的数据来逆向分析帧格式，但：
- 协议涉及校验和（CheckSum + Complemental CheckSum）
- 有 NAK/ACK 握手机制
- 有多种传输模式（mode 0~6）
- 工作量大且无法保证完整性

### 方案 C：开发自己的上位机（如果你不需要 ICS++ 硬件）

如果你的目标只是通过 UART 查看 MCU 内部变量波形，**不一定非要用 DTLScope**。你可以：

1. 自定义一个简单的串口协议
2. MCU 端周期性将变量值通过 UART 发送
3. PC 端用 Python（PySerial + Matplotlib）或其他工具接收并绘图

简单示例协议：
```
帧头(2B) | 通道数(1B) | 数据(N×2B或N×4B) | 校验(1B) | 帧尾(1B)
0xAA 0x55 |   0x04    |   ch1~ch4 数据    |   XOR   |  0x0D
```

---

## 6. MCU 端资源占用

ICS++ 库在 MCU 端会占用以下资源（以 RX23T 为例）：

| 资源 | 用途 |
|------|------|
| SCI（UART）× 1 通道 | 与 ICS++ 硬件的串口通信 |
| DTC × 1 | 自动搬运 UART 发送数据（减轻 CPU 负担） |
| SCI RXI 中断 | 接收 PC 端命令 |
| SCI TXI 中断 | 触发 DTC 传输 |
| 对应 GPIO 引脚 | TXD / RXD |

### 调用周期限制

`ics2_watchpoint()` 的调用间隔有严格限制：

**W2002 系列：**
```
最小调用间隔 = 180 / Communication_Rate[Mbps] + 30 [μs]
```

| 通信速率 | 最小调用间隔 |
|---------|------------|
| 1 Mbps | 210 μs |
| 5 Mbps | 66 μs |

**最大调用间隔：5 ms**（所有型号通用）

---

## 7. 传输模式选择

| 模式 | 位宽 | 通道数 | 传输次数 | 适用 CPU |
|------|------|-------|---------|---------|
| Mode 0 | 8/16bit | 8ch | 1次 | 16bit CPU (RL78) |
| Mode 1 | 8/16/32bit | 8ch | 2次 | 32bit CPU (RX) |
| Mode 2 | 8/16/32bit | 4ch | 1次 | 32bit CPU (RX) |
| Mode 3 | 8/16/32bit | 12ch | 3次 | 32bit CPU (仅W2002) |
| Mode 4 | 8/16bit | 15ch | 2次 | 16bit CPU (RL78) |
| Mode 6 | 16bit only | 8ch | 1次 | 16bit CPU (RL78) |

- 16bit CPU（如 RL78 系列）：使用 Mode 0/4/6
- 32bit CPU（如 RX 系列）：使用 Mode 1/2/3

---

## 8. 支持的 CPU 列表

### 16bit 设备
RL78/G1F, RL78/G14, RL78/F14, RL78/F13, RL78/G12, RL78/G13, 78K0R/IC3, 78K0R/ID3, 78K0R/IE3

### 32bit 整数型设备
RX111, RX210, RX220, V850/IA3, V850/IA4, V850E2/IG3, V850E/FJ3, SH7047, SH7146, SH7149, SH7237

### 32bit 浮点支持设备
RX13T, RX23T, RX24T, RX26T, RX62T, RX63T, RX64M, RX66T, RX631, RX71M, RX72T, RH850/F1K-S1, RZ/T1, V850E2M/FJ4, V850E2/ML4, SH7216, SH7239

---

## 9. 总结建议

1. **DTLScope 不能直接通过 UART 与你的 MCU 通信**——它需要 ICS++ 硬件作为中间桥接
2. **"8倍速率"是 ICS++ 硬件时钟配置规则**，即硬件时钟 = 通信波特率 × 8
3. **协议完全不公开**，无法自行实现兼容的通信层
4. 如果你有 ICS++ 硬件，MCU 端只需调用两个函数（`ics2_init` + `ics2_watchpoint`）
5. 如果你没有 ICS++ 硬件且只想用 UART 看波形，建议自行开发简易上位机或使用开源替代方案
