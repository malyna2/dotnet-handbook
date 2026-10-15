SELECT Name FROM Customers WHERE City = NULL;     -- no rows, whatever the data
SELECT Name FROM Customers WHERE City IS NULL;    -- Cleo
SELECT Name FROM Customers WHERE City <> 'Kyiv';  -- Ben only: Cleo's city is unknown
