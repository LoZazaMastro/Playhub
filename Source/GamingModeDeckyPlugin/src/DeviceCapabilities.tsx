import { TbCheck, TbChevronRight, TbInfoCircle, TbMinus } from "react-icons/tb";
import { DFL, SP_REACT as React } from "./decky";
import { call } from "./controlBackend";
import { deviceCapabilitiesLocale } from "./deviceCapabilitiesLocale";
import { deviceInfoLocale } from "./deviceInfoLocale";
const { DialogButton, Focusable } = DFL as any;
type Row = {id:string;available:boolean;status:string;destination:"video"|"performance"|"device";values?:(number|null)[]};
type Report = {status:string;selection:string;effective:string;detected:string;mismatch:boolean;coverage:"windows"|"oem";controls:Row[]};
const ids = new Set(["windows-display","windows-power","windows-battery","presets","fans","temperatures","power-limits","charge-limit","rgb","vibration"]);
const css = `.ph-device-capabilities{display:grid;gap:10px;min-width:0;padding:12px 0;border-top:1px solid #ffffff18}.ph-device-capability-list{display:grid;gap:10px;margin:0}.ph-device-capability-row{display:grid;grid-template-columns:18px minmax(0,1fr);gap:8px;align-items:start}.ph-device-capability-row>svg{width:16px;height:16px;margin-top:2px;opacity:.65}.ph-device-capability-row[data-available=true]>svg{color:#9bd7a7;opacity:1}.ph-device-capability-row dt{font-size:13px;line-height:1.4}.ph-device-capability-row dd{margin:2px 0 0;font-size:11px;line-height:1.4;color:#c5cbd1;overflow-wrap:anywhere}.ph-device-capability-mismatch{display:flex;gap:8px;font-size:12px;line-height:1.45;color:#ffe467}.ph-device-capability-mismatch svg{width:16px;height:16px;flex:none;margin-top:2px}.ph-device-capabilities .ph-device-actions button{justify-content:flex-start;white-space:normal;min-width:0}.ph-device-capabilities>strong{font-size:13px}`;

export function DeviceCapabilities({selection,effective,detected,locale,openVideo,openPerformance}:{selection:string;effective:string;detected:string;locale:string;openVideo?:()=>void;openPerformance?:()=>void}) {
  const copy=deviceCapabilitiesLocale(locale),device=deviceInfoLocale(locale);
  const [report,setReport]=React.useState<Report>();
  const [failed,setFailed]=React.useState(false);
  const [busy,setBusy]=React.useState(true);
  const [attempt,setAttempt]=React.useState(0);
  const epoch=React.useRef(0);
  React.useEffect(()=>{
    const generation=++epoch.current;let active=true;
    setReport(undefined);setFailed(false);setBusy(true);
    const timer=window.setTimeout(()=>{if(active&&epoch.current===generation){setFailed(true);setBusy(false);active=false;}},12000);
    void call<[string],Report>("get_device_capabilities",selection).then(value=>{
      if(!active||epoch.current!==generation)return;
      if(value?.status!=="ready"||value.selection!==selection||value.effective!==effective||value.detected!==detected||!Array.isArray(value.controls)
        ||value.controls.some(row=>!ids.has(row.id)||typeof row.available!=="boolean"||!Object.prototype.hasOwnProperty.call(copy.statuses,row.status)
          ||!["video","performance","device"].includes(row.destination)
          ||row.id==="temperatures"&&row.available&&(!Array.isArray(row.values)||row.values.length!==2||row.values.every(v=>v===null)
            ||row.values.some(v=>v!==null&&(typeof v!=="number"||!Number.isInteger(v)||v<1||v>125)))
          ||effective!==detected&&!row.id.startsWith("windows-")&&row.available))throw new Error("Device controls unavailable");
      setReport(value);
    }).catch(()=>{if(active&&epoch.current===generation)setFailed(true);}).finally(()=>{
      window.clearTimeout(timer);if(active&&epoch.current===generation)setBusy(false);
    });
    return()=>{active=false;++epoch.current;window.clearTimeout(timer);};
  },[selection,effective,detected,attempt]);
  const current=report?.selection===selection&&report.effective===effective&&report.detected===detected?report:undefined;
  const rows=current?.controls??[];
  const video=rows.some(row=>row.available&&row.destination==="video")?openVideo:undefined;
  const performance=rows.some(row=>row.available&&row.destination==="performance")?openPerformance:undefined;
  return <Focusable className="ph-device-capabilities" flow-children="column">
    <style>{css}</style>
    <strong>{current?.coverage==="windows"?copy.windows:copy.title}</strong>
    {effective!==detected&&<div className="ph-device-capability-mismatch" role="status"><TbInfoCircle aria-hidden="true"/><span>{copy.mismatch}</span></div>}
    {busy&&<div className="ph-device-detail" role="status">{device.loading}</div>}
    {failed&&<div className="ph-device-detail" role="status">{device.loadError}</div>}
    {current&&<dl className="ph-device-capability-list">{rows.map(row=><div key={row.id} className="ph-device-capability-row" data-available={row.available}>
      {row.available?<TbCheck aria-hidden="true"/>:<TbMinus aria-hidden="true"/>}
      <div><dt>{copy.labels[row.id as keyof typeof copy.labels]}</dt><dd>{copy.statuses[row.status as keyof typeof copy.statuses]}
        {row.id==="fans"&&row.available&&Array.isArray(row.values)&&<span> · {row.values.map(v=>v??"—").join(" / ")} RPM</span>}
        {row.id==="temperatures"&&row.available&&Array.isArray(row.values)&&<span> · CPU {row.values[0]??"—"} / GPU {row.values[1]??"—"} °C</span>}
      </dd></div>
    </div>)}</dl>}
    {(video||performance)&&<Focusable className="ph-device-actions" flow-children="column">
      {video&&<DialogButton onClick={video}><span>{device.video}</span><TbChevronRight aria-hidden="true"/></DialogButton>}
      {performance&&<DialogButton onClick={performance}><span>{device.power}</span><TbChevronRight aria-hidden="true"/></DialogButton>}
    </Focusable>}
    {(failed||rows.some(row=>row.status==="pending"||row.status==="unavailable"))&&<DialogButton disabled={busy} onClick={()=>setAttempt(value=>value+1)}>{copy.retry}</DialogButton>}
  </Focusable>;
}
