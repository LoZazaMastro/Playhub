using NLog;
using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace XboxGamingBarHelper.AMD
{
    internal class AMDSetting<SettingType> : IDisposable where SettingType : IADLXInterface
    {
        protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        protected SettingType adlxSetting;
        public SettingType ADLXSetting { get {  return adlxSetting; } }

        protected GetBool isSupportedFunction;
        protected GetBool isEnabledFunction;
        protected MethodInfo setEnabledMethod;

        public AMDSetting(SettingType setting)
        {
            adlxSetting = setting;

            var settingType = typeof(SettingType);
            var isSupportedMethodInfo = settingType.GetMethod("IsSupported", BindingFlags.Public | BindingFlags.Instance);
            if (isSupportedMethodInfo == null)
            {
                Logger.Warn("The method 'IsSupported' was not found in type '" + settingType.Name + "'.");
                isSupportedFunction = null;
            }
            else
            {
                isSupportedFunction = (GetBool)Delegate.CreateDelegate(typeof(GetBool), adlxSetting, isSupportedMethodInfo);
            }

            var isEnabledMethodInfo = settingType.GetMethod("IsEnabled", BindingFlags.Public | BindingFlags.Instance);
            if (isEnabledMethodInfo == null)
            {
                Logger.Warn("The method 'IsEnabled' was not found in type '" + settingType.Name + "'.");
                isEnabledFunction = null;
            }
            else
            {
                isEnabledFunction = (GetBool)Delegate.CreateDelegate(typeof(GetBool), adlxSetting, isEnabledMethodInfo);
            }

            setEnabledMethod = settingType.GetMethod("SetEnabled", BindingFlags.Public | BindingFlags.Instance);
        }

        public virtual bool IsSupported()
        {
            if (isSupportedFunction == null)
            {
                Logger.Warn(GetType().Name + " IsSupported function is not available.");
                return false;
            }

            return AMDUtilities.GetBoolValue(isSupportedFunction);
        }

        public virtual bool IsEnabled()
        {
            if (isEnabledFunction == null)
            {
                Logger.Warn(GetType().Name + " IsEnabled function is not available.");
                return false;
            }

            return AMDUtilities.GetBoolValue(isEnabledFunction);
        }

        public virtual ADLX_RESULT SetEnabledResult(bool enabled)
        {
            if (setEnabledMethod == null)
            {
                Logger.Warn(GetType().Name + " SetEnabled method is not available.");
                return ADLX_RESULT.ADLX_FAIL;
            }

            var result = setEnabledMethod.Invoke(adlxSetting, new object[1] { enabled });
            return result is ADLX_RESULT
                ? (ADLX_RESULT)result
                : ADLX_RESULT.ADLX_OK;
        }

        public virtual void SetEnabled(bool enabled)
        {
            SetEnabledResult(enabled);
        }

        ~AMDSetting()
        {
            if (adlxSetting != null) adlxSetting.Dispose();
        }

        public virtual int Release()
        {
            if (adlxSetting == null) return 0;

            return adlxSetting.Release();
        }

        public void Dispose()
        {
            if (adlxSetting != null) adlxSetting.Dispose();
        }
    }
}
