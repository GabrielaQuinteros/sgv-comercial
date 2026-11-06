import { QRCodeSVG } from "qrcode.react";
import { currency } from "../lib/api";
import { parsePayload, typeNames, type DteRecord } from "../lib/fiscal";

export function FiscalReceipt({ document }: { document: DteRecord }) {
  const data = parsePayload(document);
  if (!data) return <p>El documento aún no tiene JSON disponible.</p>;
  const url = `${window.location.origin}/consulta/dte/${document.codigoGeneracion}`;
  const iva = data.resumen.totalIva ?? data.resumen.tributos?.[0]?.valor ?? 0;
  return (
    <article className="fiscal-receipt" aria-label="Comprobante imprimible">
      <header>
        <div>
          <h2>{data.emisor.nombre}</h2>
          <p>
            NIT {data.emisor.nit} · NRC {data.emisor.nrc}
          </p>
          <p>{data.emisor.direccion.complemento}</p>
        </div>
        <div>
          <strong>
            {typeNames[document.tipoDte]} ({document.tipoDte})
          </strong>
          <p>Ambiente de pruebas</p>
          <b>{document.estado}</b>
        </div>
      </header>
      <p>
        <strong>Número de control:</strong> {document.numeroControl}
      </p>
      <p>
        <strong>Código de generación:</strong> {document.codigoGeneracion}
      </p>
      <p>
        <strong>Fecha de emisión:</strong> {data.identificacion.fecEmi}{" "}
        {data.identificacion.horEmi}
      </p>
      <p>
        <strong>Receptor:</strong> {data.receptor.nombre} · NIT{" "}
        {data.receptor.nit ?? data.receptor.numDocumento}
        {data.receptor.nrc ? ` · NRC ${data.receptor.nrc}` : ""}
      </p>
      <table>
        <thead>
          <tr>
            <th>Descripción</th>
            <th>Cantidad</th>
            <th>Precio unitario</th>
            <th>Descuento</th>
            <th>Venta gravada</th>
          </tr>
        </thead>
        <tbody>
          {data.cuerpoDocumento.map((line) => (
            <tr key={line.numItem}>
              <td>{line.descripcion}</td>
              <td>{line.cantidad}</td>
              <td>{currency(line.precioUni)}</td>
              <td>{currency(line.montoDescu)}</td>
              <td>{currency(line.ventaGravada)}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <div className="receipt-totals">
        <p>
          {document.tipoDte === "01"
            ? "Total gravado (IVA incluido)"
            : "Base gravada"}
          : <b>{currency(data.resumen.totalGravada)}</b>
        </p>
        <p>
          IVA 13 % {document.tipoDte === "01" ? "incluido" : ""}:{" "}
          <b>{currency(iva)}</b>
        </p>
        <h3>
          Total:{" "}
          {currency(
            data.resumen.totalPagar ?? data.resumen.montoTotalOperacion,
          )}
        </h3>
        <p>{data.resumen.totalLetras}</p>
      </div>
      {data.documentoRelacionado?.[0] && (
        <p>
          Documento original: {data.documentoRelacionado[0].numeroDocumento}
        </p>
      )}
      <div className="receipt-verification">
        <QRCodeSVG value={url} size={128} title="QR de consulta del DTE" />
        <div>
          <p>
            <strong>Sello de recepción</strong>
          </p>
          <p className="mono">{document.selloRecibido ?? "No disponible"}</p>
          <a href={url} target="_blank" rel="noreferrer">
            Consultar documento en SGV-Comercial
          </a>
        </div>
      </div>
    </article>
  );
}
