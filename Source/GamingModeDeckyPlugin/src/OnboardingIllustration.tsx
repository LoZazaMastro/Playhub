import { SP_REACT as React } from './decky';
import { PlayhubIcon } from './PlayhubIcon';
import { TbDeviceGamepad2, TbLayoutDashboard, TbMusic, TbPhoto, TbPlugConnected, TbPlug, TbWindow, TbAdjustmentsHorizontal, TbDeviceDesktop, TbVolume2, TbCpu, TbActivityHeartbeat, TbBrandGithub, TbBuildingStore } from 'react-icons/tb';

export type OnboardingIllustrationView = 'intro' | 'playhub' | 'decky' | 'decky-off' | 'store' | 'customize' | 'audio' | 'performance' | 'graphics' | 'controller';

const css = `
.ph-oi{width:100%;max-width:var(--ph-oi-max,320px);min-width:0;height:var(--ph-oi-height,130px);flex:none;margin:0 auto;position:relative;overflow:hidden;pointer-events:none;user-select:none;container-type:inline-size;color:rgba(255,255,255,.88)}
.ph-oi *{box-sizing:border-box;pointer-events:none}
.ph-oi-stage{position:absolute;width:280px;height:104px;left:50%;top:50%;margin-left:-140px;margin-top:-52px;transform:scale(calc(var(--ph-oi-scale,1) * var(--ph-oi-shrink,1)));transform-origin:center;isolation:isolate}
.ph-oi svg{display:block;width:24px;height:24px;stroke-width:1.5}
.ph-oi-tile{position:absolute;display:grid;place-items:center;border:1px solid rgba(255,255,255,.42);border-radius:6px;background:rgba(255,255,255,.045)}
.ph-oi-panel{width:58px;height:60px;left:111px;top:22px;animation:phOiCompose 6s cubic-bezier(.3,0,.2,1) infinite}
.ph-oi-panel::after{content:'';position:absolute;width:23px;height:2px;bottom:9px;background:rgba(255,255,255,.25);transform:scaleX(.75)}
.ph-oi-panel svg{margin-top:-8px;width:28px;height:28px}
.ph-oi-panel:nth-child(1){--x:-72px;--angle:-8deg;animation-delay:-.14s}
.ph-oi-panel:nth-child(2){--x:0px;--angle:0deg}
.ph-oi-panel:nth-child(3){--x:72px;--angle:8deg;animation-delay:-.28s}
.ph-oi-baseline{position:absolute;left:40px;right:40px;bottom:10px;height:1px;background:rgba(255,255,255,.16);transform-origin:center;animation:phOiBaseline 6s ease infinite}
@keyframes phOiCompose{0%,100%{opacity:0;transform:translate3d(0,10px,0) scale(.8) rotate(var(--angle))}14%{opacity:.8;transform:translate3d(var(--x),-5px,0) scale(.98) rotate(var(--angle))}27%,76%{opacity:1;transform:translate3d(var(--x),0,0) scale(1) rotate(0)}91%{opacity:0;transform:translate3d(var(--x),8px,0) scale(.95) rotate(0)}}
@keyframes phOiBaseline{0%,100%{opacity:0;transform:scaleX(.3)}28%,76%{opacity:1;transform:scaleX(1)}92%{opacity:0;transform:scaleX(.8)}}
.ph-oi-window{position:absolute;left:65px;top:13px;width:150px;height:79px;border:1px solid rgba(255,255,255,.45);border-radius:6px;animation:phOiDesktop 6.4s cubic-bezier(.3,0,.2,1) infinite}
.ph-oi-window-bar{position:absolute;left:0;right:0;top:15px;height:1px;background:rgba(255,255,255,.22)}
.ph-oi-window-bar::before{content:'';position:absolute;left:10px;top:-8px;width:23px;height:2px;background:rgba(255,255,255,.4)}
.ph-oi-window .ph-oi-mini{position:absolute;top:29px;width:32px;height:34px;border:1px solid rgba(255,255,255,.25);border-radius:3px;display:grid;place-items:center}
.ph-oi-mini:nth-of-type(2){left:14px}.ph-oi-mini:nth-of-type(3){left:59px}.ph-oi-mini:nth-of-type(4){left:104px}
.ph-oi-mini svg{width:19px;height:19px;opacity:.6}
.ph-oi-gamepad{position:absolute;left:106px;top:24px;animation:phOiGaming 6.4s cubic-bezier(.3,0,.2,1) infinite}
.ph-oi-gamepad svg{width:68px;height:56px;stroke-width:1.25}
.ph-oi-mode-line{position:absolute;left:116px;bottom:6px;width:48px;height:2px;background:rgba(255,255,255,.25)}
.ph-oi-mode-line::after{content:'';display:block;width:20px;height:2px;background:rgba(255,255,255,.8);animation:phOiMode 6.4s ease infinite}
@keyframes phOiDesktop{0%,18%,94%,100%{opacity:1;transform:translate3d(0,0,0) scale(1)}38%,69%{opacity:0;transform:translate3d(-46px,0,0) scale(.83)}79%{opacity:0;transform:translate3d(32px,0,0) scale(.9)}}
@keyframes phOiGaming{0%,20%,100%{opacity:0;transform:translate3d(48px,0,0) scale(.85)}39%,68%{opacity:1;transform:translate3d(0,0,0) scale(1)}83%,96%{opacity:0;transform:translate3d(-40px,0,0) scale(.85)}}
@keyframes phOiMode{0%,18%,94%,100%{transform:translateX(0)}38%,72%{transform:translateX(28px)}}
.ph-oi-plug{position:absolute;left:44px;top:35px;animation:phOiPlug 5.8s ease infinite}
.ph-oi-plug svg{width:34px;height:34px}
.ph-oi-link{position:absolute;left:88px;top:51px;width:24px;height:1px;background:rgba(255,255,255,.35);transform-origin:left;animation:phOiLink 5.8s ease infinite}
.ph-oi-plugin{left:153px;top:34px;width:36px;height:36px;animation:phOiOrganize 5.8s cubic-bezier(.3,0,.2,1) infinite}
.ph-oi-plugin:nth-of-type(3){--x:-24px;--y:-23px;--dx:-12px;--dy:8px;--r:-12deg}
.ph-oi-plugin:nth-of-type(4){--x:24px;--y:-23px;--dx:30px;--dy:-2px;--r:9deg}
.ph-oi-plugin:nth-of-type(5){--x:-24px;--y:23px;--dx:-32px;--dy:15px;--r:7deg}
.ph-oi-plugin:nth-of-type(6){--x:24px;--y:23px;--dx:18px;--dy:8px;--r:-8deg}
.ph-oi-plugin svg{width:21px;height:21px}
@keyframes phOiOrganize{0%,100%{opacity:0;transform:translate3d(var(--dx),var(--dy),0) rotate(var(--r)) scale(.8)}18%{opacity:.45;transform:translate3d(var(--dx),var(--dy),0) rotate(var(--r)) scale(.92)}40%,78%{opacity:.95;transform:translate3d(var(--x),var(--y),0) rotate(0) scale(1)}94%{opacity:0;transform:translate3d(var(--x),var(--y),0) rotate(0) scale(.92)}}
@keyframes phOiPlug{0%,100%{opacity:.35;transform:translateX(-8px)}25%,78%{opacity:.95;transform:translateX(0)}}
@keyframes phOiLink{0%,12%,100%{opacity:0;transform:scaleX(0)}30%,76%{opacity:1;transform:scaleX(1)}90%{opacity:0;transform:scaleX(1)}}
.ph-oi-off .ph-oi-plug{animation-name:phOiUnplug}
.ph-oi-off .ph-oi-link{animation-name:phOiDisconnect}
.ph-oi-off .ph-oi-plugin{animation-name:phOiPark}
@keyframes phOiUnplug{0%,12%,100%{opacity:.9;transform:translateX(0) rotate(0)}36%,80%{opacity:.5;transform:translateX(-14px) rotate(-12deg)}}
@keyframes phOiDisconnect{0%,12%,100%{opacity:.7;transform:scaleX(1)}30%,82%{opacity:0;transform:scaleX(0)}}
@keyframes phOiPark{0%,12%,100%{opacity:.75;transform:translate3d(var(--x),var(--y),0) scale(1)}38%,80%{opacity:.22;transform:translate3d(var(--x),calc(var(--y) + 7px),0) scale(.92)}}
.ph-oi-store-frame{position:absolute;left:118px;top:14px;width:144px;height:76px;border:1px solid rgba(255,255,255,.5);border-radius:7px;overflow:hidden;animation:phOiStoreFrame 6.6s ease infinite}
.ph-oi-store-bar{position:absolute;left:0;right:0;top:18px;height:1px;background:rgba(255,255,255,.22)}
.ph-oi-store-mark{position:absolute;left:9px;top:2px;width:15px;height:15px;display:grid;place-items:center}
.ph-oi-store-mark svg{width:15px;height:15px;opacity:.85}
.ph-oi-store-item{position:absolute;top:40px;width:24px;height:24px;border:1px solid rgba(255,255,255,.4);border-radius:5px;display:grid;place-items:center;opacity:0;animation:phOiStoreItem 6.6s cubic-bezier(.3,0,.2,1) infinite}
.ph-oi-store-item svg{width:13px;height:13px;opacity:.75}
.ph-oi-store-item-a{left:16px;animation-delay:0s}
.ph-oi-store-item-b{left:58px;animation-delay:.22s}
.ph-oi-store-item-c{left:100px;animation-delay:.44s}
.ph-oi-store-sweep{position:absolute;left:-34px;top:0;bottom:0;width:30px;background:linear-gradient(90deg,transparent,rgba(255,255,255,.18),transparent);animation:phOiStoreSweep 6.6s ease-in-out infinite}
.ph-oi-src{position:absolute;left:18px;width:30px;height:30px;display:grid;place-items:center;border:1px solid rgba(255,255,255,.42);border-radius:6px;background:rgba(255,255,255,.045);opacity:0;animation:phOiSource 6.6s ease infinite}
.ph-oi-src svg{width:16px;height:16px}
.ph-oi-src-a{top:6px;animation-delay:0s}
.ph-oi-src-b{top:37px;animation-delay:.2s}
.ph-oi-src-c{top:68px;animation-delay:.4s}
.ph-oi-route{position:absolute;left:48px;width:56px;height:1px;background:rgba(255,255,255,.34);transform-origin:left;animation:phOiRoute 6.6s ease infinite}
.ph-oi-route-a{top:21px;animation-delay:.1s}
.ph-oi-route-b{top:52px;animation-delay:.3s}
.ph-oi-route-c{top:83px;animation-delay:.5s}
.ph-oi-collector{position:absolute;left:104px;top:21px;width:1px;height:62px;background:rgba(255,255,255,.34);transform-origin:center;animation:phOiCollector 6.6s ease infinite}
.ph-oi-feed{position:absolute;left:105px;top:52px;width:13px;height:1px;background:rgba(255,255,255,.34);transform-origin:left;animation:phOiFeed 6.6s ease infinite}
@keyframes phOiSource{0%,3%,100%{opacity:0;transform:translateX(-8px)}12%,80%{opacity:.95;transform:translateX(0)}92%{opacity:0;transform:translateX(0)}}
@keyframes phOiRoute{0%,10%,100%{opacity:0;transform:scaleX(0)}20%,80%{opacity:.85;transform:scaleX(1)}92%{opacity:0;transform:scaleX(1)}}
@keyframes phOiCollector{0%,20%,100%{opacity:0;transform:scaleY(0)}30%,80%{opacity:.7;transform:scaleY(1)}92%{opacity:0;transform:scaleY(1)}}
@keyframes phOiFeed{0%,26%,100%{opacity:0;transform:scaleX(0)}36%,80%{opacity:.9;transform:scaleX(1)}92%{opacity:0;transform:scaleX(1)}}
@keyframes phOiStoreFrame{0%,8%,100%{opacity:.3;transform:scale(.97)}20%,84%{opacity:1;transform:scale(1)}94%{opacity:.3;transform:scale(.97)}}
@keyframes phOiStoreItem{0%,34%{opacity:0;transform:translate3d(-16px,0,0) scale(.7)}46%,82%{opacity:.95;transform:translate3d(0,0,0) scale(1)}92%,100%{opacity:0;transform:translate3d(0,0,0) scale(.92)}}
@keyframes phOiStoreSweep{0%,58%,100%{opacity:0;transform:translateX(0)}66%{opacity:1}80%{opacity:0;transform:translateX(180px)}}
.ph-oi-tabs-line{position:absolute;left:47px;right:47px;top:76px;height:1px;background:rgba(255,255,255,.2)}
.ph-oi-tab{left:117px;top:26px;width:46px;height:42px;animation:phOiReorder 7s cubic-bezier(.3,0,.2,1) infinite}
.ph-oi-tab:nth-of-type(2){--start:-62px;--end:0px;--lift:-25px}.ph-oi-tab:nth-of-type(3){--start:0px;--end:-62px;--lift:25px}.ph-oi-tab:nth-of-type(4){--start:62px;--end:62px;animation-name:phOiHide}
.ph-oi-tab:nth-of-type(2)::after{content:'';position:absolute;bottom:-10px;width:18px;height:2px;background:rgba(255,255,255,.8)}
@keyframes phOiReorder{0%,16%,100%{opacity:.9;transform:translate3d(var(--start),0,0)}26%,80%{opacity:1;transform:translate3d(-31px,var(--lift),0)}36%,72%{opacity:1;transform:translate3d(var(--end),0,0)}90%{opacity:.9;transform:translate3d(var(--start),0,0)}}
@keyframes phOiHide{0%,36%,94%,100%{opacity:.8;transform:translate3d(var(--start),0,0) scale(1)}51%,72%{opacity:0;transform:translate3d(var(--start),16px,0) scale(.8)}}
.ph-oi-monitor{position:absolute;left:89px;top:5px}.ph-oi-monitor svg{width:102px;height:82px;stroke-width:1.15}
.ph-oi-video-light{position:absolute;left:107px;top:23px;width:66px;height:33px;border-radius:2px;background:linear-gradient(120deg,rgba(255,255,255,.12),rgba(255,255,255,.65));animation:phOiBrightness 6s ease infinite}
@keyframes phOiBrightness{0%,100%{opacity:.25}45%,65%{opacity:.9}}
.ph-oi-speaker{position:absolute;left:96px;top:27px}.ph-oi-speaker svg{width:44px;height:44px}
.ph-oi-slider{position:absolute;left:106px;top:87px;width:68px;height:2px;background:rgba(255,255,255,.23)}
.ph-oi-slider::before{content:'';position:absolute;inset:0;background:rgba(255,255,255,.7);transform-origin:left;animation:phOiLevel 6s ease infinite}
.ph-oi-slider::after{content:'';position:absolute;left:0;top:-3px;width:4px;height:8px;border-radius:1px;background:rgba(255,255,255,.95);animation:phOiFader 6s ease infinite}
.ph-oi-wave{position:absolute;top:33px;width:3px;height:34px;background:rgba(255,255,255,.65);border-radius:2px;animation:phOiWave 6s ease infinite;transform-origin:center}
.ph-oi-wave:nth-of-type(3){left:153px;--quiet:.16;--loud:.6}.ph-oi-wave:nth-of-type(4){left:164px;--quiet:.3;--loud:1}.ph-oi-wave:nth-of-type(5){left:175px;--quiet:.12;--loud:.45}
@keyframes phOiLevel{0%,15%,100%{transform:scaleX(.28)}40%,66%{transform:scaleX(.83)}84%{transform:scaleX(.45)}}
@keyframes phOiFader{0%,15%,100%{transform:translateX(17px)}40%,66%{transform:translateX(54px)}84%{transform:translateX(28px)}}
@keyframes phOiWave{0%,15%,100%{opacity:.4;transform:scaleY(var(--quiet))}40%,52%,66%{opacity:.9;transform:scaleY(var(--loud))}46%,59%{opacity:.75;transform:scaleY(.4)}84%{opacity:.6;transform:scaleY(.35)}}
.ph-oi-cpu{position:absolute;left:106px;top:19px}.ph-oi-cpu svg{width:68px;height:68px;stroke-width:1.2}
.ph-oi-load{position:absolute;bottom:27px;width:7px;height:43px;border-radius:2px;background:rgba(255,255,255,.55);transform-origin:bottom;animation:phOiWork 4.8s ease infinite}
.ph-oi-load:nth-of-type(2){left:43px;--load:.6;animation-delay:-.45s}.ph-oi-load:nth-of-type(3){left:57px;--load:1;animation-delay:-.3s}.ph-oi-load:nth-of-type(4){left:71px;--load:.78;animation-delay:-.15s}
.ph-oi-through{position:absolute;left:180px;top:51px;width:16px;height:2px;background:rgba(255,255,255,.8);animation:phOiProcess 4.8s ease infinite}
.ph-oi-activity{position:absolute;left:209px;top:35px}.ph-oi-activity svg{width:35px;height:35px;opacity:.7}
@keyframes phOiWork{0%,100%{opacity:.3;transform:scaleY(.2)}24%,42%{opacity:.9;transform:scaleY(var(--load))}65%,82%{opacity:.5;transform:scaleY(.36)}}
@keyframes phOiProcess{0%,35%,100%{opacity:0;transform:translateX(-7px) scaleX(.2)}48%{opacity:.9;transform:translateX(0) scaleX(1)}66%{opacity:0;transform:translateX(17px) scaleX(.3)}}
.ph-oi-render-plane{position:absolute;left:97px;top:23px;width:86px;height:59px;border:1px solid rgba(255,255,255,.4);border-radius:4px;animation:phOiLayers 6s cubic-bezier(.3,0,.2,1) infinite}
.ph-oi-render-plane:nth-child(1){--plane-x:-19px;--plane-y:-10px;opacity:.35}.ph-oi-render-plane:nth-child(2){--plane-x:19px;--plane-y:10px;opacity:.35}
.ph-oi-render-image{position:absolute;left:97px;top:23px;width:86px;height:59px;display:grid;place-items:center;border:1px solid rgba(255,255,255,.55);border-radius:4px}
.ph-oi-render-image svg{width:45px;height:45px;stroke-width:1.15;animation:phOiResolve 6s ease infinite}
.ph-oi-scan{position:absolute;left:7px;right:7px;top:9px;height:1px;background:rgba(255,255,255,.7);animation:phOiScan 6s ease infinite}
@keyframes phOiLayers{0%,15%,100%{transform:translate(var(--plane-x),var(--plane-y));opacity:.35}36%,75%{transform:translate(0,0);opacity:0}91%{transform:translate(var(--plane-x),var(--plane-y));opacity:.35}}
@keyframes phOiResolve{0%,16%,100%{opacity:.3;transform:scale(.86)}45%,76%{opacity:1;transform:scale(1)}}
@keyframes phOiScan{0%,30%,100%{opacity:0;transform:translateY(0) scaleX(.6)}40%{opacity:.8;transform:translateY(0) scaleX(1)}67%{opacity:.8;transform:translateY(39px) scaleX(1)}77%{opacity:0;transform:translateY(39px) scaleX(.6)}}
.ph-oi-pad-body{position:absolute;left:85px;top:9px;animation:phOiPadPress 6s ease infinite}.ph-oi-pad-body svg{width:110px;height:84px;stroke-width:1.05}
.ph-oi-input-orbit{position:absolute;left:52px;top:32px;width:34px;height:34px;border:1px solid rgba(255,255,255,.3);border-radius:50%}
.ph-oi-input-stick{position:absolute;left:11px;top:11px;width:10px;height:10px;border:1px solid rgba(255,255,255,.85);border-radius:50%;animation:phOiStick 6s cubic-bezier(.3,0,.2,1) infinite}
.ph-oi-input-key{position:absolute;left:207px;top:36px;width:26px;height:26px;border:1px solid rgba(255,255,255,.5);border-radius:5px;animation:phOiButtonPress 6s ease infinite}
.ph-oi-input-key::after{content:'';position:absolute;left:8px;top:8px;width:8px;height:8px;border:1px solid rgba(255,255,255,.7);transform:rotate(45deg)}
.ph-oi-input-link{position:absolute;left:197px;top:48px;width:7px;height:1px;background:rgba(255,255,255,.4);animation:phOiButtonSignal 6s ease infinite}
@keyframes phOiStick{0%,12%,52%,100%{transform:translate(0,0)}23%{transform:translate(-7px,-3px)}34%{transform:translate(0,-7px)}43%{transform:translate(7px,0)}}
@keyframes phOiButtonPress{0%,53%,73%,100%{opacity:.6;transform:translateY(0) scale(1)}61%,66%{opacity:1;transform:translateY(3px) scale(.9)}}
@keyframes phOiPadPress{0%,53%,73%,100%{transform:rotate(0) translateY(0)}61%,66%{transform:rotate(2deg) translateY(1px)}}
@keyframes phOiButtonSignal{0%,53%,77%,100%{opacity:0;transform:scaleX(.3)}61%,70%{opacity:1;transform:scaleX(1)}}
@container(max-width:299px){.ph-oi-stage{--ph-oi-shrink:.9}}
@container(max-width:259px){.ph-oi-stage{--ph-oi-shrink:.75}}
@container(max-width:219px){.ph-oi-stage{--ph-oi-shrink:.6}}
@media(prefers-reduced-motion:reduce){
 .ph-oi *,.ph-oi *::before,.ph-oi *::after{animation:none!important}
 .ph-oi-panel{opacity:1;transform:translateX(var(--x))}.ph-oi-baseline{opacity:1}
 .ph-oi-window{opacity:1}.ph-oi-gamepad{opacity:0}.ph-oi-mode-line::after{transform:none}
 .ph-oi-plugin{opacity:.9;transform:translate(var(--x),var(--y))}.ph-oi-plug{opacity:.9}.ph-oi-link{opacity:.7}
 .ph-oi-off .ph-oi-plug{opacity:.5;transform:translateX(-14px) rotate(-12deg)}.ph-oi-off .ph-oi-link{opacity:0}.ph-oi-off .ph-oi-plugin{opacity:.22}
 .ph-oi-tab{opacity:.9;transform:translateX(var(--end))}.ph-oi-tab:nth-of-type(4){opacity:.22}
 .ph-oi-slider::before{transform:scaleX(.6)}.ph-oi-slider::after{transform:translateX(39px)}.ph-oi-wave{opacity:.65;transform:scaleY(var(--loud))}
 .ph-oi-load{opacity:.6;transform:scaleY(var(--load))}.ph-oi-through{opacity:.6}
 .ph-oi-render-plane{opacity:.25;transform:translate(var(--plane-x),var(--plane-y))}.ph-oi-render-image svg{opacity:1}.ph-oi-scan{opacity:0}
 .ph-oi-input-stick{transform:translate(3px,-3px)}.ph-oi-input-key{opacity:.8}.ph-oi-input-link{opacity:.4}
 .ph-oi-src{opacity:.9;transform:none}.ph-oi-route{opacity:.6;transform:scaleX(1)}.ph-oi-collector{opacity:.6;transform:scaleY(1)}.ph-oi-feed{opacity:.7;transform:scaleX(1)}
 .ph-oi-store-frame{opacity:1;transform:none}.ph-oi-store-item{opacity:.9;transform:none}.ph-oi-store-sweep{opacity:0}
}
`;

function PluginTiles({ disconnected }: { disconnected: boolean }) {
  return <div className={disconnected ? 'ph-oi-off' : undefined}>
    <span className="ph-oi-plug">{disconnected ? <TbPlug /> : <TbPlugConnected />}</span>
    <span className="ph-oi-link" />
    <span className="ph-oi-tile ph-oi-plugin"><TbPhoto /></span>
    <span className="ph-oi-tile ph-oi-plugin"><TbMusic /></span>
    <span className="ph-oi-tile ph-oi-plugin"><TbDeviceGamepad2 /></span>
    <span className="ph-oi-tile ph-oi-plugin"><TbAdjustmentsHorizontal /></span>
  </div>;
}

export function OnboardingIllustration({ view, scale = 1, height }: { view: OnboardingIllustrationView; scale?: number; height?: number }) {
  const size = { "--ph-oi-scale": scale, ...(height ? { "--ph-oi-height": `${height}px`, "--ph-oi-max": "100%" } : {}) } as React.CSSProperties;
  return <div className="ph-oi" aria-hidden="true" data-onboarding-illustration={view} style={size}>
    <style>{css}</style>
    <div className="ph-oi-stage" key={view}>
      {view === 'intro' && <>
        <span className="ph-oi-tile ph-oi-panel"><TbLayoutDashboard /></span>
        <span className="ph-oi-tile ph-oi-panel"><TbDeviceGamepad2 /></span>
        <span className="ph-oi-tile ph-oi-panel"><TbPlugConnected /></span>
        <span className="ph-oi-baseline" />
      </>}
      {view === 'playhub' && <>
        <span className="ph-oi-window"><span className="ph-oi-window-bar" />
          <span className="ph-oi-mini"><TbLayoutDashboard /></span><span className="ph-oi-mini"><TbWindow /></span><span className="ph-oi-mini"><TbPhoto /></span>
        </span>
        <span className="ph-oi-gamepad"><TbDeviceGamepad2 /></span><span className="ph-oi-mode-line" />
      </>}
      {(view === 'decky' || view === 'decky-off') && <PluginTiles disconnected={view === 'decky-off'} />}
      {view === 'store' && <>
        <span className="ph-oi-src ph-oi-src-a"><PlayhubIcon /></span>
        <span className="ph-oi-src ph-oi-src-b"><TbPlugConnected /></span>
        <span className="ph-oi-src ph-oi-src-c"><TbBrandGithub /></span>
        <span className="ph-oi-route ph-oi-route-a" />
        <span className="ph-oi-route ph-oi-route-b" />
        <span className="ph-oi-route ph-oi-route-c" />
        <span className="ph-oi-collector" />
        <span className="ph-oi-feed" />
        <span className="ph-oi-store-frame">
          <span className="ph-oi-store-bar" />
          <span className="ph-oi-store-mark"><TbBuildingStore /></span>
          <span className="ph-oi-store-item ph-oi-store-item-a"><TbPhoto /></span>
          <span className="ph-oi-store-item ph-oi-store-item-b"><TbMusic /></span>
          <span className="ph-oi-store-item ph-oi-store-item-c"><TbAdjustmentsHorizontal /></span>
          <span className="ph-oi-store-sweep" />
        </span>
      </>}
      {view === 'customize' && <>
        <span className="ph-oi-tabs-line" />
        <span className="ph-oi-tile ph-oi-tab"><TbLayoutDashboard /></span><span className="ph-oi-tile ph-oi-tab"><TbMusic /></span><span className="ph-oi-tile ph-oi-tab"><TbPhoto /></span>
      </>}
      {view === 'audio' && <>
        <span className="ph-oi-monitor"><TbDeviceDesktop /></span>
        <span className="ph-oi-video-light" />
        <span className="ph-oi-slider" />
      </>}
      {view === 'performance' && <>
        <span className="ph-oi-speaker"><TbVolume2 /></span>
        <span className="ph-oi-slider" /><span className="ph-oi-wave" /><span className="ph-oi-wave" /><span className="ph-oi-wave" />
      </>}
      {view === 'graphics' && <>
        <span className="ph-oi-render-plane" /><span className="ph-oi-render-plane" />
        <span className="ph-oi-render-image"><TbPhoto /><span className="ph-oi-scan" /></span>
      </>}
      {view === 'controller' && <>
        <span className="ph-oi-pad-body"><TbDeviceGamepad2 /></span>
        <span className="ph-oi-input-orbit"><span className="ph-oi-input-stick" /></span>
        <span className="ph-oi-input-key" /><span className="ph-oi-input-link" />
      </>}
    </div>
  </div>;
}
