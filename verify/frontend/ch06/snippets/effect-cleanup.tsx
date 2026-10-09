import { useEffect } from "react";

function ChatRoom({ roomId }: { roomId: string }) {
  useEffect(() => {
    const connection = createConnection(roomId);   // the effect reads roomId...
    connection.connect();
    return () => connection.disconnect();          // ...cleanup undoes it before the next run
  }, [roomId]);                                    // ...so roomId is a dependency

  return <h2>Room {roomId}</h2>;
}

// Supplied by the test (a fake connection that records what happened).
declare function createConnection(roomId: string): { connect(): void; disconnect(): void };

export { ChatRoom };
