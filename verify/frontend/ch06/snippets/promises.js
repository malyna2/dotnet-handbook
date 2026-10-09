const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

let start = Date.now();
await delay(100);
await delay(100);
console.log(Date.now() - start >= 200);   // true: the second wait starts after the first ends

start = Date.now();
await Promise.all([delay(100), delay(100)]);
console.log(Date.now() - start < 200);    // true: both waits run at the same time

export {};
