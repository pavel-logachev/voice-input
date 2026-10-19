#!/usr/bin/env python
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import struct
import subprocess
import sys
import tarfile

RUNTIME_SHA256 = "9f536cb0fb839bd305e6d92fb214fd417c7718a416a6c7646a9911fbd56fdad5"
MODEL_SHA256 = "7d69952fb431a8d7800ed9910dc61fea37d8406bfe96d10bf24c8bd4b7c68623"
REQUIRED_ICON_SIZES = {16, 20, 24, 32, 48, 64, 128, 256}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def require_sha256(path: Path, expected: str) -> None:
    if not path.is_file():
        raise FileNotFoundError(path)
    actual = sha256(path)
    if actual.lower() != expected.lower():
        raise RuntimeError(f"Checksum mismatch for {path.name}: expected {expected}, got {actual}")


def require_icon_sizes(path: Path) -> None:
    if not path.is_file():
        raise FileNotFoundError(path)
    data = path.read_bytes()
    if len(data) < 6:
        raise RuntimeError(f"Invalid ICO header in {path}")
    reserved, image_type, image_count = struct.unpack_from("<HHH", data)
    if reserved != 0 or image_type != 1 or len(data) < 6 + (16 * image_count):
        raise RuntimeError(f"Invalid ICO directory in {path}")

    sizes: set[int] = set()
    for index in range(image_count):
        width, height = struct.unpack_from("<BB", data, 6 + (16 * index))
        normalized_width = width or 256
        normalized_height = height or 256
        if normalized_width != normalized_height:
            raise RuntimeError(f"Non-square icon image in {path}: {normalized_width}x{normalized_height}")
        sizes.add(normalized_width)

    if sizes != REQUIRED_ICON_SIZES:
        raise RuntimeError(
            f"Icon sizes mismatch for {path.name}: expected {sorted(REQUIRED_ICON_SIZES)}, got {sorted(sizes)}"
        )


def run(command: list[str], cwd: Path) -> None:
    print("+", subprocess.list2cmdline(command), flush=True)
    subprocess.run(command, cwd=cwd, check=True, timeout=900)


def find_iscc() -> Path:
    candidates = [
        Path(os.environ.get("LOCALAPPDATA", "")) / "Programs" / "Inno Setup 6" / "ISCC.exe",
        Path(os.environ.get("ProgramFiles(x86)", "")) / "Inno Setup 6" / "ISCC.exe",
        Path(os.environ.get("ProgramFiles", "")) / "Inno Setup 6" / "ISCC.exe",
    ]
    for candidate in candidates:
        if candidate.is_file():
            return candidate
    raise FileNotFoundError("ISCC.exe was not found. Install JRSoftware.InnoSetup with winget.")


def publish(project: Path, output: Path, dotnet: Path, root: Path) -> None:
    run(
        [
            str(dotnet),
            "publish",
            str(project),
            "--configuration",
            "Release",
            "--runtime",
            "win-x64",
            "--self-contained",
            "true",
            "--output",
            str(output),
            "-p:PublishSingleFile=false",
            "-p:DebugType=None",
            "-p:DebugSymbols=false",
        ],
        root,
    )


def project_version(root: Path) -> str:
    """The version the application is built with: one source for the executable and the installer name."""
    properties = (root / "Directory.Build.props").read_text(encoding="utf-8")
    match = re.search(r"<Version>([^<]+)</Version>", properties)
    if match is None:
        raise RuntimeError("Directory.Build.props has no <Version>.")
    return match.group(1).strip()


def main() -> int:
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description="Build the self-contained Voice Input Windows installer.")
    parser.add_argument("--version", default=project_version(root))
    parser.add_argument("--output-dir", type=Path, default=root / "artifacts")
    parser.add_argument("--runtime-archive", type=Path,
                        default=root / ".local" / "runtime" / "transcribe-native-0.1.3-windows-x86_64-cpu-vulkan.tar.gz")
    args = parser.parse_args()
    if args.version != project_version(root):
        raise RuntimeError(
            f"--version {args.version} does not match Directory.Build.props ({project_version(root)}): "
            "the settings window would show a different version than the installer."
        )

    dotnet = Path(os.environ.get("DOTNET_EXE", r"C:\Program Files\dotnet\dotnet.exe"))
    if not dotnet.is_file():
        raise FileNotFoundError(dotnet)

    icon_file = root / "assets" / "VoiceInput.ico"
    print("Verifying release assets...", flush=True)
    require_icon_sizes(icon_file)

    runtime_archive = args.runtime_archive.resolve()
    require_sha256(runtime_archive, RUNTIME_SHA256)

    artifacts = args.output_dir.resolve()
    app_publish = artifacts / "publish" / "win-x64"
    worker_publish = artifacts / "publish" / "worker-win-x64"
    installer_output = artifacts / "installer"
    for path in (app_publish, worker_publish, installer_output):
        if path.exists():
            shutil.rmtree(path)
        path.mkdir(parents=True)

    publish(root / "src" / "VoiceInput.App" / "VoiceInput.App.csproj", app_publish, dotnet, root)
    app_executable = app_publish / "VoiceInput.App.exe"
    if not app_executable.is_file():
        raise RuntimeError("Self-contained application executable is missing after publish.")

    # Every installer ships the isolated engine and pinned native runtime, never the model.
    app_worker = app_publish / "worker"
    if app_worker.exists():
        shutil.rmtree(app_worker)
    publish(root / "src" / "VoiceInput.Asr.Worker" / "VoiceInput.Asr.Worker.csproj", worker_publish, dotnet, root)
    # WindowsDesktop supplies CoreCLR plus the managed framework superset. Its assemblies can differ
    # from NETCore's (e.g. VisualBasic), so retain the app's framework and copy only worker-owned files.
    app_config = json.loads((app_publish / "VoiceInput.App.runtimeconfig.json").read_text(encoding="utf-8"))["runtimeOptions"]
    worker_config = json.loads((worker_publish / "VoiceInput.Asr.Worker.runtimeconfig.json").read_text(encoding="utf-8"))["runtimeOptions"]
    app_frameworks = {f["name"]: f["version"] for f in app_config["includedFrameworks"]}
    for framework in worker_config["includedFrameworks"]:
        if app_frameworks.get(framework["name"]) != framework["version"]:
            raise RuntimeError("App and worker must use the same app-local runtime version.")
    for source in worker_publish.glob("VoiceInput.Asr.Worker.*"):
        shutil.copy2(source, app_publish / source.name)
    require_sha256(app_publish / "VoiceInput.Core.dll", sha256(worker_publish / "VoiceInput.Core.dll"))
    worker_executable = app_publish / "VoiceInput.Asr.Worker.exe"
    if not worker_executable.is_file():
        raise RuntimeError("The local ASR worker executable is missing after publish.")
    with tarfile.open(runtime_archive, "r:gz") as archive:
        archive.extractall(app_publish / "runtime", filter="data")

    iscc = find_iscc()
    installer_name = f"VoiceInput-Setup-{args.version}"
    command = [
        str(iscc),
        "/Qp",
        f"/O{installer_output}",
        f"/F{installer_name}",
        f"/DAppVersion={args.version}",
        f"/DPublishDir={app_publish}",
    ]
    command.append(str(root / "installer" / "VoiceInput.iss"))
    run(command, root)

    setup = installer_output / f"{installer_name}.exe"
    if not setup.is_file():
        raise FileNotFoundError(setup)

    manifest = {
        "version": args.version,
        "engines": ["openai", "local", "custom-openai-compatible"],
        "icon": {
            "file": str(icon_file.relative_to(root)),
            "sizes": sorted(REQUIRED_ICON_SIZES),
            "sha256": sha256(icon_file),
        },
        "application": {
            "file": app_executable.name,
            "size": app_executable.stat().st_size,
            "sha256": sha256(app_executable),
        },
        "installer": {
            "file": setup.name,
            "size": setup.stat().st_size,
            "sha256": sha256(setup),
        },
    }
    manifest["runtime"] = {"file": runtime_archive.name, "sha256": RUNTIME_SHA256,
                           "archive_size": runtime_archive.stat().st_size,
                           "extracted_size": sum(p.stat().st_size for p in (app_publish / "runtime").rglob("*") if p.is_file())}
    manifest["model"] = {"delivery": "on-demand", "sha256": MODEL_SHA256}
    manifest["worker"] = {
        "file": worker_executable.name,
        "deployment": "shared-app-local-runtime",
        "size": worker_executable.stat().st_size,
        "sha256": sha256(worker_executable),
    }
    manifest_path = installer_output / f"VoiceInput-Setup-{args.version}.manifest.json"
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    print(f"RELEASE_BUILD_PASS installer={setup} size={setup.stat().st_size} sha256={manifest['installer']['sha256']}")
    print(f"manifest={manifest_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
