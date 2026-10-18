"""Drive the app's explicit QA mode, including real Win32 open/save dialogs."""
import ctypes
import argparse
from ctypes import wintypes
from pathlib import Path
import subprocess
import sys
import time


def main():
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser()
    parser.add_argument("--exe", type=Path, default=root / "Builds" / "Windows" / "MoonbirdStudio.exe")
    parser.add_argument("--output", type=Path, default=root / "TestResults" / "Windows")
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    report = output / "runtime-smoke.txt"
    if report.exists():
        report.unlink()
    executable = args.exe.resolve()
    startup = subprocess.STARTUPINFO()
    startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    startup.wShowWindow = 0
    process = subprocess.Popen([
        str(executable), "-moonbirdSmoke", str(output), "-moonbirdDialogSmoke",
        "-logFile", str(output / "player.log"),
        "-screen-width", "1440", "-screen-height", "900", "-screen-fullscreen", "0",
    ], startupinfo=startup, cwd=str(root))

    user = ctypes.WinDLL("user32", use_last_error=True)
    callback_type = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    user.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
    user.GetClassNameW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    user.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    user.GetDlgItem.argtypes = [wintypes.HWND, ctypes.c_int]
    user.GetDlgItem.restype = wintypes.HWND
    user.EnumWindows.argtypes = [callback_type, wintypes.LPARAM]
    user.EnumChildWindows.argtypes = [wintypes.HWND, callback_type, wintypes.LPARAM]
    user.GetDlgCtrlID.argtypes = [wintypes.HWND]
    user.IsWindowVisible.argtypes = [wintypes.HWND]
    user.SendMessageW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
    user.SendMessageW.restype = wintypes.LPARAM
    user.PostMessageW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
    user.SetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPCWSTR]

    def class_name(hwnd):
        text = ctypes.create_unicode_buffer(256)
        user.GetClassNameW(hwnd, text, len(text))
        return text.value

    def title(hwnd):
        text = ctypes.create_unicode_buffer(1024)
        user.GetWindowTextW(hwnd, text, len(text))
        return text.value

    completed = set()
    first_seen = {}
    deadline = time.monotonic() + 100
    target_path = output / "파일 대화상자 한글 검증.json"
    if target_path.exists():
        target_path.unlink()
    target = str(target_path)
    print("Native file target:", target, flush=True)
    while process.poll() is None and time.monotonic() < deadline:
        dialogs = []

        @callback_type
        def window_callback(hwnd, _):
            pid = wintypes.DWORD()
            user.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
            if pid.value == process.pid and class_name(hwnd) == "#32770":
                dialogs.append(hwnd)
            return True

        user.EnumWindows(window_callback, 0)
        for hwnd in dialogs:
            key = (hwnd, title(hwnd))
            if key in completed or "달을 꿈꾸는 새" not in key[1]:
                continue
            if time.monotonic() - first_seen.setdefault(key, time.monotonic()) < 1:
                continue
            edits = []
            @callback_type
            def child_callback(child, _):
                if class_name(child) == "Edit" and user.IsWindowVisible(child):
                    edits.append(child)
                return True

            user.EnumChildWindows(hwnd, child_callback, 0)
            edits.sort(key=lambda edit: {1001: 0, 1148: 1}.get(user.GetDlgCtrlID(edit), 2))
            if not edits:
                continue
            # WM_CHAR follows the real edit notification path; WM_SETTEXT alone can
            # leave the shell's bound filename unchanged in modern Explorer dialogs.
            user.SendMessageW(edits[0], 0xB1, 0, -1)  # EM_SETSEL
            for character in target:
                user.SendMessageW(edits[0], 0x102, ord(character), 0)  # WM_CHAR
            time.sleep(.25)
            button = user.GetDlgItem(hwnd, 1)
            user.PostMessageW(button, 0xF5, 0, 0)  # BM_CLICK / actual native button notification
            completed.add(key)
            print("Filled native dialog:", key[1], flush=True)
        time.sleep(.2)

    if process.poll() is None:
        process.terminate()
        raise SystemExit("Runtime QA timed out; inspect TestResults/Windows/player.log")
    if not report.exists():
        raise SystemExit("App exited without a QA report; inspect TestResults/Windows/player.log")
    result = report.read_text(encoding="utf-8-sig")
    print(result)
    if result.startswith("PASS") and len(completed) < 2:
        raise SystemExit("Native dialog checks did not run; rebuild the app before QA.")
    raise SystemExit(0 if result.startswith("PASS") else 1)


if __name__ == "__main__":
    main()
