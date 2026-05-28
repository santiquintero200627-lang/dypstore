using System;
using Npgsql;

var connString = ""Host=ep-nameless-tree-aql6xmrw.c-8.us-east-1.aws.neon.tech;Port=5432;Database=neondb;Username=neondb_owner;Password=npg_dGMEu7w1iqts;SslMode=Require"";
await using var conn = new NpgsqlConnection(connString);
await conn.OpenAsync();

await using var cmd = new NpgsqlCommand(""SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'"", conn);
await using var reader = await cmd.ExecuteReaderAsync();
while (await reader.ReadAsync())
{
    Console.WriteLine(reader.GetString(0));
}
