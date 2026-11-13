// Playhub Xbox Session. Distributed under GPL-3.0-only.
#pragma once
#include <cstdint>

// Present can return a non-failing status without displaying a frame. Keep
// submission diagnostics separate from Valve's menu and input ownership.
class PresentationState
{
public:
    template<class Report>
    void Observe(std::int32_t result, bool shown, Report report)
    {
        if (!sampled || result != lastResult) report(20, result);
        sampled = true;
        lastResult = result;
        SetPresented(shown && result == 0, report); // S_OK only, not SUCCEEDED.
    }

    template<class Report>
    void Hide(Report report) { SetPresented(false, report); }

    bool Presented() const { return presented; }

private:
    template<class Report>
    void SetPresented(bool value, Report report)
    {
        if (presented == value) return;
        presented = value;
        report(21, value ? 1 : 0);
    }

    std::int32_t lastResult = 0;
    bool sampled = false;
    bool presented = false;
};
