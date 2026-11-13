// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "CallerImage.h"
namespace view {
static std::shared_ptr<const PinnedRenderer> OpenConfiguredRenderer(const wchar_t* path){
#if defined(PLAYHUB_TEXTURE_RENDERER_ATTESTED)
 return PinnedRenderer::OpenAttestedSteamRenderer(path);
#elif defined(PLAYHUB_TEXTURE_RENDERER_SHA256)
 return PinnedRenderer::Open(path,PLAYHUB_TEXTURE_RENDERER_SHA256);
#else
 return {};
#endif
}
}
