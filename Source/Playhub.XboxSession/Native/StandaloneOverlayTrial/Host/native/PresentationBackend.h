// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include <cstdint>
namespace playhub::presentation {
enum class Backend : std::uint32_t { OrdinaryTopmostBand1 = 1, ExactBand16 = 16 };
#if defined(PLAYHUB_SHELL_FREE_PRESENTATION) && PLAYHUB_SHELL_FREE_PRESENTATION
inline constexpr Backend Selected = Backend::OrdinaryTopmostBand1;
#else
inline constexpr Backend Selected = Backend::ExactBand16;
#endif
inline constexpr bool ShellFree = Selected == Backend::OrdinaryTopmostBand1;
inline constexpr std::uint32_t ExpectedBand = static_cast<std::uint32_t>(Selected);
}
