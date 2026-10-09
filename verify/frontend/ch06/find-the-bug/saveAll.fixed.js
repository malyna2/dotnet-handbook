// The fix for "Find the bug: the save that said done" (printed in the answer).
export async function saveAll(lines, api) {
  await Promise.all(lines.map((line) => api.save(line)));   // or for...of with await, one at a time
  return lines.length;
}

export async function onSaveClicked(lines, api, toast) {
  try {
    const count = await saveAll(lines, api);
    toast(`Saved ${count} lines`);
  } catch (e) {
    toast(`Save failed: ${e.message}`);
  }
}
