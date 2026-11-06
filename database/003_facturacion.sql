SET XACT_ABORT ON;
GO
IF COL_LENGTH('dbo.Sales','ReceptorJson') IS NULL
    ALTER TABLE dbo.Sales ADD ReceptorJson NVARCHAR(MAX) NOT NULL DEFAULT N'{"nombre":"Consumidor final","nit":"06140101900011","nrc":null,"codActividad":"47119","descActividad":"Comercio","correo":"cliente@example.test","departamento":"06","municipio":"14","direccion":"San Salvador"}';
IF COL_LENGTH('dbo.Sales','Referencia') IS NULL
    ALTER TABLE dbo.Sales ADD Referencia NVARCHAR(50) NULL;
IF COL_LENGTH('dbo.Sales','PruebaEscenario') IS NULL
    ALTER TABLE dbo.Sales ADD PruebaEscenario VARCHAR(30) NOT NULL DEFAULT 'normal';
GO
IF OBJECT_ID('dbo.CK_Sales_Status','C') IS NULL
BEGIN
    DECLARE @PreviousConstraint NVARCHAR(300);
    SELECT @PreviousConstraint = N'dbo.' + QUOTENAME(name) FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID('dbo.Sales') AND definition LIKE '%Status%';
    IF @PreviousConstraint IS NOT NULL
        EXEC sys.sp_rename @objname=@PreviousConstraint, @newname=N'CK_Sales_Status', @objtype=N'OBJECT';
END;
GO
IF OBJECT_ID('dbo.CK_Sales_Status','C') IS NOT NULL ALTER TABLE dbo.Sales DROP CONSTRAINT CK_Sales_Status;
ALTER TABLE dbo.Sales WITH CHECK ADD CONSTRAINT CK_Sales_Status CHECK (Status IN ('Pendiente','Pagada','Facturada','Anulada'));
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='UX_Sales_Referencia' AND object_id=OBJECT_ID('dbo.Sales'))
    CREATE UNIQUE INDEX UX_Sales_Referencia ON dbo.Sales(Referencia) WHERE Referencia IS NOT NULL;
GO
IF OBJECT_ID('dbo.DteCorrelativos','U') IS NULL
CREATE TABLE dbo.DteCorrelativos (
    Establecimiento CHAR(8) NOT NULL PRIMARY KEY,
    Correlativo BIGINT NOT NULL CHECK(Correlativo BETWEEN 1 AND 999999999999999)
);
GO
IF OBJECT_ID('dbo.Dte','U') IS NULL
CREATE TABLE dbo.Dte (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    VentaId UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.Sales(Id),
    TipoDte CHAR(2) NOT NULL CHECK(TipoDte IN ('01','03','05')),
    Estado VARCHAR(20) NOT NULL CHECK(Estado IN ('GENERADO','FIRMADO','ENVIADO','PROCESADO','RECHAZADO','CONTINGENCIA','INVALIDADO')),
    FacturaActiva BIT NOT NULL,
    OriginalId UNIQUEIDENTIFIER NULL REFERENCES dbo.Dte(Id),
    NumeroControl VARCHAR(40) NOT NULL UNIQUE,
    CodigoGeneracion CHAR(36) NOT NULL UNIQUE,
    SelloRecibido VARCHAR(40) NULL,
    Json NVARCHAR(MAX) NULL CHECK(Json IS NULL OR ISJSON(Json)=1),
    Jws NVARCHAR(MAX) NULL,
    Respuesta NVARCHAR(MAX) NULL CHECK(Respuesta IS NULL OR ISJSON(Respuesta)=1),
    Fecha DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    Actualizado DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    FechaRespuesta DATETIMEOFFSET NULL,
    TiempoEmisionMs BIGINT NULL,
    Intentos INT NOT NULL DEFAULT 1,
    Version INT NOT NULL DEFAULT 1,
    Timeline NVARCHAR(MAX) NOT NULL DEFAULT N'[]' CHECK(ISJSON(Timeline)=1),
    MotivoInvalidacion NVARCHAR(250) NULL,
    InvalidacionCodigo CHAR(36) NULL,
    InvalidacionJws NVARCHAR(MAX) NULL,
    InvalidacionEstado VARCHAR(20) NULL,
    RespuestaInvalidacion NVARCHAR(MAX) NULL
);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='UX_Dte_FacturaActiva' AND object_id=OBJECT_ID('dbo.Dte')) CREATE UNIQUE INDEX UX_Dte_FacturaActiva ON dbo.Dte(VentaId) WHERE FacturaActiva=1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='UX_Dte_NotaOriginal' AND object_id=OBJECT_ID('dbo.Dte')) CREATE UNIQUE INDEX UX_Dte_NotaOriginal ON dbo.Dte(OriginalId) WHERE TipoDte='05';
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_Dte_Consulta' AND object_id=OBJECT_ID('dbo.Dte')) CREATE INDEX IX_Dte_Consulta ON dbo.Dte(Fecha DESC,TipoDte,Estado);
GO
IF OBJECT_ID('dbo.ReceptorDte','U') IS NULL
CREATE TABLE dbo.ReceptorDte (
    CodigoGeneracion CHAR(36) NOT NULL PRIMARY KEY,
    HashJws CHAR(64) NOT NULL,
    TipoDte CHAR(2) NOT NULL,
    Operacion VARCHAR(20) NOT NULL,
    Intentos INT NOT NULL DEFAULT 1,
    Respuesta NVARCHAR(MAX) NULL,
    Invalidado BIT NOT NULL DEFAULT 0,
    Fecha DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
GO
