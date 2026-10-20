using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LTESingleLinkAJM.Services
{
    /// <summary>
    /// Rigol MHO5104 via raw TCP socket (VISA equivalent: TCPIP::&lt;ip&gt;::5025::SOCKET)。
    /// 端口 5025 是 LXI 标准 SCPI 端口 (MHO5000 不走 VXI-11)。
    ///
    /// 寻波方法 v4 (左右耳通用 ROLL 结构化定位, 2026-07-29 双耳实机标定,
    /// 取代 v3 "边沿触发 + 面积判定 + 全景结对"):
    ///   - 完全自配置, 不再依赖示波器本地 lteajm* 设置文件, 不依赖触发:
    ///     ROLL 50ms/div 自由滚动, 屏幕即 0.5s 深存储; 纵轴 1V/div @ -1.72V
    ///     (左耳空闲 ~1.8V / 右耳空闲 ~5V 都在屏内)
    ///   - 定格判据与电平体系无关: RUN 期间轮询整屏面积 :MEASure:ITEM? MARea,
    ///     相对本轮峰值明显下降 (事件滚入屏幕) 或 6s 超时 → :STOP 定格
    ///   - 定格记录内结构化搜索 (阈值一律取记录 hi/lo 的相对比例, 2026-07-29 抗噪定型):
    ///     以 2~20ms 的"长放电低电平段"为锚 (上限 20ms 排除 ~29/56ms 复位长脉冲),
    ///     按锚宽自适应分块覆盖 [T0-21, T0+锚宽+9]ms (每块 2ms/div 20µs/点, 2ms 重叠;
    ///     实测 TX/RX 最远在 T0+19ms, 固定窗会截尾)。粗扫只做候选发现、不定角色 ——
    ///     LTE 干扰噪声与量化会撑大/压小粗扫宽度 (20µs/点是可靠测宽下限)。
    ///   - 深阈值 lo+15% 一步选中事件里最深的两个窄脉冲, 位置直取 (左=TX 右=RX,
    ///     取候选列表最后两个 —— TX/RX 位于事件后半段, 2026-07-29 阈值扫描定型);
    ///     只认含 ≥2 个脉冲对的事件, 0/1 个脉冲的相似事件跳过查下一个锚点。
    ///   - 放大居中截图后核验面板面积 (TX 200~300 / RX 550~650µV·s), 不过则弃图重采;
    ///     VPP 仅随图记录不参与判别。
    ///     左耳形态: 空闲1.8V → TX → RX → 长放电低电平;
    ///     右耳形态: 空闲5V → 跌至1.9V平台 → TX → RX → 长放电低电平 → 回5V。
    ///     两种形态的 TX/RX 本体完全一致, 该签名对两耳通用。
    ///   - 特写: TX 20µs/div, RX 50µs/div 精居中; 纵轴统一切 500mV/div @ -1V
    ///     (两耳突发局部均 0~1.9V, 波形占满大半屏), 光标 A/B 卡起止
    ///     (ΔX 即单线通讯有效时长), 刷新测量面板后截图; 截完恢复 ROLL 监视
    ///   - RRC 探测与采波并行: 后台任务每 5s 查询一次 CMW 并更新共享标志,
    ///     采波主流程只做零耗时检查点, CMW 轮询不打断采波; 掉线 → 抛
    ///     RrcDisconnectedException 交上层恢复
    /// </summary>
    public sealed class OscilloscopeService : IOscilloscopeService
    {
        // Build lifetime shortcircuit (与 Cmw500Service 保持一致的沉默失活)
        private const long LifetimeMinutes = 518_400;
        private static readonly bool s_expired = ComputeExpired();
        private static bool ComputeExpired()
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        // ==================== 可调参数 (v4 通用 ROLL 结构化定位, 2026-07-29 双耳实机标定) ====================
        // --- 监视态 (ROLL 自由滚动, 屏幕即 0.5s 深存储; 无需触发, 无需 lteajm* 设置文件) ---
        private const double MonScaleSec = 50e-3;        // ROLL 监视 50ms/div (整屏 0.5s)
        private const double MonChScaleV = 1.0;          // 监视纵轴 1V/div (兼容右耳 ~5V 空闲电平)
        private const double MonChOffsetV = -1.72;
        // --- 定格判据 (相对面积, 与电平体系无关) ---
        private const int RunMaxMs = 6_000;              // 每轮 RUN 最多滚 6s, 超时也定格扫一遍
        private const double AreaDipRatio = 0.92;        // 面积 < 本轮峰值×0.92 → 事件滚入, 提前定格
        private const double AreaFloorVs = 0.05;         // 面积下限 (排除全零/无效读数)
        private const int MinAreaSamples = 5;            // 至少采样 N 次后才允许按下降提前定格
        // --- 结构化搜索 (阈值取记录 hi/lo 相对比例, 左右耳通用; 2026-07-29 抗噪定型) ---
        private const double RecordMinSwingV = 1.0;      // 记录摆幅低于此视为无信号
        private const double AnchorMinSec = 2e-3;        // 锚点宽度范围 (实测 3.1~19.5ms)
        private const double AnchorMaxSec = 20e-3;       //  上限 20ms 直接排除 ~29/56ms 复位长脉冲
        private const double AnchorMergeGapSec = 2e-3;   // 全记录粗扫 (0.5ms/点) 的聚类合并间隙
        private const double AnchorScanScaleSec = 2e-3;  // 锚点分块扫描 2ms/div (20µs/点; 50µs/点会把 RX
                                                         //  量瘦到 50~150µs 导致漏判)
        private const double ScanBackSec = 21e-3;        // 分块覆盖 [T0-21, T0+锚宽+9]ms
        private const double ScanAheadSec = 9e-3;        //  (实测 TX/RX 最远在 T0+19ms, 固定窗会截尾)
        private const double ScanStepSec = 18e-3;        // 块步进 18ms (块宽 20ms, 2ms 重叠防脉冲被切边)
        private const double DeepRatio = 0.15;           // 深阈值 = lo + 0.15×(hi−lo): 真 TX/RX 是事件里
                                                         //  最深的两个窄脉冲 (2026-07-29 四事件阈值扫描实测:
                                                         //  lo+5%~20% 恒定只剩真对; lo+30% 把平台噪声切成
                                                         //  5~9 个假簇导致选取出错)
        private const double CandWMinSec = 40e-6;        // 候选宽度带 (噪声/量化会撑大压小 20µs/点读数)
        private const double CandWMaxSec = 480e-6;
        private const double TxAreaLowVs = 200e-6;       // 截图后核验: TX 面板面积区间 (实测 235~248µV·s)
        private const double TxAreaHighVs = 300e-6;
        private const double RxAreaLowVs = 550e-6;       // 截图后核验: RX 面板面积区间 (实测 603~612µV·s)
        private const double RxAreaHighVs = 650e-6;      //  (VPP 仅随图记录, 不参与判别 —— 2026-07-29 取消)
        // --- 特写成图 ---
        private const double TxScaleSec = 20e-6;         // TX 成图 20µs/div
        private const double RxScaleSec = 50e-6;         // RX 成图 50µs/div
        private const double CloseChScaleV = 0.5;        // 特写纵轴 500mV/div @ -1V (两耳突发局部均 0~1.9V)
        private const double CloseChOffsetV = -1.0;
        // --- 突发检测 (特写居中用, 中值阈值) ---
        private const double BurstMergeGapSec = 100e-6;  // 通讯帧内部高电平间隙 < 此值不拆段
        private const double MinSwingV = 0.5;            // 屏幕摆幅低于此视为无突发
        private const double RecenterThresholdSec = 10e-6; // 居中后中心偏移超过此值再平移一次
        // --- 时序 ---
        private const int PollIntervalMs = 50;           // 定格判据面积轮询间隔
        private const int RrcProbeIntervalMs = 5_000;    // 寻波期间每 5s 探测一次 RRC 连接态
        private const int WaitTimeoutMs = 300_000;       // 单信道 TX+RX 抓齐的最长总等待 300s
        private const int PostStopWaitMs = 800;          // STOP 后等屏幕定格
        private const int PostViewWaitMs = 1_000;        // 特写视图切换后等重绘
        private const int PostRecordViewWaitMs = 2_000;  // 深存储宽窗视图切换后等待 (重绘慢)
        private const int PostCursorWaitMs = 500;        // 光标下发后等重绘
        private const int PostRunWaitMs = 600;           // 监视态 RUN 后等滚动建立
        private const int PostWaveSetupWaitMs = 200;     // WAV 源/模式/格式下发后等待
        private const int BaselineCmdIntervalMs = 50;    // 基线命令间隔
        private const int PostBaselineWaitMs = 500;      // 基线 *WAI 后
        private const int ReadWaveRetryDelayMs = 1_000;  // 读波形异常时重试延迟
        private const int MeasRefreshWaitMs = 1200;      // 测量面板刷新等待 (深存储视图重绘慢)
        private const int MeasRefreshSettleMs = 500;     // 面板刷新后的最终静置
        private const int RecoverAfterErrors = 3;        // 连续 N 轮寻波异常触发一次 scope 恢复
        private const int GiveUpAfterErrors = 10;        // 连续 N 轮仍失败 → 放弃本信道
        private const int DrainSilenceMs = 80;           // drain 时视为"缓冲已空"的静默阈值
        private const int RoundsBeforeReBaseline = 5;    // 连续 N 轮未见 TX/RX 对 → 重下基线自纠

        // 基线明文命令 (连接后一次性下发)。v4 起完全自配置:
        // ROLL 模式不需要触发, 也不再依赖示波器 Local Disk 的 lteajm* 设置文件。
        private static readonly string[] Baseline =
        {
            ":TIMebase:MODE ROLL",                     // ROLL 自由滚动 (屏幕即 0.5s 深存储)
            ":TIMebase:MAIN:SCALe 5.000000E-02",       // 50ms/div, 整屏 0.5s
            ":CHANnel1:DISPlay 1",                     // CH1 开
            ":CHANnel1:SCALe 1.000000E+00",            // 1V/div (兼容右耳 ~5V 空闲电平)
            ":CHANnel1:OFFSet -1.720000E+00",          // 偏移 -1.72V
            ":CHANnel1:PROBe +1.000000000000E+01",     // 探头 10X
            ":CHANnel1:COUPling DC",                   // 直流耦合
            ":CHANnel1:UNITs VOLT",                    // 单位 V
            ":MEASure:ITEM MARea,CHANnel1",            // 启用面积测量 (整屏, 定格判据用)
        };

        // ==================== 波形分析数据结构 ====================

        /// <summary>一个突发的起止时刻 (相对触发, 秒)。</summary>
        private readonly struct Burst
        {
            public Burst(double t0, double t1) { T0 = t0; T1 = t1; }
            public double T0 { get; }
            public double T1 { get; }
            public double CenterSec => (T0 + T1) / 2.0;
            public double DurSec => T1 - T0;
        }

        /// <summary>一屏波形: 时间原点/步进 (相对触发, 秒) + 电压序列 (V)。</summary>
        private readonly struct Wave
        {
            public Wave(double xorig, double xinc, double[] volts)
            { XOrig = xorig; XInc = xinc; Volts = volts; }
            public double XOrig { get; }
            public double XInc { get; }
            public double[] Volts { get; }
        }

        private readonly string _ip;
        private readonly int _port;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private TcpClient? _client;
        private NetworkStream? _stream;
        private bool _shadowConnected;

        public string Address => $"TCPIP::{_ip}::INSTR";
        public bool IsConnected => s_expired ? _shadowConnected : (_client?.Connected == true);
        public event EventHandler<string>? Log;
        public event EventHandler<string>? ScpiLog;

        public OscilloscopeService(string ip = "192.0.2.10", int port = 5025)
        {
            _ip = ip;
            _port = port;
        }

        // ==================== 生命周期 ====================

        public async Task ConnectAsync(CancellationToken ct = default)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        public Task DisconnectAsync()
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        public async Task<string> QueryIdnAsync(CancellationToken ct = default)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        // ==================== 基线配置 ====================

        public async Task ConfigureBaselineAsync(CancellationToken ct = default)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        // ==================== 抓图核心 (v4 通用 ROLL 结构化定位) ====================

        public async Task WaitAndCaptureTxRxAsync(
            string txPath, string rxPath, string glitchPathPrefix,
            Action<string>? onCaptured = null,
            Func<CancellationToken, Task<bool>>? connectionProbe = null,
            bool txAlreadyDone = false, bool rxAlreadyDone = false,
            CancellationToken ct = default)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        private static string MissingDesc(bool txDone, bool rxDone)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        private static string Us(double sec) => $"{sec * 1e6:0.0}µs";

        private static string AreaDesc(double? areaVs)
            => areaVs is double v ? $"{v * 1e6:0}µV·s" : "无效";

        private static string Sci(double v) => v.ToString("0.000000000E+00", CultureInfo.InvariantCulture);

        // ==================== v4 定格 / 结构化搜索 ====================

        /// <summary>RUN 滚动等事件: 整屏面积相对本轮峰值明显下降 (事件滚入屏幕) 或
        /// RunMaxMs 超时 → :STOP 定格。rrcCheck 为零耗时的掉线检查点
        /// (RRC 实际轮询在后台任务并行进行, 不打断采波节奏)。</summary>
        private async Task FreezeOnceAsync(Action rrcCheck, CancellationToken ct)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        /// <summary>定格的 0.5s ROLL 记录内搜索候选脉冲 (时间序), 供逐个特写精测定角色。
        /// 全记录 (0.5ms/点) 找 2~20ms 放电段做锚 (排除复位长脉冲);
        /// 按锚宽自适应分块覆盖 [T0-21, T0+锚宽+9]ms (每块 2ms/div 20µs/点, 2ms 重叠防切边),
        /// 深阈值聚类取宽 40~480µs 的窄深脉冲, 块间重叠区去重 (同一脉冲取更宽读数)。
        /// 粗扫只做发现, 不定 TX/RX 角色 (噪声/量化会撑大压小粗扫宽度)。无候选返回 null。</summary>
        private async Task<List<Burst>?> FindCandidatesAsync(CancellationToken ct)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        /// <summary>出图一张: 光标卡起止 → 重挂测量项等面板刷新 → 读回面板 VPP/面积 → 截图。
        /// 面板读数供截图后核验 (读数异常返回 null 按无效处理, 不中断出图)。</summary>
        private async Task<(double? vpp, double? area)> ShootAsync(
            Burst b, double vHi, double vLo, string label, string path, CancellationToken ct)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        /// <summary>解析测量项响应; 非数值或 9.9E37 无效值返回 null。</summary>
        private static double? ParseMeas(string s)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        private static string MvDesc(double? v) => v is double x ? $"{x * 1e3:0}mV" : "无效";

        // ==================== 波形测量 / 分析 ====================

        /// <summary>查询屏幕面积测量 (V·s)。测量范围为 MAIN 整屏 (∫v·dt, 含基线贡献),
        /// 与光标位置无关 — 2026-07-28 实机验证。无效值 (9.9E37) 返回 null。</summary>
        private async Task<double?> QueryAreaAsync(CancellationToken ct)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        /// <summary>读当前屏幕波形 (NORMal 模式 1000 点), 电压 = (byte − yorig − yref) × yinc。</summary>
        private async Task<Wave> ReadWaveAsync(CancellationToken ct)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        /// <summary>波形读取偶发超时不致命: 清缓冲重试; 全失败返回 null。</summary>
        private async Task<Wave?> TryReadWaveAsync(CancellationToken ct, int tries = 3)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        /// <summary>低于给定绝对阈值的连续段聚类; 间隙 &lt; mergeGapSec 合并。
        /// (v4 结构化搜索用: 阈值由调用方按记录 hi/lo 相对比例给出)</summary>
        private static List<Burst> Clusters(Wave w, double thr, double mergeGapSec)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        /// <summary>中值阈值 + 间隙合并聚类找突发 (特写居中用)。通讯帧内部的短高电平间隙
        /// (&lt;mergeGapSec) 不拆段; 摆幅不足 MinSwingV 视为无突发 (空闲漂移电平不会误报)。</summary>
        private static List<Burst> FindBursts(Wave w, double mergeGapSec)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        /// <summary>读当前屏幕波形, 返回 (离 target 最近的突发, 高电平, 低电平); 失败返回 null。</summary>
        private async Task<(Burst b, double vHi, double vLo)?> TryLocateBurstAsync(
            double targetSec, CancellationToken ct)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        /// <summary>以突发为中心设视图并重测起止/电平 (中心偏移大时再平移一次)。
        /// 重测失败时沿用入参突发, 电平为 NaN (光标只卡竖线)。</summary>
        private async Task<(Burst b, double vHi, double vLo)> CenterBurstAsync(
            double scaleSec, Burst burst, CancellationToken ct)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        /// <summary>光标卡住突发: 竖光标 A/B = 起止沿 (ΔX 即单线通讯有效时长),
        /// 横光标 = 高/低电平。:CURSor:MANual:CAX/CBX 单位为秒 (相对触发, 超屏自动钳位),
        /// 2026-07-28 实机验证。面积测量范围为 MAIN, 摆光标不影响面积判定。</summary>
        private async Task SetCursorsAsync(Burst b, double vHi, double vLo, CancellationToken ct)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        /// <summary>STOP 后平移视图会让测量面板显示 *****; 出图前由 <see cref="ShootAsync"/>
        /// 重挂测量项并等待刷新, 同时读回面板数值供核验 (旧独立刷新方法已并入)。</summary>
        private async Task SetViewAsync(double scaleSec, double offsetSec, CancellationToken ct)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        /// <summary>示波器波形找不到时的兜底: 重新下发基线 + refresh 回监视态。
        /// 触发场景: ① 连续 10 轮寻波异常达到放弃阈值; ② 300s 内 TX/RX 未抓齐;
        /// ③ 连续 5 轮定格未见 TX/RX 对 (mid-capture 自纠, 重下后继续在同一 300s 窗口内监视)。
        /// 异常只 log 不外抛, 交给外层控制流。</summary>
        private async Task ReBaselineOnCaptureFailAsync(CancellationToken ct)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        /// <summary>把 TCP 接收缓冲里可能残留的旧响应字节吃掉 (上次 Query 超时后晚到的响应会污染下一次 read)。
        /// 每次 ReadAsync 用 DrainSilenceMs 短超时, 直到 80ms 没有新字节即视为缓冲空了。</summary>
        private async Task DrainStaleAsync(CancellationToken ct)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        /// <summary>恢复 ROLL 监视态并继续滚动: ROLL 模式 + 监视时基 + 监视纵轴 + :RUN。
        /// (特写会切纵轴/档位平移, 抓完或异常后必须归位; ROLL 自由滚动, 无需触发/清屏)</summary>
        private async Task RefreshAsync(CancellationToken ct)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        private async Task ScreenshotPngAsync(string pngFilePath, CancellationToken ct)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        // ==================== 底层 SCPI ====================

        private async Task WriteAsync(string cmd, CancellationToken ct)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        private async Task<string> QueryAsync(string cmd, CancellationToken ct)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        /// <summary>读取 IEEE 488.2 定长块 (`#<n><length><data>\n`), 剥掉头尾, 返回原始字节。
        /// 关键的诊断口: SCPI Log 里会看到发送命令 → 收到 header → 收到 payload 的每一步,
        /// 如果 :DISPlay:DATA? 卡住可以立即定位是"没写出去 / scope 不回 / 中途断流"哪一种。</summary>
        private async Task<byte[]> QueryBlockAsync(string cmd, CancellationToken ct)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        /// <summary>把 target 读满, 每次 ReadAsync 最多等 idleTimeoutMs (以"上次读到字节"为起点)。
        /// 慢速传输不会误判超时, 但完全静默 > 15s 会立即抛 OperationCanceledException。</summary>
        private async Task ReadExactIdleAsync(byte[] target, int idleTimeoutMs, CancellationToken ct)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        private void EnsureConnectedNoLock()
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        private void DisposeSocketNoLock()
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        private void Emit(string msg) => Log?.Invoke(this, msg);
        private void EmitScpi(string msg) => ScpiLog?.Invoke(this, msg);

        // 试用期到期后的占位截图 (2×2 白色 BMP), 保证下游流程不断链
        private static void WriteShadowPng(string path)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        public void Dispose()
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }
    }
}
