using System;
using System.Globalization;
using System.Text;
using System.Threading;
using XboxGamingBarHelper.AMD;
using XboxGamingBarHelper.AMD.Settings;

// Standalone ADLX helper for Quick Settings.
//   adlx_helper.exe status                  -> prints JSON of all features
//   adlx_helper.exe set rsr on|off
//   adlx_helper.exe set rsr_sharpness <0-100>
//   adlx_helper.exe set afmf on|off
//   adlx_helper.exe set antilag on|off
//   adlx_helper.exe set chill on|off
//   adlx_helper.exe set chill_min <fps>
//   adlx_helper.exe set chill_max <fps>
//   adlx_helper.exe set sharpening on|off
//   adlx_helper.exe set sharpening_value <0-100>
//   adlx_helper.exe set display_brightness <driver range>
//   adlx_helper.exe set display_contrast <driver range>
//   adlx_helper.exe set display_saturation <driver range>
//   adlx_helper.exe set display_temperature <driver range>
//   adlx_helper.exe set-batch <feature> <value> [<feature> <value> ...]
//
// stdout is JSON only; diagnostics go to stderr.

namespace QuickSettingsAdlx
{
    internal static class Program
    {
        [System.Runtime.InteropServices.DllImport("kernel32", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        private static string B(bool v) { return v ? "true" : "false"; }
        private static string I(int v) { return v.ToString(CultureInfo.InvariantCulture); }

        private static bool ApplyVerified(Action setter, Func<bool> verifier)
        {
            setter();
            return WaitVerified(verifier);
        }

        private static bool WaitVerified(Func<bool> verifier)
        {
            if (verifier()) return true;
            var delays = new[] { 40, 90, 180, 360, 720 };
            for (var attempt = 0; attempt < delays.Length; attempt++)
            {
                Thread.Sleep(delays[attempt]);
                if (verifier()) return true;
            }
            return false;
        }

        private static bool WaitVerifiedExtended(Func<bool> verifier)
        {
            if (verifier()) return true;
            var delays = new[] { 50, 100, 200, 400, 800, 1200 };
            for (var attempt = 0; attempt < delays.Length; attempt++)
            {
                Thread.Sleep(delays[attempt]);
                if (verifier()) return true;
            }
            return false;
        }

        private static int Main(string[] args)
        {
            try
            {
                var exeDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(exeDir)) SetDllDirectory(exeDir);
            }
            catch { }

            ADLXHelper helper = null;
            try
            {
                helper = new ADLXHelper();
                var init = helper.Initialize();
                if (init != ADLX_RESULT.ADLX_OK)
                {
                    Console.WriteLine("{\"ok\":false,\"message\":\"ADLX initialize failed (" + init + "). Update AMD Adrenalin and ensure a Radeon GPU.\"}");
                    return 1;
                }
                var system = helper.GetSystemServices();
                if (system == null)
                {
                    Console.WriteLine("{\"ok\":false,\"message\":\"ADLX system services unavailable.\"}");
                    return 1;
                }

                // First display that exposes AMD custom-color controls.
                AMDDisplayCustomColorSetting displayColor = null;
                try
                {
                    var displayServicesPtr = ADLX.new_displaySerP_Ptr();
                    system.GetDisplaysServices(displayServicesPtr);
                    var displayServices = ADLX.displaySerP_Ptr_value(displayServicesPtr);
                    if (displayServices != null)
                    {
                        var displayListPtr = ADLX.new_displayListP_Ptr();
                        displayServices.GetDisplays(displayListPtr);
                        var displayList = ADLX.displayListP_Ptr_value(displayListPtr);
                        if (displayList != null)
                        {
                            for (uint index = 0; index < displayList.Size(); index++)
                            {
                                var displayPtr = ADLX.new_displayP_Ptr();
                                if (displayList.At(index, displayPtr) != ADLX_RESULT.ADLX_OK) continue;
                                var display = ADLX.displayP_Ptr_value(displayPtr);
                                if (display == null) continue;
                                var colorPtr = ADLX.new_displayCustomColorP_Ptr();
                                if (displayServices.GetCustomColor(display, colorPtr) != ADLX_RESULT.ADLX_OK) continue;
                                var candidate = new AMDDisplayCustomColorSetting(
                                    ADLX.displayCustomColorP_Ptr_value(colorPtr));
                                if (candidate.IsBrightnessSupported() ||
                                    candidate.IsContrastSupported() ||
                                    candidate.IsSaturationSupported() ||
                                    candidate.IsTemperatureSupported())
                                {
                                    displayColor = candidate;
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex) { Console.Error.WriteLine("DisplayColor: " + ex.Message); }

                // First GPU.
                IADLXGPU gpu = null;
                try
                {
                    var gpuListPtr = ADLX.new_gpuListP_Ptr();
                    system.GetGPUs(gpuListPtr);
                    var gpuList = ADLX.gpuListP_Ptr_value(gpuListPtr);
                    if (gpuList != null && gpuList.Size() > 0)
                    {
                        var gpuPtr = ADLX.new_gpuP_Ptr();
                        gpuList.At(gpuList.Begin(), gpuPtr);
                        gpu = ADLX.gpuP_Ptr_value(gpuPtr);
                    }
                }
                catch (Exception ex) { Console.Error.WriteLine("GPU enum: " + ex.Message); }

                // 3D settings services.
                var sp = ADLX.new_threeDSettingsSerP_Ptr();
                system.Get3DSettingsServices(sp);
                var services = new IADLX3DSettingsServices2(
                    ADLXPINVOKE.threeDSettingsSerP_Ptr_value(SWIGTYPE_p_p_adlx__IADLX3DSettingsServices.getCPtr(sp)), false);

                // Global features.
                AMDRadeonSuperResolutionSetting rsr = null;
                AMDFluidMotionFrameSetting afmf = null;
                try
                {
                    var rsrPtr = ADLX.new_threeDRadeonSuperResolutionP_Ptr();
                    services.GetRadeonSuperResolution(rsrPtr);
                    rsr = new AMDRadeonSuperResolutionSetting(ADLX.threeDRadeonSuperResolutionP_Ptr_value(rsrPtr));
                }
                catch (Exception ex) { Console.Error.WriteLine("RSR: " + ex.Message); }
                try
                {
                    var afmfPtr = ADLX.new_threeDAMDFluidMotionFramesP_Ptr();
                    services.GetAMDFluidMotionFrames(afmfPtr);
                    afmf = new AMDFluidMotionFrameSetting(ADLX.threeDAMDFluidMotionFramesP_Ptr_value(afmfPtr));
                }
                catch (Exception ex) { Console.Error.WriteLine("AFMF: " + ex.Message); }

                // Global driver features exposed for the selected Radeon GPU.
                AMDRadeonAntiLagSetting antilag = null;
                AMDRadeonChillSetting chill = null;
                AMDImageSharpeningSetting sharp = null;
                AMDSetting<IADLX3DImageSharpenDesktop> sharpDesktop = null;
                AMDRadeonBoostSetting boost = null;
                AMDSetting<IADLX3DEnhancedSync> enhancedSync = null;
                if (gpu != null)
                {
                    try
                    {
                        var alPtr = ADLX.new_threeDAntiLagP_Ptr();
                        services.GetAntiLag(gpu, alPtr);
                        antilag = new AMDRadeonAntiLagSetting(ADLX.threeDAntiLagP_Ptr_value(alPtr));
                    }
                    catch (Exception ex) { Console.Error.WriteLine("AntiLag: " + ex.Message); }
                    try
                    {
                        var chPtr = ADLX.new_threeDChillP_Ptr();
                        services.GetChill(gpu, chPtr);
                        chill = new AMDRadeonChillSetting(ADLX.threeDChillP_Ptr_value(chPtr));
                    }
                    catch (Exception ex) { Console.Error.WriteLine("Chill: " + ex.Message); }
                    try
                    {
                        var isPtr = ADLX.new_threeDImageSharpeningP_Ptr();
                        services.GetImageSharpening(gpu, isPtr);
                        sharp = new AMDImageSharpeningSetting(ADLX.threeDImageSharpeningP_Ptr_value(isPtr));
                    }
                    catch (Exception ex) { Console.Error.WriteLine("Sharpening: " + ex.Message); }
                    try
                    {
                        var desktopSharpPtr = ADLX.new_threeDImageSharpenDesktopP_Ptr();
                        services.GetImageSharpenDesktop(gpu, desktopSharpPtr);
                        sharpDesktop = new AMDSetting<IADLX3DImageSharpenDesktop>(
                            ADLX.threeDImageSharpenDesktopP_Ptr_value(desktopSharpPtr));
                    }
                    catch (Exception ex) { Console.Error.WriteLine("DesktopSharpening: " + ex.Message); }
                    try
                    {
                        var boostPtr = ADLX.new_threeDBoostP_Ptr();
                        services.GetBoost(gpu, boostPtr);
                        boost = new AMDRadeonBoostSetting(ADLX.threeDBoostP_Ptr_value(boostPtr));
                    }
                    catch (Exception ex) { Console.Error.WriteLine("Boost: " + ex.Message); }
                    try
                    {
                        var syncPtr = ADLX.new_threeDEnhancedSyncP_Ptr();
                        services.GetEnhancedSync(gpu, syncPtr);
                        enhancedSync = new AMDSetting<IADLX3DEnhancedSync>(ADLX.threeDEnhancedSyncP_Ptr_value(syncPtr));
                    }
                    catch (Exception ex) { Console.Error.WriteLine("EnhancedSync: " + ex.Message); }
                }

                string action = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
                string appliedFeature = "";
                string requestedValue = "";
                string failureDetail = "";
                var appliedFeatures = new System.Collections.Generic.List<string>();

                Func<string, string, bool> applyFeature = delegate(string feature, string value)
                {
                    bool on = value == "on" || value == "1" || value == "true";
                    int num;
                    int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out num);

                    switch (feature)
                    {
                        case "rsr":
                            return rsr != null && ApplyVerified(() => rsr.SetEnabled(on), () => rsr.IsEnabled() == on);
                        case "rsr_sharpness":
                            return rsr != null && ApplyVerified(() => rsr.SetSharpness(num), () => rsr.GetSharpness() == num);
                        case "afmf":
                            if (afmf == null)
                            {
                                failureDetail = "AFMF interface unavailable";
                                return false;
                            }
                            var afmfResult = afmf.SetEnabledResult(on);
                            if (WaitVerifiedExtended(() => afmf.IsEnabled() == on))
                            {
                                failureDetail = "";
                                return true;
                            }
                            failureDetail = afmfResult.ToString();
                            return false;
                        case "antilag":
                            return antilag != null && ApplyVerified(() => antilag.SetEnabled(on), () => antilag.IsEnabled() == on);
                        case "chill":
                            return chill != null && ApplyVerified(() => chill.SetEnabled(on), () => chill.IsEnabled() == on);
                        case "chill_min":
                            return chill != null && ApplyVerified(() => chill.SetMinFPS(num), () => chill.GetMinFPS() == num);
                        case "chill_max":
                            return chill != null && ApplyVerified(() => chill.SetMaxFPS(num), () => chill.GetMaxFPS() == num);
                        case "sharpening":
                            return sharp != null && ApplyVerified(() => sharp.SetEnabled(on), () => sharp.IsEnabled() == on);
                        case "sharpening_desktop":
                            return sharpDesktop != null && sharpDesktop.IsSupported() &&
                                ApplyVerified(
                                    () => sharpDesktop.SetEnabled(on),
                                    () => sharpDesktop.IsEnabled() == on);
                        case "sharpening_value":
                            return sharp != null && ApplyVerified(() => sharp.SetSharpness(num), () => sharp.GetSharpness() == num);
                        case "boost":
                            return boost != null && ApplyVerified(() => boost.SetEnabled(on), () => boost.IsEnabled() == on);
                        case "boost_resolution":
                            return boost != null && ApplyVerified(() => boost.SetResolution(num), () => boost.GetResolution() == num);
                        case "enhanced_sync":
                            return enhancedSync != null && ApplyVerified(
                                () => enhancedSync.SetEnabled(on),
                                () => enhancedSync.IsEnabled() == on);
                        case "display_brightness":
                            return displayColor != null && displayColor.IsBrightnessSupported() &&
                                ApplyVerified(() => displayColor.SetBrightness(num), () => displayColor.GetBrightness() == num);
                        case "display_contrast":
                            return displayColor != null && displayColor.IsContrastSupported() &&
                                ApplyVerified(() => displayColor.SetContrast(num), () => displayColor.GetContrast() == num);
                        case "display_saturation":
                            return displayColor != null && displayColor.IsSaturationSupported() &&
                                ApplyVerified(() => displayColor.SetSaturation(num), () => displayColor.GetSaturation() == num);
                        case "display_temperature":
                            return displayColor != null && displayColor.IsTemperatureSupported() &&
                                ApplyVerified(() => displayColor.SetTemperature(num), () => displayColor.GetTemperature() == num);
                        default:
                            return false;
                    }
                };

                if (action == "set" && args.Length >= 3)
                {
                    string feature = args[1].ToLowerInvariant();
                    string value = args[2];
                    if (!applyFeature(feature, value))
                    {
                        Console.WriteLine("{\"ok\":false,\"scope\":\"global\",\"failed_feature\":\"" +
                            feature + "\",\"adlx_result\":\"" + failureDetail +
                            "\",\"message\":\"AMD driver did not confirm the requested value.\"}");
                        return 2;
                    }
                    appliedFeature = feature;
                    requestedValue = value;
                    appliedFeatures.Add(feature);
                }
                else if (action == "set-batch" && args.Length >= 3 && args.Length % 2 == 1)
                {
                    for (var index = 1; index < args.Length; index += 2)
                    {
                        string feature = args[index].ToLowerInvariant();
                        string value = args[index + 1];
                        if (!applyFeature(feature, value))
                        {
                            Console.WriteLine("{\"ok\":false,\"scope\":\"global\",\"failed_feature\":\"" +
                                feature + "\",\"applied_count\":" + appliedFeatures.Count +
                                ",\"adlx_result\":\"" + failureDetail + "\"" +
                                ",\"message\":\"AMD driver did not confirm a profile value.\"}");
                            return 2;
                        }
                        appliedFeatures.Add(feature);
                    }
                }

                // Return the confirmed global state from this same ADLX session. This
                // keeps a QAM toggle to one process and one driver transaction.
                var sb = new StringBuilder();
                sb.Append("{\"ok\":true,\"scope\":\"global\",\"gpu\":").Append(B(gpu != null));
                if (!String.IsNullOrEmpty(appliedFeature))
                    sb.Append(",\"applied_feature\":\"").Append(appliedFeature)
                      .Append("\",\"requested_value\":\"").Append(requestedValue).Append("\"");
                if (appliedFeatures.Count > 0)
                {
                    sb.Append(",\"applied_features\":[");
                    for (var index = 0; index < appliedFeatures.Count; index++)
                    {
                        if (index > 0) sb.Append(",");
                        sb.Append("\"").Append(appliedFeatures[index]).Append("\"");
                    }
                    sb.Append("]");
                }

                // RSR
                sb.Append(",\"rsr\":{");
                if (rsr != null)
                {
                    var rr = rsr.GetSharpnessRange();
                    sb.Append("\"supported\":").Append(B(rsr.IsSupported()))
                      .Append(",\"enabled\":").Append(B(rsr.IsEnabled()))
                      .Append(",\"sharpness\":").Append(I(rsr.GetSharpness()))
                      .Append(",\"smin\":").Append(I(rr.Item1))
                      .Append(",\"smax\":").Append(I(rr.Item2));
                }
                else sb.Append("\"supported\":false");
                sb.Append("}");

                // AFMF
                sb.Append(",\"afmf\":{");
                if (afmf != null)
                    sb.Append("\"supported\":").Append(B(afmf.IsSupported())).Append(",\"enabled\":").Append(B(afmf.IsEnabled()));
                else sb.Append("\"supported\":false");
                sb.Append("}");

                // Anti-Lag
                sb.Append(",\"antilag\":{");
                if (antilag != null)
                    sb.Append("\"supported\":").Append(B(antilag.IsSupported())).Append(",\"enabled\":").Append(B(antilag.IsEnabled()));
                else sb.Append("\"supported\":false");
                sb.Append("}");

                // Chill
                sb.Append(",\"chill\":{");
                if (chill != null)
                {
                    var cr = chill.GetFPSRange();
                    sb.Append("\"supported\":").Append(B(chill.IsSupported()))
                      .Append(",\"enabled\":").Append(B(chill.IsEnabled()))
                      .Append(",\"min\":").Append(I(chill.GetMinFPS()))
                      .Append(",\"max\":").Append(I(chill.GetMaxFPS()))
                      .Append(",\"fmin\":").Append(I(cr.Item1))
                      .Append(",\"fmax\":").Append(I(cr.Item2));
                }
                else sb.Append("\"supported\":false");
                sb.Append("}");

                // Radeon Boost
                sb.Append(",\"boost\":{");
                if (boost != null)
                {
                    var br = boost.GetResolutionRange();
                    sb.Append("\"supported\":").Append(B(boost.IsSupported()))
                      .Append(",\"enabled\":").Append(B(boost.IsEnabled()))
                      .Append(",\"resolution\":").Append(I(boost.GetResolution()))
                      .Append(",\"rmin\":").Append(I(br.Item1))
                      .Append(",\"rmax\":").Append(I(br.Item2));
                }
                else sb.Append("\"supported\":false");
                sb.Append("}");

                // Enhanced Sync
                sb.Append(",\"enhanced_sync\":{");
                if (enhancedSync != null)
                    sb.Append("\"supported\":").Append(B(enhancedSync.IsSupported()))
                      .Append(",\"enabled\":").Append(B(enhancedSync.IsEnabled()));
                else sb.Append("\"supported\":false");
                sb.Append("}");

                // Image Sharpening
                sb.Append(",\"sharpening\":{");
                if (sharp != null)
                {
                    var sr = sharp.GetSharpnessRange();
                    sb.Append("\"supported\":").Append(B(sharp.IsSupported()))
                      .Append(",\"enabled\":").Append(B(sharp.IsEnabled()))
                      .Append(",\"value\":").Append(I(sharp.GetSharpness()))
                      .Append(",\"smin\":").Append(I(sr.Item1))
                      .Append(",\"smax\":").Append(I(sr.Item2))
                      .Append(",\"step\":10")
                      .Append(",\"desktop_supported\":").Append(B(sharpDesktop != null && sharpDesktop.IsSupported()))
                      .Append(",\"desktop_enabled\":").Append(B(sharpDesktop != null && sharpDesktop.IsSupported() && sharpDesktop.IsEnabled()));
                }
                else sb.Append("\"supported\":false");
                sb.Append("}");

                // AMD display custom color.
                sb.Append(",\"display_color\":{\"available\":").Append(B(displayColor != null));
                if (displayColor != null)
                {
                    var brightnessRange = displayColor.GetBrightnessRange();
                    var contrastRange = displayColor.GetContrastRange();
                    var saturationRange = displayColor.GetSaturationRange();
                    var temperatureRange = displayColor.GetTemperatureRange();
                    sb.Append(",\"brightness\":{\"supported\":").Append(B(displayColor.IsBrightnessSupported()))
                      .Append(",\"value\":").Append(I(displayColor.GetBrightness()))
                      .Append(",\"min\":").Append(I(brightnessRange.Item1))
                      .Append(",\"max\":").Append(I(brightnessRange.Item2)).Append("}")
                      .Append(",\"contrast\":{\"supported\":").Append(B(displayColor.IsContrastSupported()))
                      .Append(",\"value\":").Append(I(displayColor.GetContrast()))
                      .Append(",\"min\":").Append(I(contrastRange.Item1))
                      .Append(",\"max\":").Append(I(contrastRange.Item2)).Append("}")
                      .Append(",\"saturation\":{\"supported\":").Append(B(displayColor.IsSaturationSupported()))
                      .Append(",\"value\":").Append(I(displayColor.GetSaturation()))
                      .Append(",\"min\":").Append(I(saturationRange.Item1))
                      .Append(",\"max\":").Append(I(saturationRange.Item2)).Append("}")
                      .Append(",\"temperature\":{\"supported\":").Append(B(displayColor.IsTemperatureSupported()))
                      .Append(",\"value\":").Append(I(displayColor.GetTemperature()))
                      .Append(",\"min\":").Append(I(temperatureRange.Item1))
                      .Append(",\"max\":").Append(I(temperatureRange.Item2)).Append("}");
                }
                sb.Append("}}");

                Console.WriteLine(sb.ToString());
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("{\"ok\":false,\"message\":\"" + ex.Message.Replace("\"", "'") + "\"}");
                return 1;
            }
        }
    }
}
