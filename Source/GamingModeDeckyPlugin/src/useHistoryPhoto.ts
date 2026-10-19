import { SP_REACT as React } from './decky';
import { historyPhotoIntegrity } from './historyImages';
import { loadHistoryPhoto } from './historyPhotoCache';

export function useHistoryPhoto(url?: string): string | undefined {
  const [resolved, setResolved] = React.useState<{source: string; digest: string; url: string}>();
  const expected = url ? historyPhotoIntegrity[url] : undefined;
  React.useEffect(() => {
    if (!url || !expected) return;
    let alive = true; let objectUrl: string | undefined;
    void loadHistoryPhoto(url, expected).then(blob => {
      if (!alive || !blob) return;
      objectUrl = URL.createObjectURL(blob);
      setResolved({source: url, digest: expected.sha256, url: objectUrl});
    });
    return () => { alive = false; if (objectUrl) URL.revokeObjectURL(objectUrl); };
  }, [url, expected?.sha256]);
  if (!url || !historyPhotoIntegrity[url]) return url;
  return resolved?.source === url && resolved.digest === expected?.sha256 ? resolved.url : undefined;
}
