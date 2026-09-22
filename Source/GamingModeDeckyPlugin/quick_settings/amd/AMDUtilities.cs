using NLog;
using System;

namespace XboxGamingBarHelper.AMD
{
    internal static class AMDUtilities
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        internal static bool GetBoolValue(GetBool func)
        {
            var boolPointer = ADLX.new_boolP();
            var getValueResult = func(boolPointer);
            if (getValueResult != ADLX_RESULT.ADLX_OK)
            {
                ADLX.delete_boolP(boolPointer);
                Logger.Error("Failed to get AMD bool value. ADLX_RESULT: " + getValueResult);
                return false;
            }

            var boolValue = ADLX.boolP_value(boolPointer);
            ADLX.delete_boolP(boolPointer);
            return boolValue;
        }

        internal static Tuple<int, int> GetIntRangeValue(GetIntRange func)
        {
            var intRangePointer = ADLX.new_intRangeP();
            var getValueResult = func(intRangePointer);
            if (getValueResult != ADLX_RESULT.ADLX_OK)
            {
                ADLX.delete_intRangeP(intRangePointer);
                Logger.Error("Failed to get AMD int range value. ADLX_RESULT: " + getValueResult);
                return new Tuple<int, int>(0, 0);
            }

            var intRangeValue = ADLX.intRangeP_value(intRangePointer);
            ADLX.delete_intRangeP(intRangePointer);
            if (intRangePointer != null) intRangePointer.Dispose();
            return new Tuple<int, int>(intRangeValue.minValue, intRangeValue.maxValue);
        }

        internal static int GetIntValue(GetInt func)
        {
            var intPointer = ADLX.new_intP();
            var getValueResult = func(intPointer);
            if (getValueResult != ADLX_RESULT.ADLX_OK)
            {
                ADLX.delete_intP(intPointer);
                Logger.Error("Failed to get AMD int value. ADLX_RESULT: " + getValueResult);
                return 0;
            }

            var intValue = ADLX.intP_value(intPointer);
            ADLX.delete_intP(intPointer);
            return intValue;
        }
    }
}
