// Optional agent: background requests back off together while it is absent.
// Explicit status checks can still detect a new installation immediately.
export function createAgentAvailability(now: () => number = Date.now) {
  let retryAt = 0;
  return {
    canRequest: () => now() >= retryAt,
    available: () => { retryAt = 0; },
    unavailable: () => { retryAt = now() + 30000; },
  };
}
export const agentAvailability = createAgentAvailability();
