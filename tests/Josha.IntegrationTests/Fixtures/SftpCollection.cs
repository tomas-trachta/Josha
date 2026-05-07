using Xunit;

namespace Josha.IntegrationTests.Fixtures;

// Shared SFTP container across every test class that opts into [Collection("Sftp")].
// xUnit instantiates the fixture once for the whole collection and reuses it; tests
// inside the collection run sequentially, which is also what we want for the
// pool tests (they mutate static RemoteConnectionPool state).
[CollectionDefinition("Sftp")]
public sealed class SftpCollection : ICollectionFixture<SftpServerFixture> { }
