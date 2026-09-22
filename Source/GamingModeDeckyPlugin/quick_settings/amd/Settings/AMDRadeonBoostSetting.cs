using System;

namespace XboxGamingBarHelper.AMD.Settings
{
    internal class AMDRadeonBoostSetting : AMDSetting<IADLX3DBoost>
    {
        public AMDRadeonBoostSetting(IADLX3DBoost setting) : base(setting)
        {
        }

        public Tuple<int, int> GetResolutionRange()
        {
            return adlxSetting == null
                ? new Tuple<int, int>(0, 0)
                : AMDUtilities.GetIntRangeValue(adlxSetting.GetResolutionRange);
        }

        public int GetResolution()
        {
            return adlxSetting == null ? 0 : AMDUtilities.GetIntValue(adlxSetting.GetResolution);
        }

        public void SetResolution(int resolution)
        {
            if (adlxSetting != null) adlxSetting.SetResolution(resolution);
        }
    }
}
