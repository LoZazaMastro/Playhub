cmake_minimum_required(VERSION 3.20)
project(PlayhubAppContainerFixture LANGUAGES CXX)

add_executable(PlayhubAppContainerFixture AppContainerChild.cpp)
target_compile_features(PlayhubAppContainerFixture PRIVATE cxx_std_17)
target_link_libraries(PlayhubAppContainerFixture PRIVATE advapi32)
set_property(TARGET PlayhubAppContainerFixture PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
