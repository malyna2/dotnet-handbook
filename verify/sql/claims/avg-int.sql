-- Dialect difference: AVG of an INT column. Quantities sum to 19 over 10 lines.
-- SQL Server returns an int (1, integer division); PostgreSQL returns numeric (1.9...).
SELECT AVG(Quantity) AS AvgQuantity, AVG(1.0 * Quantity) AS AvgExact FROM OrderLines;
