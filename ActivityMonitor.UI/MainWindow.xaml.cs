using System.Collections.ObjectModel;
using System.Diagnostics;
using ActivityMonitor.Core.Collectors;
using ActivityMonitor.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ActivityMonitor.UI;

public sealed partial class MainWindow : Window
{
    public ObservableCollection<ProcessRow> Rows { get; } = new();

    private readonly Dictionary<int, ProcessRow> _rows = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, (Button Button, string Label)> _headers;

    private string _sortKey = "cpu";
    private bool _descending = true;
    private string _filter = "";
    private int _intervalMs = 1000;
    private int? _selectedPid;
    private bool _syncing;
    private ProcessSnapshot? _last;

    public MainWindow()
    {
        InitializeComponent();

        Title = "Activity Monitor";
        SystemBackdrop = new MicaBackdrop();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(960, 680));

        _headers = new()
        {
            ["name"] = (HeaderName, "プロセス名"),
            ["pid"] = (HeaderPid, "PID"),
            ["cpu"] = (HeaderCpu, "% CPU"),
            ["time"] = (HeaderTime, "CPU時間"),
            ["threads"] = (HeaderThreads, "スレッド"),
            ["memory"] = (HeaderMemory, "メモリ"),
        };
        UpdateHeaders();

        Closed += (_, _) => _cts.Cancel();
        _ = RunLoopAsync(_cts.Token);
    }

    // ---------- 収集ループ（バックグラウンド） ----------

    private async Task RunLoopAsync(CancellationToken ct)
    {
        using var collector = new ProcessCollector();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var snapshot = await Task.Run(collector.Collect, ct);
                DispatcherQueue.TryEnqueue(() => Apply(snapshot));
                await Task.Delay(_intervalMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    // ---------- UIスレッド ----------

    private void Apply(ProcessSnapshot snapshot)
    {
        _last = snapshot;

        var alive = new HashSet<int>();
        foreach (var info in snapshot.Processes)
        {
            alive.Add(info.Pid);
            if (_rows.TryGetValue(info.Pid, out var row))
                row.Update(info);
            else
                _rows[info.Pid] = new ProcessRow(info);
        }
        foreach (var pid in _rows.Keys.Where(k => !alive.Contains(k)).ToList())
            _rows.Remove(pid);

        Refilter();
        UpdateFooter(snapshot.System);
    }

    private void UpdateFooter(SystemSnapshot s)
    {
        UserCol.Width = new GridLength(Math.Max(0, s.UserPercent), GridUnitType.Star);
        SystemCol.Width = new GridLength(Math.Max(0, s.SystemPercent), GridUnitType.Star);
        IdleCol.Width = new GridLength(Math.Max(0.0001, s.IdlePercent), GridUnitType.Star);

        StatusText.Text =
            $"ユーザー {s.UserPercent:F1}%   システム {s.SystemPercent:F1}%   アイドル {s.IdlePercent:F1}%" +
            $"     |     プロセス {s.ProcessCount}   スレッド {s.ThreadCount}";
    }

    /// <summary>フィルタ＋ソートを適用し、ObservableCollection を差分で同期する。</summary>
    private void Refilter()
    {
        IEnumerable<ProcessRow> q = _rows.Values;
        if (_filter.Length > 0)
        {
            q = q.Where(r =>
                r.Info.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                r.PidText.Contains(_filter, StringComparison.Ordinal));
        }

        var target = q.ToList();
        target.Sort(BuildComparison());
        SyncRows(target);
    }

    private Comparison<ProcessRow> BuildComparison()
    {
        Comparison<ProcessRow> primary = _sortKey switch
        {
            "name" => (a, b) => string.Compare(a.Info.Name, b.Info.Name, StringComparison.OrdinalIgnoreCase),
            "pid" => (a, b) => a.Pid.CompareTo(b.Pid),
            "cpu" => (a, b) => a.Info.CpuPercent.CompareTo(b.Info.CpuPercent),
            "time" => (a, b) => a.Info.CpuTime.CompareTo(b.Info.CpuTime),
            "threads" => (a, b) => a.Info.ThreadCount.CompareTo(b.Info.ThreadCount),
            _ => (a, b) => a.Info.MemoryBytes.CompareTo(b.Info.MemoryBytes),
        };
        int sign = _descending ? -1 : 1;
        return (a, b) =>
        {
            int c = primary(a, b) * sign;
            return c != 0 ? c : a.Pid.CompareTo(b.Pid);
        };
    }

    private void SyncRows(List<ProcessRow> target)
    {
        _syncing = true;
        try
        {
            var keep = new HashSet<ProcessRow>(target);
            for (int i = Rows.Count - 1; i >= 0; i--)
                if (!keep.Contains(Rows[i])) Rows.RemoveAt(i);

            for (int i = 0; i < target.Count; i++)
            {
                var t = target[i];
                if (i < Rows.Count && ReferenceEquals(Rows[i], t)) continue;

                int j = -1;
                for (int k = i + 1; k < Rows.Count; k++)
                {
                    if (ReferenceEquals(Rows[k], t)) { j = k; break; }
                }
                if (j >= 0) Rows.Move(j, i);
                else Rows.Insert(i, t);
            }

            // 選択の復元
            if (_selectedPid is int pid && _rows.TryGetValue(pid, out var sel) && Rows.Contains(sel))
            {
                if (!ReferenceEquals(ProcessList.SelectedItem, sel))
                    ProcessList.SelectedItem = sel;
            }
            else if (_selectedPid is not null)
            {
                _selectedPid = null; // 終了した or フィルタで消えた
            }
        }
        finally
        {
            _syncing = false;
            KillButton.IsEnabled = _selectedPid is not null;
        }
    }

    // ---------- イベント ----------

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _filter = SearchBox.Text.Trim();
        if (_last is not null) Refilter();
    }

    private void IntervalBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IntervalBox.SelectedItem is ComboBoxItem { Tag: string tag } && int.TryParse(tag, out int ms))
            _intervalMs = ms;
    }

    private void Header_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key }) return;

        if (_sortKey == key)
        {
            _descending = !_descending;
        }
        else
        {
            _sortKey = key;
            _descending = key is not ("name" or "pid"); // 名前・PIDは昇順、数値系は降順が初期値
        }
        UpdateHeaders();
        if (_last is not null) Refilter();
    }

    private void UpdateHeaders()
    {
        foreach (var (key, (button, label)) in _headers)
        {
            string arrow = key == _sortKey ? (_descending ? " ▼" : " ▲") : "";
            button.Content = label + arrow;
        }
    }

    private void ProcessList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing) return;
        _selectedPid = (ProcessList.SelectedItem as ProcessRow)?.Pid;
        KillButton.IsEnabled = _selectedPid is not null;
    }

    private async void KillButton_Click(object sender, RoutedEventArgs e)
    {
        if (ProcessList.SelectedItem is not ProcessRow row) return;
        int pid = row.Pid;
        string name = row.Name;

        if (pid is 0 or 4)
        {
            await ShowMessageAsync("終了できません", $"{name} (PID {pid}) はシステムプロセスです。");
            return;
        }

        var confirm = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "プロセスを終了しますか？",
            Content = $"{name} (PID {pid}) を強制終了します。保存されていないデータは失われます。",
            PrimaryButtonText = "終了する",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            using var p = Process.GetProcessById(pid);
            p.Kill();
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("終了に失敗しました",
                $"{ex.Message}\n\n保護されたプロセスは管理者権限で起動しても終了できない場合があります。");
        }
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "OK",
        };
        await dialog.ShowAsync();
    }
}
