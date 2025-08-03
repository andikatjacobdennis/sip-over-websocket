# 🛠 TROUBLESHOOTING

This document provides solutions to common problems encountered when running or developing with the **SIP over WebSocket** project.

---

## 🔌 Connectivity Issues

### ❌ **Error: Could not connect to WebSocket server**

* **Cause**: SIP server is not running or is bound to a different port/interface.
* **Solution**:

  * Ensure the SIP server is started successfully.
  * Confirm the server is listening on `ws://localhost:8089`.
  * Check firewall settings that might block local WebSocket connections.

---

## 📞 Call Not Reaching Callee

### ❌ **Callee does not show incoming call**

* **Cause**: Callee user is not registered.
* **Solution**:

  * Make sure the callee has registered via the client menu (`Option 1: Register`).
  * Verify that the SIP server logs show the callee as registered.

### ❌ **Callee is registered but still doesn’t receive INVITE**

* **Cause**: Server is not forwarding the INVITE correctly.
* **Solution**:

  * Check server logs for forwarded INVITE.
  * Make sure `WebSocket` reference is set for the callee in server-side user registration.

---

## 📴 Hang Up Not Working

### ❌ **Callee does not receive BYE when caller hangs up**

* **Cause**: BYE not being forwarded to the callee.
* **Solution**:

  * Ensure `Call-ID` is tracked correctly in `_activeCalls`.
  * Validate that the `WebSocket.SendAsync` call is not failing silently.
  * Check for exceptions in server logs related to WebSocket state (e.g., `Aborted`, `Closed`).

---

## ⚠️ WebSocket Exceptions

### ❌ **System.Net.WebSockets.WebSocketException: 'The WebSocket is in an invalid state ('Aborted')'**

* **Cause**: Attempting to send or close a WebSocket that is no longer open.
* **Solution**:

  * Before using `_webSocket`, check: `if (_webSocket.State == WebSocketState.Open)`
  * Avoid using the socket after `CloseAsync`.

---

## 🔄 Client Registration Fails

### ❌ **Client stuck on "Registering..."**

* **Cause**: No response or malformed REGISTER response.
* **Solution**:

  * Confirm that the server handles `REGISTER` and returns a `200 OK` with proper `Expires` and `Contact` headers.
  * Ensure your client waits for and parses the response.

---

## 🧪 Debugging Tips

* Run server and clients in separate terminals with logging enabled.
* Use `Console.WriteLine` generously on both client and server during development.
* To inspect SIP messages more clearly, enable packet capture tools like Wireshark with SIP/WS filters:

  ```
  websocket or sip
  ```

---

## ❓ Need Help?

Feel free to open an issue at [https://github.com/andikatjacobdennis/sip-over-websocket/issues](https://github.com/andikatjacobdennis/sip-over-websocket/issues)
