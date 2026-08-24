import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { afterEach } from "vitest";

afterEach(async () => {
  cleanup();

  // React DOM 19.2 can leave passive-effect scheduler callbacks queued on
  // Node's setImmediate after unmount. Keep jsdom alive long enough to drain
  // those callbacks before Vitest removes window between test files.
  // https://github.com/facebook/react/issues/37100
  for (let turn = 0; turn < 3; turn += 1) {
    await new Promise((resolve) => setTimeout(resolve, 0));
  }
});
