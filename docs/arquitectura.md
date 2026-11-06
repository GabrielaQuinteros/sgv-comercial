# Arquitectura y operación

## Componentes

1. **Interfaz web:** React 18 y TypeScript. La sesión se mantiene en memoria y se descarta al cerrar la página o expirar el token.
2. **API:** ASP.NET Core 8. JWT de 15 minutos, autorización RBAC por endpoint y límite de solicitudes de autenticación.
3. **Persistencia:** SQL Server. Precios e impuestos en `decimal`, operaciones parametrizadas y transacciones para ventas y movimientos de inventario.
4. **Servicio DTE:** proceso independiente con autenticación entre servicios. Genera UUID v4 y firma los bytes del documento con RSA-SHA256 y un certificado .P12 vigente.
5. **Auditoría:** registro de accesos y operaciones. El esquema impide UPDATE y DELETE sobre la bitácora mediante un trigger.

## API principal

| Método | Ruta                         | Permiso             |
| ------ | ---------------------------- | ------------------- |
| GET    | `/health`                    | Público             |
| POST   | `/api/auth/login`            | Credenciales        |
| GET    | `/api/products`              | Usuario autenticado |
| GET    | `/api/dashboard`             | Usuario autenticado |
| POST   | `/api/products`              | Admin, Inventario   |
| POST   | `/api/inventory/entries`     | Admin, Inventario   |
| POST   | `/api/sales`                 | Admin, Ventas       |
| GET    | `/api/audit?from=...&to=...` | Admin, Auditor      |
| POST   | `/api/users`                 | Admin               |
| POST   | `/api/dte/sign`              | Admin, Ventas       |

Los intervalos de auditoría utilizan límites `[desde, hasta)` y no pueden superar 31 días. Los precios provienen del catálogo del servidor: el cliente no puede imponer el precio de la venta.

## Despliegue

Aplica los scripts SQL con una cuenta de migraciones. Para la operación utiliza una cuenta de aplicación con permisos mínimos sobre las tablas y procedimientos requeridos; no utilices la cuenta `sa`.

En despliegues remotos habilita TLS y valida el certificado de SQL Server. Publica la aplicación detrás de un proxy HTTPS y restringe el puerto del servicio DTE a la red interna.

No se incluyen certificados, claves JWT, credenciales ni tokens en el repositorio. `.env.example` contiene los nombres de configuración. El certificado tributario y la URL de la autoridad deben corresponder al ambiente autorizado para la organización.

La firma criptográfica no equivale por sí sola a la aceptación fiscal: la respuesta de la autoridad tributaria debe conservarse como evidencia de recepción.

## Envío DTE · SGV-38

`POST /api/dte/submit` recibe el documento firmado obtenido de `/api/dte/sign`. La API autentica el rol de Ventas y comunica la solicitud al servicio DTE. Este servicio envía el documento a la URL HTTPS configurada en `DTE_AUTHORITY_URL`, usando `DTE_AUTHORITY_TOKEN` y el UUID como clave de idempotencia. La respuesta conserva el código HTTP y el contenido devuelto por la pasarela.

Los errores de la pasarela se devuelven como 502 y la falta de configuración como 503. No se registra recepción correcta sin una respuesta HTTP satisfactoria. La integración requiere que la pasarela de la organización acepte el contrato `SignedDocument` o lo adapte al formato tributario correspondiente.
