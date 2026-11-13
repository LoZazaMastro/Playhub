// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include <cstdint>
namespace view {
struct NonceIdentity {std::uintptr_t window{},cookie{},nonce{};std::uint32_t thread{};};
inline bool SameNonceIdentity(const NonceIdentity& held,const NonceIdentity& now,bool sameProcess){return sameProcess&&held.window&&held.cookie&&held.nonce&&held.thread
 &&held.window==now.window&&held.cookie==now.cookie&&held.nonce==now.nonce&&held.thread==now.thread;}
// Shared by immutable old/new view leases on renewal. Cleanup runs only after
// the last detour/active lease reference and still checks the exact own token.
class NonceOwner final {
 const NonceIdentity identity;void(*release)(const NonceIdentity&) noexcept;
public:
 NonceOwner(NonceIdentity value,void(*callback)(const NonceIdentity&) noexcept):identity(value),release(callback){}
 NonceOwner(const NonceOwner&)=delete;NonceOwner& operator=(const NonceOwner&)=delete;
 const NonceIdentity& Identity()const noexcept{return identity;}
 ~NonceOwner(){if(release)release(identity);}
};
}
