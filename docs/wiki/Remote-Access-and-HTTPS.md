# Remote Access and HTTPS

Out of the box the panel only accepts connections from the server itself (`http://localhost:8080`). This page explains how to manage it from other computers, and how to protect it with HTTPS.

> **Security first.** The panel can run commands on your server and replace files. Only expose it to networks you trust, use a strong password, and use HTTPS for anything beyond a trusted LAN. Plain HTTP sends your password unencrypted.

## Option 1: Listen on the network (LAN)

If you ticked *Allow other computers to open the web panel* in the setup wizard, this is already done. Otherwise:

1. From an **administrator** PowerShell window, tell the panel to listen on all network interfaces:

   ```powershell
   & "C:\Program Files\Minecraft Server Manager\MinecraftServerManager.exe" configure --remote-access
   ```

   This sets `"Urls": "http://0.0.0.0:8080"` in `C:\ProgramData\Minecraft Server Manager\appsettings.json` (keeping the current port). `--local-only` undoes it. With the zip, edit `Urls` in the `appsettings.json` next to the executable instead. To listen on one interface only, set `Urls` to that address, e.g. `http://192.168.1.20:8080`.

2. Allow the port through Windows Firewall:

   ```powershell
   New-NetFirewallRule -DisplayName "Minecraft Server Manager (TCP 8080)" -Direction Inbound -Protocol TCP -LocalPort 8080 -Action Allow -Profile Domain,Private
   ```

   Limiting the rule to the *Domain* and *Private* profiles keeps it closed on public networks. You can also restrict it with `-RemoteAddress 192.168.1.0/24`.

3. Restart the service: `Restart-Service MinecraftServerManager`.
4. Browse to `http://<server-name-or-ip>:8080` from another PC.

## Option 2: HTTPS directly in the panel

The panel's web server (Kestrel) can serve HTTPS with a certificate in PFX format.

1. Get a certificate for the name you'll use (e.g. `mc-panel.example.local`): from your internal certificate authority, or from a public CA if the name is public. Export it as a `.pfx` with its private key.
2. Copy it to the server, e.g. `C:\certs\panel.pfx`.
3. Open `C:\ProgramData\Minecraft Server Manager\appsettings.json` (or, with the zip, the `appsettings.json` next to the executable) and **replace** its `"Urls"` line with:

   ```json
   "Kestrel": {
     "Endpoints": {
       "Https": {
         "Url": "https://0.0.0.0:8443",
         "Certificate": {
           "Path": "C:\\certs\\panel.pfx",
           "Password": "<pfx password>"
         }
       }
     }
   },
   ```

   Note the doubled backslashes in JSON paths.

4. Open TCP 8443 in the firewall and restart the service.
5. Browse to `https://mc-panel.example.local:8443`.

> Don't put this in `C:\Program Files\Minecraft Server Manager\appsettings.json`: that file is replaced when you upgrade. The installers already limit `C:\ProgramData\Minecraft Server Manager` to administrators and the service, which protects a certificate password stored there. Alternatively load the certificate from the Windows certificate store; see Microsoft's [Kestrel endpoint configuration](https://learn.microsoft.com/aspnet/core/fundamentals/servers/kestrel/endpoints) documentation.

## Option 3: Behind a reverse proxy

You can leave the panel on `http://localhost:8080` and publish it through a reverse proxy that handles HTTPS, such as **IIS with Application Request Routing**, **Caddy**, **nginx** or **Traefik**.

Requirements for the proxy:

- **WebSockets must be allowed.** The panel's pages use a live WebSocket connection (path `/_blazor`). In IIS, install the **WebSocket Protocol** feature; in nginx, pass the `Upgrade` and `Connection` headers.
- Keep the connection timeout generous (a few minutes), because the live connection stays open.
- Serve the panel at the root of a host name (e.g. `https://mc.example.com/`), not in a sub-folder.

Example (Caddy):

```text
mc.example.com {
    reverse_proxy localhost:8080
}
```

Example (nginx):

```nginx
location / {
    proxy_pass         http://127.0.0.1:8080;
    proxy_http_version 1.1;
    proxy_set_header   Upgrade $http_upgrade;
    proxy_set_header   Connection "upgrade";
    proxy_set_header   Host $host;
    proxy_read_timeout 300s;
}
```

> Behind a proxy, every sign-in attempt appears to come from the proxy's address, so the sign-in lockout (5 failures → 5 minutes) applies to everyone at once. Use the proxy's own rate limiting if this matters.

## Access from the internet

Avoid exposing the panel directly to the internet. Better options, in order of preference:

1. A **VPN** (WireGuard, Tailscale, your firewall's VPN) and access it as if on the LAN.
2. A reverse proxy with HTTPS **and** an extra sign-in layer (for example your proxy's authentication or an identity-aware proxy).

The Minecraft server's own port (25565) is separate and is what players need; see [Installation](Installation#open-the-firewall-for-players).
