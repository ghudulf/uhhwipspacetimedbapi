# Architecture integration scenarios

Uses the existing SpacetimeDB 2.1.0 SDK and module code. No additional test packages are required.

1. Start an isolated local SpacetimeDB 2.1.0 on 127.0.0.1:3000.
2. Create a synthetic owner using its local `/v1/identity` endpoint. Store the response in a private file (mode 0600); never print its token.
3. Build `tests/DatabaseFixture` using the .NET 8 SDK and the pinned SpacetimeDB CLI. Publish **only** to `bru-architecture-integration` on that local server using the synthetic owner.
4. Set `BRU_TEST_OWNER_FILE` to the private file path and run `mise exec dotnet@10.0.100 -- dotnet run --project tests/Architecture.Integration`.

The harness fails if any assertion fails and emits only scenario names. It checks committed token mappings/properties, equal-length payload updates, concurrency, cold stores/reconnects, sale identity and idempotency, RLS, delegated identity forgery and queued cancellation. Use a fresh synthetic fixture for repeated full runs; tests consume fixture tickets. This harness does not test real Windows authentication or the desktop UI.
