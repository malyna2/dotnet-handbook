-- Run as the Entra admin. Creates a database user for the web app's system-assigned identity
-- (its name is the app's name) and grants it only what the app needs.
CREATE USER [$(APP_NAME)] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [$(APP_NAME)];
ALTER ROLE db_datawriter ADD MEMBER [$(APP_NAME)];
