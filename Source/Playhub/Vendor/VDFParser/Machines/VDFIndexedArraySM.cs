#nullable disable
using System.Collections.Generic;
namespace VDFParser.Machines {

    /// <summary>
    /// State Machine used to parse an indexed array from the VDF structure
    /// </summary>
    public class VDFIndexedArraySM {
        enum State {
            IndexIdentifier,
            Index,
            Value
        }
        State state;
        readonly List<string> result;
        readonly List<byte> tmpBuffer;

        public string[] ParsedArray {
            get {
                return result.ToArray();
            }
        }

        public void Reset() {
            result.Clear();
            tmpBuffer.Clear();
            state = State.IndexIdentifier;
        }

        public void Flush() {
            if(tmpBuffer.Count > 0) {
                result.Add(tmpBuffer.StringFromByteArray());
            }
        }

        public VDFIndexedArraySM() {
            result = new List<string>();
            tmpBuffer = new List<byte>();
        }

        public void Feed(byte b) {
            switch(state) {
                case State.IndexIdentifier:
                    if(b == 0x01) {
                        state = State.Index;
                        Flush();
                        tmpBuffer.Clear();
                    }
                    break;
                case State.Index:
                    if(b == 0x00) {
                        tmpBuffer.Clear();
                        state = State.Value;
                    }
                    break;
                case State.Value:
                    if(b == 0x00) {
                        result.Add(tmpBuffer.StringFromByteArray());
                        tmpBuffer.Clear();
                        state = State.IndexIdentifier;
                        break;
                    }
                    tmpBuffer.Add(b);
                    break;
            }
        }

        public void Feed(List<byte> b) { Feed(b.ToArray()); }

        public void Feed(byte[] input) {
            foreach(var b in input) {
                Feed(b);
            }
        }
    }
}
