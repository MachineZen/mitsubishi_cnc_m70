import argparse
import shutil
import subprocess
import sys
from pathlib import Path


DEFAULT_REMOTE_DIRS = [
    "/sdcard/DCIM",
    "/sdcard/Pictures",
    "/sdcard/Download",
    "/sdcard/Movies",
    "/sdcard/Documents",
    "/sdcard/WhatsApp/Media",
    "/sdcard/Telegram",
]


class AdbError(RuntimeError):
    pass


def run_command(command: list[str], check: bool = True) -> subprocess.CompletedProcess[str]:
    result = subprocess.run(
        command,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
    )
    if check and result.returncode != 0:
        message = result.stderr.strip() or result.stdout.strip() or f"command failed: {' '.join(command)}"
        raise AdbError(message)
    return result


def ensure_adb() -> str:
    adb_path = shutil.which("adb")
    if not adb_path:
        raise AdbError("adb was not found on PATH. Install Android platform-tools first.")
    return adb_path


def adb_base_command(adb_path: str, serial: str | None) -> list[str]:
    command = [adb_path]
    if serial:
        command.extend(["-s", serial])
    return command


def parse_adb_devices(output: str) -> list[dict[str, str]]:
    devices: list[dict[str, str]] = []
    for raw_line in output.splitlines():
        line = raw_line.strip()
        if not line or line.startswith("List of devices attached"):
            continue

        parts = line.split()
        if len(parts) < 2:
            continue

        serial = parts[0]
        state = parts[1]
        extra = " ".join(parts[2:]) if len(parts) > 2 else ""
        devices.append({"serial": serial, "state": state, "extra": extra})

    return devices


def get_devices(adb_path: str) -> list[dict[str, str]]:
    result = run_command([adb_path, "devices", "-l"])
    return parse_adb_devices(result.stdout)


def choose_device(devices: list[dict[str, str]], serial: str | None) -> dict[str, str]:
    if serial:
        for device in devices:
            if device["serial"] == serial:
                return device
        raise AdbError(f"device with serial '{serial}' was not found in adb devices output")

    if not devices:
        raise AdbError(
            "adb does not see any phone right now. Fix the USB cable/port/driver first, "
            "then run 'adb devices -l' again."
        )

    if len(devices) > 1:
        lines = [f"{device['serial']} ({device['state']}) {device['extra']}".strip() for device in devices]
        joined = "\n".join(lines)
        raise AdbError(f"multiple adb devices were detected. Re-run with --serial.\n{joined}")

    return devices[0]


def assert_device_ready(device: dict[str, str]) -> None:
    state = device["state"]
    if state == "device":
        return

    if state == "unauthorized":
        raise AdbError(
            "the phone is visible to adb but this PC is not authorized. "
            "With a dead display there is no Python-side bypass for the on-phone trust prompt."
        )

    if state == "offline":
        raise AdbError("the phone is detected but offline. Reconnect USB, unlock the phone if possible, and retry.")

    raise AdbError(f"adb reported state '{state}'. The phone is not ready for file recovery yet.")


def adb_shell(adb_path: str, serial: str | None, shell_command: str, check: bool = True) -> subprocess.CompletedProcess[str]:
    return run_command(adb_base_command(adb_path, serial) + ["shell", shell_command], check=check)


def get_device_property(adb_path: str, serial: str | None, prop: str) -> str:
    result = adb_shell(adb_path, serial, f"getprop {prop}", check=False)
    return result.stdout.strip()


def list_sdcard_entries(adb_path: str, serial: str | None) -> list[str]:
    result = adb_shell(adb_path, serial, "ls -1 /sdcard", check=False)
    if result.returncode != 0:
        return []
    return [line.strip() for line in result.stdout.splitlines() if line.strip()]


def remote_exists(adb_path: str, serial: str | None, remote_path: str) -> bool:
    command = f'if [ -e "{remote_path}" ]; then echo EXISTS; fi'
    result = adb_shell(adb_path, serial, command, check=False)
    return "EXISTS" in result.stdout


def sanitize_output_name(remote_path: str) -> str:
    parts = [part for part in remote_path.strip("/").split("/") if part]
    if not parts:
        return "sdcard"
    return parts[-1]


def pull_directory(adb_path: str, serial: str | None, remote_path: str, output_dir: Path, dry_run: bool) -> tuple[bool, str]:
    target_dir = output_dir / sanitize_output_name(remote_path)
    target_dir.parent.mkdir(parents=True, exist_ok=True)

    if dry_run:
        return True, f"would pull {remote_path} -> {target_dir}"

    result = run_command(adb_base_command(adb_path, serial) + ["pull", remote_path, str(target_dir)], check=False)
    if result.returncode == 0:
        return True, f"pulled {remote_path} -> {target_dir}"

    message = result.stderr.strip() or result.stdout.strip() or "adb pull failed"
    return False, f"failed {remote_path}: {message}"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Recover accessible shared-storage data from an Android phone through adb."
    )
    parser.add_argument("--serial", help="Specific adb device serial to use")
    parser.add_argument(
        "--output",
        type=Path,
        default=Path(r"C:\Recovery"),
        help=r"Output directory for pulled files (default: C:\Recovery)",
    )
    parser.add_argument(
        "--remote",
        action="append",
        dest="remote_dirs",
        help="Remote directory to pull. Repeat for multiple paths. Defaults to common media folders.",
    )
    parser.add_argument("--list-only", action="store_true", help="Only print device info and top-level /sdcard entries")
    parser.add_argument("--dry-run", action="store_true", help="Show what would be copied without running adb pull")
    return parser.parse_args()


def main() -> int:
    args = parse_args()

    try:
        adb_path = ensure_adb()
        devices = get_devices(adb_path)
        device = choose_device(devices, args.serial)
        assert_device_ready(device)

        serial = device["serial"]
        model = get_device_property(adb_path, serial, "ro.product.model") or "unknown"
        android_version = get_device_property(adb_path, serial, "ro.build.version.release") or "unknown"

        print(f"adb device: {serial}")
        print(f"model: {model}")
        print(f"android: {android_version}")

        entries = list_sdcard_entries(adb_path, serial)
        if entries:
            print("\nTop-level /sdcard entries:")
            for entry in entries:
                print(f"  - {entry}")
        else:
            print("\nUnable to list /sdcard contents.")

        if args.list_only:
            return 0

        remote_dirs = args.remote_dirs or DEFAULT_REMOTE_DIRS
        args.output.mkdir(parents=True, exist_ok=True)
        print(f"\nSaving recovered files under: {args.output}")

        copied = 0
        skipped = 0
        failed = 0

        for remote_dir in remote_dirs:
            if not remote_exists(adb_path, serial, remote_dir):
                print(f"skip {remote_dir}: not found")
                skipped += 1
                continue

            ok, message = pull_directory(adb_path, serial, remote_dir, args.output, args.dry_run)
            print(message)
            if ok:
                copied += 1
            else:
                failed += 1

        print(f"\nSummary: copied={copied} skipped={skipped} failed={failed}")
        return 0 if failed == 0 else 2
    except AdbError as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1
    except KeyboardInterrupt:
        print("\nCancelled by user.", file=sys.stderr)
        return 130


if __name__ == "__main__":
    raise SystemExit(main())
