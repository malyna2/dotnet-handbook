type Order = { id: number; customer: string; total: number };

type OrderRowProps = { order: Order; onCancel: (id: number) => void };

function OrderRow({ order, onCancel }: OrderRowProps) {
  return (
    <li>
      {order.customer}: {order.total.toFixed(2)}
      <button onClick={() => onCancel(order.id)}>Cancel</button>
    </li>
  );
}

function OrderList({ orders, onCancel }: { orders: Order[]; onCancel: (id: number) => void }) {
  if (orders.length === 0) return <p>No orders yet.</p>;
  return <ul>{orders.map((o) => <OrderRow key={o.id} order={o} onCancel={onCancel} />)}</ul>;
}

export { OrderList };
