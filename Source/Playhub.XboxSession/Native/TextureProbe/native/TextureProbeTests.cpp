// SPDX-License-Identifier: GPL-3.0-only
#include "TextureProbePolicy.h"
#include <iostream>
int main(){unsigned n=0;auto check=[&](bool value){++n;if(!value){std::cerr<<"FAIL texture policy "<<n<<"\n";return false;}return true;};
 if(!check(texture::MayBegin(true,true,true,true)))return 1;for(unsigned bad=0;bad<4;bad++){bool f[]{true,true,true,true};f[bad]=false;if(!check(!texture::MayBegin(f[0],f[1],f[2],f[3])))return 1;}
 if(!check(texture::MayPost(true,true,true,true,true)))return 1;for(unsigned bad=0;bad<5;bad++){bool f[]{true,true,true,true,true};f[bad]=false;if(!check(!texture::MayPost(f[0],f[1],f[2],f[3],f[4])))return 1;}
 if(!check(texture::ShouldStop(false,true,false,100,50)==texture::Stop::None)||!check(texture::ShouldStop(true,false,true,100,200)==texture::Stop::GameEnded)||!check(texture::ShouldStop(true,true,false,100,200)==texture::Stop::SourceLost)||!check(texture::ShouldStop(true,true,true,200,200)==texture::Stop::Expired)||!check(texture::ShouldStop(true,true,true,199,200)==texture::Stop::None))return 1;
 if(!check(texture::ActualScope(true,false,true)==1)||!check(texture::ActualScope(false,true,true)==2)||!check(texture::ActualScope(false,false,true)==0)||!check(texture::ActualScope(true,false,false)==0))return 1;
 if(!check(texture::MayAdmitProbe(0,false,false,false))||!check(texture::MayAdmitProbe(1,false,false,false))||!check(!texture::MayAdmitProbe(2,false,false,false))||!check(!texture::MayAdmitProbe(1,true,false,false))||!check(!texture::MayAdmitProbe(1,false,true,false))||!check(!texture::MayAdmitProbe(1,false,false,true)))return 1;
 bool refused= !texture::MayAdmitProbe(1,true,false,false);if(!check(refused&&texture::ShouldStop(true,true,true,201,200)==texture::Stop::Expired))return 1;
 int shifted=0,tabbed=0,guards=0;bool good=texture::PostDown([&](){return ++guards==1;},[&](){++shifted;return true;},[&](){++tabbed;return true;});if(!check(!good&&shifted==1&&tabbed==0))return 1;
 shifted=tabbed=0;good=texture::PostDown([](){return false;},[&](){++shifted;return true;},[&](){++tabbed;return true;});if(!check(!good&&shifted==0&&tabbed==0))return 1;
 shifted=tabbed=0;good=texture::PostDown([](){return true;},[&](){++shifted;return true;},[&](){++tabbed;return true;});if(!check(good&&shifted==1&&tabbed==1))return 1;
 shifted=tabbed=0;good=texture::PostDown([](){return true;},[&](){++shifted;return false;},[&](){++tabbed;return true;});if(!check(!good&&shifted==1&&tabbed==0))return 1;
 std::cout<<"{\"checks\":"<<n<<",\"passed\":true,\"uiCreated\":false,\"inputSent\":0}\n";return 0;}
