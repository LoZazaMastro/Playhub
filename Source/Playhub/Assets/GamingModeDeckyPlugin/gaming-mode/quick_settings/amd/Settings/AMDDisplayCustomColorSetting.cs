using System;

namespace XboxGamingBarHelper.AMD.Settings
{
    internal class AMDDisplayCustomColorSetting
    {
        private readonly IADLXDisplayCustomColor adlxSetting;

        public AMDDisplayCustomColorSetting(IADLXDisplayCustomColor setting)
        {
            adlxSetting = setting;
        }

        public bool IsBrightnessSupported() { return adlxSetting != null && AMDUtilities.GetBoolValue(adlxSetting.IsBrightnessSupported); }
        public int GetBrightness() { return adlxSetting == null ? 0 : AMDUtilities.GetIntValue(adlxSetting.GetBrightness); }
        public Tuple<int, int> GetBrightnessRange() { return adlxSetting == null ? Tuple.Create(0, 0) : AMDUtilities.GetIntRangeValue(adlxSetting.GetBrightnessRange); }
        public void SetBrightness(int value) { if (adlxSetting != null) adlxSetting.SetBrightness(value); }

        public bool IsContrastSupported() { return adlxSetting != null && AMDUtilities.GetBoolValue(adlxSetting.IsContrastSupported); }
        public int GetContrast() { return adlxSetting == null ? 0 : AMDUtilities.GetIntValue(adlxSetting.GetContrast); }
        public Tuple<int, int> GetContrastRange() { return adlxSetting == null ? Tuple.Create(0, 0) : AMDUtilities.GetIntRangeValue(adlxSetting.GetContrastRange); }
        public void SetContrast(int value) { if (adlxSetting != null) adlxSetting.SetContrast(value); }

        public bool IsSaturationSupported() { return adlxSetting != null && AMDUtilities.GetBoolValue(adlxSetting.IsSaturationSupported); }
        public int GetSaturation() { return adlxSetting == null ? 0 : AMDUtilities.GetIntValue(adlxSetting.GetSaturation); }
        public Tuple<int, int> GetSaturationRange() { return adlxSetting == null ? Tuple.Create(0, 0) : AMDUtilities.GetIntRangeValue(adlxSetting.GetSaturationRange); }
        public void SetSaturation(int value) { if (adlxSetting != null) adlxSetting.SetSaturation(value); }

        public bool IsTemperatureSupported() { return adlxSetting != null && AMDUtilities.GetBoolValue(adlxSetting.IsTemperatureSupported); }
        public int GetTemperature() { return adlxSetting == null ? 0 : AMDUtilities.GetIntValue(adlxSetting.GetTemperature); }
        public Tuple<int, int> GetTemperatureRange() { return adlxSetting == null ? Tuple.Create(0, 0) : AMDUtilities.GetIntRangeValue(adlxSetting.GetTemperatureRange); }
        public void SetTemperature(int value) { if (adlxSetting != null) adlxSetting.SetTemperature(value); }
    }
}
