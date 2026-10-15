-- The tiny shop behind Chapter 7's SQL querying examples. Portable: the same file runs on
-- PostgreSQL 17 and SQL Server 2022. Ada has four orders (two on the same day, two of 50.00),
-- Cleo has no city, Dan has no orders, and order 16 is a guest checkout with no customer.
DROP TABLE IF EXISTS OrderLines;
DROP TABLE IF EXISTS Orders;
DROP TABLE IF EXISTS Customers;
DROP TABLE IF EXISTS Categories;

CREATE TABLE Customers (
    Id   INT PRIMARY KEY,
    Name VARCHAR(50) NOT NULL,
    City VARCHAR(50) NULL
);

CREATE TABLE Orders (
    Id         INT PRIMARY KEY,
    CustomerId INT NULL REFERENCES Customers (Id),   -- NULL: a guest checkout
    Status     VARCHAR(20) NOT NULL,
    Total      DECIMAL(10,2) NOT NULL,
    CreatedAt  DATE NOT NULL
);

CREATE TABLE OrderLines (
    Id       INT PRIMARY KEY,
    OrderId  INT NOT NULL REFERENCES Orders (Id),
    Product  VARCHAR(50) NOT NULL,
    Quantity INT NOT NULL,
    Price    DECIMAL(10,2) NOT NULL
);

CREATE TABLE Categories (
    Id       INT PRIMARY KEY,
    ParentId INT NULL REFERENCES Categories (Id),
    Name     VARCHAR(50) NOT NULL
);

INSERT INTO Customers (Id, Name, City) VALUES
    (1, 'Ada',  'Kyiv'),
    (2, 'Ben',  'Lviv'),
    (3, 'Cleo', NULL),
    (4, 'Dan',  'Kyiv');

INSERT INTO Orders (Id, CustomerId, Status, Total, CreatedAt) VALUES
    (10, 1,    'Paid',      100.00, '2026-01-05'),
    (11, 1,    'Paid',       50.00, '2026-02-10'),
    (12, 1,    'Open',       50.00, '2026-02-10'),
    (13, 2,    'Paid',       80.00, '2026-01-20'),
    (14, 2,    'Cancelled',  20.00, '2026-02-02'),
    (15, 3,    'Paid',       70.00, '2026-03-15'),
    (16, NULL, 'Paid',       40.00, '2026-03-20'),
    (17, 1,    'Paid',       30.00, '2026-03-01');

INSERT INTO OrderLines (Id, OrderId, Product, Quantity, Price) VALUES
    (1,  10, 'Keyboard', 1, 60.00),
    (2,  10, 'Mouse',    2, 20.00),
    (3,  11, 'Monitor',  1, 50.00),
    (4,  12, 'Cable',    5, 10.00),
    (5,  13, 'Desk',     1, 80.00),
    (6,  14, 'Lamp',     1, 20.00),
    (7,  15, 'Chair',    1, 50.00),
    (8,  15, 'Mat',      2, 10.00),
    (9,  16, 'Mouse',    2, 20.00),
    (10, 17, 'Cable',    3, 10.00);

INSERT INTO Categories (Id, ParentId, Name) VALUES
    (1, NULL, 'Electronics'),
    (2, 1,    'Computers'),
    (3, 2,    'Laptops'),
    (4, 1,    'Accessories');
