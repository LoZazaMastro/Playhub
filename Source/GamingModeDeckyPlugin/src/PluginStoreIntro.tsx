import { SP_REACT as React } from './decky';
import { TbCheck, TbDeviceGamepad2, TbMusic, TbPhoto } from 'react-icons/tb';

const style = `
.ph-store-intro{isolation:isolate;width:100%;min-width:0;padding:0 0 14px;box-sizing:border-box}
.ph-store-launch{position:relative;z-index:1}
.ph-store-download{position:relative;z-index:0;width:100%;overflow:hidden;pointer-events:none}
.ph-store-download-viewport{position:absolute;left:0;width:100%;pointer-events:none}
.ph-store-download-stage{position:absolute;inset:0;margin:auto;width:174px;height:84px;transform:scale(var(--ph-store-animation-scale,2))}
.ph-store-app{position:absolute;top:7px;left:75px;width:25px;height:25px;display:grid;place-items:center;border:1px solid rgba(255,255,255,.5);border-radius:6px;background:rgba(255,255,255,.08);color:rgba(255,255,255,.88);opacity:0;animation:phStoreAppSave 5.4s cubic-bezier(.3,0,.2,1) infinite;will-change:transform,opacity}
.ph-store-app svg{width:15px;height:15px}
.ph-store-app:nth-child(1){--ph-slot:-38px;animation-delay:-3.6s}
.ph-store-app:nth-child(2){--ph-slot:0px;animation-delay:-1.8s}
.ph-store-app:nth-child(3){--ph-slot:38px;animation-delay:0s}
.ph-store-shelf{position:absolute;left:20px;right:20px;bottom:8px;height:13px;border:1px solid rgba(255,255,255,.24);border-top:0;border-radius:0 0 7px 7px}
.ph-store-saved{position:absolute;right:3px;bottom:8px;width:14px;height:14px;color:rgba(255,255,255,.65);animation:phStoreSaved 1.8s ease infinite}
.ph-store-intro-copy{width:calc(100% - 42px);max-width:278px;margin:6px auto 0;text-align:center;color:rgba(255,255,255,.58);font-size:12px;line-height:1.4}
@keyframes phStoreAppSave{
  0%{opacity:0;transform:translate3d(0,-15px,0) scale(.85)}
  12%{opacity:1;transform:translate3d(0,0,0) scale(1)}
  30%{opacity:1;transform:translate3d(var(--ph-slot),38px,0) scale(1)}
  36%,73%{opacity:.8;transform:translate3d(var(--ph-slot),36px,0) scale(.94)}
  90%,100%{opacity:0;transform:translate3d(var(--ph-slot),36px,0) scale(.94)}
}
@keyframes phStoreSaved{0%,66%,100%{opacity:0;transform:scale(.8)}78%,88%{opacity:.8;transform:scale(1)}}
@media(prefers-reduced-motion:reduce){.ph-store-app{animation:none;opacity:.8;transform:translate3d(var(--ph-slot),36px,0)}.ph-store-saved{animation:none;opacity:.7}}
`;

export function PluginStoreIntro({ description, button }: { description: string; button: React.ReactNode }) {
  const container = React.useRef<HTMLDivElement>(null);
  const launcher = React.useRef<HTMLDivElement>(null);
  const [height, setHeight] = React.useState(168);
  const [overlap, setOverlap] = React.useState(0);
  const [wrapperGap, setWrapperGap] = React.useState(0);
  React.useLayoutEffect(() => {
    const node = container.current;
    if (!node) return;
    const resize = () => {
      const scale = node.clientWidth / 174;
      if (scale <= 0) return;
      node.style.setProperty('--ph-store-animation-scale', String(scale));
      setHeight(Math.ceil(84 * scale));
      const launch = launcher.current;
      const target = launch?.querySelector('button');
      if (launch && target) {
        const bounds = target.getBoundingClientRect();
        // Clip at the actual button edge, excluding native ButtonItem bottom padding.
        setWrapperGap(Math.max(0, launch.getBoundingClientRect().bottom - bounds.bottom));
        setOverlap(bounds.height / 2);
      }
    };
    resize();
    const observer = new ResizeObserver(resize);
    observer.observe(node);
    if (launcher.current) observer.observe(launcher.current);
    const target = launcher.current?.querySelector('button');
    if (target) observer.observe(target);
    return () => observer.disconnect();
  }, []);
  return <div className="ph-store-intro" ref={container}>
    <style>{style}</style>
    <div className="ph-store-launch" ref={launcher}>{button}</div>
    <div className="ph-store-download" style={{ height: Math.max(0, height - overlap), marginTop: -wrapperGap }} aria-hidden="true">
      <div className="ph-store-download-viewport" style={{ height, top: -overlap }}>
      <div className="ph-store-download-stage">
        <span className="ph-store-app"><TbPhoto/></span>
        <span className="ph-store-app"><TbMusic/></span>
        <span className="ph-store-app"><TbDeviceGamepad2/></span>
        <span className="ph-store-shelf"/>
        <TbCheck className="ph-store-saved"/>
      </div>
      </div>
    </div>
    <div className="ph-store-intro-copy">{description}</div>
  </div>;
}
