# AGENTS.md — Global working instructions

## Main objective

Work efficiently, obey the user's request, and minimize token usage. Prioritize correct, maintainable, minimal changes over long explanations or unnecessary refactors.

## Communication style

* Reply in Spanish unless the user asks otherwise.
* Be concise and direct.
* Do not explain obvious things.
* Do not repeat the user's request unless needed for clarity.
* Avoid long summaries, long plans, and unnecessary theory.
* At the end, report only:

  1. what changed,
  2. files affected,
  3. tests/checks run,
  4. pending risks or next steps.

## Token-saving rules

* Read only the files needed for the task.
* Do not scan the whole repository unless necessary.
* Do not open large files completely if a targeted search is enough.
* Prefer `rg`, file search, and focused inspection before reading full files.
* Do not paste entire files in the response.
* Do not explain unchanged code.
* Do not generate documentation unless explicitly requested.
* Do not create large plans for small changes.
* Keep intermediate reasoning internal. Show only decisions, results, and blockers.

## Work behavior

* First understand the existing structure and conventions.
* Follow the current architecture, naming, formatting, and style of the project.
* Make the smallest safe change that solves the request.
* Do not refactor unrelated code.
* Do not rename files, classes, methods, routes, database fields, or UI elements unless the task requires it.
* Do not introduce new dependencies unless clearly necessary.
* If a dependency is needed, explain briefly why before adding it.
* Preserve existing behavior unless the user asked to change it.
* Avoid breaking compatibility with the current framework/version.

## When requirements are unclear

* Do not stop for minor ambiguity.
* Make a reasonable assumption and continue.
* Ask a question only if the task is blocked or if multiple choices would cause very different implementations.
* When assuming, mention the assumption briefly in the final response.

## Code quality

* Prefer simple, readable code over clever code.
* Keep methods/components focused.
* Avoid duplicated logic when a small reusable helper already fits.
* Validate inputs where relevant.
* Handle errors clearly without hiding failures.
* Do not leave dead code, unused imports, console logs, debug comments, or temporary files.
* Do not hardcode secrets, API keys, passwords, tokens, connection strings, or private credentials.

## UI and frontend rules

* Improve layout without changing business logic unless requested.
* Keep text labels and user-facing content unchanged unless the user asks to rewrite them.
* Prioritize clean spacing, alignment, readability, responsive behavior, and visual hierarchy.
* If the project uses Bootstrap, Telerik, Tailwind, plain CSS, or a component library, follow the existing approach instead of mixing styles randomly.
* Avoid redesigning entire screens when the request asks for a specific fix.

## Backend and database rules

* Respect existing layers, services, repositories, controllers, pages, and data access patterns.
* Do not change database schema unless explicitly requested.
* If schema changes are needed, provide migration/script details clearly.
* Avoid N+1 queries and unnecessary database calls.
* Keep validation both server-side and, when applicable, client-side.

## .NET / ASP.NET / Blazor preferences

* Follow the existing project type and framework version.
* Do not upgrade .NET, packages, Telerik, or project structure unless requested.
* In WebForms/MVC/Blazor projects, keep changes compatible with the current architecture.
* Avoid large rewrites from WebForms to MVC/Blazor or from Bootstrap to Tailwind unless explicitly requested.
* For Telerik components, prefer adjusting configuration, layout wrappers, CSS overrides, and existing component properties before replacing components.

## JavaScript / HTML / CSS preferences

* Use plain, readable JavaScript unless the project already uses a framework.
* Avoid unnecessary libraries.
* Keep CSS scoped or organized according to the current project structure.
* Do not break existing IDs/classes used by scripts or backend binding.

## Git behavior

* Do not commit, push, pull, merge, rebase, or create branches unless the user explicitly asks.
* Before making risky edits, inspect current changes.
* Do not overwrite user changes.
* If there are unrelated modified files, leave them untouched.

## Testing and verification

* Run the most relevant checks available for the changed area.
* Prefer targeted tests/checks over full expensive suites when the task is small.
* If tests cannot be run, say exactly why.
* Do not claim something works unless it was verified or logically checked.
* For UI-only changes, mention whether verification was visual, structural, or not run.

## Final response format

Use this format:

### Resultado

Short summary of the completed work.

### Archivos modificados

* `path/file`: brief change.

### Verificación

* Command/check run, or "No ejecutado" with reason.

### Pendiente

Only mention real risks, missing data, or recommended next step. If nothing important remains, write: "Nada crítico pendiente."