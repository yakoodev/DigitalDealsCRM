export function createIdempotencyKey(prefix = "ddcrm-ui") {
  return `${prefix}-${crypto.randomUUID().replaceAll("-", "")}`;
}
