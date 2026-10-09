type Order = { id: number; total: number };

const fromServer = JSON.parse('{"id": "42", "total": "19.90"}');   // type: any
const order = fromServer as Order;   // a promise to the compiler, checked by nobody

console.log(typeof order.id);        // "string": the type said number
console.log(order.total + 1);        // "19.901": string concatenation, no error anywhere

function isOrder(value: unknown): value is Order {   // a runtime check the compiler trusts
  return typeof value === "object" && value !== null
    && typeof (value as Order).id === "number"
    && typeof (value as Order).total === "number";
}
console.log(isOrder(fromServer));    // false

export {};
