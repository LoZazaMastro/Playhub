import importlib.util
from pathlib import Path
import threading
import time
import unittest
from unittest.mock import Mock, patch

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('actual_capability_queue', ROOT / 'quick_settings/device_capabilities.py')
cap = importlib.util.module_from_spec(spec)
spec.loader.exec_module(cap)


def proof(provider, **values):
    return dict(ok=True, available=True, token='a'*64, provider=provider, **values)


REPORTS = {
    'asus': proof('asus-ally', commands=[0, 1, 2]),
    'asus-fan': proof('asus-ally', capabilities=dict(fan_rpm=True, temperature_c=True),
                      fans=dict(cpu_rpm=2200, gpu_rpm=2100), temperatures=dict(cpu_c=57, gpu_c=52)),
    'asus-tdp': proof('asus-rc73xa-tdp', readbackKind='firmware-register', limitsWatts=dict(spl=17, sppt=20, fppt=25),
                      writeAvailable=True, oemControllerActive=False, manualRecoveryRequired=False),
}


class QueueChecks(unittest.TestCase):
    def manager(self, probe, timeout=.5, providers=None):
        profile = dict(selection='automatic', effective='rog-xbox-ally-x', detected='rog-xbox-ally-x')
        identity = dict(manufacturer='ASUS', model='RC73XA')
        obj = cap.DeviceCapabilities(lambda: profile, lambda: identity,
            providers or {name: (lambda _: True, lambda name=name: probe(name)) for name in REPORTS}, timeout)
        self.addCleanup(obj.close)
        return obj

    def test_three_slow_providers_complete_in_first_get_with_maximum_two_running(self):
        lock, active, peak, calls = threading.Lock(), 0, 0, []
        def probe(name):
            nonlocal active, peak
            with lock:
                active += 1
                peak = max(peak, active)
                calls.append(name)
            try:
                time.sleep(.04)
                return REPORTS[name]
            finally:
                with lock:
                    active -= 1
        manager = self.manager(probe)
        with patch.object(cap, 'wait', wraps=cap.wait) as waiter:
            result = manager.get()
        rows = {row['id']: row for row in result['controls']}
        for name in ['presets', 'fans', 'temperatures', 'power-limits']:
            self.assertTrue(rows[name]['available'], rows[name])
        self.assertEqual(peak, 2)
        self.assertCountEqual(calls, REPORTS)
        self.assertEqual(waiter.call_count, 1, 'one blocking wait; no active polling')
        manager.get()
        self.assertEqual(len(calls), 3, 'completed probes retain the original cache')

    def test_queued_third_provider_is_pending_and_singleflight_until_completed(self):
        gate, entered, calls = threading.Event(), threading.Event(), []
        self.addCleanup(gate.set)
        lock = threading.Lock()
        def probe(name):
            with lock:
                calls.append(name)
                if len(calls) == 2:
                    entered.set()
            gate.wait(1)
            return REPORTS[name]
        manager = self.manager(probe, timeout=.01)
        first = manager.get()
        self.assertTrue(entered.wait(.5))
        rows = {row['id']: row for row in first['controls']}
        self.assertEqual(rows['power-limits']['status'], 'pending')
        manager.get()
        self.assertEqual(len(calls), 2)
        gate.set()
        manager._timeout = .5
        result = manager.get()
        self.assertTrue(next(row for row in result['controls'] if row['id'] == 'power-limits')['available'])
        self.assertCountEqual(calls, REPORTS)

    def test_one_absolute_deadline_includes_time_before_wait(self):
        gate = threading.Event()
        self.addCleanup(gate.set)
        manager = self.manager(lambda name: (gate.wait(1), REPORTS[name])[1], timeout=7.5)
        with patch.object(cap.time, 'monotonic', side_effect=[100.0, 105.0]), patch.object(cap, 'wait') as waiter:
            result = manager.get()
        self.assertEqual(waiter.call_count, 1)
        self.assertEqual(waiter.call_args.kwargs['timeout'], 2.5)
        self.assertEqual(next(row for row in result['controls'] if row['id'] == 'power-limits')['status'], 'pending')

    def test_slot_is_released_before_future_completion_is_published(self):
        releases = {}
        actual_future, actual_semaphore = cap.Future, cap.threading.BoundedSemaphore
        class Semaphore:
            def __init__(self, count):
                self.inner = actual_semaphore(count)
            def acquire(self, **kwargs):
                return self.inner.acquire(**kwargs)
            def release(self):
                releases[threading.get_ident()] = True
                return self.inner.release()
        class Future(actual_future):
            def set_result(inner, value):
                self.assertTrue(releases.pop(threading.get_ident(), False))
                return super().set_result(value)
        with patch.object(cap.threading, 'BoundedSemaphore', Semaphore), patch.object(cap, 'Future', Future):
            manager = self.manager(lambda name: REPORTS[name])
            result = manager.get()
        self.assertTrue(next(row for row in result['controls'] if row['id'] == 'power-limits')['available'])

    def test_close_cancels_queued_probe_without_invoking_it(self):
        gate, calls = threading.Event(), []
        self.addCleanup(gate.set)
        def probe(name):
            calls.append(name)
            gate.wait(1)
            return REPORTS[name]
        manager = self.manager(probe, timeout=.02)
        manager.get()
        self.assertEqual(len(calls), 2)
        manager.close()
        gate.set()
        self.assertNotIn('asus-tdp', calls)
        self.assertTrue(all(not row['available'] for row in manager.get()['controls']))
        self.assertEqual(len(calls), 2)

    def test_queue_capacity_is_bounded_and_overflow_reports_pending(self):
        gate = threading.Event()
        self.addCleanup(gate.set)
        providers = {str(i): (lambda _: True, lambda: gate.wait(1)) for i in range(9)}
        manager = self.manager(None, providers=providers)
        identity = {'manufacturer': 'inert', 'model': 'fixture'}
        jobs = [manager._start(str(i), identity) for i in range(9)]
        self.assertEqual(jobs[-1], {'code': 'busy'})
        self.assertEqual(sum(not item.done() for item in manager._pending.values()), 8)
        self.assertEqual(cap._control('presets', 'asus', jobs[-1])['status'], 'pending')

    def test_stale_selection_and_false_identity_never_queue_a_probe(self):
        probe = Mock()
        manager = self.manager(probe)
        self.assertEqual(manager.get('wrong-selection')['status'], 'changed')
        self.assertFalse(manager._queue)
        probe.assert_not_called()
        manager = self.manager(None, providers={name: (lambda _: False, probe) for name in REPORTS})
        manager.get()
        self.assertFalse(manager._queue)
        self.assertFalse(manager._pending)
        probe.assert_not_called()

    def test_close_notifies_waiter_for_a_cancelled_queued_job(self):
        gate, entered, finished = threading.Event(), threading.Event(), threading.Event()
        self.addCleanup(gate.set)
        calls = []
        def probe(name):
            calls.append(name)
            if len(calls) == 2:
                entered.set()
            gate.wait(1)
            return REPORTS[name]
        manager = self.manager(probe, timeout=1)
        thread = threading.Thread(target=lambda: (manager.get(), finished.set()), daemon=True)
        thread.start()
        self.assertTrue(entered.wait(.5))
        manager.close()
        gate.set()
        self.assertTrue(finished.wait(.5), 'cancelled queued Future delayed get until the deadline')
        thread.join(.1)
        self.assertNotIn('asus-tdp', calls)

    def test_thread_start_failure_releases_slots_and_reports_unavailable(self):
        probe = Mock()
        manager = self.manager(probe)
        with patch.object(cap.threading.Thread, 'start', side_effect=RuntimeError('inert start failure')):
            result = manager.get()
        self.assertEqual(manager._slots._value, 2)
        self.assertFalse(manager._queue)
        self.assertTrue(all(not row['available'] for row in result['controls']))
        probe.assert_not_called()


if __name__ == '__main__':
    unittest.main(verbosity=2)
