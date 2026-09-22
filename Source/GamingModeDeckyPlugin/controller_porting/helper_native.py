"""Native factory for the same helper loop. No construction-time hardware I/O."""
from .helper_host import Pipeline
from .helper_ownership import WindowsAncestry, WindowsLease
from .reader_sdl import NativeReader, Reader
from .output_vigem import NativeViGEm, OutputLease
from .target_identity import WindowsTargetRegistry, NativeTargetProperties, OwnedTargetVerifier


def native_pipeline(*, source_identity, source_instance, sdl_approval, output_approval,
                    verify_target=None, target_bus_instance=None, dsu_port=26760):
    """Host supplies audited artifacts and an OS-correlated owned-target verifier.

    No verifier can be supplied by renderer JSON. Missing evidence rejects startup.
    No DLL is loaded until Pipeline.start obtains the source lease.
    """
    if verify_target is not None and not callable(verify_target):
        raise ValueError("owned_target_enumeration_verifier_required")
    if verify_target is None and not target_bus_instance:
        raise ValueError("owned_target_enumeration_verifier_required")
    ancestry = WindowsAncestry()
    def make_reader(lease):
        def verify(path, instance):
            return ancestry(path, instance) if lease.valid() else None
        return Reader(NativeReader(sdl_approval, authorize_device_io=True), verify)
    def make_output(kind, reader, lease):
        backend = NativeViGEm(output_approval, authorize_device_io=True)
        target_verifier = verify_target or OwnedTargetVerifier(
            WindowsTargetRegistry(target_bus_instance), NativeTargetProperties(backend.dll),
            target_bus_instance)
        def verify(session, identity, target):
            if not lease.valid() or reader.reader.token != session or reader.identity != identity:
                return False
            reader._verify()
            # Recheck the same OS target before each native report, not just activation.
            return target is None or target_verifier(output.client, target)
        def feedback(session, identity, large, small):
            if not verify(session, identity, None):
                raise ValueError("feedback_source_changed")
            reader.reader.rumble(session, round(large * 65535), round(small * 65535),
                                 200 if large or small else 0, authorize_output=True)
        output = OutputLease(backend, verify, target_verifier, kind=kind, feedback_sink=feedback)
        return output
    return Pipeline(make_reader, make_output, WindowsLease, instance=source_instance,
                    identity=source_identity, dsu_port=dsu_port)
