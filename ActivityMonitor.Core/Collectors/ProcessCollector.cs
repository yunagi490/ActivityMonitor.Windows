using System.Diagnostics;
using ActivityMonitor.Core.Models;
using ActivityMonitor.Core.Native;

namespace ActivityMonitor.Core.Collectors;

/// <summary>
/// プロセス一覧を一括取得し、前回スナップショットとの差分でCPU%を自前計算する。
/// スレッドセーフではないので、単一のバックグラウンドループから呼ぶこと。
/// </summary>
public sealed class ProcessCollector : IDisposable
{
    private readonly NativeProcessReader _reader = new();
    private readonly int _cpuCount = Environment.ProcessorCount;

    // キーは (PID, CreateTime)。PID再利用による誤差分を防ぐ。
    private Dictionary<(int, long), (long User, long Kernel)> _prev = new();
    private Dictionary<(int, long), (long User, long Kernel)> _cur = new();
    private long _prevTimestamp;

    public ProcessSnapshot Collect()
    {
        var raws = _reader.Read();
        long now = Stopwatch.GetTimestamp();

        // 経過時間（100ns単位）× 論理CPU数 = 今回の区間で使える総CPU時間
        double capacity = _prevTimestamp == 0
            ? 0
            : (now - _prevTimestamp) * 10_000_000.0 / Stopwatch.Frequency * _cpuCount;

        var list = new List<ProcessInfo>(raws.Count);
        _cur.Clear();

        double userDelta = 0, kernelDelta = 0;
        int threadTotal = 0;

        foreach (var r in raws)
        {
            var key = (r.Pid, r.CreateTime);
            _cur[key] = (r.UserTime, r.KernelTime);

            double cpu = 0;
            if (capacity > 0 && _prev.TryGetValue(key, out var p))
            {
                double du = Math.Max(0, r.UserTime - p.User);
                double dk = Math.Max(0, r.KernelTime - p.Kernel);
                cpu = Math.Clamp((du + dk) / capacity * 100.0, 0, 100);

                if (r.Pid != 0) // System Idle Process は内訳に含めない
                {
                    userDelta += du;
                    kernelDelta += dk;
                }
            }

            threadTotal += r.ThreadCount;

            string name = r.Pid == 0 ? "System Idle Process"
                : r.Name.Length == 0 ? "(unknown)"
                : r.Name;

            // Task Manager の「メモリ」に近い Private Working Set。取れなければ Working Set。
            long mem = r.PrivateWorkingSet > 0 ? r.PrivateWorkingSet : r.WorkingSet;

            list.Add(new ProcessInfo(
                r.Pid,
                name,
                cpu,
                TimeSpan.FromTicks(r.UserTime + r.KernelTime),
                r.ThreadCount,
                mem));
        }

        double userPct = capacity > 0 ? Math.Clamp(userDelta / capacity * 100.0, 0, 100) : 0;
        double sysPct = capacity > 0 ? Math.Clamp(kernelDelta / capacity * 100.0, 0, 100) : 0;
        double idlePct = capacity > 0 ? Math.Max(0, 100.0 - userPct - sysPct) : 100;

        (_prev, _cur) = (_cur, _prev);
        _prevTimestamp = now;

        return new ProcessSnapshot(
            DateTime.Now,
            list,
            new SystemSnapshot(userPct, sysPct, idlePct, list.Count, threadTotal));
    }

    public void Dispose() => _reader.Dispose();
}
