"""Conservative device policy and MSI static packets; see PROVENANCE.md."""

from dataclasses import dataclass
import hashlib


@dataclass(frozen=True)
class Device:
    path: str
    vid: int
    pid: int
    usage_page: int
    usage: int
    release: int
    output_length: int
    input_length: int = 0
    input_report_ids: tuple = ()
    output_report_ids: tuple = ()

    @property
    def id(self):
        return hashlib.sha256(self.path.casefold().encode("utf-8")).hexdigest()


MSI_ADDRESSES = {0x0163: 0x01FA, 0x0211: 0x01FA,
                 0x0166: 0x024A, 0x0217: 0x024A, 0x0308: 0x024A}
# Internal release gate, not a user setting. Offline tests patch it with fake I/O.
_MSI_PROTOCOL_VALIDATED = False
LEGION_PIDS = {0x6182, 0x6183, 0x6184, 0x6185,
               0x61EB, 0x61EC, 0x61ED, 0x61EE}


def classify(device):
    if (device.vid == 0x17EF and device.pid in LEGION_PIDS
            and (device.usage_page, device.usage) == (0xFFA0, 1)):
        return "legion_go", "protocol_provenance_unresolved"
    if (device.vid == 0x0DB0 and device.pid in (0x1901, 0x1902)
            and device.usage_page in (0xFFA0, 0xFFF0)
            and device.usage in (1, 0x40)):
        if device.output_length != 64:
            return "msi_claw", "unexpected_output_report_length"
        if device.release not in MSI_ADDRESSES:
            return "msi_claw", "firmware_not_allowlisted"
        if not _MSI_PROTOCOL_VALIDATED:
            return "msi_claw", "firmware_write_semantics_unverified"
        return "msi_claw", None
    return None, "unsupported_interface"


def validate_color(color, brightness, power):
    if not isinstance(color, (list, tuple)) or len(color) != 3:
        raise ValueError("color must contain three integers")
    if any(type(v) is not int or not 0 <= v <= 255 for v in color):
        raise ValueError("RGB channels must be integers in 0..255")
    if type(brightness) is not int or not 0 <= brightness <= 100:
        raise ValueError("brightness must be an integer in 0..100")
    if type(power) is not bool:
        raise ValueError("power must be a boolean")
    return tuple(color)


def static_report(device, color, brightness, power):
    color = validate_color(color, brightness, power)
    family, reason = classify(device)
    if family != "msi_claw" or reason:
        raise ValueError(reason or "unsupported_protocol")
    address = MSI_ADDRESSES[device.release]
    # One static frame, all nine zones. Off means black with zero brightness.
    payload = bytes((0, 1, 9, 3, brightness if power else 0))
    payload += bytes(color if power else (0, 0, 0)) * 9
    header = bytes((0x0F, 0, 0, 0x3C, 0x21, 1,
                    address >> 8, address & 255, len(payload)))
    return (header + payload).ljust(64, b"\x00")
