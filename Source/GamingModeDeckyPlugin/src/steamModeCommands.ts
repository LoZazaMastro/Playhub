export interface SteamModeCommand {
  revision: number;
  token: string;
  kind: "exit" | "barrier";
}

export function steamModeCommand(value: any): SteamModeCommand | undefined {
  return value && Number.isSafeInteger(value.revision) && value.revision > 0
    && typeof value.token === "string" && /^[a-f0-9]{32}$/.test(value.token)
    && (value.kind === "exit" || value.kind === "barrier")
    ? { revision: value.revision, token: value.token, kind: value.kind } : undefined;
}

interface Dependencies {
  alive(): boolean;
  exit(): unknown;
  /** Each native observation is bounded by the integration, including a lost CEF reply. */
  readDesktop(): Promise<boolean>;
  ack(command: SteamModeCommand, completed: boolean): Promise<unknown>;
  now(): number;
  delay(ms: number): Promise<void>;
}

interface Queue {
  tail: Promise<unknown>;
  highest: number;
  unsettledExit: boolean;
  completed: Map<string, boolean>;
  identity?: SteamModeCommand;
}

/** Shared by old and new plugin roots until every already-submitted native exit settles. */
export function installSteamModeCommands(host: any, deps: Dependencies) {
  const key = "__playhubSteamModeCommandsV1";
  const state: Queue = host[key] ?? (host[key] = {
    tail: Promise.resolve(), highest: 0, unsettledExit: false, completed: new Map<string, boolean>(),
  });
  const confirmDesktop = async () => {
    const deadline = deps.now() + 1000;
    for (let attempt = 0; attempt < 20 && deps.now() <= deadline; attempt++) {
      if (!deps.alive()) return false;
      try { if (await deps.readDesktop()) return true; } catch { /* Retain the unsettled exit. */ }
      await deps.delay(75);
    }
    return false;
  };
  return {
    async process(value: unknown): Promise<boolean> {
      const command = steamModeCommand(value);
      if (!command || !deps.alive() || command.revision < state.highest) return false;
      if (command.revision === state.highest && state.identity
          && (command.token !== state.identity.token || command.kind !== state.identity.kind)) return false;
      state.highest = Math.max(state.highest, command.revision);
      state.identity = command;
      const task = state.tail.then(async () => {
        if (!deps.alive() || command.revision < state.highest) return false;
        const identity = `${command.revision}:${command.token}:${command.kind}`;
        let complete = state.completed.get(identity);
        if (complete !== true) {
          if (command.kind === "exit") {
            // From this point an explicit open must wait for native desktop
            // readback, even when this plugin root unloads during that read.
            if (complete === undefined) {
              state.unsettledExit = true;
              // Remember dispatch BEFORE invoking the native action. A later
              // poll retries only readback, even after an initial false ACK.
              state.completed.set(identity, false);
              try { void Promise.resolve(deps.exit()).catch(() => {}); } catch { /* Readback still decides whether Steam settled. */ }
            }
            complete = await confirmDesktop();
            if (complete) state.unsettledExit = false;
          } else {
            complete = !state.unsettledExit || await confirmDesktop();
            if (complete) state.unsettledExit = false;
          }
          state.completed.set(identity, complete);
          while (state.completed.size > 16) state.completed.delete(state.completed.keys().next().value!);
        }
        // The previous native action is now complete. Acknowledgement itself
        // performs no UI action and cannot hold the shared queue on a lost HTTP reply.
        if (deps.alive()) void deps.ack(command, complete).catch(() => {});
        return complete;
      });
      state.tail = task.catch(() => false);
      return task;
    },
  };
}
