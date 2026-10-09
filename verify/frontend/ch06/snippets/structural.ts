interface Point { x: number; y: number }

function distanceFromOrigin(p: Point): number {
  return Math.hypot(p.x, p.y);
}

const pin = { x: 3, y: 4, label: "Warehouse" };
console.log(distanceFromOrigin(pin));   // 5: pin has x and y, so it is a Point; no "implements"

class Pixel { constructor(public x: number, public y: number) {} }
console.log(distanceFromOrigin(new Pixel(6, 8)));   // 10: a class that never heard of Point

export {};
