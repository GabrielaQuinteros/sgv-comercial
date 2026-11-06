// @vitest-environment node
import { mkdtempSync, readFileSync, rmSync, statSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, expect, it, vi } from "vitest";
import { run } from "../../../scripts/demo-config.mjs";

let folder;
afterEach(() => {
  if (folder) rmSync(folder, { recursive: true, force: true });
  vi.restoreAllMocks();
});
it("genera credenciales distintas, privadas e idempotentes", () => {
  folder = mkdtempSync(join(tmpdir(), "sgv-env-"));
  run("prepare", folder);
  const file = join(folder, ".local/demo.env");
  const first = readFileSync(file, "utf8");
  const values = first
    .split("\n")
    .filter((line) => line.includes("PASSWORD=") || line.includes("KEY="))
    .map((line) => line.split("=")[1]);
  expect(new Set(values).size).toBe(values.length);
  expect(values.every((value) => value.length >= 32)).toBe(true);
  expect(statSync(file).mode & 0o777).toBe(0o600);
  run("prepare", folder);
  expect(readFileSync(file, "utf8")).toBe(first);
});
it("prepara el acceso sin incluir credenciales estáticas en los scripts", () => {
  folder = mkdtempSync(join(tmpdir(), "sgv-env-"));
  run("prepare", folder);
  const log = vi.spyOn(console, "log").mockImplementation(() => {});
  run("credentials", folder);
  expect(log).toHaveBeenCalledWith("Usuario: admin");
  const output = vi
    .spyOn(process.stdout, "write")
    .mockImplementation(() => true);
  run("login-payload", folder);
  const data = JSON.parse(output.mock.calls[0][0]);
  expect(data.username).toBe("admin");
  expect(data.password.length).toBeGreaterThanOrEqual(32);
  expect(() => run("invalid", folder)).toThrow("Usa prepare");
});
