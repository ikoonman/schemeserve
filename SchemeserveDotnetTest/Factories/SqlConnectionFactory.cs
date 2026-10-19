// -----------------------------------------------------------------------------
// File:     SqlConnectionFactory.cs
// Author:   Dawid Swanepoel
// Created:  2026-10-14
// Purpose:  SQL Server connection factory.
// -----------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Data;
using Microsoft.Data.SqlClient;
using SchemeserveDotnetTest.Interfaces;

namespace SchemeserveDotnetTest.Factories
{
    
    // Excluded from code coverage: needs a real SQL Server, so it is covered by integration tests, not unit tests.
    [ExcludeFromCodeCoverage]
    public class SqlConnectionFactory : IDbConnectionFactory
    {
        private readonly string _connectionString;

        public SqlConnectionFactory(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("CqcCache")
                ?? throw new InvalidOperationException("Missing connection string 'CqcCache'.");
        }

        public IDbConnection Open()
        {
            var connection = new SqlConnection(_connectionString);
            connection.Open();
            return connection;
        }
    }
}
