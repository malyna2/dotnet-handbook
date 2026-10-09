// Chapter 6, Exercises, "Find the bug: the save that said done": printed in the chapter as below.
export async function saveAll(lines, api) {
  lines.forEach(async (line) => {
    await api.save(line);
  });
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
