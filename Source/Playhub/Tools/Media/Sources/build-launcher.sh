#!/usr/bin/env bash
set -euo pipefail
export PATH="/mingw64/bin:/usr/bin:$PATH"
here="$(cd -- "$(dirname -- "$0")" && pwd)"
gcc -municode -O2 -s -static "$here/ytdlp_launcher.c" -o "${1:?Pass the output yt-dlp.exe path}"
