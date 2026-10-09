import { computed, signal } from "@angular/core";

const quantity = signal(2);
const price = signal(9.5);
const total = computed(() => quantity() * price());   // remembers which signals it read

console.log(total());   // 19
quantity.set(3);        // marks total stale; nothing recomputes yet
console.log(total());   // 28.5: recomputed on the next read
