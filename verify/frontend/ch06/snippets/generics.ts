function firstBy<T, K extends keyof T>(items: T[], key: K, value: T[K]): T | undefined {
  return items.find((item) => item[key] === value);
}

const users = [{ id: 1, email: "a@example.com" }, { id: 2, email: "b@example.com" }];
console.log(firstBy(users, "email", "b@example.com")?.id);   // 2
// firstBy(users, "emial", "x")  -> compile error: "emial" is not a key of the user type
// firstBy(users, "id", "2")     -> compile error: id is a number

export {};
