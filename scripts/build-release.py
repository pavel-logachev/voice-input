#!/usr/bin/env python
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

RUNTIME_SHA256 = "9f536cb0fb839bd305e6d92fb214fd417c7718a416a6c7646a9911fbd56fdad5"
MODEL_SHA256 = "7d69952fb431a8d7800ed9910dc61fea37d8406bfe96d10bf24c8bd4b7c68623"


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


def run(command: list[str], cwd: Path) -> None:
    print("+", subprocess.list2cmdline(command), flush=True)
    subprocess.run(command, cwd=cwd, check=True)


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
            "-p:PublishSingleFile=true",
            "-p:IncludeNativeLibrariesForSelfExtract=true",
            "-p:EnableCompressionInSingleFile=true",
            "-p:DebugType=None",
            "-p:DebugSymbols=false",
        ],
        root,
    )


def main() -> int:
    parser = argparse.ArgumentParser(description="Build the self-contained Voice Input Windows installer.")
    parser.add_argument("--version", default="0.4.2")
    args = parser.parse_args()

    root = Path(__file__).resolve().parents[1]
    dotnet = Path(os.environ.get("DOTNET_EXE", r"C:\Program Files\dotnet\dotnet.exe"))
    if not dotnet.is_file():
        raise FileNotFoundError(dotnet)

    runtime_archive = root / ".local" / "runtime" / "transcribe-native-0.1.3-windows-x86_64-cpu-vulkan.tar.gz"
    model_file = root / ".local" / "models" / "gigaam-v3-e2e-rnnt-Q4_K_M.gguf"
    print("Verifying pinned release assets…", flush=True)
    require_sha256(runtime_archive, RUNTIME_SHA256)
    require_sha256(model_file, MODEL_SHA256)

    artifacts = root / "artifacts"
    app_publish = artifacts / "publish" / "win-x64"
    worker_publish = artifacts / "publish" / "worker-win-x64"
    installer_output = artifacts / "installer"
    for path in (app_publish, worker_publish, installer_output):
        if path.exists():
            shutil.rmtree(path)
        path.mkdir(parents=True)

    publish(root / "src" / "VoiceInput.Asr.Worker" / "VoiceInput.Asr.Worker.csproj", worker_publish, dotnet, root)
    publish(root / "src" / "VoiceInput.App" / "VoiceInput.App.csproj", app_publish, dotnet, root)

    app_worker = app_publish / "worker"
    if app_worker.exists():
        shutil.rmtree(app_worker)
    app_worker.mkdir()
    for source in worker_publish.iterdir():
        destination = app_worker / source.name
        if source.is_dir():
            shutil.copytree(source, destination)
        else:
            shutil.copy2(source, destination)

    app_executable = app_publish / "VoiceInput.App.exe"
    worker_executable = app_worker / "VoiceInput.Asr.Worker.exe"
    if not app_executable.is_file() or not worker_executable.is_file():
        raise RuntimeError("Self-contained application or worker executable is missing after publish.")

    iscc = find_iscc()
    installer_name = f"VoiceInput-Setup-{args.version}"
    run(
        [
            str(iscc),
            "/Qp",
            f"/O{installer_output}",
            f"/F{installer_name}",
            f"/DAppVersion={args.version}",
            f"/DPublishDir={app_publish}",
            f"/DRuntimeArchive={runtime_archive}",
            f"/DModelFile={model_file}",
            str(root / "installer" / "VoiceInput.iss"),
        ],
        root,
    )

    setup = installer_output / f"{installer_name}.exe"
    if not setup.is_file():
        raise FileNotFoundError(setup)

    manifest = {
        "version": args.version,
        "runtime": {"file": runtime_archive.name, "sha256": RUNTIME_SHA256},
        "model": {"file": model_file.name, "sha256": MODEL_SHA256},
        "application": {
            "file": app_executable.name,
            "size": app_executable.stat().st_size,
            "sha256": sha256(app_executable),
        },
        "worker": {
            "file": f"worker/{worker_executable.name}",
            "size": worker_executable.stat().st_size,
            "sha256": sha256(worker_executable),
        },
        "installer": {
            "file": setup.name,
            "size": setup.stat().st_size,
            "sha256": sha256(setup),
        },
    }
    manifest_path = installer_output / f"VoiceInput-Setup-{args.version}.manifest.json"
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    print(f"RELEASE_BUILD_PASS installer={setup} size={setup.stat().st_size} sha256={manifest['installer']['sha256']}")
    print(f"manifest={manifest_path}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exception:
        print(f"RELEASE_BUILD_FAIL {exception}", file=sys.stderr)
        raise
