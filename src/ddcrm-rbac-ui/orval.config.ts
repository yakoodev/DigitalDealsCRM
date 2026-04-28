import { defineConfig } from "orval";

export default defineConfig({
  externalApi: {
    input: {
      target: "../../docs/api-contracts/openapi-external.yaml",
    },
    output: {
      target: "./src/generated/external-api.ts",
      client: "fetch",
      mode: "single",
      override: {
        fetch: {
          useRuntimeFetcher: true,
        },
      },
    },
  },
});
