// ApiIntegrationTests and AuthorizationTests both configure Program.cs via process-global
// environment variables (GraphStore__ConnectionString, Authentication__Enabled) read at
// WebApplication.CreateBuilder(args) time — see the comment in either file's InitializeAsync.
// xunit parallelizes across test classes by default, which would let these two classes race on
// the same env vars; disabling parallelization keeps each class's env var window exclusive.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
