// Copies the static export output (out/) into the .NET backend's wwwroot/, so a plain
// `dotnet build`/`publish` picks up whatever was last built here without needing Node.js
// installed on a backend-only contributor's machine.
import { cpSync, existsSync, rmSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";

const frontendDir = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const sourceDir = path.join(frontendDir, "out");
const targetDir = path.join(frontendDir, "..", "iFakeLocation", "wwwroot");

if (!existsSync(sourceDir)) {
  console.error(`copy-to-wwwroot: expected static export at ${sourceDir}, did "next build" run?`);
  process.exit(1);
}

rmSync(targetDir, { recursive: true, force: true });
cpSync(sourceDir, targetDir, { recursive: true });

console.log(`copy-to-wwwroot: copied ${sourceDir} -> ${targetDir}`);
