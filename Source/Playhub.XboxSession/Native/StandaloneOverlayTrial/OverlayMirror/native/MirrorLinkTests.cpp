// SPDX-License-Identifier: GPL-3.0-only
#include "Win32MirrorBackend.h"
#include <iostream>
int main(){using namespace playhub::mirror;if(CreateExactBand16Broker(nullptr,nullptr)||CreateWin32MirrorBackend(nullptr))return 1;std::cout<<"{\"nullFactoryChecks\":2,\"passed\":true,\"windowsCreated\":0,\"hooksInstalled\":false}\n";return 0;}
