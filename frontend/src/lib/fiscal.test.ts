// @vitest-environment node
import { afterEach, expect, it, vi } from "vitest";
import {
  exportFiscalCsv,
  fiscalQuery,
  fiscalResponse,
  parsePayload,
  type DteRecord,
} from "./fiscal";
afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  vi.useRealTimers();
});
it("construye intervalos inclusivos por fecha y maneja JSON ausente", () => {
  expect(fiscalQuery({ desde: "", hasta: "", tipo: "", estado: "" })).toBe("");
  const query = fiscalQuery({
    desde: "2026-11-01",
    hasta: "2026-11-06",
    tipo: "03",
    estado: "PROCESADO",
  });
  expect(query).toContain("tipo=03");
  expect(decodeURIComponent(query)).toContain("2026-11-07T06:00:00.000Z");
  expect(parsePayload({ json: null } as DteRecord)).toBeNull();
  expect(parsePayload({ json: "invalid" } as DteRecord)).toBeNull();
  expect(fiscalResponse({ respuesta: null } as DteRecord)).toEqual({});
  expect(fiscalResponse({ respuesta: "invalid" } as DteRecord)).toEqual({});
});
it("exporta el CSV autenticado y libera la URL", async () => {
  vi.useFakeTimers();
  const link = { href: "", download: "", click: vi.fn(), remove: vi.fn() };
  vi.stubGlobal("document", {
    createElement: () => link,
    body: { appendChild: vi.fn() },
  });
  vi.spyOn(URL, "createObjectURL").mockReturnValue("blob:csv");
  const revoke = vi.spyOn(URL, "revokeObjectURL").mockImplementation(() => {});
  const fetch = vi
    .fn()
    .mockResolvedValue(new Response("Fecha,Tipo", { status: 200 }));
  vi.stubGlobal("fetch", fetch);
  await exportFiscalCsv(
    { desde: "", hasta: "", tipo: "01", estado: "" },
    "token",
  );
  expect(fetch).toHaveBeenCalledWith("/api/dte/exportar?tipo=01", {
    headers: { Authorization: "Bearer token" },
  });
  expect(link.click).toHaveBeenCalledOnce();
  vi.runAllTimers();
  expect(revoke).toHaveBeenCalledWith("blob:csv");
  fetch.mockResolvedValue(new Response(null, { status: 403 }));
  await expect(
    exportFiscalCsv({ desde: "", hasta: "", tipo: "", estado: "" }, "token"),
  ).rejects.toThrow("No se pudo");
});
