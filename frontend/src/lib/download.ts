export async function downloadAudit(from: string, to: string, token: string) {
  const start = new Date(from);
  const end = new Date(to);
  const duration = end.getTime() - start.getTime();
  if (!Number.isFinite(duration) || duration <= 0 || duration > 31 * 86_400_000)
    throw new Error("Selecciona un rango mayor que cero y de hasta 31 días.");
  const response = await fetch(
    `/api/audit/export?from=${encodeURIComponent(start.toISOString())}&to=${encodeURIComponent(end.toISOString())}`,
    { headers: { Authorization: `Bearer ${token}` } },
  );
  if (!response.ok)
    throw new Error(
      "No fue posible exportar la bitácora. Verifica el rango y los permisos.",
    );
  const url = URL.createObjectURL(await response.blob());
  const link = document.createElement("a");
  link.href = url;
  link.download = `bitacora-${start.toISOString().slice(0, 10)}-${end.toISOString().slice(0, 10)}.csv`;
  document.body.appendChild(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 0);
}
