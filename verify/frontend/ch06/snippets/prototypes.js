class Animal {
  constructor(name) { this.name = name; }
  speak() { return `${this.name} makes a sound`; }
}

const rex = new Animal("Rex");
console.log(typeof Animal);                                // "function": a class is a function
console.log(Object.getPrototypeOf(rex) === Animal.prototype);  // true
console.log(Object.hasOwn(rex, "name"), Object.hasOwn(rex, "speak"));  // true false

Animal.prototype.speak = function () { return `${this.name} was patched`; };
console.log(rex.speak());   // "Rex was patched": the lookup happens at call time, on the chain

export {};
