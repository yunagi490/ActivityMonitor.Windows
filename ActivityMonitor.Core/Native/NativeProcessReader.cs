using System.Runtime.InteropServices;

namespace ActivityMonitor.Core.Native;

/// <summary>NtQuerySystemInformation が返す1プロセス分の生データ。時間は100ns単位。</summary>
internal readonly record struct RawProcess(
    int Pid,
    string Name,
    int ThreadCount,
    long PrivateWorkingSet,
    long WorkingSet,
    long UserTime,
    long KernelTime,
    long CreateTime);

/// <summary>
/// NtQuerySystemInformation(SystemProcessInformation) でプロセス一覧を一括取得する。
/// バッファは使い回し、足りなければ拡張する。x64 / ARM64 のレイアウト前提。
/// </summary>
internal sealed class NativeProcessReader : IDisposable
{
    // SYSTEM_PROCESS_INFORMATION (64bit) のオフセット
    private const int OffNextEntry = 0;          // ULONG
    private const int OffNumberOfThreads = 4;    // ULONG
    private const int OffWorkingSetPrivate = 8;  // LARGE_INTEGER
    private const int OffCreateTime = 32;        // LARGE_INTEGER
    private const int OffUserTime = 40;          // LARGE_INTEGER
    private const int OffKernelTime = 48;        // LARGE_INTEGER
    private const int OffImageNameLength = 56;   // USHORT (UNICODE_STRING.Length, bytes)
    private const int OffImageNameBuffer = 64;   // PWSTR
    private const int OffUniqueProcessId = 80;   // HANDLE
    private const int OffWorkingSetSize = 144;   // SIZE_T

    private IntPtr _buffer;
    private int _size;
    private readonly List<RawProcess> _result = new(512);

    public NativeProcessReader()
    {
        if (IntPtr.Size != 8)
            throw new PlatformNotSupportedException("x64 / ARM64 のみ対応しています。");
        Resize(512 * 1024);
    }

    private void Resize(int newSize)
    {
        if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
        _buffer = Marshal.AllocHGlobal(newSize);
        _size = newSize;
    }

    /// <summary>返却リストは次回の Read まで有効（使い回し）。</summary>
    public IReadOnlyList<RawProcess> Read()
    {
        while (true)
        {
            int status = NtDll.NtQuerySystemInformation(
                NtDll.SystemProcessInformation, _buffer, _size, out int needed);

            if (status == NtDll.StatusInfoLengthMismatch)
            {
                // 取得中にプロセスが増える可能性があるので余裕を持たせる
                Resize(Math.Max(needed, _size) + 64 * 1024);
                continue;
            }
            if (status < 0)
                throw new InvalidOperationException($"NtQuerySystemInformation failed: 0x{status:X8}");
            break;
        }

        _result.Clear();
        long offset = 0;
        while (true)
        {
            IntPtr p = _buffer + (nint)offset;

            uint next = (uint)Marshal.ReadInt32(p, OffNextEntry);
            int threads = Marshal.ReadInt32(p, OffNumberOfThreads);
            long privateWs = Marshal.ReadInt64(p, OffWorkingSetPrivate);
            long create = Marshal.ReadInt64(p, OffCreateTime);
            long user = Marshal.ReadInt64(p, OffUserTime);
            long kernel = Marshal.ReadInt64(p, OffKernelTime);
            int nameLen = (ushort)Marshal.ReadInt16(p, OffImageNameLength);
            IntPtr nameBuf = Marshal.ReadIntPtr(p, OffImageNameBuffer);
            int pid = (int)Marshal.ReadInt64(p, OffUniqueProcessId);
            long ws = Marshal.ReadInt64(p, OffWorkingSetSize);

            string name = nameLen > 0 && nameBuf != IntPtr.Zero
                ? Marshal.PtrToStringUni(nameBuf, nameLen / 2) ?? ""
                : "";

            _result.Add(new RawProcess(pid, name, threads, privateWs, ws, user, kernel, create));

            if (next == 0) break;
            offset += next;
        }

        return _result;
    }

    public void Dispose()
    {
        if (_buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
        }
    }
}
