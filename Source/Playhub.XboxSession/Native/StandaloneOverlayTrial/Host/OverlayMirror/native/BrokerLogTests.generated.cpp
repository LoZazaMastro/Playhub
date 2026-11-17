// Generated from the actual candidate EventLog::WriteBroker; OS calls replaced only in this test.
#include <windows.h>
#include "BrokerDiagnostics.h"
#include <cstdio>
#include <string>
namespace m=playhub::mirror;
static std::string output;static unsigned writes;
static ULONGLONG FakeTick(){return 42;}
static BOOL FakeWrite(HANDLE,const void* p,DWORD n,DWORD* written,LPOVERLAPPED){output.assign(static_cast<const char*>(p),n);*written=n;++writes;return TRUE;}
#define GetTickCount64 FakeTick
#define WriteFile FakeWrite
struct ActualLog { HANDLE file=reinterpret_cast<HANDLE>(1);
 void WriteQuietInput(const m::BrokerDiagnostic& d,std::uint64_t generation){
  if(file==INVALID_HANDLE_VALUE||!d.noInputFailures)return;char text[1024]{};const auto& f=d.blockedInput;
  int size=snprintf(text,sizeof(text),"{\"stage\":\"band-broker-no-input\",\"bootMs\":%llu,\"serial\":%llu,\"generation\":%llu,\"setupStage\":\"%s\",\"inputFailures\":%u,\"deferrals\":%u,\"held\":%s,\"firstHeldVk\":%u,\"cursorAttempted\":%s,\"cursorRead\":%s,\"cursorReadFailed\":%s,\"initialX\":%d,\"initialY\":%d,\"currentX\":%d,\"currentY\":%d,\"cursorMismatch\":%s,\"hookInstalled\":false,\"productionAuthority\":false}\n",
   static_cast<unsigned long long>(GetTickCount64()),static_cast<unsigned long long>(d.serial),static_cast<unsigned long long>(generation),m::BrokerStageName(d.stage),d.noInputFailures,d.quietDeferrals,f.held?"true":"false",f.firstHeldVk,f.cursorAttempted?"true":"false",f.cursorRead?"true":"false",f.cursorAttempted&&!f.cursorRead?"true":"false",f.initialX,f.initialY,f.currentX,f.currentY,f.cursorRead&&(f.initialX!=f.currentX||f.initialY!=f.currentY)?"true":"false");
  if(size>0&&size<int(sizeof(text))){DWORD written{};WriteFile(file,text,DWORD(size),&written,nullptr);}
 }

 void WriteBroker(const m::BrokerDiagnostic& d,std::uint64_t generation){
  if(file==INVALID_HANDLE_VALUE||!d.pollRefused)return;char text[1024]{};
  int size=snprintf(text,sizeof(text),"{\"stage\":\"band-broker-refused\",\"bootMs\":%llu,\"serial\":%llu,\"generation\":%llu,\"firstSetupStage\":\"%s\",\"setupRefused\":%s,\"win32Valid\":%s,\"win32\":%u,\"pollRefused\":true,\"sharedRead\":%s,\"hookInstalled\":%s,\"received\":%d,\"rejected\":%d,\"refusalMask\":%d,\"permit\":%d,\"bandInvoked\":%d,\"bandReturned\":%d,\"bandError\":%u}\n",
   static_cast<unsigned long long>(GetTickCount64()),static_cast<unsigned long long>(d.serial),static_cast<unsigned long long>(generation),m::BrokerStageName(d.stage),d.setupRefused?"true":"false",d.win32Valid?"true":"false",d.win32,d.sharedRead?"true":"false",d.hookInstalled?"true":"false",d.received,d.rejected,d.refusalMask,d.permit,d.bandInvoked,d.bandReturned,d.bandError);
  if(size>0&&size<int(sizeof(text))){DWORD written{};WriteFile(file,text,DWORD(size),&written,nullptr);}
 }

};
int main(){unsigned n=0;auto check=[&](bool b){++n;if(!b){std::printf("FAIL log %u\n",n);return false;}return true;};
 ActualLog quietLog;m::FirstBrokerFailure quiet;quiet.Begin();quietLog.WriteQuietInput(quiet.Read(),7);if(!check(writes==0))return 1;
 m::NoInputFacts held;held.held=true;held.firstHeldVk=195;quiet.InputBlocked(held);quiet.DeferInput();quiet.Reject(m::BrokerStage::InitialNoInput);quietLog.WriteQuietInput(quiet.Read(),7);
 for(const char* token:{"\"firstHeldVk\":195","\"held\":true","\"cursorAttempted\":false","\"cursorReadFailed\":false","\"inputFailures\":1","\"deferrals\":1","\"hookInstalled\":false"})if(!check(output.find(token)!=std::string::npos))return 1;
 quiet.RestartAttempt();m::NoInputFacts failed;failed.cursorAttempted=true;quiet.InputBlocked(failed);quietLog.WriteQuietInput(quiet.Read(),7);if(!check(output.find("\"cursorReadFailed\":true")!=std::string::npos))return 1;
 quiet.RestartAttempt();m::NoInputFacts moved;moved.cursorAttempted=moved.cursorRead=true;moved.initialX=10;moved.initialY=20;moved.currentX=11;moved.currentY=20;quiet.InputBlocked(moved);quietLog.WriteQuietInput(quiet.Read(),7);if(!check(output.find("\"cursorMismatch\":true")!=std::string::npos))return 1;
 writes=0;output.clear();
 ActualLog log;m::FirstBrokerFailure first;first.Begin();log.WriteBroker(first.Read(),17);if(!check(writes==0))return 1;
 first.Reject(m::BrokerStage::HookInstall,true,5);first.PollRefused({});log.WriteBroker(first.Read(),17);
 if(!check(writes==1))return 1;
 for(const char* token:{"\"generation\":17","\"firstSetupStage\":\"hook-install\"","\"win32Valid\":true","\"win32\":5","\"sharedRead\":false"})if(!check(output.find(token)!=std::string::npos))return 1;
 first.Begin();first.Reject(m::BrokerStage::InitialSource);m::BrokerDiagnostic fields;fields.sharedRead=true;fields.refusalMask=8192;fields.received=0;fields.rejected=1;fields.permit=3;first.PollRefused(fields);log.WriteBroker(first.Read(),18);
 for(const char* token:{"\"generation\":18","\"firstSetupStage\":\"initial-source\"","\"win32Valid\":false","\"win32\":0","\"refusalMask\":8192","\"rejected\":1"})if(!check(output.find(token)!=std::string::npos))return 1;
 log.file=INVALID_HANDLE_VALUE;log.WriteBroker(first.Read(),18);if(!check(writes==2))return 1;
 std::printf("PASS %u actual EventLog method checks with fake file/tick; no OS or callbacks\n",n);return 0;}
