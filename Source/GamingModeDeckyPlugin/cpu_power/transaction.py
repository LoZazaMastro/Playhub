"""Single-owner PPT transactions. No native code, driver loading or auto-apply."""

from dataclasses import dataclass
from decimal import Decimal, InvalidOperation
import threading
import time
import uuid

from .recovery import RecoveryJournalError


@dataclass(frozen=True)
class Snapshot:
    device_id: str
    firmware_id: str
    active_ppt_mw: int
    minimum_ppt_mw: int
    maximum_ppt_mw: int
    step_mw: int
    limits_source: str
    readback_source: str
    observed_at: float

    def validate(self, now):
        values = (self.active_ppt_mw, self.minimum_ppt_mw,
                  self.maximum_ppt_mw, self.step_mw)
        if any(type(value) is not int or value <= 0 for value in values):
            raise ValueError("invalid_processor_limits")
        if not self.minimum_ppt_mw <= self.active_ppt_mw <= self.maximum_ppt_mw:
            raise ValueError("active_limit_outside_documented_range")
        if not all(isinstance(value, str) and value.strip() for value in (
                self.device_id, self.firmware_id, self.limits_source, self.readback_source)):
            raise ValueError("unverified_processor_contract")
        if not 0 <= now - self.observed_at <= 2:
            raise ValueError("stale_readback")

    @property
    def contract(self):
        return (self.device_id, self.firmware_id, self.minimum_ppt_mw,
                self.maximum_ppt_mw, self.step_mw, self.limits_source,
                self.readback_source)


def watts_to_mw(value):
    if isinstance(value, bool) or not isinstance(value, (str, int, float, Decimal)):
        raise ValueError("invalid_watts")
    try:
        number = Decimal(str(value)) * 1000
        if not number.is_finite() or not 0 < number <= 0xFFFFFFFF or number != number.to_integral_value():
            raise ValueError("invalid_watts")
        return int(number)
    except (InvalidOperation, OverflowError):
        raise ValueError("invalid_watts") from None


class PptController:
    """The provider is trusted integration code, never a frontend-supplied object.

    It must expose an independently refreshed snapshot(), set_ppt_mw(int) returning
    a literal bool, and exclusive() holding the cross-process hardware mutex over
    the WHOLE transaction. Constructors and snapshot must never set tuning values.
    Persisting/replaying profiles is deliberately outside this controller.
    """

    def __init__(self, provider=None, clock=time.monotonic, journal=None, limits=None):
        self._provider = provider
        self._clock = clock
        self._lock = threading.RLock()
        self._owned = None
        self._uncertain = False
        # Journal di ripristino (cpu_power.recovery.PptRecoveryJournal). Se e'
        # None la transazione resta valida ma NON sopravvive a un crash: e' una
        # scelta del chiamante, non un default silenzioso.
        self._journal = journal
        # Riga della tabella famiglie (cpu_power.capability.family_limits_mw).
        # None = nessun clamp di famiglia configurato.
        self._limits = limits
        self._token = None

    def _read(self):
        snapshot = self._provider.snapshot()
        if not isinstance(snapshot, Snapshot):
            raise ValueError("invalid_snapshot")
        snapshot.validate(self._clock())
        return snapshot

    def status(self):
        with self._lock:
            result = {"supported_read": False, "supported_write": False,
                      "observed_ppt_w": None, "range": None,
                      "hardware_accessed": False, "reason": "verified_ppt_provider_missing"}
            if self._provider is None:
                return result
            try:
                result["hardware_accessed"] = True
                with self._provider.exclusive():
                    current = self._read()
                writable = not self._uncertain and self._owned is None
                return {**result, "supported_read": True, "supported_write": writable,
                        "observed_ppt_w": current.active_ppt_mw / 1000,
                        "device_id": current.device_id, "firmware_id": current.firmware_id,
                        "limits_source": current.limits_source, "readback_source": current.readback_source,
                        "observed_at_monotonic": current.observed_at,
                        "range": {"min": current.minimum_ppt_mw / 1000,
                                  "max": current.active_ppt_mw / 1000,
                                  "step": current.step_mw / 1000, "unit": "W"},
                        "restore_required": self._owned is not None,
                        "manual_recovery_required": self._uncertain,
                        "reason": ("manual_recovery_required" if self._uncertain else
                                   "restore_previous_transaction_first" if self._owned else "ready")}
            except Exception as error:
                return {**result, "reason": "ppt_readback_failed", "error_type": type(error).__name__}

    def apply(self, watts, *, authorized=False):
        with self._lock:
            if authorized is not True:
                return self._failure("explicit_apply_required")
            if self._provider is None:
                return self._failure("verified_ppt_provider_missing")
            if self._uncertain:
                return self._failure("manual_recovery_required")
            if self._owned:
                return self._failure("restore_previous_transaction_first")
            try:
                target = watts_to_mw(watts)
                family = self._family_failure(target)
                if family:
                    return family
                with self._provider.exclusive():
                    before = self._read()
                    if not before.minimum_ppt_mw <= target <= before.maximum_ppt_mw:
                        return self._failure("outside_documented_processor_limits")
                    if (target - before.minimum_ppt_mw) % before.step_mw:
                        return self._failure("invalid_processor_step")
                    # Initial release is reduction-only. Restoring this captured
                    # baseline is the sole allowed power increase.
                    if target > before.active_ppt_mw:
                        return self._failure("power_increase_not_allowed")
                    if target == before.active_ppt_mw:
                        return {"ok": True, "changed": False, "observed_ppt_w": target / 1000}
                    preflight = self._read()
                    if preflight.contract != before.contract or preflight.active_ppt_mw != before.active_ppt_mw:
                        return self._failure("concurrent_change")
                    self._token = uuid.uuid4().hex
                    # Il baseline finisce su disco PRIMA di toccare l'hardware:
                    # se il processo muore adesso, l'avvio successivo lo rimette.
                    journal_error = self._write_journal("applying", before, target)
                    if journal_error:
                        return journal_error
                    self._owned = (before, target)
                    accepted = False
                    try:
                        accepted = self._provider.set_ppt_mw(target) is True
                        after = self._read()
                    except Exception as error:
                        self._uncertain = True
                        return self._failure("apply_state_unknown", error)
                    if after.contract != before.contract:
                        self._uncertain = True
                        return self._failure("processor_contract_changed")
                    if accepted and after.active_ppt_mw == target:
                        # Successo dichiarato solo dopo il readback: qui after e'
                        # stato riletto dal getter indipendente e coincide.
                        self._write_journal("applied", before, target)
                        return {"ok": True, "changed": True, "observed_ppt_w": target / 1000,
                                "baseline_ppt_w": before.active_ppt_mw / 1000,
                                "restore_required": True}
                    if after.active_ppt_mw == before.active_ppt_mw:
                        # Riletto il valore di partenza: la scrittura non e'
                        # avvenuta. Nessun successo, e il journal si chiude.
                        self._owned = None
                        self._clear_journal()
                        return self._failure("apply_not_confirmed")
                    if after.active_ppt_mw == target:
                        # Setter rejected but the independent getter saw the
                        # target: restore only a value we can still attribute.
                        restored = self._restore_locked()
                        return {**self._failure("apply_rejected"), "rollback": restored}
                    self._uncertain = True
                    return self._failure("unexpected_readback_manual_recovery")
            except Exception as error:
                if self._owned is not None:
                    self._uncertain = True
                return self._failure("preflight_failed", error)

    def restore(self, *, authorized=False):
        with self._lock:
            if authorized is not True:
                return self._failure("explicit_restore_required")
            if self._uncertain:
                return self._failure("manual_recovery_required")
            if self._owned is None:
                return {"ok": True, "changed": False}
            try:
                with self._provider.exclusive():
                    return self._restore_locked()
            except Exception as error:
                self._uncertain = True
                return self._failure("restore_state_unknown", error)

    def _restore_locked(self):
        before, target = self._owned
        current = self._read()
        if current.contract != before.contract or current.active_ppt_mw != target:
            self._owned = None
            self._uncertain = True
            return self._failure("external_change_restore_skipped")
        try:
            accepted = self._provider.set_ppt_mw(before.active_ppt_mw) is True
            restored = self._read()
        except Exception as error:
            self._uncertain = True
            return self._failure("restore_state_unknown", error)
        if not accepted or restored.contract != before.contract or restored.active_ppt_mw != before.active_ppt_mw:
            self._uncertain = True
            return self._failure("restore_not_confirmed")
        self._owned = None
        # Baseline riletto e confermato: la transazione e' chiusa, il journal
        # non deve piu' far ripartire niente al prossimo avvio.
        self._clear_journal()
        return {"ok": True, "changed": True, "observed_ppt_w": restored.active_ppt_mw / 1000}

    # ------------------------------------------------------------ tabella famiglie
    def _family_failure(self, target_mw):
        """None se la famiglia ammette questo limite, altrimenti il rifiuto."""
        limits = self._limits
        if limits is None:
            return None
        if not limits.get("verified"):
            return self._failure(limits.get("reason") or "cpu_family_backend_unverified")
        minimum = limits.get("minimum_mw")
        maximum = limits.get("maximum_mw")
        if not isinstance(minimum, int) or not isinstance(maximum, int) or minimum > maximum:
            return self._failure("cpu_family_table_invalid")
        if not minimum <= target_mw <= maximum:
            return self._failure("outside_family_table_limits")
        return None

    # ------------------------------------------------------------------ journal
    def _write_journal(self, state, before, target):
        """None se il journal e' a posto, altrimenti il fallimento da restituire.

        Se il baseline non si riesce a scrivere NON si tocca l'hardware: meglio
        non applicare niente che applicare un limite che nessuno sa ripristinare.
        """
        if self._journal is None:
            return None
        record = {"schema_version": 1, "state": state, "token": self._token,
                  "device_id": before.device_id, "firmware_id": before.firmware_id,
                  "baseline_ppt_mw": before.active_ppt_mw, "target_ppt_mw": target}
        try:
            self._journal.save(record)
            return None
        except (RecoveryJournalError, OSError) as error:
            if state == "applying":
                self._owned = None
                return self._failure("recovery_journal_unwritable", error)
            # Il limite e' gia' applicato: non si puo' tornare indietro in
            # silenzio, ma va detto che il ripristino automatico non c'e'.
            self._uncertain = True
            return None

    def _clear_journal(self):
        self._token = None
        if self._journal is None:
            return
        try:
            self._journal.clear()
        except OSError:
            # Un journal che non si cancella fara' ritentare un ripristino
            # gia' fatto: recover() rilegge il valore attuale, lo trova gia' al
            # baseline e non applica niente. Nessun danno, nessuna bugia.
            pass

    def recover(self, *, authorized=False):
        """Riapplica il baseline lasciato da una sessione finita male.

        Chiamata all'avvio. Rilegge il valore attuale, riapplica il baseline e
        RILEGGE ancora: senza riscontro il ripristino e' fallito e lo dice.
        Il record resta su disco finche' il ripristino non e' confermato, cosi'
        un avvio andato storto puo' riprovare al successivo.
        """
        with self._lock:
            if authorized is not True:
                return {"ok": False, "reason": "explicit_recovery_required"}
            if self._journal is None:
                return {"ok": False, "reason": "recovery_journal_missing"}
            try:
                record = self._journal.load()
            except RecoveryJournalError as error:
                return {"ok": False, "reason": str(error) or "recovery_record_unreadable"}
            if record is None:
                return {"ok": True, "changed": False, "reason": "no_recovery_pending"}
            if record["state"] == "completed":
                self._journal.clear()
                return {"ok": True, "changed": False, "reason": "recovery_already_completed"}
            if self._provider is None:
                # Senza backend verificato non si tocca niente e il record resta:
                # il ripristino verra' ritentato quando il backend ci sara'.
                return {"ok": False, "reason": "verified_ppt_provider_missing",
                        "recovery_pending": True}
            baseline = record["baseline_ppt_mw"]
            try:
                with self._provider.exclusive():
                    current = self._read()
                    if (current.device_id != record["device_id"]
                            or current.firmware_id != record["firmware_id"]):
                        # Un altro processore: il baseline non gli appartiene.
                        self._journal.clear()
                        return {"ok": False, "reason": "recovery_hardware_changed"}
                    if current.active_ppt_mw == baseline:
                        self._journal.clear()
                        return {"ok": True, "changed": False, "reason": "already_at_baseline"}
                    if not current.minimum_ppt_mw <= baseline <= current.maximum_ppt_mw:
                        return {"ok": False, "reason": "recovery_baseline_outside_limits"}
                    accepted = self._provider.set_ppt_mw(baseline) is True
                    restored = self._read()
                    if not accepted or restored.active_ppt_mw != baseline:
                        self._uncertain = True
                        return {"ok": False, "reason": "recovery_not_confirmed",
                                "observed_ppt_w": restored.active_ppt_mw / 1000,
                                "recovery_pending": True}
                    self._journal.clear()
                    self._token = None
                    return {"ok": True, "changed": True, "reason": "recovered",
                            "observed_ppt_w": baseline / 1000}
            except Exception as error:
                self._uncertain = True
                return {"ok": False, "reason": "recovery_state_unknown",
                        "error_type": type(error).__name__, "recovery_pending": True}

    def _failure(self, reason, error=None):
        result = {"ok": False, "reason": reason, "manual_recovery_required": self._uncertain,
                  "restore_required": self._owned is not None}
        if error:
            result["error_type"] = type(error).__name__
        return result
