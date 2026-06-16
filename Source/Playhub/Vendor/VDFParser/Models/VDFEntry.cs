#nullable disable
namespace VDFParser.Models {

    /// <summary>
    /// Represents a VDF entry
    /// Reference: https://developer.valvesoftware.com/wiki/Add_Non-Steam_Game
    /// </summary>
    public class VDFEntry {

        public int Index { get; set; }

        [VDFField("appid")]
        public int appid { get; set; }

        [VDFField("AppName")]
        public string AppName { get; set; }

        [VDFField("Exe")]
        public string Exe { get; set; }

        [VDFField("StartDir")]
        public string StartDir { get; set; }

        [VDFField("icon")]
        public string Icon { get; set; }

        [VDFField("ShortcutPath")]
        public string ShortcutPath { get; set; }

        [VDFField("LaunchOptions")]
        public string LaunchOptions { get; set; }

        [VDFField("IsHidden", Type = VDFFieldType.Integer)]
        public int IsHidden { get; set; }

        [VDFField("AllowDesktopConfig", Type = VDFFieldType.Integer)]
        public int AllowDesktopConfig { get; set; }

        [VDFField("AllowOverlay", Type = VDFFieldType.Integer)]
        public int AllowOverlay { get; set; }

        [VDFField("openvr", Type = VDFFieldType.Integer)]
        public int OpenVR { get; set; }

        [VDFField("Devkit", Type = VDFFieldType.Integer)]
        public int Devkit { get; set; }

        [VDFField("DevkitGameID")]
        public string DevkitGameID { get; set; }

        [VDFField("LastPlayTime", Type = VDFFieldType.Integer)]
        public int LastPlayTime { get; set; }

        [VDFField("tags", Type = VDFFieldType.IndexedArray)]
        public string[] Tags { get; set; }

        public override string ToString() {
            return string.Format("[VDFEntry: AppName={0}, Exe={1}, StartDir={2}, Icon={3}, ShortcutPath={4}, LaunchOptions={5}, IsHidden={6}, AllowDesktopConfig={7}, AllowOverlay={8}, OpenVR={9}, Devkit={10}, DevkitGameID={11}, LastPlayTime={12}, Tags={13}]", AppName, Exe, StartDir, Icon, ShortcutPath, LaunchOptions, IsHidden, AllowDesktopConfig, AllowOverlay, OpenVR, Devkit, DevkitGameID, LastPlayTime, Tags == null ? "(null)" : string.Join(", ", Tags));
        }
    }
}
