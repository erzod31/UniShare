using Microsoft.Data.Sqlite;
using UniShare.Infrastructure;

namespace UniShare.Infrastructure.Tests;

public sealed class LibraryProfileValidatorTests
{
    [Fact]
    public async Task EmptyFolderIsAccepted()
    {
        using var temporary = new TemporaryDirectory();
        await LibraryProfileValidator.ValidateExistingAsync(temporary.Path, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task InitializedProfileIsAccepted()
    {
        using var temporary = new TemporaryDirectory();
        var repository = new SqliteLibraryRepository(Path.Combine(temporary.Path, "library.db"));
        await repository.InitializeAsync(TestContext.Current.CancellationToken);

        await LibraryProfileValidator.ValidateExistingAsync(temporary.Path, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task UnrelatedSqliteDatabaseIsRejected()
    {
        using var temporary = new TemporaryDirectory();
        await using (var connection = new SqliteConnection($"Data Source={Path.Combine(temporary.Path, "library.db")}"))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE unrelated(value TEXT);";
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await Assert.ThrowsAsync<InvalidDataException>(
            () => LibraryProfileValidator.ValidateExistingAsync(temporary.Path, TestContext.Current.CancellationToken));
    }
}
