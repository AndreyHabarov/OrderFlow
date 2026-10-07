// PostToolUse (Edit|Write): format the changed C# file. Never fails the tool call.
const { spawnSync } = require("node:child_process");
let input = "";
process.stdin.on("data", (d) => (input += d));
process.stdin.on("end", () => {
  try {
    const file = (JSON.parse(input).tool_input?.file_path ?? "").replace(/\\/g, "/");
    if (!file.endsWith(".cs")) process.exit(0);
    spawnSync("dotnet", ["format", "OrderFlow.sln", "--include", file, "--no-restore"], {
      cwd: process.env.CLAUDE_PROJECT_DIR ?? process.cwd(),
      stdio: "ignore",
    });
  } catch {
    // formatting is best effort
  }
  process.exit(0);
});
