namespace AudioRoleTests
{
    public sealed class Observation
    {
        public QuickSettingsAudio.DefaultChangeResult Result;
        public string[] State;
        public string[] Writes;
    }

    public static class Fixture
    {
        public sealed class MicrophoneObservation
        {
            public QuickSettingsAudio.MicrophoneChangeResult Result;
            public string WrittenEndpoint;
            public string DefaultEndpoint;
        }

        public static MicrophoneObservation RunMicrophone(int scenario)
        {
            string captured = scenario == 0 ? "mic-b" : "mic-a";
            var observation = new MicrophoneObservation { DefaultEndpoint = captured };
            observation.Result = QuickSettingsAudio.MicrophoneEndpointTransaction.Apply(
                scenario == 2 ? null : "mic-a", captured, 75, () =>
                {
                    observation.DefaultEndpoint = "mic-b";
                    observation.WrittenEndpoint = captured;
                    if (scenario == 4) throw new System.Exception("write failed");
                    return scenario == 3 ? 20 : 75;
                });
            return observation;
        }

        public static Observation Run(int scenario)
        {
            var state = new string[] { "console", "multimedia", "communications" };
            var writes = new System.Collections.Generic.List<string>();
            bool failed = false;
            if (scenario == 11) state = new string[] { "new", "NEW", "new" };
            System.Func<QuickSettingsAudio.ERole, string> read = role =>
            {
                int i = (int)role;
                if (scenario == 9 && i == 1) return "";
                if (scenario == 13 && failed && i == 1) throw new System.Exception("read unavailable");
                return state[i];
            };
            System.Func<QuickSettingsAudio.ERole, string, int> write = (role, endpoint) =>
            {
                int i = (int)role;
                writes.Add(i + ":" + endpoint);
                if (endpoint == "new")
                {
                    if (scenario >= 1 && scenario <= 3 && i == scenario - 1)
                    { failed = true; return unchecked((int)0x80004005); }
                    if (scenario == 10 && i == 1) return 0;
                    state[i] = endpoint;
                    if ((scenario >= 4 && scenario <= 6 && i == scenario - 4) ||
                        ((scenario == 7 || scenario == 8 || scenario == 13) && i == 2))
                    {
                        failed = true;
                        if (scenario == 8) state[0] = "external";
                        return unchecked((int)0x80004005);
                    }
                    if (scenario == 12 && i == 1)
                    { failed = true; throw new System.Exception("response lost after change"); }
                }
                else
                {
                    if (scenario == 7 && i == 1) return unchecked((int)0x80004005);
                    state[i] = endpoint;
                }
                return 0;
            };
            return new Observation {
                Result = QuickSettingsAudio.DefaultRoleTransaction.Apply("new",
                    scenario == 16 ? -1 : scenario == 15 ? 1 : 0,
                    scenario == 14 ? 1 : 0, read, write),
                State = state, Writes = writes.ToArray()
            };
        }
    }
}
