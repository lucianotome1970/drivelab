// ============================================================================
//  DriveLab
//  HidBaseTransport.cs — Transporte HID real do volante DriveLab: enquadra payloads A0 e despacha reports por Report ID.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;
using HidSharp;

namespace DriveLab.Hid;

/// <summary>
/// Real <see cref="IBaseTransport"/> over USB HID. Frames A0 payloads with their HID Report ID
/// and sends them through an <see cref="IHidChannel"/>. Incoming reports are dispatched by
/// report id. StateReceived is raised on the channel's read thread; consumers (BaseSession)
/// marshal it to the UI thread.
/// </summary>
public sealed class HidBaseTransport : IBaseTransport, IDisposable
{
    private readonly IHidChannel _channel;
    private readonly object _pendingLock = new();
    private readonly Dictionary<byte, TaskCompletionSource<SettingValue>> _pendingReads = new();
    private static readonly TimeSpan DefaultReadTimeout = TimeSpan.FromMilliseconds(500);

    // O firmware da base tem UM único slot de read pendente (g_pendingField): um novo
    // SettingReadRequest sobrescreve o anterior. Se vários VMs (as 3 abas de settings + o card do
    // dash) disparam LoadAsync juntos no evento Connected, as leituras concorrentes se perdem e a
    // maioria dá timeout (o módulo "não carrega nada"). Este gate serializa as leituras — uma
    // ida-e-volta 0x15→0x16 por vez — casando com o slot único do firmware. (Escritas 0x14 são
    // imediatas no firmware e disparadas pelo usuário uma a uma, então não precisam de gate.)
    private readonly SemaphoreSlim _readGate = new(1, 1);

    public HidBaseTransport(IHidChannel channel)
    {
        _channel = channel;
        _channel.ReportReceived += OnReport;
    }

    public bool IsConnected { get; private set; }
    public FirmwareVersion FirmwareVersion { get; private set; }

    public event EventHandler<BaseState>? StateReceived;

    /// <summary>Usage page (HID) do canal vendor A0 da base.</summary>
    public const int A0UsagePage = 0xFF00;

    /// <summary>
    /// Predicado puro/testável: true só para a usage page do canal A0 (0xFF00). A base expõe UMA
    /// interface HID combinada com duas top-level collections — Generic-Desktop 0x01 (o volante FFB)
    /// e vendor 0xFF00 (A0 config/telemetria). O HidSharp enumera um <c>HidDevice</c> por top-level
    /// collection para VID 0x1209/PID 0x0001, então o transporte precisa escolher a 0xFF00.
    /// </summary>
    public static bool IsA0UsagePage(int usagePage) => usagePage == A0UsagePage;

    /// <summary>
    /// Lê a usage page da primeira top-level collection do device (best-effort: qualquer falha do
    /// parser do HidSharp vira 0 = "nenhuma usage page encontrada").
    /// </summary>
    internal static int GetTopUsagePage(HidDevice device)
    {
        try
        {
            var item = device.GetReportDescriptor().DeviceItems.FirstOrDefault();
            var usage = item?.Usages.GetAllValues().FirstOrDefault() ?? 0;
            return (int)(usage >> 16);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>True quando o HidDevice é a top-level collection do canal A0 (usage page 0xFF00).</summary>
    internal static bool IsA0Device(HidDevice device) => IsA0UsagePage(GetTopUsagePage(device));

    /// <summary>Detecta a base por VID/PID (autodetecção/hotplug). O PID 0x0001 é exclusivo da base,
    /// então a presença por VID/PID já basta — NÃO filtrar por usage page aqui: a base expõe UMA
    /// interface HID combinada (FFB + A0), e o macOS reporta o device-level usage como 0x00
    /// (indefinido, pois há duas top-level collections), então exigir 0xFF00 daria falso-negativo.
    /// A escolha da collection A0 (quando o SO enumera mais de um HidDevice, ex.: Windows) fica no
    /// <c>OpenAsync</c>/<c>HidSharpChannel</c>.</summary>
    public static bool IsDevicePresent()
    {
        try
        {
            return DeviceList.Local
                .GetHidDevices(BaseDeviceIdentity.VendorId, BaseDeviceIdentity.ProductId)
                .Any();
        }
        catch
        {
            return false;
        }
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        IsConnected = await _channel.OpenAsync(BaseDeviceIdentity.VendorId, BaseDeviceIdentity.ProductId);
    }

    public Task DisconnectAsync()
    {
        _channel.Close();
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task SendDirectControlAsync(BaseDirectControl control) =>
        _channel.WriteAsync(Frame(BaseReportIds.DirectControl, control.ToBytes()));

    public Task SendCommandAsync(BaseCommand command, byte arg = 0) =>
        _channel.WriteAsync(Frame(BaseReportIds.Command, new CommandReport((byte)command, arg).ToBytes()));

    public Task WriteSettingAsync(BaseSettingId id, SettingValue value) =>
        _channel.WriteAsync(Frame(BaseReportIds.SettingWrite, new SettingReport((byte)id, 0, value).ToBytes()));

    public Task<SettingValue> ReadSettingAsync(BaseSettingId id) => ReadSettingAsync(id, DefaultReadTimeout);

    public async Task<SettingValue> ReadSettingAsync(BaseSettingId id, TimeSpan timeout)
    {
        // Serializa: uma leitura por vez (o firmware só guarda um read pendente).
        await _readGate.WaitAsync();
        try
        {
            var tcs = new TaskCompletionSource<SettingValue>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_pendingLock) _pendingReads[(byte)id] = tcs;

            await _channel.WriteAsync(Frame(BaseReportIds.SettingReadRequest, new SettingReadRequestReport((byte)id, 0).ToBytes()));

            using var cts = new CancellationTokenSource(timeout);
            using (cts.Token.Register(() =>
            {
                lock (_pendingLock) _pendingReads.Remove((byte)id);
                tcs.TrySetException(new TimeoutException($"No SettingValue reply for field {(byte)id} within {timeout.TotalMilliseconds}ms"));
            }))
            {
                return await tcs.Task;
            }
        }
        finally
        {
            _readGate.Release();
        }
    }

    private void OnReport(object? sender, byte[] wire)
    {
        if (wire.Length < 1 + ReportConstants.ReportSize)
            return;

        try
        {
            var reportId = wire[0];
            var payload = wire.AsSpan(1, ReportConstants.ReportSize);

            if (reportId == BaseReportIds.DeviceState)
            {
                var state = BaseState.Parse(payload);
                FirmwareVersion = state.Firmware;
                StateReceived?.Invoke(this, state);
            }
            else if (reportId == BaseReportIds.SettingValue)
            {
                var report = SettingReport.Parse(payload);
                TaskCompletionSource<SettingValue>? tcs;
                lock (_pendingLock)
                {
                    _pendingReads.Remove(report.FieldId, out tcs);
                }
                tcs?.TrySetResult(report.Value);
            }
        }
        catch
        {
            // Drop malformed or corrupt reports rather than crash the read thread
        }
    }

    internal static byte[] Frame(byte reportId, byte[] payload64)
    {
        var wire = new byte[1 + ReportConstants.ReportSize];
        wire[0] = reportId;
        payload64.CopyTo(wire, 1);
        return wire;
    }

    public void Dispose()
    {
        _channel.ReportReceived -= OnReport;
        _channel.Dispose();
        _readGate.Dispose();
    }
}
