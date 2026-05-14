import argparse
import json
import os
import subprocess
import sys
import urllib.parse
import urllib.request
from datetime import datetime, timezone

from influxdb_client import InfluxDBClient, Point
from influxdb_client.client.write_api import SYNCHRONOUS

RUN_STATUS_RUNNING = {3, 4, 5, 6}
MODE_AUTO = {0, 1, 2}
MODE_MDI = {3}
MODE_REF = {12}

MODE_TEXT = {
    0: "MEM",
    1: "DNC",
    2: "LNK",
    3: "MDI",
    4: "PC",
    5: "MNL",
    6: "JOG",
    7: "J+H",
    8: "R+H",
    9: "HDL",
    10: "STP",
    11: "STP1",
    12: "REF",
    13: "DRT",
    14: "INI",
    15: "NON",
    16: "LIN",
}

RUN_STATUS_TEXT = {
    0: "RST",
    1: "EMG",
    2: "RDY",
    3: "AUT",
    4: "SYN",
    5: "CRS",
    6: "BST",
    7: "HLD",
}


def _to_number(value):
    if isinstance(value, bool):
        return int(value)
    if isinstance(value, (int, float)):
        return value
    return None


def sample_unix_ms(sample):
    raw_ms = sample.get("ts_ms")
    if isinstance(raw_ms, (int, float)):
        unix_ms = int(raw_ms)
        if unix_ms > 0:
            return unix_ms

    raw_ts = sample.get("ts")
    if isinstance(raw_ts, (int, float)):
        unix_ts = int(raw_ts)
        if unix_ts > 0:
            return unix_ts * 1000

    return int(datetime.now(timezone.utc).timestamp() * 1000)


def sample_axis_names(sample):
    axis_count = int(sample.get("axis_count", 0) or 0)
    raw_names = sample.get("axis_names")
    names = []

    for index in range(axis_count):
        fallback = f"Axis{index + 1}"
        if isinstance(raw_names, list) and index < len(raw_names):
            raw_name = raw_names[index]
            if raw_name is not None:
                text = str(raw_name).strip()
                if text:
                    names.append(text)
                    continue
        names.append(fallback)

    return names


def load_tracker_state(path):
    if not path:
        return None
    if not os.path.exists(path):
        return None
    try:
        with open(path, "r", encoding="utf-8") as f:
            data = json.load(f)
        return data
    except Exception:
        return None


def save_tracker_state(path, tracker):
    if not path:
        return
    try:
        d = os.path.dirname(path)
        if d:
            os.makedirs(d, exist_ok=True)
        with open(path, "w", encoding="utf-8") as f:
            json.dump(tracker, f, ensure_ascii=True)
    except Exception:
        pass


def _is_running(sample):
    run_status = sample.get("run_status")
    mode = sample.get("mode")
    spindle_speed = sample.get("spindle_speed", 0) or 0
    feed_speed = sample.get("feed_speed", 0) or 0

    # Primary rule: CNC cycle states AUT/SYN/CRS/BST indicate running.
    if run_status in RUN_STATUS_RUNNING:
        return True

    # Fallback: motion/rotation implies running even if run_status is not exposed correctly.
    if isinstance(spindle_speed, (int, float)) and spindle_speed > 0:
        return True
    if isinstance(feed_speed, (int, float)) and feed_speed > 0:
        return True

    # In RDY/EMG/HLD or manual/reference modes, treat as not running.
    if mode in MODE_REF:
        return False
    return False


def enrich_sample(sample, tracker, ideal_cycle_sec):
    s = dict(sample)
    unix_ms = sample_unix_ms(s)
    ts = unix_ms // 1000
    part_count = int(s.get("part_count", s.get("counter", 0)) or 0)
    mode = s.get("mode")
    run_status = s.get("run_status")
    running = _is_running(s)
    productive_running = running and (mode in MODE_AUTO)

    prev_ts_ms = tracker.get("prev_ts_ms")
    if prev_ts_ms is None and tracker.get("prev_ts") is not None:
        prev_ts_ms = int(tracker["prev_ts"]) * 1000

    if prev_ts_ms is not None:
        dt = max(0.0, (unix_ms - prev_ts_ms) / 1000.0)
        if productive_running:
            tracker["run_sec"] += dt
        else:
            tracker["down_sec"] += dt

    tracker["prev_ts_ms"] = unix_ms
    tracker["prev_ts"] = ts

    if tracker["last_part_raw"] is None:
        tracker["last_part_raw"] = part_count
    else:
        delta = part_count - tracker["last_part_raw"]
        if delta >= 0:
            tracker["total_good_parts"] += delta
        else:
            # Counter reset/power cycle detected. Do not add current raw count again,
            # otherwise good parts gets inflated and performance/OEE becomes invalid.
            tracker["counter_reset_events"] += 1
        tracker["last_part_raw"] = part_count

    good_parts = max(0, int(tracker["total_good_parts"]))
    planned_time = tracker["run_sec"] + tracker["down_sec"]
    availability = (tracker["run_sec"] / planned_time) if planned_time > 0 else 0.0
    performance_raw = ((ideal_cycle_sec * good_parts) / tracker["run_sec"]) if tracker["run_sec"] > 0 else 0.0
    performance = performance_raw
    if performance < 0.0:
        performance = 0.0
    if performance > 1.0:
        performance = 1.0
    quality = 1.0  # good parts = produced parts for now
    oee = availability * performance * quality
    if oee < 0:
        oee = 0.0
    if oee > 1.0:
        oee = 1.0

    mode_text = MODE_TEXT.get(mode, "UNKNOWN")
    run_status_text = RUN_STATUS_TEXT.get(run_status, "UNKNOWN")
    if mode in MODE_AUTO:
        mode_class = "AUTO"
    elif mode in MODE_MDI:
        mode_class = "MDI"
    elif mode in MODE_REF:
        mode_class = "REF"
    else:
        mode_class = "OTHER"

    s["good_parts"] = good_parts
    s["machine_running"] = 1 if productive_running else 0
    s["machine_stopped"] = 0 if productive_running else 1
    s["machine_state_text"] = "RUNNING" if productive_running else "STOPPED"
    s["machine_signal_running"] = 1 if running else 0
    s["machine_connected"] = 1
    s["mode_auto"] = 1 if mode in MODE_AUTO else 0
    s["mode_mdi"] = 1 if mode in MODE_MDI else 0
    s["mode_ref"] = 1 if mode in MODE_REF else 0
    s["mode_text"] = mode_text
    s["mode_class_text"] = mode_class
    s["run_status_text"] = run_status_text

    s["total_run_time_sec"] = float(tracker["run_sec"])
    s["total_down_time_sec"] = float(tracker["down_sec"])
    s["total_run_time_min"] = float(tracker["run_sec"]) / 60.0
    s["total_down_time_min"] = float(tracker["down_sec"]) / 60.0

    s["availability_pct"] = availability * 100.0
    s["performance_raw_pct"] = performance_raw * 100.0
    s["performance_pct"] = performance * 100.0
    s["quality_pct"] = quality * 100.0
    s["counter_reset_events"] = int(tracker["counter_reset_events"])
    s["oee_pct"] = oee * 100.0
    s["ts"] = ts
    s["ts_ms"] = unix_ms
    return s


def build_point(sample, machine):
    unix_ms = sample_unix_ms(sample)
    point = Point("m80_metrics").tag("machine", machine)

    for key, val in sample.items():
        if key in {"ts", "axis_torque", "axis_feed_rate", "axis_names", "ret"}:
            continue
        if isinstance(val, bool):
            point = point.field(key, int(val))
        elif isinstance(val, (int, float, str)):
            point = point.field(key, val)

    axis_torque = sample.get("axis_torque", [])
    if isinstance(axis_torque, list):
        for i, val in enumerate(axis_torque, start=1):
            num = _to_number(val)
            if num is not None:
                point = point.field(f"axis_torque_{i}", num)

    axis_feed_rate = sample.get("axis_feed_rate", [])
    if isinstance(axis_feed_rate, list):
        for i, val in enumerate(axis_feed_rate, start=1):
            num = _to_number(val)
            if num is not None:
                point = point.field(f"axis_feed_rate_{i}", num)

    for i, name in enumerate(sample_axis_names(sample), start=1):
        point = point.field(f"axis_name_{i}", name)

    point = point.time(datetime.fromtimestamp(unix_ms / 1000.0, tz=timezone.utc))
    return point


def _escape_tag(v):
    return str(v).replace("\\", "\\\\").replace(",", "\\,").replace(" ", "\\ ").replace("=", "\\=")


def _escape_field_str(v):
    return str(v).replace("\\", "\\\\").replace('"', '\\"')


def build_line_protocol(sample, machine):
    unix_ms = sample_unix_ms(sample)
    fields = {}

    for key, val in sample.items():
        if key in {"ts", "axis_torque", "axis_feed_rate", "axis_names", "ret"}:
            continue
        if isinstance(val, bool):
            fields[key] = int(val)
        elif isinstance(val, (int, float, str)):
            fields[key] = val

    axis_torque = sample.get("axis_torque", [])
    if isinstance(axis_torque, list):
        for i, val in enumerate(axis_torque, start=1):
            num = _to_number(val)
            if num is not None:
                fields[f"axis_torque_{i}"] = num

    axis_feed_rate = sample.get("axis_feed_rate", [])
    if isinstance(axis_feed_rate, list):
        for i, val in enumerate(axis_feed_rate, start=1):
            num = _to_number(val)
            if num is not None:
                fields[f"axis_feed_rate_{i}"] = num

    for i, name in enumerate(sample_axis_names(sample), start=1):
        fields[f"axis_name_{i}"] = name

    if not fields:
        return None

    parts = []
    for k, v in fields.items():
        if isinstance(v, bool):
            parts.append(f"{k}=true" if v else f"{k}=false")
        elif isinstance(v, int):
            parts.append(f"{k}={v}i")
        elif isinstance(v, float):
            parts.append(f"{k}={v}")
        else:
            parts.append(f'{k}="{_escape_field_str(v)}"')

    tag_machine = _escape_tag(machine)
    line = f"m80_metrics,machine={tag_machine} " + ",".join(parts) + f" {unix_ms * 1000000}"
    return line


def detect_influx_version(url):
    try:
        req = urllib.request.Request(url.rstrip("/") + "/ping", method="GET")
        with urllib.request.urlopen(req, timeout=5) as resp:
            ver = resp.headers.get("X-Influxdb-Version", "")
            if ver.startswith("1."):
                return 1
            if ver.startswith("2."):
                return 2
    except Exception:
        return None
    return None


def write_influx_v1(url, db, line, user=None, password=None, rp=None):
    params = {"db": db, "precision": "ns"}
    if user:
        params["u"] = user
    if password:
        params["p"] = password
    if rp:
        params["rp"] = rp
    write_url = url.rstrip("/") + "/write?" + urllib.parse.urlencode(params)
    req = urllib.request.Request(write_url, data=line.encode("utf-8"), method="POST")
    with urllib.request.urlopen(req, timeout=10) as resp:
        if resp.status != 204:
            body = resp.read().decode("utf-8", errors="ignore")
            raise RuntimeError(f"Influx v1 write failed: status={resp.status}, body={body}")


def stream_samples(proc):
    while True:
        line = proc.stdout.readline()
        if line == "" and proc.poll() is not None:
            break
        if not line:
            continue
        line = line.strip()
        if not line or not line.startswith("{"):
            print(f"skip: {line}", file=sys.stderr)
            continue
        try:
            data = json.loads(line)
            yield data
        except json.JSONDecodeError as exc:
            print(f"invalid json: {exc}: {line}", file=sys.stderr)


def main():
    parser = argparse.ArgumentParser(description="Stream M80 JSON samples to InfluxDB.")
    parser.add_argument("--exe", default="m80_smoke_test.exe", help="Path to m80_smoke_test executable")
    parser.add_argument("--ip", required=True, help="CNC IP address")
    parser.add_argument("--port", default="683", help="CNC port")
    parser.add_argument("--nc-type", default="8", help="NC type (8 or 9 for M80/M800)")
    parser.add_argument("--interval-ms", default="1000", help="Polling interval in ms")
    parser.add_argument("--samples", default="0", help="0 for infinite")
    parser.add_argument("--machine", default="M80_1", help="Machine tag")
    parser.add_argument("--ideal-cycle-sec", type=float, default=1.0, help="Ideal cycle time per part in seconds for OEE performance")
    parser.add_argument("--state-file", default="cnc_oee_state.json", help="Local state file for cumulative counters across restarts")

    parser.add_argument("--influx-url", default=os.getenv("INFLUX_URL", "http://localhost:8086"))
    parser.add_argument("--influx-version", choices=["auto", "1", "2"], default=os.getenv("INFLUX_VERSION", "auto"))
    parser.add_argument("--influx-db", default=os.getenv("INFLUX_DB", "cnc"))
    parser.add_argument("--influx-user", default=os.getenv("INFLUX_USER"))
    parser.add_argument("--influx-password", default=os.getenv("INFLUX_PASSWORD"))
    parser.add_argument("--influx-rp", default=os.getenv("INFLUX_RP"))
    parser.add_argument("--influx-token", default=os.getenv("INFLUX_TOKEN"))
    parser.add_argument("--influx-org", default=os.getenv("INFLUX_ORG"))
    parser.add_argument("--influx-bucket", default=os.getenv("INFLUX_BUCKET", "cnc"))

    args = parser.parse_args()

    influx_version = args.influx_version
    if influx_version == "auto":
        detected = detect_influx_version(args.influx_url)
        if detected is None:
            raise SystemExit("Could not detect Influx version from /ping. Set --influx-version 1 or 2.")
        influx_version = str(detected)

    cmd = [
        args.exe,
        args.ip,
        str(args.port),
        str(args.nc_type),
        str(args.interval_ms),
        str(args.samples),
    ]
    print(f"starting: {' '.join(cmd)}")
    print(f"oee config: ideal_cycle_sec={args.ideal_cycle_sec}")

    tracker = {
        "prev_ts": None,
        "prev_ts_ms": None,
        "run_sec": 0.0,
        "down_sec": 0.0,
        "last_part_raw": None,
        "total_good_parts": 0,
        "counter_reset_events": 0,
    }
    saved = load_tracker_state(args.state_file)
    if isinstance(saved, dict):
        tracker.update(saved)
        print(f"loaded state: {args.state_file}")

    proc = subprocess.Popen(
        cmd,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        bufsize=1,
    )
    try:
        if influx_version == "2":
            if not args.influx_token or not args.influx_org:
                raise SystemExit("Set --influx-token and --influx-org (or INFLUX_TOKEN / INFLUX_ORG env vars).")
            with InfluxDBClient(url=args.influx_url, token=args.influx_token, org=args.influx_org) as client:
                write_api = client.write_api(write_options=SYNCHRONOUS)
                for raw_sample in stream_samples(proc):
                    sample = enrich_sample(raw_sample, tracker, args.ideal_cycle_sec)
                    point = build_point(sample, args.machine)
                    write_api.write(bucket=args.influx_bucket, org=args.influx_org, record=point)
                    save_tracker_state(args.state_file, tracker)
                    print(
                        f"wrote v2 ts={sample.get('ts')} status={sample.get('status_text')} "
                        f"rpm={sample.get('spindle_speed')} part={sample.get('part_count')} tool={sample.get('tool_number')} "
                        f"good={sample.get('good_parts')} oee={sample.get('oee_pct'):.2f}% "
                        f"run={sample.get('total_run_time_min'):.2f}m down={sample.get('total_down_time_min'):.2f}m"
                    )
        else:
            for raw_sample in stream_samples(proc):
                sample = enrich_sample(raw_sample, tracker, args.ideal_cycle_sec)
                line = build_line_protocol(sample, args.machine)
                if line is None:
                    continue
                write_influx_v1(
                    url=args.influx_url,
                    db=args.influx_db,
                    line=line,
                    user=args.influx_user,
                    password=args.influx_password,
                    rp=args.influx_rp,
                )
                save_tracker_state(args.state_file, tracker)
                print(
                    f"wrote v1 ts={sample.get('ts')} status={sample.get('status_text')} "
                    f"rpm={sample.get('spindle_speed')} part={sample.get('part_count')} tool={sample.get('tool_number')} "
                    f"good={sample.get('good_parts')} oee={sample.get('oee_pct'):.2f}% "
                    f"run={sample.get('total_run_time_min'):.2f}m down={sample.get('total_down_time_min'):.2f}m"
                )
    finally:
        if proc.poll() is None:
            proc.terminate()


if __name__ == "__main__":
    main()
