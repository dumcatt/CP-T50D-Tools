using System.Runtime.InteropServices;
using System.Text;

namespace CPT50DTools.Printer;

/// <summary>12-byte printer handle populated by CPUXSearchPrinters.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct PrinterInfo
{
    public ushort PortNum;      // +0x00: USB device index (0..31)
    public ushort ModelId;      // +0x02: 0x0870..0x0873 = CP-T50D, 0x0875 = CP-M50D
    public fixed byte Serial[6]; // +0x04: 6-byte unit serial
    public ushort SubModel;     // +0x0A: media / capability flags (e.g. 0x0049 = holo/overcoat)

    public string SerialString
    {
        get
        {
            fixed (byte* p = Serial)
                return Encoding.ASCII.GetString(p, 6).Trim('\0', ' ');
        }
    }

    public string ModelName => ModelId switch
    {
        >= 0x0870 and <= 0x0873 => "CP-T50D",
        0x0875 => "CP-M50D",
        _ => $"Unknown (0x{ModelId:X4})",
    };
}

/// <summary>36-byte print configuration (RCX in CPUASendImagePrint).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PrintConfig
{
    public uint StructVersion;  // +0x00: 0x66
    public uint ModeFlags;      // +0x04: 0xA1 (bit 0=cut, bit 5=filter, bit 7=overcoat)
    public uint Reserved08;
    public ushort Width;        // +0x0C: 1072
    public ushort Height;       // +0x0E: 712
    public ushort Param10;
    public ushort CutLength;    // +0x12: 1
    public ushort Param14;      // +0x14: 1
    public ushort Param16;
    public ushort Param18;
    public ushort FilterMode;
    public ushort Param1C;
    public ushort UseOvercoat2; // +0x1E: 0 for CPUASendImagePrint
    public ushort Param20;
    public ushort Padding22;
}

[StructLayout(LayoutKind.Sequential)]
public struct NativeRect
{
    public int Left, Top, Right, Bottom;
}

/// <summary>Image buffer descriptor (RDX in CPUASendImagePrint).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct ImageDescriptor
{
    public IntPtr PixelBuffer;  // +0x00: 24-bit BGR buffer
    public int RowStride;       // +0x08: +3216 = bottom-up, -3216 = top-down
    public NativeRect Rect;     // +0x0C: {0, 0, 1072, 712}
}

/// <summary>64-byte job options (R8 in CPUASendImagePrint).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct JobOptions
{
    public uint StructVersion;  // +0x00: 0x68
    public short Opt04;         // +0x04: -1
    public short Opt06;         // +0x06: -1 skips the external .lut, 0 loads KM1_LUT.lut
    public short Opt08;         // +0x08: -1
    public fixed ushort Pad0A[3];
    public IntPtr CustomLut10;  // +0x10
    public short SharpnessMode; // +0x18: 4 (SDVX default)
    public fixed ushort Pad1A[3];
    public IntPtr CustomLut20;  // +0x20
    public short Opt28, Opt2A, Opt2C, Opt2E; // +0x28..+0x2E: -1
    public ulong Reserved30;
    public short ColorConvMode; // +0x38: 0x0101 standard, 0x0102 with KMAT pattern
    public short DensityCode;   // +0x3A: 0x0101..0x011E, or the KMAT pattern code
    public uint Reserved3C;
}

/// <summary>Invoked on the SDK's worker thread while a job is processed.</summary>
[UnmanagedFunctionPointer(CallingConvention.Winapi)]
public delegate void PrintCallback(uint status, ushort stage, ushort subProgress, uint jobId, uint extraStatus);

/// <summary>
/// Dynamically loaded CPUSBXPKM.dll. The DLL can only be loaded once per process,
/// so the SDK folder is fixed after the first successful <see cref="Load"/>.
/// </summary>
public sealed class PrinterNative
{
    public const int PrintWidth = 1072;
    public const int PrintHeight = 712;
    public const int RowStride = PrintWidth * 3;

    // RVA of the GetPrivateProfileStringA slot in CPUSBXPKM.dll's import table.
    private const int ProfileStringIatRva = 0x240A8;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void InitFn();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate uint SearchPrintersFn(ref PrinterInfo outList, uint listBytes, out uint found, out uint ready);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate uint CheckPrinterFn(byte[] outStatus, ref PrinterInfo printer);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate uint OutShortFn(out short value, ref PrinterInfo printer);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate uint OutUIntFn(out uint value, ref PrinterInfo printer);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate uint OutBytesFn(byte[] buffer, ref PrinterInfo printer);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate uint PreHeatFn(ref PrinterInfo printer);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint SendImagePrintFn(IntPtr cfg, IntPtr img, IntPtr opts, uint flags, PrintCallback callback, uint jobId, IntPtr printer);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint GetProfileStringFn(IntPtr app, IntPtr key, IntPtr def, IntPtr outBuf, uint size, IntPtr fileName);

    private static PrinterNative? _instance;
    public static PrinterNative? Instance => _instance;

    public string Folder { get; }
    /// <summary>True when DATA_FOLDER lookups are redirected to <see cref="DataFolder"/>.</summary>
    public bool DataFolderHooked { get; private set; }
    /// <summary>Folder with KM1_LUT.lut / KM1_N_*.csv / KMAT_*.raw; read by the SDK on demand.</summary>
    public string? DataFolder { get; set; }

    private readonly IntPtr _handle;
    private readonly SearchPrintersFn _search;
    private readonly CheckPrinterFn _check;
    private readonly OutShortFn _paperRemain;
    private readonly OutBytesFn _fwInfo;
    private readonly OutBytesFn _mediaType;
    private readonly OutShortFn _temp;
    private readonly OutUIntFn _totalCount;
    private readonly PreHeatFn _preHeat;
    private readonly SendImagePrintFn _sendImage;

    // Kept alive for the lifetime of the process: the DLL's import table points at it.
    private GetProfileStringFn? _profileHook;
    private GetProfileStringFn? _originalProfileString;

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectoryW(string? path);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool VirtualProtect(IntPtr address, UIntPtr size, uint newProtect, out uint oldProtect);

    private PrinterNative(string folder, IntPtr handle, Action<string>? log)
    {
        Folder = folder;
        _handle = handle;
        _search = Bind<SearchPrintersFn>("CPUXSearchPrinters");
        _check = Bind<CheckPrinterFn>("CPU9CheckPrinter");
        _paperRemain = Bind<OutShortFn>("CPU9CheckPaperRemain");
        _fwInfo = Bind<OutBytesFn>("CPU9GetFWInfo");
        _mediaType = Bind<OutBytesFn>("CPU9GetMediaType");
        _temp = Bind<OutShortFn>("CPU9GetTempInfo");
        _totalCount = Bind<OutUIntFn>("CPU9GetTotalPrintCount");
        _preHeat = Bind<PreHeatFn>("CPU9PreHeat");
        _sendImage = Bind<SendImagePrintFn>("CPUASendImagePrint");
        var init = Bind<InitFn>("CPUXInit");

        InstallDataFolderHook(log);
        // Must run before any other SDK call; otherwise everything returns 1.
        init();
    }

    /// <summary>Loads CPUSBXPKM.dll from <paramref name="folder"/> (once per process).</summary>
    public static PrinterNative Load(string folder, string? dataFolder, Action<string>? log = null)
    {
        if (_instance != null)
        {
            if (!string.Equals(Path.GetFullPath(_instance.Folder), Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"The printer SDK is already loaded from {_instance.Folder}.\nRestart the application to use a different SDK folder.");
            _instance.DataFolder = dataFolder;
            return _instance;
        }
        if (!Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("CPUSBXPKM.dll is a 64-bit DLL; the application must run as x64.");

        string dll = Path.Combine(folder, "CPUSBXPKM.dll");
        // MUsbPuIf.dll lives next to CPUSBXPKM.dll and may be loaded with a plain LoadLibrary.
        SetDllDirectoryW(folder);
        var handle = NativeLibrary.Load(dll, typeof(PrinterNative).Assembly,
            DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.System32);
        try
        {
            _instance = new PrinterNative(folder, handle, log) { DataFolder = dataFolder };
        }
        catch
        {
            NativeLibrary.Free(handle);
            throw;
        }
        return _instance;
    }

    private T Bind<T>(string name) where T : Delegate
    {
        if (!NativeLibrary.TryGetExport(_handle, name, out var fn))
            throw new EntryPointNotFoundException($"CPUSBXPKM.dll does not export {name}.");
        return Marshal.GetDelegateForFunctionPointer<T>(fn);
    }

    /// <summary>
    /// Redirects the SDK's DATA_FOLDER lookup (GetPrivateProfileStringA on CPUSBXPKM.ini) to
    /// <see cref="DataFolder"/>, so the LUT / KMAT files don't need an ini in C:\Windows\System32.
    /// Only patches the import slot if it really holds GetPrivateProfileStringA.
    /// </summary>
    private void InstallDataFolderHook(Action<string>? log)
    {
        try
        {
            var kernel32 = NativeLibrary.Load("kernel32.dll");
            var real = NativeLibrary.GetExport(kernel32, "GetPrivateProfileStringA");
            var slot = _handle + ProfileStringIatRva;
            if (Marshal.ReadIntPtr(slot) != real)
            {
                log?.Invoke("Printer SDK: unexpected CPUSBXPKM.dll build; DATA_FOLDER comes from CPUSBXPKM.ini.");
                return;
            }

            _originalProfileString = Marshal.GetDelegateForFunctionPointer<GetProfileStringFn>(real);
            _profileHook = HookedGetProfileString;
            var hookPtr = Marshal.GetFunctionPointerForDelegate(_profileHook);

            const uint PAGE_READWRITE = 0x04;
            if (!VirtualProtect(slot, (UIntPtr)IntPtr.Size, PAGE_READWRITE, out uint old))
            {
                log?.Invoke("Printer SDK: could not redirect DATA_FOLDER (VirtualProtect failed).");
                return;
            }
            Marshal.WriteIntPtr(slot, hookPtr);
            VirtualProtect(slot, (UIntPtr)IntPtr.Size, old, out _);
            DataFolderHooked = true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"Printer SDK: could not redirect DATA_FOLDER: {ex.Message}");
        }
    }

    private uint HookedGetProfileString(IntPtr app, IntPtr key, IntPtr def, IntPtr outBuf, uint size, IntPtr fileName)
    {
        string? data = DataFolder;
        if (data != null && outBuf != IntPtr.Zero && key != IntPtr.Zero &&
            string.Equals(Marshal.PtrToStringAnsi(key), "DATA_FOLDER", StringComparison.OrdinalIgnoreCase))
        {
            byte[] bytes = Encoding.ASCII.GetBytes(data + "\0");
            if (bytes.Length <= size)
            {
                Marshal.Copy(bytes, 0, outBuf, bytes.Length);
                return (uint)(bytes.Length - 1);
            }
        }
        return _originalProfileString!(app, key, def, outBuf, size, fileName);
    }

    #region SDK calls (not thread safe; PrinterService serializes them)

    public uint SearchPrinters(out PrinterInfo printer, out uint found, out uint ready)
    {
        printer = default;
        return _search(ref printer, (uint)Marshal.SizeOf<PrinterInfo>(), out found, out ready);
    }

    public uint CheckPrinter(ref PrinterInfo printer, out byte[] raw)
    {
        raw = new byte[16];
        return _check(raw, ref printer);
    }

    public uint CheckPaperRemain(ref PrinterInfo printer, out short remain) => _paperRemain(out remain, ref printer);

    public uint GetFirmwareInfo(ref PrinterInfo printer, out string version)
    {
        var buf = new byte[18];
        uint ret = _fwInfo(buf, ref printer);
        string Part(int i) => Encoding.ASCII.GetString(buf, i * 6, 6).Trim('\0', ' ');
        version = ret == 0 ? $"{Part(0)}.{Part(1)}.{Part(2)}" : "";
        return ret;
    }

    public uint GetMediaType(ref PrinterInfo printer, out byte[] raw)
    {
        raw = new byte[6];
        return _mediaType(raw, ref printer);
    }

    public uint GetTempInfo(ref PrinterInfo printer, out short temp) => _temp(out temp, ref printer);

    public uint GetTotalPrintCount(ref PrinterInfo printer, out uint count) => _totalCount(out count, ref printer);

    public uint PreHeat(ref PrinterInfo printer) => _preHeat(ref printer);

    /// <summary>All pointers must stay valid until the callback reports the job finished.</summary>
    public uint SendImagePrint(IntPtr cfg, IntPtr img, IntPtr opts, PrintCallback callback, uint jobId, IntPtr printer) =>
        _sendImage(cfg, img, opts, 0, callback, jobId, printer);

    #endregion
}
