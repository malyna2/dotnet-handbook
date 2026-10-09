// One listener on the list handles clicks on every row, including rows added later.
const list = document.querySelector("#orders");

list.addEventListener("click", (event) => {
  const button = event.target.closest("button[data-order-id]");
  if (!button || !list.contains(button)) return;   // a click elsewhere in the list
  cancelOrder(Number(button.dataset.orderId));
});

export {};
