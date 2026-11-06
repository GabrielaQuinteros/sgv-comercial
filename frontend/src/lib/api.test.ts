import { afterEach, describe, expect, it, vi } from "vitest";
import { calculateCart, currency, request } from "./api";

afterEach(() => vi.unstubAllGlobals());

describe("ventas", () => {
  it("calcula impuestos y total en centavos", () => {
    expect(calculateCart([{ quantity: 3, price: 19.99 }])).toEqual({
      subtotal: 59.97,
      tax: 7.8,
      total: 67.77,
    });
    expect(calculateCart([])).toEqual({ subtotal: 0, tax: 0, total: 0 });
    expect(currency(10.5)).toContain("10.50");
  });
});

describe("cliente HTTP", () => {
  it("envía autenticación y cuerpo JSON", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValue(new Response('{"id":1}', { status: 201 }));
    vi.stubGlobal("fetch", fetch);
    expect(await request("/sales", "access-token", { lines: [] })).toEqual({
      id: 1,
    });
    expect(fetch).toHaveBeenCalledWith(
      "/api/sales",
      expect.objectContaining({
        method: "POST",
        body: '{"lines":[]}',
        headers: {
          "Content-Type": "application/json",
          Authorization: "Bearer access-token",
        },
      }),
    );
  });
  it("propaga un problema del servidor", async () => {
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValue(
          new Response('{"detail":"Existencias insuficientes"}', {
            status: 409,
          }),
        ),
    );
    await expect(request("/sales")).rejects.toThrow(
      "Existencias insuficientes",
    );
  });
  it("gestiona respuestas vacías y errores sin JSON", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(new Response(null, { status: 204 })),
    );
    expect(await request("/inventory/entries", "token", {})).toBeUndefined();
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(new Response("unauthorized", { status: 401 })),
    );
    await expect(request("/products")).rejects.toThrow("credenciales");
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(new Response("", { status: 503 })),
    );
    await expect(request("/products")).rejects.toThrow("(503)");
  });
});
