using System.Net;
using System.Net.Sockets;
using System.Text;
using BlueMeal.Api.Infrastructure;
using FluentAssertions;
using Serilog;
using Serilog.Events;
using Xunit;

namespace BlueMeal.IntegrationTests;

/// <summary>Centralized logging (SPEC §10.3): events reach a syslog collector as RFC 5424 messages carrying the JSON event.</summary>
public class SyslogSinkTests
{
    [Fact]
    public async Task Udp_sends_rfc5424_message_with_json_body()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
        using (var log = Logger(new SyslogOptions { Host = "127.0.0.1", Port = port }))
            log.ForContext("SourceContext", "BlueMeal.Api.Auth.AuthService").Warning("Login failed for {UserName}", "kho01");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var received = await server.ReceiveAsync(cts.Token);
        var text = Encoding.UTF8.GetString(received.Buffer);

        // facility local0 (16) * 8 + warning (4) = 132
        text.Should().StartWith("<132>1 ");
        text.Should().Contain(" bluemeal ").And.Contain(" AuthService - ﻿{");
        text.Should().Contain("\"@mt\":\"Login failed for {UserName}\"").And.Contain("\"UserName\":\"kho01\"").And.Contain("\"@l\":\"Warning\"");
    }

    [Fact]
    public async Task Tcp_uses_octet_counting_frames_and_respects_minimum_level()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var opts = new SyslogOptions { Host = "127.0.0.1", Port = port, Protocol = "tcp", AppName = "blue meal", MinimumLevel = LogEventLevel.Information };
            using (var log = Logger(opts))
            {
                log.Debug("not sent");
                log.Information("first {N}", 1);
                log.Error("second");
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var client = await listener.AcceptTcpClientAsync(cts.Token);
            var frames = await ReadFramesAsync(client.GetStream(), 2, cts.Token);

            frames[0].Should().StartWith("<134>1 ").And.Contain(" bluemeal ").And.Contain("\"N\":1");   // info = 6; spaces stripped from APP-NAME
            frames[1].Should().StartWith("<131>1 ").And.Contain("\"@mt\":\"second\"");                // error = 3
            frames.Should().NotContain(f => f.Contains("not sent"));
        }
        finally { listener.Stop(); }
    }

    private static Serilog.Core.Logger Logger(SyslogOptions o) =>
        new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(new SyslogSink(o), o.MinimumLevel).CreateLogger();

    /// <summary>Reads RFC 6587 frames ("LEN SP MSG") until <paramref name="count"/> messages have arrived.</summary>
    private static async Task<List<string>> ReadFramesAsync(Stream s, int count, CancellationToken ct)
    {
        var buffer = new List<byte>();
        var chunk = new byte[4096];
        var frames = new List<string>();
        while (frames.Count < count)
        {
            var n = await s.ReadAsync(chunk, ct);
            if (n == 0) break;
            buffer.AddRange(chunk.AsSpan(0, n).ToArray());
            while (true)
            {
                var sp = buffer.IndexOf((byte)' ');
                if (sp < 0) break;
                var len = int.Parse(Encoding.ASCII.GetString(buffer.GetRange(0, sp).ToArray()));
                if (buffer.Count < sp + 1 + len) break;
                frames.Add(Encoding.UTF8.GetString(buffer.GetRange(sp + 1, len).ToArray()));
                buffer.RemoveRange(0, sp + 1 + len);
            }
        }
        return frames;
    }
}
