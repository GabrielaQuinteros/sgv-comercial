SET XACT_ABORT ON;
MERGE dbo.Products AS target
USING (VALUES
    (N'AW-HL-01', N'Casco de ciclismo', CAST(45.00 AS DECIMAL(18,2)), 25, 5),
    (N'AW-GL-01', N'Guantes deportivos', CAST(18.50 AS DECIMAL(18,2)), 40, 8),
    (N'AW-BT-01', N'Botella térmica', CAST(22.00 AS DECIMAL(18,2)), 30, 5),
    (N'AW-JS-01', N'Jersey de ciclismo', CAST(55.00 AS DECIMAL(18,2)), 18, 4)
) AS source(Sku, Name, Price, Stock, MinimumStock)
ON target.Sku = source.Sku
WHEN NOT MATCHED THEN INSERT(Sku, Name, Price, Stock, MinimumStock)
VALUES(source.Sku, source.Name, source.Price, source.Stock, source.MinimumStock);
GO
