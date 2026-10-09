console.log("1: sync");
setTimeout(() => console.log("6: timer (a task)"), 0);
Promise.resolve().then(() => console.log("4: then (a microtask)"));

async function save() {
  console.log("2: save() up to its first await");
  await null;
  console.log("5: rest of save() (a microtask)");
}
save();
console.log("3: sync, after save() returned");

export {};
