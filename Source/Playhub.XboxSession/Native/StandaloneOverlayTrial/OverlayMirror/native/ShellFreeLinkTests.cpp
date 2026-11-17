// SPDX-License-Identifier: GPL-3.0-only
#include "ShellFreeWin32MirrorBackend.h"
#include <cstdio>
int main(){
 auto backend=playhub::mirror::CreateShellFreeWin32MirrorBackend();
 if(!backend||backend->Window()||backend->Cookie()||backend->IsShown()||backend->HasCompositionSource())return 1;
 playhub::mirror::SourceLease invalid;
 // Must reject before opening a process or creating an HWND. No desktop is used.
 if(backend->CreateHidden(invalid)||backend->Window()||backend->Cookie())return 2;
 if(!backend->Hide()||!backend->DetachGpu()||!backend->RetireBand()||!backend->DestroyOwned())return 3;
 std::puts("PASS shell-free empty authority refusal and inert cleanup");return 0;
}
