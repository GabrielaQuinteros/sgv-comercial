import { useEffect, useState } from "react";
import { request, currency } from "../lib/api";

interface Receipt {
  ambiente: string;
  estado: string;
  tipoDte: string;
  numeroControl: string;
  codigoGeneracion: string;
  selloRecibido: string | null;
  fecha: string;
  emisor: string;
  total: number;
}
export function FiscalConsultation({ code }: { code: string }) {
  const [receipt, setReceipt] = useState<Receipt | null>(null);
  const [error, setError] = useState("");
  useEffect(() => {
    const controller = new AbortController();
    request<Receipt>(
      `/consulta/dte/${encodeURIComponent(code)}`,
      undefined,
      undefined,
      controller.signal,
    )
      .then(setReceipt)
      .catch((e: Error) => {
        if (e.name !== "AbortError") setError(e.message);
      });
    return () => controller.abort();
  }, [code]);
  return (
    <main className="public-consultation">
      <div className="panel">
        <span className="environment-badge">Ambiente de pruebas</span>
        <h1>Consulta de documento electrónico</h1>
        {error && <p role="alert">{error}</p>}
        {receipt ? (
          <>
            <h2>{receipt.emisor}</h2>
            <p
              className={`fiscal-status state-${receipt.estado.toLowerCase()}`}
            >
              {receipt.estado}
            </p>
            <dl>
              <dt>Número de control</dt>
              <dd>{receipt.numeroControl}</dd>
              <dt>Código de generación</dt>
              <dd>{receipt.codigoGeneracion}</dd>
              <dt>Sello de recepción</dt>
              <dd>{receipt.selloRecibido ?? "Pendiente"}</dd>
              <dt>Fecha</dt>
              <dd>{new Date(receipt.fecha).toLocaleString("es-SV")}</dd>
              <dt>Importe</dt>
              <dd>{currency(receipt.total)}</dd>
            </dl>
          </>
        ) : (
          !error && <p>Cargando documento…</p>
        )}
        <a href="/">Ir a SGV-Comercial</a>
      </div>
    </main>
  );
}
