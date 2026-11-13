// SPDX-License-Identifier: GPL-3.0-only
#pragma once
namespace view::admission {
// The same ordered admission runs against retained native files and inert tests.
// Failure publishes no receipt; expensive module work starts only after file proof.
template<class Ops> bool File(Ops& o) {
 return o.BindSteamRoot() && o.OpenRetainedFiles() && o.TrustValveFiles() &&
        o.ReadImageContract() && o.PinDigest() && o.VerifyRetainedIdentity() && o.VerifyDigest();
}
template<class Ops> bool Loaded(Ops& o) {
 return o.BindLoadedIdentity() && o.MatchLoadedContract() &&
        o.VerifyRetainedIdentity() && o.VerifyDigest();
}
}
