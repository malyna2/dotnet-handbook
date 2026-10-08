using System.Data;
using Microsoft.Data.SqlClient;

// Prove it: which predicates can SEEK an index and which must SCAN it. Needs a SQL Server:
// `docker compose up -d` in this folder. Prints each query's index operator and logical reads.
using var db = new SqlConnection("Server=localhost,14330;Database=tempdb;User Id=sa;Password=Emulator-Only-Passw0rd!;TrustServerCertificate=true");
db.Open();
new SqlCommand("""
    DROP TABLE IF EXISTS dbo.Customers;
    CREATE TABLE dbo.Customers (Id int IDENTITY PRIMARY KEY, Email varchar(100) NOT NULL, City varchar(50) NOT NULL, CreatedAt datetime2 NOT NULL);
    INSERT dbo.Customers (Email, City, CreatedAt) SELECT CONCAT('user', value, '@example.com'), CONCAT('City', value % 200), DATEADD(minute, -value, '2026-01-01') FROM GENERATE_SERIES(1, 200000);
    CREATE INDEX IX_Email ON dbo.Customers (Email); CREATE INDEX IX_City_CreatedAt ON dbo.Customers (City, CreatedAt);
    SET STATISTICS PROFILE ON; SET STATISTICS IO ON;
    """, db).ExecuteNonQuery();
string reads = "?";
db.InfoMessage += (_, e) => { if (e.Message.Contains("logical reads")) reads = e.Message.Split("logical reads ")[1].Split(',')[0]; };
Plan("Email = @p", SqlDbType.NVarChar, "user42@example.com");     // a C# string is sent as nvarchar
Plan("Email = @p", SqlDbType.VarChar, "user42@example.com");
Plan("LOWER(Email) = @p", SqlDbType.VarChar, "user42@example.com");
Plan("City = @p AND CreatedAt >= '2025-12-31'", SqlDbType.VarChar, "City7");
Plan("CreatedAt >= @p", SqlDbType.DateTime2, new DateTime(2025, 12, 31));
void Plan(string where, SqlDbType type, object value)
{
    using var command = new SqlCommand($"SELECT Id FROM dbo.Customers WHERE {where}", db);
    command.Parameters.Add(new SqlParameter("@p", type) { Value = value });
    var ops = new List<string>();
    using (var reader = command.ExecuteReader())
        do while (reader.Read()) if (reader.FieldCount > 2 && reader.GetString(2).Contains("Index")) ops.Add(reader.GetString(2).Trim()); while (reader.NextResult());
    Console.WriteLine($"WHERE {where} (@p {type}): {reads} logical reads\n    {string.Join(" ", ops).Replace("[tempdb].[dbo].[Customers].", "")}");
}
