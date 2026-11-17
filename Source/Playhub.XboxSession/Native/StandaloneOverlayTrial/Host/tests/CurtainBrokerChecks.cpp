#include "BandAuthorization.h"
#include <iostream>
#include <stdexcept>
using namespace playhub;
struct Source final:curtain::ISourceAuthority {
 mutable curtain::Id tick=1000;mutable bool alive=true;bool slow=false;
 bool Current(const curtain::Authority&)const override {if(slow)tick=1600;return alive;}
 curtain::Id Now()const noexcept override{return tick;}
};
int main(){
 int checks=0;auto check=[&](bool ok){if(!ok)throw std::runtime_error("curtain "+std::to_string(checks+1));++checks;};
 auto source=std::make_shared<Source>();curtain::BandLease l;auto& a=l.authority;
 a.purpose=curtain::Purpose::FirstContentCover;a.generation=1;a.requestEpoch=1;
 a.requestId=std::string(32,'a');a.sourceReceipt=std::string(32,'b');a.ownerBuild=std::string(64,'c');
 a.appId=100;a.ownerPid=1;a.ownerBirth=101;a.gamePid=a.framePid=3;a.gameBirth=a.frameBirth=103;
 a.frameThread=33;a.gameWindow=300;a.steamPid=2;a.steamBirth=102;a.steamThread=22;a.steamWindow=200;a.session=1;
 a.gameRect={0,0,1920,1080};a.steamRect=a.gameRect;a.steamClass=L"Chrome_WidgetWin_1";
 a.gameClass=L"GameWindow";a.steamImage=L"C:\\Steam\\steamwebhelper.exe";a.gameImage=L"C:\\Games\\game.exe";
 l.source=source;l.issuedAt=900;l.expiresAt=1500;l.authorizationNonce.fill('d');l.authorizationNonce[64]=0;
 check(curtain::Valid(a));check(curtain::Valid(l,1000));
 mirror::BandAuthorization typed(l);check(typed.Current(1000));auto game=typed.Game();
 check(game.gamePid==3&&game.framePid==3&&game.gameBirth==103&&game.frameBirth==103&&game.gameWindow==300&&game.frameThread==33);
 check(game.sessionId==l.authorizationNonce&&game.expiresAt==1500&&game.gameRect.right==1920);
 auto reject=[&](auto change){auto v=l;change(v);check(!curtain::Valid(v,1000));};
 reject([](auto& v){v.authority.purpose=curtain::Purpose::None;});
 reject([](auto& v){v.authority.generation=0;});reject([](auto& v){v.authority.requestEpoch=0;});
 reject([](auto& v){v.authority.requestId="unknown";});reject([](auto& v){v.authority.sourceReceipt.clear();});
 reject([](auto& v){v.authority.ownerBuild.clear();});reject([](auto& v){v.authority.ownerPid=v.authority.gamePid;});
 reject([](auto& v){v.authority.steamPid=v.authority.gamePid;});reject([](auto& v){v.authority.steamWindow=v.authority.gameWindow;});
 reject([](auto& v){v.authority.gameBirth=0;});reject([](auto& v){v.authority.steamBirth=0;});
 reject([](auto& v){v.authority.frameThread=0;});reject([](auto& v){v.authority.session=0;});
 reject([](auto& v){v.authority.gameRect.right=v.authority.gameRect.left;});
 reject([](auto& v){v.authority.steamRect.right=40000;});reject([](auto& v){v.authority.steamClass.clear();});
 reject([](auto& v){v.authority.steamImage.clear();});reject([](auto& v){v.authority.gameClass.clear();});
 reject([](auto& v){v.authority.gameImage.clear();});reject([](auto& v){v.authority.frameBirth=104;});
 reject([](auto& v){v.authority.steamImage=L"relative.exe";});reject([](auto& v){v.authority.gameImage=L"relative.exe";});
 reject([](auto& v){v.authority.expectedFrameAumid=L"Package!Game";});
 reject([](auto& v){v.source.reset();});reject([](auto& v){v.issuedAt=1001;});
 reject([](auto& v){v.expiresAt=1000;});reject([](auto& v){v.expiresAt=6001;});
 reject([](auto& v){v.authorizationNonce[64]='x';});reject([](auto& v){v.authorizationNonce[1]='!';});
 reject([](auto& v){v.actualFrameAumid[512]=L'x';});
 check(typed.CurtainCurrent(1000));source->alive=false;check(!typed.Current(1000));check(!typed.CurtainCurrent(1000));source->alive=true;
 check(mirror::BandAuthorization(mirror::SourceLease{}).CurtainCurrent(1000));
 source->slow=true;check(!typed.Current(1000));source->slow=false;source->tick=1000;
 auto uwp=l;uwp.authority.framePid=4;uwp.authority.frameBirth=104;uwp.authority.expectedFrameAumid=L"Package!Game";
 wcscpy_s(uwp.actualFrameAumid.data(),uwp.actualFrameAumid.size(),L"Package!Game");
 check(curtain::Valid(uwp,1000));mirror::BandAuthorization uwpTyped(uwp);check(uwpTyped.Game().framePid==4&&uwpTyped.Game().expectedFrameAumid==uwp.actualFrameAumid);
 std::cout<<checks<<" typed curtain authorization checks PASS; no native APIs/windows/hooks/input\n";
}
