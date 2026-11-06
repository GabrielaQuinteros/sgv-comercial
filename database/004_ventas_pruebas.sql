SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @Index INT=1;
DECLARE @Product INT=(SELECT TOP(1) Id FROM dbo.Products ORDER BY Id);
WHILE @Index<=35
BEGIN
    DECLARE @Reference NVARCHAR(50)=CASE WHEN @Index<=5 THEN N'DEMO-'+RIGHT('000'+CONVERT(VARCHAR(3),@Index),3) ELSE N'ADIC-'+RIGHT('000'+CONVERT(VARCHAR(3),@Index-5),3) END;
    IF NOT EXISTS(SELECT 1 FROM dbo.Sales WHERE Referencia=@Reference)
    BEGIN
        DECLARE @Id UNIQUEIDENTIFIER=NEWID();
        DECLARE @Nit NVARCHAR(30)=CASE WHEN @Index IN(2,11,21) THEN N'NIT-INVALIDO' ELSE N'06140101900011' END;
        DECLARE @Nrc NVARCHAR(10)=CASE WHEN @Index=3 OR (@Index>5 AND @Index%2=0) THEN CONVERT(VARCHAR(8),100000+@Index) ELSE NULL END;
        DECLARE @Name NVARCHAR(200)=N'Cliente '+@Reference+CASE WHEN @Index IN(2,11,21) THEN N' · NIT inválido' WHEN @Index=26 THEN N' · total inconsistente' ELSE N'' END;
        DECLARE @Customer NVARCHAR(MAX)=(SELECT @Name AS nombre,@Nit AS nit,@Nrc AS nrc,N'47119' AS codActividad,N'Comercio' AS descActividad,N'cliente@example.test' AS correo,N'06' AS departamento,N'14' AS municipio,N'San Salvador, dirección de pruebas' AS direccion FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES);
        DECLARE @Price DECIMAL(18,2)=10+((@Index*7)%90);
        DECLARE @Quantity INT=1+(@Index%3);
        DECLARE @Subtotal DECIMAL(18,2)=@Price*@Quantity;
        DECLARE @Tax DECIMAL(18,2)=ROUND(@Subtotal*0.13,2);
        DECLARE @Total DECIMAL(18,2)=@Subtotal+@Tax+CASE WHEN @Index=26 THEN 1 ELSE 0 END;
        DECLARE @Scenario VARCHAR(30)=CASE WHEN @Index IN(4,28) THEN 'timeout-once' ELSE 'normal' END;
        -- Saldos históricos pagados para la demostración fiscal; el catálogo representa el stock inicial disponible.
        INSERT dbo.Sales(Id,CreatedAt,Subtotal,Tax,Total,Status,ReceptorJson,Referencia,PruebaEscenario)
        VALUES(@Id,DATEADD(DAY,-(@Index%7),SYSDATETIMEOFFSET()),@Subtotal,@Tax,@Total,'Pagada',@Customer,@Reference,@Scenario);
        INSERT dbo.SaleItems(SaleId,ProductId,Quantity,UnitPrice)VALUES(@Id,@Product,@Quantity,@Price);
        INSERT dbo.Payments(Id,SaleId,Amount,Method,Reference)VALUES(NEWID(),@Id,@Total,'Transferencia',N'PAGO-'+@Reference);
        INSERT dbo.AuditLog(Actor,Action,EntityId,Detail)VALUES(N'preparación',N'Venta pagada inicial',CONVERT(NVARCHAR(36),@Id),@Reference);
    END;
    SET @Index=@Index+1;
END;
COMMIT TRANSACTION;
GO
