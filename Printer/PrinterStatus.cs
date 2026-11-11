namespace CPT50DTools.Printer;

public enum PrinterState
{
    SdkNotLoaded,
    Disconnected,
    Searching,
    Ready,
    Printing,
    Cooling,
    Error,
    /// <summary>Communication paused for a paper / ink change (the game's PAPER/INK CHANGE test menu).</summary>
    Standby,
}

/// <summary>Status / error codes returned by CPUSBXPKM.dll.</summary>
public static class PrinterStatusCodes
{
    public const uint Ok = 0x000;
    public const uint NotInitialized = 0x001;
    public const uint Initializing = 0x064;
    public const uint Standby = 0x065;
    public const uint Busy = 0x066;
    public const uint Cooling = 0x068;
    public const uint Notification = 0x069;
    public const uint InvalidParameter = 0x3E8;
    public const uint MediaEmpty = 0x3F6;
    public const uint UnsupportedModel = 0x3FA;
    public const uint Rediscover = 0x5DC;

    public static string Describe(uint code) => code switch
    {
        Ok => "Ready",
        NotInitialized => "Disconnected / not initialized",
        Initializing => "Initializing",
        Standby => "Standby / door check",
        Busy => "Printing",
        Cooling => "Cooling thermal head",
        Notification => "Status notification",
        0x0C9 => "Internal object / parameter error",
        0x0CA => "Out of memory",
        0x0CB => "Thread synchronization error",
        InvalidParameter => "Invalid parameter",
        >= 0x3E9 and <= 0x3F5 => "Mechanical / sensor error (paper jam or cover open)",
        MediaEmpty => "Paper empty / ribbon exhausted / media mismatch",
        >= 0x3F7 and <= 0x3F9 => "Model mismatch / unsupported command",
        UnsupportedModel => "Unsupported printer model",
        _ => "Unknown hardware error",
    };

    public static string Format(uint code) => $"{Describe(code)} (0x{code:X3})";

    /// <summary>States in which SOUND VOLTEX accepts a new print job.</summary>
    public static bool CanPrint(uint code) => code is Ok or Busy or Cooling;

    public static PrinterState ToState(uint code) => code switch
    {
        Ok => PrinterState.Ready,
        NotInitialized => PrinterState.Disconnected,
        Initializing or Standby or Rediscover => PrinterState.Searching,
        Busy => PrinterState.Printing,
        Cooling => PrinterState.Cooling,
        Notification => PrinterState.Ready,
        _ => PrinterState.Error,
    };
}

/// <summary>Snapshot of the printer's state from the last poll.</summary>
public sealed record PrinterSnapshot
{
    public PrinterState State { get; init; }
    public uint StatusCode { get; init; } = PrinterStatusCodes.NotInitialized;
    public string StatusText { get; init; } = "";
    public PrinterInfo? Printer { get; init; }
    public uint Found { get; init; }
    public uint Ready { get; init; }
    public string? Firmware { get; init; }
    public byte[]? RawStatus { get; init; }
    public byte[]? RawMedia { get; init; }
    public short? PaperRemain { get; init; }
    public short? HeadTemp { get; init; }
    public uint? TotalPrints { get; init; }
    public DateTime Time { get; init; } = DateTime.Now;

    /// <summary>SDVX treats media with 'H' in the high byte of bytes [2..3] as holo/overcoat media.</summary>
    public bool? HoloMedia => RawMedia is { Length: >= 4 } m ? m[3] == 0x48 : null;
}
