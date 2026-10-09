const withVar = [];
for (var i = 0; i < 3; i++) withVar.push(() => i);
console.log(withVar.map((f) => f()));  // [3, 3, 3]: one i for the whole loop

const withLet = [];
for (let j = 0; j < 3; j++) withLet.push(() => j);
console.log(withLet.map((f) => f()));  // [0, 1, 2]: a fresh j per iteration

export {};
