// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using Playhub.Shared;

// Runs only on the existing layout worker and retained PDO. Native240 is not
// Shown; this distinct lease additionally requires actual stop/neutral/769 ACK.
internal static class SteamNativeVisibleInputCycle
{
    private sealed class PublicationOwner { internal HiddenObservation? Hidden; }
    private sealed record Publication(PublicationOwner Owner,SteamMirrorNativeVisibleDto Native,string State,
        bool Neutral,bool Menu,bool Automatic,long At,long ClosedUntil,
        Func<bool> Current,Func<bool> NeutralHeld,Func<SteamMirrorNativeVisibleDto?> Read,Func<long> Clock);
    private static Publication? published;
    // Passive status reads can observe a real source-hidden packet while this
    // worker is blocked in its own automatic0 launcher. Preserve its original
    // time/owner only as history, never as current UI/input/closed authority.
    private sealed record HiddenObservation(SteamMirrorNativeVisibleDto Native,long At);
    private static ulong N(string text)=>ulong.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out var n)?n:0;
    private static string Text(long n)=>n.ToString(CultureInfo.InvariantCulture);
    internal static bool SameIdentity(SteamMirrorNativeVisibleDto a,SteamMirrorNativeVisibleDto b)
        =>a.SessionId==b.SessionId&&a.Generation==b.Generation&&a.GameId==b.GameId&&a.GuideEpoch==b.GuideEpoch
        &&a.OwnerPid==b.OwnerPid&&a.OwnerBirth==b.OwnerBirth&&a.GamePid==b.GamePid&&a.GameBirth==b.GameBirth
        &&a.SourceHwnd==b.SourceHwnd&&a.SourceCookie==b.SourceCookie;
    internal static bool Pending(Func<SteamMirrorNativeVisibleDto?> read,ulong gameId,uint gamePid,long gameBirth,
        Func<long> clock,out SteamMirrorNativeVisibleDto? admission)
    {
        admission=read();return SteamMirrorWireProtocol.PositiveNativeVisible(admission,clock())&&admission is {} n
            &&N(n.GameId)==gameId&&n.GamePid==gamePid&&N(n.GameBirth)==(ulong)gameBirth;
    }
    internal static bool MatchesLease(SteamMirrorNativeVisibleDto? n,SteamInputOrderedIssuer.NativeVisibleInputLease lease,long now)
        =>SteamMirrorWireProtocol.PositiveNativeVisible(n,now)&&n is not null&&n.SessionId==lease.SessionId
        &&N(n.Generation)==lease.Generation&&N(n.GameId)==lease.GameId&&N(n.GuideEpoch)==lease.GuideEpoch
        &&N(n.SourceHwnd)==lease.SourceHwnd&&N(n.SourceCookie)==lease.SourceCookie
        &&n.OwnerPid==(uint)lease.Session.CompanionPid&&N(n.OwnerBirth)==(ulong)lease.Session.CompanionBirth
        &&n.GamePid==lease.Session.GamePid&&N(n.GameBirth)==(ulong)lease.Session.GameBirth;

    // A cross-thread immutable report, revalidated against the actual native
    // getter. Never invokes the worker-owned issuer from a pipe/fence thread.
    internal static SteamNativeVisibleInputDto? ReadStatus()
    {
        var p=Volatile.Read(ref published);if(p is null||!p.Current())return null;
        long now=p.Clock();var n=p.Read();
        if(n is null||!SameIdentity(p.Native,n)||!SteamMirrorWireProtocol.NativeVisibleValid(n,now))return null;
        bool hidden=n.Phase==2;
        if(hidden&&p.Current())
        {
            long after=p.Clock();
            if(after>=now&&SteamMirrorWireProtocol.NativeVisibleValid(n,after)
                &&ReferenceEquals(Volatile.Read(ref published),p))
            {
                var prior=Volatile.Read(ref p.Owner.Hidden);
                // Each cycle owns its own slot. A delayed old-generation getter
                // cannot overwrite a newer terminal, or refresh the same one.
                if(prior is null||N(n.Generation)>N(prior.Native.Generation)&&N(n.GuideEpoch)>N(prior.Native.GuideEpoch))
                    Interlocked.CompareExchange(ref p.Owner.Hidden,new(n,now),prior);
            }
        }
        bool neutral=p.Neutral&&p.NeutralHeld();
        bool menu=!hidden&&p.Menu&&neutral&&p.State=="menu"&&now>=p.At&&now-p.At<=250;
        long end=hidden?Math.Min((long)N(n.ObservedAt)+250,p.ClosedUntil)
            :Math.Min((long)N(n.ExpiresAt),Math.Min((long)N(n.PhysicalExpiresAt),p.At+250));
        if(end<=now||!p.Current()||!ReferenceEquals(Volatile.Read(ref published),p))return null;
        var last=p.Read();now=p.Clock();
        if(last is null||!SameIdentity(n,last)||last.Phase!=n.Phase||!SteamMirrorWireProtocol.NativeVisibleValid(last,now)
            ||!p.Current()||menu&&!p.NeutralHeld()||!ReferenceEquals(Volatile.Read(ref published),p)||end<=now)return null;
        long completed=p.Clock();
        if(completed<now||completed>=end||N(n.ReadAt)>(ulong)completed||N(n.ObservedAt)>(ulong)completed
            ||(ulong)completed-N(n.ReadAt)>250||(ulong)completed-N(n.ObservedAt)>250
            ||!ReferenceEquals(Volatile.Read(ref published),p))return null;
        // Use the earlier real native read/expiry, never manufacture freshness.
        return new SteamNativeVisibleInputDto{Protocol=1,SessionId=n.SessionId,Generation=n.Generation,GameId=n.GameId,
            GuideEpoch=n.GuideEpoch,OwnerPid=n.OwnerPid,OwnerBirth=n.OwnerBirth,GamePid=n.GamePid,GameBirth=n.GameBirth,
            SourceHwnd=n.SourceHwnd,SourceCookie=n.SourceCookie,ObservedAt=n.ObservedAt,ReadAt=n.ReadAt,ExpiresAt=Text(end),
            State=hidden&&p.Automatic?"closed":menu?"menu":p.State=="menu"?"uncertain":p.State,NeutralHeld=neutral,
            MenuLayoutReady=menu,InputReady=menu,AutomaticSettled=p.Automatic,SourceHidden=hidden};
    }

    // An own-mirror Preparing stop can predate original-source visibility.
    // Reuse that exact stopped reader/PDO after own0 ACK, before any new raw
    // reader/bootstrap. No positive original source means no new authority.
    internal static bool? ResumeStopped(long epoch,SteamRawInputRelayTrial.SessionBinding session,
        SteamRawInputRelayTrial.ReaderOutcome reader,SteamInputOrderedIssuer issuer,Func<bool> current,Func<bool> neutralHeld,
        Func<SteamMirrorNativeVisibleDto?> read,Func<long> clock,Action<int> wait,CancellationToken cancellation,
        Func<bool>? disconnected=null)
    {
        if(!Pending(read,session.GameId,session.GamePid,session.GameBirth,clock,out var admission)||admission is null)return null;
        // Once a current original UI owns this handoff, a refusal must abort;
        // it must never fall through and bootstrap a raw game reader.
        if(!reader.CanResume||reader.EndReason!=RelayExit.OverlayOpened||reader.Epoch!=epoch
            ||!neutralHeld()||!current()||!issuer.AutomaticSettled||disconnected?.Invoke()==true)return false;
        return Run(admission,epoch,session,reader,issuer,current,neutralHeld,read,clock,wait,cancellation,disconnected);
    }

    // A past, authenticated source-hidden observation permits retiring only
    // that old input lease. The successor grants NOTHING until its own fresh
    // physical proof, new lease and new769 ACK pass on the SAME stopped PDO.
    internal static bool SuccessorAfterHidden(SteamMirrorNativeVisibleDto hidden,long hiddenAt,
        SteamMirrorNativeVisibleDto? next,long now)=>hiddenAt>=0&&now>=hiddenAt&&hidden.Phase==2
        &&SteamMirrorWireProtocol.NativeVisibleValid(hidden,hiddenAt)
        &&SteamMirrorWireProtocol.PositiveNativeVisible(next,now)&&next is not null
        &&hidden.SessionId==next.SessionId&&hidden.GameId==next.GameId&&hidden.OwnerPid==next.OwnerPid&&hidden.OwnerBirth==next.OwnerBirth
        &&hidden.GamePid==next.GamePid&&hidden.GameBirth==next.GameBirth&&hidden.SourceHwnd==next.SourceHwnd&&hidden.SourceCookie==next.SourceCookie
        &&N(next.Generation)>N(hidden.Generation)&&N(next.GuideEpoch)>N(hidden.GuideEpoch);

    internal static bool Run(SteamMirrorNativeVisibleDto admission,long epoch,SteamRawInputRelayTrial.SessionBinding session,
        SteamRawInputRelayTrial.ReaderOutcome reader,SteamInputOrderedIssuer issuer,Func<bool> current,Func<bool> neutralHeld,
        Func<SteamMirrorNativeVisibleDto?> read,Func<long> clock,Action<int> wait,CancellationToken cancellation,
        Func<bool>? disconnected=null)
    {
        var owner=new PublicationOwner();bool returning=false,automatic=false;long closedUntil=0,unknownDeadline=0;
        SteamMirrorNativeVisibleDto? hiddenObservation=null;long hiddenObservedAt=0;
        void Report(SteamMirrorNativeVisibleDto n,string state,bool menu=false)
        {
            long now=clock();
            Volatile.Write(ref published,new Publication(owner,n,state,reader.CanResume&&reader.ReaderStopped&&reader.ReaderReleased&&neutralHeld(),
                menu,automatic,now,closedUntil,current,neutralHeld,read,clock));
        }
        bool Return()
        {
            if(returning)return automatic;returning=true;
            // Revoke the cross-thread input grant BEFORE the blocking0 ACK.
            var p=Volatile.Read(ref published);
            if(p is not null&&ReferenceEquals(p.Owner,owner))
                Volatile.Write(ref published,p with{State="returning",Menu=false,Automatic=false,At=clock()});
            automatic=SteamMirrorInputCycle.Release(issuer,clock,wait);return automatic;
        }
        bool Same(SteamMirrorNativeVisibleDto? n)=>n is not null&&SameIdentity(admission,n);
        SteamInputOrderedIssuer.NativeVisibleInputLease? FreshAfterWait(SteamInputOrderedIssuer.NativeVisibleInputLease held)
        {
            var first=read();long now=clock();
            if(!Same(first)||!MatchesLease(first,held,now)||!current()||!neutralHeld())return null;
            var last=read();now=clock();
            if(last is null||first is null||!SameIdentity(first,last)||!MatchesLease(last,held,now)
                ||!current()||!neutralHeld()||!MatchesLease(last,held,clock()))return null;
            return held with{ExpiresAt=(long)N(last.PhysicalExpiresAt)};
        }
        try
        {
            var first=read();long now=clock();
            bool valid=reader.CanResume&&reader.EndReason==RelayExit.OverlayOpened&&reader.Epoch==epoch&&neutralHeld()
                &&Same(first)&&first is not null&&first.OwnerPid==(uint)session.CompanionPid&&N(first.OwnerBirth)==(ulong)session.CompanionBirth
                &&first.GamePid==session.GamePid&&N(first.GameBirth)==(ulong)session.GameBirth&&N(first.GameId)==session.GameId
                &&SteamMirrorWireProtocol.PositiveNativeVisible(first,now)&&current();
            SteamInputOrderedIssuer.NativeVisibleInputLease? lease=null;
            bool Admit(SteamMirrorNativeVisibleDto n)
            {
                var binding=new SteamInputSessionOutput.ReaderBinding(session,reader.ReaderPid,reader.ReaderBirth,reader.Epoch);
                lease=new(Guid.NewGuid(),n.SessionId,N(n.Generation),session.GameId,(long)N(n.PhysicalExpiresAt),
                    session,epoch,binding,N(n.GuideEpoch),N(n.SourceHwnd),N(n.SourceCookie));
                return issuer.BeginNativeVisible(lease)&&issuer.ReaderRetired(binding,reader);
            }
            if(!valid){Return();return false;}
            if(!Admit(first!)){Return();return false;}
            while(!cancellation.IsCancellationRequested&&current()&&neutralHeld())
            {
                if(disconnected?.Invoke()==true)return false;
                var n=read();now=clock();
                bool same=Same(n)&&SteamMirrorWireProtocol.NativeVisibleValid(n,now)&&current();
                if(hiddenObservation is null)
                {
                    var historical=Volatile.Read(ref owner.Hidden);
                    if(historical is not null&&Same(historical.Native)
                        &&historical.Native.Phase==2&&SteamMirrorWireProtocol.NativeVisibleValid(historical.Native,historical.At)
                        &&now>=historical.At&&now-historical.At<1500&&(unknownDeadline==0||now<unknownDeadline)
                        &&current()&&neutralHeld())
                    {
                        long after=clock();
                        if(after>=now&&after-historical.At<1500&&(unknownDeadline==0||after<unknownDeadline)
                            &&ReferenceEquals(Volatile.Read(ref owner.Hidden),historical))
                        {hiddenObservation=historical.Native;hiddenObservedAt=historical.At;}
                    }
                }
                if(hiddenObservation is {} hidden&&automatic&&(unknownDeadline==0||now<unknownDeadline)
                    &&SuccessorAfterHidden(hidden,hiddenObservedAt,n,now))
                {
                    // The successor may arrive after0 ACK, during the SAME
                    // bounded unknown wait. This historical hidden fact never
                    // refreshes its deadline or supplies a new physical lease.
                    var next=read();now=clock();
                    if(n is null||next is null||!SameIdentity(n,next)||!SuccessorAfterHidden(hidden,hiddenObservedAt,next,now)
                        ||!current()||!neutralHeld()||!issuer.AutomaticAcknowledged
                        ||unknownDeadline!=0&&clock()>=unknownDeadline
                        ||!SuccessorAfterHidden(hidden,hiddenObservedAt,next,clock()))return false;
                    admission=next;returning=false;automatic=false;unknownDeadline=0;closedUntil=0;hiddenObservation=null;
                    if(!Admit(next)){Return();return false;}
                    Report(next,"neutralizing");continue;
                }
                if(same&&n!.Phase==2)
                {
                    if(hiddenObservation is null){hiddenObservation=n;hiddenObservedAt=now;}
                    if(!Return())return false;
                    // Source-hidden and own0 ACK are independent required facts.
                    var last=read();now=clock();
                    if(!Same(last)||last?.Phase!=2||!SteamMirrorWireProtocol.NativeVisibleValid(last,now)||!current()){
                        if(unknownDeadline==0)unknownDeadline=now+1500;
                        if(clock()>=unknownDeadline)return false;
                        wait(15);continue;
                    }
                    closedUntil=now+5000;Report(last!,"closed");return issuer.ResumeAllowed&&current();
                }
                if(!same||!SteamMirrorWireProtocol.PositiveNativeVisible(n,now)||lease is null)
                {
                    if(unknownDeadline==0)unknownDeadline=now+1500;
                    if(!Return())return false;
                    if(clock()>=unknownDeadline)return false;
                    if(same)Report(n!,"returning");
                    // Unknown/expired visible proof is not source-hidden. Never
                    // resume a reader. Retain SAME neutral PDO only bounded.
                    wait(15);continue;
                }
                if(returning)
                {
                    // A real fresh positive240 can obtain a NEW typed lease,
                    // after our0 ACK, still using this same stopped reader/PDO.
                    if(!automatic||!Admit(n!))return false;
                    returning=false;automatic=false;
                }
                long expiry=(long)N(n!.PhysicalExpiresAt);
                if(expiry>lease.ExpiresAt)
                {
                    var next=lease with{ExpiresAt=expiry};
                    if(!issuer.RenewNativeVisible(next)){if(!Return())return false;continue;}lease=next;
                }
                issuer.Tick(FreshAfterWait);
                if(issuer.State==SteamInputOrderedIssuer.Phase.Uncertain)return false;
                var retained=issuer.HeldNativeVisible(lease.Owner);
                if(retained is null)
                {
                    if(!Return())return false;
                    Report(n,"returning");wait(15);continue;
                }
                lease=retained; // Only the issuer's accepted fresh lease, never a proposal.
                bool menu=issuer.UiInputAllowed;
                Report(n,menu?"menu":"neutralizing",menu);
                wait(15);
            }
            return false;
        }
        finally
        {
            Return();
            Interlocked.Exchange(ref owner.Hidden,null);
            var p=Volatile.Read(ref published);
            if(p is not null&&ReferenceEquals(p.Owner,owner)&&p.State!="closed")
                Interlocked.CompareExchange(ref published,null,p);
        }
    }
}
