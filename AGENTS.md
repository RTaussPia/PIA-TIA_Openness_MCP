# Repository development rules

## Development language

Use US English for all new code and all new file and directory names in this repository. This includes identifiers, comments, docstrings, diagnostic messages, test names, and names exposed by custom MCP tools. Use US spellings such as `behavior` and `initialize`.

Preserve existing upstream names and external API identifiers when referencing them.

## Reviewing Markdown, rules, and conventions

Before creating, editing, or deleting a Markdown file anywhere in this
repository, show its path and the exact proposed changes to the user.
Apply the changes only after the user explicitly approves that version.
If the proposed wording changes after approval, show the revised proposal
and obtain approval again before applying it.

Follow the same review process before adding or revising a repository rule
or project convention, regardless of which file contains it.

Before adding or revising a rule in any AGENTS.md file in this repository:

1. Show the file path and a diff of the proposed wording.
2. Suggest one concrete improvement to the wording and explain what it clarifies.
3. Wait for the user to approve the wording before editing the file.

## Answering TIA Portal questions

- Determine whether the user's question concerns TIA Portal, even when
  TIA Portal is not named explicitly. Use the conversation and the
  documented PIA terminology to recognize the context.
- For each TIA Portal question, check which parts the current MCP tools
  can answer, individually or in combination. If coverage is unclear,
  inspect the registered tools with `tools/list` or `FindTools`.
- Use the available tools for the supported parts. State what was
  verified and what remains open. For each open part, explain whether
  a prerequisite is missing, a tool call failed, or the current MCP
  tools lack the required capability.
- When a capability is missing, explain what a new or extended MCP tool
  would need to do and ask whether the user wants it developed. Wait for
  explicit approval before implementing or running it. This also applies
  to temporary tools, one-off scripts, and direct API calls that would
  bypass the limits of the existing MCP tools.

## 1. Core and compatibility

- Treat the upstream MCP implementation and bundled runtime as protected.
  Put custom features in `PIA-Custom/` and use existing MCP interfaces
  through wrappers or adapters where practical.
- Keep changes within the requested scope. Preserve existing behavior,
  public interfaces, tool names and descriptions, parameter schemas,
  response formats, endpoints, and configuration options.
- Before modifying the core or changing a public contract, explain why,
  identify the affected files and callers, and get explicit approval.

## 2. Design and dependencies

- Prefer cohesive modules, descriptive names, straightforward control
  flow, and explicit dependencies. Reuse existing patterns and business
  logic. Use composition where it fits, limit shared mutable state, and
  introduce abstractions only when they improve clarity or testability.
- Prefer standard libraries and existing packages. Before adding, removing,
  or upgrading a production dependency, explain the need and get approval.
  Evaluate new packages for license compatibility, maintenance, security,
  and transitive cost. Follow existing versioning conventions.

## 3. MCP tool design and token usage

- Give each tool a unique, descriptive name and an explicit, validated
  input schema. Keep tool descriptions concise. Document purpose, inputs,
  defaults, limits, response shape, and relevant errors. Use structured,
  predictable results and add examples where they clarify behavior.
- Fetch only the fields and records needed for the current step. Filter
  at the source when supported, return compact results, and load details
  on demand. Use bounded pages and expose how to continue. When a task
  requires complete results, process all relevant pages before reporting
  completion. Report incomplete coverage explicitly.

## 4. Security, errors, and logging

- Validate external input at system boundaries. Use least privilege and
  the existing secure configuration mechanism. Prevent injection, path
  traversal, unsafe deserialization, and unintended file or command access.
  Preserve authentication, authorization, and TLS verification. Get
  approval before changing security-sensitive behavior.
- Use existing error and logging conventions. Distinguish validation,
  operational, external, and internal failures. Return actionable errors
  and represent partial failures accurately. Retain useful diagnostic
  context internally. Keep secrets and sensitive data out of logs and
  responses, and keep stack traces and internal paths out of tool
  responses. Use debug level for verbose diagnostics.

## 5. Tests and Visual Studio

- Add automated tests with each new feature and regression tests for fixes
  where practical. Cover relevant success, failure, and edge cases. Test
  MCP registration and compatibility when those interfaces are affected.
  Keep assertions meaningful.
- With documented prerequisites installed, developers must be able to open
  the version-controlled C# solution or project in Visual Studio, build
  Debug, press F5, and hit a breakpoint in the current source.

## 6. Workflow and documentation

- Inspect the relevant instructions, code, and interfaces before editing.
  For substantial changes, present a short plan with affected areas,
  material risks, and how the result will be verified.
- Run the tests and checks relevant to the change. Run the full suite when
  changes affect shared behavior or public contracts and the required
  environment is available. Review the final diff. Report checks actually
  run, failures, and verification gaps.
- Update existing docs and capability files when behavior, configuration,
  or tool contracts change. Keep capability prompts and implementation in
  sync. Leave staging, commits, and pushes to the user.
