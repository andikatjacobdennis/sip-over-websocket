using Microsoft.Extensions.Configuration;
using System.Net.WebSockets;
using System.Text;

namespace SipConsoleClient;

public class Program
{
    private static ClientWebSocket _webSocket = new();
    private static string _username = "";
    private static string _password = "";
    private static string _domain = "";
    private static string _wssServer = "";
    private static bool _isRegistered = false;
    private static string _callId = "";
    private static string _currentCallStatus = "Idle";
    private static CancellationTokenSource _cts = new();

    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== SIP Console Client ===");

        // Load configuration
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();

        _domain = config["SipSettings:Domain"] ?? "localhost";
        _wssServer = config["SipSettings:WebSocketServer"] ?? "ws://localhost:8089";

        Console.Write("Enter SIP username: ");
        _username = Console.ReadLine() ?? "1001";
        Console.Write("Enter SIP password: ");
        _password = Console.ReadLine() ?? "defaultpassword";

        await ConnectToServer();

        while (!_cts.IsCancellationRequested)
        {
            PrintStatus();
            Console.WriteLine("\nOptions:");
            Console.WriteLine("1. Register");
            Console.WriteLine("2. Unregister");
            Console.WriteLine("3. Make Call");
            Console.WriteLine("4. Hang Up");
            Console.WriteLine("5. Exit");
            Console.Write("Select option: ");

            var option = Console.ReadLine();

            switch (option)
            {
                case "1":
                    await Register();
                    break;
                case "2":
                    await Unregister();
                    break;
                case "3":
                    if (_isRegistered)
                    {
                        Console.Write("Enter destination (e.g., 1002): ");
                        var dest = Console.ReadLine();
                        if (!string.IsNullOrEmpty(dest))
                        {
                            await MakeCall(dest);
                        }
                    }
                    else
                    {
                        Console.WriteLine("Error: Please register first");
                    }
                    break;
                case "4":
                    await HangUp();
                    break;
                case "5":
                    _cts.Cancel();
                    break;
                default:
                    Console.WriteLine("Invalid option");
                    break;
            }
        }

        if (_webSocket != null && _webSocket.State == WebSocketState.Open)
        {
            await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
        }
    }

    private static void PrintStatus()
    {
        Console.WriteLine("\n=== Current Status ===");
        Console.WriteLine($"Registered: {(_isRegistered ? "Yes" : "No")}");
        Console.WriteLine($"Call Status: {_currentCallStatus}");
        Console.WriteLine($"Server: {_wssServer}");
        Console.WriteLine($"User: {_username}@{_domain}");
    }

    private static async Task ConnectToServer()
    {
        int retries = 3;
        int delay = 2000;

        for (int i = 0; i < retries; i++)
        {
            try
            {
                Console.WriteLine($"Connecting to server (attempt {i + 1}/{retries})...");
                _webSocket = new ClientWebSocket();
                _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _webSocket.ConnectAsync(new Uri(_wssServer), timeoutCts.Token);

                if (_webSocket.State == WebSocketState.Open)
                {
                    Console.WriteLine("Connected successfully!");
                    _ = Task.Run(ReceiveMessages);
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Connection attempt failed: {ex.Message}");
                if (i < retries - 1)
                {
                    Console.WriteLine($"Retrying in {delay / 1000} seconds...");
                    await Task.Delay(delay);
                }
            }
        }

        Console.WriteLine("Failed to connect to server. Please check:");
        Console.WriteLine($"- Server URL: {_wssServer}");
        Console.WriteLine("- Is the server running?");
        Console.WriteLine("- Network connectivity");
    }

    private static async Task ReceiveMessages()
    {
        var buffer = new byte[4096];

        while (_webSocket.State == WebSocketState.Open)
        {
            try
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    Console.WriteLine($"\n<<< Received Message >>>\n{message}");
                    ProcessSipMessage(message);
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    Console.WriteLine("Server initiated close. Closing client WebSocket...");
                    _isRegistered = false;
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by server", CancellationToken.None);
                    break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Receive error: {ex.Message}");
                break;
            }
        }
    }

    private static void ProcessSipMessage(string message)
    {
        try
        {
            if (message.StartsWith("SIP/2.0 200 OK"))
            {
                if (_currentCallStatus == "Registering...")
                {
                    _isRegistered = true;
                    _currentCallStatus = "Ready";
                    Console.WriteLine("\n=== REGISTRATION SUCCESSFUL ===");
                    Console.WriteLine($"User: {_username}@{_domain}");
                    Console.WriteLine($"Expires: {GetHeaderValue(message, "Expires")} seconds");
                }
                else if (_currentCallStatus == "Calling...")
                {
                    _callId = ExtractCallId(message);
                    _currentCallStatus = "Call Active";
                    Console.WriteLine("\n=== CALL CONNECTED ===");
                }
            }
            else if (message.StartsWith("INVITE"))
            {
                var lines = message.Split("\r\n");

                var from = GetHeaderValue(message, "From") ?? "<unknown>";
                var to = GetHeaderValue(message, "To") ?? $"<sip:{_username}@{_domain}>";
                var callId = GetHeaderValue(message, "Call-ID") ?? Guid.NewGuid().ToString();
                var cseq = GetHeaderValue(message, "CSeq") ?? "1 INVITE";
                var via = lines.FirstOrDefault(l => l.StartsWith("Via:")) ?? $"Via: SIP/2.0/WS {_domain};branch={Guid.NewGuid()}";

                _callId = callId;
                _currentCallStatus = "Incoming Call";

                Console.WriteLine($"\n=== INCOMING CALL from {from} ===");

                // Build 180 Ringing response
                var ringingResponse = $"SIP/2.0 180 Ringing\r\n" +
                                      $"{via}\r\n" +
                                      $"To: {to};tag={Guid.NewGuid().ToString().Substring(0, 8)}\r\n" +
                                      $"From: {from}\r\n" +
                                      $"Call-ID: {callId}\r\n" +
                                      $"CSeq: {cseq}\r\n" +
                                      $"Content-Length: 0\r\n\r\n";

                Console.WriteLine("\n>>> Sending 180 Ringing >>>");
                _ = SendSipMessage(ringingResponse);
            }
            else if (message.Contains("SIP/2.0 401 Unauthorized"))
            {
                Console.WriteLine("\n=== AUTHENTICATION REQUIRED ===");
            }
            else if (message.Contains("BYE"))
            {
                _callId = "";
                _currentCallStatus = "Ready";
                Console.WriteLine("\n=== CALL ENDED ===");
            }
            else if (message.Contains("SIP/2.0 180 Ringing"))
            {
                _currentCallStatus = "Ringing";
                Console.WriteLine("\n=== REMOTE PARTY RINGING ===");
            }
            else if (message.Contains("SIP/2.0 486 Busy Here"))
            {
                _currentCallStatus = "Remote Busy";
                Console.WriteLine("\n=== REMOTE PARTY BUSY ===");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing message: {ex.Message}");
        }
    }

    private static string? GetHeaderValue(string message, string headerName)
    {
        using var reader = new StringReader(message);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.StartsWith(headerName + ":"))
            {
                return line.Split(new[] { ':' }, 2)[1].Trim();
            }
        }
        return null;
    }

    private static string ExtractCallId(string message)
    {
        var callId = GetHeaderValue(message, "Call-ID");
        return callId ?? Guid.NewGuid().ToString();
    }

    private static async Task Register()
    {
        if (_isRegistered)
        {
            Console.WriteLine("Already registered");
            return;
        }

        var registerMsg = $"REGISTER sip:{_domain} SIP/2.0\r\n" +
                         $"Via: SIP/2.0/WS {_domain};branch={Guid.NewGuid()}\r\n" +
                         $"Max-Forwards: 70\r\n" +
                         $"To: <sip:{_username}@{_domain}>\r\n" +
                         $"From: <sip:{_username}@{_domain}>;tag={Guid.NewGuid().ToString().Substring(0, 8)}\r\n" +
                         $"Call-ID: {Guid.NewGuid()}@{_domain}\r\n" +
                         $"CSeq: 1 REGISTER\r\n" +
                         $"Contact: <sip:{_username}@{_domain};transport=ws>\r\n" +
                         $"Expires: 3600\r\n" +
                         $"Content-Length: 0\r\n\r\n";

        Console.WriteLine("\n>>> Sending REGISTER >>>");
        await SendSipMessage(registerMsg);
        _currentCallStatus = "Registering...";
    }

    private static async Task Unregister()
    {
        if (!_isRegistered)
        {
            Console.WriteLine("Not currently registered");
            return;
        }

        var unregisterMsg = $"REGISTER sip:{_domain} SIP/2.0\r\n" +
                           $"Via: SIP/2.0/WS {_domain};branch={Guid.NewGuid()}\r\n" +
                           $"Max-Forwards: 70\r\n" +
                           $"To: <sip:{_username}@{_domain}>\r\n" +
                           $"From: <sip:{_username}@{_domain}>;tag={Guid.NewGuid().ToString().Substring(0, 8)}\r\n" +
                           $"Call-ID: {Guid.NewGuid()}@{_domain}\r\n" +
                           $"CSeq: 2 REGISTER\r\n" +
                           $"Contact: <sip:{_username}@{_domain};transport=ws>\r\n" +
                           $"Expires: 0\r\n" +
                           $"Content-Length: 0\r\n\r\n";

        Console.WriteLine("\n>>> Sending UNREGISTER >>>");
        await SendSipMessage(unregisterMsg);
        _isRegistered = false;
        _currentCallStatus = "Unregistering...";
    }

    private static async Task MakeCall(string destination)
    {
        _callId = Guid.NewGuid().ToString();

        var inviteMsg = $"INVITE sip:{destination}@{_domain} SIP/2.0\r\n" +
                       $"Via: SIP/2.0/WS {_domain};branch={Guid.NewGuid()}\r\n" +
                       $"Max-Forwards: 70\r\n" +
                       $"To: <sip:{destination}@{_domain}>\r\n" +
                       $"From: <sip:{_username}@{_domain}>;tag={Guid.NewGuid().ToString().Substring(0, 8)}\r\n" +
                       $"Call-ID: {_callId}@{_domain}\r\n" +
                       $"CSeq: 1 INVITE\r\n" +
                       $"Contact: <sip:{_username}@{_domain};transport=ws>\r\n" +
                       $"Content-Type: application/sdp\r\n" +
                       $"Content-Length: 0\r\n\r\n";

        Console.WriteLine($"\n>>> Calling {destination}@{_domain} >>>");
        await SendSipMessage(inviteMsg);
        _currentCallStatus = "Calling...";
    }

    private static async Task HangUp()
    {
        if (string.IsNullOrEmpty(_callId))
        {
            Console.WriteLine("No active call");
            return;
        }

        var byeMsg = $"BYE sip:{_username}@{_domain} SIP/2.0\r\n" +
                    $"Via: SIP/2.0/WS {_domain};branch={Guid.NewGuid()}\r\n" +
                    $"Max-Forwards: 70\r\n" +
                    $"To: <sip:{_username}@{_domain}>\r\n" +
                    $"From: <sip:{_username}@{_domain}>;tag={Guid.NewGuid().ToString().Substring(0, 8)}\r\n" +
                    $"Call-ID: {_callId}@{_domain}\r\n" +
                    $"CSeq: 2 BYE\r\n" +
                    $"Content-Length: 0\r\n\r\n";

        Console.WriteLine("\n>>> Ending call >>>");
        await SendSipMessage(byeMsg);
        _currentCallStatus = "Hanging Up...";
    }

    private static async Task SendSipMessage(string message)
    {
        if (_webSocket.State != WebSocketState.Open)
        {
            Console.WriteLine("Error: Not connected to server");
            return;
        }

        try
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            await _webSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                _cts.Token);

            Console.WriteLine($"Message sent (first line): {message.Split('\r')[0]}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending message: {ex.Message}");
        }
    }
}