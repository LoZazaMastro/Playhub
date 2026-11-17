// Playhub Xbox Session. Distributed under GPL-3.0-only.
#include "PresentationState.h"
#include <windows.h>
#include <dxgi.h>
#include <iostream>
#include <utility>
#include <vector>

int main()
{
    using Event = std::pair<int, std::int32_t>;
    std::vector<Event> events;
    const auto report = [&events](int kind, std::int32_t detail) { events.emplace_back(kind, detail); };
    PresentationState state;
    int checks = 0;
    const auto expect = [&checks](bool condition, const char* message) {
        ++checks;
        if (!condition) std::cerr << "FAILED: " << message << '\n';
        return condition;
    };
    if (!expect(!state.Presented(), "No frame was submitted initially")) return 1;
    state.Observe(S_OK, false, report);
    if (!expect(events == std::vector<Event>{{20, S_OK}} && !state.Presented(), "Hidden startup S_OK is not presented")) return 2;
    events.clear();
    state.Observe(S_OK, true, report);
    if (!expect(events == std::vector<Event>{{21, 1}} && state.Presented(), "Shown S_OK changes only submission state")) return 3;
    events.clear();
    for (int frame = 0; frame < 1000; ++frame) state.Observe(S_OK, true, report);
    if (!expect(events.empty(), "A steady presentation emits no per-frame log")) return 4;
    state.Observe(DXGI_STATUS_OCCLUDED, true, report);
    if (!expect(SUCCEEDED(DXGI_STATUS_OCCLUDED) && !state.Presented()
        && events == std::vector<Event>{{20, DXGI_STATUS_OCCLUDED}, {21, 0}}, "Occluded success status never means presented")) return 5;
    events.clear();
    for (int frame = 0; frame < 1000; ++frame) state.Observe(DXGI_STATUS_OCCLUDED, true, report);
    if (!expect(events.empty(), "A steady occlusion emits no per-frame log")) return 6;
    state.Observe(DXGI_ERROR_DEVICE_REMOVED, true, report);
    if (!expect(!state.Presented() && events == std::vector<Event>{{20, static_cast<std::int32_t>(DXGI_ERROR_DEVICE_REMOVED)}},
        "Device failure preserves its exact HRESULT without claiming a frame")) return 7;
    events.clear();
    state.Observe(S_OK, true, report);
    if (!expect(state.Presented() && events == std::vector<Event>{{20, S_OK}, {21, 1}}, "Recovery requires a new S_OK")) return 8;
    events.clear();
    state.Hide(report);
    if (!expect(!state.Presented() && events == std::vector<Event>{{21, 0}}, "Hide revokes the last submitted frame")) return 9;
    events.clear();
    state.Hide(report);
    state.Observe(S_OK, false, report);
    if (!expect(events.empty() && !state.Presented(), "Repeated hide and hidden Present cannot restore presented state")) return 10;
    state.Observe(S_FALSE, true, report);
    if (!expect(!state.Presented() && events == std::vector<Event>{{20, S_FALSE}}, "Other non-failing status is not S_OK")) return 11;
    events.clear();
    state.Observe(DXGI_STATUS_MODE_CHANGED, true, report);
    if (!expect(!state.Presented() && events == std::vector<Event>{{20, DXGI_STATUS_MODE_CHANGED}}, "Mode transition stays unconfirmed")) return 12;
    events.clear();
    state.Observe(S_OK, true, report);
    events.clear();
    state.Observe(E_INVALIDARG, true, report);
    if (!expect(!state.Presented() && events == std::vector<Event>{{20, static_cast<std::int32_t>(E_INVALIDARG)}, {21, 0}},
        "A draw failure also revokes the previous submitted frame")) return 13;
    std::cout << checks << " presentation checks passed\n";
    return 0;
}
