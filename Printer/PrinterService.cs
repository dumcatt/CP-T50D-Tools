using System.Runtime.InteropServices;

namespace CPT50DTools.Printer;

/// <summary>
/// Owns the connection to the printer: polls status once a second on a background thread
/// (like SOUND VOLTEX's PrinterDriverDriver) and runs print jobs. All SDK calls are serialized.
/// </summary>
public sealed class PrinterService : IDisposable
{
    private const int PollIntervalMs = 1000;
    private static readonly TimeSpan TransferTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PrintTimeout = TimeSpan.FromSeconds(120);

    private readonly PrinterNative _sdk;
    private readonly object _sdkLock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _pollThread;
    private readonly Action<string>? _log;

    private PrinterInfo? _printer;
    private string? _firmware;
    private uint _nextJobId = 1;
    private volatile bool _jobActive;
    private volatile bool _standby;
    private readonly AutoResetEvent _pollNow = new(false);
    private volatile PrinterSnapshot _snapshot = new() { State = PrinterState.Searching, StatusText = "Searching for printer..." };

    /// <summary>Raised on the polling thread whenever a new snapshot is taken.</summary>
    public event Action<PrinterSnapshot>? StatusChanged;

    public PrinterSnapshot Snapshot => _snapshot;
    public bool IsPrinting => _jobActive;
    public bool IsStandby => _standby;
    public PrinterNative Sdk => _sdk;

    /// <summary>
    /// Paper / ink change, as in the game's PAPER/INK CHANGE test menu: the game sends no printer command,
    /// it just stops talking to the printer so the door can be opened and the media swapped, then
    /// forces an immediate rediscovery so the new card / ribbon counts are read.
    /// </summary>
    public void SetStandby(bool standby)
    {
        if (standby && _jobActive) throw new InvalidOperationException("Wait for the current card to finish printing.");
        if (_standby == standby) return;
        _standby = standby;
        if (standby)
        {
            lock (_sdkLock) { } // let any in-flight poll finish before reporting standby
            Publish(new PrinterSnapshot
            {
                State = PrinterState.Standby,
                StatusCode = _snapshot.StatusCode,
                StatusText = "Standby - paper / ink change",
                Printer = _snapshot.Printer,
                Firmware = _snapshot.Firmware,
            });
        }
        else
        {
            lock (_sdkLock) _printer = null; // rediscover, like the game's forced poll
            Publish(new PrinterSnapshot { State = PrinterState.Searching, StatusText = "Searching for printer..." });
            _pollNow.Set();
        }
    }

    public PrinterService(PrinterNative sdk, Action<string>? log = null)
    {
        _sdk = sdk;
        _log = log;
        _pollThread = new Thread(PollLoop) { IsBackground = true, Name = "Printer status poll" };
        _pollThread.Start();
    }

    #region Polling

    private void PollLoop()
    {
        var token = _cts.Token;
        while (!token.IsCancellationRequested)
        {
            // Don't talk to the printer while the SDK worker is streaming a job over USB, or in standby.
            if (!_jobActive && !_standby)
            {
                try
                {
                    var s = Poll();
                    if (!_standby) Publish(s);
                }
                catch (Exception ex)
                {
                    Publish(new PrinterSnapshot { State = PrinterState.Error, StatusText = $"Status poll failed: {ex.Message}" });
                }
            }
            WaitHandle.WaitAny(new[] { token.WaitHandle, _pollNow }, PollIntervalMs);
        }
    }

    /// <summary>Polls the printer now (rediscovering it if needed) and returns the new snapshot.</summary>
    public PrinterSnapshot Refresh()
    {
        if (_jobActive || _standby) return _snapshot;
        var s = Poll();
        Publish(s);
        return s;
    }

    private void Publish(PrinterSnapshot s)
    {
        var old = _snapshot;
        _snapshot = s;
        if (old.StatusCode != s.StatusCode || old.Printer?.PortNum != s.Printer?.PortNum)
            _log?.Invoke($"Printer: {s.StatusText}");
        StatusChanged?.Invoke(s);
    }

    private PrinterSnapshot Poll()
    {
        lock (_sdkLock)
        {
            uint found = 0, ready = 0;
            // Full discovery while disconnected or initializing, as SDVX does.
            if (_printer == null || _snapshot.StatusCode is PrinterStatusCodes.NotInitialized or
                    PrinterStatusCodes.Initializing or PrinterStatusCodes.Standby or PrinterStatusCodes.Rediscover)
            {
                uint ret = _sdk.SearchPrinters(out var info, out found, out ready);
                if (ret != 0 || found == 0)
                {
                    _printer = null;
                    _firmware = null;
                    return new PrinterSnapshot
                    {
                        State = PrinterState.Disconnected,
                        StatusCode = ret == 0 ? PrinterStatusCodes.NotInitialized : ret,
                        StatusText = found == 0 || ret == PrinterStatusCodes.NotInitialized
                            ? "Printer offline"
                            : $"Printer offline - {PrinterStatusCodes.Format(ret)}",
                        Found = found,
                        Ready = ready,
                    };
                }
                if (_printer == null || _printer.Value.PortNum != info.PortNum)
                {
                    _log?.Invoke($"Found {info.ModelName} on port {info.PortNum} (model 0x{info.ModelId:X4}, serial '{info.SerialString}', " +
                                 $"sub-model 0x{info.SubModel:X4}); {found} found, {ready} ready.");
                    _firmware = null;
                }
                _printer = info;
            }

            var p = _printer.Value;
            uint status = _sdk.CheckPrinter(ref p, out var raw);
            short? paper = _sdk.CheckPaperRemain(ref p, out short remain) == 0 ? remain : null;
            byte[]? media = _sdk.GetMediaType(ref p, out var m) == 0 ? m : null;
            short? temp = _sdk.GetTempInfo(ref p, out short t) == 0 ? t : null;
            uint? total = _sdk.GetTotalPrintCount(ref p, out uint c) == 0 ? c : null;
            if (_firmware == null && _sdk.GetFirmwareInfo(ref p, out var fw) == 0) _firmware = fw;
            if (status == PrinterStatusCodes.NotInitialized) _printer = null;

            var state = PrinterStatusCodes.ToState(status);
            string text = state switch
            {
                PrinterState.Ready => paper is { } n ? $"{p.ModelName} ready ({n} cards left)" : $"{p.ModelName} ready",
                PrinterState.Printing => "Printing card...",
                PrinterState.Cooling => "Thermal head cooling...",
                PrinterState.Searching => PrinterStatusCodes.Describe(status),
                PrinterState.Disconnected => "Printer offline",
                _ => PrinterStatusCodes.Format(status),
            };
            return new PrinterSnapshot
            {
                State = state,
                StatusCode = status,
                StatusText = text,
                Printer = p,
                Found = found,
                Ready = ready,
                Firmware = _firmware,
                RawStatus = raw,
                RawMedia = media,
                PaperRemain = paper,
                HeadTemp = temp,
                TotalPrints = total,
            };
        }
    }

    #endregion

    #region Printing

    /// <summary>
    /// Sends a 1072x712 landscape bitmap to the printer and waits until the card is out.
    /// Progress messages are reported through <paramref name="progress"/>.
    /// </summary>
    public async Task PrintAsync(Bitmap printBitmap, PrintJobSettings settings, IProgress<string>? progress = null)
    {
        if (_jobActive) throw new InvalidOperationException("A print job is already in progress.");
        if (_standby) throw new InvalidOperationException("The printer is in standby for a paper / ink change.\nResume it from Options > Printer status first.");

        // Status reads occasionally fail for one poll, so give the printer a few tries before refusing.
        var snap = await Task.Run(Refresh);
        for (int attempt = 0; attempt < 3 && (snap.Printer == null || !PrinterStatusCodes.CanPrint(snap.StatusCode)); attempt++)
        {
            await Task.Delay(700);
            snap = await Task.Run(Refresh);
        }
        if (snap.Printer is not { } printer)
            throw new PrinterException(snap.StatusCode, $"No CP-T50D printer is connected ({snap.StatusText}).");
        if (!PrinterStatusCodes.CanPrint(snap.StatusCode))
            throw new PrinterException(snap.StatusCode, $"The printer is not ready: {PrinterStatusCodes.Format(snap.StatusCode)}.");
        if (_sdk.DataFolderHooked && _sdk.DataFolder == null)
            throw new PrinterException(PrinterStatusCodes.NotInitialized,
                "No printer data folder (cpusbxpkm_data with KM1_LUT.lut / KM1_N_*.csv) is set.\n" +
                "Use File > Load game data folder to load the game; its contents\\ext\\cpusbxpkm_data folder is used.");

        // Everything the SDK reads lives in unmanaged memory until the job callback says it's done.
        IntPtr pixels = PrintJobBuilder.ToBgrBottomUp(printBitmap);
        IntPtr cfgPtr = Alloc(PrintJobBuilder.BuildConfig());
        IntPtr imgPtr = Alloc(PrintJobBuilder.BuildDescriptor(pixels));
        IntPtr optsPtr = Alloc(PrintJobBuilder.BuildOptions(settings, loadLut: _sdk.DataFolderHooked && _sdk.DataFolder != null));
        IntPtr printerPtr = Alloc(printer);

        var done = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
        PrintCallback callback = (status, stage, sub, jobId, extra) =>
        {
            _log?.Invoke($"Print job #{jobId}: {PrinterStatusCodes.Format(status)}, stage {stage}, progress {sub}, extra {extra}");
            if (stage != 0 && PrinterStatusCodes.CanPrint(status))
                progress?.Report($"Sending card to printer (stage {stage}, {sub})...");
            else
                done.TrySetResult(status);
        };

        _jobActive = true;
        bool leakBuffers = false;
        try
        {
            uint jobId = _nextJobId++;
            await Task.Run(() =>
            {
                lock (_sdkLock)
                {
                    if (settings.Preheat)
                    {
                        progress?.Report("Pre-heating thermal head...");
                        uint ph = _sdk.PreHeat(ref printer);
                        _log?.Invoke($"CPU9PreHeat -> {PrinterStatusCodes.Format(ph)}");
                    }

                    var opts = Marshal.PtrToStructure<JobOptions>(optsPtr);
                    _log?.Invoke($"Submitting print job #{jobId} ({PrinterNative.PrintWidth}x{PrinterNative.PrintHeight}, " +
                                 $"mode 0x{opts.ColorConvMode:X4}, code 0x{opts.DensityCode:X4}, LUT {(opts.Opt06 >= 0 ? "KM1_LUT.lut" : "built-in")})");
                    progress?.Report("Sending card to printer...");
                    uint ret = _sdk.SendImagePrint(cfgPtr, imgPtr, optsPtr, callback, jobId, printerPtr);
                    if (ret != 0)
                        throw new PrinterException(ret, $"CPUASendImagePrint rejected the job: {PrinterStatusCodes.Format(ret)}.");
                }
            });

            // Step 1: the SDK's worker thread colour-converts and streams the planes over USB.
            var finished = await Task.WhenAny(done.Task, Task.Delay(TransferTimeout));
            if (finished != done.Task)
            {
                // The SDK may still be reading the buffers; never free them under it.
                leakBuffers = true;
                throw new PrinterException(PrinterStatusCodes.Busy, "Timed out waiting for the printer SDK to send the card.");
            }
            uint result = done.Task.Result;
            if (result != 0)
                throw new PrinterException(result, $"The printer SDK failed while processing the card: {PrinterStatusCodes.Format(result)}.");
        }
        catch
        {
            _jobActive = false;
            throw;
        }
        finally
        {
            if (leakBuffers)
            {
                // The SDK may still call back later; keep the delegate alive for the process lifetime.
                lock (AbandonedCallbacks) AbandonedCallbacks.Add(callback);
            }
            else
            {
                foreach (var ptr in new[] { pixels, cfgPtr, imgPtr, optsPtr, printerPtr })
                    Marshal.FreeHGlobal(ptr);
            }
            GC.KeepAlive(callback);
        }

        // Step 2: data is in the printer; poll until it returns from Busy / Cooling to Ready.
        _jobActive = false;
        progress?.Report("Printing card...");
        var start = DateTime.Now;
        while (true)
        {
            await Task.Delay(PollIntervalMs);
            var s = await Task.Run(Refresh);
            if (s.StatusCode == PrinterStatusCodes.Ok) break;
            if (s.State is PrinterState.Error or PrinterState.Disconnected)
                throw new PrinterException(s.StatusCode, $"The printer reported an error while printing: {PrinterStatusCodes.Format(s.StatusCode)}.");
            progress?.Report(s.State == PrinterState.Cooling ? "Thermal head cooling..." : $"Printing card... ({(int)(DateTime.Now - start).TotalSeconds}s)");
            if (DateTime.Now - start > PrintTimeout)
            {
                _log?.Invoke("Timed out waiting for the printer to finish; it may still be printing.");
                break;
            }
        }
    }

    private static readonly List<PrintCallback> AbandonedCallbacks = new();

    private static IntPtr Alloc<T>(T value) where T : struct
    {
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
        Marshal.StructureToPtr(value, ptr, false);
        return ptr;
    }

    /// <summary>Sends CPU9PreHeat on its own.</summary>
    public uint PreHeat()
    {
        if (_jobActive || _standby) return PrinterStatusCodes.Busy;
        lock (_sdkLock)
        {
            if (_printer is not { } p) return PrinterStatusCodes.NotInitialized;
            return _sdk.PreHeat(ref p);
        }
    }

    #endregion

    public void Dispose()
    {
        _cts.Cancel();
        _pollThread.Join(2000);
        _cts.Dispose();
        _pollNow.Dispose();
    }
}

public sealed class PrinterException : Exception
{
    public uint StatusCode { get; }
    public PrinterException(uint statusCode, string message) : base(message) => StatusCode = statusCode;
}
