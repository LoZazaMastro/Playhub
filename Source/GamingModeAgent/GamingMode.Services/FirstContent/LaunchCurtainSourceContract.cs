// SPDX-License-Identifier: GPL-3.0-only
using Playhub.FirstContent;
namespace GamingMode.Services;
// Private service objects, never serialized or supplied by an endpoint.
internal interface ILaunchCurtainSource:IDisposable
{
 CurtainVisualAuthority Visual{get;}
 ContentTarget CaptureTarget{get;}
 bool Current{get;}
 Task<bool> RefreshAssociationAsync(CancellationToken cancellation);
}
