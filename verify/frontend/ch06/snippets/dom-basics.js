const btn = document.querySelector("#save");
btn.addEventListener("click", () => {
  document.querySelector("#status").textContent = "Saving...";
});

export {};
