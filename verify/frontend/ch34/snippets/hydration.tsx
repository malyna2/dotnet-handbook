function LastUpdated() {
  return <p>Updated at {new Date().toISOString()}</p>;   // a different value on server and client
}

export { LastUpdated };
