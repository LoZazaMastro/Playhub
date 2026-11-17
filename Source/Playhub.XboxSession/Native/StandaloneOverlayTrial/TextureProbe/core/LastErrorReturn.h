// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include <windows.h>
namespace view {
// Teardown may remove an own-window property or close retained handles. Finish
// it before restoring the original getter's LastError; no later Win32 teardown.
template<class Answer,class Scope,class Pin>
Answer FinishReturn(Answer answer,DWORD error,Scope& scope,Pin& pin){scope.reset();pin.reset();SetLastError(error);return answer;}
}
