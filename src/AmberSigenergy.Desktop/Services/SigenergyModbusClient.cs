using System.IO;
using System.Buffers.Binary;
using System.Net.Sockets;
using AmberSigenergy.Desktop.Models;

namespace AmberSigenergy.Desktop.Services;

/// <summary>
/// Minimal read-only Sigenergy Modbus TCP client. Uses FC03 only.
/// There are deliberately no write methods or write function codes in this class.
/// </summary>
public sealed class SigenergyModbusClient
{
    private readonly string _host;
    private readonly int _port;
    private readonly byte _unitId;
    private readonly byte _inverterUnitId;
    private ushort _transactionId;

    public SigenergyModbusClient(string host, int port = 502, byte unitId = 247, byte inverterUnitId = 1)
    {
        _host = host;
        _port = port;
        _unitId = unitId;
        _inverterUnitId = inverterUnitId;
    }

    public async Task<SigenergyTelemetry> ReadPlantTelemetryAsync(CancellationToken cancellationToken = default)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(_host, _port, cancellationToken);
        await using var stream = client.GetStream();

        // Plant registers 30005..30038: grid, SOC, plant, PV and ESS power.
        var block = await ReadRegistersAsync(stream, 30005, 34, cancellationToken);
        var capacity = await ReadRegistersAsync(stream, 30083, 2, cancellationToken);
        double? dailyChargeKwh = null;
        double? dailyDischargeKwh = null;
        try
        {
            // Battery daily counters are inverter-level registers (unit ID 1 by default).
            var dailyCharge = await ReadRegistersAsync(stream, 30566, 2, cancellationToken, unitId: _inverterUnitId);
            var dailyDischarge = await ReadRegistersAsync(stream, 30572, 2, cancellationToken, unitId: _inverterUnitId);
            dailyChargeKwh = ToUInt32(dailyCharge, 0) / 100d;
            dailyDischargeKwh = ToUInt32(dailyDischarge, 0) / 100d;
        }
        catch (IOException)
        {
            // Keep plant telemetry alive if the inverter-level unit ID is not reachable.
        }
        double? dailyPvKwh = null;
        try
        {
            // Empirically observed plant daily PV counter; not listed in public V2.5 map.
            var pvDaily = await ReadRegistersAsync(stream, 30272, 2, cancellationToken);
            var value = ToUInt32(pvDaily, 0) / 100d;
            if (value is >= 0 and < 1000) dailyPvKwh = value;
        }
        catch (IOException)
        {
            // Keep the rest of the telemetry available if this undocumented counter is absent.
        }

        var gridPower = ToInt32(block, 0) / 1000d;
        var soc = block[9] / 10d;
        var plantPower = ToInt32(block, 26) / 1000d;
        var pvPower = ToInt32(block, 30) / 1000d;
        var batteryPower = ToInt32(block, 32) / 1000d;
        var capacityKwh = ToUInt32(capacity, 0) / 100d;

        return new SigenergyTelemetry(DateTimeOffset.Now, gridPower, soc, plantPower, pvPower, batteryPower, capacityKwh,
            dailyPvKwh, dailyChargeKwh, dailyDischargeKwh);
    }

    private async Task<ushort[]> ReadRegistersAsync(NetworkStream stream, ushort start, ushort quantity, CancellationToken ct, byte? unitId = null)
    {
        if (quantity is 0 or > 124) throw new ArgumentOutOfRangeException(nameof(quantity));
        var transaction = unchecked(++_transactionId);
        var request = new byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(0, 2), transaction);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(2, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(4, 2), 6);
        request[6] = unitId ?? _unitId;
        request[7] = 0x03; // FC03: read-only plant running registers
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(8, 2), start);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(10, 2), quantity);
        await stream.WriteAsync(request, ct);

        var header = new byte[7];
        await ReadExactlyAsync(stream, header, ct);
        var responseTransaction = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(0, 2));
        var protocol = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2, 2));
        var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2));
        if (responseTransaction != transaction || protocol != 0 || header[6] != (unitId ?? _unitId) || length < 3 || length > 255)
            throw new IOException("Invalid Modbus TCP response header.");

        var pdu = new byte[length - 1];
        await ReadExactlyAsync(stream, pdu, ct);
        if (pdu[0] == 0x83)
            throw new IOException($"Sigenergy Modbus exception: 0x{(pdu.Length > 1 ? pdu[1] : 0):X2}.");
        if (pdu[0] != 0x03 || pdu.Length != 2 + quantity * 2 || pdu[1] != quantity * 2)
            throw new IOException("Unexpected Modbus register response.");

        var values = new ushort[quantity];
        for (var i = 0; i < quantity; i++)
            values[i] = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(2 + i * 2, 2));
        return values;
    }

    private static async Task ReadExactlyAsync(NetworkStream stream, Memory<byte> buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], ct);
            if (read == 0) throw new EndOfStreamException("Sigenergy closed the Modbus TCP connection.");
            offset += read;
        }
    }

    private static int ToInt32(ushort[] registers, int offset)
    {
        var raw = ((uint)registers[offset] << 16) | registers[offset + 1];
        return unchecked((int)raw);
    }

    private static uint ToUInt32(ushort[] registers, int offset) =>
        ((uint)registers[offset] << 16) | registers[offset + 1];
}
