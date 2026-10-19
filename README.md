# LTE Single Link Anti-Jamming Measurement （LTESingleLinkAJM）

> LTE 单线通信抗干扰全自动测试系统
> 通过 R&S CMW500 综测仪向被测设备（DUT）施加 LTE 干扰，同时联动控制 RIGOL MHO5104 示波器
> 自动搜索、定位并抓取被测设备单线总线（single-wire bus）上的 TX / RX 通信波形。

一套面向产线 / 实验室的 Windows 桌面自动测试软件：在一整张 LTE 频段测试计划上逐信道
建立蜂窝连接、施加干扰、自愈掉线，并在每个信道自动截取 TX、RX 两张波形图并落盘存档，
全程无人值守。

---

## 目录
- [运行环境](#运行环境)
- [功能特性](#功能特性)
- [系统架构](#系统架构)
- [硬件与连接](#硬件与连接)
- [目录结构](#目录结构)
- [环境要求](#环境要求)
- [构建与发布](#构建与发布)
- [使用说明](#使用说明)
- [核心测试流程](#核心测试流程)
- [关键技术要点](#关键技术要点)
- [波形寻波算法（v4）](#波形寻波算法-v4)
- [产出物](#产出物)
- [文档与参考](#文档与参考)
  
---

## 运行环境

- Windows 10/11 x64
- NI-488.2 驱动（GPIB 通信必需）——[NI 官网下载](https://www.ni.com/en/support/downloads/drivers/download.ni-488-2.html)
- 无需安装 .NET 运行时（自包含单文件）

---

## 功能特性

- **全自动多信道测试**：内置默认测试计划覆盖 22 个 LTE 频段（FDD + TDD）共 68 条信道，
  每条 Low/Mid/High 逐一执行，可勾选 / 全选任意子集。
- **FDD ⇄ TDD 双工自动切换**：同双工只换频段/信道保持连接；跨双工自动走「小区下电 →
  切 DMOD → 设频段 → 小区上电」序列，规避固件的 `-220 / -221 / -113` 报错。
- **RRC 连接态监护与自愈**：入场与寻波期间轮询 RRC 状态；IDLE / 掉线时自动
  `LTE OFF/ON`，必要时联动 SIM Simulator 发送 AT 复位序列，16 轮阶梯恢复失败才跳过信道。
- **免触发结构化寻波（v4）**：ROLL 自由滚动 + 面积相对下降定格 + 深存储记录内结构化搜索，
  左右耳通用，不依赖示波器本地设置文件，自动定位 TX / RX 并卡光标测量有效时长。
- **截图后核验**：以测量面板面积区间对 TX / RX 交叉验证，未过则弃图重采，保证图不「抽奖」。
- **防待机保护**：测试期间阻止系统进入 Modern Standby，避免 GPIB-USB 适配器被节电断电导致会话丢失。
- **可视化 WPF 界面**：仪器连接状态、测试计划表、进度条、实时日志、SCPI 原文日志导出、
  暂停/继续/停止控制、自定义无边框深色主题。
- **单文件自包含发布**：ReadyToRun + Self-Contained + 单文件压缩，目标机无需安装 .NET 运行时。

---

## 系统架构

采用 MVVM 架构（WPF），业务逻辑通过接口注入，便于替换与测试。

```
 View (MainWindow.xaml)
        │  绑定
 ViewModel (MainViewModel)
        │  编排
 AutomationRunner  ──────────────┐
        │                        │
        ▼                        ▼
 ICmw500Service          IOscilloscopeService        ISimSimulatorService
 (Cmw500Service)         (OscilloscopeService)       (SimSimulatorService)
   R&S CMW500              RIGOL MHO5104               SIM Simulator
   GPIB / VISA             raw TCP :5025               USB CDC (串口)
```

| 层 | 主要类型 | 职责 |
|---|---|---|
| View | `Views/MainWindow.xaml(.cs)`、`App.xaml`、`Converters/` | 界面、无边框窗口、状态/颜色转换器 |
| ViewModel | `ViewModels/MainViewModel`、`RelayCommand`、`ObservableObject` | 命令绑定、连接控制、日志聚合、进度统计 |
| 编排 | `Services/AutomationRunner` | 逐条测试主控：BAND→RRC→寻波→自愈→下一条 |
| 仪器服务 | `Services/Cmw500Service` | CMW500 SCPI（VISA/GPIB）、双工切换、RRC 查询、小区循环 |
| 仪器服务 | `Services/OscilloscopeService` | MHO5104 raw TCP、寻波、光标测量、BMP→PNG 截图 |
| 仪器服务 | `Services/SimSimulatorService` + `SimSimulatorClient` | SIM Simulator 串口 AT 复位序列（私有 CRC16 帧） |
| 辅助 | `Services/KeepAwake` | 运行期间阻止系统待机 |
| 模型 | `Models/TestPlan`、`TestItem`、`EarSide`、`TestStatus` | 测试计划、单条测试项、耳侧、状态枚举 |

---

## 硬件与连接

| 设备 | 型号 | 连接方式 | 默认地址 | 说明 |
|---|---|---|---|---|
| 综测仪 | R&S CMW500（固件 V3.7.170） | VISA / GPIB | `GPIB0::20::0::INSTR` | LTE Signaling，施加干扰、建链、RRC 监护 |
| 示波器 | RIGOL MHO5104（固件 00.02.00） | raw TCP Socket | `192.0.2.10:5025` | 采集单线总线 TX/RX 波形；LXI 标准 SCPI 端口，不走 VXI-11 |
| SIM 模拟 | SIM Simulator | USB CDC 串口 | 自动识别，230400 8N1 | RRC 长时 IDLE 时发 AT 复位序列唤醒 DUT |

> 地址在 `MainViewModel` 的无参构造中给出默认值，可按需修改后重新构建。

---

## 目录结构

```
LTESingleLinkAJM/
├─ README.md                     ← 本文件
├─ .claude/
│  └─ WORKLOG.md                 ← 工作历史与关键事实（标定/协议）落盘记录
├─ src/                          ← C# WPF 主程序（.NET 10，独立 git 仓库）
│  ├─ App.xaml(.cs)              ← 应用入口、全局资源/主题
│  ├─ Views/MainWindow.xaml(.cs) ← 主界面
│  ├─ ViewModels/               ← MainViewModel / RelayCommand / ObservableObject
│  ├─ Services/                 ← 仪器服务与自动化主控
│  ├─ Models/                   ← 测试计划与数据模型
│  ├─ Converters/               ← WPF 值转换器
│  ├─ Properties/PublishProfiles/win-x64.pubxml ← 发布配置
│  ├─ LTESingleLinkAJM.csproj   ← 项目文件
│  ├─ CMW500_双工切换SCPI指令.md  ← FDD⇄TDD 切换实测指令手册
│  ├─ .github/workflows/release.yml ← 打 tag 自动构建单 EXE 发布
│  └─ *.py                      ← 抓波 Python 原型/诊断脚本（历史参考）
├─ docs/                         ← 需求与方案文档
│  ├─ TX_RX波形抓图方案.md        ← 早期（v8/触发法）抓图方案
│  ├─ LTE单线通信设置.xlsx        ← 频段/信道/命名约定
│  ├─ 设置.docx / TX波形.png      ← 设置说明与样例波形
│  └─ LTESingleLink_scpi_*.txt   ← 导出的 SCPI 交易日志样例
├─ patent/                       ← 专利技术交底书与配图
│  ├─ 一种通过控制综测仪CMW500…技术交底书(_v2).docx
│  ├─ figures/                  ← 交底书配图
│  ├─ make_figs.py / build_docx.py ← 配图与文档生成脚本
└─ research/                     ← 研究/原型代码（Python 抓波、示波器配置转储、C# 客户端）
```

> `src/bin/`、`src/obj/`、`src/.vs/` 为构建/IDE 产物，不纳入版本管理。

---

## 环境要求

- **操作系统**：Windows 10/11 x64
- **.NET SDK**：.NET 10（`net10.0-windows`），启用 WPF
- **NI-VISA Runtime**：CMW500 通过 `NationalInstruments.Visa` 直接 P/Invoke `visa32.dll`，
  目标机必须安装 NI-VISA runtime（不依赖 IVI 的 vendor-discovery）
- **GPIB 适配器**：如 NI GPIB-USB-HS（连接 CMW500）
- **网络**：与示波器同网段，可访问 `192.0.2.10:5025`
- **NuGet 依赖**（随构建自动还原）：
  - `NationalInstruments.Visa` 25.5.0.13
  - `System.IO.Ports` 9.0.0
  - `System.Management` 9.0.0
- NI-488.2 驱动（GPIB 通信必需）——[NI 官网下载](https://www.ni.com/en/support/downloads/drivers/download.ni-488-2.html)

---

## 构建与发布

在 `src/` 目录（含 `.csproj`）下执行：

```powershell
# 构建（Release，应 0 警告 0 错误）
dotnet build LTESingleLinkAJM.csproj -c Release

# 运行（调试期）
dotnet run --project LTESingleLinkAJM.csproj -c Release

# 发布：ReadyToRun + 自包含 + 单文件压缩（产物见 pubxml 的 PublishDir）
dotnet publish LTESingleLinkAJM.csproj -c Release -r win-x64 `
  /p:PublishProfile=Properties/PublishProfiles/win-x64.pubxml
```

- 发布产物为单个自包含 EXE，目标机**无需安装 .NET 运行时**。
- Release 配置下 `DebugType=None`（不产 PDB），并裁剪多余卫星语言资源以减小体积。
- 未启用 `PublishTrimmed`：裁剪会误删程序集元数据与 NI-VISA 的反射/P-Invoke 依赖。
- **CI**：向仓库推送 `v*` / `V*` 标签会触发 `.github/workflows/release.yml`，
  自动构建单文件 EXE（命名如 `LTESingleLinkAJMV1.0.exe`）并发布到 GitHub Releases。

---

## 使用说明

1. 连接好 CMW500（GPIB）、示波器（网口）、SIM Simulator（USB），并确保 DUT 就位。
2. 启动程序，分别点击 **Oscilloscope / CMW500 的 Connect**，状态灯变绿即连接成功。
   （示波器连接后会自动下发 ROLL 监视基线。）
3. 在 **Test Setup** 选择待测**耳侧**（左耳 / 右耳），并设置**图片保存目录**
   （留空默认 `D:\LTESingleLinkAJM`）。
4. 在 **Test Plan** 表格中勾选要测的信道（默认全选，可用表头 checkbox 全选/全不选）。
5. 点击 **▶ Start** 开始。运行中可 **‖ Pause / ▶ Resume**（在两条测试项边界暂停，不打断进行中的序列），
   或 **⏹ Stop** 停止。
6. 进度条与每行状态、TX/RX 抓取标记实时更新；**Log** 面板显示流程日志，
   可 **导出** 日志 / **导出 SCPI** 原文（调试超时等问题时用）/ **清空**。

---

## 核心测试流程

`AutomationRunner` 对每条勾选的 `TestItem` 依次执行：

1. **下发 BAND + 信道**：CMW500 按目标频段判断 FDD/TDD，同双工只换 BAND+EARFCN，
   跨双工走完整下电/切模式/上电序列（详见 `src/CMW500_双工切换SCPI指令.md`）。
2. **RRC 连接态检测**：
   - 立即查一次；IDLE → 先 `LTE OFF/ON` 并等 10s。
   - 每 10s 轮询一次，最多 16 轮（≈160s）。
   - 第 4/8/12/16 轮仍 IDLE → `LTE OFF/ON` + SIM AT 复位序列。
   - 第 16 轮恢复后再等 10s 做最终检查，仍 IDLE → 判失败，跳过该信道。
3. **connected → 立即寻波**：v4 免触发结构化定位（见下节），抓齐 TX 与 RX 两张图。
4. **寻波期间掉线自愈**：后台每 5s 探测 RRC，非 connected 立即停止寻波 → 恢复阶梯 →
   connected 后等 10s 再恢复寻波，只补抓缺的那张；单信道掉线超过 3 次则跳过。
5. **换下一条**。

失败/跳过的条目在表格行内以状态与 Message 体现，不中断整轮测试。

---

## 关键技术要点

以下为项目在真机上标定/踩坑得到的关键事实（详见 `.claude/WORKLOG.md`）：

- **示波器走裸 TCP :5025**（LXI 标准 SCPI 端口），二进制数据用 IEEE-488.2 定长块
  `#<n><len><data>` 按长度精确读取（数据内含 `0x0A`，不能按行读）。
- **BMP→PNG**：`:DISPlay:DATA?` 返回 BMP，用 WPF 内置 `PngBitmapEncoder` 转真 PNG 落盘。
- **面积测量 `MARea` 与光标无关**（区间为 MAIN 整屏），`|v|>1e30` 视为无效读数。
- **CMW500 老固件适配**：RRC 用 `FETC:LTE:SIGN1:PSW:STAT?`；TDD 上下行/特殊子帧用
  `CELL:ULDL` / `CELL:SSUBframe`（非 `CELL:TDD:*`，否则 `-113`）；改 DMOD 前必须小区下电。
- **VISA 自愈**：GPIB-USB-HS 被 Windows 节电挂起会致 `VI_ERROR_CONN_LOST`，
  服务层检测到连接丢失即标记会话待重建，下次 SCPI 调用自动重连。
- **KeepAwake**：Modern Standby 平台空闲约 1 分钟会强制移除 USB-GPIB，测试期间用
  `SetThreadExecutionState` 阻止系统待机。

---

## 波形寻波算法 v4

`OscilloscopeService` 的 v4 方法（2026-07-29 双耳实机标定），取代早期「边沿触发 + 峰峰值/面积判定」：

1. **监视态**：`:TIMebase:MODE ROLL` 50ms/div（整屏即 0.5s 深存储），纵轴 1V/div @ -1.72V，
   兼容左耳（空闲 ~1.8V）与右耳（空闲 ~5V）。不依赖触发，不依赖示波器本地设置文件。
2. **定格**：RUN 期间轮询整屏面积 `MARea`，相对本轮峰值明显下降（事件滚入屏幕）或 6s 超时 → `:STOP`。
3. **结构化搜索**：在定格的 0.5s 记录内，以 2~20ms「长放电低电平段」为锚（上限 20ms 排除复位长脉冲），
   按锚宽自适应分块（2ms/div，20µs/点，重叠防切边）覆盖 `[T0-21, T0+锚宽+9]ms`。
4. **深阈值取脉冲对**：阈值 `lo + 15%×(hi-lo)` 选出事件里最深的两个窄脉冲，位置直取
   （左 = TX，右 = RX；取事件后半段最后两个），只认含 ≥2 个脉冲对的事件。
5. **特写成图**：TX 20µs/div、RX 50µs/div 精居中，纵轴 500mV/div，光标 A/B 卡起止
   （ΔX 即单线通讯有效时长），刷新测量面板后截图。
6. **截图后核验**：面板面积 TX 200~300µV·s、RX 550~650µV·s，任一不过则弃图重采
   （VPP 仅随图记录，不参与判别）。

---

## 产出物

- **波形图片**：保存到用户指定目录（默认 `D:\LTESingleLinkAJM`），命名遵循 Excel 约定：


 <img width="800" height="600" alt="B1 18050 右耳RX波形" src="https://github.com/user-attachments/assets/7a9eb2e2-063a-4685-9a6d-c51514626ff4" />


- **日志**：界面 Log 可导出为 `.txt`；SCPI 收发原文可单独导出（`LTESingleLink_scpi_*.txt`）。

---

## 文档与参考
- `src/CMW500_双工切换SCPI指令.md` —— CMW500 FDD⇄TDD 切换的实测 SCPI 指令与硬约束。
- `docs/TX_RX波形抓图方案.md` —— 早期（触发法 v8）抓图方案，含端口/触发/判据踩坑记录。
- `docs/LTE单线通信设置.xlsx`、`docs/设置.docx` —— 频段/信道计划与命名约定、设置说明。
- `research/` —— Python 抓波原型、示波器配置转储、SIM 客户端等研究代码。

---

