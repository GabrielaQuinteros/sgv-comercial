using System.Buffers.Binary;
using System.Net.Sockets;

namespace Sgv.Dte;

public interface IClock
{
    Task<DateTimeOffset> UtcNowAsync(CancellationToken cancellation);
}

public sealed class NtpClock(IConfiguration configuration) : IClock
{
    private static readonly DateTimeOffset Epoch = new(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public async Task<DateTimeOffset> UtcNowAsync(CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        using var client = new UdpClient();
        var packet = new byte[48];
        packet[0] = 0x23;
        var sentAt = DateTimeOffset.UtcNow;
        WriteTimestamp(packet.AsSpan(40, 8), sentAt);
        try
        {
            var host = configuration["Ntp:Host"] ?? "pool.ntp.org";
            var port = configuration.GetValue<int?>("Ntp:Port") ?? 123;
            await client.SendAsync(packet, host, port, timeout.Token);
            var received = await client.ReceiveAsync(timeout.Token);
            var receivedAt = DateTimeOffset.UtcNow;
            var response = received.Buffer;
            if (response.Length < 48 || (response[0] & 7) != 4 || (response[0] >> 6) == 3 || response[1] is 0 or > 15)
                throw new InvalidOperationException("El servidor NTP no entregó una respuesta sincronizada.");
            if (!response.AsSpan(24, 8).SequenceEqual(packet.AsSpan(40, 8)))
                throw new InvalidOperationException("La respuesta NTP no corresponde a esta consulta.");
            var serverReceive = ReadTimestamp(response.AsSpan(32, 8));
            var serverTransmit = ReadTimestamp(response.AsSpan(40, 8));
            var offset = TimeSpan.FromTicks(((serverReceive - sentAt).Ticks + (serverTransmit - receivedAt).Ticks) / 2);
            if (offset.Duration() > TimeSpan.FromMinutes(2))
                throw new InvalidOperationException("La diferencia con NTP supera dos minutos. Revisa el reloj del servidor antes de firmar.");
            return receivedAt + offset;
        }
        catch (SocketException exception)
        {
            throw new InvalidOperationException("No fue posible consultar el servidor NTP.", exception);
        }
        catch (OperationCanceledException exception) when (!cancellation.IsCancellationRequested)
        {
            throw new InvalidOperationException("Se agotó el tiempo de sincronización NTP.", exception);
        }
    }

    private static void WriteTimestamp(Span<byte> bytes, DateTimeOffset time)
    {
        var seconds = (time - Epoch).TotalSeconds;
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)seconds);
        BinaryPrimitives.WriteUInt32BigEndian(bytes[4..], (uint)((seconds - Math.Floor(seconds)) * 4_294_967_296d));
    }

    private static DateTimeOffset ReadTimestamp(ReadOnlySpan<byte> bytes) =>
        Epoch.AddSeconds(BinaryPrimitives.ReadUInt32BigEndian(bytes) + BinaryPrimitives.ReadUInt32BigEndian(bytes[4..]) / 4_294_967_296d);
}
