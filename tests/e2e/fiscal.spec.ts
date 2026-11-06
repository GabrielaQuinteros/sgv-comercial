import { expect, test } from "@playwright/test";

test("emite desde la venta pagada hasta PROCESADO y abre el comprobante @fullstack", async ({
  page,
  request,
}) => {
  const password = process.env.SGV_E2E_PASSWORD;
  if (!password)
    throw new Error("Configura SGV_E2E_PASSWORD para la prueba integrada.");
  const login = await request.post("/api/auth/login", {
    data: { username: "admin", password },
  });
  expect(login.ok()).toBeTruthy();
  const session = (await login.json()) as { accessToken: string };
  const headers = { Authorization: `Bearer ${session.accessToken}` };
  const productResponse = await request.post("/api/products", {
    headers,
    data: {
      sku: `E2E-${Date.now()}`,
      name: "Producto de emisión integrada",
      price: 10,
      minimumStock: 1,
    },
  });
  expect(productResponse.status()).toBe(201);
  const product = (await productResponse.json()) as { id: number };
  expect(
    (
      await request.post("/api/inventory/entries", {
        headers,
        data: { productId: product.id, quantity: 20, reference: "E2E" },
      })
    ).status(),
  ).toBe(204);
  const saleResponse = await request.post("/api/sales", {
    headers,
    data: { lines: [{ productId: product.id, quantity: 2 }] },
  });
  expect(saleResponse.ok()).toBeTruthy();
  const sale = (await saleResponse.json()) as { id: string; total: number };
  expect(
    (
      await request.post(`/api/sales/${sale.id}/payments`, {
        headers,
        data: {
          amount: sale.total,
          method: "Transferencia",
          reference: `E2E-${sale.id}`,
        },
      })
    ).ok(),
  ).toBeTruthy();
  await page.goto("/");
  await page.getByLabel("Usuario", { exact: true }).fill("admin");
  await page.getByLabel("Contraseña", { exact: true }).fill(password);
  await page.getByRole("button", { name: "Entrar a la plataforma" }).click();
  await page
    .getByRole("button", { name: "Facturación electrónica", exact: true })
    .click();
  await page.getByLabel("Buscar venta pagada").fill(sale.id);
  const row = page
    .getByRole("row")
    .filter({ hasText: new RegExp(sale.id, "i") });
  await expect(row).toBeVisible();
  const responsePromise = page.waitForResponse(
    (response) =>
      response.url().endsWith(`/api/ventas/${sale.id}/dte`) &&
      response.request().method() === "POST",
  );
  await row.getByRole("button", { name: "Emitir DTE" }).click();
  const response = await responsePromise;
  expect(response.ok()).toBeTruthy();
  const doc = (await response.json()) as {
    estado: string;
    selloRecibido: string;
    codigoGeneracion: string;
  };
  expect(doc.estado).toBe("PROCESADO");
  expect(doc.selloRecibido).toMatch(/^[A-Z0-9]{40}$/);
  const detail = page.getByRole("region", { name: "Detalle del DTE" });
  await expect(detail).toContainText("PROCESADO");
  await expect(detail).toContainText(doc.codigoGeneracion);
  await expect(row).toContainText("Facturada");
  await detail.getByRole("button", { name: "Ver comprobante" }).click();
  await expect(
    page.getByRole("article", { name: "Comprobante imprimible" }),
  ).toContainText(doc.selloRecibido);
  const consult = await request.get(
    `/api/consulta/dte/${doc.codigoGeneracion}`,
  );
  expect(consult.ok()).toBeTruthy();
  expect(((await consult.json()) as { estado: string }).estado).toBe(
    "PROCESADO",
  );
});
