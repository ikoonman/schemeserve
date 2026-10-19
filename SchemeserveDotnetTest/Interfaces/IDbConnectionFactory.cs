// -----------------------------------------------------------------------------
// File:     IDbConnectionFactory.cs
// Author:   Dawid Swanepoel
// Created:  2026-10-14
// Purpose:  Interface for opening database connections.
// -----------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Data;
using Microsoft.Data.SqlClient;

namespace SchemeserveDotnetTest.Interfaces
{
    // Hands Dapper an open connection. Behind an interface so it can be swapped in tests.
    public interface IDbConnectionFactory
    {
        IDbConnection Open();
    }
}