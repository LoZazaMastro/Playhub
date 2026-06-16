#nullable disable
using System.Collections.Generic;

namespace VDFParser.Machines {

    /// <summary>
    /// State machine used to parse fields out from a VDF structure
    /// </summary>
    public class VDFFieldsSM {
        enum State {
            BeginIndicator,         // 0x00
            Type,                   // ATM, 0x00 || 0x01 || 0x02
            Name,                   // [^0x00]+
            Value,                  // [^0x00]+
        }

        readonly Dictionary<string, byte[]> results;
        readonly List<byte> tmpBuffer;
        State state;
        byte lastByte;
        byte tmpFieldType;
        string tmpFieldName;

        public Dictionary<string, byte[]> Fields {
            get {
                return results;
            }
        }

        public VDFFieldsSM() {
            results = new Dictionary<string, byte[]>();
            tmpBuffer = new List<byte>();
        }

        public void Reset() {
            results.Clear();
            state = State.BeginIndicator;
            tmpBuffer.Clear();
            lastByte = 0x00;
            tmpFieldType = 0x00;
            tmpFieldName = null;
        }

        public bool Feed(byte b) {
            switch(state) {
                case State.BeginIndicator:
                    if(b == 0x00) {
                        state = State.Type;
                        tmpBuffer.Clear();
                    }
                    break;
                case State.Type:
                    if(b == 0x01 || b == 0x02 || b == 0x00) {
                        tmpFieldType = b;
                        state = State.Name;
                    } else {
                        state = State.BeginIndicator;
                    }
                    break;
                case State.Name:
                    if(b == 0x00) {
                        tmpFieldName = tmpBuffer.StringFromByteArray();
                        tmpBuffer.Clear();
                        state = State.Value;
                    } else {
                        tmpBuffer.Add(b);
                    }
                    break;
                case State.Value:
                    bool vdfEnd = false;
                    if(b == 0x08 && lastByte == 0x08) {
                        tmpBuffer.RemoveAt(tmpBuffer.Count - 1);
                        vdfEnd = true;
                    }
                    if((tmpFieldType == 0x02 && tmpBuffer.Count == 3) || (tmpFieldType == 0x01 && b == 0x00) || (tmpFieldType == 0x00 && b == 0x08 && lastByte == 0x08)) {
                        if(tmpFieldType == 0x02) {
                            tmpBuffer.Add(b);
                        }

                        if(!string.IsNullOrEmpty(tmpFieldName)) {
                            results.Add(tmpFieldName, tmpBuffer.ToArray());
                        }

                        tmpFieldName = null;
                        tmpBuffer.Clear();

                        if(vdfEnd) {
                            state = State.BeginIndicator;
                            return true;
                        }
                        state = State.Type;

                    } else {
                        tmpBuffer.Add(b);
                    }
                    break;
            }
            lastByte = b;
            return false;
        }
    }
}
