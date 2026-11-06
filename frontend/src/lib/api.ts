export interface Product {
  id: number;
  sku: string;
  name: string;
  price: number;
  stock: number;
  minimumStock: number;
}
export interface Session {
  accessToken: string;
  username: string;
  role: string;
  expiresAt: string;
}
export interface Dashboard {
  products: number;
  lowStockProducts: number;
  sales: number;
  revenue: number;
}
export interface Sale {
  id: string;
  subtotal: number;
  tax: number;
  total: number;
  status: string;
}
export interface AuditEvent {
  id: number;
  createdAt: string;
  actor: string;
  action: string;
  entityId: string;
  detail: string;
}

export async function request<T>(
  path: string,
  token?: string,
  body?: unknown,
  signal?: AbortSignal,
): Promise<T> {
  const response = await fetch(`/api${path}`, {
    method: body === undefined ? "GET" : "POST",
    headers: {
      ...(body === undefined ? {} : { "Content-Type": "application/json" }),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
    signal,
  });
  if (!response.ok) {
    const problem = (await response.json().catch(() => ({}))) as {
      detail?: string;
    };
    throw new Error(
      problem.detail ??
        (response.status === 401
          ? "La sesión o las credenciales no son válidas."
          : `No fue posible completar la solicitud (${response.status}).`),
    );
  }
  if (response.status === 204) return undefined as T;
  return response.json() as Promise<T>;
}

export const currency = (amount: number) =>
  new Intl.NumberFormat("es-SV", { style: "currency", currency: "USD" }).format(
    amount,
  );

export function calculateCart(
  items: Array<{ quantity: number; price: number }>,
) {
  const subtotal =
    Math.round(
      items.reduce(
        (sum, item) => sum + Math.round(item.price * 100) * item.quantity,
        0,
      ),
    ) / 100;
  const tax = Math.round(subtotal * 13) / 100;
  return { subtotal, tax, total: Math.round((subtotal + tax) * 100) / 100 };
}
