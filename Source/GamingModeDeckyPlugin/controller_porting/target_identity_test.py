import ctypes as C
from dataclasses import replace
import unittest
from .target_identity import Node, OwnedTargetVerifier, WindowsTargetRegistry, TargetIdentityError


BUS = "ROOT\\SYSTEM\\0001"
TARGET = Node("USB\\VID_045E&PID_028E\\01", BUS,
              ("USB\\VID_045E&PID_028E&REV_0100",), 1, 1)


class CorrelationTests(unittest.TestCase):
    def setUp(self):
        self.nodes = []
        self.props = (1, 0x45E, 0x28E)
        self.verifier = OwnedTargetVerifier(lambda: tuple(self.nodes), lambda _: self.props, BUS)

    def test_new_owned_pdo_binds_and_rechecks_full_instance(self):
        self.nodes = [TARGET]
        self.assertTrue(self.verifier(11, 22))
        self.assertTrue(self.verifier(11, 22))
        self.nodes = [replace(TARGET, instance=TARGET.instance + "NEW")]
        self.assertFalse(self.verifier(11, 22))

    def test_same_vidpid_is_not_evidence(self):
        for changed in (replace(TARGET, parent="ROOT\\OTHER\\0001"),
                        replace(TARGET, address=2), replace(TARGET, ui_number=None),
                        replace(TARGET, hardware_ids=("USB\\VID_045E&PID_028E0",))):
            self.nodes = [changed]
            self.assertFalse(self.verifier(11, 22))

    def test_baseline_and_ambiguity_rejected(self):
        self.nodes = [TARGET]
        old = OwnedTargetVerifier(lambda: self.nodes, lambda _: self.props, BUS)
        self.assertFalse(old(11, 22))
        self.nodes.append(replace(TARGET, instance=TARGET.instance + "OTHER"))
        self.assertFalse(self.verifier(11, 22))

    def test_target_pointer_and_serial_cannot_change(self):
        self.nodes = [TARGET]
        self.assertTrue(self.verifier(11, 22))
        self.assertFalse(self.verifier(11, 23))
        self.props = (2, 0x45E, 0x28E)
        self.assertFalse(self.verifier(11, 22))

    def test_race_during_enumeration_rejected(self):
        self.nodes = [TARGET]
        def changed():
            self.props = (2, 0x45E, 0x28E)
            return self.nodes
        self.verifier.snapshot = changed
        self.assertFalse(self.verifier(11, 22))

    def test_cleanup_requires_bound_target_and_observed_absence(self):
        self.assertFalse(self.verifier.absent())
        self.nodes = [TARGET]
        self.assertTrue(self.verifier(11, 22))
        self.assertFalse(self.verifier.absent())
        self.nodes = []
        self.assertTrue(self.verifier.absent())

    def test_registry_error_fails_closed(self):
        def error():
            raise TargetIdentityError("missing")
        self.verifier.snapshot = error
        self.assertFalse(self.verifier(11, 22))


class RegistryFixture:
    def CM_Locate_DevNodeW(self, pointer, instance, flags):
        pointer._obj.value = 1
        return 0

    def CM_Get_Device_IDW(self, node, buffer, size, flags):
        buffer.value = BUS if node == 1 else TARGET.instance
        return 0

    def CM_Get_Child(self, pointer, node, flags):
        pointer._obj.value = 2
        return 0

    def CM_Get_Parent(self, pointer, node, flags):
        pointer._obj.value = 1
        return 0

    def CM_Get_Sibling(self, pointer, node, flags):
        return 0x0D

    def CM_Get_DevNode_Registry_PropertyW(self, node, key, kind, buffer, size, flags):
        value = {5: "ViGEmBus\0", 2: "USB\\VID_045E&PID_028E&REV_0100\0\0",
                 0x1D: 1, 0x11: 1}[key]
        kind._obj.value = 4 if isinstance(value, int) else 7 if key == 2 else 1
        raw = value.to_bytes(4, "little") if isinstance(value, int) else value.encode("utf-16-le")
        C.memmove(buffer, raw, len(raw))
        size._obj.value = len(raw)
        return 0


class RegistryTests(unittest.TestCase):
    def test_actual_cm_adapter_reads_typed_properties_fixture(self):
        registry = WindowsTargetRegistry(BUS)
        registry.api = RegistryFixture()
        self.assertEqual(registry(), (TARGET,))

    def test_wrong_registry_type_rejected(self):
        registry = WindowsTargetRegistry(BUS)
        registry.api = RegistryFixture()
        original = registry.api.CM_Get_DevNode_Registry_PropertyW
        def malformed(*args):
            result = original(*args)
            args[2]._obj.value = 3
            return result
        registry.api.CM_Get_DevNode_Registry_PropertyW = malformed
        with self.assertRaises(TargetIdentityError):
            registry()


if __name__ == "__main__":
    unittest.main()
