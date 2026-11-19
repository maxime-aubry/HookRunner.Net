# Align layers with the validated architecture

The validated specification places execution and storage contracts in Application, so Infrastructure must reference Application to implement them. This supersedes [ADR 0002](0002-layer-dependencies.md): rename Core to `HookRunner.Domain`, with no internal references; Application references only Domain, Infrastructure references Domain and Application, and `HookRunner.Cli` references Application and Infrastructure.

Cli remains a class library composed by the consumer's executable, as decided in [ADR 0001](0001-cli-as-a-class-library.md). Architecture tests enforce the exhaustive direct project-reference graph and reject forbidden compiled assembly references; implicit transitive project references remain disabled.
