# Registro de pagos · SGV-25

`POST /api/sales/{id}/payments` acepta importe, método y referencia. El rol debe ser Admin o Ventas. El importe debe coincidir exactamente con el total de la venta y tener como máximo dos decimales.

En una sola transacción SQL se bloquea la venta, se descuentan las líneas de inventario en orden de producto, se inserta el pago, se cambia el estado a Pagada y se agrega el evento de auditoría. Si falta inventario o la referencia está duplicada, se revierte la operación completa.

Repetir la misma referencia con el mismo importe y método devuelve el recibo existente y no vuelve a descontar inventario. Un segundo pago distinto para una venta ya pagada se rechaza.

La pantalla de ventas permite registrar el pago tras guardar la venta. Las pruebas verifican idempotencia, rechazo de importes, reversión por inventario insuficiente y referencias duplicadas.
