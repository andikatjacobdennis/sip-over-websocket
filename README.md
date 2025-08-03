# SIP over WebSocket Implementation

![License](https://img.shields.io/badge/License-MIT-blue.svg)
![.NET](https://img.shields.io/badge/.NET-9.0-purple.svg)

A high-performance SIP (Session Initiation Protocol) server and client implementation using WebSockets, designed for modern real-time communication systems.

## Key Features

- **WebSocket Transport**: Full SIP protocol support over WebSocket transport (RFC 7118)
- **Scalable Architecture**: Designed for horizontal scaling in cloud environments
- **Enterprise-grade**: Production-ready with proper error handling and recovery
- **Modern .NET**: Built with .NET 6+ for high performance
- **Full SIP Core**: Supports REGISTER, INVITE, BYE, OPTIONS methods
- **Call Management**: Complete call state tracking and routing

## Architecture Overview

```mermaid
sequenceDiagram
    participant ClientA
    participant Server
    participant ClientB
    
    ClientA->>Server: REGISTER
    Server-->>ClientA: 200 OK
    ClientB->>Server: REGISTER
    Server-->>ClientB: 200 OK
    
    ClientA->>Server: INVITE (ClientB)
    Server->>ClientB: Forward INVITE
    ClientB-->>Server: 180 Ringing
    Server-->>ClientA: Forward 180
    ClientB-->>Server: 200 OK
    Server-->>ClientA: Forward 200
    ClientA->>Server: ACK
    Server->>ClientB: Forward ACK
    
    Note over ClientA,ClientB: Media session established
    
    ClientA->>Server: BYE
    Server->>ClientB: Forward BYE
    ClientB-->>Server: 200 OK
    Server-->>ClientA: Forward 200
