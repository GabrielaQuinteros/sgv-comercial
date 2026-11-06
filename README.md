# SGV-Comercial

Sistema de Gestión Comercial de Adventure Works: ventas, inventario, facturación electrónica, control de acceso y auditoría.

## Equipo

| Integrante                       | Rol                |
| -------------------------------- | ------------------ |
| Gabriela Sofía Quinteros Ramírez | Scrum Master       |
| Oscar Ernesto Menjívar Ayala     | Product Owner      |
| Joel Mauricio Barrera Zepeda     | QA                 |
| Fabio Alberto Mijango Serrano    | Seguridad          |
| Miguel Andreé Torres Campos      | Gestión de Riesgos |

## Arquitectura

```text
.github/workflows/     Integración continua y controles de calidad
backend/SGV.Api/       API ASP.NET Core 8: JWT, RBAC y gestión comercial
database/             Esquema y procedimientos T-SQL para SQL Server
docs/                 Arquitectura, operación y trazabilidad
dte-service/SGV.Dte/   Firma RSA-SHA256 con certificado .P12 y UUID v4
frontend/             React 18, TypeScript y Vite
tests/                Pruebas xUnit, integración con SQL Server y Playwright
```

El frontend consulta la API autenticada. La API utiliza SQL Server y se comunica con el servicio DTE mediante una credencial de servicio. Las claves y los certificados se configuran fuera del repositorio.

## Requisitos

- .NET SDK 8.0 y Node.js 22.14 o posterior.
- SQL Server 2022 con `database/001_schema.sql` y `database/002_catalog.sql` aplicados en una base `Sgv`.
- Certificado .P12 vigente y credenciales de la pasarela tributaria para habilitar la integración DTE.

## Ejecución local

Configura las variables del proceso antes de ejecutar los servicios:

| Variable                   | Uso                                                          |
| -------------------------- | ------------------------------------------------------------ |
| `ConnectionStrings__Sgv`   | Cadena de conexión a la base SQL Server                      |
| `SGV_JWT_KEY`              | Clave aleatoria de al menos 32 bytes                         |
| `SGV_ADMIN_USERNAME`       | Nombre del administrador inicial                             |
| `SGV_ADMIN_PASSWORD`       | Contraseña inicial de 14 a 128 caracteres                    |
| `DTE_SERVICE_KEY`          | Credencial compartida API/DTE de al menos 32 caracteres      |
| `Services__DteUrl`         | URL interna del servicio DTE; local: `http://localhost:5081` |
| `DTE_CERTIFICATE_PATH`     | Ruta externa al certificado .P12                             |
| `DTE_CERTIFICATE_PASSWORD` | Contraseña del certificado                                   |

```bash
dotnet run --project backend/SGV.Api --urls http://localhost:5080
dotnet run --project dte-service/SGV.Dte --urls http://localhost:5081
```

En otra terminal, desde la raíz del repositorio:

```bash
npm ci
npm run dev
```

Abre `http://localhost:5173`. La cuenta administradora se registra en la base únicamente cuando no existen usuarios. Los roles disponibles son Admin, Ventas, Inventario y Auditor.

## Validación

```bash
dotnet test tests/SGV.Tests/SGV.Tests.csproj
```

Las pruebas SQL se ejecutan cuando `SGV_TEST_SQL` apunta a una instancia SQL Server con permisos para crear una base de pruebas. Sin esa variable, xUnit las informa como omitidas. GitHub Actions prepara SQL Server y ejecuta esas pruebas con una base independiente.

Desde la raíz del repositorio:

```bash
npm run build
npm test
npx playwright install chromium
npm run test:e2e
```

Los resultados y la cobertura se publican como artefactos en Actions. Las métricas de calidad se obtienen de la ejecución del analizador.

## Ramas e integración

- `main`: versión estable.
- `feature/SGV-<n>-<descripcion>`: implementación por historia.
- Cada Pull Request requiere las verificaciones de Backend y SQL Server, Frontend y SonarQube Cloud correctas antes de fusionarse.
- La integración se administra desde la cuenta responsable del repositorio; las pruebas y el análisis de calidad son obligatorios.

## Trazabilidad

Las referencias funcionales SGV-1 a SGV-44 aparecen en los títulos de Jira. El espacio utiliza claves internas SCRUM: por ejemplo, **SGV-33 → [SCRUM-203](https://practicaprofesional02.atlassian.net/browse/SCRUM-203)**.

[Product Backlog](https://practicaprofesional02.atlassian.net/issues/?filter=10066) · [Documentación técnica](docs/arquitectura.md)
