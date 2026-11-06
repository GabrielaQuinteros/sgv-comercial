import { useCallback, useEffect, useState } from "react";
import { request, currency } from "../lib/api";
import {
  exportFiscalCsv,
  fiscalQuery,
  fiscalResponse,
  loadDte,
  terminalStates,
  typeNames,
  type DteRecord,
  type FiscalFilter,
  type FiscalIndicators,
  type PaidSale,
} from "../lib/fiscal";
import { FiscalReceipt } from "./FiscalReceipt";
import "../fiscal.css";

const emptyFilter: FiscalFilter = {
  desde: "",
  hasta: "",
  tipo: "",
  estado: "",
};
export function Facturacion({ token }: { token: string }) {
  const [sales, setSales] = useState<PaidSale[]>([]);
  const [documents, setDocuments] = useState<DteRecord[]>([]);
  const [indicators, setIndicators] = useState<FiscalIndicators | null>(null);
  const [filter, setFilter] = useState<FiscalFilter>(emptyFilter);
  const [applied, setApplied] = useState<FiscalFilter>(emptyFilter);
  const [selected, setSelected] = useState<DteRecord | null>(null);
  const [busy, setBusy] = useState(false);
  const [emittingSale, setEmittingSale] = useState<string | null>(null);
  const [message, setMessage] = useState("");
  const [search, setSearch] = useState("DEMO-");
  const [reason, setReason] = useState("");
  const [showReceipt, setShowReceipt] = useState(false);
  const query = fiscalQuery(applied);

  const refresh = useCallback(
    async (signal?: AbortSignal) => {
      const [paid, rows, summary] = await Promise.all([
        request<PaidSale[]>("/facturacion/ventas", token, undefined, signal),
        request<DteRecord[]>(`/dte${query}`, token, undefined, signal),
        request<FiscalIndicators>(
          `/facturacion/indicadores${query}`,
          token,
          undefined,
          signal,
        ),
      ]);
      setSales(paid);
      setDocuments(rows);
      setIndicators(summary);
    },
    [token, query],
  );
  useEffect(() => {
    const controller = new AbortController();
    void refresh(controller.signal).catch((e: Error) => {
      if (e.name !== "AbortError") setMessage(e.message);
    });
    return () => controller.abort();
  }, [refresh]);
  useEffect(() => {
    if (!emittingSale) return;
    const controller = new AbortController();
    const timer = setInterval(() => {
      void request<DteRecord | null>(
        `/ventas/${emittingSale}/dte`,
        token,
        undefined,
        controller.signal,
      )
        .then((row) => {
          if (row && !controller.signal.aborted)
            setSelected((current) =>
              !current || row.version >= current.version ? row : current,
            );
        })
        .catch(() => {});
    }, 600);
    return () => {
      controller.abort();
      clearInterval(timer);
    };
  }, [emittingSale, token]);

  async function perform(operation: () => Promise<DteRecord>, saleId?: string) {
    setBusy(true);
    setMessage("");
    setShowReceipt(false);
    if (saleId) {
      setSelected(null);
      setEmittingSale(saleId);
    }
    try {
      const row = await operation();
      setSelected(row);
      setReason(row.motivoInvalidacion ?? "");
      setMessage(`Documento ${row.numeroControl}: ${row.estado}.`);
      await refresh();
    } catch (e) {
      setMessage(
        e instanceof Error ? e.message : "La operación no pudo completarse.",
      );
    } finally {
      setBusy(false);
      setEmittingSale(null);
    }
  }
  async function open(id: string) {
    try {
      const row = await loadDte(id, token);
      setSelected(row);
      setReason(row.motivoInvalidacion ?? "");
      setShowReceipt(false);
    } catch (e) {
      setMessage(
        e instanceof Error
          ? e.message
          : "No fue posible consultar el documento.",
      );
    }
  }
  const visibleSales = sales.filter((s) =>
    `${s.referencia} ${s.cliente}`.toLowerCase().includes(search.toLowerCase()),
  );
  const result = selected ? fiscalResponse(selected) : {};

  return (
    <div className="billing-module">
      <div className="billing-heading">
        <span className="environment-badge">Ambiente de pruebas</span>
        <span className="muted">
          Receptor propio · Emisión, seguimiento y correcciones
        </span>
      </div>
      {message && (
        <p className="notice" role="status">
          {message}
          <button
            type="button"
            onClick={() => setMessage("")}
            aria-label="Cerrar mensaje de facturación"
          >
            ×
          </button>
        </p>
      )}
      <section
        className="metrics fiscal-metrics"
        aria-label="Indicadores de facturación"
      >
        <Metric
          label="Total emitidos"
          value={String(indicators?.totalEmitidos ?? 0)}
          detail="Documentos registrados"
        />
        <Metric
          label="Procesados"
          value={`${indicators?.porcentajeProcesados ?? 0}%`}
          detail={`${indicators?.procesados ?? 0} documentos`}
        />
        <Metric
          label="Rechazados"
          value={`${indicators?.porcentajeRechazados ?? 0}%`}
          detail={`${indicators?.rechazados ?? 0} documentos`}
        />
        <Metric
          label="En contingencia"
          value={String(indicators?.enContingencia ?? 0)}
          detail="Pendientes de reenvío"
        />
        <Metric
          label="Tiempo promedio"
          value={`${((indicators?.tiempoPromedioMs ?? 0) / 1000).toFixed(2)} s`}
          detail="Desde emisión hasta respuesta"
        />
      </section>
      {!!indicators?.motivosFrecuentes.length && (
        <aside className="panel rejection-reasons">
          <h2>Motivos de rechazo frecuentes</h2>
          {indicators.motivosFrecuentes.map((item) => (
            <span key={item.motivo}>
              {item.motivo} <b>{item.cantidad}</b>
            </span>
          ))}
        </aside>
      )}
      <section className="panel">
        <div className="section-heading">
          <div>
            <h2>Ventas pagadas</h2>
            <p className="muted">
              Emite la factura correspondiente al receptor. El estado de venta
              cambia a Facturada solo al ser procesada.
            </p>
          </div>
          <input
            aria-label="Buscar venta pagada"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Referencia o cliente"
          />
        </div>
        <div className="table-scroll">
          <table>
            <thead>
              <tr>
                <th>Referencia</th>
                <th>Receptor</th>
                <th>Tipo</th>
                <th>Total</th>
                <th>Venta</th>
                <th>DTE</th>
                <th>Acción</th>
              </tr>
            </thead>
            <tbody>
              {visibleSales.map((sale) => (
                <tr key={sale.id}>
                  <td>
                    {sale.referencia}
                    {sale.escenario === "timeout-once" && (
                      <small>Respuesta lenta inicial</small>
                    )}
                  </td>
                  <td>{sale.cliente}</td>
                  <td>
                    {typeNames[sale.tipoDte]} ({sale.tipoDte})
                  </td>
                  <td>{currency(sale.total)}</td>
                  <td>{sale.estado}</td>
                  <td>
                    {sale.estadoDte ? (
                      <Status state={sale.estadoDte} />
                    ) : (
                      "Pendiente de emisión"
                    )}
                  </td>
                  <td>
                    {!sale.dteId ? (
                      <button
                        type="button"
                        className="primary"
                        disabled={busy}
                        onClick={() =>
                          void perform(
                            () =>
                              request<DteRecord>(
                                `/ventas/${sale.id}/dte`,
                                token,
                                {},
                              ),
                            sale.id,
                          )
                        }
                      >
                        {emittingSale === sale.id ? "Emitiendo…" : "Emitir DTE"}
                      </button>
                    ) : (
                      <button
                        type="button"
                        className="secondary"
                        onClick={() => void open(sale.dteId!)}
                      >
                        Ver DTE
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {!visibleSales.length && (
            <p className="empty">No hay ventas con esta búsqueda.</p>
          )}
        </div>
      </section>
      {emittingSale && (
        <p className="notice" role="progressbar" aria-label="Emisión en curso">
          Generando, firmando y enviando el documento. La respuesta puede tardar
          hasta ocho segundos.
        </p>
      )}
      {selected && (
        <section className="panel billing-detail" aria-label="Detalle del DTE">
          <div className="section-heading">
            <div>
              <h2>
                {typeNames[selected.tipoDte]} · {selected.numeroControl}
              </h2>
              <p className="mono">{selected.codigoGeneracion}</p>
            </div>
            <Status state={selected.estado} />
          </div>
          <ol className="fiscal-timeline" aria-label="Línea de tiempo del DTE">
            {selected.timeline.map((step, index) => (
              <li key={`${step.fecha}-${index}`}>
                <strong>{step.estado}</strong>
                <time>{new Date(step.fecha).toLocaleTimeString("es-SV")}</time>
                <small>{step.detalle}</small>
              </li>
            ))}
          </ol>
          <dl className="fiscal-identifiers">
            <dt>Sello de recepción</dt>
            <dd className="mono">{selected.selloRecibido ?? "Pendiente"}</dd>
            <dt>Firma JWS</dt>
            <dd className="mono">
              {selected.jws
                ? `${selected.jws.slice(0, 42)}…${selected.jws.slice(-20)}`
                : "Pendiente"}
            </dd>
            <dt>Intentos de envío</dt>
            <dd>{selected.intentos}</dd>
            <dt>Tiempo de emisión</dt>
            <dd>
              {selected.tiempoEmisionMs === null
                ? "En curso"
                : `${(selected.tiempoEmisionMs / 1000).toFixed(2)} s`}
            </dd>
          </dl>
          {result.descripcionMsg && (
            <p className="receiver-message">
              Receptor: <b>{result.descripcionMsg}</b>
            </p>
          )}
          {!!result.observaciones?.length && (
            <ul className="fiscal-observations">
              {result.observaciones.map((text, index) => (
                <li key={`${index}-${text}`}>{text}</li>
              ))}
            </ul>
          )}
          <div className="billing-actions">
            {selected.estado === "CONTINGENCIA" && (
              <button
                type="button"
                className="primary"
                disabled={busy}
                onClick={() =>
                  void perform(() =>
                    request<DteRecord>(
                      `/dte/${selected.id}/reenviar`,
                      token,
                      {},
                    ),
                  )
                }
              >
                Reenviar
              </button>
            )}
            {selected.estado === "PROCESADO" && selected.tipoDte === "03" && (
              <button
                type="button"
                className="secondary"
                disabled={busy}
                onClick={() =>
                  void perform(() =>
                    request<DteRecord>(
                      `/dte/${selected.id}/nota-credito`,
                      token,
                      {},
                    ),
                  )
                }
              >
                Emitir nota de crédito total
              </button>
            )}
            {selected.json &&
              ["PROCESADO", "INVALIDADO"].includes(selected.estado) && (
                <button
                  type="button"
                  className="secondary"
                  onClick={() => setShowReceipt((value) => !value)}
                >
                  Ver comprobante
                </button>
              )}
            {selected.originalId && (
              <button
                type="button"
                className="text-button"
                onClick={() => void open(selected.originalId!)}
              >
                Consultar DTE original
              </button>
            )}
          </div>
          {selected.estado === "PROCESADO" && (
            <form
              className="inline-form"
              onSubmit={(e) => {
                e.preventDefault();
                void perform(() =>
                  request<DteRecord>(
                    `/dte/${selected.id}/invalidacion`,
                    token,
                    { motivo: reason },
                  ),
                );
              }}
            >
              <label>
                Motivo de invalidación
                <input
                  value={reason}
                  onChange={(e) => setReason(e.target.value)}
                  minLength={5}
                  maxLength={250}
                  required
                />
              </label>
              <button type="submit" className="secondary" disabled={busy}>
                {selected.invalidacionEstado === "CONTINGENCIA"
                  ? "Reintentar invalidación"
                  : "Invalidar DTE"}
              </button>
            </form>
          )}
          {selected.invalidacionEstado && (
            <p>
              Invalidación: <b>{selected.invalidacionEstado}</b> ·{" "}
              {selected.motivoInvalidacion}
            </p>
          )}
          {showReceipt && (
            <div className="print-region">
              <button
                type="button"
                className="primary no-print"
                onClick={() => window.print()}
              >
                Imprimir comprobante
              </button>
              <FiscalReceipt document={selected} />
            </div>
          )}
          <details>
            <summary>JSON del documento</summary>
            <pre className="fiscal-json">
              {selected.json
                ? JSON.stringify(JSON.parse(selected.json), null, 2)
                : "Preparando JSON…"}
            </pre>
          </details>
          <details>
            <summary>Respuesta del receptor</summary>
            <pre className="fiscal-json">
              {selected.respuesta
                ? JSON.stringify(JSON.parse(selected.respuesta), null, 2)
                : "Esperando respuesta…"}
            </pre>
          </details>
        </section>
      )}
      <section className="panel">
        <h2>Consulta de documentos electrónicos</h2>
        <p className="muted">
          Los indicadores corresponden al rango y los filtros seleccionados.
          Fechas en hora de El Salvador.
        </p>
        <form
          className="inline-form"
          onSubmit={(e) => {
            e.preventDefault();
            setApplied({ ...filter });
          }}
        >
          <label>
            Desde
            <input
              type="date"
              value={filter.desde}
              onChange={(e) => setFilter({ ...filter, desde: e.target.value })}
            />
          </label>
          <label>
            Hasta
            <input
              type="date"
              value={filter.hasta}
              onChange={(e) => setFilter({ ...filter, hasta: e.target.value })}
            />
          </label>
          <label>
            Tipo de DTE
            <select
              value={filter.tipo}
              onChange={(e) => setFilter({ ...filter, tipo: e.target.value })}
            >
              <option value="">Todos</option>
              {Object.entries(typeNames).map(([key, value]) => (
                <option key={key} value={key}>
                  {key} · {value}
                </option>
              ))}
            </select>
          </label>
          <label>
            Estado del DTE
            <select
              value={filter.estado}
              onChange={(e) => setFilter({ ...filter, estado: e.target.value })}
            >
              <option value="">Todos</option>
              {terminalStates.map((state) => (
                <option key={state}>{state}</option>
              ))}
            </select>
          </label>
          <button type="submit" className="primary">
            Filtrar
          </button>
          <button
            type="button"
            className="secondary"
            onClick={() =>
              void exportFiscalCsv(applied, token).catch((e: Error) =>
                setMessage(e.message),
              )
            }
          >
            Exportar CSV
          </button>
        </form>
        <div className="table-scroll">
          <table>
            <thead>
              <tr>
                <th>Fecha</th>
                <th>Número de control</th>
                <th>Tipo</th>
                <th>Estado</th>
                <th>Tiempo</th>
                <th>Acción</th>
              </tr>
            </thead>
            <tbody>
              {documents.map((row) => (
                <tr key={row.id}>
                  <td>{new Date(row.fecha).toLocaleString("es-SV")}</td>
                  <td>{row.numeroControl}</td>
                  <td>{row.tipoDte}</td>
                  <td>
                    <Status state={row.estado} />
                  </td>
                  <td>
                    {row.tiempoEmisionMs === null
                      ? "—"
                      : `${(row.tiempoEmisionMs / 1000).toFixed(2)} s`}
                  </td>
                  <td>
                    <button
                      type="button"
                      className="text-button"
                      onClick={() => void open(row.id)}
                    >
                      Detalle
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {!documents.length && (
            <p className="empty">
              No hay documentos para los filtros seleccionados.
            </p>
          )}
        </div>
      </section>
    </div>
  );
}
function Metric({
  label,
  value,
  detail,
}: {
  label: string;
  value: string;
  detail: string;
}) {
  return (
    <article className="metric">
      <p>{label}</p>
      <strong>{value}</strong>
      <small>{detail}</small>
    </article>
  );
}
function Status({ state }: { state: string }) {
  return (
    <span className={`fiscal-status state-${state.toLowerCase()}`}>
      {state}
    </span>
  );
}
