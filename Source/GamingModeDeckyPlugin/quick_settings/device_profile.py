"""Local device identity and the user's Control Center presentation preference."""
import json
import os
import re
import tempfile
import threading
from pathlib import Path


def _identifier(value):
    return re.sub(r"[^a-z0-9]", "", value.lower()) if isinstance(value, str) else ""


# Model identifiers are presentation hints, never permission to control firmware.
# They come from OEM model listings and publicly documented SMBIOS identities.
_VENDORS = {
    "asus": ("ASUSTeK COMPUTER INC.", "ASUS"),
    "lenovo": ("LENOVO",),
    "msi": ("Micro-Star International Co., Ltd.", "MSI"),
    "valve": ("Valve", "Valve Corporation"),
    "ayaneo": ("AYA", "AYANEO", "AYADEVICE"),
    "ayn": ("AYN",),
    "gpd": ("GPD",),
    "onexplayer": ("ONE-NETBOOK", "ONE-NETBOOK TECHNOLOGY CO., LTD.", "ONEXPLAYER"),
    "aokzoe": ("AOKZOE",),
    "acer": ("Acer", "Acer Incorporated"),
    "zotac": ("PC Partner Limited", "ZOTAC"),
}

_HANDHELDS = (
    ("rog-ally", "ROG Ally", "asus", ("RC71L", "ROG Ally RC71L_RC71L")),
    ("rog-ally-x", "ROG Ally X", "asus", ("RC72LA", "ROG Ally X RC72LA_RC72LA")),
    ("rog-xbox-ally", "ROG Xbox Ally", "asus", ("RC73YA", "ROG Xbox Ally RC73YA_RC73YA")),
    ("rog-xbox-ally-x", "ROG Xbox Ally X", "asus", ("RC73XA", "ROG Xbox Ally X RC73XA_RC73XA")),
    ("rog-xbox-ally-x20", "ROG Xbox Ally X20", "asus", ("RC74XA",)),
    ("legion-go", "Legion Go", "lenovo", ("83E1", "Legion Go 8APU1")),
    ("legion-go-s", "Legion Go S", "lenovo", ("83L3", "83N6", "83Q2", "83Q3", "Legion Go S 8ARP1", "Legion Go S 8APU1")),
    ("legion-go-2", "Legion Go 2", "lenovo", ("83N0", "83N1", "Legion Go 8ASP2", "Legion Go 8AHP2")),
    ("msi-claw", "MSI Claw", "msi", ("MS-1T41", "Claw A1M")),
    ("msi-claw-7-ai-plus", "MSI Claw 7 AI+", "msi", ("MS-1T42", "Claw 7 AI+ A2VM")),
    ("msi-claw-8-ai-plus", "MSI Claw 8 AI+", "msi", ("MS-1T52", "Claw 8 AI+ A2VM")),
    ("msi-claw-a8", "MSI Claw A8", "msi", ("MS-1T8K", "Claw A8 BZ2EM")),
    ("msi-claw-8-ex", "MSI Claw 8 EX AI+", "msi", ("MS-1T91", "Claw 8 EX AI+ CG3EM")),
    ("steam-deck-lcd", "Steam Deck LCD", "valve", ("Jupiter",)),
    ("steam-deck-oled", "Steam Deck OLED", "valve", ("Galileo",)),
    ("ayaneo", "AYANEO", "ayaneo", (
        "AIR", "AIR Pro", "AIR Lite", "AIR 1S", "AIR 1S Limited", "AIR 2", "AB10", "AIR Pro 1S",
        "AYANEO AIR Pro 1S", "AYANEO AIR 2", "AYA NEO FOUNDER",
        "AYANEO 2021", "AYANEO 2021 Pro", "AYANEO 2021 Pro Retro Power", "AYANEO 2", "AYANEO 2S", "GEEK", "GEEK 1S", "KUN",
        "NEXT", "NEXT Pro", "NEXT Advance", "NEXT Lite", "AS01", "FLIP DS", "FLIP KB",
    )),
    ("ayaneo-air-plus", "AYANEO AIR Plus", "ayaneo", ("AB05-AMD", "AB05-Mendocino", "AB05-Intel", "AB05N", "AB05NXX", "AIR Plus Nxx")),
    # These two retail names share the same manufacturer and model in firmware.
    ("ayaneo-slide", "AYANEO SLIDE / Antec Core HS", "ayaneo", ("SLIDE",)),
    ("ayaneo-3", "AYANEO 3", "ayaneo", ("AYANEO 3", "AB07")),
    ("ayaneo-next-2", "AYANEO NEXT II", "ayaneo", ("NEXT II", "NEXT 2", "AYANEO NEXT II", "AYANEO NEXT 2", "AB09")),
    ("ayaneo-flip-1s", "AYANEO FLIP 1S", "ayaneo", ("FLIP 1S DS", "FLIP 1S KB")),
    ("ayaneo-flip-11", "AYANEO FLIP 11", "ayaneo", ("FLIP 11", "AYANEO FLIP 11")),
    ("ayn-loki-mini-pro", "AYN Loki Mini Pro", "ayn", ("Loki MiniPro",)),
    ("ayn-loki-zero", "AYN Loki Zero", "ayn", ("Loki Zero",)),
    ("ayn-loki-max", "AYN Loki Max", "ayn", ("Loki Max",)),
    ("gpd-win", "GPD WIN", "gpd", ()),
    ("gpd-win-2", "GPD WIN 2", "gpd", ("WIN2",)),
    ("gpd-win-3", "GPD WIN 3", "gpd", ("G1618-03",)),
    ("gpd-win-4", "GPD WIN 4", "gpd", ("G1618-04",)),
    ("gpd-win-5", "GPD WIN 5", "gpd", ("G1618-05",)),
    ("gpd-win-mini", "GPD WIN Mini", "gpd", ("G1617-01", "G1617-02")),
    ("gpd-win-max", "GPD WIN Max", "gpd", ("G1619-01",)),
    ("gpd-win-max-2", "GPD WIN Max 2", "gpd", ("G1619-03", "G1619-04", "G1619-05")),
    # Early Mini revisions reuse these system names across different hardware.
    ("onexplayer", "ONEXPLAYER", "onexplayer", ("ONE XPLAYER", "ONEXPLAYER Mini Pro", "ONEXPLAYER Mini A07")),
    ("onexplayer-2", "ONEXPLAYER 2", "onexplayer", ("ONEXPLAYER 2 ARP23",)),
    ("onexplayer-2-pro", "ONEXPLAYER 2 Pro", "onexplayer", ("ONEXPLAYER 2 PRO ARP23P", "ONEXPLAYER 2 PRO ARP23P EVA-01")),
    ("onexplayer-3", "ONEXPLAYER 3", "onexplayer", ("ONEXPLAYER 3",)),
    ("onexplayer-apex", "ONEXPLAYER APEX", "onexplayer", ("ONEXPLAYER APEX",)),
    ("onexplayer-apex-i", "ONEXPLAYER Apex i", "onexplayer", ()),
    ("onexplayer-apex-air", "ONEXPLAYER Apex Air", "onexplayer", ()),
    ("onexplayer-super-v", "ONEXPLAYER SUPER V", "onexplayer", ()),
    ("onexplayer-g1", "ONEXPLAYER G1", "onexplayer", ("ONEXPLAYER G1 i", "ONEXPLAYER G1 A")),
    ("onexplayer-x1", "ONEXPLAYER X1", "onexplayer", ("ONEXPLAYER X1 i", "ONEXPLAYER X1 A", "ONEXPLAYER X1z")),
    ("onexplayer-x1-mini", "ONEXPLAYER X1 Mini", "onexplayer", ("ONEXPLAYER X1 Mini",)),
    ("onexplayer-x1-pro", "ONEXPLAYER X1 Pro", "onexplayer", ("ONEXPLAYER X1Pro",)),
    ("onexplayer-x2", "ONEXPLAYER X2", "onexplayer", ("ONEXPLAYER X2",)),
    ("onexplayer-x2-mini", "ONEXPLAYER X2 Mini", "onexplayer", ()),
    ("onexplayer-x2-mini-pro", "ONEXPLAYER X2 Mini Pro", "onexplayer", ("ONEXPLAYER X2Mini PRO",)),
    ("onexfly-f1", "OneXFly F1", "onexplayer", ("ONEXPLAYER F1",)),
    ("onexfly-f1-pro", "OneXFly F1 Pro", "onexplayer", ("ONEXPLAYER F1Pro",)),
    ("aokzoe", "AOKZOE", "aokzoe", ("AOKZOE A1 AR07", "AOKZOE A1 Pro", "AOKZOE A1X", "AOKZOE A2 Pro")),
    ("acer-nitro-blaze", "Acer Nitro Blaze", "acer", ("GN771", "GN782U", "GN7112U", "Nitro Blaze 7", "Nitro Blaze 8", "Nitro Blaze 11")),
    ("zotac-zone", "ZOTAC ZONE", "zotac", ("G0A1W", "ZGC-G1A1W")),
)
_PROFILES = {profile: label for profile, label, _, _ in _HANDHELDS}
_VENDOR_IDS = {_identifier(name): vendor for vendor, names in _VENDORS.items() for name in names}
_MATCHES = {
    (vendor, _identifier(model)): profile
    for profile, _, vendor, models in _HANDHELDS
    for model in models
}
# The Intel X2 Mini identifies itself by its board. The AMD Pro has a distinct model.
_BOARD_MATCHES = {("onexplayer", _identifier(product)): profile for product, profile in (
    ("ONEXPLAYER X2Mini", "onexplayer-x2-mini"), ("ONEXPLAYER X2", "onexplayer-x2"),
    ("ONEXPLAYER X2 EVA", "onexplayer-x2"), ("ONEXPLAYER 3", "onexplayer-3"),
    ("ONEXPLAYER Apex i", "onexplayer-apex-i"), ("ONEXPLAYER Apex Air", "onexplayer-apex-air"),
    ("ONEXPLAYER SUPER V", "onexplayer-super-v"))}
_ALLOWED = {"auto", "pc", "handheld", *_PROFILES}
_OEM_DEFAULTS = {"", "defaultstring", "systemmanufacturer", "systemproductname", "tobefilledbyoem", "notapplicable", "unknown"}


def _identity_value(value):
    return value.strip()[:160] if _identifier(value) not in _OEM_DEFAULTS else ""


def detect_profile(identity):
    vendor = _VENDOR_IDS.get(_identifier(identity.get("manufacturer")))
    # Exact system model takes precedence over the broader system family.
    for key in ("model", "family"):
        profile = _MATCHES.get((vendor, _identifier(identity.get(key))))
        if profile:
            return profile
    board_vendor = _VENDOR_IDS.get(_identifier(identity.get("board_manufacturer")))
    if vendor and board_vendor == vendor:
        return _BOARD_MATCHES.get((board_vendor, _identifier(identity.get("board_model"))), "pc")
    return "pc"


class DeviceProfiles:
    def __init__(self, directory, read_bios, read_cim):
        self._path = Path(directory) / "device-profile.json"
        self._read_bios = read_bios
        self._read_cim = read_cim
        self._identity = None
        self._lock = threading.Lock()

    def identity(self):
        with self._lock:
            if self._identity is None:
                bios = self._read_bios()
                identity = {key: _identity_value(bios.get(key)) for key in ("manufacturer", "model", "family")}
                if not identity["manufacturer"] or not identity["model"]:
                    cim = self._read_cim()
                    for key, value in identity.items():
                        if not value:
                            identity[key] = _identity_value(cim.get(key))
                for key in ("board_manufacturer", "board_model", "version"):
                    identity[key] = _identity_value(bios.get(key))
                self._identity = identity
            return self._identity.copy()

    def _get_preference(self):
        try:
            with self._path.open(encoding="utf-8") as stream:
                record = json.loads(stream.read(1025))
            profile = record.get("profile") if isinstance(record, dict) else None
            if isinstance(profile, str) and profile in _ALLOWED:
                return profile
        except (OSError, ValueError):
            pass
        return "auto"

    def get(self):
        identity = self.identity()
        detected = detect_profile(identity)
        with self._lock:
            selected = self._get_preference()
        effective = detected if selected == "auto" else selected
        return {
            "selection": selected,
            "detected": detected,
            "effective": effective,
            "manufacturer": identity["manufacturer"],
            "model": identity["model"],
            "handhelds": [{"id": profile, "name": label} for profile, label in _PROFILES.items()],
        }

    def save(self, profile):
        if not isinstance(profile, str) or profile not in _ALLOWED:
            raise ValueError("Invalid device profile")
        with self._lock:
            self._path.parent.mkdir(parents=True, exist_ok=True)
            temporary = None
            try:
                with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", dir=self._path.parent,
                                                 prefix=".device-profile-", delete=False) as stream:
                    temporary = Path(stream.name)
                    json.dump({"profile": profile}, stream)
                os.replace(temporary, self._path)
            finally:
                if temporary is not None:
                    temporary.unlink(missing_ok=True)
