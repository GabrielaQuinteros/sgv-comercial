import { useEffect, useState, type FormEvent } from "react";
import { PaymentForm } from "./components/PaymentForm";
import {
  calculateCart,
  currency,
  request,
  type AuditEvent,
  type Dashboard,
  type Product,
  type Sale,
  type Session,
} from "./lib/api";

type View =
  | "Resumen"
  | "Ventas"
  | "Inventario"
  | "Auditoría"
  | "Facturación DTE";
const views: View[] = [
  "Resumen",
  "Ventas",
  "Inventario",
  "Auditoría",
  "Facturación DTE",
];

export function App() {
  const [session, setSession] = useState<Session | null>(null);
  const [view, setView] = useState<View>("Resumen");
  const [products, setProducts] = useState<Product[]>([]);
  const [dashboard, setDashboard] = useState<Dashboard | null>(null);
  const [events, setEvents] = useState<AuditEvent[]>([]);
  const [cart, setCart] = useState<
    Array<{ product: Product; quantity: number }>
  >([]);
  const [sale, setSale] = useState<Sale | null>(null);
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  const [revision, setRevision] = useState(0);
  const [search, setSearch] = useState("");
  const total = calculateCart(
    cart.map((item) => ({
      quantity: item.quantity,
      price: item.product.price,
    })),
  );

  useEffect(() => {
    if (!session) return;
    const controller = new AbortController();
    Promise.all([
      request<Product[]>(
        "/products",
        session.accessToken,
        undefined,
        controller.signal,
      ),
      request<Dashboard>(
        "/dashboard",
        session.accessToken,
        undefined,
        controller.signal,
      ),
    ])
      .then(([catalog, summary]) => {
        setProducts(catalog);
        setDashboard(summary);
      })
      .catch((error: Error) => {
        if (error.name !== "AbortError") setMessage(error.message);
      });
    const expires = Math.max(0, Date.parse(session.expiresAt) - Date.now());
    const timer = setTimeout(() => {
      setSession(null);
      setMessage("Tu sesión ha expirado. Inicia sesión nuevamente.");
    }, expires);
    return () => {
      controller.abort();
      clearTimeout(timer);
    };
  }, [session, revision]);

  async function run(operation: () => Promise<void>) {
    setBusy(true);
    setMessage("");
    try {
      await operation();
    } catch (error) {
      setMessage(
        error instanceof Error
          ? error.message
          : "No fue posible completar la operación.",
      );
    } finally {
      setBusy(false);
    }
  }

  function login(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    void run(async () => {
      const result = await request<Session>("/auth/login", undefined, {
        username: data.get("username"),
        password: data.get("password"),
      });
      setSession(result);
    });
  }

  if (!session)
    return (
      <div className="login-page">
        <div className="login-story">
          <div className="brand">
            <span className="brand-mark">AW</span> ADVENTURE WORKS
          </div>
          <div>
            <p className="eyebrow">SGV · PLATAFORMA COMERCIAL</p>
            <h1>
              Todo tu negocio.
              <br />
              <span>Una sola conexión.</span>
            </h1>
            <p>
              Ventas, inventario y facturación electrónica, con la trazabilidad
              que tu operación necesita.
            </p>
          </div>
          <p className="login-footer">
            Control de acceso · Auditoría · Gestión comercial
          </p>
        </div>
        <main className="login-form">
          <div className="login-box">
            <p className="eyebrow">BIENVENIDO A SGV-COMERCIAL</p>
            <h2>Iniciar sesión</h2>
            <p className="muted">
              Accede con las credenciales de tu organización.
            </p>
            <form onSubmit={login}>
              <label>
                Usuario
                <input
                  name="username"
                  autoComplete="username"
                  required
                  maxLength={100}
                />
              </label>
              <label>
                Contraseña
                <input
                  name="password"
                  type="password"
                  autoComplete="current-password"
                  required
                  maxLength={128}
                />
              </label>
              {message && (
                <p className="notice" role="alert">
                  {message}
                </p>
              )}
              <button disabled={busy} className="primary">
                {busy ? "Verificando…" : "Entrar a la plataforma →"}
              </button>
            </form>
            <small>Acceso exclusivo para usuarios autorizados.</small>
          </div>
        </main>
      </div>
    );

  const filteredProducts = products.filter((product) =>
    `${product.name} ${product.sku}`
      .toLowerCase()
      .includes(search.toLowerCase()),
  );
  return (
    <div className="workspace">
      <aside className="sidebar">
        <div className="brand">
          <span className="brand-mark">AW</span>
          <div>
            Adventure Works<small>SGV-Comercial</small>
          </div>
        </div>
        <p className="nav-caption">OPERACIÓN</p>
        <nav aria-label="Módulos">
          {views.map((item, index) => (
            <button
              key={item}
              className={view === item ? "nav-item active" : "nav-item"}
              onClick={() => {
                setView(item);
                setMessage("");
              }}
            >
              <span aria-hidden="true">
                {["◈", "↗", "▦", "◎", "▤"][index]}
              </span>
              {item}
            </button>
          ))}
        </nav>
        <div className="sidebar-bottom">
          <span className="avatar">
            {session.username.slice(0, 2).toUpperCase()}
          </span>
          <div>
            {session.username}
            <small>{session.role}</small>
          </div>
          <button
            className="logout"
            onClick={() => {
              setSession(null);
              setCart([]);
              setSale(null);
              setProducts([]);
              setDashboard(null);
            }}
            aria-label="Cerrar sesión"
          >
            ↪
          </button>
        </div>
      </aside>
      <div className="content">
        <header className="topbar">
          <span>
            Espacio de trabajo <b>/ {view}</b>
          </span>
          <span className="status-dot">Sesión protegida</span>
        </header>
        <main>
          <div className="page-heading">
            <div>
              <p className="eyebrow">GESTIÓN COMERCIAL</p>
              <h1>
                {view === "Resumen" ? "Tu operación, en perspectiva" : view}
              </h1>
              <p className="muted">
                {view === "Resumen"
                  ? "Información centralizada para tomar mejores decisiones."
                  : "Adventure Works · SGV-Comercial"}
              </p>
            </div>
            <button
              className="secondary"
              onClick={() => setRevision((value) => value + 1)}
            >
              ↻ Actualizar
            </button>
          </div>
          {message && (
            <div className="notice" role="status">
              {message}
              <button onClick={() => setMessage("")} aria-label="Cerrar aviso">
                ×
              </button>
            </div>
          )}
          {view === "Resumen" && (
            <>
              <section className="metrics" aria-label="Indicadores">
                <Metric
                  title="Ventas registradas"
                  value={dashboard?.sales.toString() ?? "—"}
                  caption="Total de operaciones"
                />
                <Metric
                  title="Ingresos por ventas"
                  value={dashboard ? currency(dashboard.revenue) : "—"}
                  caption="Ventas pagadas · USD"
                />
                <Metric
                  title="Productos en catálogo"
                  value={dashboard?.products.toString() ?? "—"}
                  caption="Inventario centralizado"
                />
                <Metric
                  title="Alertas de inventario"
                  value={dashboard?.lowStockProducts.toString() ?? "—"}
                  caption="Productos bajo el mínimo"
                />
              </section>
              <section className="panel">
                <div className="section-heading">
                  <div>
                    <h2>Control de inventario</h2>
                    <p className="muted">
                      Disponibilidad y niveles mínimos del catálogo.
                    </p>
                  </div>
                  <button
                    className="text-button"
                    onClick={() => setView("Inventario")}
                  >
                    Ver inventario →
                  </button>
                </div>
                <ProductTable products={products} />
              </section>
              <section className="quick-grid">
                <div className="panel accent">
                  <p className="eyebrow">VENTAS</p>
                  <h2>De la selección al cierre.</h2>
                  <p>
                    Crea una venta con precios del catálogo e impuestos
                    calculados automáticamente.
                  </p>
                  <button className="primary" onClick={() => setView("Ventas")}>
                    Registrar una venta →
                  </button>
                </div>
                <div className="panel">
                  <p className="eyebrow">TRAZABILIDAD</p>
                  <h2>Cada cambio cuenta.</h2>
                  <p className="muted">
                    Consulta eventos de acceso, movimientos de inventario y
                    operaciones comerciales.
                  </p>
                  <button
                    className="secondary"
                    onClick={() => setView("Auditoría")}
                  >
                    Consultar bitácora
                  </button>
                </div>
              </section>
            </>
          )}
          {view === "Inventario" && (
            <>
              <section className="panel">
                <div className="section-heading">
                  <h2>Catálogo de productos</h2>
                  <input
                    aria-label="Buscar producto"
                    placeholder="Buscar por nombre o SKU…"
                    value={search}
                    onChange={(event) => setSearch(event.target.value)}
                  />
                </div>
                <ProductTable products={filteredProducts} />
              </section>
              <section className="panel">
                <h2>Entrada de mercadería</h2>
                <form
                  className="inline-form"
                  onSubmit={(event) => {
                    event.preventDefault();
                    const form = event.currentTarget;
                    const data = new FormData(form);
                    void run(async () => {
                      await request("/inventory/entries", session.accessToken, {
                        productId: Number(data.get("product")),
                        quantity: Number(data.get("quantity")),
                        reference: data.get("reference"),
                      });
                      form.reset();
                      setRevision((value) => value + 1);
                      setMessage("Entrada registrada correctamente.");
                    });
                  }}
                >
                  <label>
                    Producto
                    <select name="product" required>
                      {products.map((product) => (
                        <option key={product.id} value={product.id}>
                          {product.name}
                        </option>
                      ))}
                    </select>
                  </label>
                  <label>
                    Cantidad
                    <input
                      name="quantity"
                      type="number"
                      min={1}
                      max={100000}
                      required
                    />
                  </label>
                  <label>
                    Referencia
                    <input name="reference" maxLength={100} required />
                  </label>
                  <button className="primary" disabled={busy}>
                    Registrar entrada
                  </button>
                </form>
              </section>
            </>
          )}
          {view === "Ventas" && (
            <div className="sales-grid">
              <section className="panel">
                <h2>Nueva venta</h2>
                <form
                  className="inline-form"
                  onSubmit={(event) => {
                    event.preventDefault();
                    const data = new FormData(event.currentTarget);
                    const product = products.find(
                      (item) => item.id === Number(data.get("product")),
                    );
                    const quantity = Number(data.get("quantity"));
                    if (!product || quantity <= 0) return;
                    setCart((items) => [
                      ...items.filter((item) => item.product.id !== product.id),
                      { product, quantity },
                    ]);
                    setSale(null);
                  }}
                >
                  <label>
                    Producto
                    <select name="product">
                      {products.map((product) => (
                        <option key={product.id} value={product.id}>
                          {product.name} · {currency(product.price)}
                        </option>
                      ))}
                    </select>
                  </label>
                  <label>
                    Cantidad
                    <input
                      name="quantity"
                      type="number"
                      min={1}
                      max={100000}
                      defaultValue={1}
                      required
                    />
                  </label>
                  <button className="secondary">Añadir</button>
                </form>
                <table>
                  <thead>
                    <tr>
                      <th>Producto</th>
                      <th>Cantidad</th>
                      <th>Importe</th>
                      <th aria-label="Acciones" />
                    </tr>
                  </thead>
                  <tbody>
                    {cart.map((item) => (
                      <tr key={item.product.id}>
                        <td>{item.product.name}</td>
                        <td>{item.quantity}</td>
                        <td>{currency(item.product.price * item.quantity)}</td>
                        <td>
                          <button
                            type="button"
                            className="text-button"
                            onClick={() => {
                              setCart((items) =>
                                items.filter((row) => row !== item),
                              );
                              setSale(null);
                            }}
                            aria-label={`Quitar ${item.product.name}`}
                          >
                            ×
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
                {cart.length === 0 && (
                  <p className="empty">
                    Añade productos para comenzar la venta.
                  </p>
                )}
              </section>
              <section className="panel">
                <p className="eyebrow">RESUMEN DE LA VENTA</p>
                <div className="total-row">
                  <span>Subtotal</span>
                  <b>{currency(total.subtotal)}</b>
                </div>
                <div className="total-row">
                  <span>IVA · 13 %</span>
                  <b>{currency(total.tax)}</b>
                </div>
                <div className="total-row grand-total">
                  <span>Total</span>
                  <b>{currency(total.total)}</b>
                </div>
                <button
                  className="primary full"
                  disabled={busy || cart.length === 0 || sale !== null}
                  onClick={() =>
                    void run(async () => {
                      const result = await request<Sale>(
                        "/sales",
                        session.accessToken,
                        {
                          lines: cart.map((item) => ({
                            productId: item.product.id,
                            quantity: item.quantity,
                          })),
                        },
                      );
                      setSale(result);
                      setRevision((value) => value + 1);
                      setMessage("Venta registrada.");
                    })
                  }
                >
                  Guardar venta
                </button>
                {sale && (
                  <div className="sale-result">
                    <strong>{sale.status}</strong>
                    <p>Venta: {sale.id}</p>
                    <p>Total confirmado: {currency(sale.total)}</p>
                    {sale.status === "Pendiente" && (
                      <PaymentForm
                        saleId={sale.id}
                        amount={sale.total}
                        token={session.accessToken}
                        onPaid={() => {
                          setSale({ ...sale, status: "Pagada" });
                          setRevision((value) => value + 1);
                          setMessage("Pago registrado correctamente.");
                        }}
                      />
                    )}
                  </div>
                )}
              </section>
            </div>
          )}
          {view === "Auditoría" && (
            <section className="panel">
              <h2>Bitácora de eventos</h2>
              <p className="muted">
                Consulta intervalos de hasta 31 días. Acceso para
                administradores y auditores.
              </p>
              <form
                className="inline-form"
                onSubmit={(event) => {
                  event.preventDefault();
                  const data = new FormData(event.currentTarget);
                  void run(async () => {
                    const from = new Date(
                      String(data.get("from")),
                    ).toISOString();
                    const to = new Date(String(data.get("to"))).toISOString();
                    setEvents(
                      await request<AuditEvent[]>(
                        `/audit?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`,
                        session.accessToken,
                      ),
                    );
                  });
                }}
              >
                <label>
                  Desde
                  <input type="datetime-local" name="from" required />
                </label>
                <label>
                  Hasta
                  <input type="datetime-local" name="to" required />
                </label>
                <button className="primary" disabled={busy}>
                  Consultar
                </button>
              </form>
              <table>
                <thead>
                  <tr>
                    <th>Fecha</th>
                    <th>Responsable</th>
                    <th>Evento</th>
                    <th>Detalle</th>
                  </tr>
                </thead>
                <tbody>
                  {events.map((item) => (
                    <tr key={item.id}>
                      <td>
                        {new Date(item.createdAt).toLocaleString("es-SV")}
                      </td>
                      <td>{item.actor}</td>
                      <td>{item.action}</td>
                      <td>{item.detail}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
              {events.length === 0 && (
                <p className="empty">
                  Selecciona un rango para consultar la actividad.
                </p>
              )}
            </section>
          )}
          {view === "Facturación DTE" && (
            <section className="panel">
              <p className="eyebrow">DOCUMENTOS TRIBUTARIOS ELECTRÓNICOS</p>
              <h2>Preparar documento firmado</h2>
              <p className="muted">
                El servicio utiliza el certificado .P12 configurado por la
                organización.
              </p>
              <form
                className="inline-form"
                onSubmit={(event) => {
                  event.preventDefault();
                  const data = new FormData(event.currentTarget);
                  void run(async () => {
                    const document = await request<{
                      id: string;
                      signature: string;
                    }>("/dte/sign", session.accessToken, {
                      saleId: data.get("saleId"),
                      total: Number(data.get("total")),
                      currency: "USD",
                    });
                    setMessage(`Documento firmado: ${document.id}`);
                  });
                }}
              >
                <label>
                  Identificador de venta
                  <input
                    name="saleId"
                    required
                    pattern="[a-fA-F0-9-]{36}"
                    placeholder="UUID de la venta"
                  />
                </label>
                <label>
                  Total USD
                  <input
                    name="total"
                    type="number"
                    step="0.01"
                    min="0.01"
                    required
                  />
                </label>
                <button className="primary" disabled={busy}>
                  Generar y firmar
                </button>
              </form>
            </section>
          )}
          <footer>
            SGV-Comercial <span>Adventure Works · Equipo Pepsiman</span>
          </footer>
        </main>
      </div>
    </div>
  );
}

function Metric({
  title,
  value,
  caption,
}: {
  title: string;
  value: string;
  caption: string;
}) {
  return (
    <article className="metric">
      <p>{title}</p>
      <strong>{value}</strong>
      <small>{caption}</small>
    </article>
  );
}

function ProductTable({ products }: { products: Product[] }) {
  return (
    <div className="table-scroll">
      <table>
        <thead>
          <tr>
            <th>Producto</th>
            <th>SKU</th>
            <th>Precio</th>
            <th>Existencias</th>
            <th>Estado</th>
          </tr>
        </thead>
        <tbody>
          {products.map((product) => (
            <tr key={product.id}>
              <td className="product-name">{product.name}</td>
              <td className="muted">{product.sku}</td>
              <td>{currency(product.price)}</td>
              <td>
                {product.stock} <span className="muted">uds.</span>
              </td>
              <td>
                <span
                  className={
                    product.stock <= product.minimumStock
                      ? "badge warning"
                      : "badge"
                  }
                >
                  {product.stock <= product.minimumStock
                    ? "Bajo el mínimo"
                    : "Disponible"}
                </span>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {products.length === 0 && (
        <p className="empty">No hay productos para mostrar.</p>
      )}
    </div>
  );
}
