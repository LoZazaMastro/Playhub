#nullable disable
using System;
using System.Linq;
using System.Collections.Generic;
using VDFParser.Models;

namespace VDFParser.Machines {

    /// <summary>
    /// State machine used to parse a VDF structure
    /// </summary>
    public class VDFSM {
        enum MainSMState {
            IndexBeginIndicator,    // 0x00
            IndexValue,             // [^0x00]+
            IndexEndIndicator,      // 0x00
            Fields,                 // 0x00
        }

        readonly List<byte> tmpBuffer;
        readonly VDFIndexedArraySM arraySM;
        readonly VDFFieldsSM fieldsSM;
        static readonly Type entryType = typeof(VDFEntry);

        List<VDFEntry> entries;
        VDFEntry currentEntry;

        MainSMState mainState;

        public VDFEntry[] Entries {
            get {
                return entries.ToArray();
            }
        }

        public VDFSM() {
            entries = new List<VDFEntry>();
            tmpBuffer = new List<byte>();
            mainState = MainSMState.IndexBeginIndicator;

            arraySM = new VDFIndexedArraySM();
            fieldsSM = new VDFFieldsSM();
        }

        public void Flush() {
            if(currentEntry != null) {
                entries.Add(currentEntry);
            }
            currentEntry = null;
        }

        public void Feed(byte b) {
            switch(mainState) {
                case MainSMState.IndexBeginIndicator:
                    if(b == 0x00) {
                        Flush();
                        mainState = MainSMState.IndexValue;
                        tmpBuffer.Clear();
                        fieldsSM.Reset();
                    }
                    break;
                case MainSMState.IndexValue:
                    if(b == 0x00) {
                        int indexResult;
                        if(int.TryParse(tmpBuffer.StringFromByteArray(), out indexResult)) {
                            currentEntry = new VDFEntry();
                            currentEntry.Index = indexResult;
                            mainState = MainSMState.Fields;
                        }

                        fieldsSM.Feed(b);
                        break;
                    }
                    tmpBuffer.Add(b);
                    break;
                case MainSMState.Fields:
                    if(fieldsSM.Feed(b)) {
                        foreach(KeyValuePair<string, byte[]> k in fieldsSM.Fields) {
                            FillEntry(k.Key, k.Value);
                        }
                        mainState = MainSMState.IndexBeginIndicator;
                    }
                    break;
            }
        }


        void FillEntry(string fieldName, byte[] value) {
            var props = from p in entryType.GetProperties()
                        where Attribute.IsDefined(p, typeof(VDFField))
                        where p.Name.Equals(fieldName, StringComparison.InvariantCultureIgnoreCase)
                        select p;
            var prop = props.FirstOrDefault();
            if(prop == null) {
                return;
            }

            object result = null;
            if(prop.PropertyType == typeof(int)) {
                result = BitConverter.ToInt32(value, 0);
            } else if(prop.PropertyType == typeof(string)) {
                result = value.StringFromByteArray();
            } else if(prop.PropertyType.IsArray && prop.PropertyType.GetElementType() == typeof(string)) {
                result = parseIndexedArray(value);
            } else {
                return;
            }

            prop.SetValue(currentEntry, result);
        }

        string[] parseIndexedArray(byte[] input) {
            if(input.Length < 5) {
                return new string[] { };
            }
            arraySM.Reset();
            arraySM.Feed(input);
            arraySM.Flush();
            return arraySM.ParsedArray;
        }
    }
}
