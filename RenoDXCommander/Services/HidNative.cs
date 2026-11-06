// HidNative.cs -- the Windows HID / SetupAPI calls the native controller readers share
// (PlayStationPad.cs, SteamControllerPad.cs): list HID devices, open one shared, read its ids and caps.

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RenoDXCommander.Services;

internal static class HidNative
{
    private const uint DigcfPresent = 0x02, DigcfDeviceInterface = 0x10;
    private const uint GenericRead = 0x80000000, FileShareRead = 1, FileShareWrite = 2, OpenExisting = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInterfaceData
    {
        public int Size;
        public Guid ClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Attributes
    {
        public int Size;
        public ushort VendorId;
        public ushort ProductId;
        public ushort Version;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Caps
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
            NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
            NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] private static extern bool HidD_GetAttributes(SafeFileHandle device, ref Attributes attributes);
    [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr data);
    [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, ref Caps caps);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, uint index, ref DeviceInterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref DeviceInterfaceData data, IntPtr detail, int size, out int required, IntPtr info);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool ReadFile(SafeFileHandle file, byte[] buffer, uint toRead, out uint read, IntPtr overlapped);
    [DllImport("kernel32.dll")]
    internal static extern bool CancelIoEx(SafeFileHandle file, IntPtr overlapped);

    /// <summary>Every HID device's path.</summary>
    internal static List<string> DevicePaths()
    {
        var paths = new List<string>();
        HidD_GetHidGuid(out var guid);
        var set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) return paths;
        try
        {
            var data = new DeviceInterfaceData { Size = Marshal.SizeOf<DeviceInterfaceData>() };
            for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data); i++)
            {
                SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out int size, IntPtr.Zero);
                if (size <= 0) continue;
                var detail = Marshal.AllocHGlobal(size);
                try
                {
                    // SP_DEVICE_INTERFACE_DETAIL_DATA_W: cbSize is 8 on 64-bit, the path follows at offset 4
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetail(set, ref data, detail, size, out _, IntPtr.Zero)
                        && Marshal.PtrToStringUni(detail + 4) is { Length: > 0 } path)
                        paths.Add(path);
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return paths;
    }

    /// <summary>Opens a device shared with everything else (to read, or only to query); null when that's refused (e.g. exclusive use).</summary>
    internal static SafeFileHandle? Open(string path, bool read)
    {
        var h = CreateFile(path, read ? GenericRead : 0, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (!h.IsInvalid) return h;
        h.Dispose();
        return null;
    }

    internal static bool TryGetIds(SafeFileHandle h, out ushort vendor, out ushort product)
    {
        var a = new Attributes { Size = Marshal.SizeOf<Attributes>() };
        bool ok = HidD_GetAttributes(h, ref a);
        vendor = a.VendorId;
        product = a.ProductId;
        return ok;
    }

    /// <summary>The input report length and usage page (0xFF00 and up = vendor-defined); false when they can't be read.</summary>
    internal static bool TryGetCaps(SafeFileHandle h, out int inputReportLength, out ushort usagePage)
    {
        inputReportLength = 0;
        usagePage = 0;
        if (!HidD_GetPreparsedData(h, out var data)) return false;
        try
        {
            var caps = new Caps { Reserved = new ushort[17] };
            if (HidP_GetCaps(data, ref caps) != 0x00110000 /* HIDP_STATUS_SUCCESS */) return false;
            inputReportLength = caps.InputReportByteLength;
            usagePage = caps.UsagePage;
            return true;
        }
        finally { HidD_FreePreparsedData(data); }
    }

    internal static int InputReportLength(SafeFileHandle h) => TryGetCaps(h, out var length, out _) ? length : 0;
}
