console.log(1 + "2");            // "12": + with a string concatenates
console.log("3" * "4");          // 12: * converts both to numbers
console.log(0 == "");            // true: "" becomes 0
console.log(0 == "0");           // true: "0" becomes 0
console.log("" == "0");          // false: two strings
console.log(null == undefined);  // true: a special rule
console.log(null == 0);          // false
console.log(NaN === NaN);        // false: use Number.isNaN
console.log(typeof null);        // "object": a historic bug

export {};
