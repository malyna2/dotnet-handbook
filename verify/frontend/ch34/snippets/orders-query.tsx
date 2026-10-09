import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

type Order = { id: number; status: string };

function useOrders() {
  return useQuery({
    queryKey: ["orders"],                       // the cache key: same key, same cache entry
    queryFn: async (): Promise<Order[]> => {
      const res = await fetch("/api/orders");
      if (!res.ok) throw new Error(`Orders failed: ${res.status}`);
      return res.json();
    },
    staleTime: 30_000,                          // fresh for 30 s: no refetch on mount or focus
  });
}

function OrderCount() {
  const { data, isPending, error } = useOrders();
  if (isPending) return <p>Loading…</p>;
  if (error) return <p role="alert">{error.message}</p>;
  return <p>{data.length} orders</p>;
}

function CancelButton({ id }: { id: number }) {
  const queryClient = useQueryClient();
  const cancel = useMutation({
    mutationFn: () => fetch(`/api/orders/${id}/cancel`, { method: "POST" }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["orders"] }),  // refetch the list
  });
  return <button onClick={() => cancel.mutate()} disabled={cancel.isPending}>Cancel</button>;
}

export { CancelButton, OrderCount, useOrders };
