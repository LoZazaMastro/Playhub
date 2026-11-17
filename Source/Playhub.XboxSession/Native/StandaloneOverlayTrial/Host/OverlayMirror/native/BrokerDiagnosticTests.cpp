// SPDX-License-Identifier: GPL-3.0-only
#include "BrokerDiagnostics.h"
#include <cstdio>
#include <cstring>
int main(){
 using namespace playhub::mirror;unsigned checks=0;
 auto check=[&](bool value){++checks;if(!value){std::printf("FAIL %u\n",checks);return false;}return true;};
 FirstBrokerFailure d;if(!check(d.Read().serial==0))return 1;
 d.Begin();if(!check(d.Read().serial==1&&d.Read().stage==BrokerStage::None))return 1;
 if(!check(!d.Reject(BrokerStage::InitialNoInput)))return 1;
 d.Reject(BrokerStage::HookInstall,true,5);auto a=d.Read();
 if(!check(a.stage==BrokerStage::InitialNoInput&&a.setupRefused&&!a.win32Valid&&a.win32==0))return 1;
 BrokerDiagnostic fields;fields.sharedRead=true;fields.hookInstalled=true;fields.received=1;fields.rejected=2;
 fields.refusalMask=65536;fields.permit=3;fields.bandInvoked=0;fields.bandReturned=0;fields.bandError=5;
 d.PollRefused(fields);a=d.Read();
 if(!check(a.stage==BrokerStage::InitialNoInput&&a.pollRefused&&a.sharedRead&&a.hookInstalled))return 1;
 if(!check(a.received==1&&a.rejected==2&&a.refusalMask==65536&&a.permit==3&&a.bandError==5))return 1;
 d.Begin();if(!check(d.Read().serial==2&&!d.Read().pollRefused&&!d.Read().sharedRead&&d.Read().win32==0))return 1;
 d.Reject(BrokerStage::HookInstall,true,5);a=d.Read();
 if(!check(a.setupRefused&&a.win32Valid&&a.win32==5))return 1;
 d.PollRefused({});a=d.Read();if(!check(a.stage==BrokerStage::HookInstall&&a.win32==5&&!a.sharedRead))return 1;
 d.Begin();d.PollRefused(fields);a=d.Read();
 if(!check(a.stage==BrokerStage::PollRefused&&!a.setupRefused&&!a.win32Valid&&a.pollRefused))return 1;
 for(unsigned i=0;i<=unsigned(BrokerStage::PollRefused);i++)if(!check(std::strcmp(BrokerStageName(BrokerStage(i)),"unknown")!=0))return 1;
 std::printf("PASS %u broker diagnostic checks; no OS, callbacks, UI, input or authority\n",checks);return 0;
}
