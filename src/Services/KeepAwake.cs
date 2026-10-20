using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace LTESingleLinkAJM.Services
{
    /// <summary>
    /// 测试运行期间阻止整机进入待机 (Modern Standby / S0 低功耗待机)。
    ///
    /// 背景: 本机是 Modern Standby 平台, 空闲进待机约 1 分钟后 Windows 会为了
    /// 达到 DRIPS 深度节电强制移除 GPIB-USB-HS (System 日志 USBHUB3 事件 196,
    /// 2026-07-29 实锤), NI-VISA 会话随之报 VI_ERROR_CONN_LOST "给定会话的连接
    /// 已丢失"。设备管理器的节电勾选项和 USB 选择性暂停都拦不住这条路径,
    /// 唯一可靠的办法是运行期间不让系统进待机 —— 即本类。
    ///
    /// ES_CONTINUOUS 是"每线程"状态, 线程一退出即自动清除; async 流程的续体
    /// 会在线程池线程间跳来跳去, 不能直接在流程里调用。因此这里用一个专职
    /// 后台线程持有该状态, Dispose 时唤醒它清除并退出。
    /// </summary>
    internal sealed class KeepAwake : IDisposable
    {
        private const uint ES_CONTINUOUS      = 0x80000000;
        private const uint ES_SYSTEM_REQUIRED = 0x00000001;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SetThreadExecutionState(uint esFlags);

        private readonly ManualResetEventSlim _stop = new(false);
        private readonly Thread _thread;

        public KeepAwake()
        {
            _thread = new Thread(() =>
            {
                SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED);
                _stop.Wait();
                SetThreadExecutionState(ES_CONTINUOUS);   // 清除 SYSTEM_REQUIRED
            })
            {
                IsBackground = true,
                Name = "KeepAwake",
            };
            _thread.Start();
        }

        public void Dispose()
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }
    }
}
