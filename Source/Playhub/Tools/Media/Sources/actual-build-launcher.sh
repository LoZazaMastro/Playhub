#!/usr/bin/env bash
set -euo pipefail
export PATH="/mingw64/bin:/usr/bin:$PATH"
cd /f/Playhub/Plugin/Playhub
gcc -municode -O2 -s -static Source/ApplicationIntegrations/ToolsSource/ytdlp_launcher.c -o Source/Playhub/Tools/Media/yt-dlp.exe
