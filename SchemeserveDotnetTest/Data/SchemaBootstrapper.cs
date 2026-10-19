// -----------------------------------------------------------------------------
// File:     SchemaBootstrapper.cs
// Author:   Dawid Swanepoel
// Created:  2026-10-14
// Purpose:  Creates the database and tables at startup.
// -----------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;

namespace SchemeserveDotnetTest.Data
{
    // Creates the database and tables at startup, so nothing has to be set up by hand.
    // Each table is generated from the public properties of a row class.
    // Excluded from code coverage: needs a real SQL Server, so it is covered by integration tests, not unit tests.
    [ExcludeFromCodeCoverage]
    public static class SchemaBootstrapper
    {
        private static readonly Dictionary<Type, string> SqlTypes = new Dictionary<Type, string>
        {
            { typeof(string), "NVARCHAR(MAX)" },
            { typeof(int), "INT" },
            { typeof(long), "BIGINT" },
            { typeof(double), "FLOAT" },
            { typeof(decimal), "DECIMAL(18,4)" },
            { typeof(bool), "BIT" },
            { typeof(DateTime), "DATETIME2" },
        };

        // Creates the database named in the connection string if it doesn't exist.
        public static async Task EnsureDatabaseAsync(string connectionString)
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            var databaseName = builder.InitialCatalog;
            builder.InitialCatalog = "master";

            using (var connection = new SqlConnection(builder.ConnectionString))
            {
                await connection.ExecuteAsync(
                    "IF DB_ID(@databaseName) IS NULL CREATE DATABASE [" + databaseName + "]",
                    new { databaseName });
            }
        }

        // Creates dbo.{tableName} from T's properties if the table doesn't exist.
        // Only creates: a property added later needs the table dropped so it's recreated.
        public static async Task EnsureTableAsync<T>(string connectionString, string tableName, string keyColumn)
        {
            var columns = new List<string>();

            foreach (var property in typeof(T).GetProperties())
            {
                var nullableOf = Nullable.GetUnderlyingType(property.PropertyType);
                var clrType = nullableOf ?? property.PropertyType;

                if (!SqlTypes.TryGetValue(clrType, out var sqlType))
                    throw new NotSupportedException(typeof(T).Name + "." + property.Name + ": no SQL type for " + clrType.Name);

                if (property.Name == keyColumn)
                {
                    // NVARCHAR(MAX) can't be a primary key, so string keys are limited to 450 characters.
                    var keyType = clrType == typeof(string) ? "NVARCHAR(450)" : sqlType;
                    columns.Add("[" + property.Name + "] " + keyType + " NOT NULL PRIMARY KEY");
                }
                else
                {
                    // Strings and Nullable<T> may be NULL; plain value types (e.g. DateTime) may not.
                    var allowsNull = clrType == typeof(string) || nullableOf != null;
                    columns.Add("[" + property.Name + "] " + sqlType + (allowsNull ? " NULL" : " NOT NULL"));
                }
            }

            var sql = new StringBuilder();
            sql.AppendLine("IF OBJECT_ID(N'dbo." + tableName + "', N'U') IS NULL");
            sql.AppendLine("CREATE TABLE dbo.[" + tableName + "] (");
            sql.AppendLine("    " + string.Join(",\n    ", columns));
            sql.AppendLine(");");

            using (var connection = new SqlConnection(connectionString))
            {
                await connection.ExecuteAsync(sql.ToString());
            }
        }
    }
}
