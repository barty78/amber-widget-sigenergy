# Sigenergy EC 10.0 SP read-only Modbus profile

Based on Sigenergy Modbus Protocol V2.5 (2025-02-19), which lists SigenStor EC 10.0 SP in the supported Hybrid Inverter family.

- Transport: Modbus TCP, TCP port 502.
- Plant registers use the configured plant unit ID (default 247).
- Hybrid-inverter registers use a separate inverter unit ID (default 1; configurable as `Sigenergy.InverterUnitId`).
- All reads use FC03. The application contains no write function codes.

| Unit | Register | Quantity | Value | Scale | Sign / notes |
|---|---:|---:|---|---:|---|
| Plant | 30005 | 2 | Grid sensor active power (kW) | raw / 1000 | positive import, negative export |
| Plant | 30014 | 1 | ESS state of charge (%) | raw / 10 | — |
| Plant | 30031 | 2 | Plant active power (kW) | raw / 1000 | signed |
| Plant | 30035 | 2 | Photovoltaic power (kW) | raw / 1000 | — |
| Plant | 30037 | 2 | ESS power (kW) | raw / 1000 | positive charging, negative discharging |
| Plant | 30083 | 2 | ESS rated energy capacity (kWh) | raw / 100 | — |
| Inverter | 30566 | 2 | Battery daily charge energy (kWh) | raw / 100 | native daily counter |
| Inverter | 30572 | 2 | Battery daily discharge energy (kWh) | raw / 100 | native daily counter |
| Plant | 30272 | 2 | Daily PV generation (kWh) | raw / 100 | undocumented/empirical; best-effort only |

Grid daily import/export are not treated as native counters in V2.5: registers 30554–30565 are reserved in V2.5. The application integrates grid CT power over time for those two values. Daily home load is calculated from the energy balance (PV - battery charge + battery discharge + grid import - grid export).

Register 30272 is not documented in the public V2.5 map and is therefore optional; if unavailable, solar energy falls back to local power integration. Battery counters 30566 and 30572 are documented inverter-level registers, not plant-level registers. They are queried separately using the inverter unit ID.

32-bit values are decoded as big-endian, high word first. All monitoring is read-only.

Vendor protocol reference: https://b2b.aprilice.com/se_en/mpattachment/file/download/id/778/
