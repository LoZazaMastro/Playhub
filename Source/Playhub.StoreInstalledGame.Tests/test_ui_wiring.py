# SPDX-License-Identifier: MIT
"""Source integration guards; process and asynchronous behavior are in Program.cs."""
import json,re,sys
from pathlib import Path
source=Path(__file__).resolve().parents[1]
page=source/'Playhub/StoreLibrary/StoreLibraryPage.xaml.cs'
if '--before-page' in sys.argv:page=Path(sys.argv[sys.argv.index('--before-page')+1])
s=page.read_text(encoding='utf-8-sig');count=0
def check(value,message):
 global count
 count+=1
 assert value,message
check('Action("Play", () => PlayInstalledAsync(game)' in s and 'Action("Uninstall", () => UninstallInstalledAsync(game)' in s,'installed cards expose actual Play and Uninstall routes')
check('StoreInstalledGameController.Supported(game)' in s and 'row.Uninstall.IsEnabled = available;' in s and 'row.Status?.CanLaunch == true' in s,'removal stays available independently of advisory launch capability')
check('Title = game.Title' in s and 'Label(preparation.InstallPath ?? "")' in s,'confirmation identifies exact game and prepared folder')
check('PrimaryButtonText = text("StoreLibrary.Uninstall")' in s and 'DefaultButton = ContentDialogButton.Close' in s,'uninstall confirmation defaults to cancel')
check('Danger(uninstall)' in s and 'Danger(button)' in s,'destructive row and confirmation retain native danger affordance')
check('installedViewPending = true; UpdateInstalledButtons();' in s and '!installedViewPending && !installedActions.Busy' in s,'pending operations disable all installed actions')
check('await controller.ReadAsync();' in s and 'gamesSignature = ""; Render();' in s,'installed state is re-read after the host operation')
check('installedActionDialog?.Hide()' in s and 'installedActions?.Dispose()' in s,'view close dismisses pending confirmation and invalidates operation')
check('Directory.Delete' not in s and 'Process.Start' not in s,'page cannot delete directories or bypass pinned wrapper')
for prefix in ['row.State.Text = StoreLibraryPresentation.Name(job.Key.Store)', 'Content = l.DisplayName']:
 line=next(line for line in s.splitlines() if prefix in line)
 literal=re.search(r'\+\s*("(?:[^"\\]|\\.)*")',line).group(1)
 check(json.loads(literal)==' \u00b7 ','UI middle-dot separator remains correct after UTF-8 decoding')
keys=['Play','Uninstall','UninstallConfirm','LaunchFailed','UninstallFailed','LaunchRequested','UninstallCompleted']
for lang in ['de','en','es','fr','hi','it','ja','ko','pt','ru','uk','zh']:
 d=json.loads((source/'Playhub/Assets/Localization'/f'{lang}.json').read_text(encoding='utf-8-sig'))
 for key in keys:check(isinstance(d.get('StoreLibrary.'+key),str) and d['StoreLibrary.'+key].strip(),'localized installed action '+lang+'/'+key)
main=(source/'Playhub/MainWindow.StoreLibrary.cs').read_text(encoding='utf-8-sig')
check('new StoreAccountsPage(controller, service as IStoreXboxAccountService, T, _settings.Language)' in main,'desktop store configuration uses the real connected-account service')
print(f'PASS {count} installed-game UI wiring/encoding/localization guards; no UI or network executed')
