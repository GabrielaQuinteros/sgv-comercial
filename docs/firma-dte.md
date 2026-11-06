# Sincronización y firma · SGV-37 / IMP-05

Antes de firmar se consulta un servidor NTP mediante UDP. El cliente valida la longitud de la respuesta, el modo de servidor, el estrato y la marca de origen, y calcula la diferencia del reloj usando los cuatro tiempos del intercambio.

Se rechazan las respuestas sin sincronización, las que no corresponden a la consulta y las diferencias de reloj superiores a dos minutos. El plazo de consulta es de tres segundos; si no existe una referencia válida, no se firma el documento.

`Ntp__Host` selecciona el servidor de la organización y `Ntp__Port` su puerto; el valor predeterminado es `pool.ntp.org:123`. El servidor debe permitir la salida UDP al destino configurado.

Las pruebas utilizan un servidor UDP de prueba para verificar el ajuste de tiempo, las marcas de origen, los errores de protocolo y la cancelación. La firma utiliza la hora obtenida y comprueba la vigencia del certificado .P12.
