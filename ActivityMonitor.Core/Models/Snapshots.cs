namespace ActivityMonitor.Core.Models;

/// <summary>1プロセス分の表示用情報。</summary>
public sealed record ProcessInfo(
    int Pid,
    string Name,
    double CpuPercent,
    TimeSpan CpuTime,
    int ThreadCount,
    long MemoryBytes);

/// <summary>システム全体のCPU内訳など。</summary>
public sealed record SystemSnapshot(
    double UserPercent,
    double SystemPercent,
    double IdlePercent,
    int ProcessCount,
    int ThreadCount);

/// <summary>ある時点のスナップショット。UIスレッドに渡す不変データ。</summary>
public sealed record ProcessSnapshot(
    DateTime Timestamp,
    IReadOnlyList<ProcessInfo> Processes,
    SystemSnapshot System);
