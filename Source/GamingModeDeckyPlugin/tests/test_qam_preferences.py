import json
from pathlib import Path
import tempfile
import unittest
from quick_settings.qam_preferences import QamPreferences, normalize


class QamPreferenceTests(unittest.TestCase):
    def test_legacy_import_keeps_unknown_plugins_icons_and_order(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            legacy=root/'Shortcuts'/'state.json'
            legacy.parent.mkdir()
            data={'version':3,'selected':['News','Future Plugin'],'icons':{'Future Plugin':'custom-icon'},
                  'order':['steam:5','shortcut:News','decky:999'],'hidden':['steam:7'],'updated_at':1234}
            legacy.write_text(json.dumps(data))
            settings=QamPreferences(root/'gaming-mode')
            self.assertEqual(settings.get(),{**data,'exists':True})
            self.assertEqual(json.loads(legacy.read_text()),data)
            settings.save({**data,'hidden':[]})
            self.assertEqual(settings.get()['hidden'],[])

    def test_corrupt_primary_uses_valid_backup_without_losing_preferences(self):
        with tempfile.TemporaryDirectory() as directory:
            settings=QamPreferences(directory)
            settings.save({'selected':['News']})
            settings.save({'selected':['Weather']})
            settings.path.write_text('{broken')
            self.assertEqual(settings.get()['selected'],['News'])

    def test_hidden_scope_excludes_plugin_tabs(self):
        result=normalize({'hidden':['steam:5','shortcut:Playhub','decky:999','steam:5'],
                          'selected':['News','News',None],'updatedAt':123})
        self.assertEqual(result['hidden'],['steam:5'])
        self.assertEqual(result['selected'],['News'])
        self.assertEqual(result['updated_at'],123)
