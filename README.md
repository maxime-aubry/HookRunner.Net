# HookRunner.Net
Hook runner for .NET projects.

HookRunner targets .NET 11 and contains four class libraries:
Domain, Application, Infrastructure, and Cli. A consumer executable defines its
own hooks and uses HookRunner.Cli to receive Codex events and run one hook in
an isolated child process. See the [consumer guide](docs/consumer.md).

See [contribution instructions](CONTRIBUTING.md), [CI documentation](docs/ci.md),
and [architecture decisions](docs/adr/).

See [Architecture de HookRunner](docs/architecture.md) for the complete project
design, business rules, execution contracts, configuration, and validation
criteria. Domain vocabulary is defined in [GLOSSARY.md](GLOSSARY.md).

See [CONTRIBUTING.md](CONTRIBUTING.md) for branch naming and local hook setup.
