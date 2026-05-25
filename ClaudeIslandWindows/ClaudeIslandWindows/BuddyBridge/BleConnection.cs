using System.Diagnostics;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace ClaudeIslandWindows.BuddyBridge;

public enum BridgeStatus { Disabled, Disconnected, Scanning, Connecting, Connected, Busy }

/// Owns the BLE link to a Buddy. Drives scan / connect / GATT discovery / reconnect.
/// Doesn't speak the wire protocol — that's NusClient's job. Exposes the connected
/// NusClient via the Connected event; subscribers wire heartbeats / inbound there.
public sealed class BleConnection : IDisposable
{
    public static readonly Guid NusService     = new("6e400001-b5a3-f393-e0a9-e50e24dcca9e");
    public static readonly Guid NusRxCharWrite = new("6e400002-b5a3-f393-e0a9-e50e24dcca9e");
    public static readonly Guid NusTxCharNotify= new("6e400003-b5a3-f393-e0a9-e50e24dcca9e");

    private readonly string _namePrefix;
    private readonly Action<string> _log;
    private BluetoothLEAdvertisementWatcher? _watcher;
    private BluetoothLEDevice? _device;
    private NusClient? _client;
    private CancellationTokenSource? _cts;

    // Exponential backoff for reconnect: 1s, 2s, 5s, 10s, 30s cap.
    private static readonly int[] BackoffSeconds = { 1, 2, 5, 10, 30 };
    private int _backoffIndex = 0;

    public BridgeStatus Status { get; private set; } = BridgeStatus.Disconnected;
    public string? DeviceName { get; private set; }
    public ulong DeviceAddress { get; private set; }

    public event Action<BridgeStatus>? StatusChanged;
    public event Action<NusClient>? Connected;
    public event Action? Disconnected;

    public BleConnection(string namePrefix, Action<string>? log = null)
    {
        _namePrefix = namePrefix;
        _log = log ?? (_ => { });
    }

    /// Start the search/connect loop. Returns immediately; runs in background.
    public void Start(ulong preferredAddress = 0)
    {
        Stop();
        _cts = new CancellationTokenSource();
        _ = RunAsync(preferredAddress, _cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        TeardownWatcher();
        TeardownDevice();
        SetStatus(BridgeStatus.Disconnected);
    }

    public void Dispose() => Stop();

    private async Task RunAsync(ulong preferredAddress, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Try the last-known address first — skips scanning round-trip.
                if (preferredAddress != 0)
                {
                    _log($"Bridge: trying preferred address {preferredAddress:X12}");
                    if (await TryConnectAsync(preferredAddress, ct))
                    {
                        _backoffIndex = 0;
                        await WaitForDisconnectAsync(ct);
                        if (ct.IsCancellationRequested) return;
                    }
                    preferredAddress = 0; // fall through to scan next iteration
                }

                if (ct.IsCancellationRequested) return;

                SetStatus(BridgeStatus.Scanning);
                var addr = await ScanForBuddyAsync(ct);
                if (ct.IsCancellationRequested) return;

                if (addr == 0)
                {
                    await BackoffDelayAsync(ct);
                    continue;
                }

                if (await TryConnectAsync(addr, ct))
                {
                    _backoffIndex = 0;
                    await WaitForDisconnectAsync(ct);
                }
                else
                {
                    await BackoffDelayAsync(ct);
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _log($"Bridge: loop error: {ex.Message}");
                await BackoffDelayAsync(ct);
            }
        }
    }

    private Task BackoffDelayAsync(CancellationToken ct)
    {
        var s = BackoffSeconds[Math.Min(_backoffIndex, BackoffSeconds.Length - 1)];
        _backoffIndex = Math.Min(_backoffIndex + 1, BackoffSeconds.Length - 1);
        return Task.Delay(TimeSpan.FromSeconds(s), ct);
    }

    private Task<ulong> ScanForBuddyAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<ulong>(TaskCreationOptions.RunContinuationsAsynchronously);
        var watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active
        };
        watcher.AdvertisementFilter.Advertisement.ServiceUuids.Add(NusService);

        watcher.Received += (s, e) =>
        {
            try
            {
                var name = e.Advertisement.LocalName;
                if (string.IsNullOrEmpty(name) ||
                    !name.StartsWith(_namePrefix, StringComparison.Ordinal))
                    return;
                _log($"Bridge: found '{name}' @ {e.BluetoothAddress:X12} rssi={e.RawSignalStrengthInDBm}");
                DeviceName = name;
                tcs.TrySetResult(e.BluetoothAddress);
            }
            catch { }
        };
        watcher.Stopped += (_, _) => tcs.TrySetResult(0);
        watcher.Start();
        _watcher = watcher;

        // Cancel scan when CT fires.
        var reg = ct.Register(() => tcs.TrySetResult(0));

        return tcs.Task.ContinueWith(t =>
        {
            try { watcher.Stop(); } catch { }
            reg.Dispose();
            _watcher = null;
            return t.Result;
        }, TaskScheduler.Default);
    }

    private async Task<bool> TryConnectAsync(ulong address, CancellationToken ct)
    {
        SetStatus(BridgeStatus.Connecting);
        try
        {
            _device = await BluetoothLEDevice.FromBluetoothAddressAsync(address).AsTask(ct);
            if (_device == null)
            {
                _log("Bridge: FromBluetoothAddressAsync returned null");
                SetStatus(BridgeStatus.Disconnected);
                return false;
            }
            DeviceAddress = address;
            DeviceName ??= _device.Name;

            var svcResult = await _device.GetGattServicesForUuidAsync(NusService,
                BluetoothCacheMode.Uncached).AsTask(ct);
            if (svcResult.Status != GattCommunicationStatus.Success || svcResult.Services.Count == 0)
            {
                _log($"Bridge: NUS service not found ({svcResult.Status}) — Buddy likely already linked elsewhere");
                SetStatus(BridgeStatus.Busy);
                TeardownDevice();
                return false;
            }
            var svc = svcResult.Services[0];

            var rxResult = await svc.GetCharacteristicsForUuidAsync(NusRxCharWrite,
                BluetoothCacheMode.Uncached).AsTask(ct);
            var txResult = await svc.GetCharacteristicsForUuidAsync(NusTxCharNotify,
                BluetoothCacheMode.Uncached).AsTask(ct);
            if (rxResult.Status != GattCommunicationStatus.Success || rxResult.Characteristics.Count == 0
             || txResult.Status != GattCommunicationStatus.Success || txResult.Characteristics.Count == 0)
            {
                _log($"Bridge: NUS chars not found (rx={rxResult.Status}, tx={txResult.Status})");
                SetStatus(BridgeStatus.Disconnected);
                TeardownDevice();
                return false;
            }

            var rx = rxResult.Characteristics[0];
            var tx = txResult.Characteristics[0];

            var notifyStatus = await tx.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask(ct);
            if (notifyStatus != GattCommunicationStatus.Success)
            {
                _log($"Bridge: subscribe to TX notifications failed ({notifyStatus})");
                SetStatus(BridgeStatus.Disconnected);
                TeardownDevice();
                return false;
            }

            _client = new NusClient(rx, tx, _log);
            _device.ConnectionStatusChanged += OnConnectionStatusChanged;
            SetStatus(BridgeStatus.Connected);
            _log($"Bridge: connected to '{DeviceName}' @ {DeviceAddress:X12}");
            Connected?.Invoke(_client);
            return true;
        }
        catch (OperationCanceledException) { TeardownDevice(); throw; }
        catch (Exception ex)
        {
            _log($"Bridge: connect failed: {ex.Message}");
            SetStatus(BridgeStatus.Disconnected);
            TeardownDevice();
            return false;
        }
    }

    private TaskCompletionSource<bool>? _disconnectTcs;

    private Task WaitForDisconnectAsync(CancellationToken ct)
    {
        _disconnectTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reg = ct.Register(() => _disconnectTcs?.TrySetResult(true));
        return _disconnectTcs.Task.ContinueWith(_ =>
        {
            reg.Dispose();
            TeardownDevice();
            if (!ct.IsCancellationRequested) Disconnected?.Invoke();
        }, TaskScheduler.Default);
    }

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object _)
    {
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
        {
            _log("Bridge: ConnectionStatusChanged → Disconnected");
            SetStatus(BridgeStatus.Disconnected);
            _disconnectTcs?.TrySetResult(true);
        }
    }

    private void TeardownWatcher()
    {
        if (_watcher != null)
        {
            try { _watcher.Stop(); } catch { }
            _watcher = null;
        }
    }

    private void TeardownDevice()
    {
        if (_device != null)
        {
            try { _device.ConnectionStatusChanged -= OnConnectionStatusChanged; } catch { }
            try { _device.Dispose(); } catch { }
            _device = null;
        }
        _client = null;
    }

    private void SetStatus(BridgeStatus s)
    {
        if (Status == s) return;
        Status = s;
        StatusChanged?.Invoke(s);
    }
}
