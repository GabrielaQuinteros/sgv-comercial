// @vitest-environment node
import { afterEach, expect, it, vi } from "vitest";
import { downloadAudit } from "./download";

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  vi.useRealTimers();
});

it("descarga el CSV autorizado y libera el recurso del navegador", async () => {
  vi.useFakeTimers();
  const fetch = vi
    .fn()
    .mockResolvedValue(new Response("Id,Usuario\n1,qa", { status: 200 }));
  const link = { href: "", download: "", click: vi.fn(), remove: vi.fn() };
  const appendChild = vi.fn();
  vi.stubGlobal("fetch", fetch);
  vi.stubGlobal("document", {
    createElement: vi.fn().mockReturnValue(link),
    body: { appendChild },
  });
  vi.spyOn(URL, "createObjectURL").mockReturnValue("blob:audit");
  const revoke = vi.spyOn(URL, "revokeObjectURL").mockImplementation(() => {});
  await downloadAudit("2026-11-01T00:00:00Z", "2026-11-02T00:00:00Z", "token");
  expect(fetch).toHaveBeenCalledWith(
    expect.stringContaining("/api/audit/export?from="),
    { headers: { Authorization: "Bearer token" } },
  );
  expect(link.download).toBe("bitacora-2026-11-01-2026-11-02.csv");
  expect(appendChild).toHaveBeenCalledWith(link);
  expect(link.click).toHaveBeenCalledOnce();
  expect(link.remove).toHaveBeenCalledOnce();
  vi.runAllTimers();
  expect(revoke).toHaveBeenCalledWith("blob:audit");
});

it("rechaza rangos inválidos y fallos del servidor", async () => {
  await expect(downloadAudit("invalid", "2026-11-01", "token")).rejects.toThrow(
    "31 días",
  );
  await expect(
    downloadAudit("2026-11-02", "2026-11-01", "token"),
  ).rejects.toThrow("31 días");
  await expect(
    downloadAudit("2026-01-01", "2026-11-01", "token"),
  ).rejects.toThrow("31 días");
  vi.stubGlobal(
    "fetch",
    vi.fn().mockResolvedValue(new Response(null, { status: 403 })),
  );
  await expect(
    downloadAudit("2026-11-01", "2026-11-02", "token"),
  ).rejects.toThrow("permisos");
});
