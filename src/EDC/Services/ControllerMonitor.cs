using System.Globalization;
using System.Runtime.InteropServices;
using EDC.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace EDC.Services;

/// <summary>
/// Reads the game controller's battery every 30 s, off the UI thread.
/// <list type="bullet">
/// <item>Plugged in: XInput says the controller is wired, and gives no level.</item>
/// <item>Bluetooth: Windows keeps the level the controller reports on its device node, the figure the
/// Settings show. It stays there once the controller is off or plugged in, so the Bluetooth link must
/// be up for it to count.</item>
/// <item>Otherwise XInput's coarse level (Xbox wireless adapter), if any.</item>
/// </list>
/// </summary>
public sealed class ControllerMonitor : IDisposable
{
    private const string BluetoothClass = "{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}";
    private const string BatteryLevel = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly CancellationTokenSource _stop = new();
    private string? _lastError;

    public PadBattery Current { get; private set; } = PadBattery.None;

    /// <summary>Raised on the thread that called <see cref="Start"/> when the reading changes.</summary>
    public event Action? Changed;

    public void Start() => _ = Loop();

    private async Task Loop()
    {
        while (!_stop.IsCancellationRequested)
        {
            var pad = Current;
            try
            {
                pad = await Task.Run(ReadAsync);
                _lastError = null;
            }
            catch (Exception ex)
            {
                // Logged once, not every 30 s.
                if (ex.Message != _lastError)
                    AppLog.Write($"batterie de la manette illisible : {ex.Message}");
                _lastError = ex.Message;
            }

            if (pad != Current)
            {
                Current = pad;
                Changed?.Invoke();
            }

            try
            {
                await Task.Delay(Interval, _stop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static async Task<PadBattery> ReadAsync()
    {
        var xinput = ReadXInput();
        if (xinput.Link == PadLink.Wired)
            return xinput;

        return await ReadBluetoothAsync() ?? xinput;
    }

    private static async Task<PadBattery?> ReadBluetoothAsync()
    {
        var devices = await DeviceInformation.FindAllAsync(
            $"System.Devices.ClassGuid:=\"{BluetoothClass}\"", [BatteryLevel], DeviceInformationKind.Device);

        foreach (var device in devices)
        {
            if (!device.Properties.TryGetValue(BatteryLevel, out var level) || level is not IConvertible
                || !IsController(device.Name) || Address(device.Id) is not { } address)
                continue;

            using var link = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
            if (link?.ConnectionStatus == BluetoothConnectionStatus.Connected)
                return new PadBattery(PadLink.Wireless, Math.Clamp(Convert.ToInt32(level, CultureInfo.InvariantCulture), 0, 100));
        }

        return null;
    }

    // "Xbox Wireless Controller"; the other Bluetooth devices with a battery (earbuds…) are not watched.
    private static bool IsController(string name) =>
        name.Contains("controller", StringComparison.OrdinalIgnoreCase)
        || name.Contains("manette", StringComparison.OrdinalIgnoreCase)
        || name.Contains("gamepad", StringComparison.OrdinalIgnoreCase);

    // BTHLE\Dev_a88c3e0ccd01\7&fc821d3&0&a88c3e0ccd01: the address is in the second part.
    internal static ulong? Address(string instanceId)
    {
        var parts = instanceId.Split('\\');
        if (parts.Length < 2 || !parts[0].Equals("BTHLE", StringComparison.OrdinalIgnoreCase)
            || !parts[1].StartsWith("Dev_", StringComparison.OrdinalIgnoreCase))
            return null;

        return ulong.TryParse(parts[1][4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var address) ? address : null;
    }

    // ------------------------------------------------------------------------------------- XInput

    private const byte BatteryDevTypeGamepad = 0;
    private const byte BatteryTypeWired = 1;
    private const byte BatteryTypeAlkaline = 2;
    private const byte BatteryTypeNimh = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short ThumbLX, ThumbLY, ThumbRX, ThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputBattery
    {
        public byte Type;
        public byte Level;
    }

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputGetState(uint userIndex, out XInputState state);

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputGetBatteryInformation(uint userIndex, byte devType, ref XInputBattery battery);

    private static PadBattery ReadXInput()
    {
        for (uint user = 0; user < 4; user++)
        {
            var battery = default(XInputBattery);
            if (XInputGetState(user, out _) != 0 || XInputGetBatteryInformation(user, BatteryDevTypeGamepad, ref battery) != 0)
                continue;

            if (battery.Type == BatteryTypeWired)
                return PadBattery.Wired;
            if (battery.Type is BatteryTypeAlkaline or BatteryTypeNimh)
                return new PadBattery(PadLink.Wireless, battery.Level * 100 / 3);
        }

        return PadBattery.None;
    }

    // Cancelled, not disposed: a reading in flight still asks for its token on the way back.
    public void Dispose() => _stop.Cancel();
}
