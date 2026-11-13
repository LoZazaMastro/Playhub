# Windows build

Upstream: SteamDeckHomebrew/decky-loader v3.3.1, commit
ffd103b9c1a4ce5bf305e7c0998eda6e05457dad. Private version:
3.3.1+playhub.reconnect.1. Original GPLv2 license and credits are preserved.

Toolchain used: Windows x64, Python 3.14.8 (official PSF NuGet package),
Poetry 2.3.1, poetry-dynamic-versioning 1.10.1, PyInstaller 6.22.3,
Node.js 26.13.0 and pnpm 11.25.0. Backend dependencies come from poetry.lock;
frontend dependencies come from pnpm-lock.yaml. Dependency source archives and
their exact lock hashes are supplied separately under sources/dependencies.

Create an isolated Python virtual environment. Install Poetry 2.3.1 and
poetry-dynamic-versioning[plugin]==1.10.1 inside it. Set VIRTUAL_ENV to that
environment and POETRY_VIRTUALENVS_CREATE=false. In backend, set
POETRY_DYNAMIC_VERSIONING_BYPASS=3.3.1+playhub.reconnect.1 and run
`python -m poetry install --no-interaction`.

In frontend, run `pnpm install --frozen-lockfile` and `pnpm run build` with
Node 26.13.0. HUSKY=0 skips development Git hooks in a source archive.

In backend, set DECKY_REPO=SteamDeckHomebrew/decky-loader and
DECKY_BRANCH=v3.3.1. Build twice using the isolated Python environment:

```text
DECKY_NOCONSOLE=0 python -m PyInstaller --noconfirm --clean --distpath <build-root>/out/console --workpath <build-root>/build/console pyinstaller.spec
DECKY_NOCONSOLE=1 python -m PyInstaller --noconfirm --clean --distpath <build-root>/out/noconsole --workpath <build-root>/build/noconsole pyinstaller.spec
```

The environment assignments above use shell notation; in PowerShell assign
`$env:NAME='value'` before each command. Rename only the second output to
PluginLoader_noconsole.exe when constructing the distributable package.
The included playhub-version.txt sets the PE private-build provenance.

Run the seven source fault fixtures from this directory:
`python -B -X utf8 -m unittest discover -s tests -p test_reinjector_recovery.py`.
For frozen-bytecode checks, use the same source layout used for this build:
<build-root>/source/decky-loader, and the two out directories above. Set
DECKY_TEST_BUILD_ROOT=<build-root>, then run
`python -X utf8 tests/test_packaged_reconnect.py`. These tests use temporary
loopback ports; they do not launch the loader, contact Steam or use port 1337.

The private executables are unsigned. Python and Node build-tool signatures
were independently checked against Python Software Foundation and OpenJS
Foundation. No publisher signature is claimed for the generated binaries.
