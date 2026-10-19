using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LTESingleLinkAJM.Models
{
    public class TestItem : INotifyPropertyChanged
    {
        private int _index;
        private string _band = string.Empty;
        private string _channelType = string.Empty;
        private int _earfcn;
        private string _tpcSetup = "MAX POWER";
        private int _targetPower = 23;
        private int _ulRb = 50;
        private int _dlRb = 50;
        private TestStatus _status = TestStatus.Pending;
        private string _message = string.Empty;
        private bool _isCurrent;
        private bool _txCaptured;
        private bool _rxCaptured;
        private bool _selected = true;   // 默认全部勾选

        public int Index
        {
            get => _index;
            set => Set(ref _index, value);
        }

        public string Band
        {
            get => _band;
            set => Set(ref _band, value);
        }

        public string ChannelType
        {
            get => _channelType;
            set => Set(ref _channelType, value);
        }

        public int Earfcn
        {
            get => _earfcn;
            set => Set(ref _earfcn, value);
        }

        public string TpcSetup
        {
            get => _tpcSetup;
            set => Set(ref _tpcSetup, value);
        }

        public int TargetPower
        {
            get => _targetPower;
            set => Set(ref _targetPower, value);
        }

        public int UlRb
        {
            get => _ulRb;
            set => Set(ref _ulRb, value);
        }

        public int DlRb
        {
            get => _dlRb;
            set => Set(ref _dlRb, value);
        }

        public TestStatus Status
        {
            get => _status;
            set
            {
                if (Set(ref _status, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        public string StatusText => Status switch
        {
            TestStatus.Pending => "待运行",
            TestStatus.Running => "运行中",
            TestStatus.Completed => "完成",
            TestStatus.Failed => "失败",
            TestStatus.Skipped => "跳过",
            _ => string.Empty
        };

        public string Message
        {
            get => _message;
            set => Set(ref _message, value);
        }

        public bool IsCurrent
        {
            get => _isCurrent;
            set => Set(ref _isCurrent, value);
        }

        public bool TxCaptured
        {
            get => _txCaptured;
            set => Set(ref _txCaptured, value);
        }

        public bool RxCaptured
        {
            get => _rxCaptured;
            set => Set(ref _rxCaptured, value);
        }

        /// <summary>是否参与本轮测试; 未勾选则跳过。默认 true。</summary>
        public bool Selected
        {
            get => _selected;
            set => Set(ref _selected, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name!));
    }
}
