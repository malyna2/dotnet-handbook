const form = document.querySelector("#signup");

form.addEventListener("submit", async (event) => {
  event.preventDefault();                  // stop the browser's own full-page POST
  const body = new FormData(form);         // every field with a name attribute
  const res = await fetch(form.action, { method: "POST", body });
  form.querySelector("output").textContent = res.ok ? "Saved" : `Failed: ${res.status}`;
});

export {};
