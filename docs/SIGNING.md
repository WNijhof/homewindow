# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

## What is signed

- `HomeWindow.exe` and `HomeWindow.dll`, the app itself
- `HomeWindow-Setup-<version>.exe`, the installer (and the same file as `HomeyBar-Setup-<version>.exe`, for the app under its old name)

Only files built from this repository are signed. The .NET runtime in the installer is signed by Microsoft. Other libraries keep the signature their makers gave them, or stay unsigned.

## How

The [release workflow](../.github/workflows/release.yml) builds everything on a GitHub-hosted runner from a version tag. It sends the app, and then the installer built from the signed app, to SignPath. The release certificate is used only after a maintainer approves that signing request in SignPath. A manual run of the workflow uses a test certificate and publishes no release.

The artifact configurations SignPath uses are in [.signpath/artifact-configurations](../.signpath/artifact-configurations). They sign only files whose product name is HomeWindow and whose version is the version being released.

## Team

| Role | Members |
|---|---|
| Committers and reviewers | [WNijhof](https://github.com/WNijhof) |
| Approvers | [WNijhof](https://github.com/WNijhof) |

Everyone in these roles uses multi-factor authentication for GitHub and SignPath.

## Privacy

HomeWindow will not transfer any information to other networked systems unless specifically requested by the user, apart from:

- **your own Homey**, which it controls: directly on your network, or through Athom's cloud relay (`<homey-id>.connect.athom.com`) when you are away;
- **GitHub**, which it asks whether there is a new version and from which it downloads that version. You can turn this off under Settings → Updates.

Settings stay on your PC, in `%APPDATA%\HomeWindow`.

## Setting it up (maintainer)

1. Apply at [signpath.org/apply](https://signpath.org/apply) for the open source program, with this repository.
2. In SignPath, once approved: create the project with slug `homewindow`, linked to this repository, and the signing policies `test-signing` and `release-signing` (SignPath Foundation usually sets these up).
3. Add two artifact configurations with the slugs `app` and `setup`, using the XML in `.signpath/artifact-configurations`.
4. Install the SignPath GitHub app on the repository, and create an API token for a CI user who may submit to both policies.
5. In GitHub, Settings → Secrets and variables → Actions:
   - secret `SIGNPATH_API_TOKEN`: the API token
   - variable `SIGNPATH_ORGANIZATION_ID`: the organization id from SignPath

   Signing starts with the next workflow run; without the variable the workflow builds unsigned, as before.
6. Try it with a manual run first (Actions → Release → Run workflow), which uses the test certificate.
