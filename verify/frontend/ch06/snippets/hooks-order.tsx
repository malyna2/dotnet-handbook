import { useState } from "react";

type User = { name: string };

function Profile({ user }: { user: User | null }) {
  const [theme] = useState("light");           // hook 1 on every render
  if (!user) return <p className={theme}>Signed out</p>;   // an early return...
  const [tab, setTab] = useState("orders");     // ...so hook 2 runs on some renders only
  return <button onClick={() => setTab("settings")}>{user.name}: {tab}</button>;
}

export { Profile };
