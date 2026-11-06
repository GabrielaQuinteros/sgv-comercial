import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { App } from "./App";
import { request } from "./lib/api";

vi.mock("./lib/api", async () => ({
  ...(await vi.importActual<typeof import("./lib/api")>("./lib/api")),
  request: vi.fn(),
}));

const product = {
  id: 1,
  name: "Casco",
  sku: "AW-001",
  price: 10,
  stock: 10,
  minimumStock: 2,
};
const sale = {
  id: "7f6377e8-9383-4875-96a4-a9436f851cec",
  subtotal: 20,
  tax: 2.6,
  total: 22.6,
  status: "Pendiente",
};

beforeEach(() => {
  vi.mocked(request).mockImplementation(async <T,>(path: string) => {
    let result: unknown;
    if (path === "/auth/login")
      result = {
        accessToken: "session",
        username: "operador",
        role: "Admin",
        expiresAt: new Date(Date.now() + 900_000).toISOString(),
      };
    else if (path === "/products") result = [product];
    else if (path === "/dashboard")
      result = { products: 1, lowStockProducts: 0, sales: 1, revenue: 22.6 };
    else if (path === "/sales") result = sale;
    else if (path.endsWith("/payments"))
      result = {
        id: "payment",
        saleId: sale.id,
        amount: 22.6,
        method: "Efectivo",
        reference: "PAY-001",
        status: "Pagada",
      };
    else if (path.startsWith("/audit?"))
      result = [
        {
          id: 1,
          createdAt: "2026-11-06T16:00:00Z",
          actor: "operador",
          action: "Venta creada",
          entityId: sale.id,
          detail: "Registro de venta",
        },
      ];
    else if (path === "/dte/sign")
      result = { id: "document-001", signature: "signature" };
    return result as T;
  });
});
afterEach(() => {
  cleanup();
  vi.resetAllMocks();
});

async function login() {
  render(<App />);
  fireEvent.change(screen.getByLabelText("Usuario", { exact: true }), {
    target: { value: "operador" },
  });
  fireEvent.change(screen.getByLabelText("Contraseña", { exact: true }), {
    target: { value: "user-password" },
  });
  fireEvent.click(
    screen.getByRole("button", { name: /Entrar a la plataforma/ }),
  );
  await screen.findByRole("heading", { name: "Tu operación, en perspectiva" });
  await screen.findByText("Casco");
}

it("registra una venta y confirma el pago desde la interfaz", async () => {
  await login();
  fireEvent.click(screen.getByRole("button", { name: "Ventas" }));
  fireEvent.change(screen.getByLabelText("Cantidad", { exact: true }), {
    target: { value: "2" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Añadir" }));
  fireEvent.click(
    screen.getByRole("button", { name: "Guardar venta" }),
  );
  await screen.findByRole("heading", { name: /Registrar pago/ });
  fireEvent.change(screen.getByLabelText("Referencia del pago"), {
    target: { value: "PAY-001" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Confirmar pago" }));
  await screen.findByText("Pago registrado correctamente.");
  expect(screen.getByText("Pagada")).toBeTruthy();
  expect(request).toHaveBeenCalledWith(
    `/sales/${sale.id}/payments`,
    "session",
    { amount: 22.6, method: "Efectivo", reference: "PAY-001" },
  );
});

it("opera inventario, auditoría, firma y cierre de sesión", async () => {
  await login();
  fireEvent.click(
    screen.getByRole("button", { name: "Inventario" }),
  );
  fireEvent.change(screen.getByLabelText("Buscar producto"), {
    target: { value: "AW-001" },
  });
  fireEvent.change(screen.getByLabelText("Cantidad", { exact: true }), {
    target: { value: "3" },
  });
  fireEvent.change(screen.getByLabelText("Referencia", { exact: true }), {
    target: { value: "REC-001" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Registrar entrada" }));
  await screen.findByText("Entrada registrada correctamente.");
  expect(request).toHaveBeenCalledWith("/inventory/entries", "session", {
    productId: 1,
    quantity: 3,
    reference: "REC-001",
  });
  fireEvent.click(
    screen.getByRole("button", { name: "Auditoría" }),
  );
  fireEvent.change(screen.getByLabelText("Desde", { exact: true }), {
    target: { value: "2026-11-06T09:00" },
  });
  fireEvent.change(screen.getByLabelText("Hasta", { exact: true }), {
    target: { value: "2026-11-06T18:00" },
  });
  fireEvent.click(
    screen.getByRole("button", { name: "Consultar" }),
  );
  await screen.findByText("Venta creada");
  fireEvent.click(
    screen.getByRole("button", { name: "Facturación DTE" }),
  );
  fireEvent.change(screen.getByLabelText("Identificador de venta"), {
    target: { value: sale.id },
  });
  fireEvent.change(screen.getByLabelText("Total USD"), {
    target: { value: "113" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Generar y firmar" }));
  await screen.findByText("Documento firmado: document-001");
  expect(request).toHaveBeenCalledWith("/dte/sign", "session", {
    saleId: sale.id,
    total: 113,
    currency: "USD",
  });
  fireEvent.click(screen.getByRole("button", { name: "Cerrar sesión" }));
  await screen.findByRole("heading", { name: "Iniciar sesión" });
  expect(screen.getByRole("heading", { name: "Iniciar sesión" })).toBeTruthy();
});

it("muestra un error de autenticación sin abrir una sesión", async () => {
  vi.mocked(request).mockRejectedValueOnce(
    new Error("Credenciales no válidas"),
  );
  render(<App />);
  fireEvent.change(screen.getByLabelText("Usuario", { exact: true }), {
    target: { value: "operador" },
  });
  fireEvent.change(screen.getByLabelText("Contraseña", { exact: true }), {
    target: { value: "invalid" },
  });
  fireEvent.click(
    screen.getByRole("button", { name: /Entrar a la plataforma/ }),
  );
  await waitFor(() =>
    expect(screen.getByRole("alert").textContent).toBe(
      "Credenciales no válidas",
    ),
  );
});
