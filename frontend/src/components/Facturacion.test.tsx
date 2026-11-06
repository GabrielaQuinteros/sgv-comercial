import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { Facturacion } from "./Facturacion";
import { FiscalReceipt } from "./FiscalReceipt";
import { FiscalConsultation } from "./FiscalConsultation";
import { request } from "../lib/api";
import { exportFiscalCsv, type DteRecord, type PaidSale } from "../lib/fiscal";

vi.mock("../lib/api", async () => ({
  ...(await vi.importActual<typeof import("../lib/api")>("../lib/api")),
  request: vi.fn(),
}));
vi.mock("../lib/fiscal", async () => ({
  ...(await vi.importActual<typeof import("../lib/fiscal")>("../lib/fiscal")),
  exportFiscalCsv: vi.fn().mockResolvedValue(undefined),
}));

const code = "4B241E88-9383-4875-96A4-A9436F851CEC";
const originalId = "7f6377e8-9383-4875-96a4-a9436f851cec";
function document(state = "PROCESADO", type = "01"): DteRecord {
  return {
    id: "document",
    ventaId: "sale",
    tipoDte: type,
    estado: state,
    numeroControl: `DTE-${type}-M001P001-000000000000001`,
    codigoGeneracion: code,
    selloRecibido: state === "PROCESADO" ? "A".repeat(40) : null,
    jws: "header.payload.signature",
    fecha: "2026-11-06T16:00:00Z",
    actualizado: "2026-11-06T16:00:01Z",
    fechaRespuesta: "2026-11-06T16:00:01Z",
    tiempoEmisionMs: 1000,
    intentos: 1,
    version: 4,
    originalId: type === "05" ? originalId : null,
    motivoInvalidacion: null,
    invalidacionEstado: null,
    respuestaInvalidacion: null,
    timeline: [
      {
        estado: "GENERADO",
        fecha: "2026-11-06T16:00:00Z",
        detalle: "JSON generado",
      },
      { estado: state, fecha: "2026-11-06T16:00:01Z", detalle: "Respuesta" },
    ],
    respuesta: JSON.stringify({
      descripcionMsg:
        state === "RECHAZADO" ? "NIT DEL RECEPTOR NO VÁLIDO" : "RECIBIDO",
      observaciones:
        state === "RECHAZADO" ? ["NIT DEL RECEPTOR NO VÁLIDO"] : [],
    }),
    json: JSON.stringify({
      identificacion: {
        codigoGeneracion: code,
        numeroControl: "control",
        fecEmi: "2026-11-06",
        horEmi: "10:00:00",
        tipoDte: type,
      },
      emisor: {
        nombre: "Adventure Works",
        nit: "06140101900011",
        nrc: "1234567",
        direccion: { complemento: "San Salvador" },
      },
      receptor: {
        nombre: "Cliente",
        nit: "06140101900011",
        numDocumento: "06140101900011",
        nrc: type === "01" ? null : "1234567",
      },
      cuerpoDocumento: [
        {
          numItem: 1,
          descripcion: "Producto",
          cantidad: 1,
          precioUni: type === "01" ? 113 : 100,
          montoDescu: 0,
          ventaGravada: type === "01" ? 113 : 100,
        },
      ],
      resumen: {
        totalGravada: type === "01" ? 113 : 100,
        totalIva: type === "01" ? 13 : undefined,
        tributos: type === "01" ? null : [{ valor: 13 }],
        totalPagar: type === "05" ? undefined : 113,
        montoTotalOperacion: 113,
        totalLetras: "CIENTO TRECE 00/100 DÓLARES",
      },
      documentoRelacionado: type === "05" ? [{ numeroDocumento: code }] : null,
    }),
  };
}
let current: DteRecord;
let sales: PaidSale[];
beforeEach(() => {
  vi.mocked(exportFiscalCsv).mockResolvedValue(undefined);
  current = document();
  sales = [
    {
      id: "sale",
      referencia: "DEMO-001",
      estado: "Pagada",
      total: 113,
      cliente: "Cliente",
      tipoDte: "01",
      escenario: "normal",
      dteId: null,
      estadoDte: null,
    },
  ];
  vi.mocked(request).mockImplementation(async <T,>(path: string) => {
    let value: unknown;
    if (path === "/facturacion/ventas") value = sales;
    else if (path.startsWith("/facturacion/indicadores"))
      value = {
        totalEmitidos: 1,
        procesados: 1,
        rechazados: current.estado === "RECHAZADO" ? 1 : 0,
        enContingencia: current.estado === "CONTINGENCIA" ? 1 : 0,
        invalidados: 0,
        porcentajeProcesados: 100,
        porcentajeRechazados: 0,
        tiempoPromedioMs: 1000,
        motivosFrecuentes:
          current.estado === "RECHAZADO"
            ? [{ motivo: "NIT DEL RECEPTOR NO VÁLIDO", cantidad: 1 }]
            : [],
      };
    else if (path.startsWith("/dte?")) value = [current];
    else if (path === "/dte") value = [current];
    else if (path === "/ventas/sale/dte") {
      sales = [
        {
          ...sales[0],
          dteId: current.id,
          estadoDte: current.estado,
          estado: current.estado === "PROCESADO" ? "Facturada" : "Pagada",
        },
      ];
      value = current;
    } else if (path.endsWith("/reenviar")) {
      current = { ...document(), intentos: 2 };
      value = current;
    } else if (path.endsWith("/invalidacion")) {
      current = {
        ...current,
        estado: "INVALIDADO",
        invalidacionEstado: "PROCESADO",
        motivoInvalidacion: "Corrección de receptor",
        version: 5,
      };
      value = current;
    } else if (path.endsWith("/nota-credito")) {
      current = document("PROCESADO", "05");
      value = current;
    } else if (path.startsWith("/consulta/dte/"))
      value = {
        ambiente: "00",
        estado: "PROCESADO",
        tipoDte: "01",
        numeroControl: "control",
        codigoGeneracion: code,
        selloRecibido: "A".repeat(40),
        fecha: "2026-11-06T16:00:00Z",
        emisor: "Adventure Works",
        total: 113,
      };
    else value = current;
    return value as T;
  });
});
afterEach(() => {
  cleanup();
  vi.resetAllMocks();
  vi.restoreAllMocks();
});

it("emite un DTE, muestra evidencias y abre el comprobante imprimible", async () => {
  render(<Facturacion token="token" />);
  await screen.findByText("DEMO-001");
  fireEvent.click(screen.getByRole("button", { name: "Emitir DTE" }));
  const detail = await screen.findByRole("region", { name: "Detalle del DTE" });
  expect(within(detail).getByText(code)).toBeTruthy();
  expect(await screen.findByText("Facturada")).toBeTruthy();
  expect(request).toHaveBeenCalledWith("/ventas/sale/dte", "token", {});
  fireEvent.click(
    within(detail).getByRole("button", { name: "Ver comprobante" }),
  );
  expect(
    screen.getByRole("article", { name: "Comprobante imprimible" }),
  ).toBeTruthy();
  const print = vi.spyOn(window, "print").mockImplementation(() => {});
  fireEvent.click(screen.getByRole("button", { name: "Imprimir comprobante" }));
  expect(print).toHaveBeenCalledOnce();
  expect(
    screen
      .getByRole("link", { name: "Consultar documento en SGV-Comercial" })
      .getAttribute("href"),
  ).toContain(`/consulta/dte/${code}`);
  fireEvent.click(screen.getByText("JSON del documento"));
  expect(within(detail).getByText(/"codigoGeneracion"/).textContent).toContain(
    code,
  );
});

it("presenta el rechazo y reenvía una contingencia", async () => {
  current = document("CONTINGENCIA");
  sales = [
    {
      ...sales[0],
      dteId: current.id,
      estadoDte: "CONTINGENCIA",
      escenario: "timeout-once",
    },
  ];
  render(<Facturacion token="token" />);
  await screen.findByText("DEMO-001");
  fireEvent.click(screen.getByRole("button", { name: "Ver DTE" }));
  await screen.findByRole("region", { name: "Detalle del DTE" });
  fireEvent.click(screen.getByRole("button", { name: "Reenviar" }));
  await waitFor(() =>
    expect(
      screen.getByText("Intentos de envío").nextElementSibling?.textContent,
    ).toBe("2"),
  );
  expect(request).toHaveBeenCalledWith("/dte/document/reenviar", "token", {});
});

it("invalida un documento y consulta notas de crédito", async () => {
  current = document("PROCESADO", "03");
  sales = [
    { ...sales[0], dteId: current.id, tipoDte: "03", estadoDte: "PROCESADO" },
  ];
  render(<Facturacion token="token" />);
  await screen.findByText("DEMO-001");
  fireEvent.click(screen.getByRole("button", { name: "Ver DTE" }));
  await screen.findByRole("region", { name: "Detalle del DTE" });
  fireEvent.change(screen.getByLabelText("Motivo de invalidación"), {
    target: { value: "Corrección de receptor" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Invalidar DTE" }));
  await waitFor(() =>
    expect(screen.getAllByText("INVALIDADO").length).toBeGreaterThan(0),
  );
  expect(request).toHaveBeenCalledWith("/dte/document/invalidacion", "token", {
    motivo: "Corrección de receptor",
  });
  cleanup();
  current = document("PROCESADO", "03");
  render(<Facturacion token="token" />);
  await screen.findByText("DEMO-001");
  fireEvent.click(screen.getByRole("button", { name: "Ver DTE" }));
  await screen.findByRole("region", { name: "Detalle del DTE" });
  fireEvent.click(
    screen.getByRole("button", { name: "Emitir nota de crédito total" }),
  );
  await screen.findByRole("button", { name: "Consultar DTE original" });
  expect(request).toHaveBeenCalledWith(
    "/dte/document/nota-credito",
    "token",
    {},
  );
  fireEvent.click(
    screen.getByRole("button", { name: "Consultar DTE original" }),
  );
  await waitFor(() =>
    expect(request).toHaveBeenCalledWith(`/dte/${originalId}`, "token"),
  );
});

it("filtra documentos, muestra los motivos de rechazo y comunica errores", async () => {
  current = document("RECHAZADO");
  render(<Facturacion token="token" />);
  await screen.findByText("DEMO-001");
  fireEvent.click(screen.getByRole("button", { name: "Detalle" }));
  await screen.findByRole("region", { name: "Detalle del DTE" });
  expect(
    screen.getAllByText("NIT DEL RECEPTOR NO VÁLIDO").length,
  ).toBeGreaterThan(0);
  fireEvent.change(screen.getByLabelText("Tipo de DTE"), {
    target: { value: "03" },
  });
  fireEvent.change(screen.getByLabelText("Estado del DTE"), {
    target: { value: "RECHAZADO" },
  });
  fireEvent.change(screen.getByLabelText("Desde"), {
    target: { value: "2026-11-01" },
  });
  fireEvent.change(screen.getByLabelText("Hasta"), {
    target: { value: "2026-11-06" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Filtrar" }));
  await waitFor(() =>
    expect(
      vi
        .mocked(request)
        .mock.calls.some(
          ([path]) =>
            path.startsWith("/dte?") &&
            path.includes("tipo=03") &&
            path.includes("estado=RECHAZADO"),
        ),
    ).toBe(true),
  );
  fireEvent.click(screen.getByRole("button", { name: "Exportar CSV" }));
  expect(exportFiscalCsv).toHaveBeenCalledWith(
    {
      desde: "2026-11-01",
      hasta: "2026-11-06",
      tipo: "03",
      estado: "RECHAZADO",
    },
    "token",
  );
  fireEvent.change(screen.getByLabelText("Buscar venta pagada"), {
    target: { value: "missing" },
  });
  expect(screen.getByText("No hay ventas con esta búsqueda.")).toBeTruthy();
});

it("comunica el fallo al consultar un documento", async () => {
  render(<Facturacion token="token" />);
  await screen.findByText("DEMO-001");
  vi.mocked(request).mockRejectedValueOnce(new Error("Consulta no disponible"));
  fireEvent.click(screen.getByRole("button", { name: "Detalle" }));
  await screen.findByText("Consulta no disponible");
  expect(screen.getByRole("status").textContent).toContain(
    "Consulta no disponible",
  );
});

it.each(["01", "03", "05"])(
  "imprime un comprobante %s con QR y totales",
  (type) => {
    render(<FiscalReceipt document={document("PROCESADO", type)} />);
    expect(
      screen.getByRole("article", { name: "Comprobante imprimible" }),
    ).toBeTruthy();
    expect(screen.getByTitle("QR de consulta del DTE")).toBeTruthy();
    expect(screen.getByText("CIENTO TRECE 00/100 DÓLARES")).toBeTruthy();
  },
);

it("consulta el estado por código sin mostrar datos privados del receptor", async () => {
  render(<FiscalConsultation code={code} />);
  await screen.findByText("Adventure Works");
  expect(screen.getByText("PROCESADO")).toBeTruthy();
  expect(screen.getByText(code)).toBeTruthy();
  expect(request).toHaveBeenCalledWith(
    `/consulta/dte/${code}`,
    undefined,
    undefined,
    expect.any(AbortSignal),
  );
});
