"""Independent Playhub QAM preferences, compatible with prior Shortcuts data."""
import json
import math
import os
import threading
from pathlib import Path


def normalize(value):
    value = value if isinstance(value, dict) else {}
    def names(key, prefixes=None):
        result = []
        for item in value.get(key, []) if isinstance(value.get(key), list) else []:
            if isinstance(item, str) and 0 < len(item) <= 384 and item not in result:
                if prefixes is None or item.startswith(prefixes):
                    result.append(item)
            if len(result) >= 256:
                break
        return result
    icons = value.get('icons', {})
    icons = {k:v for k,v in icons.items() if isinstance(k, str) and len(k)<=384
             and isinstance(v,str) and 0<len(v)<=128} if isinstance(icons, dict) else {}
    timestamp = value.get('updated_at', value.get('updatedAt', 0))
    if isinstance(timestamp, bool) or not isinstance(timestamp, (float,int)) or not math.isfinite(timestamp):
        timestamp = 0
    return {'version':3, 'selected':names('selected'), 'icons':icons,
            'order':names('order', ('steam:','shortcut:','decky:')),
            'hidden':names('hidden', ('steam:',)), 'updated_at':max(0,min(timestamp,1e16))}


class QamPreferences:
    def __init__(self, directory):
        self.directory = Path(directory)
        self.path = self.directory / 'qam-layout.json'
        self.lock = threading.RLock()

    def _read(self, path):
        try:
            if path.stat().st_size > 1024*1024:
                return None
            data = json.loads(path.read_text('utf-8-sig'))
            return normalize(data) if isinstance(data, dict) else None
        except (OSError, ValueError):
            return None

    def get(self):
        with self.lock:
            for path in (self.path, self.path.with_suffix('.json.bak')):
                current = self._read(path)
                if current is not None:
                    return {**current, 'exists':True}
            # Read compatibility data only from the named legacy settings folder.
            # Never delete it; the installer archives the old plugin separately.
            for folder in ('Shortcuts', 'shortcuts'):
                for filename in ('state.json', 'state.json.bak'):
                    legacy = self._read(self.directory.parent / folder / filename)
                    if legacy is not None:
                        return {**self.save(legacy), 'exists':True}
            return {**normalize({}), 'exists':False}

    def save(self, value):
        with self.lock:
            value = normalize(value)
            self.directory.mkdir(parents=True, exist_ok=True)
            if self._read(self.path) is not None:
                self.path.with_suffix('.json.bak').write_bytes(self.path.read_bytes())
            temporary = self.path.with_suffix('.json.tmp')
            temporary.write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8')
            os.replace(temporary,self.path)
            return {**value, 'exists':True}
