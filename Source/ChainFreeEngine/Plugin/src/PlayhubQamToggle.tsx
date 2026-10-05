import { DFL, SP_REACT as React } from "./decky";
import { getPlayhubQamState, setPlayhubQamVisible, subscribePlayhubQam } from "./qam";
import { getPlayhubQamLabel, getPlayhubQamDescription } from "./qamLocale";

export function PlayhubQamToggle({ locale }: { locale: string }) {
  const state = React.useSyncExternalStore(subscribePlayhubQam, getPlayhubQamState);
  const { ToggleField } = DFL as any;
  return <ToggleField bottomSeparator="none" label={getPlayhubQamLabel(locale)} description={getPlayhubQamDescription(locale)} checked={state.visible}
      disabled={!state.available || state.pending}
      onChange={(visible: boolean) => { void setPlayhubQamVisible(visible).catch((error) => console.warn("Playhub QAM preference was not saved", error)); }} />;
}
