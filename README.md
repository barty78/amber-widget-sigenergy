# Amber + Sigenergy Windows Monitor

A native Windows desktop and system-tray monitor for Amber Electric and a Sigenergy EC 10.0 SP with a 40 kWh battery.

## What it will show

- Live Amber import price (general channel) and export/feed-in price (feedIn channel), in c/kWh.
- Today’s import cost, export earnings, net energy value, imported/exported kWh.
- Current and forecast Amber prices, including negative prices and price spikes.
- Sigenergy battery state of charge, stored energy (40 kWh nameplate), charge/discharge power, PV, grid import/export and home load.
- Daily and lifetime energy totals, trends and a compact tray popup.

## Important accounting rules

- Export earnings are calculated per meter interval as exported kWh × the matching Amber feed-in price.
- Import energy cost is calculated per interval as imported kWh × the matching Amber general price.
- Show gross import cost, gross export earnings and net energy value separately. Do not label net energy value as the final bill: daily supply charges, subscription, demand charges, credits and other bill items may be additional.
- Prefer Amber meter usage intervals for financial accounting. Sigenergy power readings are for live flow/energy estimates and cross-checking; do not double-count them with meter intervals.
- Clearly label estimates and incomplete intervals. Use actual Amber usage records for settled values where available.

## Integrations

### Amber Electric

Official API: https://app.amber.com.au/developers  
API base: https://api.amber.com.au/v1  
Bearer token is supplied by the user and stored locally using Windows secure storage. Never commit API tokens.

The official API exposes site/channel metadata, current and forecast prices, and usage. The app should discover the site and general/feedIn channel identifiers rather than hard-code them.

### Sigenergy

Read-only Modbus TCP initially (port 502; common plant unit ID 247, inverter unit ID configured separately). Confirm the exact register profile and addressing for the installed EC 10.0 SP firmware before relying on any value. Start with battery SOC, ESS power, grid power, PV power and daily energy counters. No write/control registers in the first release.

## Build

Requires Windows 10/11 and .NET 10 SDK.

```powershell
dotnet restore
dotnet build
dotnet run --project src/AmberSigenergy.Desktop
```

## Configuration

Copy `appsettings.example.json` to a local config file (never commit secrets). Configure Amber API token, site (optional auto-discovery), Sigenergy host, Modbus port/unit IDs, and 40 kWh battery capacity.

## Roadmap

1. WPF dashboard shell and tray app.
2. Amber API client, site/channel discovery, price timeline and usage import/export accounting.
3. Sigenergy Modbus read-only telemetry and power-flow diagram.
4. Local SQLite history, earnings/cost charts and data reconciliation.
5. Notifications, startup-on-login, installer and signed release.

## Disclaimer

This is a monitoring/estimation tool, not a billing system or inverter control system. Financial totals are estimates until reconciled against Amber's settled usage and bill data.
