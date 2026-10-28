param([Parameter(Mandatory)][string]$Path)
$ErrorActionPreference = 'Stop'
if (!('MsiPackageFlags' -as [type])) {
    Add-Type @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class MsiPackageFlags {
    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    static extern uint MsiGetSummaryInformationW(uint database, string path, uint count, out uint summary);
    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    static extern uint MsiSummaryInfoSetPropertyW(uint summary, uint property, uint type, int value, IntPtr time, string text);
    [DllImport("msi.dll")]
    static extern uint MsiSummaryInfoPersist(uint summary);
    [DllImport("msi.dll")]
    static extern uint MsiCloseHandle(uint handle);
    static void Check(uint result) { if (result != 0) throw new Win32Exception((int)result); }
    public static void Set(string path) {
        uint summary;
        Check(MsiGetSummaryInformationW(0, path, 1, out summary));
        try {
            // PID_WORDCOUNT (15), VT_I4 (3), compressed (2) + LUA (8).
            Check(MsiSummaryInfoSetPropertyW(summary, 15, 3, 10, IntPtr.Zero, null));
            Check(MsiSummaryInfoPersist(summary));
        } finally { MsiCloseHandle(summary); }
    }
}
'@
}
[MsiPackageFlags]::Set([IO.Path]::GetFullPath($Path))
