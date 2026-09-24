# RimMind Core test contracts

Core uses three compact contract projects:

- `Tests/Contracts/`: domain, application, lifecycle, public API, and UI contracts.
- `IntegrationTests/Contracts/`: runtime adapter and mechanism contracts.
- `ArchTests/Contracts/`: dependency direction, runtime boundary, and cross-Mod contracts.

All Core test projects are one budget unit: fewer than 1000 discovered cases in
total, counting each parameterized data row. Prefer focused behavior and failure
boundary tests using production logic. Test doubles isolate external dependencies;
do not mirror production algorithms, assert private source shapes, or combine
unrelated scenarios merely to reduce the count.

`AsyncContextBuildContracts` exercises the production context engine, layer builder,
provider cache, history, diff state, and budget processing. The built-in mode
contract in `ExtensionRegistrationContracts` invokes production registration and
checks initial, periodic, and perception-driven thinking without placeholder triggers.

## Retired legacy tests

Files outside `Contracts/` are retained on disk but excluded from compilation.
Their behavior mapping is recorded in the root contract mapping document.
Deletion requires explicit owner approval for each exact file path; directories
are never deletion candidates.
