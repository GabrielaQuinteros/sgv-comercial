SET XACT_ABORT ON;
GO
IF OBJECT_ID('dbo.Users', 'U') IS NULL
CREATE TABLE dbo.Users (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    Username NVARCHAR(100) NOT NULL UNIQUE,
    PasswordHash VARCHAR(256) NOT NULL,
    Role VARCHAR(20) NOT NULL CHECK (Role IN ('Admin','Ventas','Inventario','Auditor')),
    Active BIT NOT NULL DEFAULT 1
);
GO
IF OBJECT_ID('dbo.Products', 'U') IS NULL
CREATE TABLE dbo.Products (
    Id INT IDENTITY PRIMARY KEY,
    Sku NVARCHAR(50) NOT NULL UNIQUE,
    Name NVARCHAR(200) NOT NULL,
    Price DECIMAL(18,2) NOT NULL CHECK (Price >= 0),
    Stock INT NOT NULL DEFAULT 0 CHECK (Stock >= 0),
    MinimumStock INT NOT NULL DEFAULT 5 CHECK (MinimumStock >= 0)
);
GO
IF OBJECT_ID('dbo.Sales', 'U') IS NULL
CREATE TABLE dbo.Sales (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    Subtotal DECIMAL(18,2) NOT NULL CHECK (Subtotal >= 0),
    Tax DECIMAL(18,2) NOT NULL CHECK (Tax >= 0),
    Total DECIMAL(18,2) NOT NULL CHECK (Total >= 0),
    Status VARCHAR(20) NOT NULL CHECK (Status IN ('Pendiente','Pagada','Anulada'))
);
GO
IF OBJECT_ID('dbo.SaleItems', 'U') IS NULL
CREATE TABLE dbo.SaleItems (
    SaleId UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.Sales(Id),
    ProductId INT NOT NULL REFERENCES dbo.Products(Id),
    Quantity INT NOT NULL CHECK (Quantity > 0),
    UnitPrice DECIMAL(18,2) NOT NULL CHECK (UnitPrice >= 0),
    PRIMARY KEY (SaleId, ProductId)
);
GO
IF OBJECT_ID('dbo.Payments', 'U') IS NULL
CREATE TABLE dbo.Payments (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    SaleId UNIQUEIDENTIFIER NOT NULL UNIQUE REFERENCES dbo.Sales(Id),
    Amount DECIMAL(18,2) NOT NULL CHECK (Amount > 0),
    Method VARCHAR(20) NOT NULL CHECK (Method IN ('Efectivo','Tarjeta','Transferencia')),
    Reference NVARCHAR(100) NOT NULL UNIQUE,
    CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
GO
IF OBJECT_ID('dbo.AuditLog', 'U') IS NULL
CREATE TABLE dbo.AuditLog (
    Id BIGINT IDENTITY PRIMARY KEY,
    CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    Actor NVARCHAR(100) NOT NULL,
    Action NVARCHAR(100) NOT NULL,
    EntityId NVARCHAR(100) NOT NULL,
    Detail NVARCHAR(1000) NOT NULL
);
GO
CREATE OR ALTER TRIGGER dbo.AuditLog_Immutable ON dbo.AuditLog INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 50010, 'La bitácora es inmutable.', 1;
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_DescontarExistencias @ProductId INT, @Quantity INT AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @Quantity <= 0 THROW 50001, 'Cantidad inválida.', 1;
    UPDATE dbo.Products SET Stock = Stock - @Quantity WHERE Id = @ProductId AND Stock >= @Quantity;
    IF @@ROWCOUNT <> 1 THROW 50001, 'Existencias insuficientes.', 1;
END;
GO
