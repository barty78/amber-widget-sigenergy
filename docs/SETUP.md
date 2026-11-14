# First-run setup (Windows 10/11)

## 1. Prerequisites

Install the .NET 10 SDK for Windows from https://dotnet.microsoft.com/download/dotnet/10.0

## 2. Clone and run

Open PowerShell:

```powershell
git clone https://github.com/barty78/amber-widget-sigenergy.git
cd amber-widget-sigenergy
dotnet run --project .\src\AmberSigenergy.Desktop
```

## 3. Connect Amber

1. In the app, select **Connect Amber**.
2. Paste the API token from https://app.amber.com.au/developers.
3. Select **Save & test connection**.
4. The app encrypts the token with Windows DPAPI for the current Windows account and requests the site's metadata. The site response is displayed for initial verification.

The token is stored under the current user's LocalAppData folder and is not written to appsettings or the repository.

## 4. Check Sigenergy network access

From the Windows PC that will run the app:

```powershell
Test-NetConnection 192.168.1.21 -Port 502
```

A successful test should report `TcpTestSucceeded : True`. If false, check that the PC is on the same LAN/VLAN, the inverter Modbus TCP service is enabled, and the router/firewall permits TCP port 502.

The application uses read-only Modbus function code 03 and plant unit ID 247. This first connection test checks TCP reachability only; it does not yet validate register responses.

## Notes

- Never paste the API token into GitHub, source files, issues or chat.
- The current dashboard is a scaffold. Amber site discovery is wired; price/usage parsing and binding live values into the dashboard are the next implementation step.
- Sigenergy telemetry client is implemented read-only, but still needs to be exercised against the actual inverter and firmware before treating values as verified.
