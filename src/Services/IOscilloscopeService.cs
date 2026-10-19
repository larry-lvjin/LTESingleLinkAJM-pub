using System;
using System.Threading;
using System.Threading.Tasks;

namespace LTESingleLinkAJM.Services
{
    /// <summary>寻波期间周期性 RRC 探测发现连接丢失时, 由示波器服务抛出以立即中止
    /// 寻找有效通讯波形的行为; 上层捕获后进入 RRC 恢复流程。</summary>
    public sealed class RrcDisconnectedException : Exception
    {
        public RrcDisconnectedException(string message) : base(message) { }
    }

    public interface IOscilloscopeService : IDisposable
    {
        string Address { get; }
        bool IsConnected { get; }

        Task ConnectAsync(CancellationToken ct = default);
        Task DisconnectAsync();
        Task<string> QueryIdnAsync(CancellationToken ct = default);

        /// <summary>下发基线 (v4 自配置 ROLL 监视态: ROLL 50ms/div 整屏 0.5s;
        /// CH1 1V/div @ −1.72V 10X DC; 启用面积测量 MARea), 只需在连接后调用一次。
        /// v4 起不依赖触发, 也不依赖示波器 Local Disk 的 lteajm* 设置文件。</summary>
        Task ConfigureBaselineAsync(CancellationToken ct = default);

        /// <summary>左右耳通用 ROLL 结构化定位寻波 (v4, 2026-07-29 双耳实机标定),
        /// 直到 TX/RX 两张抓齐:
        /// ① ROLL 滚动, 整屏面积相对本轮峰值明显下降 (事件滚入) 或 6s 超时 → :STOP 定格;
        /// ② 定格的 0.5s 记录内结构化搜索 (阈值取记录 hi/lo 相对比例, 与空闲电平体系无关):
        ///    2~20ms 放电段做锚 (排除复位长脉冲), 按锚宽自适应分块覆盖 [T0-21, T0+锚宽+9]ms
        ///    (20µs/点, 深阈值 lo+15%) 一步选中事件里最深的两个窄脉冲, 位置直取左=TX 右=RX
        ///    (只认含 ≥2 个脉冲对的事件); 放大居中截图后核验面板面积
        ///    (TX 200~300 / RX 550~650µV·s, VPP 仅记录不判别), 不过则弃图重采;
        /// ③ 特写纵轴统一 500mV/div @ −1V, TX 20µs/div / RX 50µs/div 精居中,
        ///    光标 A/B 卡起止 (ΔX=单线通讯有效时长) → 刷新测量面板 → 分别截图
        ///    txPath / rxPath。每抓到一张回调 onCaptured("TX"/"RX")。
        /// glitchPathPrefix 保留参数 (新判定法不再单独产出毛刺截图)。
        /// connectionProbe 非空时由后台任务每 5s 并行探测 RRC (与采波并行, 不打断采波节奏);
        /// 返回 false → 主流程在下一个检查点中止寻波并抛
        /// <see cref="RrcDisconnectedException"/>。txAlreadyDone / rxAlreadyDone 用于 RRC 恢复后
        /// 续抓时跳过已取得的波形 (同一对里只补抓缺的那张)。</summary>
        Task WaitAndCaptureTxRxAsync(string txPath, string rxPath, string glitchPathPrefix,
            Action<string>? onCaptured = null,
            Func<CancellationToken, Task<bool>>? connectionProbe = null,
            bool txAlreadyDone = false, bool rxAlreadyDone = false,
            CancellationToken ct = default);

        event EventHandler<string>? Log;

        /// <summary>只承载 SCPI 收发原文 (write / query 的命令 + 响应),
        /// 供导出"SCPI Log"用。不会重复投递到 <see cref="Log"/>。</summary>
        event EventHandler<string>? ScpiLog;
    }
}
