localStorage.setItem("cart", { items: 2 });
console.log(localStorage.getItem("cart"));   // "[object Object]": values are always strings

localStorage.setItem("cart", JSON.stringify({ items: 2 }));
console.log(JSON.parse(localStorage.getItem("cart")).items);   // 2

export {};
