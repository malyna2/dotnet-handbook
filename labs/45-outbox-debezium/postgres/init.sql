-- The order, the outbox row Debezium reads, and the consumer's tables.
CREATE TABLE orders (
    id          bigint PRIMARY KEY,
    customer_id bigint NOT NULL,
    total       numeric(10,2) NOT NULL,
    created_at  timestamptz NOT NULL DEFAULT now()
);

-- The column names are the Outbox Event Router's defaults.
CREATE TABLE outbox (
    id            uuid PRIMARY KEY,
    aggregatetype varchar(255) NOT NULL,   -- routes to topic outbox.event.<aggregatetype>
    aggregateid   varchar(255) NOT NULL,   -- becomes the Kafka message key, so one order stays in one partition
    type          varchar(255) NOT NULL,   -- the event type
    payload       jsonb NOT NULL,
    created_at    timestamptz NOT NULL DEFAULT now()
);

-- Consumer side: the claim (one row per event) and the effect.
CREATE TABLE processed_messages (
    event_id     uuid PRIMARY KEY,
    processed_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE loyalty_points (
    id          bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    order_id    bigint NOT NULL,
    customer_id bigint NOT NULL,
    points      int NOT NULL
);
