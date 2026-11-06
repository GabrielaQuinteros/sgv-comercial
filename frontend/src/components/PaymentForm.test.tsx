import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { PaymentForm } from "./PaymentForm";
import { request } from "../lib/api";

vi.mock("../lib/api", async () => ({
  ...(await vi.importActual<typeof import("../lib/api")>("../lib/api")),
  request: vi.fn(),
}));
afterEach(() => {
  cleanup();
  vi.resetAllMocks();
});

it("registra el pago confirmado y comunica el recibo", async () => {
  const onPaid = vi.fn();
  const receipt = {
    id: "payment",
    saleId: "sale",
    amount: 22.6,
    method: "Tarjeta",
    reference: "PAY-001",
    status: "Pagada",
  };
  vi.mocked(request).mockResolvedValue(receipt);
  render(
    <PaymentForm saleId="sale" amount={22.6} token="token" onPaid={onPaid} />,
  );
  fireEvent.change(screen.getByLabelText("Método de pago"), {
    target: { value: "Tarjeta" },
  });
  fireEvent.change(screen.getByLabelText("Referencia del pago"), {
    target: { value: " PAY-001 " },
  });
  fireEvent.submit(screen.getByRole("form", { name: "Registrar pago" }));
  await waitFor(() => expect(onPaid).toHaveBeenCalledWith(receipt));
  expect(request).toHaveBeenCalledWith("/sales/sale/payments", "token", {
    amount: 22.6,
    method: "Tarjeta",
    reference: "PAY-001",
  });
});

it("muestra el rechazo y permite corregir la solicitud", async () => {
  vi.mocked(request).mockRejectedValue(new Error("Existencias insuficientes"));
  const onPaid = vi.fn();
  render(
    <PaymentForm saleId="sale" amount={22.6} token="token" onPaid={onPaid} />,
  );
  fireEvent.change(screen.getByLabelText("Referencia del pago"), {
    target: { value: "PAY-002" },
  });
  fireEvent.submit(screen.getByRole("form", { name: "Registrar pago" }));
  await waitFor(() =>
    expect(screen.getByRole("alert").textContent).toBe(
      "Existencias insuficientes",
    ),
  );
  expect(onPaid).not.toHaveBeenCalled();
  expect(
    screen
      .getByRole("button", { name: "Confirmar pago" })
      .hasAttribute("disabled"),
  ).toBe(false);
});

it("exige una referencia antes de enviar el pago", () => {
  render(
    <PaymentForm saleId="sale" amount={22.6} token="token" onPaid={vi.fn()} />,
  );
  fireEvent.submit(screen.getByRole("form", { name: "Registrar pago" }));
  expect(screen.getByRole("alert").textContent).toBe(
    "Introduce una referencia de pago.",
  );
  expect(request).not.toHaveBeenCalled();
});
