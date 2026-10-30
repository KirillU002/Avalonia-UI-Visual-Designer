-- Execute only inside a new isolated test database.
CREATE TABLE dbo.Customers (Id int NOT NULL PRIMARY KEY, Name nvarchar(80) NOT NULL, Email nvarchar(100) NULL, City nvarchar(50) NOT NULL);
INSERT INTO dbo.Customers VALUES (101,N'Анна SQL',N'anna@example.test',N'Казань'),(202,N'Борис SQL',NULL,N'Тула'),(303,N'Вера SQL',N'vera@example.test',N'Омск');
CREATE TABLE dbo.Orders (Id int NOT NULL PRIMARY KEY, CustomerId int NOT NULL, Amount decimal(12,2) NOT NULL);
INSERT INTO dbo.Orders VALUES (11,101,19.50),(22,303,720.25);
CREATE TABLE dbo.EmptyTable (Id int NOT NULL PRIMARY KEY, Name nvarchar(80) NULL);
