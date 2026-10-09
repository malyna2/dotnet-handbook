import { useState } from "react";

function Quantity() {
  const [count, setCount] = useState(0);

  function addThreeWrong() {
    setCount(count + 1);   // count is 0 in this render: "set to 1"
    setCount(count + 1);   // "set to 1" again
    setCount(count + 1);   // the next render shows 1
  }

  function addThree() {
    setCount((c) => c + 1);   // updaters are queued and run in order
    setCount((c) => c + 1);
    setCount((c) => c + 1);   // the next render shows 3
  }

  return (
    <>
      <output>{count}</output>
      <button onClick={addThreeWrong}>+3 (wrong)</button>
      <button onClick={addThree}>+3</button>
    </>
  );
}

export { Quantity };
