# M80 Live Dashboard Setup

## 1. Prerequisites

- Collector executable built in one of these locations:
  - `..\build\collector\MitsubishiCncCollector.exe`
  - `..\build\publish\MitsubishiCncMonitor\MitsubishiCncCollector.exe`
  - `..\m80_smoke_test.exe`
- Python 3.10+.
- InfluxDB 2.x running (`http://localhost:8086`).
- Grafana running (`http://localhost:3000`).

To rebuild the packaged collector and desktop app:

```bat
powershell -ExecutionPolicy Bypass -File ..\scripts\publish-dashboard.ps1
```

## 2. Install Python dependency

```bat
cd /d C:\Users\innom\OneDrive\Desktop\Integration\mitsubishi_cnc_m70_ezsocket_net\dashboard
py -m pip install -r requirements.txt
```

## 3. InfluxDB one-time setup

Create these in InfluxDB UI:

- Organization: `your-org`
- Bucket: `cnc`
- API Token: read/write access to bucket `cnc`

### If using InfluxDB 2.x

Set env vars in terminal:

```bat
set INFLUX_URL=http://localhost:8086
set INFLUX_ORG=your-org
set INFLUX_BUCKET=cnc
set INFLUX_TOKEN=your_token_here
```

### If using InfluxDB 1.7.x (your current setup)

Create database once:

```bat
curl -G http://localhost:8086/query --data-urlencode "q=CREATE DATABASE cnc"
```

Optional auth env vars:

```bat
set INFLUX_USER=your_user
set INFLUX_PASSWORD=your_password
```

## 4. Start ingestion (CNC -> InfluxDB)

Run from `dashboard` folder:

```bat
py cnc_to_influx.py --exe ..\m80_smoke_test.exe --ip 192.168.200.15 --port 683 --nc-type 8 --interval-ms 1000 --samples 0 --machine M80_1
```

For InfluxDB 1.7 force mode explicitly:

```bat
py cnc_to_influx.py --influx-version 1 --influx-url http://localhost:8086 --influx-db cnc --exe ..\m80_smoke_test.exe --ip 192.168.200.15 --port 683 --nc-type 8 --interval-ms 1000 --samples 0 --machine M80_1 --ideal-cycle-sec 1.0
```

State persistence (recommended):

```bat
py cnc_to_influx.py --influx-version 1 --influx-url http://localhost:8086 --influx-db cnc --exe ..\m80_smoke_test.exe --ip 192.168.200.15 --port 683 --nc-type 8 --interval-ms 1000 --samples 0 --machine M80_1 --ideal-cycle-sec 1.0 --state-file cnc_oee_state.json
```

## 4A. Export CNC -> CSV

Run from `dashboard` folder:

```bat
py cnc_to_csv.py --ip 192.168.200.15 --port 683 --nc-type 8 --interval-ms 100 --samples 600 --output ..\logs\m80_capture.csv
```

Notes:

- `cnc_to_csv.py` auto-detects the collector executable from the packaged build or repo root.
- CSV output includes `timestamp_local`, `timestamp_utc`, and `unix_ms`.
- Per-axis columns use collector `axis_names` when available, with safe fallbacks like `axis1`, `axis2`, etc.

## 4B. Open CSV Logs In A Shareable Web Table

Run from `dashboard` folder:

```bat
py csv_log_web.py --host 0.0.0.0 --port 8080 --log-dir ..\build\publish\MitsubishiCncMonitor\logs
```

Open this in your browser on the same PC:

```text
http://127.0.0.1:8080/
```

Share this link with other PCs on your network:

```text
http://YOUR_PC_IP:8080/
```

Features:

- Shows the CSV log in a browser table with paging and search.
- Lets you switch between log files.
- Provides `Download CSV` and `Download Excel` buttons for the selected log.
- The `Copy Share Link` button copies a URL that keeps the selected file in view.

## 5. Grafana datasource

Add datasource:

- Type: `InfluxDB`
- Query language: `Flux`
- URL: `http://localhost:8086`
- Organization: `your-org`
- Token: same token
- Default bucket: `cnc`

## 6. Import dashboard

- Grafana -> Dashboards -> Import
- Upload file: `grafana_dashboard_m80.json`
- Select the InfluxDB datasource you created.

For InfluxDB 1.7 (InfluxQL), import:

- `grafana_dashboard_m80_influxql.json`
- Datasource type must be InfluxDB with query language `InfluxQL`
- Database: `cnc`

Professional OEE dashboard (InfluxQL):

- `grafana_dashboard_m80_oee_professional_influxql.json`
- Includes OEE, availability, performance, quality, running/stopped, AUTO/MDI/REF, total runtime/downtime.

## 7. Notes

- If no data appears, verify ingestion terminal prints `wrote ts=...`.
- If tool/torque fields are null, CNC may not expose those fields in current mode/state.
- The dashboard filters machine tag as `M80_1`. Change this in `cnc_to_influx.py` command (`--machine`) or edit panel queries.
- Current `grafana_dashboard_m80.json` uses Flux (InfluxDB 2). For InfluxDB 1.7, create panels with InfluxQL or upgrade InfluxDB to 2.x.
- Runtime/downtime/OEE are calculated in the bridge process and now persisted in `--state-file` across restarts/power loss.
- To intentionally reset OEE counters, stop bridge and delete state file (for example `del cnc_oee_state.json`) then start again.
- The collector now emits `ts_ms` and `axis_names`. Both bridge scripts use millisecond timestamps when present.
