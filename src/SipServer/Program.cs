using System.Net;
using System.Net.WebSockets;
using System.Text;

namespace SipServer;

public class Program
{
    private static readonly Dictionary<string, SipUser> _users = new();
    private static readonly Dictionary<string, ActiveCall> _activeCalls = new();
    private static readonly string _domain = "localhost";
    private static readonly int _wssPort = 8089;
    private static readonly CancellationTokenSource _cts = new();

    public static async Task Main(string[] args)
    {
        Console.WriteLine($"[{DateTime.Now}] Starting Enhanced SIP Server...");

        InitializeTestUsers();

        var serverTask = StartWebSocketServer();

        Console.WriteLine($"[{DateTime.Now}] SIP Server running on ws://{_domain}:{_wssPort}");
        Console.WriteLine($"[{DateTime.Now}] Registered users: {string.Join(", ", _users.Keys)}");
        Console.WriteLine($"[{DateTime.Now}] Press Q to quit");

        await HandleConsoleInput();
        await serverTask;
    }

    private static void InitializeTestUsers()
    {
        _users.Add("1001", new SipUser { Username = "1001", Password = "defaultpassword", ContactUri = $"sip:1001@{_domain}" });
        _users.Add("1002", new SipUser { Username = "1002", Password = "defaultpassword", ContactUri = $"sip:1002@{_domain}" });
    }

    private static async Task HandleConsoleInput()
    {
        while (!_cts.IsCancellationRequested)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Q)
            {
                Console.WriteLine($"[{DateTime.Now}] Shutdown requested");
                _cts.Cancel();
                break;
            }
            else if (key.Key == ConsoleKey.S)
            {
                PrintServerStatus();
            }
        }
    }

    private static void PrintServerStatus()
    {
        Console.WriteLine($"\n[{DateTime.Now}] === Server Status ===");
        Console.WriteLine($"Active calls: {_activeCalls.Count}");
        Console.WriteLine("Registered users:");
        foreach (var user in _users.Values.Where(u => u.IsRegistered))
        {
            Console.WriteLine($"- {user.Username} (expires: {user.Expires.Subtract(DateTime.UtcNow).TotalSeconds:F0}s)");
        }
    }

    private static async Task StartWebSocketServer()
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{_wssPort}/");
        listener.Prefixes.Add($"http://*:{_wssPort}/");

        try
        {
            listener.Start();
            Console.WriteLine($"[{DateTime.Now}] Server listening on port {_wssPort}");

            while (!_cts.IsCancellationRequested)
            {
                var context = await listener.GetContextAsync().WaitAsync(_cts.Token);

                if (context.Request.IsWebSocketRequest)
                {
                    var webSocketContext = await context.AcceptWebSocketAsync(null);
                    _ = HandleClientConnection(webSocketContext.WebSocket);
                }
                else
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                }
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task HandleClientConnection(WebSocket webSocket)
    {
        var buffer = new byte[4096];
        var remoteEndpoint = webSocket.GetRemoteEndpoint();

        Console.WriteLine($"[{DateTime.Now}] New connection from {remoteEndpoint}");

        SipUser connectedUser = null;

        try
        {
            while (webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    Console.WriteLine($"[{DateTime.Now}] Received from {remoteEndpoint}:\n{message}");

                    if (message.StartsWith("REGISTER"))
                    {
                        var fromHeader = GetHeaderValue(message, "From");
                        var username = fromHeader?.Split('@')[0].Split(':')[1];

                        if (!string.IsNullOrEmpty(username) && _users.TryGetValue(username, out var user))
                        {
                            user.WebSocket = webSocket;
                            user.RemoteEndpoint = remoteEndpoint;
                            connectedUser = user;
                            Console.WriteLine($"[{DateTime.Now}] WebSocket stored for user {username}");
                        }
                    }

                    var response = ProcessSipMessage(message, remoteEndpoint);

                    if (!string.IsNullOrEmpty(response))
                    {
                        await webSocket.SendAsync(Encoding.UTF8.GetBytes(response), WebSocketMessageType.Text, true, _cts.Token);
                    }
                }
            }
        }
        finally
        {
            if (connectedUser != null)
            {
                connectedUser.WebSocket = null;
                Console.WriteLine($"[{DateTime.Now}] WebSocket cleared for user {connectedUser.Username}");
            }
            webSocket.Dispose();
            Console.WriteLine($"[{DateTime.Now}] Connection closed for {remoteEndpoint}");
        }
    }

    private static string ProcessSipMessage(string message, string remoteEndpoint)
    {
        try
        {
            if (message.StartsWith("REGISTER")) return HandleRegistration(message, remoteEndpoint);
            if (message.StartsWith("INVITE")) return HandleInvite(message, remoteEndpoint);
            if (message.StartsWith("BYE")) return HandleBye(message);
            if (message.StartsWith("OPTIONS")) return HandleOptions();

            if (message.StartsWith("SIP/2.0"))
            {
                var callId = GetHeaderValue(message, "Call-ID");
                if (!string.IsNullOrEmpty(callId) && _activeCalls.TryGetValue(callId, out var call))
                {
                    var callerSocket = call.CallerWebSocket;
                    if (callerSocket?.State == WebSocketState.Open)
                    {
                        _ = callerSocket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, _cts.Token);
                        Console.WriteLine($"[{DateTime.Now}] Forwarded SIP response to caller {call.Caller}: {message.Split('\r')[0]}");
                    }
                    return "";
                }
                Console.WriteLine($"[{DateTime.Now}] No matching call for Call-ID {callId}");
                return GenerateResponse(481, "Call/Transaction Does Not Exist") + "Content-Length: 0\r\n\r\n";
            }

            return GenerateResponse(501, "Not Implemented") + "Content-Length: 0\r\n\r\n";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now}] Error: {ex.Message}");
            return GenerateResponse(500, "Server Error") + "Content-Length: 0\r\n\r\n";
        }
    }

    private static string HandleRegistration(string message, string remoteEndpoint)
    {
        var from = GetHeaderValue(message, "From");
        var expires = int.Parse(GetHeaderValue(message, "Expires") ?? "0");
        var username = from.Split('@')[0].Split(':')[1];

        if (!_users.TryGetValue(username, out var user))
            return GenerateResponse(404, "Not Found") + "Content-Length: 0\r\n\r\n";

        user.Expires = DateTime.UtcNow.AddSeconds(expires);
        user.RemoteEndpoint = remoteEndpoint;

        var response = GenerateResponse(200, "OK");
        response += $"Expires: {expires}\r\n";
        response += $"Contact: <sip:{username}@{_domain}>;expires={expires}\r\n";
        response += "Content-Length: 0\r\n\r\n";

        Console.WriteLine($"[{DateTime.Now}] User {username} registered (expires: {expires}s)");
        return response;
    }

    private static string HandleInvite(string message, string callerEndpoint)
    {
        var to = GetHeaderValue(message, "To");
        var from = GetHeaderValue(message, "From");
        var callId = GetHeaderValue(message, "Call-ID");

        var caller = from.Split('@')[0].Split(':')[1];
        var callee = to.Split('@')[0].Split(':')[1];

        if (!_users.TryGetValue(callee, out var calleeUser) || !calleeUser.IsRegistered)
            return GenerateResponse(404, "Not Found") + "Content-Length: 0\r\n\r\n";

        _activeCalls[callId] = new ActiveCall
        {
            CallId = callId,
            Caller = caller,
            Callee = callee,
            CallerEndpoint = callerEndpoint,
            CallerWebSocket = _users[caller].WebSocket
        };

        if (calleeUser.WebSocket?.State == WebSocketState.Open)
        {
            _ = calleeUser.WebSocket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, _cts.Token);
            Console.WriteLine($"[{DateTime.Now}] Forwarded INVITE to {callee}");
        }

        var ringing = GenerateResponse(180, "Ringing");
        ringing += $"To: {to}\r\n";
        ringing += $"From: {from}\r\n";
        ringing += $"Call-ID: {callId}\r\n";
        ringing += "Content-Length: 0\r\n\r\n";

        Console.WriteLine($"[{DateTime.Now}] Call {callId}: {caller}->{callee} (ringing)");
        return ringing;
    }

    private static string HandleBye(string message)
    {
        var callId = GetHeaderValue(message, "Call-ID");

        if (_activeCalls.TryGetValue(callId, out var call))
        {
            _activeCalls.Remove(callId);
            Console.WriteLine($"[{DateTime.Now}] Call {callId}: {call.Caller}->{call.Callee} ended");

            // Determine who sent the BYE
            var fromHeader = GetHeaderValue(message, "From");
            var fromUser = fromHeader?.Split('@')[0].Split(':')[1];

            var recipientUser = fromUser == call.Caller ? call.Callee : call.Caller;

            if (_users.TryGetValue(recipientUser, out var recipient) && recipient.WebSocket?.State == WebSocketState.Open)
            {
                _ = recipient.WebSocket.SendAsync(
                    Encoding.UTF8.GetBytes(message),
                    WebSocketMessageType.Text,
                    true,
                    _cts.Token
                );

                Console.WriteLine($"[{DateTime.Now}] Forwarded BYE to {recipient.Username}");
            }
        }

        return GenerateResponse(200, "OK");
    }

    private static string HandleOptions()
    {
        return GenerateResponse(200, "OK") + "Allow: INVITE, ACK, BYE, CANCEL, OPTIONS\r\nContent-Length: 0\r\n\r\n";
    }

    private static string GenerateResponse(int statusCode, string reasonPhrase)
    {
        return $"SIP/2.0 {statusCode} {reasonPhrase}\r\n" +
               $"Via: SIP/2.0/WS {_domain};branch={Guid.NewGuid()}\r\n" +
               "Server: C# SIP Server\r\n";
    }

    private static string GetHeaderValue(string message, string header)
    {
        using var reader = new StringReader(message);
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.StartsWith(header + ":"))
                return line.Substring(header.Length + 1).Trim();
        }
        return null;
    }
}

public class SipUser
{
    public string Username { get; set; }
    public string Password { get; set; }
    public string ContactUri { get; set; }
    public DateTime Expires { get; set; }
    public string RemoteEndpoint { get; set; }
    public WebSocket WebSocket { get; set; }
    public bool IsRegistered => DateTime.UtcNow < Expires;
}

public class ActiveCall
{
    public string CallId { get; set; }
    public string Caller { get; set; }
    public string Callee { get; set; }
    public string CallerEndpoint { get; set; }
    public WebSocket CallerWebSocket { get; set; }
    public DateTime StartTime { get; } = DateTime.UtcNow;
}

public static class WebSocketExtensions
{
    public static string GetRemoteEndpoint(this WebSocket webSocket)
    {
        try { return webSocket.CloseStatusDescription ?? "unknown"; }
        catch { return "unknown"; }
    }
}