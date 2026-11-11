using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace BlueMeal.Api.Infrastructure;

/// <summary>Remote syslog target for centralized logging / SIEM (config section <c>Logging:Syslog</c>; off while Host is empty).</summary>
public sealed class SyslogOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 514;
    /// <summary>"udp" (default) or "tcp" (RFC 6587 octet-counting framing).</summary>
    public string Protocol { get; set; } = "udp";
    public string AppName { get; set; } = "bluemeal";
    /// <summary>Syslog facility number; 16 = local0.</summary>
    public int Facility { get; set; } = 16;
    public LogEventLevel MinimumLevel { get; set; } = LogEventLevel.Information;
}

/// <summary>
/// Sends each event as an RFC 5424 syslog message whose body is the same compact JSON written to the log file, so a SIEM
/// (rsyslog, Graylog, Wazuh, Splunk…) receives structured fields. Events are queued and sent on a background task: a slow or
/// unreachable collector never blocks a request; when the queue is full the oldest events are dropped, and send failures go
/// to Serilog's SelfLog (the local JSON file stays the complete record).
/// </summary>
public sealed class SyslogSink : ILogEventSink, IDisposable
{
    private const int UdpMaxBytes = 8192;
    private readonly SyslogOptions options;
    private readonly bool tcp;
    private readonly string host = Sanitize(Environment.MachineName, 255);
    private readonly string app;
    private readonly string procId = Environment.ProcessId.ToString();
    private readonly CompactJsonFormatter formatter = new();
    private readonly Channel<LogEvent> queue = Channel.CreateBounded<LogEvent>(
        new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly Task pump;
    private UdpClient? udp;
    private TcpClient? tcpClient;
    private Stream? tcpStream;

    public SyslogSink(SyslogOptions options)
    {
        this.options = options;
        tcp = string.Equals(options.Protocol, "tcp", StringComparison.OrdinalIgnoreCase);
        app = Sanitize(options.AppName, 48);
        pump = Task.Run(PumpAsync);
    }

    public void Emit(LogEvent logEvent) => queue.Writer.TryWrite(logEvent);

    /// <summary>RFC 5424: <c>&lt;PRI&gt;1 TIMESTAMP HOST APP PROCID MSGID - BOM MSG</c>.</summary>
    public byte[] Format(LogEvent e)
    {
        using var body = new StringWriter();
        formatter.Format(e, body);
        var json = body.ToString().TrimEnd('\r', '\n');
        var pri = options.Facility * 8 + Severity(e.Level);
        var msgId = e.Properties.TryGetValue("SourceContext", out var sc) && sc is ScalarValue { Value: string s } ? Sanitize(s[(s.LastIndexOf('.') + 1)..], 32) : "-";
        var header = $"<{pri}>1 {e.Timestamp.UtcDateTime:yyyy-MM-ddTHH:mm:ss.ffffffZ} {host} {app} {procId} {msgId} - ";
        var bytes = Encoding.UTF8.GetBytes(header + "﻿" + json);
        return !tcp && bytes.Length > UdpMaxBytes ? bytes[..UdpMaxBytes] : bytes;
    }

    private static int Severity(LogEventLevel level) => level switch
    {
        LogEventLevel.Fatal => 2,
        LogEventLevel.Error => 3,
        LogEventLevel.Warning => 4,
        LogEventLevel.Information => 6,
        _ => 7,
    };

    /// <summary>Header fields are printable US-ASCII without spaces; "-" when empty.</summary>
    private static string Sanitize(string value, int max)
    {
        var chars = value.Where(c => c is > ' ' and < (char)127).Take(max).ToArray();
        return chars.Length == 0 ? "-" : new string(chars);
    }

    private async Task PumpAsync()
    {
        await foreach (var e in queue.Reader.ReadAllAsync())
        {
            try
            {
                var msg = Format(e);
                if (tcp) await SendTcpAsync(msg);
                else
                {
                    udp ??= new UdpClient();
                    await udp.SendAsync(msg, options.Host!, options.Port);
                }
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine("Syslog send to {0}:{1} failed: {2}", options.Host, options.Port, ex.Message);
                ResetTcp();
            }
        }
    }

    private async Task SendTcpAsync(byte[] msg)
    {
        if (tcpStream is null)
        {
            tcpClient = new TcpClient();
            using var connect = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await tcpClient.ConnectAsync(options.Host!, options.Port, connect.Token);
            tcpStream = tcpClient.GetStream();
        }
        var frame = Encoding.ASCII.GetBytes(msg.Length + " ");
        await tcpStream.WriteAsync(frame);
        await tcpStream.WriteAsync(msg);
    }

    private void ResetTcp()
    {
        tcpStream?.Dispose();
        tcpClient?.Dispose();
        tcpStream = null;
        tcpClient = null;
    }

    /// <summary>Flushes what is queued (up to 3 s) when the logger is disposed at shutdown.</summary>
    public void Dispose()
    {
        queue.Writer.TryComplete();
        pump.Wait(TimeSpan.FromSeconds(3));
        udp?.Dispose();
        ResetTcp();
    }
}
