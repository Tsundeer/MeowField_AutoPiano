using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using MeowField.Application;
using MeowField.Domain;

namespace MeowField.Infrastructure.Windows;

public sealed class NtpService : INtpService
{
    private const long NtpEpochSeconds = 2_208_988_800L;
    private static readonly string[] Servers =
        ["ntp.aliyun.com", "ntp.tencent.com", "cn.ntp.org.cn", "ntp.ntsc.ac.cn", "time.pool.aliyun.com"];

    public async Task<NtpMeasurement> MeasureAsync(CancellationToken cancellationToken = default)
    {
        var tasks = Servers.Select(server => QueryAsync(server, cancellationToken)).ToArray();
        var results = await Task.WhenAll(tasks);
        return results.Where(item => item.Success).OrderBy(item => item.RoundTripTime).FirstOrDefault()
            ?? new NtpMeasurement(false, "", TimeSpan.Zero, TimeSpan.Zero, null, "所有 NTP 服务器均不可用");
    }

    private static async Task<NtpMeasurement> QueryAsync(string server, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            var addresses = await Dns.GetHostAddressesAsync(server, timeout.Token);
            var address = addresses.FirstOrDefault(item => item.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                ?? throw new SocketException((int)SocketError.HostNotFound);
            using var client = new UdpClient(address.AddressFamily);
            client.Connect(address, 123);
            var request = new byte[48];
            request[0] = 0x23;
            var t1 = DateTimeOffset.UtcNow;
            WriteTimestamp(request.AsSpan(40, 8), t1);
            await client.SendAsync(request, timeout.Token);
            var response = await client.ReceiveAsync(timeout.Token);
            var t4 = DateTimeOffset.UtcNow;
            if (response.Buffer.Length < 48) throw new InvalidDataException("NTP response is shorter than 48 bytes.");
            var t2 = ReadTimestamp(response.Buffer.AsSpan(32, 8));
            var t3 = ReadTimestamp(response.Buffer.AsSpan(40, 8));
            var offset = TimeSpan.FromTicks(((t2 - t1).Ticks + (t3 - t4).Ticks) / 2);
            var roundTrip = (t4 - t1) - (t3 - t2);
            return new NtpMeasurement(true, server, offset, roundTrip < TimeSpan.Zero ? TimeSpan.Zero : roundTrip, t3);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new NtpMeasurement(false, server, TimeSpan.Zero, TimeSpan.Zero, null, exception.Message);
        }
    }

    private static DateTimeOffset ReadTimestamp(ReadOnlySpan<byte> bytes)
    {
        var seconds = BinaryPrimitives.ReadUInt32BigEndian(bytes[..4]);
        var fraction = BinaryPrimitives.ReadUInt32BigEndian(bytes[4..]);
        var unixSeconds = seconds - NtpEpochSeconds;
        var ticks = (long)(fraction / (double)uint.MaxValue * TimeSpan.TicksPerSecond);
        return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).AddTicks(ticks);
    }

    private static void WriteTimestamp(Span<byte> bytes, DateTimeOffset value)
    {
        var unixSeconds = value.ToUnixTimeSeconds();
        var remainderTicks = value.UtcTicks - DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcTicks;
        BinaryPrimitives.WriteUInt32BigEndian(bytes[..4], checked((uint)(unixSeconds + NtpEpochSeconds)));
        BinaryPrimitives.WriteUInt32BigEndian(bytes[4..], checked((uint)(remainderTicks / (double)TimeSpan.TicksPerSecond * uint.MaxValue)));
    }
}
