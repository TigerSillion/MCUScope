# MCUScope v2.0

跨平台 MCU 示波器与信号分析器。基于 Web 技术构建，提供现代化的深色主题 UI、实时波形渲染和专业级信号分析功能。

## 相比 DTLScope v1.6 的改进

### 性能提升
- **Hardware-accelerated Canvas 渲染**：使用 HTML5 Canvas 2D 实现高性能波形绘制
- **WebSocket 实时推送**：低延迟数据传输，支持 20+ fps 刷新率
- **异步架构**：基于 FastAPI + asyncio，非阻塞 I/O
- **高效 DSP**：使用 NumPy/SciPy 进行信号处理，比纯 C# 实现更快

### 功能增强
- **8 种 FFT 窗函数**：Hamming, Hanning, Blackman, Blackman-Harris, Flat Top, Kaiser 等
- **数字滤波器**：支持低通、高通、带通、带阻 Butterworth 滤波器
- **数学通道**：支持自定义表达式 (sin, cos, sqrt, diff, cumsum 等)
- **高级触发**：上升沿/下降沿/双边触发，支持 holdoff
- **信号测量**：Min, Max, Mean, RMS, Vpp, 频率, 周期, 占空比
- **双光标系统**：X/Y 双光标，支持拖动，自动计算 Δt 和频率
- **模拟器模式**：无需硬件即可测试，支持 8 种信号类型
- **跨平台**：支持 Windows, macOS, Linux

### 外观改进
- **现代深色主题**：专业级示波器风格暗色 UI
- **可折叠面板**：灵活的三栏布局
- **实时参数显示**：FPS、采样率、样本数状态栏
- **波形辉光效果**：模拟真实示波器的荧光效果
- **自适应布局**：支持各种屏幕尺寸

## 快速开始

```bash
# 安装依赖
cd mcuscope-app
bash setup.sh

# 启动应用
python3 run.py
```

浏览器会自动打开 http://127.0.0.1:8080

## 使用方法

### 基本操作
- **Run/Stop**：开始/停止数据采集（快捷键：Space）
- **Single**：单次采集
- **FFT**：切换频谱分析显示（快捷键：F）
- **Cursors**：切换光标测量（快捷键：C）
- **1-8 数字键**：切换对应通道显示
- **鼠标滚轮**：缩放时间轴

### 连接设备
点击工具栏 "Connect" 按钮，输入串口号和波特率。支持 ICSP 协议兼容设备。

### 模拟器模式
未连接设备时自动进入模拟器模式，生成 4 通道测试信号：
- CH1: 50Hz 正弦波 (3.3Vpp)
- CH2: 100Hz 方波 (5V, 60% 占空比)
- CH3: 25Hz 三角波 (2.5Vpp)
- CH4: 复合信号 (30+90+150Hz)

## 项目结构

```
mcuscope-app/
├── backend/
│   ├── server.py          # FastAPI 主服务器 + WebSocket
│   ├── serial_manager.py  # 串口通信管理
│   ├── protocol.py        # ICSP 协议编解码
│   ├── dsp.py             # DSP 引擎 (FFT, 滤波, 测量)
│   └── simulator.py       # 信号模拟器
├── frontend/
│   ├── index.html         # 主页面
│   ├── css/style.css      # UI 样式
│   └── js/
│       ├── scope-renderer.js  # Canvas 波形渲染器
│       └── app.js             # 应用控制器
├── requirements.txt
├── setup.sh
└── run.py                 # 启动脚本
```

## 技术栈

- **后端**: Python 3.11+, FastAPI, WebSocket, NumPy, SciPy, PySerial
- **前端**: HTML5 Canvas, Vanilla JavaScript (零依赖)
- **通信**: WebSocket 实时双向通信
- **协议**: ICSP (兼容 ScopeBox 设备)
