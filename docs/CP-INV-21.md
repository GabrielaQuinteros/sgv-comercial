# CP-INV-21 · Concurrencia al descontar existencias

**Historia:** [SGV-33 / SCRUM-203](https://practicaprofesional02.atlassian.net/browse/SCRUM-203) · **Impedimento:** IMP-06

## Regla de concurrencia

`dbo.usp_DescontarExistencias` requiere una transacción explícita. Antes de modificar las existencias, consulta la fila con `UPDLOCK`, `HOLDLOCK` y `ROWLOCK`. La comprobación y el descuento permanecen bajo el mismo bloqueo hasta que la transacción finaliza.

Una cantidad no positiva, un producto inexistente o las existencias insuficientes producen el error 50001. La ausencia de una transacción explícita produce el error 50002. `XACT_ABORT` garantiza que un error revierte los cambios del conjunto de la operación.

## Casos automatizados con SQL Server

| Prueba | Preparación | Resultado esperado |
|---|---|---|
| `CP_INV_21_TwoConcurrentDiscountsNeverOversell` | 10 unidades; dos transacciones concurrentes de 8 | Una operación correcta, una rechazada, saldo final 2 |
| `FailureOnSecondProductRollsBackFirstDiscount` | Dos productos; el segundo no cubre la cantidad | Ambos saldos se conservan tras el rollback |
| `RejectsImplicitTransactionsAndNonPositiveQuantities` | Ejecución sin transacción y cantidad negativa | Rechazo y saldo intacto |

El inicio de las dos transacciones se coordina mediante una barrera para que ambas intenten descontar simultáneamente. Las pruebas utilizan conexiones independientes a SQL Server.

## Ejecución y evidencia

```bash
dotnet test tests/SGV.Tests/SGV.Tests.csproj --filter FullyQualifiedName~InventoryConcurrencyTests --logger trx
```

Configura `SGV_TEST_SQL` para ejecutar localmente. GitHub Actions proporciona SQL Server 2022 y publica el TRX y la cobertura en el artefacto `backend-test-results`. La revisión se realiza en el Pull Request de SGV-33 antes de fusionar el cambio.
