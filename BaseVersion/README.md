# Base Version (pre-AI)

`Program.cs` in this folder is the dispatch center **before** the Agent Framework was added: a plain menu-driven console app (numbered menus, typed prompts, manual ID entry). It is kept for side-by-side comparison with the root `Program.cs`, where the same domain is driven by natural language through an `AIAgent` with function tools and human approval gates.

Key comparison points for the demo:

- **Same domain, different interaction layer.** `DispatchCenter.cs`, `Unit.cs`, `Incident.cs`, and `enums.cs` are shared unchanged. The AI version replaces only the menu loop.
- **Menus become tools.** Each numbered menu action (report / dispatch / arrive / resolve / status) maps one-to-one onto an `AIFunctionFactory`-created tool in the AI version.
- **Prompt sequences become one sentence.** Reporting an incident here takes five prompts and exact IDs; the AI version takes "there's a fire at Lincroft, send someone."
- **The human stays in charge either way.** Here every action is human-typed; in the AI version, side-effecting tools are wrapped in `ApprovalRequiredAIFunction` so a human still approves each one.

This folder is its own runnable project (`BaseVersion.csproj`) that links the shared domain files from the root — identical code, no copies. It is excluded from the root project's compile (`<Compile Remove="BaseVersion\**" />`) because two `Program` classes with `Main` cannot share one executable.

Run it from the repo root:

```
dotnet run --project BaseVersion
```

