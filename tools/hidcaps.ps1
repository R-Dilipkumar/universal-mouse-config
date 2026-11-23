# Read-only: lists HID collections of VID 1BCF PID 08A0 with usage page, usage and report sizes.
# Opens each device with zero access rights (no reads or writes to the mouse).
$src = @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

public static class HidProbe {
  [StructLayout(LayoutKind.Sequential)] struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid g; public int Flags; public IntPtr Reserved; }
  [StructLayout(LayoutKind.Sequential)] public struct HIDP_CAPS {
    public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
    public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
      NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices, NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices; }
  [StructLayout(LayoutKind.Sequential)] public struct HIDP_VALUE_CAPS {
    public ushort UsagePage; public byte ReportID; public byte IsAlias; public ushort BitField, LinkCollection, LinkUsage, LinkUsagePage;
    public byte IsRange, IsStringRange, IsDesignatorRange, IsAbsolute, HasNull, Reserved; public ushort BitSize, ReportCount;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)] public ushort[] Reserved2;
    public uint UnitsExp, Units; public int LogicalMin, LogicalMax, PhysicalMin, PhysicalMax;
    public ushort UsageMin, UsageMax, StringMin, StringMax, DesignatorMin, DesignatorMax, DataIndexMin, DataIndexMax; }
  [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
  [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
  [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
  [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, out HIDP_CAPS c);
  [DllImport("hid.dll")] static extern int HidP_GetValueCaps(int type, [Out] HIDP_VALUE_CAPS[] c, ref ushort n, IntPtr p);
  [DllImport("hid.dll")] static extern int HidP_GetButtonCaps(int type, [Out] byte[] c, ref ushort n, IntPtr p);
  [DllImport("setupapi.dll")] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr h, int f);
  [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref SP_DEVICE_INTERFACE_DATA did);
  [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref SP_DEVICE_INTERFACE_DATA did, IntPtr buf, int size, out int req, IntPtr dev);
  [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
  [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern SafeFileHandle CreateFile(string n, uint a, uint s, IntPtr sec, uint c, uint f, IntPtr t);

  public static List<string> Run(string match) {
    var outp = new List<string>(); Guid g; HidD_GetHidGuid(out g);
    IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12);
    var did = new SP_DEVICE_INTERFACE_DATA(); did.cbSize = Marshal.SizeOf(did);
    for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref did); i++) {
      int req; SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, out req, IntPtr.Zero);
      IntPtr buf = Marshal.AllocHGlobal(req); Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
      string path = null;
      if (SetupDiGetDeviceInterfaceDetail(set, ref did, buf, req, out req, IntPtr.Zero)) path = Marshal.PtrToStringUni(buf + 4);
      Marshal.FreeHGlobal(buf);
      if (path == null || path.IndexOf(match, StringComparison.OrdinalIgnoreCase) < 0) continue;
      using (var h = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero)) {
        if (h.IsInvalid) { outp.Add(path + " -> open failed " + Marshal.GetLastWin32Error()); continue; }
        IntPtr pp; if (!HidD_GetPreparsedData(h, out pp)) { outp.Add(path + " -> no preparsed data"); continue; }
        HIDP_CAPS c; HidP_GetCaps(pp, out c);
        string line = string.Format("{0}\n  UsagePage=0x{1:X4} Usage=0x{2:X4} InLen={3} OutLen={4} FeatLen={5} InButtonCaps={6}",
          path, c.UsagePage, c.Usage, c.InputReportByteLength, c.OutputReportByteLength, c.FeatureReportByteLength, c.NumberInputButtonCaps);
        foreach (var t in new[] { 0, 1, 2 }) {
          ushort n = t == 0 ? c.NumberInputValueCaps : t == 1 ? c.NumberOutputValueCaps : c.NumberFeatureValueCaps;
          if (n == 0) continue;
          var vc = new HIDP_VALUE_CAPS[n]; HidP_GetValueCaps(t, vc, ref n, pp);
          foreach (var v in vc) line += string.Format("\n  {0} value: ReportID={1} UsagePage=0x{2:X4} Usage=0x{3:X4} BitSize={4} Count={5}",
            new[] { "Input", "Output", "Feature" }[t], v.ReportID, v.UsagePage, v.UsageMin, v.BitSize, v.ReportCount);
        }
        outp.Add(line); HidD_FreePreparsedData(pp);
      }
    }
    SetupDiDestroyDeviceInfoList(set); return outp;
  }
}
"@
Add-Type -TypeDefinition $src
[HidProbe]::Run("vid_1bcf&pid_08a0") | ForEach-Object { $_ }
