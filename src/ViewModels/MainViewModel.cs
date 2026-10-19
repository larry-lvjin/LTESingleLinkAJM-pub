using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using LTESingleLinkAJM.Models;
using LTESingleLinkAJM.Services;

namespace LTESingleLinkAJM.ViewModels
{
    public class MainViewModel : ObservableObject, IDisposable
    {
        private readonly ICmw500Service _cmw;
        private readonly IOscilloscopeService _scope;
        private readonly ISimSimulatorService _sim;
        private readonly IAutomationRunner _runner;

        private CancellationTokenSource? _cts;

        private bool _isBusy;
        private bool _isPaused;
        private bool _cmwConnected;
        private bool _scopeConnected;
        private string _cmwStatusText = "Not Connected";
        private string _scopeStatusText = "Not Connected";
        private string _statusText = "空闲";
        private string _log = string.Empty;
        private string _scpiLog = string.Empty;
        private int _progress;
        private TestItem? _selectedItem;
        private EarSide _side = EarSide.Left;
        private string _saveDir = AutomationRunner.DefaultSaveDir;

        public ObservableCollection<TestItem> Items { get; }

        // ==================== 状态属性 ====================

        public bool IsBusy
        {
            get => _isBusy;
            private set { if (Set(ref _isBusy, value)) RefreshCommands(); }
        }

        public bool IsPaused
        {
            get => _isPaused;
            private set
            {
                if (Set(ref _isPaused, value))
                {
                    OnPropertyChanged(nameof(PauseButtonText));
                    RefreshCommands();
                }
            }
        }

        public string PauseButtonText => IsPaused ? "▶  Resume" : "‖  Pause";

        public bool CmwConnected
        {
            get => _cmwConnected;
            private set { if (Set(ref _cmwConnected, value)) RefreshCommands(); }
        }
        public bool ScopeConnected
        {
            get => _scopeConnected;
            private set { if (Set(ref _scopeConnected, value)) RefreshCommands(); }
        }
        public bool AllConnected => CmwConnected && ScopeConnected;

        public string CmwStatusText
        {
            get => _cmwStatusText;
            private set => Set(ref _cmwStatusText, value);
        }
        public string ScopeStatusText
        {
            get => _scopeStatusText;
            private set => Set(ref _scopeStatusText, value);
        }
        public string CmwAddress => _cmw.Address;
        public string ScopeAddress => _scope.Address;

        public string StatusText
        {
            get => _statusText;
            private set => Set(ref _statusText, value);
        }

        public string Log
        {
            get => _log;
            private set => Set(ref _log, value);
        }

        /// <summary>与 <see cref="Log"/> 独立: 只承载各仪器上下行的 SCPI 原文,
        /// 供"导出 SCPI"按钮单独存盘。UI 不显示、原有 Log 内容不受影响。</summary>
        public string ScpiLog
        {
            get => _scpiLog;
            private set => Set(ref _scpiLog, value);
        }

        public int Progress
        {
            get => _progress;
            private set => Set(ref _progress, value);
        }
        public int Total => Items.Count(i => i.Selected);

        /// <summary>表头的全选 checkbox 绑定。TRUE 且所有 item.Selected=true 时为 true;
        /// 任一取消勾选变 false。写入时会把所有 item 同步为该值。</summary>
        public bool AllSelected
        {
            get => Items.Count > 0 && Items.All(i => i.Selected);
            set
            {
                if (_suppressAllSelectedPropagate) return;
                _suppressItemChangeFanOut = true;
                try
                {
                    foreach (var it in Items) it.Selected = value;
                }
                finally { _suppressItemChangeFanOut = false; }
                OnPropertyChanged(nameof(AllSelected));
                OnPropertyChanged(nameof(Total));
                RecomputeProgress();
                RefreshCommands();
            }
        }
        private bool _suppressAllSelectedPropagate;
        private bool _suppressItemChangeFanOut;

        public TestItem? SelectedItem
        {
            get => _selectedItem;
            set => Set(ref _selectedItem, value);
        }

        public EarSide Side
        {
            get => _side;
            set
            {
                if (Set(ref _side, value))
                {
                    OnPropertyChanged(nameof(IsLeft));
                    OnPropertyChanged(nameof(IsRight));
                    OnPropertyChanged(nameof(SideTxLabel));
                    OnPropertyChanged(nameof(SideRxLabel));
                }
            }
        }
        public bool IsLeft
        {
            get => Side == EarSide.Left;
            set { if (value) Side = EarSide.Left; }
        }
        public bool IsRight
        {
            get => Side == EarSide.Right;
            set { if (value) Side = EarSide.Right; }
        }

        public string SideTxLabel => $"{Side.Label()}TX波形";
        public string SideRxLabel => $"{Side.Label()}RX波形";

        /// <summary>波形 PNG 保存目录。留空/无效时使用 <see cref="AutomationRunner.DefaultSaveDir"/>
        /// (D:\LTESingleLinkAJM)。</summary>
        public string SaveDir
        {
            get => _saveDir;
            set => Set(ref _saveDir, string.IsNullOrWhiteSpace(value)
                ? AutomationRunner.DefaultSaveDir
                : value);
        }

        // ==================== 命令 ====================

        public AsyncRelayCommand ConnectScopeCommand    { get; }
        public AsyncRelayCommand DisconnectScopeCommand { get; }
        public AsyncRelayCommand ConnectCmwCommand      { get; }
        public AsyncRelayCommand DisconnectCmwCommand   { get; }
        public AsyncRelayCommand StartCommand           { get; }
        public RelayCommand      PauseCommand           { get; }
        public AsyncRelayCommand StopCommand            { get; }
        public RelayCommand      BrowseSaveDirCommand   { get; }
        public RelayCommand      ExportLogCommand       { get; }
        public RelayCommand      ExportScpiLogCommand   { get; }
        public RelayCommand      ClearLogCommand        { get; }

        // ==================== 构造 ====================

        // 无参构造供 XAML DataContext 直接实例化。
        public MainViewModel()
            : this(
                new Cmw500Service("GPIB0::20::0::INSTR"),
                new OscilloscopeService("192.0.2.10", 5025),
                new SimSimulatorService())
        { }

        public MainViewModel(ICmw500Service cmw, IOscilloscopeService scope, ISimSimulatorService sim)
        {
            _cmw = cmw;
            _scope = scope;
            _sim = sim;
            _runner = new AutomationRunner(_cmw, _scope, _sim);

            Items = new ObservableCollection<TestItem>(TestPlan.BuildDefault());
            // 监听每个 item 的 Selected 变化, 同步 AllSelected / Total / Progress / Start 可用性
            foreach (var it in Items) it.PropertyChanged += Item_PropertyChanged;

            _cmw.Log     += (_, m) => AppendLog(m);
            _scope.Log   += (_, m) => AppendLog(m);
            _sim.Log     += (_, m) => AppendLog(m);
            _runner.Log  += (_, m) => AppendLog(m);
            _cmw.ScpiLog   += (_, m) => AppendScpi(m);
            _scope.ScpiLog += (_, m) => AppendScpi(m);
            _runner.ItemFinished += (_, item) =>
            {
                RecomputeProgress();
                RunOnUi(() => SelectedItem = item);
            };
            _runner.PauseStateChanged += (_, _) => RunOnUi(() => IsPaused = _runner.IsPaused);

            ConnectScopeCommand    = new AsyncRelayCommand(_ => ConnectScopeAsync(),
                _ => !IsBusy && !ScopeConnected);
            DisconnectScopeCommand = new AsyncRelayCommand(_ => DisconnectScopeAsync(),
                _ => !IsBusy &&  ScopeConnected);
            ConnectCmwCommand      = new AsyncRelayCommand(_ => ConnectCmwAsync(),
                _ => !IsBusy && !CmwConnected);
            DisconnectCmwCommand   = new AsyncRelayCommand(_ => DisconnectCmwAsync(),
                _ => !IsBusy &&  CmwConnected);
            StartCommand           = new AsyncRelayCommand(_ => StartAsync(),
                _ => !IsBusy && AllConnected && Items.Any(i => i.Selected));
            PauseCommand           = new RelayCommand(_ => TogglePause(),
                _ => IsBusy);
            StopCommand            = new AsyncRelayCommand(_ => StopAsync(),
                _ => IsBusy);
            BrowseSaveDirCommand   = new RelayCommand(_ => BrowseSaveDir(),
                _ => !IsBusy);
            ExportLogCommand       = new RelayCommand(_ => ExportLog());
            ExportScpiLogCommand   = new RelayCommand(_ => ExportScpiLog());
            ClearLogCommand        = new RelayCommand(_ =>
            {
                Log = string.Empty;
                ScpiLog = string.Empty;
            });
        }

        /// <summary>某个 TestItem 属性变化的回调, 用来同步表头 AllSelected 和进度显示。
        /// <see cref="_suppressItemChangeFanOut"/> 是 AllSelected 批量写入时防止 O(n²) 回调风暴的开关。</summary>
        private void Item_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(TestItem.Selected)) return;
            if (_suppressItemChangeFanOut) return;
            _suppressAllSelectedPropagate = true;
            try
            {
                OnPropertyChanged(nameof(AllSelected));
                OnPropertyChanged(nameof(Total));
                RecomputeProgress();
                RefreshCommands();
            }
            finally { _suppressAllSelectedPropagate = false; }
        }

        private void RecomputeProgress()
        {
            // 统计当前勾选的、已处理完的条目 (完成/失败/跳过都推进进度条,
            // 否则有条目失败时跑完也停在 14/15; 成败由行内状态列体现)
            Progress = Items.Count(i => i.Selected
                && (i.Status == TestStatus.Completed
                    || i.Status == TestStatus.Failed
                    || i.Status == TestStatus.Skipped));
        }

        private void BrowseSaveDir()
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "选择波形图片保存目录",
                InitialDirectory = System.IO.Directory.Exists(SaveDir)
                    ? SaveDir
                    : AutomationRunner.DefaultSaveDir,
            };
            if (dlg.ShowDialog() == true)
                SaveDir = dlg.FolderName;
        }

        private void ExportLog()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "导出日志",
                Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                FileName = $"LTESingleLink_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                InitialDirectory = System.IO.Directory.Exists(SaveDir) ? SaveDir : null,
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    System.IO.File.WriteAllText(dlg.FileName, Log ?? string.Empty, System.Text.Encoding.UTF8);
                    AppendLog($"[LOG] 已导出到 {dlg.FileName}");
                }
                catch (Exception ex)
                {
                    AppendLog($"[LOG] 导出失败: {ex.Message}");
                }
            }
        }

        private void ExportScpiLog()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "导出 SCPI 日志",
                Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                FileName = $"LTESingleLink_scpi_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                InitialDirectory = System.IO.Directory.Exists(SaveDir) ? SaveDir : null,
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    System.IO.File.WriteAllText(dlg.FileName, ScpiLog ?? string.Empty, System.Text.Encoding.UTF8);
                    AppendLog($"[LOG] SCPI 已导出到 {dlg.FileName}");
                }
                catch (Exception ex)
                {
                    AppendLog($"[LOG] SCPI 导出失败: {ex.Message}");
                }
            }
        }

        // ==================== 连接控制 ====================

        private async Task ConnectScopeAsync()
        {
            IsBusy = true;
            StatusText = "示波器连接中...";
            try
            {
                await _scope.ConnectAsync().ConfigureAwait(true);
                var idn = await _scope.QueryIdnAsync().ConfigureAwait(true);
                ScopeConnected = _scope.IsConnected;
                ScopeStatusText = idn;
                AppendLog($"[SCOPE] IDN: {idn}");

                // 程序运行一开始就把 lteajm0 基线 + 光标窗口设置好, 后续开始测试不再重发,
                // 避免每轮覆盖用户手动改动 (对齐 py 脚本: 连接后立即下发, 循环里只监测抓图)
                StatusText = "示波器基线下发中...";
                try
                {
                    await _scope.ConfigureBaselineAsync().ConfigureAwait(true);
                }
                catch (Exception baseEx)
                {
                    AppendLog($"[SCOPE] 基线下发异常 (可稍后手动重连再试): {baseEx.Message}");
                }

                StatusText = "示波器已连接";
            }
            catch (Exception ex)
            {
                ScopeConnected = false;
                ScopeStatusText = ex.Message;
                AppendLog($"[SCOPE] 连接失败: {ex.Message}");
                StatusText = "示波器连接失败";
            }
            finally { IsBusy = false; OnPropertyChanged(nameof(AllConnected)); }
        }

        private async Task DisconnectScopeAsync()
        {
            IsBusy = true;
            try
            {
                try { await _scope.DisconnectAsync().ConfigureAwait(true); } catch { }
                ScopeConnected = false;
                ScopeStatusText = "Not Connected";
                StatusText = "示波器已断开";
            }
            finally { IsBusy = false; OnPropertyChanged(nameof(AllConnected)); }
        }

        private async Task ConnectCmwAsync()
        {
            IsBusy = true;
            StatusText = "CMW 连接中...";
            try
            {
                await _cmw.ConnectAsync().ConfigureAwait(true);
                var idn = await _cmw.QueryIdnAsync().ConfigureAwait(true);
                CmwConnected = _cmw.IsConnected;
                CmwStatusText = idn;
                AppendLog($"[CMW] IDN: {idn}");
                StatusText = "CMW 已连接";
            }
            catch (Exception ex)
            {
                CmwConnected = false;
                CmwStatusText = ex.Message;
                AppendLog($"[CMW] 连接失败: {ex.Message}");
                StatusText = "CMW 连接失败";
            }
            finally { IsBusy = false; OnPropertyChanged(nameof(AllConnected)); }
        }

        private async Task DisconnectCmwAsync()
        {
            IsBusy = true;
            try
            {
                try { await _cmw.DisconnectAsync().ConfigureAwait(true); } catch { }
                CmwConnected = false;
                CmwStatusText = "Not Connected";
                StatusText = "CMW 已断开";
            }
            finally { IsBusy = false; OnPropertyChanged(nameof(AllConnected)); }
        }

        // ==================== 自动流程 ====================

        private async Task StartAsync()
        {
            var selected = Items.Where(i => i.Selected).ToList();
            if (IsBusy || !AllConnected || selected.Count == 0) return;

            // 只重置勾选的条目; 未勾选的条目保留上一轮结果
            foreach (var it in selected)
            {
                it.Status = TestStatus.Pending;
                it.Message = string.Empty;
                it.IsCurrent = false;
                it.TxCaptured = false;
                it.RxCaptured = false;
            }
            Progress = 0;

            IsBusy = true;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            StatusText = $"自动测试运行中 ({Side.Label()}, {selected.Count} 条)";

            try
            {
                // 只把勾选的传给 runner, unselected 完全不参与循环
                await _runner.RunAsync(selected, Side, SaveDir, token).ConfigureAwait(true);
                StatusText = "全部完成";
            }
            catch (OperationCanceledException)
            {
                StatusText = "已停止";
            }
            catch (Exception ex)
            {
                StatusText = $"异常: {ex.Message}";
                AppendLog($"[AUTO] 异常: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
                IsPaused = false;
            }
        }

        private Task StopAsync()
        {
            _cts?.Cancel();
            // 取消时若正卡在暂停等待, 让 runner 立刻醒过来走 OperationCanceled 路径
            if (_runner.IsPaused) _runner.Resume();
            return Task.CompletedTask;
        }

        private void TogglePause()
        {
            if (!IsBusy) return;
            if (_runner.IsPaused) _runner.Resume();
            else _runner.Pause();
        }

        // ==================== 工具 ====================

        private void AppendLog(string msg)
        {
            string line = $"{DateTime.Now:HH:mm:ss}  {msg}";
            RunOnUi(() => Log = string.IsNullOrEmpty(Log) ? line : Log + Environment.NewLine + line);
        }

        private void AppendScpi(string msg)
        {
            string line = $"{DateTime.Now:HH:mm:ss.fff}  {msg}";
            RunOnUi(() => ScpiLog = string.IsNullOrEmpty(ScpiLog) ? line : ScpiLog + Environment.NewLine + line);
        }

        private static void RunOnUi(Action action)
        {
            var app = Application.Current;
            if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess())
                app.Dispatcher.BeginInvoke(action);
            else
                action();
        }

        private void RefreshCommands()
        {
            RunOnUi(() =>
            {
                ConnectScopeCommand?.RaiseCanExecuteChanged();
                DisconnectScopeCommand?.RaiseCanExecuteChanged();
                ConnectCmwCommand?.RaiseCanExecuteChanged();
                DisconnectCmwCommand?.RaiseCanExecuteChanged();
                StartCommand?.RaiseCanExecuteChanged();
                PauseCommand?.RaiseCanExecuteChanged();
                StopCommand?.RaiseCanExecuteChanged();
                BrowseSaveDirCommand?.RaiseCanExecuteChanged();
            });
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cmw.Dispose();
            _scope.Dispose();
        }
    }
}
