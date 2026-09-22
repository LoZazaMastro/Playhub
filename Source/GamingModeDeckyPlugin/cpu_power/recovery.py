"""Journal di ripristino del limite PPT.

Stesso modello del journal del display (`display-recovery.json`): il valore
precedente viene scritto su disco PRIMA di toccare l'hardware, con scrittura
atomica (file temporaneo, fsync, os.replace). Se la sessione finisce male -
crash, blackout, chiusura forzata - all'avvio successivo il record e' ancora li'
e il baseline viene riapplicato.

Il journal non parla con l'hardware: legge, scrive e valida record. Chi lo usa
e' `PptController`.
"""

import json
import os


SCHEMA_VERSION = 1
# Stati possibili di una transazione, in ordine di vita:
#   applying  - baseline salvato, scrittura non ancora confermata
#   applied   - limite applicato e RILETTO: il baseline va ripristinato
#   completed - baseline gia' ripristinato: niente da fare all'avvio
STATES = ("applying", "applied", "completed")


class RecoveryJournalError(ValueError):
    """Record presente ma non utilizzabile: non si indovina, si dichiara."""


def _positive_int(value):
    return type(value) is int and value > 0


def validate(record):
    """Valida un record letto da disco. Solleva RecoveryJournalError se rotto."""
    if not isinstance(record, dict):
        raise RecoveryJournalError("recovery_record_not_an_object")
    if record.get("schema_version") != SCHEMA_VERSION:
        raise RecoveryJournalError("recovery_schema_unknown")
    if record.get("state") not in STATES:
        raise RecoveryJournalError("recovery_state_unknown")
    for key in ("device_id", "firmware_id", "token"):
        value = record.get(key)
        if not isinstance(value, str) or not value.strip():
            raise RecoveryJournalError("recovery_identity_missing")
    if record["state"] == "completed":
        return record
    if not _positive_int(record.get("baseline_ppt_mw")):
        raise RecoveryJournalError("recovery_baseline_invalid")
    if not _positive_int(record.get("target_ppt_mw")):
        raise RecoveryJournalError("recovery_target_invalid")
    # Il ripristino puo' solo RIALZARE il limite fino al baseline: un record che
    # chiede di scendere non e' un ripristino ed e' rifiutato.
    if record["target_ppt_mw"] > record["baseline_ppt_mw"]:
        raise RecoveryJournalError("recovery_target_above_baseline")
    return record


class PptRecoveryJournal:
    """Piccolo file JSON accanto alle impostazioni del plugin."""

    def __init__(self, path):
        self._path = path

    @property
    def path(self):
        return self._path

    def save(self, record):
        validate(record)
        directory = os.path.dirname(self._path)
        if directory:
            os.makedirs(directory, exist_ok=True)
        temporary = self._path + ".tmp"
        with open(temporary, "w", encoding="utf-8") as handle:
            json.dump(record, handle)
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(temporary, self._path)

    def load(self):
        """Record valido, oppure None se non c'e'. Record rotto -> eccezione."""
        try:
            with open(self._path, encoding="utf-8") as handle:
                record = json.load(handle)
        except FileNotFoundError:
            return None
        except (OSError, ValueError) as error:
            raise RecoveryJournalError("recovery_record_unreadable") from error
        return validate(record)

    def clear(self):
        try:
            os.remove(self._path)
        except FileNotFoundError:
            pass
