// The fix for Chapter 6's "Find the bug" (printed in the answer).
import { useEffect, useState } from "react";

type Product = { id: number; name: string };

export function ProductSearch({ query }: { query: string }) {
  const [results, setResults] = useState<Product[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    fetch(`/api/products?q=${encodeURIComponent(query)}`, { signal: controller.signal })
      .then((res) => {
        if (!res.ok) throw new Error(`Search failed: ${res.status}`);
        return res.json();
      })
      .then((data: Product[]) => { setResults(data); setError(null); })
      .catch((err) => { if (err.name !== "AbortError") setError(err.message); });
    return () => controller.abort();   // a newer query cancels this one, and so does unmounting
  }, [query]);

  if (error) return <p role="alert">{error}</p>;
  return (
    <ul>
      {results.map((p) => <li key={p.id}>{p.name}</li>)}
    </ul>
  );
}
