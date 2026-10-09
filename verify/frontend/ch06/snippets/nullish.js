const settings = { pageSize: 0, title: "" };

console.log(settings.pageSize || 20);  // 20: 0 is falsy, so || throws it away
console.log(settings.pageSize ?? 20);  // 0: ?? replaces only null and undefined
console.log(settings.title || "Untitled");  // "Untitled"
console.log(settings.owner?.name);     // undefined: ?. stops at the missing owner

export {};
