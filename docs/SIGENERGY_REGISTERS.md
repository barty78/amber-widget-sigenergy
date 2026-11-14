# Sigenergy EC 10.0 SP read-only Modbus profile

Based on Sigenergy Modbus Protocol V2.5 (2025-02-19), which explicitly lists SigenStor EC 10.0 SP in the supported Hybrid Inverter family.

- Transport: Modbus TCP
- TCP port: 502
- Plant unit/slave ID: 247
- Plant running information is read at unit ID 247.
- Read-only register function code: 0x03.
- The application implementation intentionally contains no write function codes.

| Register | Quantity | Value | Scale | Sign |
|---:|---:|---|---:|---|
| 30005 | 2 | Grid sensor active power (kW) | raw / 1000 | positive import, negative export |
| 30014 | 1 | ESS state of charge (%) | raw / 10 | — |
| 30031 | 2 | Plant active power (kW) | raw / 1000 | signed |
| 30035 | 2 | Photovoltaic power (kW) | raw / 1000 | signed |
| 30037 | 2 | ESS power (kW) | raw / 1000 | positive charging, negative discharging |
| 30083 | 2 | ESS rated energy capacity (kWh) | raw / 100 | — |

Addresses and access method are taken from the vendor protocol's plant running information table and Modbus command overview. Registers are read as big-endian 16-bit words, with 32-bit values high word first.

The client reads registers 30005–30038 as one block and 30083 separately. It is read-only and must not be extended with writes without a separate, explicit product decision and safety review.

Vendor protocol PDF: https://b2b.aprilice.com/se_en/mpattachment/file/download/id/778/
