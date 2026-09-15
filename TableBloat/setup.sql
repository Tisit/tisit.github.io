/*
    setup.sql  --  Bulk-insert table-bloat reproducer
    ------------------------------------------------------------------
    (Re)creates the TableBloatTest database and the test table.
    Run standalone in SSMS/sqlcmd, or let Run.cs execute it.
    Connect with integrated security.

    SAFE TO RE-RUN: it drops and recreates the database every time.
*/

SET NOCOUNT ON;
GO

IF DB_ID(N'TableBloatTest') IS NOT NULL
BEGIN
    ALTER DATABASE [TableBloatTest] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [TableBloatTest];
END
GO

CREATE DATABASE [TableBloatTest];
GO

-- Modest pre-size so autogrowth doesn't add noise (the bloated run reaches ~300 MB).
ALTER DATABASE [TableBloatTest] MODIFY FILE (NAME = N'TableBloatTest',     SIZE = 512MB, FILEGROWTH = 256MB);
ALTER DATABASE [TableBloatTest] MODIFY FILE (NAME = N'TableBloatTest_log', SIZE = 256MB, FILEGROWTH = 256MB);
GO

ALTER DATABASE [TableBloatTest] SET RECOVERY SIMPLE;
GO

USE [TableBloatTest];
GO

-------------------------------------------------------------------------------
-- Test table: clustered PK on (CreatedOn, ID) + 5 payload columns.
-- Payload CHAR(350) fixes the row at ~404 bytes.
-------------------------------------------------------------------------------
CREATE TABLE dbo.BloatTest
(
    CreatedOn DATETIME          NOT NULL,
    ID        INT IDENTITY(1,1) NOT NULL,
    Amount    DECIMAL(18,2)     NOT NULL,
    Quantity  INT               NOT NULL,
    Status    TINYINT           NOT NULL,
    RefGuid   UNIQUEIDENTIFIER  NOT NULL,
    Payload   CHAR(350)         NOT NULL,
    CONSTRAINT PK_BloatTest PRIMARY KEY CLUSTERED (CreatedOn, ID)
);
GO

PRINT 'setup.sql complete: database [TableBloatTest], table dbo.BloatTest ready.';
GO