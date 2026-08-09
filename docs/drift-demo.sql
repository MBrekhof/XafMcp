-- XafMcp drift demo — deliberate, EF-read-compatible schema drift (spec §3 Schema).
-- Apply:  sqlcmd -S "(localdb)\MSSQLLocalDB" -d XafMcp -i docs\drift-demo.sql
-- Undo:   drop & recreate the database (see README).

-- 1. Column the model does not know
ALTER TABLE dbo.Customers ADD LegacyCode nvarchar(20) NULL;

-- 2. Widen + nullability flip on a model column (model: nvarchar(100) NOT NULL)
ALTER TABLE dbo.Regions ALTER COLUMN Name nvarchar(200) NULL;

-- 3. A whole table the model does not know
CREATE TABLE dbo.LegacyImport (Id int NOT NULL PRIMARY KEY, Payload nvarchar(max) NULL);
