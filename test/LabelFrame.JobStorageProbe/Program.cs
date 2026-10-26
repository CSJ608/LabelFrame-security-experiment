using LabelFrame.Core.Data;
using LabelFrame.Core.Jobs;
using Microsoft.Data.Sqlite;

SqliteSupport.EnsureInitialized();
if (args[0] == "initialize")
{
    Pause("ready");
    try
    {
        await new SqliteLabelJobStore(args[1]).InitializeAsync();
        Console.WriteLine("initialized");
    }
    catch (SqliteException error) when (error.SqliteErrorCode == 5)
    {
        Console.Error.WriteLine(error);
        Environment.ExitCode = 5;
    }
}
else if (args[0] == "hold-read")
{
    using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
    {
        DataSource = args[1], Pooling = false, Mode = SqliteOpenMode.ReadWrite,
    }.ToString());
    connection.Open();
    using var transaction = connection.BeginTransaction(deferred: true);
    using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = "SELECT count(*) FROM jobs;";
    command.ExecuteScalar();
    Pause("ready");
}
else
{
    await JobStorageMigration.ConvertAsync(args[1], args[0] == "to-split", checkpoint: (stage, _) =>
    {
        if (stage == args[2]) Pause(stage);
    });
    Pause("committed");
}

static void Pause(string stage)
{
    Console.WriteLine(stage);
    Console.Out.Flush();
    Console.ReadLine();
}
