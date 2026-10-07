// PreToolUse (Edit|Write): block hand edits to EF Core migrations.
// .env files are intentionally editable: this is a personal learning project with local-only passwords.
let input = "";
process.stdin.on("data", (d) => (input += d));
process.stdin.on("end", () => {
  let path = "";
  try {
    path = (JSON.parse(input).tool_input?.file_path ?? "").replace(/\\/g, "/");
  } catch {
    process.exit(0);
  }
  if (/\/Migrations\//.test(path)) {
    console.error("Blocked: migrations are generated with `dotnet ef migrations add`, never edited by hand.");
    process.exit(2);
  }
  process.exit(0);
});
