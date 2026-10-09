// Runs a chapter snippet (imported as a module) and returns what it printed with console.log.
// One argument per call is kept as is; several become an array.
import { vi } from "vitest";

export async function logsOf(load: () => Promise<unknown>, settleMs = 0): Promise<unknown[]> {
  const logs: unknown[] = [];
  const spy = vi.spyOn(console, "log").mockImplementation((...args: unknown[]) => {
    logs.push(args.length === 1 ? args[0] : args);
  });
  try {
    await load();
    if (settleMs > 0) await new Promise((resolve) => setTimeout(resolve, settleMs));
  } finally {
    spy.mockRestore();
  }
  return logs;
}
