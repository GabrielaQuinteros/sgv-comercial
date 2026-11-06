# Control de acceso · SGV-11

Una cuenta se bloquea durante 15 minutos al acumular cinco autenticaciones fallidas. Los intentos se actualizan en SQL Server dentro de una transacción con `UPDLOCK` y `HOLDLOCK`, de modo que los accesos simultáneos no sobrescriben el contador.

Al finalizar el bloqueo se permite un nuevo ciclo de autenticación. Un acceso correcto reinicia el contador. Las respuestas no revelan si el usuario existe o está bloqueado; las contraseñas no se registran en la bitácora.

El bloqueo por cuenta complementa el límite de solicitudes por IP. La migración de columnas está incluida de forma idempotente en `database/001_schema.sql`.

Las pruebas de integración verifican los cinco intentos, el período de bloqueo, la recuperación, los intentos concurrentes y la ausencia de contraseñas en los eventos de auditoría.
