using System.Text;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;

namespace ClaudeIslandWindows.BuddyBridge;

/// Speaks the Nordic UART Service. Writes go to the RX characteristic (WriteWithoutResponse),
/// reads come as TX notifications, fragmented across notifications until '\n'.
public sealed class NusClient
{
    private readonly GattCharacteristic _rx;
    private readonly GattCharacteristic _tx;
    private readonly Action<string> _log;
    private readonly List<byte> _rxBuffer = new();
    private readonly object _bufferLock = new();
    private bool _disposed;

    public event Action<string>? LineReceived;

    public NusClient(GattCharacteristic rx, GattCharacteristic tx, Action<string> log)
    {
        _rx = rx;
        _tx = tx;
        _log = log;
        _tx.ValueChanged += OnTxValueChanged;
    }

    public bool IsConnected => !_disposed;

    public async Task<bool> WriteLineAsync(string line, CancellationToken ct = default)
    {
        if (_disposed) return false;
        if (!line.EndsWith('\n')) line += '\n';
        var bytes = Encoding.UTF8.GetBytes(line);

        // ATT Write Command (WriteWithoutResponse) max value length is MTU - 3.
        // btstack's nordic_spp_service_server doesn't implement Long Write, so
        // WriteWithResponse won't help us fragment either. Windows throws
        // E_INVALIDARG ("Value does not fall within the expected range") if the
        // payload exceeds that limit. The firmware's RX side accumulates bytes
        // into a 2KB ring buffer and only splits on '\n', so fragmenting a
        // single JSON line across multiple writes is safe and reassembles
        // correctly on the device.
        int chunkSize;
        try { chunkSize = (int)_rx.Service.Session.MaxPduSize - 3; }
        catch { chunkSize = 20; } // ATT default MTU 23 minus the 3-byte header
        if (chunkSize < 20) chunkSize = 20;
        if (chunkSize > 244) chunkSize = 244; // sane cap; nothing here needs huge writes

        for (int offset = 0; offset < bytes.Length; offset += chunkSize)
        {
            int take = Math.Min(chunkSize, bytes.Length - offset);
            var chunk = new byte[take];
            Array.Copy(bytes, offset, chunk, 0, take);
            var buf = CryptographicBuffer.CreateFromByteArray(chunk);
            try
            {
                var result = await _rx.WriteValueAsync(buf, GattWriteOption.WriteWithoutResponse).AsTask(ct);
                if (result != GattCommunicationStatus.Success)
                {
                    _log($"NUS: write failed ({result}) at offset {offset}/{bytes.Length} chunk={take}");
                    return false;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log($"NUS: write threw at offset {offset}/{bytes.Length} chunk={take} mtu={chunkSize + 3}: {ex.Message}");
                return false;
            }
        }
        return true;
    }

    private void OnTxValueChanged(GattCharacteristic _, GattValueChangedEventArgs e)
    {
        try
        {
            CryptographicBuffer.CopyToByteArray(e.CharacteristicValue, out var bytes);
            if (bytes == null || bytes.Length == 0) return;

            string[]? completed = null;
            lock (_bufferLock)
            {
                List<string>? batch = null;
                foreach (var b in bytes)
                {
                    if (b == (byte)'\n')
                    {
                        if (_rxBuffer.Count > 0)
                        {
                            (batch ??= new List<string>()).Add(Encoding.UTF8.GetString(_rxBuffer.ToArray()));
                            _rxBuffer.Clear();
                        }
                    }
                    else if (b != (byte)'\r')
                    {
                        _rxBuffer.Add(b);
                    }
                }
                completed = batch?.ToArray();
            }

            if (completed != null)
                foreach (var line in completed) LineReceived?.Invoke(line);
        }
        catch (Exception ex)
        {
            _log($"NUS: rx parse error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _tx.ValueChanged -= OnTxValueChanged; } catch { }
    }
}
