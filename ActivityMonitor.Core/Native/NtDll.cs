using System.Runtime.InteropServices;

namespace ActivityMonitor.Core.Native;

internal static class NtDll
{
    public const int SystemProcessInformation = 5;
    public const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

    [DllImport("ntdll.dll")]
    public static extern int NtQuerySystemInformation(
        int systemInformationClass,
        IntPtr systemInformation,
        int systemInformationLength,
        out int returnLength);
}
