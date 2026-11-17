// Window only the already admitted progressive prefix. Native focus stays on a keyed card.
export type StoreDigitalGridMetrics={columns:number;rowHeight:number;gap:number};
export type StoreDigitalWindow={start:number;end:number;loaded:number;top:number;bottom:number;capacity:number};
export const DIGITAL_WINDOW_CARDS=192;
export function digitalWindowPlan(loaded:number,requestedStart:number,metrics:StoreDigitalGridMetrics|null):StoreDigitalWindow{
 if(!metrics)return{start:0,end:loaded,loaded,top:0,bottom:0,capacity:DIGITAL_WINDOW_CARDS};
 const {columns,rowHeight,gap}=metrics,rows=Math.ceil(loaded/columns),windowRows=Math.max(1,Math.floor(DIGITAL_WINDOW_CARDS/columns)-1),capacity=windowRows*columns;
 const start=Math.max(0,Math.min(Math.floor(requestedStart/columns),Math.max(0,rows-windowRows)))*columns,end=Math.min(loaded,start+capacity),stride=rowHeight+gap;
 const before=start/columns,after=rows-Math.ceil(end/columns);
 return{start,end,loaded,top:before?before*stride-gap:0,bottom:after?after*stride-gap:0,capacity};
}
export type StoreDigitalWindowRange={start:number;end:number};
export function digitalWindowRanges(plan:StoreDigitalWindow,focusIndex:number,metrics:StoreDigitalGridMetrics|null):StoreDigitalWindowRange[]{
 const ranges=[{start:plan.start,end:plan.end}];
 if(metrics&&focusIndex>=0&&focusIndex<plan.loaded&&(focusIndex<plan.start||focusIndex>=plan.end)){
  const start=Math.floor(focusIndex/metrics.columns)*metrics.columns;ranges.push({start,end:Math.min(plan.loaded,start+metrics.columns)});
 }
 ranges.sort((a,b)=>a.start-b.start);const merged:StoreDigitalWindowRange[]=[];
 for(const range of ranges){const previous=merged[merged.length-1];if(previous&&range.start<=previous.end)previous.end=Math.max(previous.end,range.end);else merged.push({...range});}
 return merged;
}
export function digitalWindowPieces(plan:StoreDigitalWindow,ranges:StoreDigitalWindowRange[],metrics:StoreDigitalGridMetrics|null):({kind:"cards";start:number;end:number}|{kind:"spacer";key:string;height:number})[]{
 if(!metrics)return[{kind:"cards",start:0,end:plan.loaded}];
 const pieces:ReturnType<typeof digitalWindowPieces>=[];let previousRow=0;const stride=metrics.rowHeight+metrics.gap;
 for(const range of ranges){const firstRow=range.start/metrics.columns;if(firstRow>previousRow)pieces.push({kind:"spacer",key:`gap-${previousRow}-${firstRow}`,height:(firstRow-previousRow)*stride-metrics.gap});pieces.push({kind:"cards",...range});previousRow=Math.ceil(range.end/metrics.columns);}
 const rows=Math.ceil(plan.loaded/metrics.columns);if(previousRow<rows)pieces.push({kind:"spacer",key:`gap-${previousRow}-${rows}`,height:(rows-previousRow)*stride-metrics.gap});return pieces;
}
export function digitalWindowFocusStart(index:number,start:number,loaded:number,metrics:StoreDigitalGridMetrics|null):number{
 const plan=digitalWindowPlan(loaded,start,metrics);if(!metrics)return 0;
 if(index<plan.start||index>=plan.end)return digitalWindowPlan(loaded,index-plan.capacity/2,metrics).start;
 const margin=Math.max(12,metrics.columns*2);
 if(index>=plan.end-margin&&plan.end<loaded)return digitalWindowPlan(loaded,plan.start+48,metrics).start;
 if(index<plan.start+margin&&plan.start>0)return digitalWindowPlan(loaded,plan.start-48,metrics).start;
 return plan.start;
}
export function readDigitalGridMetrics(grid:HTMLElement):StoreDigitalGridMetrics|null{
 const card=grid.querySelector<HTMLElement>(".ph-sd-game"),rect=card?.getBoundingClientRect();if(!rect||rect.height<=0||rect.width<=0)return null;
 const style=grid.ownerDocument.defaultView!.getComputedStyle(grid),tracks=style.gridTemplateColumns.match(/[\d.]+px/g);
 const columns=tracks?.length||Math.max(1,Math.round(grid.clientWidth/rect.width)),gap=parseFloat(style.rowGap)||0;
 return{columns,rowHeight:rect.height,gap};
}
