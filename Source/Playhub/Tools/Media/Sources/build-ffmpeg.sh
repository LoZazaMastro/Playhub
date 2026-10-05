#!/usr/bin/env bash
set -euo pipefail
export PATH="/mingw64/bin:/usr/bin:$PATH"
cd "${1:?Pass the extracted ffmpeg-8.1.1 source directory}"
./configure --target-os=mingw32 --arch=x86_64 --disable-autodetect --disable-everything --disable-doc --disable-debug --disable-shared --enable-static --disable-x86asm --disable-network --disable-iconv --disable-bzlib --disable-lzma --disable-zlib --disable-sdl2 --disable-avdevice --disable-gpl --disable-version3 --enable-ffmpeg --enable-ffprobe --enable-avcodec --enable-avformat --enable-avfilter --enable-swresample --enable-swscale --enable-small --enable-demuxer=mov,matroska,mp3,wav,flac,aac,ogg --enable-muxer=mp4,ipod,adts,ogg,wav --enable-parser=aac,aac_latm,mpegaudio,opus,vorbis,flac,h264 --enable-decoder=aac,aac_latm,mp3,mp3float,flac,vorbis,opus,pcm_s16le,pcm_s24le,pcm_s32le,pcm_f32le,h264 --enable-encoder=aac,pcm_s16le --enable-protocol=file,pipe --enable-filter=loudnorm,aresample,aformat,anull,volume,atrim,asetpts --enable-bsf=aac_adtstoasc,h264_mp4toannexb --extra-ldflags=-static
make -j2 ffmpeg.exe ffprobe.exe
