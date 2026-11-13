// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include <cstdint>
namespace playhub::mirror {
// Admission is published before any foreign callback/setup. Cancellation before
// the first GUI Poll removes the reserved operation without calling its setup.
class DeferredBandAdmission {
 bool pending{},started{};
public:
 bool Reserve(){if(pending||started)return false;pending=true;return true;}
 bool TakeStart(){if(!pending)return false;pending=false;started=true;return true;}
 void Reset(){pending=started=false;}
 bool Pending()const{return pending;}
};
struct RetirementFacts {bool shellExitObserved{},unhooked{},operationFinishedOrRevoked{},barrierReturned{},absenceRead{},moduleAbsent{};std::int32_t inFlight{};std::uint64_t absentForMs{};};
inline bool MayReleaseCallback(const RetirementFacts& f){return f.shellExitObserved||(f.unhooked&&f.operationFinishedOrRevoked&&f.barrierReturned&&f.absenceRead&&f.moduleAbsent&&f.inFlight==0&&f.absentForMs>=100);}
}
