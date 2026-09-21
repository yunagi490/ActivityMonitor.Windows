using System.ComponentModel;
using ActivityMonitor.Core.Models;

namespace ActivityMonitor.UI;

/// <summary>
/// ListView 用の行。PIDごとに1インスタンスを使い回し、値だけ更新することで
/// 毎秒のリスト再構築（スクロール位置・選択のリセット）を避ける。
/// </summary>
public sealed class ProcessRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public ProcessInfo Info { get; private set; }
    public int Pid => Info.Pid;

    public ProcessRow(ProcessInfo info) => Info = info;

    public void Update(ProcessInfo info)
    {
        if (info == Info) return; // record の値比較。変化なしなら通知しない
        Info = info;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    public string Name => Info.Name;
    public string PidText => Info.Pid.ToString();
    public string CpuText => Info.CpuPercent.ToString("F1");
    public string CpuTimeText
    {
        get
        {
            var t = Info.CpuTime;
            return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
        }
    }
    public string ThreadsText => Info.ThreadCount.ToString();
    public string MemoryText => Info.MemoryBytes >= 1L << 30
        ? $"{Info.MemoryBytes / 1073741824.0:F2} GB"
        : $"{Info.MemoryBytes / 1048576.0:F1} MB";
}
