# 📘 API Reference

This document outlines the key SIP messages, server behavior, and WebSocket API usage in the **SIP over WebSocket** project.

---

## 📡 SIP Message Support

The SIP server supports a subset of core SIP methods over WebSocket:

| Method     | Description                     | Supported | Notes                                |
| ---------- | ------------------------------- | --------- | ------------------------------------ |
| `REGISTER` | Registers a SIP user            | ✅         | Expires header must be included      |
| `INVITE`   | Initiates a call                | ✅         | Routed to recipient if registered    |
| `BYE`      | Terminates a call               | ✅         | Ends the call session                |
| `OPTIONS`  | Capability query                | ✅         | Responds with basic Allow header     |
| `ACK`      | Acknowledges 200 OK from INVITE | ⚠️        | Expected by clients, no-op on server |
| `CANCEL`   | Cancels a pending INVITE        | ❌         | Not yet implemented                  |

---

## 🧾 SIP Header Requirements

For successful SIP message processing, the following headers are expected:

### 🔐 REGISTER

```plaintext
REGISTER sip:localhost SIP/2.0
From: <sip:1001@localhost>
To: <sip:1001@localhost>
Call-ID: random-call-id
CSeq: 1 REGISTER
Expires: 3600
Content-Length: 0
```

**Server Response:**

```plaintext
SIP/2.0 200 OK
Via: SIP/2.0/WS localhost;branch=...
Server: C# SIP Server
Expires: 3600
Contact: <sip:1001@localhost>;expires=3600
Content-Length: 0
```

---

### ☎️ INVITE

```plaintext
INVITE sip:1002@localhost SIP/2.0
From: <sip:1001@localhost>;tag=randomtag
To: <sip:1002@localhost>
Call-ID: unique-call-id
CSeq: 1 INVITE
Content-Length: 0
```

**Server Behavior:**

* Validates recipient is registered.
* Forwards INVITE to callee's WebSocket.
* Sends `180 Ringing` to caller.

---

### ❌ BYE

```plaintext
BYE sip:1002@localhost SIP/2.0
From: <sip:1001@localhost>
To: <sip:1002@localhost>
Call-ID: same-call-id
CSeq: 2 BYE
Content-Length: 0
```

**Server Response:**

```plaintext
SIP/2.0 200 OK
```

* Terminates call from `_activeCalls` dictionary.

---

## 🔄 WebSocket Message Flow

### Client ➡️ Server

Clients send raw SIP messages over WebSocket as UTF-8 strings.

### Server ➡️ Client

Server responds with SIP-formatted strings or forwards messages (e.g. INVITE, 180 Ringing) to the appropriate client.

---

## 🛑 Error Responses

| Code | Description           |
| ---- | --------------------- |
| 404  | Not Found             |
| 500  | Internal Server Error |
| 501  | Not Implemented       |

Returned when an invalid or unsupported SIP method is received.

---

## 📦 Example Call Flow

1. **Client A** registers with `REGISTER`
2. **Client B** registers
3. **Client A** sends `INVITE` to B
4. Server:

   * Forwards `INVITE` to B
   * Sends `180 Ringing` to A
5. **Client B** may send `200 OK` or `486 Busy`
6. **Client A** sends `ACK` (optional/no-op)
7. Either party sends `BYE`
8. Server sends `200 OK` to BYE sender and clears call

---

## 📁 See Also

* [TROUBLESHOOTING.md](TROUBLESHOOTING.md)
* [RFC 3261 - SIP Protocol](https://datatracker.ietf.org/doc/html/rfc3261)

