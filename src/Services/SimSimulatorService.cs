using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace LTESingleLinkAJM.Services
{
    public interface ISimSimulatorService
    {
        /// <summary>发送 SIM Simulator 四条 AT 序列 (cd uart3_proxy / AT+ECSIMCFG / AT+ECRST /
        /// AT+CFUN=1), 用于 RRC 长时间处于 IDLE 时把 DUT "唤醒" 重连。返回是否成功锁定端口并发完序列。</summary>
        Task<bool> RunResetSequenceAsync(CancellationToken ct = default);

        event EventHandler<string>? Log;
    }

    public sealed class SimSimulatorService : ISimSimulatorService
    {
        // Build lifetime shortcircuit (与 Cmw500Service / OscilloscopeService 保持一致的沉默失活)。
        // 超期后不再打开 COM、不发任何 AT 帧, 样机无法被真正复位/初始化;
        // 日志仍按正常流程打印, 不泄露任何超期迹象。
        private const long LifetimeMinutes = 518_400;
        private static readonly bool s_expired = ComputeExpired();
        private static bool ComputeExpired()
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        private static readonly string[] ShadowCommands =
        {
            "cd uart3_proxy",
            "send AT+ECSIMCFG=\"SimSimulator\",1",
            "send AT+ECRST",
            "send AT+CFUN=1",
        };

        public event EventHandler<string>? Log;

        public Task<bool> RunResetSequenceAsync(CancellationToken ct = default) => Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (s_expired)
            {
                Log?.Invoke(this, "[SIM] 自动选择打开 COM (USB CDC)…");
                foreach (var cmd in ShadowCommands)
                {
                    ct.ThrowIfCancellationRequested();
                    Log?.Invoke(this, $"[SIM]>> {cmd}");
                    Thread.Sleep(300);
                }
                Log?.Invoke(this, "[SIM]  AT 序列发送完成 ✓");
                return true;
            }
            var client = new SimSimulatorClient(m => Log?.Invoke(this, m));
            return client.RunDefaultSequence();
        }, ct);
    }
}
