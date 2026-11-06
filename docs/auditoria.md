# Exportación de bitácora · SGV-19

La API expone `GET /api/audit/export?from=<ISO-8601>&to=<ISO-8601>` para Admin y Auditor. El intervalo es `[desde,hasta)`, con una duración máxima de 31 días. Para evitar exportaciones incompletas, una consulta que supera 10000 eventos se rechaza y solicita reducir el rango.

El CSV se codifica en UTF-8 con BOM, expresa las fechas en UTC y conserva Unicode, comillas, comas y saltos de línea. Los campos que podrían interpretarse como fórmulas de hoja de cálculo se prefijan para conservarlos como texto.

La pantalla Auditoría permite consultar o descargar directamente el intervalo seleccionado. Las pruebas verifican autenticación, contenido, rangos inválidos, formato del CSV y protección frente a fórmulas.
