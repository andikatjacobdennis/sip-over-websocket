# Development Environment Setup

## Prerequisites

### Hardware Requirements
- Minimum: 4GB RAM, 2-core CPU
- Recommended: 8GB+ RAM, 4-core CPU (for media processing)

### Operating Systems
- ✅ Linux (Ubuntu 22.04 LTS recommended)
- ✅ macOS (M1/Intel)
- ⚠️ Windows (WSL2 required for full functionality)

---

## 1. Core Dependencies

### For All Platforms
```bash
# Node.js (LTS version)
curl -fsSL https://deb.nodesource.com/setup_lts.x | sudo -E bash -
sudo apt-get install -y nodejs

# Python (for some SIP components)
sudo apt install python3-pip

# Build Essentials
sudo apt install build-essential
```

### SIP Stack (PJSIP)
```bash
# Ubuntu/Debian
sudo apt install libpjproject-dev pkg-config

# macOS
brew install pjsip
```

### WebSocket Server
```bash
npm install ws@latest --save
```

---

## 2. Database Setup

### Redis (Session Store)
```bash
sudo apt install redis-server
sudo systemctl enable redis
```

### PostgreSQL (Optional for CDRs)
```bash
sudo apt install postgresql postgresql-contrib
sudo -u postgres createdb sip_ws
```

---

## 3. Configuration Files

### Environment Variables
Create `.env` in project root:
```ini
# WebSocket Server
WS_PORT=5080
WS_SECURE_PORT=5443

# SIP Configuration
SIP_DOMAIN=sip.yourdomain.com
SIP_PROXY=your.sip.proxy

# TLS Certificates (generate with Let's Encrypt)
TLS_CERT=/etc/letsencrypt/live/yourdomain.com/fullchain.pem
TLS_KEY=/etc/letsencrypt/live/yourdomain.com/privkey.pem
```

### PJSIP Config
`/etc/pjsip.cfg`:
```ini
[transport]
type = ws
port = 5080

[account]
id = <your_account>
registrar = sip:your.sip.proxy
```

---

## 4. Running the System

### Development Mode
```bash
# Start WebSocket proxy
npm run dev-server

# Start SIP component (separate terminal)
npm run dev-sip
```

### With Docker (Alternative)
```bash
docker-compose -f docker-compose.dev.yml up
```

---

## 5. Testing Tools

### SIP Clients
- [Wireshark](https://www.wireshark.org/) (with SIP plugin)
- [SIPp](https://github.com/SIPp/sipp) (load testing):
  ```bash
  sipp -sn uac -ws_uri ws://localhost:5080 -s 1234 127.0.0.1
  ```

### Web Client
```bash
# Launch test page
python3 -m http.server 8000
```
Access: `http://localhost:8000/test-client.html`

---

## 6. Debugging Tips

### Common Issues
1. **Port Conflicts**:
   ```bash
   sudo lsof -i :5080
   ```

2. **TLS Errors**:
   ```bash
   openssl s_client -connect localhost:5443 -showcerts
   ```

3. **SIP Registration Failures**:
   Check PJSIP logs:
   ```bash
   journalctl -u pjsip -f
   ```

### Log Locations
- WebSocket: `logs/ws_server.log`
- SIP: `/var/log/pjsip.log`
- Combined: `npm run logs` (preconfigured script)

---

## 7. IDE Configuration

### VS Code Recommended Extensions
- **ESLint** (JavaScript linting)
- **REST Client** (for API testing)
- **Mermaid** (architecture diagram preview)

### Launch Configuration
`.vscode/launch.json`:
```json
{
  "configurations": [
    {
      "type": "node",
      "request": "launch",
      "name": "Debug WS Server",
      "program": "${workspaceFolder}/src/server.js"
    }
  ]
}
```

---

## 8. Contribution Workflow

1. Create feature branch:
   ```bash
   git checkout -b feat/new-feature
   ```

2. Commit with semantic messages:
   ```bash
   git commit -m "feat(transport): add WS keep-alive support"
   ```

3. Run tests:
   ```bash
   npm test
   ```

4. Submit PR to `main` branch

---

## Need Help?
- Check `TROUBLESHOOTING.md` for known solutions
- Open an issue with:
  ```bash
  npm run report-issue
  ```
