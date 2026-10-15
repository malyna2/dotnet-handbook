-- A SELECT alias is not visible in WHERE on either engine: SELECT is evaluated after WHERE.
SELECT Id, Total * 2 AS Doubled FROM Orders WHERE Doubled > 100;
