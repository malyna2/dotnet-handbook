type Order = { id: number; total: number };

async function loadOrder(id: number, signal?: AbortSignal): Promise<Order> {
  const res = await fetch(`/api/orders/${id}`, { credentials: "include", signal });
  if (!res.ok) throw new Error(`Order ${id} failed: ${res.status}`);  // fetch does not throw on 404 or 500
  return (await res.json()) as Order;
}

export { loadOrder };
