import { Component, Injectable, inject, signal } from "@angular/core";
import { HttpClient } from "@angular/common/http";

type Order = { id: number; total: number };

@Injectable({ providedIn: "root" })            // one instance for the app: a singleton
export class OrderService {
  private http = inject(HttpClient);
  getOrders() { return this.http.get<Order[]>("/api/orders"); }   // an Observable
}

@Component({
  selector: "app-orders",
  template: `@for (o of orders(); track o.id) { <li>{{ o.id }}: {{ o.total }}</li> }`,
})
export class OrdersComponent {
  orders = signal<Order[]>([]);
  constructor() { inject(OrderService).getOrders().subscribe((o) => this.orders.set(o)); }
}
