const cart = {
  items: 0,
  add() { this.items++; return this.items; },
  addLater() { return [1, 2].map(() => this.add()); },
};

console.log(cart.add());               // 1: called as cart.add(), so this is cart

const add = cart.add;                  // the function, detached from its object
try { add(); } catch (e) { console.log(e.constructor.name); }  // TypeError: this is undefined

console.log(cart.add.bind(cart)());    // 2: bind fixes this for good
console.log(cart.addLater());          // [3, 4]: arrow functions use the this of the code around them

export {};
