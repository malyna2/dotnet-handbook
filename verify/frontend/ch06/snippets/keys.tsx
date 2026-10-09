import { useState } from "react";

type Item = { id: number; name: string };

function Line({ item }: { item: Item }) {
  const [note, setNote] = useState("");   // state belongs to the position React matched
  return (
    <li>
      {item.name} <input aria-label={`Note for ${item.name}`} value={note}
                         onChange={(e) => setNote(e.target.value)} />
    </li>
  );
}

function Basket({ items, keyByIndex }: { items: Item[]; keyByIndex: boolean }) {
  return (
    <ul>
      {items.map((item, index) =>
        <Line key={keyByIndex ? index : item.id} item={item} />)}
    </ul>
  );
}

export { Basket };
