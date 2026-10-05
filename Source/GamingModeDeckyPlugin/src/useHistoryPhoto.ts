import { SP_REACT as React } from './decky';
import { historyPhotoIntegrity } from './historyImages';
import { loadHistoryPhoto } from './historyPhotoCache';

export function useHistoryPhoto(url?: string): string | undefined {
  const [resolved, setResolved] = React.useState<{source: string; url: string}>();
  React.useEffect(() => {
    if (!url || !historyPhotoIntegrity[url]) return;
    let alive = true; let objectUrl: string | undefined;
    void loadHistoryPhoto(url, historyPhotoIntegrity[url]).then(blob => {
      if (!alive || !blob) return;
      objectUrl = URL.createObjectURL(blob);
      setResolved({source: url, url: objectUrl});
    });
    return () => { alive = false; if (objectUrl) URL.revokeObjectURL(objectUrl); };
  }, [url]);
  if (!url || !historyPhotoIntegrity[url]) return url;
  return resolved?.source === url ? resolved.url : undefined;
}
