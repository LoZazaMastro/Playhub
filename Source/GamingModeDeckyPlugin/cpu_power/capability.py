"""Original passive Windows inventory; no HC code or SMU command tables."""

import os
import re
import threading




# ---------------------------------------------------------------------------
# Tabella famiglia CPU -> intervallo di PPT ammesso.
#
# Sostituisce l'allowlist di un solo processore. Ogni riga dichiara:
#   vendor/family/modelli   identita' CPUID, come la espone Windows;
#   codename                nome leggibile della famiglia;
#   minimum_w / maximum_w   CLAMP ESTERNO conservativo: nessuna richiesta fuori
#                           da questo intervallo viene mai inoltrata, qualunque
#                           cosa dica l'hardware. Non e' una misura del PPT
#                           reale: i limiti veri restano quelli letti a runtime,
#                           e vale sempre il piu' stretto dei due;
#   step_w                  granularita' minima ammessa;
#   backend                 quale ponte servirebbe (mailbox SMU AMD, MSR RAPL Intel);
#   verified                False finche' quel backend non e' stato verificato
#                           SU QUELLA FAMIGLIA. Una riga non verificata NON
#                           abilita niente: produce un rifiuto motivato.
#
# Fuori tabella si rifiuta con "cpu_not_allowlisted". Dentro tabella ma non
# verificata si rifiuta con "cpu_family_backend_unverified". In nessuno dei due
# casi si accende qualcosa di nuovo per default.
# ---------------------------------------------------------------------------

class FamilyEntry:
    __slots__ = ("vendor", "family", "models", "codename", "minimum_w",
                 "maximum_w", "step_w", "backend", "verified", "note")

    def __init__(self, vendor, family, models, codename, minimum_w, maximum_w,
                 step_w, backend, verified, note):
        self.vendor = vendor
        self.family = family
        self.models = models
        self.codename = codename
        self.minimum_w = minimum_w
        self.maximum_w = maximum_w
        self.step_w = step_w
        self.backend = backend
        self.verified = verified
        self.note = note

    def matches(self, vendor, family, model):
        return (vendor == self.vendor and family == self.family
                and self.models[0] <= model <= self.models[1])

    def as_dict(self):
        return {"codename": self.codename, "backend": self.backend,
                "verified": self.verified, "note": self.note,
                "minimum_w": self.minimum_w, "maximum_w": self.maximum_w,
                "step_w": self.step_w}


FAMILY_TABLE = (
    # Zen 4 desktop/mobile-HX: e' l'unica famiglia su cui il ponte PawnIO/SMU e'
    # stato istruito. Resta comunque subordinata alla presenza del driver.
    FamilyEntry("AuthenticAMD", 0x19, (0x60, 0x6F), "raphael_or_dragon_range",
                35, 170, 1, "amd_smu_mailbox", True,
                "Zen 4 desktop (Raphael) e mobile HX (Dragon Range)."),
    # Le righe seguenti esistono per essere estese con prove, non per abilitare:
    # verified=False significa rifiuto motivato, non tentativo.
    FamilyEntry("AuthenticAMD", 0x19, (0x20, 0x2F), "vermeer",
                35, 142, 1, "amd_smu_mailbox", False,
                "Zen 3 desktop: mailbox SMU diversa, mai verificata qui."),
    FamilyEntry("AuthenticAMD", 0x19, (0x70, 0x7F), "phoenix",
                4, 54, 1, "amd_smu_mailbox", False,
                "APU mobile Zen 4: limiti STAPM/PPT distinti, non verificati."),
    FamilyEntry("AuthenticAMD", 0x1A, (0x40, 0x4F), "granite_ridge",
                35, 200, 1, "amd_smu_mailbox", False,
                "Zen 5 desktop: tabella PM e mailbox non verificate."),
    FamilyEntry("GenuineIntel", 0x06, (0x00, 0xFF), "intel_rapl",
                10, 250, 1, "intel_msr_rapl", False,
                "Intel passa dagli MSR RAPL: backend separato, non implementato."),
)


def lookup_family(vendor, family, model):
    """Riga della tabella per questa CPU, oppure None se fuori tabella."""
    for entry in FAMILY_TABLE:
        if entry.matches(vendor, family, model):
            return entry
    return None



def _inventory():
    if os.name != "nt":
        return {"platform": "unsupported"}
    import winreg

    result = {"platform": "windows", "cpu": {}, "driver": {}}
    with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE,
                        r"HARDWARE\DESCRIPTION\System\CentralProcessor\0") as key:
        for source, target in (("VendorIdentifier", "vendor"),
                               ("Identifier", "identifier"),
                               ("ProcessorNameString", "name")):
            result["cpu"][target] = str(winreg.QueryValueEx(key, source)[0]).strip()
    try:
        with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE,
                            r"SYSTEM\CurrentControlSet\Services\PawnIO") as key:
            result["driver"] = {
                "registered": True,
                "service_type": winreg.QueryValueEx(key, "Type")[0],
                "image_path": winreg.QueryValueEx(key, "ImagePath")[0],
            }
    except FileNotFoundError:
        result["driver"] = {"registered": False}
    return result


class PassiveAdapter:
    """Serialize a single metadata provider; never opens the driver device.

    Registry reads are local and bounded in number, not cancellable operations.
    A registered service is not evidence of its signature, running state or ABI.
    """

    def __init__(self, inventory=None):
        self._inventory = inventory or _inventory
        self._lock = threading.Lock()
        self._closed = False

    def close(self):
        with self._lock:
            self._closed = True

    def get_status(self):
        with self._lock:
            if self._closed:
                raise RuntimeError("adapter_closed")
            status = {
                "schema_version": 1, "probe": "passive_registry",
                "hardware_accessed": False,
                "supported_read": False, "supported_write": False,
                "observed_ppt_w": None,
                "reason": "platform_unsupported",
            }
            try:
                data = self._inventory()
                status["inventory"] = data
                if data.get("platform") != "windows":
                    return status
                cpu = data.get("cpu", {})
                if re.fullmatch(r"AMD Ryzen 9 7900X(?: 12-Core Processor)?", cpu.get("name", "")):
                    # AMD product specification, not PPT telemetry or a slider range.
                    status["rated_tdp_w"] = 170
                    status["rated_tdp_source"] = (
                        "https://www.amd.com/en/products/processors/desktops/ryzen/"
                        "7000-series/amd-ryzen-9-7900x.html")
                # Windows Identifier is CPUID-derived, but does not expose the
                # package-type distinction between Raphael and Dragon Range.
                words = cpu.get("identifier", "").split()
                family = int(words[words.index("Family") + 1])
                model = int(words[words.index("Model") + 1])
                entry = lookup_family(cpu.get("vendor"), family, model)
                status["cpu_family"] = {"vendor": cpu.get("vendor"),
                                        "family": family, "model": model}
                status["cpu_candidate"] = entry.codename if entry else "not_allowlisted"
                if entry is not None:
                    status["family_entry"] = entry.as_dict()
                    status["range"] = {"min": entry.minimum_w, "max": entry.maximum_w,
                                       "step": entry.step_w, "unit": "W"}
                if entry is None:
                    # Fuori tabella: si dice quale CPU e' stata vista, non solo "no".
                    status["reason"] = "cpu_not_allowlisted"
                elif not entry.verified:
                    # In tabella ma senza backend verificato per questa famiglia.
                    status["reason"] = "cpu_family_backend_unverified"
                elif not data.get("driver", {}).get("registered"):
                    status["reason"] = "pawnio_service_missing"
                elif data["driver"].get("service_type") != 1:
                    status["reason"] = "pawnio_service_type_invalid"
                else:
                    status["reason"] = "driver_signature_abi_and_pmt_unverified"
                return status
            except (OSError, ValueError, KeyError, TypeError, AttributeError) as exc:
                status["reason"] = "inventory_failed"
                status["error_type"] = type(exc).__name__
                return status


def get_status():
    return PassiveAdapter().get_status()


def family_limits_mw(vendor, family, model):
    """Clamp esterno in milliwatt per questa CPU, o None se fuori tabella.

    Restituisce anche verified: chi applica un limite deve rifiutare se e' False.
    """
    entry = lookup_family(vendor, family, model)
    if entry is None:
        return None
    return {"codename": entry.codename, "verified": entry.verified,
            "minimum_mw": entry.minimum_w * 1000,
            "maximum_mw": entry.maximum_w * 1000,
            "step_mw": entry.step_w * 1000}


def current_family_limits(inventory=None):
    """Clamp di famiglia per la CPU di QUESTA macchina.

    Non solleva mai: fuori tabella o identita' illeggibile diventano una
    sentinella con verified=False e la ragione, cosi' chi applica un limite
    fallisce in modo motivato invece di procedere al buio.
    """
    try:
        data = (inventory or _inventory)()
        if data.get("platform") != "windows":
            return {"verified": False, "reason": "platform_unsupported"}
        cpu = data.get("cpu", {})
        words = cpu.get("identifier", "").split()
        family = int(words[words.index("Family") + 1])
        model = int(words[words.index("Model") + 1])
    except (OSError, ValueError, KeyError, TypeError, AttributeError):
        return {"verified": False, "reason": "inventory_failed"}
    limits = family_limits_mw(cpu.get("vendor"), family, model)
    if limits is None:
        return {"verified": False, "reason": "cpu_not_allowlisted"}
    if not limits.get("verified"):
        limits = {**limits, "reason": "cpu_family_backend_unverified"}
    return limits
