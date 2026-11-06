import { useState, type FormEvent } from "react";
import { currency, request } from "../lib/api";

export interface PaymentReceipt {
  id: string;
  saleId: string;
  amount: number;
  method: string;
  reference: string;
  status: string;
}

export function PaymentForm({
  saleId,
  amount,
  token,
  onPaid,
}: {
  saleId: string;
  amount: number;
  token: string;
  onPaid: (receipt: PaymentReceipt) => void;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const reference = String(data.get("reference") ?? "").trim();
    if (!reference) {
      setError("Introduce una referencia de pago.");
      return;
    }
    setBusy(true);
    setError("");
    try {
      onPaid(
        await request<PaymentReceipt>(`/sales/${saleId}/payments`, token, {
          amount,
          method: data.get("method"),
          reference,
        }),
      );
    } catch (failure) {
      setError(
        failure instanceof Error
          ? failure.message
          : "No fue posible registrar el pago.",
      );
    } finally {
      setBusy(false);
    }
  }
  return (
    <form onSubmit={submit} aria-label="Registrar pago">
      <h3>Registrar pago · {currency(amount)}</h3>
      <label>
        Método de pago
        <select name="method">
          <option>Efectivo</option>
          <option>Tarjeta</option>
          <option>Transferencia</option>
        </select>
      </label>
      <label>
        Referencia del pago
        <input name="reference" maxLength={100} required />
      </label>
      {error && <p role="alert">{error}</p>}
      <button type="submit" className="primary full" disabled={busy}>
        {busy ? "Registrando…" : "Confirmar pago"}
      </button>
    </form>
  );
}
