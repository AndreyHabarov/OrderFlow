// PreToolUse (Bash): run `dotnet test` before `git commit` when C#/project files are staged.
const { spawnSync } = require("node:child_process");
let input = "";
process.stdin.on("data", (d) => (input += d));
process.stdin.on("end", () => {
  let command = "";
  try {
    command = JSON.parse(input).tool_input?.command ?? "";
  } catch {
    process.exit(0);
  }
  if (!/\bgit\s+commit\b/.test(command)) process.exit(0);

  const cwd = process.env.CLAUDE_PROJECT_DIR ?? process.cwd();
  const staged = spawnSync("git", ["diff", "--cached", "--name-only"], { cwd, encoding: "utf8" }).stdout ?? "";
  if (!/\.(cs|csproj|props|sln)$/m.test(staged)) process.exit(0);

  const result = spawnSync("dotnet", ["test", "OrderFlow.sln", "--nologo", "-v", "q"], { cwd, encoding: "utf8" });
  if (result.status !== 0) {
    console.error("Blocked: `dotnet test` failed, fix the tests before committing.\n" + (result.stdout ?? "").slice(-2000));
    process.exit(2);
  }
  process.exit(0);
});
