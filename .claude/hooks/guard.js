// PreToolUse (Edit|Write): block edits to secrets and to EF Core migrations.
let input = "";
process.stdin.on("data", (d) => (input += d));
process.stdin.on("end", () => {
  let path = "";
  try {
    path = (JSON.parse(input).tool_input?.file_path ?? "").replace(/\\/g, "/");
  } catch {
    process.exit(0);
  }
  const name = path.split("/").pop() ?? "";
  const isSecret = /^\.env(\..+)?$/.test(name) && name !== ".env.example";
  const isMigration = /\/Migrations\//.test(path);
  if (isSecret) {
    console.error(`Blocked: ${name} holds secrets and is edited by the owner only.`);
    process.exit(2);
  }
  if (isMigration) {
    console.error("Blocked: migrations are generated with `dotnet ef migrations add`, never edited by hand.");
    process.exit(2);
  }
  process.exit(0);
});
