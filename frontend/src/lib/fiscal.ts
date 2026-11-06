import { request } from "./api";

export interface FiscalStep {
  estado: string;
  fecha: string;
  detalle: string;
}
export interface DteRecord {
  id: string;
  ventaId: string;
  tipoDte: string;
  estado: string;
  numeroControl: string;
  codigoGeneracion: string;
  selloRecibido: string | null;
  json: string | null;
  jws: string | null;
  respuesta: string | null;
  fecha: string;
  actualizado: string;
  fechaRespuesta: string | null;
  tiempoEmisionMs: number | null;
  intentos: number;
  version: number;
  originalId: string | null;
  timeline: FiscalStep[];
  motivoInvalidacion: string | null;
  invalidacionEstado: string | null;
  respuestaInvalidacion: string | null;
}
export interface PaidSale {
  id: string;
  referencia: string;
  estado: string;
  total: number;
  cliente: string;
  tipoDte: string;
  escenario: string;
  dteId: string | null;
  estadoDte: string | null;
}
export interface FiscalIndicators {
  totalEmitidos: number;
  procesados: number;
  rechazados: number;
  enContingencia: number;
  invalidados: number;
  porcentajeProcesados: number;
  porcentajeRechazados: number;
  tiempoPromedioMs: number;
  motivosFrecuentes: Array<{ motivo: string; cantidad: number }>;
}
export interface FiscalFilter {
  desde: string;
  hasta: string;
  tipo: string;
  estado: string;
}
export interface FiscalPayload {
  identificacion: {
    codigoGeneracion: string;
    numeroControl: string;
    fecEmi: string;
    horEmi: string;
    tipoDte: string;
  };
  emisor: {
    nombre: string;
    nit: string;
    nrc: string;
    direccion: { complemento: string };
  };
  receptor: {
    nombre: string;
    nit?: string;
    numDocumento?: string;
    nrc?: string;
  };
  cuerpoDocumento: Array<{
    numItem: number;
    descripcion: string;
    cantidad: number;
    precioUni: number;
    montoDescu: number;
    ventaGravada: number;
  }>;
  resumen: {
    totalGravada: number;
    totalIva?: number;
    tributos: Array<{ valor: number }> | null;
    totalPagar?: number;
    montoTotalOperacion: number;
    totalLetras: string;
  };
  documentoRelacionado: Array<{ numeroDocumento: string }> | null;
}
export const typeNames: Record<string, string> = {
  "01": "Factura",
  "03": "Crédito Fiscal",
  "05": "Nota de Crédito",
};
export const terminalStates = [
  "PROCESADO",
  "RECHAZADO",
  "CONTINGENCIA",
  "INVALIDADO",
];

export function fiscalQuery(filter: FiscalFilter) {
  const values = new URLSearchParams();
  if (filter.desde) values.set("desde", `${filter.desde}T00:00:00-06:00`);
  if (filter.hasta) {
    const next = new Date(`${filter.hasta}T00:00:00-06:00`);
    next.setUTCDate(next.getUTCDate() + 1);
    values.set("hasta", next.toISOString());
  }
  if (filter.tipo) values.set("tipo", filter.tipo);
  if (filter.estado) values.set("estado", filter.estado);
  return values.size ? `?${values}` : "";
}
export function parsePayload(document: DteRecord): FiscalPayload | null {
  try {
    return document.json ? (JSON.parse(document.json) as FiscalPayload) : null;
  } catch {
    return null;
  }
}
export function fiscalResponse(document: DteRecord): {
  descripcionMsg?: string;
  observaciones?: string[];
} {
  try {
    return document.respuesta ? JSON.parse(document.respuesta) : {};
  } catch {
    return {};
  }
}
export async function exportFiscalCsv(filter: FiscalFilter, token: string) {
  const response = await fetch(`/api/dte/exportar${fiscalQuery(filter)}`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  if (!response.ok) throw new Error("No se pudo exportar la consulta de DTE.");
  const url = URL.createObjectURL(await response.blob());
  const link = document.createElement("a");
  link.href = url;
  link.download = "documentos-electronicos.csv";
  document.body.appendChild(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 0);
}
export const loadDte = (id: string, token: string) =>
  request<DteRecord>(`/dte/${id}`, token);
