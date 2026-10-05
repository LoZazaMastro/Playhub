# Rebuilding the bundled media tools

The source and license archives in this directory correspond to the pinned executable inventory. Playhub is a separate process consumer. No source from a GPL plugin was incorporated into the app's providers or launcher.

FFmpeg 8.1.1: extract ffmpeg-8.1.1.tar.xz into a new directory. Use the MSYS2 MINGW64 environment with GCC 16.1.0, GNU make and binutils. Run `bash build-ffmpeg.sh /absolute/path/to/ffmpeg-8.1.1`. The portable script contains every configure argument, disables all autodetected external libraries and uses only internal AAC/H264 decoding and AAC encoding plus local file/pipe protocols. It builds with two workers. Copy the resulting ffmpeg.exe and ffprobe.exe beside runtime-manifest.json. ffmpeg-config.mak/config.h and actual-build-ffmpeg.sh record the original build. The complete archive contains all FFmpeg program and library source, enabling modification/relinking.

Playhub launcher: run `bash build-launcher.sh /absolute/output/path/yt-dlp.exe` in the same MINGW64 environment. It builds ytdlp_launcher.c with `gcc -municode -O2 -s -static`. Source is MIT. It preserves each Windows command-line argument and returns the private child's exit code.

CPython: Python-3.14.8.tar.xz is the complete matching source. The official embedded build uses the upstream PCbuild project and upstream Windows build instructions. The shipped Python directory is the official embedded distribution with only python314._pth changed to `python314.zip`, `.`, `../yt-dlp.pyz` (one line each, no import site).

Node: node-v24.21.0.tar.xz is the complete matching official source. Its BUILDING.md describes Windows vcbuild.bat and upstream tool requirements. The shipped node.exe is the unmodified official win-x64 release.

yt-dlp: yt-dlp-2026.08.18.122307.tar.gz contains the pinned source release. The shipped yt-dlp.pyz is the official zipimport `yt-dlp` asset for that same nightly, commit 5d5b634. Follow its bundle/README for zipimport rebuilding and yt-dlp-ejs-0.8.0.tar.gz/package.json for the bundled solver dependencies; no PyInstaller executable or optional GPL Mutagen package is used. Included solver license headers preserve meriyah/astring attribution.

After rebuilding a tool, update its files group in runtime-manifest.json with the SHA256 of every changed executable/interpreter dependency. Integrity is checked per explicit job, not continuously. No signature bypass or installed process manipulation is necessary.
