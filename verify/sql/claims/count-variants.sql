-- COUNT(*) counts rows, COUNT(col) counts non-NULL values, COUNT(DISTINCT col) distinct non-NULL values: 4, 3, 2.
SELECT COUNT(*) AS AllRows, COUNT(City) AS WithCity, COUNT(DISTINCT City) AS Cities FROM Customers;

-- NULL forms one group of its own in GROUP BY (and one partition in PARTITION BY).
SELECT City, COUNT(*) AS Customers FROM Customers GROUP BY City;

-- An aggregate over no rows: COUNT gives 0, SUM gives NULL.
SELECT COUNT(*) AS NoRows, SUM(Total) AS SumOfNothing FROM Orders WHERE Status = 'Refunded';
