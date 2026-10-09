import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { OrderList } from "./OrderList";

test("cancelling an order reports that order's id", async () => {
  const onCancel = vi.fn();
  render(<OrderList orders={[{ id: 7, customer: "Ada", total: 12 }]} onCancel={onCancel} />);

  await userEvent.click(screen.getByRole("button", { name: "Cancel" }));   // as a user would

  expect(onCancel).toHaveBeenCalledWith(7);
});

test("an empty list says so instead of rendering an empty table", () => {
  render(<OrderList orders={[]} onCancel={() => {}} />);
  expect(screen.getByText("No orders yet.")).toBeTruthy();
});
