// SPDX-License-Identifier: GPL-3.0-only
#include "CallerImage.h"
#include "RendererAttestation.h"
#include "ForegroundViewPolicy.h"
#include <wintrust.h>
#include <softpub.h>
#include <wincrypt.h>
#include <bcrypt.h>
#include <array>
#include <cwchar>
#include <cstring>
namespace view {
static bool Readable(const void* p,size_t n){MEMORY_BASIC_INFORMATION m{};if(!p||!VirtualQuery(p,&m,sizeof(m))||m.State!=MEM_COMMIT||(m.Protect&(PAGE_NOACCESS|PAGE_GUARD)))return false;auto start=reinterpret_cast<std::uintptr_t>(p),end=reinterpret_cast<std::uintptr_t>(m.BaseAddress)+m.RegionSize;return start<=end&&n<=end-start;}
std::unique_ptr<ExecutableImage> ExecutableImage::Read(HMODULE module){
 if(!Readable(module,sizeof(IMAGE_DOS_HEADER)))return {};
 auto base=reinterpret_cast<std::uintptr_t>(module);auto dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(module);
 if(dos->e_magic!=IMAGE_DOS_SIGNATURE||dos->e_lfanew<sizeof(*dos)||dos->e_lfanew>1024*1024)return {};
 auto nt=reinterpret_cast<const IMAGE_NT_HEADERS64*>(base+dos->e_lfanew);
 if(!Readable(nt,sizeof(*nt))||nt->Signature!=IMAGE_NT_SIGNATURE||nt->FileHeader.Machine!=IMAGE_FILE_MACHINE_AMD64||nt->OptionalHeader.Magic!=IMAGE_NT_OPTIONAL_HDR64_MAGIC||nt->FileHeader.NumberOfSections==0||nt->FileHeader.NumberOfSections>96||nt->OptionalHeader.SizeOfImage<4096)return {};
 auto sections=IMAGE_FIRST_SECTION(nt);if(!Readable(sections,sizeof(*sections)*nt->FileHeader.NumberOfSections))return {};
 auto result=std::unique_ptr<ExecutableImage>(new ExecutableImage());result->module=module;
 constexpr BYTE textName[8]{'.','t','e','x','t',0,0,0};
 for(unsigned i=0;i<nt->FileHeader.NumberOfSections;i++)if((sections[i].Characteristics&IMAGE_SCN_MEM_EXECUTE)&&memcmp(sections[i].Name,textName,8)==0){auto at=sections[i].VirtualAddress,size=sections[i].Misc.VirtualSize;if(!size||at>=nt->OptionalHeader.SizeOfImage||size>nt->OptionalHeader.SizeOfImage-at)return {};result->ranges.push_back({base+at,size});}
 return result->ranges.empty()?nullptr:std::move(result);
}
bool ExecutableImage::Contains(const void* address)const noexcept{
 MEMORY_BASIC_INFORMATION memory{};if(!address||!VirtualQuery(address,&memory,sizeof(memory))||memory.AllocationBase!=module||memory.Type!=MEM_IMAGE||memory.State!=MEM_COMMIT||(memory.Protect&(PAGE_NOACCESS|PAGE_GUARD))||!(memory.Protect&(PAGE_EXECUTE|PAGE_EXECUTE_READ|PAGE_EXECUTE_READWRITE|PAGE_EXECUTE_WRITECOPY)))return false;
 for(const auto& range:ranges)if(view::Contains(range.first,range.second,reinterpret_cast<std::uintptr_t>(address)))return true;return false;
}
static std::wstring FinalPath(HANDLE file){wchar_t p[32768]{};DWORD n=GetFinalPathNameByHandleW(file,p,32768,FILE_NAME_NORMALIZED|VOLUME_NAME_DOS);if(!n||n>=32768)return {};std::wstring result(p,n);if(result.rfind(L"\\\\?\\",0)==0)result.erase(0,4);return result;}
static bool ShaMatches(HANDLE file,const char* expected){
 if(!expected||strlen(expected)!=64)return false;for(unsigned i=0;i<64;i++)if(!((expected[i]>='0'&&expected[i]<='9')||(expected[i]>='a'&&expected[i]<='f')||(expected[i]>='A'&&expected[i]<='F')))return false;
 BCRYPT_ALG_HANDLE alg{};BCRYPT_HASH_HANDLE hash{};if(BCryptOpenAlgorithmProvider(&alg,BCRYPT_SHA256_ALGORITHM,nullptr,0)<0)return false;
 bool valid=false;if(BCryptCreateHash(alg,&hash,nullptr,0,nullptr,0,0)>=0){std::array<BYTE,65536> buffer{};DWORD n{};bool ok=true;LARGE_INTEGER start{};ok=SetFilePointerEx(file,start,nullptr,FILE_BEGIN)!=FALSE;while(ok){if(!ReadFile(file,buffer.data(),DWORD(buffer.size()),&n,nullptr)){ok=false;break;}if(!n)break;if(BCryptHashData(hash,buffer.data(),n,0)<0)ok=false;}BYTE bytes[32]{};if(ok&&BCryptFinishHash(hash,bytes,32,0)>=0){char text[65]{};constexpr char digits[]="0123456789abcdef";for(unsigned i=0;i<32;i++){text[2*i]=digits[bytes[i]>>4];text[2*i+1]=digits[bytes[i]&15];}valid=_stricmp(text,expected)==0;}BCryptDestroyHash(hash);}BCryptCloseAlgorithmProvider(alg,0);return valid;
}
static bool ValveSigner(const wchar_t* path,HANDLE file){
 WINTRUST_FILE_INFO item{sizeof(item)};item.pcwszFilePath=path;item.hFile=file;WINTRUST_DATA data{sizeof(data)};data.dwUIChoice=WTD_UI_NONE;data.fdwRevocationChecks=WTD_REVOKE_NONE;data.dwUnionChoice=WTD_CHOICE_FILE;data.pFile=&item;data.dwStateAction=WTD_STATEACTION_VERIFY;data.dwProvFlags=WTD_CACHE_ONLY_URL_RETRIEVAL|WTD_REVOCATION_CHECK_NONE;GUID action=WINTRUST_ACTION_GENERIC_VERIFY_V2;bool valid=false;
 if(WinVerifyTrust(nullptr,&action,&data)==ERROR_SUCCESS){auto provider=WTHelperProvDataFromStateData(data.hWVTStateData);auto signer=provider?WTHelperGetProvSignerFromChain(provider,0,FALSE,0):nullptr;if(signer&&signer->csCertChain&&signer->pasCertChain[0].pCert){wchar_t name[128]{},org[128]{};auto cert=signer->pasCertChain[0].pCert;DWORD a=CertGetNameStringW(cert,CERT_NAME_SIMPLE_DISPLAY_TYPE,0,nullptr,name,128),b=CertGetNameStringW(cert,CERT_NAME_ATTR_TYPE,0,const_cast<char*>(szOID_ORGANIZATION_NAME),org,128);valid=a>1&&a<=128&&b>1&&b<=128&&wcscmp(name,L"Valve Corp.")==0&&wcscmp(org,L"Valve Corp.")==0;}}
 data.dwStateAction=WTD_STATEACTION_CLOSE;WinVerifyTrust(nullptr,&action,&data);return valid;
}
static bool SamePe(HANDLE file,HMODULE module){
 LARGE_INTEGER at{};DWORD count{};IMAGE_DOS_HEADER dos{};if(!SetFilePointerEx(file,at,nullptr,FILE_BEGIN)||!ReadFile(file,&dos,sizeof(dos),&count,nullptr)||count!=sizeof(dos)||dos.e_magic!=IMAGE_DOS_SIGNATURE||dos.e_lfanew<sizeof(dos)||dos.e_lfanew>1024*1024)return false;
 auto memoryDos=reinterpret_cast<const IMAGE_DOS_HEADER*>(module);if(!Readable(memoryDos,sizeof(*memoryDos))||memoryDos->e_magic!=dos.e_magic||memoryDos->e_lfanew!=dos.e_lfanew)return false;
 IMAGE_NT_HEADERS64 disk{};at.QuadPart=dos.e_lfanew;if(!SetFilePointerEx(file,at,nullptr,FILE_BEGIN)||!ReadFile(file,&disk,sizeof(disk),&count,nullptr)||count!=sizeof(disk))return false;auto memory=reinterpret_cast<const IMAGE_NT_HEADERS64*>(reinterpret_cast<const BYTE*>(module)+dos.e_lfanew);
 return Readable(memory,sizeof(*memory))&&disk.Signature==IMAGE_NT_SIGNATURE&&disk.FileHeader.Machine==IMAGE_FILE_MACHINE_AMD64&&disk.OptionalHeader.Magic==IMAGE_NT_OPTIONAL_HDR64_MAGIC&&memory->Signature==disk.Signature&&memory->FileHeader.TimeDateStamp==disk.FileHeader.TimeDateStamp&&memory->FileHeader.NumberOfSections==disk.FileHeader.NumberOfSections&&memory->OptionalHeader.SizeOfImage==disk.OptionalHeader.SizeOfImage&&memory->OptionalHeader.SizeOfHeaders==disk.OptionalHeader.SizeOfHeaders;
}
PinnedRenderer::~PinnedRenderer(){if(module)FreeLibrary(module);if(file!=INVALID_HANDLE_VALUE)CloseHandle(file);}
std::shared_ptr<const PinnedRenderer> PinnedRenderer::Open(const wchar_t* path,const char* digest){
 return OpenInternal(path,digest,{});
}
std::shared_ptr<const PinnedRenderer> PinnedRenderer::OpenAttestedSteamRenderer(const wchar_t* path){
 auto proof=RendererAttestation::OpenFile(path);if(!proof)return {};
 return OpenInternal(path,proof->Digest(),proof);
}
std::shared_ptr<const PinnedRenderer> PinnedRenderer::OpenInternal(const wchar_t* path,const char* digest,std::shared_ptr<const RendererAttestation> proof){
 if(!path||!*path)return {};const wchar_t* name=wcsrchr(path,L'\\');if(!name||_wcsicmp(name+1,L"GameOverlayRenderer64.dll"))return {};
 auto value=std::shared_ptr<PinnedRenderer>(new PinnedRenderer());value->file=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);if(value->file==INVALID_HANDLE_VALUE)return {};
 FILE_ATTRIBUTE_TAG_INFO attr{};LARGE_INTEGER size{};if(!GetFileInformationByHandleEx(value->file,FileAttributeTagInfo,&attr,sizeof(attr))||(attr.FileAttributes&(FILE_ATTRIBUTE_DIRECTORY|FILE_ATTRIBUTE_REPARSE_POINT))||!GetFileSizeEx(value->file,&size)||size.QuadPart<4096||size.QuadPart>128*1024*1024||!ShaMatches(value->file,digest)||!ValveSigner(path,value->file))return {};
 auto actual=FinalPath(value->file);if(actual.empty()||_wcsicmp(actual.c_str(),path)||!GetModuleHandleExW(0,path,&value->module))return {};
 wchar_t loaded[32768]{};DWORD n=GetModuleFileNameW(value->module,loaded,32768);if(!n||n>=32768)return {};HANDLE loadedFile=CreateFileW(loaded,GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);if(loadedFile==INVALID_HANDLE_VALUE)return {};auto loadedPath=FinalPath(loadedFile);CloseHandle(loadedFile);if(_wcsicmp(loadedPath.c_str(),actual.c_str())||!SamePe(value->file,value->module))return {};
 value->executable=ExecutableImage::Read(value->module);if(!value->executable||(proof&&!proof->VerifyLoaded(value->module)))return {};
 value->attestation=std::move(proof);return value;
}
}
