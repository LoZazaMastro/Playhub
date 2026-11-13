cmake_minimum_required(VERSION 3.20)
project(WsgmUwpBridge LANGUAGES C CXX)

# One injectable target. The spike also built an input probe DLL for attended diagnostics; a second
# thing that gets loaded into somebody's game is surface this feature does not need.

# MinHook is compiled from the vendored tree in external/minhook, which eng/build-uwp-bridge.ps1
# passes as MINHOOK_SOURCE.
if(NOT EXISTS "${MINHOOK_SOURCE}/include/MinHook.h")
  message(FATAL_ERROR "Set MINHOOK_SOURCE to the repository's external/minhook")
endif()
add_library(WsgmUwpBridge SHARED Bridge.cpp InputActivation.cpp
  "${MINHOOK_SOURCE}/src/buffer.c"
  "${MINHOOK_SOURCE}/src/hook.c"
  "${MINHOOK_SOURCE}/src/trampoline.c"
  "${MINHOOK_SOURCE}/src/hde/hde64.c")
target_include_directories(WsgmUwpBridge PRIVATE "${MINHOOK_SOURCE}/include")
target_compile_features(WsgmUwpBridge PRIVATE cxx_std_17)
target_compile_definitions(WsgmUwpBridge PRIVATE WIN32_LEAN_AND_MEAN NOMINMAX)
target_link_libraries(WsgmUwpBridge PRIVATE runtimeobject)
set_property(TARGET WsgmUwpBridge PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
target_compile_options(WsgmUwpBridge PRIVATE $<$<CXX_COMPILER_ID:MSVC>:/guard:cf>)
target_link_options(WsgmUwpBridge PRIVATE $<$<CXX_COMPILER_ID:MSVC>:/guard:cf /DYNAMICBASE /NXCOMPAT>)
