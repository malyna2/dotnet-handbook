type Order = { id: number; total: number };

type LoadResult =
  | { kind: "ok"; order: Order }
  | { kind: "not-found" }
  | { kind: "error"; status: number };

function describe(result: LoadResult): string {
  switch (result.kind) {
    case "ok":
      return `Order ${result.order.id}: ${result.order.total}`;  // narrowed: order exists here
    case "not-found":
      return "No such order";
    case "error":
      return `Failed with ${result.status}`;
    default: {
      const unreachable: never = result;   // a new case that is not handled stops the build here
      return unreachable;
    }
  }
}

console.log(describe({ kind: "ok", order: { id: 7, total: 30 } }));  // "Order 7: 30"
console.log(describe({ kind: "error", status: 503 }));               // "Failed with 503"

export {};
