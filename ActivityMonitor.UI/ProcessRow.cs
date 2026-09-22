using System.ComponentModel;
using ActivityMonitor.Core.Models;

namespace ActivityMonitor.UI;

public sealed class ProcessRow : INotifyPropertyChanged
{
	public event PropertyChangedEventHandler? PropertyChanged;
	public ProcessInfo Info { get; private set; }
	public int Pid => Info.Pid;
	
	// 表示文字列は「変わったときだけ」通知する
	private string _cpuText;
	private string _cpuTimeText;
	private string _threadsText;
	private string _memoryText;

	public string Name { get; }
	public string PidText { get; }
	public string CpuText => _cpuText;
	public string CpuTimeText => _cpuTimeText;
	public string ThreadsText => _threadsText;
	public string MemoryText => _memoryText;
	
	public ProcessRow(ProcessInfo info)
	{
		Info = info;
		Name = info.Name;
		PidText = info.Pid.ToString();
		_cpuText = FormatCpu(info);
		_cpuTimeText = FormatTime(info);
		_threadsText = info.ThreadCount.ToString();
		_memoryText = FormatMemory(info);
	}
	public void Update(ProcessInfo info)
	{
		if (info == Info) return;
		Info = info;

		Set(ref _cpuText, FormatCpu(info), nameof(CpuText));
		Set(ref _cpuTimeText, FormatTime(info), nameof(CpuTimeText));
		Set(ref _threadsText, info.ThreadCount.ToString(), nameof(ThreadsText));
		Set(ref _memoryText, FormatMemory(info), nameof(MemoryText));
	}
	private void Set(ref string field, string value, string prop)
	{
		if (field == value) return;
		field = value;
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
	}

	private static string FormatCpu(ProcessInfo i) => i.CpuPercent.ToString("F1");

	private static string FormatTime(ProcessInfo i)
	{
		var t = i.CpuTime;
		return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
	}

	private static string FormatMemory(ProcessInfo i) => i.MemoryBytes >= 1L << 30
		? $"{i.MemoryBytes / 1073741824.0:F2} GB"
		: $"{i.MemoryBytes / 1048576.0:F1} MB";
}
