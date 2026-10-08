-- Companion to SeekVsScan: the same nvarchar-parameter query against a varchar column, once with a
-- SQL collation (the server default here) and once with a Windows collation. capture.sh runs it.
SET NOCOUNT ON;
DROP TABLE IF EXISTS dbo.EmailsSql, dbo.EmailsWindows;
CREATE TABLE dbo.EmailsSql (Id int IDENTITY PRIMARY KEY, Email varchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL);
CREATE TABLE dbo.EmailsWindows (Id int IDENTITY PRIMARY KEY, Email varchar(100) COLLATE Latin1_General_CI_AS NOT NULL);
INSERT dbo.EmailsSql (Email) SELECT CONCAT('user', value, '@example.com') FROM GENERATE_SERIES(1, 200000);
INSERT dbo.EmailsWindows (Email) SELECT Email FROM dbo.EmailsSql;
CREATE INDEX IX_Email ON dbo.EmailsSql (Email);
CREATE INDEX IX_Email ON dbo.EmailsWindows (Email);
GO
SET STATISTICS IO ON;
SET STATISTICS PROFILE ON;
PRINT '--- SQL collation, nvarchar parameter';
EXEC sp_executesql N'SELECT Id FROM dbo.EmailsSql WHERE Email = @p', N'@p nvarchar(100)', @p = N'user42@example.com';
PRINT '--- Windows collation, nvarchar parameter';
EXEC sp_executesql N'SELECT Id FROM dbo.EmailsWindows WHERE Email = @p', N'@p nvarchar(100)', @p = N'user42@example.com';
GO
