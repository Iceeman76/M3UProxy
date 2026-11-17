# Running M3U Proxy as a service

Service mode runs without a terminal, menus, progress bars, or keyboard input. It uses Windows Service hosting on Windows and systemd notifications on Linux. The existing interactive console mode remains available.

## Modes and configuration

```text
M3UProxyApp --service --proxy --config /etc/m3uproxy/appsettings.json
M3UProxyApp --service --download --config /etc/m3uproxy/appsettings.json
```

`--service` defaults to proxy mode. `--service --download` runs only scheduled downloads, without an HTTP server. `--download` without `--service` still performs one batch and exits. Native Windows Service and systemd hosting are detected automatically; including `--service` explicitly also makes the intended behavior clear when running manually.

`--config` selects the JSON file. Its default is `appsettings.json` beside the application. Relative configuration paths resolve against the application directory. Relative download, log, and browser executable paths resolve against the selected configuration file's directory, independently of the service manager's working directory. Absolute paths are recommended for service deployments.

Merge these settings into a working provider configuration:

```json
"Service": {
  "RefreshIntervalMinutes": 60,
  "RefreshOnStartup": true
}
```

| Setting | Default | Behavior |
| --- | --- | --- |
| `RefreshIntervalMinutes` | `60` | Delay between completion of one scheduled batch and the next. `0` disables periodic refresh in proxy service mode. Download-only service mode requires a positive interval. |
| `RefreshOnStartup` | `true` | Refreshes configured playlists and EPG when the service starts. Set to `false` to wait for the first interval. |

The schedule applies only to service mode. Scheduled batches and refreshes triggered by player requests share a lock and cannot overlap. Each batch downloads all configured files. A failed download retains its previous completed file and is retried at the next interval; failures are logged. Player requests can still refresh missing or stale files according to `Proxy:DownloadMaxAgeMinutes`.

The HTTP server stays available while scheduled downloads run. Proxy service readiness means that the listener has started; the initial background downloads may still be running. Restart after changing settings. Service stop cancels active requests and scheduled downloads, removes unfinished download files, closes browser processes, and disposes logging. The host allows 30 seconds for graceful shutdown.

## Account, browser, and logging requirements

Install the .NET 10 ASP.NET Core Runtime when using a framework-dependent application. Install the chosen Chrome or Edge channel for the machine, or set `Browser:ExecutablePath` to an executable accessible to the service account. A browser installed only in another user's profile is not sufficient. On Linux, also install the browser's required system libraries. No desktop session or separate Node installation is needed.

The service account needs:

- Read and execute access to the application and browser.
- Read access to its configuration; restrict other users' access because provider credentials are stored there.
- Create, write, rename, and delete access to the download and log directories.
- A writable temporary directory and home/profile directory for the browser's isolated temporary profiles.
- Network access to configured providers and permission to bind the configured listen address and port.

Run the Linux service as its own unprivileged user. Keep the browser sandbox enabled. Choose a listen port above 1023, such as the default 5050, to avoid needing elevated port-binding privileges.

Application logs include startup, selected mode/configuration, browser selection and resolved executable, scheduled refreshes, requests, failures, and shutdown. Console and file log levels remain independent, and the same credential redaction applies in service mode. Enable file logging on Windows because there is no attached console. On Linux, console output is collected by the journal. Keep `Logging:LogLevel:Microsoft` at `None` to reduce framework noise.

For Windows, set absolute paths such as `C:\ProgramData\M3UProxy\data\playlist.m3u` and `C:\ProgramData\M3UProxy\logs\m3uproxy.log`. For Linux, use `/var/lib/m3uproxy/playlist.m3u` and `/var/log/m3uproxy/m3uproxy.log`. The examples below grant access to those locations. File logging failures are reported to standard error; monitor the journal or another stderr capture in unattended deployments.

## Windows Service

Place the application in `C:\Services\M3UProxy` and a completed configuration in `C:\ProgramData\M3UProxy\appsettings.json`. Set `Logging:File:Enabled` to `true` and configure download/log paths under the corresponding `data` and `logs` directories.

Run the following in an elevated PowerShell terminal. This uses the dedicated virtual account `NT SERVICE\M3UProxy`, which has no password to manage:

```powershell
$serviceCommand = '"C:\Services\M3UProxy\M3UProxyApp.exe" --service --proxy --config "C:\ProgramData\M3UProxy\appsettings.json"'
sc.exe create M3UProxy binPath= $serviceCommand start= auto obj= 'NT SERVICE\M3UProxy'
New-Item -ItemType Directory -Force 'C:\ProgramData\M3UProxy\data', 'C:\ProgramData\M3UProxy\logs', 'C:\ProgramData\M3UProxy\tmp'
icacls.exe 'C:\Services\M3UProxy' /grant 'NT SERVICE\M3UProxy:(OI)(CI)RX'
icacls.exe 'C:\ProgramData\M3UProxy' /grant 'NT SERVICE\M3UProxy:(OI)(CI)RX'
icacls.exe 'C:\ProgramData\M3UProxy\data' /grant 'NT SERVICE\M3UProxy:(OI)(CI)M'
icacls.exe 'C:\ProgramData\M3UProxy\logs' /grant 'NT SERVICE\M3UProxy:(OI)(CI)M'
icacls.exe 'C:\ProgramData\M3UProxy\tmp' /grant 'NT SERVICE\M3UProxy:(OI)(CI)M'
icacls.exe 'C:\ProgramData\M3UProxy\appsettings.json' /inheritance:r /grant '*S-1-5-32-544:F' '*S-1-5-18:F' 'NT SERVICE\M3UProxy:R'
New-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Services\M3UProxy' -Name Environment -PropertyType MultiString -Value @('TEMP=C:\ProgramData\M3UProxy\tmp', 'TMP=C:\ProgramData\M3UProxy\tmp')
Start-Service M3UProxy
Get-Service M3UProxy
```

The service name must be `M3UProxy`. If using a different dedicated local/domain account instead of the virtual account, grant it **Log on as a service** and the same directory permissions. Ensure its browser policy and profile permit headless browser startup. The example does not change firewall rules.

Manage the service:

```powershell
Stop-Service M3UProxy
Start-Service M3UProxy
Restart-Service M3UProxy
Get-Content 'C:\ProgramData\M3UProxy\logs\m3uproxy.log' -Tail 50 -Wait
```

Remove it:

```powershell
Stop-Service M3UProxy
sc.exe delete M3UProxy
```

Configuration, downloads, and logs remain for the operator to retain or remove. For download-only operation, replace `--proxy` with `--download` in the registered command and keep a positive refresh interval.

## Linux systemd

Place the Linux application in `/opt/m3uproxy` and install Chrome or Edge with its system dependencies. Create a dedicated account and protect the configuration:

```sh
sudo useradd --system --home-dir /var/lib/m3uproxy --no-create-home --shell /usr/sbin/nologin m3uproxy
sudo install -d -m 0750 -o root -g m3uproxy /etc/m3uproxy
sudo install -m 0640 -o root -g m3uproxy appsettings.json /etc/m3uproxy/appsettings.json
sudo chmod 0755 /opt/m3uproxy/M3UProxyApp
sudo install -m 0644 deployment/m3uproxy.service /etc/systemd/system/m3uproxy.service
sudo systemctl daemon-reload
sudo systemctl enable --now m3uproxy
```

Use absolute download paths under `/var/lib/m3uproxy`. If file logging is enabled, use `/var/log/m3uproxy/m3uproxy.log`. The supplied [systemd unit](../deployment/m3uproxy.service) creates writable state, log, and temporary directories for the service account:

```ini
[Service]
Type=notify
User=m3uproxy
Group=m3uproxy
ExecStart=/opt/m3uproxy/M3UProxyApp --service --proxy --config /etc/m3uproxy/appsettings.json
WorkingDirectory=/var/lib/m3uproxy
StateDirectory=m3uproxy
LogsDirectory=m3uproxy
RuntimeDirectory=m3uproxy
Environment=TMPDIR=/run/m3uproxy
UMask=0077
Restart=on-failure
RestartSec=5
TimeoutStopSec=45
KillMode=control-group
```

`Type=notify` lets the application report readiness and stopping to systemd. `KillMode=control-group` also covers child processes if graceful shutdown cannot finish. Keep `TimeoutStopSec` longer than the application's 30-second host timeout.

Manage the service and inspect logs:

```sh
sudo systemctl status m3uproxy
sudo systemctl restart m3uproxy
sudo systemctl stop m3uproxy
sudo systemctl start m3uproxy
sudo journalctl -u m3uproxy -f
```

For download-only operation, replace `--proxy` with `--download` in `ExecStart`, reload systemd, and restart. Remove the service with:

```sh
sudo systemctl disable --now m3uproxy
sudo rm /etc/systemd/system/m3uproxy.service
sudo systemctl daemon-reload
```

The account, application, configuration, downloaded files, and logs remain. Remove them separately if they are no longer needed.

## Troubleshooting

If startup fails, inspect the file log or journal and check the selected configuration path, browser installation, service account permissions, and whether the listen port is already in use. Configuration and binding failures return a nonzero exit code. Provider refresh failures are logged and retried on the next interval while the service remains running.

To reproduce account-specific browser problems, run the same command under the service account where your operating system permits it. A successful run under your signed-in account does not establish that the service account can access a per-user browser installation, temporary directory, or provider network route.
