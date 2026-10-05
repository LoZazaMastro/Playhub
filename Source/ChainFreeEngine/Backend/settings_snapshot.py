"""Create a verified, isolated settings candidate without retiring or writing Decky."""
from pathlib import Path
import hashlib
import json
import os
import shutil
import uuid


def stage_settings(source, destination):
    source, destination = Path(source).absolute(), Path(destination).absolute()
    if not source.is_dir() or destination.exists() or source == destination or source in destination.parents or destination in source.parents:
        raise ValueError("Use a new, separate destination for the settings candidate.")
    def safe(path):
        for part in (path, *path.parents):
            if part.exists() and (part.is_symlink() or getattr(part.lstat(), "st_file_attributes", 0) & 0x400):
                raise ValueError("Linked settings paths are not supported.")
    safe(source); safe(destination)
    files = []
    for path in source.rglob("*"):
        safe(path)
        if path.is_file() and path.name != ".playhub-backend.lock":
            if path.stat().st_size > 32 * 1024 * 1024 or len(files) >= 4096:
                raise ValueError("Settings snapshot exceeds the supported size.")
            files.append(path)
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_name(destination.name + ".staging-" + uuid.uuid4().hex)
    temporary.mkdir()
    try:
        manifest = []
        total = 0
        for path in files:
            relative = path.relative_to(source)
            content = path.read_bytes(); total += len(content)
            if total > 128 * 1024 * 1024: raise ValueError("Settings snapshot exceeds the supported size.")
            target = temporary / relative; target.parent.mkdir(parents=True, exist_ok=True)
            with target.open("xb") as stream:
                stream.write(content); stream.flush(); os.fsync(stream.fileno())
            digest = hashlib.sha256(content).hexdigest()
            if hashlib.sha256(path.read_bytes()).hexdigest() != digest: raise ValueError("Source settings changed during snapshot.")
            manifest.append({"path": relative.as_posix(), "bytes": len(content), "sha256": digest})
        # Recheck all sources after copying: never attest a mixed settings revision.
        for entry in manifest:
            if hashlib.sha256((source / entry["path"]).read_bytes()).hexdigest() != entry["sha256"]:
                raise ValueError("Source settings changed during snapshot.")
        receipt = {"schemaVersion": 1, "status": "staged-only", "files": manifest}
        (temporary / "playhub-migration-manifest.json").write_text(json.dumps(receipt, indent=2), encoding="utf-8")
        os.rename(temporary, destination)
        return {"files": len(manifest), "bytes": total, "status": "staged-only"}
    except BaseException:
        shutil.rmtree(temporary)
        raise
