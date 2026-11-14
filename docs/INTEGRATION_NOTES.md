# Integration and accounting notes

## Amber

Amber confirms its official REST API provides prices, forecasts and usage. Generate an API token from the Amber web app's Developers page: https://app.amber.com.au/developers

Expected integration flow:
1. Discover sites for the authenticated account.
2. Select the electricity site/meter.
3. Fetch current prices and forecasts; select channels by channel type, not array order.
4. Fetch usage intervals for the chosen local date range.
5. Match each import interval with the General channel price and each export interval with the Feed In channel price, using the interval start/end and timezone correctly.
6. Retain raw interval records and mark actual vs forecast / incomplete intervals.

Amber's displayed general usage price includes wholesale, network usage costs, other usage costs and GST. Feed-in prices do not include GST. Live prices and interval usage costs do not include daily supply charges, Amber subscription, demand charges or other bill adjustments.

Amber also documents that negative solar feed-in tariffs can affect usage summaries differently from the final bill: negative export pricing is capped at $0 on the bill. Keep raw interval export value distinct from bill-credit estimate and do not claim a final bill amount from live prices alone.

## Sigenergy

Initial integration is read-only Modbus TCP. A commonly documented plant unit ID is 247, with useful read-only values including grid active power (30005), ESS state of charge (30014), and PV power (31035). Register maps and addressing can differ by protocol version, inverter family and firmware. Confirm the exact EC 10.0 SP profile before enabling polling; never write configuration/control registers.

Power sign conventions from the cited register documentation:
- Grid sensor active power: positive = grid import, negative = grid export.
- ESS charge/discharge power: positive = charging, negative = discharging (verify against the exact selected register profile).

## Financial display definitions

- Import cost: sum of imported kWh × matching General price / 100.
- Export interval value: sum of exported kWh × matching Feed In price / 100. This may be negative when feed-in prices are negative.
- Net interval energy value: export interval value minus import cost.
- Bill credit estimate: must apply Amber's bill treatment for negative feed-in tariffs and be reconciled with settled usage/billing records.
- Final bill: not calculated until daily charges, subscription, demand charges, taxes/adjustments, rebates and billing-period rules are included.

Use Amber usage intervals as the financial source of truth. Sigenergy live power readings are for live display and cross-checking, not an additional source to add to meter interval totals.
