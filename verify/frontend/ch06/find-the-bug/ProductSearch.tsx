// Chapter 6, Exercises, "Find the bug": printed in the chapter exactly as below.
import { useEffect, useState } from "react";

type Product = { id: number; name: string };

export function ProductSearch({ query }: { query: string }) {
  const [results, setResults] = useState<Product[]>([]);

  useEffect(() => {
    fetch(`/api/products?q=${encodeURIComponent(query)}`)
      .then((res) => res.json())
      .then((data: Product[]) => setResults(data));
  }, [query]);

  return (
    <ul>
      {results.map((p) => <li key={p.id}>{p.name}</li>)}
    </ul>
  );
}
