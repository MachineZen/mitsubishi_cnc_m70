import argparse
import csv
import json
import re
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

MODE_MAP = {
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

RUN_STATUS_MAP = {
    0: "RST",
    1: "EMG",
    2: "RDY",
    3: "AUT",
    4: "SYN",
    5: "CRS",
    6: "BST",
    7: "HLD",
}


def resolve_default_exe() -> Path:
    repo_root = Path(__file__).resolve().parent.parent
    candidates = [
        repo_root / "build" / "collector" / "MitsubishiCncCollector.exe",
        repo_root / "build" / "publish" / "MitsubishiCncMonitor" / "MitsubishiCncCollector.exe",
        repo_root / "m80_smoke_test.exe",
    ]
    for candidate in candidates:
        if candidate.exists():
            return candidate
    return candidates[0]


def sanitize_axis_label(name: str, fallback: str) -> str:
    cleaned = re.sub(r"[^0-9a-zA-Z]+", "_", name.strip()).strip("_").lower()
    return cleaned or fallback


def unique_axis_labels(sample: dict) -> list[str]:
    axis_count = int(sample.get("axis_count", 0) or 0)
    raw_names = sample.get("axis_names")
    labels: list[str] = []
    seen: dict[str, int] = {}

    for index in range(axis_count):
        fallback = f"axis{index + 1}"
        raw_name = fallback
        if isinstance(raw_names, list) and index < len(raw_names) and raw_names[index]:
            raw_name = str(raw_names[index])
        label = sanitize_axis_label(raw_name, fallback)
        count = seen.get(label, 0)
        seen[label] = count + 1
        if count > 0:
            label = f"{label}_{count + 1}"
        labels.append(label)

    return labels


def axis_metric_column(label: str, suffix: str) -> str:
    if suffix.startswith("axis_") and label.endswith("_axis"):
        return f"{label}_{suffix[len('axis_'):]}"
    return f"{label}_{suffix}"


def build_headers(axis_labels: list[str]) -> list[str]:
    headers = [
        "timestamp_local",
        "timestamp_utc",
        "unix_ms",
        "sample",
        "status_text",
        "mode_text",
        "run_status_text",
        "spindle_rpm",
        "spindle_torque_load",
        "feed_rate",
    ]
    for label in axis_labels:
        headers.append(axis_metric_column(label, "servo_torque"))
    for label in axis_labels:
        headers.append(axis_metric_column(label, "axis_feed_rate"))
    headers.append("alarm_text")
    return headers


def sample_timestamp_fields(sample: dict) -> tuple[str, str, int]:
    unix_ms = int(sample.get("ts_ms") or int(sample.get("ts", 0) or 0) * 1000)
    if unix_ms <= 0:
        now = datetime.now().astimezone()
        return (
            now.strftime("%Y-%m-%d %H:%M:%S.%f")[:-3],
            now.astimezone(timezone.utc).strftime("%Y-%m-%d %H:%M:%S.%f")[:-3],
            0,
        )

    dt_local = datetime.fromtimestamp(unix_ms / 1000.0).astimezone()
    dt_utc = datetime.fromtimestamp(unix_ms / 1000.0, tz=timezone.utc)
    return (
        dt_local.strftime("%Y-%m-%d %H:%M:%S.%f")[:-3],
        dt_utc.strftime("%Y-%m-%d %H:%M:%S.%f")[:-3],
        unix_ms,
    )


def build_row(sample: dict, axis_labels: list[str]) -> dict[str, object]:
    timestamp_local, timestamp_utc, unix_ms = sample_timestamp_fields(sample)
    mode = sample.get("mode")
    run_status = sample.get("run_status")
    row: dict[str, object] = {
        "timestamp_local": timestamp_local,
        "timestamp_utc": timestamp_utc,
        "unix_ms": unix_ms,
        "sample": sample.get("sample"),
        "status_text": sample.get("status_text", ""),
        "mode_text": MODE_MAP.get(mode, ""),
        "run_status_text": RUN_STATUS_MAP.get(run_status, ""),
        "spindle_rpm": sample.get("spindle_speed"),
        "spindle_torque_load": sample.get("spindle_torque_load"),
        "feed_rate": sample.get("feed_speed"),
        "alarm_text": sample.get("alarm_text", ""),
    }

    axis_torque = sample.get("axis_torque")
    axis_feed_rate = sample.get("axis_feed_rate")
    if not isinstance(axis_torque, list):
        axis_torque = []
    if not isinstance(axis_feed_rate, list):
        axis_feed_rate = []

    for index, label in enumerate(axis_labels):
        row[axis_metric_column(label, "servo_torque")] = axis_torque[index] if index < len(axis_torque) else ""
    for index, label in enumerate(axis_labels):
        row[axis_metric_column(label, "axis_feed_rate")] = axis_feed_rate[index] if index < len(axis_feed_rate) else ""

    return row


def stream_samples(args: argparse.Namespace):
    command = [
        str(args.exe),
        args.ip,
        str(args.port),
        str(args.nc_type),
        str(args.interval_ms),
        str(args.samples),
    ]
    process = subprocess.Popen(
        command,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        encoding="utf-8",
        errors="replace",
        bufsize=1,
    )

    try:
        assert process.stdout is not None
        for line in process.stdout:
            text = line.strip()
            if not text or not text.startswith("{"):
                continue
            yield json.loads(text)
    finally:
        if process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)

        stderr_text = ""
        if process.stderr is not None:
            stderr_text = process.stderr.read().strip()

        if process.returncode not in (0, None) and stderr_text:
            raise RuntimeError(stderr_text)
        if process.returncode not in (0, None) and not stderr_text:
            raise RuntimeError(f"Collector exited with code {process.returncode}")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Capture M80 collector samples to CSV.")
    parser.add_argument("--exe", type=Path, default=resolve_default_exe(), help="Path to MitsubishiCncCollector.exe")
    parser.add_argument("--ip", required=True, help="Controller IP address")
    parser.add_argument("--port", type=int, default=683, help="Controller port")
    parser.add_argument("--nc-type", type=int, default=8, help="NC type (8 for M80/M800)")
    parser.add_argument("--interval-ms", type=int, default=100, help="Requested polling interval in milliseconds")
    parser.add_argument("--samples", type=int, default=0, help="Number of samples to capture (0 = infinite)")
    parser.add_argument("--output", type=Path, required=True, help="CSV file path")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if not args.exe.exists():
        print(f"Collector executable was not found at {args.exe}", file=sys.stderr)
        return 1

    args.output.parent.mkdir(parents=True, exist_ok=True)

    sample_count = 0
    axis_labels: list[str] | None = None
    with args.output.open("w", newline="", encoding="utf-8") as handle:
        writer = None
        try:
            for sample in stream_samples(args):
                if axis_labels is None:
                    axis_labels = unique_axis_labels(sample)
                    writer = csv.DictWriter(handle, fieldnames=build_headers(axis_labels))
                    writer.writeheader()

                assert writer is not None
                writer.writerow(build_row(sample, axis_labels))
                handle.flush()
                sample_count += 1
        except KeyboardInterrupt:
            pass

    print(f"Wrote {sample_count} samples to {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
