using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Sgv.Dte;
using Xunit;

namespace Sgv.Tests;

public sealed class NtpClockTests
{
    private static readonly int[] TimestampOffsets = [32, 40];
    [Fact]
    public async Task UsesNetworkTimeAndValidatesResponseOrigin()
    {
        var result = await QueryAsync(response => response, TimeSpan.FromSeconds(1));
        Assert.InRange((result - DateTimeOffset.UtcNow).TotalSeconds, 0, 2);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("unsynchronized")]
    [InlineData("origin")]
    [InlineData("skew")]
    public async Task RejectsInvalidNetworkTime(string failure)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => QueryAsync(response =>
        {
            if (failure == "short") return new byte[4];
            if (failure == "unsynchronized") response[1] = 0;
            if (failure == "origin") response[24] ^= 1;
            return response;
        }, failure == "skew" ? TimeSpan.FromMinutes(10) : TimeSpan.Zero));
    }

    [Fact]
    public async Task HonorsCallerCancellation()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Ntp:Host"] = "127.0.0.1", ["Ntp:Port"] = "9" }).Build();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new NtpClock(config).UtcNowAsync(cancellation.Token));
    }

    private static async Task<DateTimeOffset> QueryAsync(Func<byte[], byte[]> transform, TimeSpan offset)
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var responseTask = Task.Run(async () =>
        {
            var request = await server.ReceiveAsync(deadline.Token);
            var response = new byte[48]; response[0] = 0x24; response[1] = 1;
            request.Buffer.AsSpan(40, 8).CopyTo(response.AsSpan(24, 8));
            var seconds = (DateTimeOffset.UtcNow + offset - new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero)).TotalSeconds;
            foreach (var start in TimestampOffsets)
            {
                BinaryPrimitives.WriteUInt32BigEndian(response.AsSpan(start, 4), (uint)seconds);
                BinaryPrimitives.WriteUInt32BigEndian(response.AsSpan(start + 4, 4), (uint)((seconds - Math.Floor(seconds)) * 4_294_967_296d));
            }
            await server.SendAsync(transform(response), request.RemoteEndPoint, deadline.Token);
        }, deadline.Token);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Ntp:Host"] = "127.0.0.1", ["Ntp:Port"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture) }).Build();
        try { return await new NtpClock(config).UtcNowAsync(deadline.Token); }
        finally { await responseTask; }
    }
}
