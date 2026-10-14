using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LegacyVault.IntegrationTests.Email;

/// <summary>
/// Máy chủ SMTP giả tối thiểu chạy trên cổng ngẫu nhiên của localhost, để test MailKit gửi thư thật
/// mà không cần Gmail. Không hỗ trợ TLS và đăng nhập (test đặt SecureSocket = None, không UserName).
/// </summary>
public sealed class FakeSmtpServer : IDisposable
{
    private readonly TcpListener listener;
    private readonly CancellationTokenSource stop = new CancellationTokenSource();
    private readonly object gate = new object();
    private readonly List<byte[]> messages = new List<byte[]>();

    public FakeSmtpServer()
    {
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Task.Run(AcceptLoopAsync);
    }

    public int Port { get; }

    /// <summary>Đặt giá trị (ví dụ "550 no such user") thì máy chủ từ chối người nhận bằng mã đó.</summary>
    public string? RejectRecipientWith { get; set; }

    /// <summary>Các thư đã nhận (nội dung thô, theo thứ tự).</summary>
    public IReadOnlyList<byte[]> ReceivedMessages
    {
        get
        {
            lock (gate)
            {
                return messages.ToList();
            }
        }
    }

    public void Dispose()
    {
        stop.Cancel();
        listener.Stop();
    }

    private async Task AcceptLoopAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                TcpClient client = await listener.AcceptTcpClientAsync(stop.Token);
                _ = Task.Run(() => HandleAsync(client));
            }
            catch (Exception)
            {
                return;
            }
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        using (NetworkStream stream = client.GetStream())
        using (StreamReader reader = new StreamReader(stream, Encoding.Latin1))
        using (StreamWriter writer = new StreamWriter(stream, Encoding.Latin1))
        {
            writer.NewLine = "\r\n";
            writer.AutoFlush = true;
            await writer.WriteLineAsync("220 fake.local ESMTP ready");

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                string command = line.ToUpperInvariant();
                if (command.StartsWith("EHLO") || command.StartsWith("HELO"))
                {
                    await writer.WriteLineAsync("250 fake.local");
                }
                else if (command.StartsWith("RCPT TO") && RejectRecipientWith != null)
                {
                    await writer.WriteLineAsync(RejectRecipientWith);
                }
                else if (command.StartsWith("DATA"))
                {
                    await writer.WriteLineAsync("354 end with <CRLF>.<CRLF>");
                    await ReadMessageAsync(reader, writer);
                }
                else if (command.StartsWith("QUIT"))
                {
                    await writer.WriteLineAsync("221 bye");
                    return;
                }
                else
                {
                    await writer.WriteLineAsync("250 OK");
                }
            }
        }
    }

    private async Task ReadMessageAsync(StreamReader reader, StreamWriter writer)
    {
        StringBuilder data = new StringBuilder();
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (line == ".")
            {
                break;
            }
            if (line.StartsWith(".."))
            {
                line = line.Substring(1);
            }
            data.Append(line).Append("\r\n");
        }
        lock (gate)
        {
            messages.Add(Encoding.Latin1.GetBytes(data.ToString()));
        }
        await writer.WriteLineAsync("250 OK queued");
    }
}
