# Contributing to Sanctuary-RP

This is the **OSFR-RP roleplay server**, maintained separately from
[upstream Sanctuary](https://github.com/Open-Source-Free-Realms/Sanctuary).
Please open RP issues and pull requests in
[amuralle/Sanctuary-RP](https://github.com/amuralle/Sanctuary-RP).

## Scope and branches

RP commands, scene tools, the companion website, and RP instance scripts belong
here. General emulator fixes may also be suitable for upstream; keep reusable
fixes separate from RP-specific behavior and discuss cross-project changes first.
Do not assume an RP feature is intended for the main OSFR server.

Use `main` as the default pull request target. For changes specifically based on
`roleplay/tabletop-controls`, target that branch and state why; it can differ from
`main` in both RP work and upstream integration. Branch from the target you intend
to change, and avoid bringing unrelated upstream merges into a feature PR.

## Before submitting

- Include a short explanation, reproduction steps or a concrete command example,
  the branch and commit tested, and any user-visible behavior change.
- Use the SDK in `src/global.json`. Run `dotnet build` from `src` and the tests
  relevant to code changes. Database integration tests need a configured test
  database; see `.github/workflows/build.yml` for the CI MariaDB setup. State any
  checks you could not run.
- For RP commands and website changes, describe in-game or browser checks
  separately from automated tests. Update `README.md` or `ROLEPLAY_SERVER.md`
  when setup, permissions, persistence, or command behavior changes.
- Keep credentials, player data, databases, logs, and backups out of commits.

Contributions are what make the open source community such an amazing place to learn, inspire, and create. Any contributions you make are greatly appreciated.

If you have a suggestion that would make this better, please fork the repo and create a pull request. You can also simply open an issue with the tag "enhancement".

## Get Started

1) Fork the Project
2) Create your Feature Branch \
`git checkout -b feature/AmazingFeature`
3) Commit your Changes \
`git commit -m 'Add some AmazingFeature'`
4) Push to the Branch \
`git push origin feature/AmazingFeature`
5) Open a Pull Request to `main`, or the RP development branch selected above

## Coding Values

### Match source code conventions
Please follow the style of the surrounding code. This includes but is not limited to: code styling, naming conventions, and folder structure.

### Consider reusability
When adding a change, ask yourself: "Is there something else in this code base that I can already use or update?" Or, if you have to add a brand new implementation, ask yourself: "How can I structure my changes in a way future developers can reuse it?"

We want to avoid multiple sources of truth and redundancy in our code.

### Prefer smaller changes
Big pull requests with huge diffs are hard to read, review, and test. They can usually be broken down into smaller changes that can be independently tested. Keep in mind that large PRs will take longer to review, may not ever be reviewed, or may even be closed.

### Be concise
Avoid lengthy comments, branch names, or variable names. The code should be *mostly* self-explanatory.

## AI Policies
Using AI-assisted coding tools are allowed as long as human review is involved in the development process. AI should not replace human thinking and should not compromise our coding values. Plus, AI-generated pull requests are just difficult to review by fellow contributors.

Therefore, as *general* guidelines:
- LLMs can be used to analyze, learn, review, draft, etc. LLMs should not be used to create code.
- All pull requests identified as AI-generated/"vibe-coded" will be closed.
- Use AI responsibly.

## Need Help?
For project questions, open an issue in
[Sanctuary-RP](https://github.com/amuralle/Sanctuary-RP/issues). Include enough
context to distinguish an RP feature from shared emulator behavior.
