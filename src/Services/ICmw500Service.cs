using System;
using System.Threading;
using System.Threading.Tasks;

namespace LTESingleLinkAJM.Services
{
    public enum RrcState
    {
        Unknown,
        Idle,
        Connected,
    }

    public interface ICmw500Service : IDisposable
    {
        /// <summary>VISA resource string, e.g. GPIB0::20::0::INSTR.</summary>
        string Address { get; }
        bool IsConnected { get; }

        Task ConnectAsync(CancellationToken ct = default);
        Task DisconnectAsync();

        Task<string> QueryIdnAsync(CancellationToken ct = default);

        /// <summary>下发 BAND + 信道, 并按目标频段自动处理双工模式 (2026-07-30 实机定型):
        ///   · 同模式: 只换 BAND + EARFCN (FDD 用 CHAN:UL, TDD 用 CHAN:DL), 保持连接;
        ///   · FDD→TDD: Cell OFF → DMOD TDD → BAND → CELL:ULDL 2 / CELL:SSUBframe 7 →
        ///     CHAN:DL → Cell ON (UE 重新附着, 由上层 RRC 轮询梯等待);
        ///   · TDD→FDD: Cell OFF → DMOD FDD → BAND → CHAN:UL → Cell ON。
        /// 改 DMOD 前必须小区下电 (小区 ON 时改报 -220 access permission, 双向均须下电)。
        /// 功率/RB 等其它设置一律不改, 保留用户在仪器面板上的手动设置。
        /// SYSTem:ERRor? 非 0 时抛异常给上层。</summary>
        Task SetBandChannelAsync(string band, int uplinkEarfcn, CancellationToken ct = default);

        /// <summary>查询 LTE 小区当前 RRC 状态。</summary>
        Task<RrcState> QueryRrcStateAsync(CancellationToken ct = default);

        /// <summary>关闭再打开 LTE 小区 (SOUR:LTE:SIGN1:CELL:STAT OFF -> ON),
        /// 用于把 DUT 从 IDLE 里"踢"出来重连。</summary>
        Task CycleCellAsync(CancellationToken ct = default);

        event EventHandler<string>? Log;

        /// <summary>只承载 SCPI 收发原文 (write / query 的命令 + 响应),
        /// 供导出"SCPI Log"用。不会重复投递到 <see cref="Log"/>。</summary>
        event EventHandler<string>? ScpiLog;
    }
}
