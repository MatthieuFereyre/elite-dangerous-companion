using EDC.Resources;

namespace EDC.Core;

public enum PadLink { None, Wired, Wireless }

/// <summary>
/// The controller as last read: absent, plugged in (no level: Windows gives none over USB), or wireless
/// with its level, 0 to 100. Over the XInput fallback the level is coarse: 0, 33, 66 or 100.
/// </summary>
public sealed record PadBattery(PadLink Link, int Percent = 0)
{
    public static readonly PadBattery None = new(PadLink.None);
    public static readonly PadBattery Wired = new(PadLink.Wired);

    public string Text => Link switch
    {
        PadLink.Wired => Strings.PadWired,
        PadLink.Wireless => $"{Percent} %",
        _ => Strings.PadAbsent,
    };
}

/// <summary>
/// When to warn about the controller's battery: once on going under 20 %, once more under 10 %. A level
/// back to 30 % or more, or a cable, means it was charged: the warnings are armed again.
/// </summary>
public sealed class BatteryAlerts
{
    public const int Low = 20;
    public const int Critical = 10;
    private const int Rearm = 30;

    private int _warnedAt = int.MaxValue;

    /// <summary>The threshold just crossed, to warn about; null when there is nothing new to say.</summary>
    public int? Check(PadBattery pad)
    {
        if (pad.Link == PadLink.Wired || (pad.Link == PadLink.Wireless && pad.Percent >= Rearm))
        {
            _warnedAt = int.MaxValue;
            return null;
        }

        if (pad.Link != PadLink.Wireless)
            return null;

        foreach (var threshold in new[] { Critical, Low })
        {
            if (pad.Percent <= threshold)
            {
                if (_warnedAt <= threshold)
                    return null;

                _warnedAt = threshold;
                return threshold;
            }
        }

        return null;
    }
}
