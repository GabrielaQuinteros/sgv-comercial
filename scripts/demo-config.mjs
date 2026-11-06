import { randomBytes } from "node:crypto";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const parse = (text) =>
  new Map(
    text
      .split("\n")
      .filter(Boolean)
      .map((line) => {
        const split = line.indexOf("=");
        return [line.slice(0, split), line.slice(split + 1)];
      }),
  );
const makeSecret = () =>
  randomBytes(24).toString("base64url") + String.fromCharCode(65, 57, 33);

export function run(
  command,
  root = resolve(dirname(fileURLToPath(import.meta.url)), ".."),
) {
  const file = resolve(root, ".local/demo.env");
  const load = () => parse(readFileSync(file, "utf8"));
  switch (command) {
    case "prepare": {
      mkdirSync(resolve(root, ".local/certificates"), {
        recursive: true,
        mode: 0o700,
      });
      let values;
      try {
        values = load();
      } catch (error) {
        if (error.code !== "ENOENT") throw error;
        values = new Map();
      }
      for (const name of [
        "SGV_SQL_PASSWORD",
        "SGV_JWT_KEY",
        "SGV_ADMIN_PASSWORD",
        "DTE_SERVICE_KEY",
        "DTE_CERTIFICATE_PASSWORD",
      ])
        if (!values.has(name)) values.set(name, makeSecret());
      values.set("LOCAL_UID", String(process.getuid?.() ?? 1000));
      values.set("LOCAL_GID", String(process.getgid?.() ?? 1000));
      writeFileSync(
        file,
        [...values].map(([name, value]) => `${name}=${value}`).join("\n") +
          "\n",
        { mode: 0o600 },
      );
      break;
    }
    case "credentials":
      console.log("Usuario: admin");
      console.log("Contraseña: " + load().get("SGV_ADMIN_PASSWORD"));
      break;
    case "login-payload":
      process.stdout.write(
        JSON.stringify({
          username: "admin",
          password: load().get("SGV_ADMIN_PASSWORD"),
        }),
      );
      break;
    default:
      throw new Error("Usa prepare, credentials o login-payload.");
  }
}
if (
  process.argv[1] &&
  resolve(process.argv[1]) === fileURLToPath(import.meta.url)
)
  run(process.argv[2]);
